"""Cursor API director — per-milestone captions + vision from real PNGs (demo-recap-pipeline.ts)."""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

PROVIDER_NAMES = [
    "Cursor",
    "GoogleGemini",
    "AnthropicClaude",
    "OpenAICompatible",
    "OpenRouter",
    "LocalOllama",
    "LocalLmStudio",
    "CustomEndpoint",
]


def resolve_hub() -> Path:
    hub = Path(os.environ.get("HUB_ROOT", "")).expanduser()
    if hub.is_dir() and (hub / "Assets").is_dir():
        return hub
    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    return hub


def load_dotenv(hub: Path) -> None:
    p = hub / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
    if not p.is_file():
        p = Path(__file__).resolve().parent / ".env"
    if not p.is_file():
        return
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, _, v = line.partition("=")
        k, v = k.strip(), v.strip().strip('"').strip("'")
        if k and k not in os.environ:
            os.environ[k] = v


def load_hub_ai_env(hub: Path) -> None:
    load_dotenv(hub)
    asset = (
        hub
        / "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildCursorSettings.asset"
    )
    if asset.is_file():
        text = asset.read_text(encoding="utf-8")

        def pick(field: str, default: str = "") -> str:
            mm = re.search(rf"{field}:\s*(.+)", text)
            return mm.group(1).strip() if mm else default

        m = re.search(r"aiProvider:\s*(\d+)", text)
        idx = int(m.group(1)) if m else 0
        provider = PROVIDER_NAMES[idx] if 0 <= idx < len(PROVIDER_NAMES) else "Cursor"
        os.environ.setdefault("CAVE_AI_PROVIDER", provider)
        if provider == "Cursor":
            os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("modelId", "auto"))
            os.environ.setdefault(
                "CAVE_ACTIVE_API_KEY",
                os.environ.get("CURSOR_API_KEY", os.environ.get("CAVE_ACTIVE_API_KEY", "")),
            )
        elif provider == "OpenAICompatible":
            os.environ.setdefault("CAVE_ACTIVE_BASE_URL", pick("openAiCompatibleBaseUrl"))
            os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("openAiModelId", "gpt-4.1-mini"))
            os.environ.setdefault("CAVE_ACTIVE_API_KEY", os.environ.get("OPENAI_API_KEY", ""))
    os.environ.setdefault("HUB_ROOT", str(hub))
    os.environ.setdefault("TSX_DISABLE_IPC", "1")
    os.environ.setdefault("CAVE_RECAP_CONCURRENCY", os.environ.get("CAVE_RECAP_CONCURRENCY", "4"))


def has_cursor_api_key() -> bool:
    return bool(
        os.environ.get("CURSOR_API_KEY", "").strip()
        or os.environ.get("CAVE_ACTIVE_API_KEY", "").strip()
    )


def milestone_png(frames: list[Path], m: dict[str, Any]) -> str:
    fi = min(int(m.get("frame", 0)), len(frames) - 1)
    return str(frames[fi].resolve())


def ai_milestone_row(i: int, m: dict[str, Any], frames: list[Path]) -> dict[str, Any]:
    row: dict[str, Any] = {
        "i": i,
        "frame": m.get("frame", 0),
        "imagePath": milestone_png(frames, m),
        "beatKind": m.get("beatKind", "checkpoint"),
        "chapter": m.get("chapter", ""),
        "phase": m.get("phase", ""),
        "sub": m.get("sub", "") or m.get("subAction", ""),
        "line1": m.get("line1", ""),
        "line2": m.get("line2", ""),
        "line3": m.get("line3", ""),
    }
    if m.get("subAction"):
        row["subAction"] = m["subAction"]
    return row


def run_tsx(hub: Path, mode: str, req_path: Path) -> dict[str, Any]:
    tools = hub / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
    if not (tools / "demo-recap-pipeline.ts").is_file():
        tools = Path(__file__).resolve().parent
    script = tools / "demo-recap-pipeline.ts"
    node = shutil.which("node") or "node"
    env = os.environ.copy()
    env["HUB_ROOT"] = str(hub)
    env["TSX_DISABLE_IPC"] = "1"
    proc = subprocess.run(
        [node, "--import", "tsx", str(script), mode, str(req_path)],
        capture_output=True,
        text=True,
        cwd=tools,
        env=env,
        timeout=3600 if mode in ("vision", "milestones") else 1200 if mode == "narration" else 900,
    )
    if proc.returncode != 0:
        raise RuntimeError((proc.stderr or proc.stdout or f"pipeline {mode} failed")[:4000])
    return json.loads(proc.stdout)


