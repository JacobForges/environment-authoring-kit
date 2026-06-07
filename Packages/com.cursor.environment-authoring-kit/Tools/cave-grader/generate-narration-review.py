#!/usr/bin/env python3
"""Generate narration script + NarrationVoiceGuide.md for dashboard review (no TTS)."""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

import importlib.util

from envkit_paths import approved_dir


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / file)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


narr = _load("narr", "demo-recap-narrator.py")
director = _load("director", "demo-recap-cursor-director.py")
producer = _load("producer", "demo-recap-producer.py")

APPROVAL_NAME = "NarrationVoiceApproval.json"


def hub_root() -> Path:
    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    return hub


def write_approval(capture: Path, patch: dict) -> dict:
    path = capture / APPROVAL_NAME
    data: dict = {}
    if path.is_file():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            data = {}
    data.update(patch)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return data


def probe_video_target(capture: Path, spec: dict) -> float:
    for rel in ("_presentation_compose/_final_video.mp4", "DemoRecapPresentation.mp4"):
        vid = capture / rel
        if vid.is_file():
            try:
                dur = narr.probe_duration(vid)
                if dur > 30:
                    return dur
            except Exception:
                pass
    return float(
        spec.get("fullNarrationTargetSec") or spec.get("targetDurationSec") or 480
    )


def generate(capture: Path, *, regen: bool = False, use_cursor: bool = False) -> dict:
    tl_path = capture / "DemoRecapTimeline.json"
    if not tl_path.is_file():
        raise FileNotFoundError("DemoRecapTimeline.json missing")

    spec = json.loads(tl_path.read_text(encoding="utf-8"))
    milestones = spec.get("milestones") or []
    if not milestones:
        raise ValueError("No milestones in timeline")

    approved_path = approved_dir() / "ApprovedCards.json"
    if approved_path.is_file():
        spec = narr.flatten_narrator_personal_settings(
            {**spec, **json.loads(approved_path.read_text(encoding="utf-8"))}
        )
    else:
        spec = narr.flatten_narrator_personal_settings(spec)

    target_sec = probe_video_target(capture, spec)
    spec["fullNarrationTargetSec"] = target_sec

    write_approval(
        capture,
        {
            "status": "generating",
            "generating": True,
            "approved": False,
            "startedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        },
    )

    full_json = capture / "DemoRecapFullNarration.json"
    script = ""
    source = "local_milestones"

    if regen and full_json.is_file():
        full_json.unlink(missing_ok=True)
        (capture / "DemoRecapFullNarration.txt").unlink(missing_ok=True)

    if use_cursor and director.has_cursor_api_key():
        build_mode = str(spec.get("buildMode") or "world-build")
        script = director.apply_cursor_full_narration_script(
            hub_root(),
            capture,
            milestones,
            spec,
            build_mode=build_mode,
        ) or ""
        source = "cursor"
    elif full_json.is_file() and not regen:
        script = narr.resolve_full_narration_script(capture, spec, milestones)
        try:
            payload = json.loads(full_json.read_text(encoding="utf-8"))
            source = str(payload.get("source") or source)
        except json.JSONDecodeError:
            pass
    else:
        script = narr.write_local_full_narration_script(
            capture, milestones, spec, target_sec=target_sec
        )
        source = "local_milestones"

    if not script.strip():
        write_approval(
            capture,
            {
                "status": "error",
                "generating": False,
                "approved": False,
                "error": "Script generation produced empty text",
            },
        )
        return {"ok": False, "error": "empty script"}

    target_words = narr.narration_target_word_count(spec, target_sec)
    word_count = len(script.split())
    approval = write_approval(
        capture,
        {
            "status": "pending",
            "generating": False,
            "approved": False,
            "approvedAt": None,
            "generatedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "wordCount": word_count,
            "targetWordCount": target_words,
            "targetDurationSec": round(target_sec, 1),
            "source": source,
            "guidePath": str(capture / "NarrationVoiceGuide.md"),
            "scriptPath": str(capture / "DemoRecapFullNarration.txt"),
        },
    )
    narr.write_narration_voice_guide(
        capture, spec, milestones, target_sec=target_sec, script_preview=script
    )
    print(
        f"Narration review ready: {word_count} words (target ~{target_words}) → "
        f"{APPROVAL_NAME}",
        flush=True,
    )
    return {"ok": True, "approval": approval, "wordCount": word_count}


def main() -> int:
    ap = argparse.ArgumentParser(description="Generate narration script for dashboard review")
    ap.add_argument("capture", type=Path)
    ap.add_argument("--regen", action="store_true", help="Force new script")
    ap.add_argument("--cursor", action="store_true", help="Use Cursor API instead of local script")
    args = ap.parse_args()
    capture = args.capture.expanduser().resolve()
    if not capture.is_dir():
        print(f"ERROR: capture not found: {capture}", file=sys.stderr)
        return 1
    try:
        out = generate(capture, regen=args.regen, use_cursor=args.cursor)
    except Exception as exc:
        write_approval(
            capture,
            {
                "status": "error",
                "generating": False,
                "approved": False,
                "error": str(exc),
            },
        )
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    return 0 if out.get("ok") else 1


if __name__ == "__main__":
    raise SystemExit(main())
