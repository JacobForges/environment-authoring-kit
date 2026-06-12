#!/usr/bin/env python3
"""
AI-directed demo recap: director → quality ladder loop → enhanced compose.

Usage:
  python3 run-demo-recap-pipeline.py <capture_folder> [--checkpoints 15] [--subbeats 10] [--draft-only]

Only touches Tools/cave-grader + capture folder artifacts.
"""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

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

CHAPTERS = [
    ("Session bootstrap", "surface_build", "editor queue", [72, 138, 220]),
    ("Grid contract", "surface_build", "nine-tile grid", [90, 168, 120]),
    ("Seam invariants", "surface_build", "tile seams", [110, 175, 200]),
    ("Play disk", "surface_build", "play disk grading", [200, 140, 80]),
    ("Terrain meat", "surface_build", "terrain meat", [210, 120, 90]),
    ("Foothills", "surface_build", "foothills", [150, 110, 210]),
    ("Mountain ring", "surface_build", "mountains", [130, 100, 190]),
    ("Labyrinth annex", "surface_build", "south annex", [100, 190, 230]),
    ("Trail bench", "surface_build", "trail queue", [220, 90, 90]),
    ("Surface phase", "surface_build", "surface lock", [72, 138, 220]),
    ("Final capture", "surface_build", "recording end", [220, 90, 90]),
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
        return
    for line in p.read_text().splitlines():
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
    if not asset.is_file():
        return
    text = asset.read_text()
    m = re.search(r"aiProvider:\s*(\d+)", text)
    idx = int(m.group(1)) if m else 0
    provider = PROVIDER_NAMES[idx] if 0 <= idx < len(PROVIDER_NAMES) else "Cursor"
    os.environ.setdefault("CAVE_AI_PROVIDER", provider)

    def pick(field: str, default: str = "") -> str:
        mm = re.search(rf"{field}:\s*(.+)", text)
        return mm.group(1).strip() if mm else default

    if provider == "Cursor":
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("modelId", "auto"))
        os.environ.setdefault(
            "CAVE_ACTIVE_API_KEY",
            os.environ.get("CURSOR_API_KEY", os.environ.get("CAVE_ACTIVE_API_KEY", "")),
        )
    elif provider == "OpenAICompatible":
        os.environ.setdefault("CAVE_ACTIVE_BASE_URL", pick("openAiCompatibleBaseUrl"))
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("openAiModelId", "gpt-4.1-mini"))
    elif provider == "LocalOllama":
        os.environ.setdefault("CAVE_ACTIVE_BASE_URL", "http://localhost:11434/v1")
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("ollamaModelId", "qwen2.5-coder:14b"))
        os.environ.setdefault("CAVE_ACTIVE_API_KEY", "ollama")
    os.environ.setdefault("HUB_ROOT", str(hub))
    os.environ.setdefault("TSX_DISABLE_IPC", "1")
    os.environ.setdefault("CAVE_RECAP_CONCURRENCY", "1")
    os.environ.setdefault("CAVE_RECAP_PACE_MS", "1500")
    os.environ.setdefault("CAVE_RECAP_REUSE_IMAGE", "1")


def milestone_png(frames: list[Path], m: dict) -> str:
    fi = min(int(m.get("frame", 0)), len(frames) - 1)
    return str(frames[fi].resolve())


def ai_milestone_row(i: int, m: dict, frames: list[Path]) -> dict:
    row = {
        "i": i,
        "frame": m.get("frame", 0),
        "imagePath": milestone_png(frames, m),
        "beatKind": m.get("beatKind", "checkpoint"),
        "chapter": m.get("chapter", ""),
        "phase": m.get("phase", ""),
        "sub": m.get("sub", ""),
        "line1": m.get("line1", ""),
        "line2": m.get("line2", ""),
        "line3": m.get("line3", ""),
    }
    if m.get("subAction"):
        row["subAction"] = m["subAction"]
    return row


def list_timelapse(run: Path) -> list[Path]:
    d = run / "timelapse"
    for pat in ("tl_*.png", "t_*.png"):
        hits = sorted(d.glob(pat))
        if hits:
            return hits
    return []


def load_timeline_module():
    import importlib.util

    p = Path(__file__).resolve().parent / "demo-recap-timeline.py"
    spec = importlib.util.spec_from_file_location("demo_recap_timeline", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def run_tsx(hub: Path, mode: str, req_path: Path) -> dict:
    tools = hub / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
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
        timeout=3600 if mode in ("vision", "milestones") else 900,
    )
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr or proc.stdout or f"pipeline {mode} failed")
    return json.loads(proc.stdout)


def merge_vision_into_timeline(milestones: list[dict], vision: list[dict]) -> None:
    by_i = {int(v.get("i", 0)): v.get("regions", []) for v in vision}
    for i, m in enumerate(milestones):
        if i in by_i and by_i[i]:
            m["regions"] = by_i[i][:2]