def merge_director_into_timeline(milestones: list[dict[str, Any]], directed: list[dict[str, Any]]) -> None:
    by_i = {int(d.get("i", k)): d for k, d in enumerate(directed)}
    for i, m in enumerate(milestones):
        d = by_i.get(i, by_i.get(int(m.get("i", i)), {}))
        if not d:
            continue
        for key in ("line1", "line2", "line3", "chapter", "narratorScript", "teachingFocus"):
            if d.get(key):
                m[key] = d[key]
        if d.get("regions") is not None:
            m["regions"] = d.get("regions", [])[:2]
            m["annotationSource"] = "cursor"


def apply_cursor_milestones_director(
    hub: Path,
    run: Path,
    milestones: list[dict[str, Any]],
    frames: list[Path],
    *,
    build_mode: str,
    milestone_indices: list[int] | None = None,
) -> bool:
    """One Cursor agent per milestone PNG — captions + callout boxes from what it sees."""
    load_hub_ai_env(hub)
    if not has_cursor_api_key():
        print("Cursor director skipped: set CURSOR_API_KEY in Tools/cave-grader/.env", file=sys.stderr)
        return False

    indices = milestone_indices if milestone_indices is not None else list(range(len(milestones)))
    rows = [ai_milestone_row(i, milestones[i], frames) for i in indices if 0 <= i < len(milestones)]
    if not rows:
        return False

    req = run / "DemoRecapCursorDirectorRequest.json"
    req.write_text(
        json.dumps({"buildMode": build_mode, "milestones": rows}, indent=2) + "\n",
        encoding="utf-8",
    )
    provider = os.environ.get("CAVE_AI_PROVIDER", "Cursor")
    conc = os.environ.get("CAVE_RECAP_CONCURRENCY", "4")
    print(f"Director: Cursor API ({provider}) — {len(rows)} milestones ×{conc} (PNG vision + captions)…")
    out = run_tsx(hub, "milestones", req)
    (run / "DemoRecapCursorDirector.json").write_text(json.dumps(out, indent=2) + "\n", encoding="utf-8")
    merge_director_into_timeline(milestones, out.get("milestones", []))
    try:
        import importlib.util

        tools = Path(__file__).resolve().parent
        spec = importlib.util.spec_from_file_location("opencv", tools / "demo-recap-opencv-annotate.py")
        opencv = importlib.util.module_from_spec(spec)
        assert spec.loader
        spec.loader.exec_module(opencv)
        n = opencv.sync_region_labels_from_timeline(milestones)
        if n:
            print(f"Director: synced {n} region label(s) to timeline phase")
    except Exception:
        pass
    with_regions = sum(1 for m in milestones if m.get("regions"))
    print(f"Director applied: {with_regions}/{len(milestones)} beats with vision regions")
    return True


