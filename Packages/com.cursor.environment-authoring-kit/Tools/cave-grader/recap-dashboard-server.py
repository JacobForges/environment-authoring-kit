#!/usr/bin/env python3
"""Optional local API for the recap dashboard (read/write timeline, observe compose).

Personal Voice narration runs headless via run-recap-headless.sh (no Terminal window).
Terminal.app is opt-in only: RECAP_NARRATION_TERMINAL_FALLBACK=1.

  python3 recap-dashboard-server.py [--capture /path/to/DemoCapture/ts] [--port 8765]
  bash start-recap-dashboard.sh [capture_path]   # server + Vite in one command
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import mimetypes
import os
import signal
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, unquote, urlparse

_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

from envkit_paths import approved_dir, resolve_envkit_root  # noqa: E402


def _load_module(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / filename)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


narr = _load_module("demo_recap_narrator", "demo-recap-narrator.py")

DASHBOARD_DIR = _TOOLS / "recap-dashboard"
COMPOSE_COMPLETED_MARKER = "RecapComposeCompleted.marker"
COMPOSE_STARTED_MARKER = "RecapComposeStarted.marker"
NARRATION_APPROVAL_FILE = "NarrationVoiceApproval.json"
NARRATION_GUIDE_FILE = "NarrationVoiceGuide.md"
DEFAULT_PORT = 8765
COMPOSE_JOBS: dict[str, dict] = {}
COMPOSE_SPAWN_LOCKS: dict[str, threading.Lock] = {}
COMPOSE_SPAWN_LOCK_META = threading.Lock()
COMPOSE_GATES: dict[str, dict] = {}
COMPOSE_SERVER_LOG = Path.home() / "Library" / "EnvironmentKit" / "recap-dashboard-server.log"


def resolve_python3() -> str:
    for candidate in (
        "/opt/homebrew/bin/python3",
        "/usr/local/bin/python3",
        "/usr/bin/python3",
        sys.executable,
    ):
        if Path(candidate).is_file():
            return candidate
    return "python3"


def capture_status_artifacts(capture: Path) -> dict:
    """Lightweight artifact probe without full capture_status."""
    segs = 0
    for work_name in ("_presentation_compose", "_presentation_compose_preview120"):
        seg_dir = capture / work_name / "segments"
        if seg_dir.is_dir():
            segs = max(segs, len(list(seg_dir.glob("*.mp4"))))
    presentation = capture / "DemoRecapPresentation.mp4"
    return {
        "segmentCount": segs,
        "presentationMp4": presentation.is_file(),
        "presentationHasAudio": narr.mp4_has_audio_stream(presentation) if presentation.is_file() else False,
    }


def reconcile_compose_job(capture: Path) -> None:
    """Clear zombie 'running' jobs when the worker process died or hung."""
    key = str(capture)
    job = COMPOSE_JOBS.get(key)
    if not job or not job.get("running"):
        return

    pid = job.get("pid")
    started = float(job.get("started") or 0)
    elapsed = time.time() - started
    alive = False
    if pid:
        try:
            os.kill(int(pid), 0)
            alive = True
        except (OSError, ValueError, TypeError):
            alive = False

    seg_count = capture_status_artifacts(capture).get("segmentCount", 0)
    stuck = alive and elapsed > 120 and seg_count == 0 and not (capture / "_presentation_compose").is_dir()

    if stuck and pid:
        try:
            os.kill(int(pid), signal.SIGTERM)
        except (OSError, ValueError, TypeError):
            pass
        append_compose_live(capture, "Compose worker killed — hung on Cursor API (retry with OpenCV path).")
        alive = False

    if not alive:
        job = {
            **job,
            "running": False,
            "exitCode": job.get("exitCode") if job.get("exitCode") is not None else 1,
            "stderrTail": job.get("stderrTail")
            or "Compose worker stopped before finishing (click Retry compose).",
            "finished": time.time(),
        }
        COMPOSE_JOBS[key] = job
AGENT_STATE: dict[str, Any] = {"lastPing": 0.0, "agent": None, "note": None}
AGENT_CONNECTED_SEC = 90.0

# Whitelist only — no arbitrary path reads from approved_dir.
APPROVED_IMAGE_WHITELIST = frozenset(
    {
        "ApprovedIntro.png",
        "ApprovedOutro.png",
        "_approval_intro_c.png",
        "_approval_outro_b.png",
        "_approval_portrait.png",
        "DemoRecapPortrait.png",
        "DemoRecapSignature.png",
    }
)


def latest_capture() -> Path | None:
    root = resolve_envkit_root() / "DemoCapture"
    if not root.is_dir():
        return None
    runs = sorted(root.iterdir(), key=lambda p: p.stat().st_mtime if p.is_dir() else 0, reverse=True)
    for run in runs:
        if run.is_dir() and (run / "timelapse").is_dir():
            return run
    return None


def resolve_capture(path: str | None) -> Path | None:
    if not path:
        return latest_capture()
    p = Path(path).expanduser().resolve()
    return p if p.is_dir() else None


def gate_key(capture: Path) -> str:
    return str(capture.resolve())


def get_gate(capture: Path) -> dict:
    key = gate_key(capture)
    gate = COMPOSE_GATES.get(key)
    if not gate:
        return {
            "waiting": False,
            "proceed": False,
            "assetsVerified": False,
            "capture": key,
        }
    return {
        "waiting": bool(gate.get("waiting")),
        "proceed": bool(gate.get("proceed")),
        "assetsVerified": bool(gate.get("assetsVerified")),
        "capture": key,
        "started": gate.get("started"),
        "proceededAt": gate.get("proceededAt"),
        "verifiedAt": gate.get("verifiedAt"),
    }


def set_gate_waiting(capture: Path) -> dict:
    key = gate_key(capture)
    COMPOSE_GATES[key] = {
        "waiting": True,
        "proceed": False,
        "assetsVerified": False,
        "started": time.time(),
        "proceededAt": None,
        "verifiedAt": None,
    }
    return get_gate(capture)


def set_gate_assets_verified(capture: Path) -> dict:
    key = gate_key(capture)
    gate = COMPOSE_GATES.get(key, {})
    gate["assetsVerified"] = True
    gate["verifiedAt"] = time.time()
    COMPOSE_GATES[key] = gate
    return get_gate(capture)


def _compose_spawn_lock(key: str) -> threading.Lock:
    with COMPOSE_SPAWN_LOCK_META:
        if key not in COMPOSE_SPAWN_LOCKS:
            COMPOSE_SPAWN_LOCKS[key] = threading.Lock()
        return COMPOSE_SPAWN_LOCKS[key]


def mark_compose_started(capture: Path) -> None:
    try:
        compose_started_marker(capture).write_text(
            time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), encoding="utf-8"
        )
    except OSError:
        pass


def compose_started_marker(capture: Path) -> Path:
    return capture / COMPOSE_STARTED_MARKER


def apply_approved_cards_to_capture(capture: Path) -> tuple[bool, str]:
    """Copy approved intro/outro/portrait PNGs from DemoRecapApproved into the capture run."""
    script = _TOOLS / "apply-approved-cards.py"
    if not script.is_file():
        return True, "apply-approved-cards.py missing — skipped"
    try:
        proc = subprocess.run(
            [sys.executable, str(script), str(capture)],
            cwd=str(_TOOLS),
            capture_output=True,
            text=True,
            timeout=120,
        )
        tail = (proc.stdout or proc.stderr or "").strip()[-500:]
        if proc.returncode != 0:
            return False, tail or f"apply-approved-cards exited {proc.returncode}"
        return True, tail.splitlines()[-1] if tail else "cards applied"
    except Exception as exc:
        return False, str(exc)


def merge_approved_settings_into_timeline(capture: Path) -> None:
    cards = read_approved_cards()
    if not cards or not (capture / "DemoRecapTimeline.json").is_file():
        return
    try:
        spec = read_timeline(capture)
        spec.update(cards)
        write_timeline(capture, spec)
    except (ValueError, FileNotFoundError, json.JSONDecodeError):
        pass


def set_gate_proceed(capture: Path) -> dict:
    key = gate_key(capture)
    gate = COMPOSE_GATES.get(key, {})
    if not gate.get("assetsVerified"):
        raise ValueError(
            "Approved intro/outro/portrait must be verified in the recap dashboard "
            'before proceeding to compose. Review the cards and click '
            '"I verified these cards are correct".'
        )
    gate.update({"waiting": False, "proceed": True, "proceededAt": time.time()})
    COMPOSE_GATES[key] = gate
    return get_gate(capture)


def capture_has_approved_cards(capture: Path) -> bool:
    intro = capture / "ApprovedIntro.png"
    outro = capture / "ApprovedOutro.png"
    if intro.is_file() and outro.is_file():
        return True
    ad = approved_dir()
    return (ad / "_approval_intro_c.png").is_file() and (ad / "_approval_outro_b.png").is_file()


def ensure_gate_ready_for_compose(capture: Path) -> dict:
    """Allow retry compose when cards exist on disk even if in-memory gate was lost (server restart)."""
    gate = get_gate(capture)
    if gate.get("assetsVerified"):
        return gate
    if capture_has_approved_cards(capture):
        return set_gate_assets_verified(capture)
    return gate


def proceed_and_start_compose(capture: Path) -> dict:
    """Verify gate, copy approved PNGs, merge settings, spawn full producer recap + narration."""
    gate = set_gate_proceed(capture)
    merge_approved_settings_into_timeline(capture)
    cards_ok, cards_note = apply_approved_cards_to_capture(capture)
    if not cards_ok:
        return {
            "ok": False,
            "error": f"Could not copy approved cards into capture: {cards_note}",
            "composeGate": gate,
        }

    result = spawn_compose(
        capture,
        preview=False,
        no_narrator=False,
        narration_only=False,
        require_gate=True,
    )
    if not result.get("ok"):
        err = str(result.get("error") or "")
        if "already running" in err.lower():
            return {
                "ok": True,
                "composeGate": gate,
                "composeJob": COMPOSE_JOBS.get(str(capture)),
                "message": "Compose already running for this capture.",
            }
        return {"ok": False, "composeGate": gate, **result}

    return {
        "ok": True,
        "composeGate": gate,
        "composeJob": COMPOSE_JOBS.get(str(capture)),
        "cardsNote": cards_note,
        "message": "Compose started — silent video first, then headless Personal Voice after Step 4 approval.",
    }


def read_approved_cards() -> dict:
    p = approved_dir() / "ApprovedCards.json"
    if not p.is_file():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {}


def write_approved_cards(patch: dict) -> dict:
    root = approved_dir()
    root.mkdir(parents=True, exist_ok=True)
    p = root / "ApprovedCards.json"
    spec = read_approved_cards()
    spec.update(patch)
    p.write_text(json.dumps(spec, indent=2) + "\n", encoding="utf-8")
    return spec


def approved_settings_payload() -> dict:
    cards = read_approved_cards()
    return {
        "approvedDir": str(approved_dir()),
        "settings": {
            "cursorFullNarration": bool(cards.get("cursorFullNarration", True)),
            "fullNarrationFromCaptions": bool(cards.get("fullNarrationFromCaptions", True)),
            "narrationMode": str(cards.get("narrationMode", "fullScript")),
            "narratorEngine": str(cards.get("narratorEngine", "personal")),
            "narratorRequirePersonal": bool(cards.get("narratorRequirePersonal", True)),
            "cursorVision": bool(cards.get("cursorVision", False)),
            "videoEnhance": bool(cards.get("videoEnhance", True)),
            "requireNarrationBeforeFinalize": bool(cards.get("requireNarrationBeforeFinalize", True)),
            "narrationEmphasisCapture": bool(cards.get("narrationEmphasisCapture", False)),
            "showAnnotations": bool(cards.get("showAnnotations", True)),
            "annotationSource": str(cards.get("annotationSource", "opencv")),
        },
    }


def approved_assets_payload() -> dict:
    ad = approved_dir()
    assets: dict[str, dict[str, Any]] = {}
    for name in sorted(APPROVED_IMAGE_WHITELIST):
        p = ad / name
        exists = p.is_file()
        assets[name] = {
            "path": str(p),
            "exists": exists,
            "url": f"/api/approved/image?name={name}" if exists else None,
        }
    return {
        "approvedDir": str(ad),
        "assets": assets,
        "canonicalOutroExists": (ad / "_approval_outro_b.png").is_file(),
        "canonicalIntroExists": (ad / "_approval_intro_c.png").is_file(),
    }


def agent_status() -> dict:
    last = float(AGENT_STATE.get("lastPing") or 0.0)
    ago = time.time() - last if last else None
    connected = ago is not None and ago < AGENT_CONNECTED_SEC
    return {
        "connected": connected,
        "lastPing": last or None,
        "secondsAgo": round(ago, 1) if ago is not None else None,
        "agent": AGENT_STATE.get("agent"),
        "note": AGENT_STATE.get("note"),
    }


def compose_live_log_path(capture: Path) -> Path:
    return capture / "RecapComposeLive.log"


def append_compose_live(capture: Path, message: str) -> None:
    try:
        line = f"{time.strftime('%H:%M:%S')} {message}\n"
        with compose_live_log_path(capture).open("a", encoding="utf-8") as fh:
            fh.write(line)
    except OSError:
        pass


def compose_live_feed(capture: Path | None) -> dict[str, Any]:
    """Live feed for the recap video editor — not Unity world build."""
    lines: list[str] = [
        "Recap video editor (PNG timelapse → presentation + Personal Voice)",
        "This pipeline does not run inside Unity — only uses capture PNGs.",
        "",
    ]
    if capture is None or not capture.is_dir():
        lines.append("No capture folder selected.")
        return {"markdown": "\n".join(lines), "lines": lines}

    st = capture_status(capture)
    job = st.get("composeJob") or {}
    gate = st.get("composeGate") or {}

    lines.append(f"Capture: {capture}")
    lines.append(f"Timelapse PNGs: {st.get('timelapseFrames', 0)}")
    lines.append(f"Timeline milestones: {st.get('milestones', 0)}")
    lines.append(
        f"Gate: proceed={gate.get('proceed')} verified={gate.get('assetsVerified')}"
    )
    lines.append("")

    if job.get("running"):
        lines.append("▶ COMPOSE RUNNING")
        cmd = job.get("cmd") or []
        lines.append(f"  cmd: {' '.join(str(c) for c in cmd)}")
    elif job.get("exitCode") is not None:
        lines.append(f"Last compose exit code: {job.get('exitCode')}")
        if job.get("terminalNarration"):
            lines.append("  → Personal Voice narration (headless)")
    elif not gate.get("proceed"):
        lines.append("Idle — verify cards and click Proceed to compose.")
    else:
        lines.append("Proceed sent — waiting for compose worker…")

    lines.append("")
    lines.append("Artifacts:")
    lines.append(f"  silent video: {'yes' if st.get('silentVideo') else 'no'}")
    lines.append(f"  narration script: {'yes' if st.get('narrationScript') else 'no'}")
    lines.append(f"  presentation mp4: {'yes' if st.get('presentationMp4') else 'no'}")
    lines.append(f"  segments encoded: {st.get('segmentCount', 0)}")

    for work_name in ("_presentation_compose", "_presentation_compose_preview120"):
        work = capture / work_name
        if work.is_dir():
            seg_dir = work / "segments"
            n = len(list(seg_dir.glob("*.mp4"))) if seg_dir.is_dir() else 0
            final = work / "_final_video.mp4"
            lines.append(f"  {work_name}: {n} segment(s)" + (" + final shell" if final.is_file() else ""))

    live_log = compose_live_log_path(capture)
    if live_log.is_file():
        try:
            tail = live_log.read_text(encoding="utf-8", errors="replace").splitlines()[-30:]
            if tail:
                lines.append("")
                lines.append("--- compose log ---")
                lines.extend(tail)
        except OSError:
            pass

    for label, key in (("stdout", "stdoutTail"), ("stderr", "stderrTail")):
        chunk = (job.get(key) or "").strip()
        if chunk:
            lines.append("")
            lines.append(f"--- job {label} ---")
            lines.extend(chunk.splitlines()[-12:])

    if COMPOSE_SERVER_LOG.is_file():
        try:
            raw = COMPOSE_SERVER_LOG.read_text(encoding="utf-8", errors="replace").splitlines()
            hits = [
                ln
                for ln in raw[-60:]
                if any(
                    k in ln.lower()
                    for k in ("compose", "recap", "producer", "narrat", "capture/compose")
                )
            ]
            if hits:
                lines.append("")
                lines.append("--- api server ---")
                lines.extend(hits[-10:])
        except OSError:
            pass

    text = "\n".join(lines)
    return {"markdown": text, "lines": lines, "capture": str(capture)}


def live_status_text() -> str:
    """Deprecated — recap dashboard uses compose_live_feed, not Unity build status."""
    cap = latest_capture()
    return compose_live_feed(cap)["markdown"]


def read_timeline(capture: Path) -> dict:
    tl = capture / "DemoRecapTimeline.json"
    if not tl.is_file():
        raise FileNotFoundError("DemoRecapTimeline.json missing")
    return json.loads(tl.read_text(encoding="utf-8"))


def validate_playback_factor(spec: dict) -> None:
    if float(spec.get("videoPlaybackFactor", 1.0) or 1.0) != 1.0:
        raise ValueError(
            "videoPlaybackFactor must stay 1.0 for Personal Voice sync "
            "(use --stretch-to-target only when intentional)"
        )


def write_timeline(capture: Path, spec: dict) -> Path:
    validate_playback_factor(spec)
    tl = capture / "DemoRecapTimeline.json"
    tl.write_text(json.dumps(spec, indent=2) + "\n", encoding="utf-8")
    return tl


def merge_timeline_patch(capture: Path, patch: dict) -> dict:
    spec = read_timeline(capture)
    spec.update(patch)
    validate_playback_factor(spec)
    write_timeline(capture, spec)
    return spec


def load_capture_spec(capture: Path) -> dict:
    tl = capture / "DemoRecapTimeline.json"
    if not tl.is_file():
        return {}
    try:
        spec = json.loads(tl.read_text(encoding="utf-8"))
        return narr.flatten_narrator_personal_settings(spec)
    except json.JSONDecodeError:
        return {}


def narration_terminal_fallback_enabled() -> bool:
    return os.environ.get("RECAP_NARRATION_TERMINAL_FALLBACK", "").strip() in (
        "1",
        "true",
        "yes",
    )


def run_headless_narration(
    capture: Path,
    *,
    preview: bool = False,
    narration_only: bool = True,
    no_narrator: bool = False,
    no_cursor: bool = False,
) -> tuple[int, str]:
    """Run producer recap headless — blocks until Personal Voice + mux finish or fail."""
    headless = _TOOLS / "run-recap-headless.sh"
    cmd = ["bash", str(headless), str(capture)]
    if preview:
        cmd.append("--preview")
    if narration_only:
        cmd.append("--narration-only")
    elif no_narrator:
        cmd.append("--no-narrator")
    if no_cursor:
        cmd.append("--no-cursor")
    append_compose_live(capture, f"$ {' '.join(cmd)}")
    env = os.environ.copy()
    env.setdefault("ENVIRONMENT_KIT_DATA_ROOT", str(resolve_envkit_root()))
    env["PYTHONUNBUFFERED"] = "1"
    env.setdefault("TERM_PROGRAM", "Apple_Terminal")
    proc = subprocess.run(
        cmd,
        cwd=str(_TOOLS),
        env=env,
        capture_output=True,
        text=True,
        timeout=7200,
    )
    tail = ((proc.stdout or "") + (proc.stderr or "")).strip()
    for line in tail.splitlines():
        if line.strip():
            append_compose_live(capture, line)
    return proc.returncode or 0, tail[-4000:]


def capture_status(capture: Path) -> dict:
    reconcile_compose_job(capture)
    tl = capture / "DemoRecapTimeline.json"
    spec = {}
    if tl.is_file():
        try:
            spec = json.loads(tl.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            spec = {}

    def exists(name: str) -> bool:
        return (capture / name).is_file()

    def work_dir(name: str) -> Path:
        raw = spec.get("composeWorkDir") or capture / name
        return Path(str(raw)).expanduser()

    preview_work = work_dir("_presentation_compose_preview120")
    full_work = work_dir("_presentation_compose") if spec.get("composeWorkDir") else capture / "_presentation_compose"

    silent = preview_work / "_final_video.mp4"
    if not silent.is_file():
        silent = full_work / "_final_video.mp4"

    narration_json = capture / "DemoRecapFullNarration.json"
    presentation = capture / "DemoRecapPresentation.mp4"
    preview = Path.home() / "Desktop" / "DemoRecap-Card-Preview" / "DirectorPreview.mp4"

    segments = list((preview_work / "segments").glob("*.mp4")) if (preview_work / "segments").is_dir() else []
    if not segments and (full_work / "segments").is_dir():
        segments = list((full_work / "segments").glob("*.mp4"))

    job = COMPOSE_JOBS.get(str(capture))
    return {
        "capture": str(capture),
        "timelapseFrames": len(list((capture / "timelapse").glob("tl_*.png"))),
        "timeline": tl.is_file(),
        "milestones": len(spec.get("milestones") or []),
        "silentVideo": silent.is_file(),
        "silentVideoPath": str(silent) if silent.is_file() else None,
        "narrationScript": narration_json.is_file(),
        "narrationWav": (preview_work / "narration.wav").is_file() or (full_work / "narration.wav").is_file(),
        "presentationMp4": presentation.is_file(),
        "presentationHasAudio": narr.mp4_has_audio_stream(presentation) if presentation.is_file() else False,
        "personalVoicePending": presentation.is_file()
        and not narr.mp4_has_audio_stream(presentation)
        and silent.is_file(),
        "previewMp4": preview.is_file(),
        "segmentCount": len(segments),
        "composeJob": job,
        "composeGate": get_gate(capture),
        "agent": agent_status(),
        "pacing": {
            "milestoneHoldSec": spec.get("milestoneHoldSec"),
            "subbeatHoldSec": spec.get("subbeatHoldSec"),
            "introSec": spec.get("introSec"),
            "outroSec": spec.get("outroSec"),
            "videoPlaybackFactor": spec.get("videoPlaybackFactor"),
        },
        "narrator": {
            "engine": spec.get("narratorEngine"),
            "requirePersonal": spec.get("narratorRequirePersonal"),
            "sayRate": spec.get("narratorPersonalSayRate") or spec.get("narratorRate"),
        },
        "narrationReview": narration_review_summary(capture),
    }


def read_narration_approval(capture: Path) -> dict:
    path = capture / NARRATION_APPROVAL_FILE
    if not path.is_file():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {"status": "error", "error": "invalid NarrationVoiceApproval.json"}


def write_narration_approval(capture: Path, patch: dict) -> dict:
    path = capture / NARRATION_APPROVAL_FILE
    data = read_narration_approval(capture)
    data.update(patch)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return data


def narration_review_summary(capture: Path) -> dict:
    """Compact narration review state for status polling."""
    approval = read_narration_approval(capture)
    guide = capture / NARRATION_GUIDE_FILE
    script_txt = capture / "DemoRecapFullNarration.txt"
    script_json = capture / "DemoRecapFullNarration.json"
    silent = (capture / "_presentation_compose/_final_video.mp4").is_file()
    presentation = capture / "DemoRecapPresentation.mp4"
    has_audio = narr.mp4_has_audio_stream(presentation) if presentation.is_file() else False
    ready = guide.is_file() or script_txt.is_file() or script_json.is_file()
    if not approval and ready:
        word_count = None
        target_words = None
        if script_json.is_file():
            try:
                doc = json.loads(script_json.read_text(encoding="utf-8"))
                word_count = doc.get("wordCount") or len(str(doc.get("script") or "").split())
                target_words = doc.get("targetWordCount")
            except json.JSONDecodeError:
                pass
        approval = {
            "status": "pending",
            "approved": False,
            "generating": False,
            "wordCount": word_count,
            "targetWordCount": target_words,
        }
    generating = bool(approval.get("generating"))
    approved = bool(approval.get("approved"))
    needs_review = silent and not has_audio and ready and not approved
    return {
        "ready": ready,
        "generating": generating,
        "approved": approved,
        "status": approval.get("status") or ("pending" if ready else "missing"),
        "wordCount": approval.get("wordCount"),
        "targetWordCount": approval.get("targetWordCount"),
        "targetDurationSec": approval.get("targetDurationSec"),
        "source": approval.get("source"),
        "error": approval.get("error"),
        "needsReview": needs_review,
        "guideModified": guide.stat().st_mtime if guide.is_file() else None,
    }


def narration_review_payload(capture: Path) -> dict:
    """Full review payload for dashboard (guide markdown + spoken script)."""
    summary = narration_review_summary(capture)
    guide_path = capture / NARRATION_GUIDE_FILE
    script_path = capture / "DemoRecapFullNarration.txt"
    guide_markdown = ""
    script_text = ""
    if guide_path.is_file():
        guide_markdown = guide_path.read_text(encoding="utf-8")
    if script_path.is_file():
        script_text = script_path.read_text(encoding="utf-8")
    elif (capture / "DemoRecapFullNarration.json").is_file():
        try:
            data = json.loads((capture / "DemoRecapFullNarration.json").read_text(encoding="utf-8"))
            script_text = str(data.get("script") or "")
        except json.JSONDecodeError:
            pass
    return {
        **summary,
        "capture": str(capture),
        "guideMarkdown": guide_markdown,
        "scriptText": script_text,
        "guidePath": str(guide_path) if guide_path.is_file() else None,
        "scriptPath": str(script_path) if script_path.is_file() else None,
    }


def run_generate_narration_review(capture: Path, *, regen: bool = False, use_cursor: bool = False) -> dict:
    """Headless script + NarrationVoiceGuide generation for dashboard review."""
    script = _TOOLS / "generate-narration-review.py"
    py = resolve_python3()
    cmd = [py, str(script), str(capture)]
    if regen:
        cmd.append("--regen")
    if use_cursor:
        cmd.append("--cursor")
    write_narration_approval(
        capture,
        {"status": "generating", "generating": True, "approved": False},
    )
    append_compose_live(capture, f"$ {' '.join(cmd)}")
    proc = subprocess.run(
        cmd,
        cwd=str(_TOOLS),
        capture_output=True,
        text=True,
        timeout=600,
    )
    tail = ((proc.stdout or "") + (proc.stderr or "")).strip()[-2000:]
    if proc.returncode != 0:
        write_narration_approval(
            capture,
            {
                "status": "error",
                "generating": False,
                "approved": False,
                "error": tail or f"exit {proc.returncode}",
            },
        )
        append_compose_live(capture, f"Narration script generation failed (exit {proc.returncode})")
        return {"ok": False, "error": tail or f"exit {proc.returncode}"}
    append_compose_live(capture, "Narration script ready — review in dashboard (Step 4)")
    return {"ok": True, "review": narration_review_payload(capture)}


def approve_narration_review(capture: Path) -> dict:
    write_narration_approval(
        capture,
        {
            "status": "approved",
            "approved": True,
            "approvedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "generating": False,
        },
    )
    append_compose_live(capture, "Narration script approved — starting headless Personal Voice")
    return {"ok": True, "review": narration_review_summary(capture)}


def narration_approved(capture: Path) -> bool:
    return bool(read_narration_approval(capture).get("approved"))


def capture_artifacts(capture: Path) -> dict:
    st = capture_status(capture)
    paths: dict[str, str | None] = {
        "timeline": str(capture / "DemoRecapTimeline.json") if (capture / "DemoRecapTimeline.json").is_file() else None,
        "narration": str(capture / "DemoRecapFullNarration.json")
        if (capture / "DemoRecapFullNarration.json").is_file()
        else None,
        "opencv": str(capture / "DemoRecapOpenCV.json") if (capture / "DemoRecapOpenCV.json").is_file() else None,
        "director": str(capture / "DemoRecapCursorDirector.json")
        if (capture / "DemoRecapCursorDirector.json").is_file()
        else None,
        "presentation": str(capture / "DemoRecapPresentation.mp4")
        if (capture / "DemoRecapPresentation.mp4").is_file()
        else None,
        "silentVideo": st.get("silentVideoPath"),
        "previewDesktop": str(Path.home() / "Desktop" / "DemoRecap-Card-Preview" / "DirectorPreview.mp4")
        if st.get("previewMp4")
        else None,
    }

    durations: dict[str, float | None] = {}
    for label, p in paths.items():
        if p and p.endswith(".mp4"):
            durations[label] = ffprobe_duration(Path(p))

    opencv_summary = None
    opencv_path = capture / "DemoRecapOpenCV.json"
    if opencv_path.is_file():
        try:
            opencv_data = json.loads(opencv_path.read_text(encoding="utf-8"))
            opencv_summary = {
                "milestoneCount": len(opencv_data.get("milestones") or []),
                "regionCount": sum(
                    len(m.get("regions") or []) for m in (opencv_data.get("milestones") or [])
                ),
            }
        except json.JSONDecodeError:
            opencv_summary = {"error": "invalid JSON"}

    return {
        "capture": str(capture),
        "paths": paths,
        "durationsSec": durations,
        "opencv": opencv_summary,
        "milestones": st.get("milestones"),
        "composeGate": st.get("composeGate"),
        "composeJob": st.get("composeJob"),
    }


def ffprobe_duration(path: Path) -> float | None:
    for ffprobe in ("ffprobe", "/opt/homebrew/bin/ffprobe", "/usr/local/bin/ffprobe"):
        try:
            out = subprocess.run(
                [
                    ffprobe,
                    "-v",
                    "error",
                    "-show_entries",
                    "format=duration",
                    "-of",
                    "default=noprint_wrappers=1:nokey=1",
                    str(path),
                ],
                capture_output=True,
                text=True,
                check=True,
                timeout=30,
            )
            return float(out.stdout.strip())
        except (FileNotFoundError, subprocess.CalledProcessError, ValueError, subprocess.TimeoutExpired):
            continue
    return None


def compose_completed_marker(capture: Path) -> Path:
    return capture / COMPOSE_COMPLETED_MARKER


def is_compose_completed(capture: Path) -> bool:
    return compose_completed_marker(capture).is_file()


def mark_compose_completed(capture: Path) -> None:
    try:
        compose_completed_marker(capture).write_text(time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), encoding="utf-8")
    except OSError:
        pass


def spawn_compose(
    capture: Path,
    *,
    preview: bool,
    no_narrator: bool,
    narration_only: bool,
    require_gate: bool = True,
    skip_approval_check: bool = False,
) -> dict:
    key = str(capture)
    gate = get_gate(capture)
    if require_gate and not gate.get("proceed"):
        return {
            "ok": False,
            "error": "Compose blocked — verify approved cards and click Proceed in the recap dashboard.",
            "composeGate": gate,
        }
    if narration_only:
        presentation = capture / "DemoRecapPresentation.mp4"
        work = capture / "_presentation_compose" / "_final_video.mp4"
        if not work.is_file() and not presentation.is_file():
            return {
                "ok": False,
                "error": "No silent video yet — run Proceed (silent shell) before Personal Voice narration.",
            }
        if not skip_approval_check and not narration_approved(capture):
            review = narration_review_summary(capture)
            if review.get("ready") and not review.get("approved"):
                print(
                    "Narration: script not approved in dashboard — attempting Personal Voice anyway",
                    flush=True,
                )

    if not preview and is_compose_completed(capture):
        presentation = capture / "DemoRecapPresentation.mp4"
        if narration_only:
            if presentation.is_file() and not narr.mp4_has_audio_stream(presentation):
                pass
            else:
                return {
                    "ok": False,
                    "error": "Compose already completed for this capture",
                }
        else:
            return {
                "ok": False,
                "error": "Compose already completed for this capture",
            }

    producer = _TOOLS / "run-producer-recap.py"
    headless = _TOOLS / "run-recap-headless.sh"
    py = resolve_python3()
    full_presentation = not preview and not narration_only and not no_narrator

    def producer_cmd(
        *,
        preview_flag: bool,
        narration_only_flag: bool,
        no_narrator_flag: bool,
        no_cursor: bool = False,
    ) -> list[str]:
        cmd = ["bash", str(headless), str(capture)]
        if preview_flag:
            cmd.append("--preview")
        if narration_only_flag:
            cmd.append("--narration-only")
        elif no_narrator_flag:
            cmd.append("--no-narrator")
        if no_cursor:
            cmd.append("--no-cursor")
        return cmd

    cmd = producer_cmd(
        preview_flag=preview,
        narration_only_flag=narration_only,
        no_narrator_flag=no_narrator,
    )

    with _compose_spawn_lock(key):
        reconcile_compose_job(capture)
        if COMPOSE_JOBS.get(key, {}).get("running"):
            return {"ok": False, "error": "compose already running for this capture"}
        COMPOSE_JOBS[key] = {
            "running": True,
            "started": time.time(),
            "cmd": cmd,
            "pid": None,
        }
        mark_compose_started(capture)

    env = os.environ.copy()
    env.setdefault("ENVIRONMENT_KIT_DATA_ROOT", str(resolve_envkit_root()))
    env["PYTHONUNBUFFERED"] = "1"

    def run_producer_streaming(run_cmd: list[str]) -> tuple[int, str]:
        append_compose_live(capture, f"$ {' '.join(run_cmd)}")
        proc = subprocess.Popen(
            run_cmd,
            cwd=str(_TOOLS),
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
        )
        COMPOSE_JOBS[key]["pid"] = proc.pid
        lines: list[str] = []
        assert proc.stdout is not None
        for line in proc.stdout:
            line = line.rstrip()
            if line:
                append_compose_live(capture, line)
                lines.append(line)
        proc.wait()
        tail = "\n".join(lines)
        return proc.returncode or 0, tail[-4000:]

    def launch_terminal_narration(*, preview_run: bool = False) -> None:
        """Opt-in fallback only (RECAP_NARRATION_TERMINAL_FALLBACK=1). Tab auto-closes when done."""
        term_sh = _TOOLS / "run-recap-terminal.sh"
        args = ["bash", str(term_sh), str(capture)]
        if preview_run:
            args.append("--preview")
        args.append("--narration-only")
        append_compose_live(
            capture,
            f"Opening Terminal.app for Personal Voice: {' '.join(args[1:])}",
        )
        subprocess.run(args, cwd=str(_TOOLS), env=env, check=False)

    def run() -> None:
        try:
            compose_live_log_path(capture).write_text(
                f"{time.strftime('%H:%M:%S')} Compose started (python={py})\n", encoding="utf-8"
            )
        except OSError:
            pass
        try:
            if narration_only:
                append_compose_live(capture, "Personal Voice narration (headless, no Terminal)…")
                narr_cmd = producer_cmd(
                    preview_flag=preview,
                    narration_only_flag=True,
                    no_narrator_flag=False,
                )
                COMPOSE_JOBS[key]["cmd"] = narr_cmd
                code, tail = run_producer_streaming(narr_cmd)
                presentation = capture / "DemoRecapPresentation.mp4"
                work = capture / "_presentation_compose"
                spec = load_capture_spec(capture)
                verified = narr.narration_wav_is_verified_personal(work, spec)
                has_audio = (
                    narr.mp4_has_audio_stream(presentation) if presentation.is_file() else False
                )
                if code == 0 and has_audio and verified:
                    append_compose_live(capture, "Personal Voice applied (headless) — recap ready.")
                    COMPOSE_JOBS[key] = {
                        "running": False,
                        "exitCode": 0,
                        "stdoutTail": tail,
                        "stderrTail": "Personal Voice mux complete (headless).",
                        "finished": time.time(),
                        "phase": "narration",
                    }
                    return
                err = (
                    f"Headless Personal Voice failed (exit {code}). "
                    "See compose live feed below."
                )
                if narration_terminal_fallback_enabled():
                    append_compose_live(capture, err + " Opening Terminal fallback…")
                    launch_terminal_narration(preview_run=preview)
                    COMPOSE_JOBS[key] = {
                        "running": False,
                        "exitCode": code or 1,
                        "stdoutTail": tail,
                        "stderrTail": "Terminal fallback (RECAP_NARRATION_TERMINAL_FALLBACK=1). Tab closes when done.",
                        "finished": time.time(),
                        "terminalNarration": True,
                        "phase": "narration",
                    }
                    return
                append_compose_live(capture, err)
                COMPOSE_JOBS[key] = {
                    "running": False,
                    "exitCode": code or 1,
                    "stdoutTail": tail,
                    "stderrTail": err,
                    "finished": time.time(),
                    "phase": "narration_failed",
                }
                return

            if full_presentation:
                # Fast silent shell: OpenCV boxes only (--no-cursor). AI script runs in Terminal pass.
                silent_cmd = producer_cmd(
                    preview_flag=False,
                    narration_only_flag=False,
                    no_narrator_flag=True,
                    no_cursor=True,
                )
                COMPOSE_JOBS[key]["cmd"] = silent_cmd
                code, tail = run_producer_streaming(silent_cmd)
                if code != 0:
                    append_compose_live(capture, f"Silent shell failed (exit {code})")
                    COMPOSE_JOBS[key] = {
                        "running": False,
                        "exitCode": code,
                        "stdoutTail": tail,
                        "stderrTail": tail,
                        "finished": time.time(),
                    }
                    return
                append_compose_live(capture, "Silent shell OK — generating narration script for dashboard review…")
                gen = run_generate_narration_review(capture, regen=False, use_cursor=False)
                if not gen.get("ok"):
                    COMPOSE_JOBS[key] = {
                        "running": False,
                        "exitCode": 1,
                        "stdoutTail": tail,
                        "stderrTail": gen.get("error") or "Narration script generation failed",
                        "finished": time.time(),
                        "awaitingNarrationReview": False,
                    }
                    return
                COMPOSE_JOBS[key] = {
                    "running": False,
                    "exitCode": 0,
                    "stdoutTail": tail,
                    "stderrTail": "Review narration in dashboard Step 4 — approve to start headless Personal Voice.",
                    "finished": time.time(),
                    "awaitingNarrationReview": True,
                    "phase": "narration_review",
                }
                return

            code, tail = run_producer_streaming(cmd)
            append_compose_live(capture, f"Compose finished exit {code}")
            COMPOSE_JOBS[key] = {
                "running": False,
                "exitCode": code,
                "stdoutTail": tail,
                "stderrTail": tail,
                "finished": time.time(),
            }
            if code == 0 and not preview and not narration_only:
                presentation = capture / "DemoRecapPresentation.mp4"
                if presentation.is_file() and presentation.stat().st_size >= 50_000:
                    mark_compose_completed(capture)
        except Exception as exc:
            append_compose_live(capture, f"ERROR: {exc}")
            COMPOSE_JOBS[key] = {"running": False, "error": str(exc), "finished": time.time()}

    threading.Thread(target=run, daemon=True).start()
    return {"ok": True, "cmd": cmd}


def run_agent_command(body: dict) -> dict:
    command = str(body.get("command") or "").strip()
    capture = resolve_capture(body.get("capture") or body.get("path"))
    params = body.get("params") or body.get("payload") or {}

    AGENT_STATE["lastPing"] = time.time()
    AGENT_STATE["agent"] = body.get("agent") or "cursor"
    AGENT_STATE["note"] = f"command:{command}"

    if command in ("ping", ""):
        return {"ok": True, "agent": agent_status()}

    if not capture:
        return {"ok": False, "error": "capture not found"}

    if command == "get_status":
        return {"ok": True, "status": capture_status(capture)}

    if command == "get_timeline":
        try:
            return {"ok": True, "timeline": read_timeline(capture)}
        except FileNotFoundError as exc:
            return {"ok": False, "error": str(exc)}

    if command == "get_artifacts":
        return {"ok": True, "artifacts": capture_artifacts(capture)}

    if command == "save_timeline":
        try:
            spec = params if isinstance(params, dict) and params else body.get("timeline") or {}
            if not spec:
                return {"ok": False, "error": "timeline or params required"}
            path = write_timeline(capture, spec)
            return {"ok": True, "path": str(path)}
        except (ValueError, json.JSONDecodeError) as exc:
            return {"ok": False, "error": str(exc)}

    if command == "patch_timeline":
        try:
            patch = params if isinstance(params, dict) else {}
            if not patch:
                return {"ok": False, "error": "params patch required"}
            spec = merge_timeline_patch(capture, patch)
            return {"ok": True, "timeline": spec}
        except (ValueError, FileNotFoundError, json.JSONDecodeError) as exc:
            return {"ok": False, "error": str(exc)}

    if command == "compose_preview":
        result = spawn_compose(capture, preview=True, no_narrator=True, narration_only=False)
        return {"ok": bool(result.get("ok")), **result}

    if command == "compose_narration_only":
        result = spawn_compose(capture, preview=True, no_narrator=False, narration_only=True)
        return {"ok": bool(result.get("ok")), **result}

    if command == "proceed_compose":
        try:
            return proceed_and_start_compose(capture)
        except ValueError as exc:
            return {"ok": False, "error": str(exc), "composeGate": get_gate(capture)}

    if command == "get_gate":
        return {"ok": True, "composeGate": get_gate(capture)}

    return {"ok": False, "error": f"unknown command: {command}"}


class Handler(BaseHTTPRequestHandler):
    capture: Path | None = None

    def log_message(self, fmt: str, *args) -> None:
        sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))

    def _json(self, code: int, payload: dict | list) -> None:
        body = json.dumps(payload, indent=2).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _binary(self, code: int, data: bytes, content_type: str) -> None:
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            return json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise ValueError(f"invalid JSON body: {exc}") from exc

    def do_OPTIONS(self) -> None:
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, PUT, PATCH, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def _resolve_cap(self, qs: dict) -> Path | None:
        return resolve_capture(qs.get("path", [None])[0] or (str(self.capture) if self.capture else None))

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        path = unquote(parsed.path)
        qs = parse_qs(parsed.query)

        if path.startswith("/api/"):
            if path in ("/api/health", "/api/status"):
                payload: dict = {
                    "ok": True,
                    "server": "recap-dashboard",
                    "dataRoot": str(resolve_envkit_root()),
                    "defaultCapture": str(self.capture) if self.capture else None,
                    "agent": agent_status(),
                }
                if self.capture:
                    payload["capture"] = capture_status(self.capture)
                return self._json(200, payload)

            if path == "/api/build/live-status":
                cap = self._resolve_cap(qs)
                return self._json(200, compose_live_feed(cap))

            if path == "/api/capture/compose/live":
                cap = self._resolve_cap(qs)
                if not cap:
                    return self._json(404, {"error": "capture not found"})
                return self._json(200, compose_live_feed(cap))

            if path == "/api/capture/latest":
                cap = latest_capture()
                return self._json(200, {"capture": str(cap) if cap else None})

            if path == "/api/capture/status":
                cap = self._resolve_cap(qs)
                if not cap:
                    return self._json(404, {"error": "capture not found"})
                return self._json(200, capture_status(cap))

            if path == "/api/capture/timeline":
                cap = self._resolve_cap(qs)
                if not cap:
                    return self._json(404, {"error": "capture not found"})
                try:
                    return self._json(200, read_timeline(cap))
                except FileNotFoundError:
                    return self._json(404, {"error": "DemoRecapTimeline.json missing"})
                except json.JSONDecodeError as exc:
                    return self._json(500, {"error": f"invalid DemoRecapTimeline.json: {exc}"})

            if path == "/api/capture/narration":
                cap = self._resolve_cap(qs)
                if not cap:
                    return self._json(404, {"error": "capture not found"})
                p = cap / "DemoRecapFullNarration.json"
                if not p.is_file():
                    return self._json(404, {"error": "DemoRecapFullNarration.json missing"})
                try:
                    return self._json(200, json.loads(p.read_text(encoding="utf-8")))
                except json.JSONDecodeError as exc:
                    return self._json(500, {"error": f"invalid DemoRecapFullNarration.json: {exc}"})

            if path == "/api/capture/narration/review":
                cap = self._resolve_cap(qs)
                if not cap:
                    return self._json(404, {"error": "capture not found"})
                return self._json(200, narration_review_payload(cap))

            if path == "/api/capture/artifacts":
                cap = self._resolve_cap(qs)
                if not cap:
                    return self._json(404, {"error": "capture not found"})
                return self._json(200, capture_artifacts(cap))

            if path == "/api/capture/compose/gate":
                cap = self._resolve_cap(qs)
                if not cap:
                    return self._json(404, {"error": "capture not found"})
                return self._json(200, get_gate(cap))

            if path == "/api/approved/assets":
                return self._json(200, approved_assets_payload())

            if path == "/api/approved/settings":
                return self._json(200, approved_settings_payload())

            if path == "/api/approved/image":
                name = qs.get("name", [None])[0]
                if not name or name not in APPROVED_IMAGE_WHITELIST:
                    return self._json(400, {"error": "name must be a whitelisted approved asset filename"})
                p = approved_dir() / name
                if not p.is_file():
                    return self._json(404, {"error": f"{name} not found in approved dir"})
                data = p.read_bytes()
                ctype = mimetypes.guess_type(name)[0] or "image/png"
                return self._binary(200, data, ctype)

            if path == "/api/agent/status":
                return self._json(200, agent_status())

            if path == "/api/ffprobe":
                target = qs.get("path", [None])[0]
                if not target:
                    return self._json(400, {"error": "path query required"})
                p = Path(target).expanduser()
                if not p.is_file():
                    return self._json(404, {"error": "file not found"})
                dur = ffprobe_duration(p)
                return self._json(200, {"path": str(p), "durationSec": dur})

            return self._json(404, {"error": "unknown api route", "path": path})

        rel = path.lstrip("/") or "index.html"
        for base in (DASHBOARD_DIR / "dist", DASHBOARD_DIR):
            candidate = (base / rel).resolve()
            if not str(candidate).startswith(str(base.resolve())):
                continue
            if candidate.is_file():
                data = candidate.read_bytes()
                ctype = mimetypes.guess_type(str(candidate))[0] or "application/octet-stream"
                self.send_response(200)
                self.send_header("Content-Type", ctype)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)
                return

        self.send_error(404)

    def _write_timeline(self, cap: Path, spec: dict) -> None:
        try:
            path = write_timeline(cap, spec)
            return self._json(200, {"ok": True, "path": str(path)})
        except ValueError as exc:
            return self._json(400, {"error": str(exc)})

    def do_PUT(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path != "/api/capture/timeline":
            return self._json(404, {"error": "unknown route"})
        qs = parse_qs(parsed.query)
        cap = self._resolve_cap(qs)
        if not cap:
            return self._json(404, {"error": "capture not found"})
        try:
            spec = self._read_json()
        except ValueError as exc:
            return self._json(400, {"error": str(exc)})
        return self._write_timeline(cap, spec)

    def do_PATCH(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path != "/api/capture/timeline":
            return self._json(404, {"error": "unknown route"})
        qs = parse_qs(parsed.query)
        cap = self._resolve_cap(qs)
        if not cap:
            return self._json(404, {"error": "capture not found"})
        try:
            patch = self._read_json()
            spec = merge_timeline_patch(cap, patch)
            return self._json(200, {"ok": True, "timeline": spec})
        except (ValueError, FileNotFoundError, json.JSONDecodeError) as exc:
            return self._json(400, {"error": str(exc)})

    def do_POST(self) -> None:
        parsed = urlparse(self.path)
        path = parsed.path
        qs = parse_qs(parsed.query)

        try:
            body = self._read_json()
        except ValueError as exc:
            return self._json(400, {"error": str(exc)})

        if path == "/api/compose":
            cap = resolve_capture(body.get("capture") or (str(self.capture) if self.capture else None))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            result = spawn_compose(
                cap,
                preview=bool(body.get("preview", True)),
                no_narrator=bool(body.get("noNarrator", True)),
                narration_only=bool(body.get("narrationOnly", False)),
            )
            code = 200 if result.get("ok") else 409
            return self._json(code, result)

        if path == "/api/capture/compose/preview":
            cap = resolve_capture(body.get("capture") or qs.get("path", [None])[0] or (str(self.capture) if self.capture else None))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            result = spawn_compose(cap, preview=True, no_narrator=True, narration_only=False)
            code = 200 if result.get("ok") else 409
            return self._json(code, result)

        if path == "/api/capture/compose/narration-only":
            cap = resolve_capture(body.get("capture") or qs.get("path", [None])[0] or (str(self.capture) if self.capture else None))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            result = spawn_compose(
                cap,
                preview=bool(body.get("preview", False)),
                no_narrator=False,
                narration_only=True,
                require_gate=False,
                skip_approval_check=bool(body.get("skipApproval")),
            )
            code = 200 if result.get("ok") else 409
            return self._json(code, result)

        if path == "/api/capture/narration/review/regenerate":
            cap = resolve_capture(body.get("capture") or qs.get("path", [None])[0] or (str(self.capture) if self.capture else None))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            write_narration_approval(cap, {"approved": False, "status": "generating", "generating": True})
            use_cursor = bool(body.get("useCursor"))
            result = run_generate_narration_review(cap, regen=True, use_cursor=use_cursor)
            code = 200 if result.get("ok") else 500
            return self._json(code, result)

        if path == "/api/capture/narration/review/approve":
            cap = resolve_capture(body.get("capture") or qs.get("path", [None])[0] or (str(self.capture) if self.capture else None))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            review = narration_review_summary(cap)
            if not review.get("ready"):
                return self._json(409, {"ok": False, "error": "No narration script to approve — regenerate first."})
            if review.get("generating"):
                return self._json(409, {"ok": False, "error": "Script still generating — wait a moment."})
            approve_narration_review(cap)
            start = body.get("startPersonalVoice", True)
            if start is not False:
                result = spawn_compose(
                    cap,
                    preview=False,
                    no_narrator=False,
                    narration_only=True,
                    require_gate=False,
                    skip_approval_check=True,
                )
                if not result.get("ok"):
                    return self._json(409, {"ok": False, "error": result.get("error"), "review": narration_review_summary(cap)})
                return self._json(200, {"ok": True, "review": narration_review_summary(cap), "composeJob": COMPOSE_JOBS.get(str(cap))})
            return self._json(200, {"ok": True, "review": narration_review_summary(cap)})

        if path == "/api/capture/compose/gate":
            cap = self._resolve_cap(qs) or resolve_capture(body.get("capture"))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            if body.get("waiting"):
                return self._json(200, set_gate_waiting(cap))
            if body.get("proceed"):
                try:
                    return self._json(200, set_gate_proceed(cap))
                except ValueError as exc:
                    return self._json(400, {"error": str(exc), "composeGate": get_gate(cap)})
            return self._json(200, get_gate(cap))

        if path == "/api/capture/compose/gate/verify":
            cap = self._resolve_cap(qs) or resolve_capture(body.get("capture"))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            return self._json(200, {"ok": True, "composeGate": set_gate_assets_verified(cap)})

        if path == "/api/capture/compose/gate/proceed":
            cap = self._resolve_cap(qs) or resolve_capture(body.get("capture"))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            try:
                payload = proceed_and_start_compose(cap)
                code = 200 if payload.get("ok") else 400
                return self._json(code, payload)
            except ValueError as exc:
                return self._json(400, {"error": str(exc), "composeGate": get_gate(cap)})

        if path == "/api/capture/compose/start":
            cap = self._resolve_cap(qs) or resolve_capture(body.get("capture"))
            if not cap:
                return self._json(404, {"error": "capture not found"})
            try:
                gate = ensure_gate_ready_for_compose(cap)
                if not gate.get("assetsVerified"):
                    return self._json(
                        400,
                        {"error": "Verify cards in Step 1 first", "composeGate": gate},
                    )
                if not gate.get("proceed"):
                    set_gate_proceed(cap)
                payload = proceed_and_start_compose(cap)
                code = 200 if payload.get("ok") else 400
                return self._json(code, payload)
            except ValueError as exc:
                return self._json(400, {"error": str(exc), "composeGate": get_gate(cap)})

        if path == "/api/approved/settings":
            try:
                patch = body.get("settings") if isinstance(body.get("settings"), dict) else body
                spec = write_approved_cards(patch)
                return self._json(200, {"ok": True, "settings": approved_settings_payload()["settings"]})
            except OSError as exc:
                return self._json(500, {"error": str(exc)})

        if path == "/api/agent/ping":
            AGENT_STATE["lastPing"] = time.time()
            AGENT_STATE["agent"] = body.get("agent") or "cursor"
            AGENT_STATE["note"] = body.get("note")
            return self._json(200, {"ok": True, **agent_status()})

        if path == "/api/agent/command":
            result = run_agent_command(body)
            code = 200 if result.get("ok", True) and "error" not in result else 400
            if result.get("ok") is False:
                code = 400
            return self._json(code, result)

        return self._json(404, {"error": "unknown route", "path": path})


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--capture", type=Path, help="Default capture folder for API calls")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--bind", default="127.0.0.1")
    args = ap.parse_args()

    cap = resolve_capture(str(args.capture) if args.capture else None)
    Handler.capture = cap

    httpd = ThreadingHTTPServer((args.bind, args.port), Handler)
    base = f"http://{args.bind}:{args.port}"
    print(f"Recap dashboard API on {base}/")
    print("Keep this process running while using curl or the React UI.")
    if cap:
        print(f"Default capture: {cap}")
    print(f"Data root: {resolve_envkit_root()}")
    print("Quick test:")
    print(f"  curl -s {base}/api/health")
    if cap:
        print(f"  curl -s '{base}/api/capture/status?path={cap}'")
        print(f"  curl -s '{base}/api/capture/compose/gate?path={cap}'")
    else:
        print(f"  curl -s {base}/api/capture/latest")
    print("One command (server + UI): bash start-recap-dashboard.sh [capture_path]")
    print("Agent docs: RECAP_AGENT_API.md")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