def load_apply_approved_cards():
    import importlib.util

    p = Path(__file__).resolve().parent / "apply-approved-cards.py"
    spec = importlib.util.spec_from_file_location("apply_approved_cards", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def merge_director_into_timeline(milestones: list[dict], directed: list[dict]) -> list[dict]:
    by_i = {int(d.get("i", k)): d for k, d in enumerate(directed)}
    merged: list[dict] = []
    for i, m in enumerate(milestones):
        d = by_i.get(i, by_i.get(m.get("i", i), {}))
        nm = {**m, **d}
        nm["frame"] = m.get("frame", d.get("frame", 0))
        nm["beatKind"] = m.get("beatKind", d.get("beatKind", "checkpoint"))
        nm["accent"] = m.get("accent", d.get("accent", [72, 138, 220]))
        if m.get("subAction"):
            nm["subAction"] = m["subAction"]
        if d.get("regions") is not None:
            nm["regions"] = d.get("regions", [])[:2]
        merged.append(nm)
    return merged


def main() -> int:
    hub = resolve_hub()
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    compose_only = "--compose-only" in sys.argv
    draft_only = "--draft-only" in sys.argv or compose_only
    checkpoints = 15
    subbeats = 10
    for i, a in enumerate(sys.argv[1:]):
        if a in ("--milestones", "--checkpoints") and i + 2 < len(sys.argv):
            checkpoints = max(6, int(sys.argv[i + 2]))
        if a == "--subbeats" and i + 2 < len(sys.argv):
            subbeats = max(0, int(sys.argv[i + 2]))

    if not args:
        print(
            "usage: run-demo-recap-pipeline.py <capture_folder> "
            "[--checkpoints 15] [--subbeats 10] [--draft-only]"
        )
        return 1

    run = Path(args[0]).expanduser()
    frames = list_timelapse(run)
    if len(frames) < 8:
        print("Need timelapse PNGs", file=sys.stderr)
        return 1

    tl = run / "DemoRecapTimeline.json"
    existing = json.loads(tl.read_text()) if tl.is_file() else {}
    build_mode = existing.get("buildMode", "FullWorld additive surface_build")

    if compose_only and existing.get("milestones"):
        milestones = existing["milestones"]
        print(f"Compose-only: {len(milestones)} milestones (AI skipped)")
    elif (
        existing.get("milestones")
        and len(existing["milestones"]) >= 6
        and "--rebuild-milestones" not in sys.argv
    ):
        milestones = existing["milestones"]
        print(f"Using {len(milestones)} existing milestones")
    else:
        tl_mod = load_timeline_module()
        milestones = tl_mod.build_layered_timeline(
            len(frames), build_mode, checkpoints=checkpoints, subbeats=subbeats
        )
        n_cp = sum(1 for m in milestones if m.get("beatKind") == "checkpoint")
        n_sub = sum(1 for m in milestones if m.get("beatKind") == "subbeat")
        print(f"Built timeline: {n_cp} checkpoints + {n_sub} subbeats ({len(milestones)} beats)")

    director_in = {
        "buildMode": build_mode,
        "milestones": [ai_milestone_row(i, m, frames) for i, m in enumerate(milestones)],
    }

    critique = ""
    max_iter = 1 if draft_only else 3
    if not draft_only:
        load_hub_ai_env(hub)
        req = run / "DemoRecapDirectorRequest.json"
        for it in range(1, max_iter + 1):
            director_in["critique"] = critique
            director_in["iteration"] = it
            req.write_text(json.dumps(director_in, indent=2))
            conc = os.environ.get("CAVE_RECAP_CONCURRENCY", "1")
            print(
                f"AI pass {it}/{max_iter} — Cursor parallel ×{conc} "
                f"(captions + vision per milestone PNG)…"
            )
            out = run_tsx(hub, "milestones", req)
            (run / "DemoRecapDirector.json").write_text(json.dumps(out, indent=2) + "\n")
            milestones = merge_director_into_timeline(milestones, out.get("milestones", []))
            vision_out = {
                "milestones": [
                    {"i": m.get("i", i), "regions": m.get("regions", [])}
                    for i, m in enumerate(out.get("milestones", []))
                ]
            }
            (run / "DemoRecapVision.json").write_text(json.dumps(vision_out, indent=2) + "\n")
            grade_path = run / "DemoRecapQualityIn.json"
            grade_path.write_text(
                json.dumps({"buildMode": build_mode, "milestones": milestones}, indent=2)
            )
            quality = run_tsx(hub, "grade", grade_path)
            (run / "DemoRecapQuality.json").write_text(json.dumps(quality, indent=2) + "\n")
            print(
                f"Quality score {quality.get('overallScore')} "
                f"pass={quality.get('pass')} rung={quality.get('rung')}"
            )
            if quality.get("pass"):
                break
            critique = quality.get("revisionPrompt", "")
            director_in["milestones"] = [
                ai_milestone_row(i, m, frames) for i, m in enumerate(milestones)
            ]
    else:
        print("draft-only: skipping AI director/quality loop")

    hold = float(existing.get("milestoneHoldSec", 12.0))
    sub_hold = float(existing.get("subbeatHoldSec", 5.5))
    delay = float(existing.get("annotationDelaySec", 3.0))
    sub_delay = float(existing.get("subbeatAnnotationDelaySec", 1.8))
    if hold < delay + 4.0:
        hold = delay + 7.0

    import importlib.util

    cap_path = Path(__file__).resolve().parent / "demo-recap-captions.py"
    cs = importlib.util.spec_from_file_location("demo_recap_captions", cap_path)
    cm = importlib.util.module_from_spec(cs)
    assert cs.loader
    cs.loader.exec_module(cm)
    for m in milestones:
        cm.fill_milestone_captions(m)

    card_fields = load_apply_approved_cards().apply_to_timeline(run)
    print(f"Approved cards: intro c / outro b → {run / 'ApprovedIntro.png'}")

    tc_path = Path(__file__).resolve().parent / "demo-recap-title-cards.py"
    tc_spec = importlib.util.spec_from_file_location("demo_recap_title_cards", tc_path)
    tc_mod = importlib.util.module_from_spec(tc_spec)
    assert tc_spec.loader
    tc_spec.loader.exec_module(tc_mod)
    when = existing.get("captureDisplayTime") or tc_mod.capture_display_time(run)

    spec = {
        "recapMode": "professional_motion",
        "captureIntervalSec": existing.get("captureIntervalSec", 2.0),
        "targetDurationSec": float(existing.get("targetDurationSec", 3600)),
        "milestoneHoldSec": hold,
        "subbeatHoldSec": sub_hold,
        "subbeatAnnotationDelaySec": sub_delay,
        "checkpointCount": checkpoints,
        "subbeatCount": subbeats,
        "annotationDelaySec": delay,
        "annotationFadeSec": float(existing.get("annotationFadeSec", 0.65)),
        "captionLine2DelaySec": 0.55,
        "captionLine3DelaySec": 1.05,
        "framesPerSource": int(existing.get("framesPerSource", 5)),
        "introSec": float(existing.get("introSec", 5.5)),
        "outroSec": float(existing.get("outroSec", 6.0)),
        "outputFps": float(existing.get("outputFps", 60)),
        "portfolioTitleCards": bool(existing.get("portfolioTitleCards", True)),
        "ensureCreatorAssets": True,
        "mimicSignature": existing.get("mimicSignature", False),
        "signatureName": existing.get("signatureName", "Jacob Adkins"),
        "introFooter": existing.get("introFooter", "Built in Unity · cut by code"),
        "outroFooter": existing.get("outroFooter", "Until the next build"),
        "introPortraitHeight": int(existing.get("introPortraitHeight", 210)),
        "outroPortraitHeight": int(existing.get("outroPortraitHeight", 268)),
        "introSignatureMaxHeight": int(existing.get("introSignatureMaxHeight", 52)),
        "outroSignatureMaxHeight": int(existing.get("outroSignatureMaxHeight", 64)),
        "captureDisplayTime": when,
        "signatureImage": existing.get("signatureImage", ""),
        "minTimelapseGapFrames": 1,
        "videoEnhance": True,
        "noTransitions": True,
        "segmentFadeSec": 0.0,
        "segmentXfadeSec": 0.0,
        "lectureMode": True,
        "aiRegionsOnly": True,
        "showAnnotations": bool(existing.get("showAnnotations", False)),
        "chapterFocusAnnotations": draft_only or not bool(existing.get("cursorVision", True)),
        "aiDirected": not draft_only,
        "cinematicCamera": True,
        "cursorVision": not draft_only,
        "introTitle": existing.get("introTitle", "Unity World Build Recap"),
        "introSubtitle": existing.get(
            "introSubtitle",
            "Procedural terrain timelapse · automated AI captions & vision annotations",
        ),
        "introProcessNote": existing.get("introProcessNote", existing.get("introDisclosure", "")),
        "introBadge": existing.get("introBadge", "Automated · AI-integrated"),
        "outroTitle": existing.get("outroTitle", "Recap complete · automated assembly"),
        "outroSubtitle": existing.get(
            "outroSubtitle",
            "Second-year AI engineering portfolio — procedural world build, automated recap pipeline.",
        ),
        "outroProcessNote": existing.get("outroProcessNote", existing.get("outroDisclosure", "")),
        "outroBadge": existing.get("outroBadge", "Environment Kit · Cave Grader"),
        "buildMode": build_mode,
        "milestones": milestones,
    }
    spec.update(card_fields)
    work_raw = existing.get("composeWorkDir") or os.environ.get("DEMO_RECAP_WORK_DIR")
    if work_raw:
        spec["composeWorkDir"] = str(Path(str(work_raw)).expanduser())
    tl.write_text(json.dumps(spec, indent=2) + "\n")

    compose = Path(__file__).resolve().parent / "compose-smart-recap.py"
    out = run / "DemoRecap.mp4"
    env = {**os.environ, "HUB_ROOT": str(hub)}
    print(
        f"Composing professional recap ({spec['outputFps']:.0f} fps, "
        f"{len(frames)} source PNGs, ×{spec['framesPerSource']} per frame)…"
    )
    subprocess.run([sys.executable, str(compose), str(run), str(out)], check=True, env=env)
    print(str(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