def narration_outline_rows(milestones: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Caption + structure per beat — feeds Cursor full-script (spoken words are fresh, not pasted)."""
    rows: list[dict[str, Any]] = []
    for i, m in enumerate(milestones):
        lines = [
            str(m.get("line1", "") or "").strip(),
            str(m.get("line2", "") or "").strip(),
            str(m.get("line3", "") or "").strip(),
        ]
        rows.append(
            {
                "i": int(m.get("i", i)),
                "beatKind": m.get("beatKind", "checkpoint"),
                "chapter": m.get("chapter", ""),
                "phase": m.get("phase", ""),
                "sub": m.get("sub", "") or m.get("subAction", ""),
                "subAction": m.get("subAction", ""),
                "teachingFocus": m.get("teachingFocus", ""),
                "line1": lines[0],
                "line2": lines[1],
                "line3": lines[2],
                "onScreenCaption": " ".join(x for x in lines if x),
            }
        )
    return rows


def ensure_full_narration_script(
    run: Path,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    graded_video: Path,
    *,
    build_mode: str,
    regen: bool = False,
    prefer_local: bool = False,
) -> str | None:
    """
    After silent video is graded: probe duration, write Cursor script from captions, save JSON.
    When prefer_local (narration-only / Terminal), skip Cursor Agent and stitch locally.
    """
    narr_path = Path(__file__).resolve().parent / "demo-recap-narrator.py"
    import importlib.util

    ns = importlib.util.spec_from_file_location("narr", narr_path)
    narr = importlib.util.module_from_spec(ns)
    assert ns.loader
    ns.loader.exec_module(narr)

    spec = narr.flatten_narrator_personal_settings(spec)
    existing = narr.resolve_full_narration_script(run, spec, milestones)
    json_path = run / "DemoRecapFullNarration.json"
    if existing and json_path.is_file() and not regen:
        print(f"Full narration: reusing {json_path.name}", flush=True)
        return existing

    target_sec = float(spec.get("fullNarrationTargetSec") or spec.get("targetDurationSec") or 480)
    if graded_video.is_file():
        try:
            dur = narr.probe_duration(graded_video)
            if dur > 30:
                target_sec = dur
                spec = {**spec, "fullNarrationTargetSec": dur}
                print(f"Full narration: script target {dur:.1f}s from graded video", flush=True)
        except Exception as exc:
            print(f"Full narration: could not probe video ({exc})", file=sys.stderr)

    if prefer_local and not regen:
        print(
            "Full narration: local script for Personal Voice (skipping Cursor Agent in Terminal pass)",
            flush=True,
        )
        return narr.write_local_full_narration_script(
            run, milestones, spec, target_sec=target_sec
        )

    if not has_cursor_api_key():
        local = narr.write_local_full_narration_script(
            run, milestones, spec, target_sec=target_sec
        )
        if local:
            return local
        print(
            "ERROR: CURSOR_API_KEY required to write narration script before finalize.",
            file=sys.stderr,
        )
        return existing or None

    try:
        return apply_cursor_full_narration_script(
            resolve_hub(),
            run,
            milestones,
            spec,
            build_mode=build_mode,
        )
    except Exception as exc:
        print(f"Cursor narration script failed ({exc}) — using local milestone script", file=sys.stderr)
        return narr.write_local_full_narration_script(
            run, milestones, spec, target_sec=target_sec
        ) or existing


def apply_cursor_full_narration_script(
    hub: Path,
    run: Path,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    *,
    build_mode: str,
) -> str | None:
    """
    Cursor writes one continuous voiceover script (DemoRecapFullNarration.json).
    On-screen captions are not used for TTS.
    """
    load_hub_ai_env(hub)
    if not has_cursor_api_key():
        print("Full narration script skipped: no CURSOR_API_KEY", file=sys.stderr)
        return None

    target_sec = float(
        spec.get("fullNarrationTargetSec")
        or spec.get("targetDurationSec")
        or 480
    )
    try:
        narr_path = Path(__file__).resolve().parent / "demo-recap-narrator.py"
        import importlib.util

        ns = importlib.util.spec_from_file_location("narr_mod", narr_path)
        narr = importlib.util.module_from_spec(ns)
        assert ns.loader
        ns.loader.exec_module(narr)
        say_r = narr.personal_say_rate(spec)
    except Exception:
        narr = None
        say_r = int(spec.get("narratorPersonalSayRate") or spec.get("narratorRate", 183))

    req = run / "DemoRecapFullNarrationRequest.json"
    req.write_text(
        json.dumps(
            {
                "buildMode": build_mode,
                "targetDurationSec": target_sec,
                "sayRateWpm": say_r,
                "introTitle": spec.get("introTitle", "World Build Recap"),
                "introSubtitle": spec.get("introSubtitle", ""),
                "milestones": narration_outline_rows(milestones),
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    if narr:
        narr.write_narration_voice_guide(run, spec, milestones, target_sec=target_sec)
    print(
        f"Director: Cursor full narration script (~{int(target_sec)}s video, "
        f"from milestone captions + scene outline)…",
        flush=True,
    )
    try:
        out = run_tsx(hub, "narration", req)
    except Exception as exc:
        print(f"Full narration script failed: {exc}", file=sys.stderr)
        return None

    script = (out.get("script") or "").strip()
    if not script:
        return None

    payload = {
        "script": script,
        "wordCount": out.get("wordCount", len(script.split())),
        "targetDurationSec": target_sec,
        "sayRateWpm": say_r,
        "source": "cursor",
    }
    payload.update(narr.narration_voice_metadata(spec, run) if narr else {})
    path = run / "DemoRecapFullNarration.json"
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    (run / "DemoRecapFullNarration.txt").write_text(script + "\n", encoding="utf-8")
    if narr:
        narr.write_narration_voice_guide(
            run, spec, milestones, target_sec=target_sec, script_preview=script
        )
    print(
        f"Full narration script: {payload['wordCount']} words → {path.name}",
        flush=True,
    )
    return script
