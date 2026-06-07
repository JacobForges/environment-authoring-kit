#!/usr/bin/env python3
"""One-screen health check for a demo capture + suggested next commands."""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
from envkit_paths import approved_dir

APPROVED = approved_dir()
DESKTOP_PREVIEW = Path.home() / "Desktop/DemoRecap-Card-Preview"


def load_mod(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, TOOLS / file)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def main() -> int:
    capture = Path(sys.argv[1]).expanduser().resolve() if len(sys.argv) > 1 else None
    if not capture or not capture.is_dir():
        print("Usage: python3 recap-doctor.py <capture_folder>")
        return 1

    print(f"Capture: {capture}\n")
    frames = sorted((capture / "timelapse").glob("tl_*.png"))
    print(f"  Timelapse frames: {len(frames)}")

    for label, p in (
        ("ApprovedIntro", capture / "ApprovedIntro.png"),
        ("ApprovedOutro", capture / "ApprovedOutro.png"),
        ("Timeline", capture / "DemoRecapTimeline.json"),
        ("OpenCV map", capture / "DemoRecapOpenCV.json"),
    ):
        print(f"  {label}: {'OK' if p.is_file() else 'missing'}")

    for label, p in (
        ("Old recap", capture / "DemoRecap.mp4"),
        ("Producer recap", capture / "DemoRecapPresentation.mp4"),
        ("Desktop preview", DESKTOP_PREVIEW / "DirectorPreview.mp4"),
    ):
        if p.is_file():
            mb = p.stat().st_size / (1024 * 1024)
            print(f"  {label}: {p.name} ({mb:.1f} MB)")

    tl = capture / "DemoRecapTimeline.json"
    if tl.is_file():
        spec = json.loads(tl.read_text())
        ms = spec.get("milestones", [])
        print(f"\nTimeline: producer v{spec.get('producerVersion', '?')}, {len(ms)} milestones")
        print(f"  Holds: {spec.get('milestoneHoldSec')}s / sub {spec.get('subbeatHoldSec')}s")
        try:
            narr = load_mod("narr", "demo-recap-narrator.py")
            flat = narr.flatten_narrator_personal_settings(spec)
            print(
                f"  Narrator: {flat.get('narratorEngine')} "
                f"sayRate={narr.personal_say_rate(flat)} wpm "
                f"(ApprovedCards narratorPersonalSettings.sayRate)"
            )
        except Exception:
            print(f"  Narrator: {spec.get('narratorEngine')} rate={spec.get('narratorRate')}")

    print("\n--- Personal Voice ---")
    name_file = APPROVED / "NarratorPersonalVoice.txt"
    if name_file.is_file():
        print(f"  Configured name: {name_file.read_text().strip().splitlines()[0]}")
    if (TOOLS / "mysay.dylib").is_file():
        print("  mysay.dylib: OK (say can export Personal Voice to file)")
    else:
        print("  mysay.dylib: missing (build from mysay.c in cave-grader)")

    try:
        narr = load_mod("narr", "demo-recap-narrator.py")
        if narr.edge_tts_available():
            print("  edge-tts: installed")
        else:
            print("  edge-tts: not in this python3")
    except Exception as exc:
        print(f"  narrator module: {exc}")

    cards = APPROVED / "ApprovedCards.json"
    if cards.is_file():
        print(f"  ApprovedCards: {cards}")
    print(f"  Docs: {TOOLS}/PERSONAL_VOICE_NARRATION.md")

    print("\n--- Suggested commands ---")
    print(f"  python3 {TOOLS}/recap-doctor.py {capture}")
    print(f"  bash {TOOLS}/run-recap-terminal.sh {capture} --preview")
    print(f"  python3 {TOOLS}/run-producer-recap.py {capture} --preview --install-deps")
    print(f"  python3 {TOOLS}/prepare-narrator-voice.py --test")
    if not (capture / "DemoRecapPresentation.mp4").is_file():
        print(f"  python3 {TOOLS}/run-producer-recap.py {capture}   # full encode")
    print(f"\n  open {DESKTOP_PREVIEW}/DirectorPreview.mp4")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
