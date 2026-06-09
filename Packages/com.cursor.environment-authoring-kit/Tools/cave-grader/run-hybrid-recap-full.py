#!/usr/bin/env python3
"""
Full hybrid recap pipeline:
  1) compose ~8:30 silent video (screen B-roll + PNG holds, 12:00 max)
  2) Cursor AI full narration script from assembled timeline + exact video duration
  3) optional Personal Voice mux (--narration-only)
"""
from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / file)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def _run_hybrid_mux(capture: Path) -> int:
    r = subprocess.run(
        [sys.executable, str(_TOOLS / "mux-hybrid-recap-narration.py"), str(capture)],
        cwd=str(_TOOLS),
    )
    return r.returncode


def main() -> int:
    if len(sys.argv) < 2:
        print(
            "Usage: python3 run-hybrid-recap-full.py /path/to/DemoCapture/<timestamp> "
            "[--skip-compose] [--regen-script] [--narration-only] [--with-narration]",
            file=sys.stderr,
        )
        return 1

    capture = Path(sys.argv[1]).expanduser().resolve()
    flags = set(sys.argv[2:])
    skip_compose = "--skip-compose" in flags
    regen = "--regen-script" in flags or "--regen-narration-script" in flags
    narration_only = "--narration-only" in flags

    if not skip_compose and not narration_only:
        print("=== Step 1: Compose hybrid silent video (~8:30 target, 12:00 max) ===", flush=True)
        r = subprocess.run(
            [sys.executable, str(_TOOLS / "compose-hybrid-recap.py"), str(capture)],
            cwd=str(_TOOLS),
        )
        if r.returncode != 0:
            return r.returncode

    silent = capture / "HybridRecapPresentation-SILENT.mp4"
    if not silent.is_file():
        print(f"Missing {silent}", file=sys.stderr)
        return 1

    tl_path = capture / "DemoRecapTimeline.json"
    if not tl_path.is_file():
        print(f"Missing {tl_path}", file=sys.stderr)
        return 1

    if narration_only:
        full_json = capture / "DemoRecapFullNarration.json"
        if not full_json.is_file():
            print(
                f"Missing {full_json} — run without --narration-only to generate Cursor script",
                file=sys.stderr,
            )
            return 1
        print("=== Personal Voice narration mux (hybrid speech-first) ===", flush=True)
        return _run_hybrid_mux(capture)

    spec = json.loads(tl_path.read_text(encoding="utf-8"))
    milestones = spec.get("milestones") or []
    director = _load("director", "demo-recap-cursor-director.py")
    narr = _load("narr", "demo-recap-narrator.py")
    hub = director.resolve_hub()
    director.load_hub_ai_env(hub)

    spec = narr.flatten_narrator_personal_settings(spec)
    try:
        silent_dur = narr.probe_duration(silent)
        target = float(
            spec.get("hybridTargetDurationSec")
            or spec.get("fullNarrationTargetSec")
            or spec.get("targetDurationSec")
            or silent_dur
        )
        dur = target
        spec["fullNarrationTargetSec"] = dur
        spec["targetDurationSec"] = dur
        print(
            f"Script target {dur:.0f}s (silent compose {silent_dur:.0f}s, "
            f"max {float(spec.get('hybridMaxDurationSec', 720)):.0f}s)",
            flush=True,
        )
    except Exception as exc:
        print(f"Could not probe video: {exc}", file=sys.stderr)
        dur = float(spec.get("hybridTargetDurationSec") or spec.get("targetDurationSec") or 510)

    print(f"=== Step 2: Cursor full narration script ({dur:.1f}s video) ===", flush=True)
    full_json = capture / "DemoRecapFullNarration.json"
    if regen or not full_json.is_file():
        if director.has_cursor_api_key():
            script = director.apply_cursor_full_narration_script(
                hub,
                capture,
                milestones,
                spec,
                build_mode=str(spec.get("buildMode") or "FullWorld hybrid screencast recap"),
            )
        else:
            print("No CURSOR_API_KEY — writing local milestone script", flush=True)
            script = narr.write_local_full_narration_script(capture, milestones, spec, target_sec=dur)
    else:
        script = narr.resolve_full_narration_script(capture, spec, milestones)
        print(f"Reusing {full_json.name}")

    if not script:
        print("ERROR: narration script empty", file=sys.stderr)
        return 1

    tl_path.write_text(json.dumps({**spec, "milestones": milestones}, indent=2) + "\n", encoding="utf-8")
    out_folder = Path.home() / "Desktop" / "HybridRecap-Full"
    out_folder.mkdir(exist_ok=True)
    (out_folder / "DemoRecapFullNarration.txt").write_text(script + "\n", encoding="utf-8")
    if full_json.is_file():
        shutil.copy2(full_json, out_folder / "DemoRecapFullNarration.json")

    print(f"Script: {capture / 'DemoRecapFullNarration.txt'}")
    words = len(script.split())
    wpm = narr.personal_say_rate(spec)
    density = float(spec.get("narrationScriptDensityMultiplier", 1.0))
    word_budget = max(200, int((dur / 60.0) * wpm * 0.92 * density))
    print(f"Words: {words} (budget ~{word_budget} for {dur:.0f}s @ {wpm} wpm)")
    if words > int(word_budget * 1.15):
        print(
            f"WARNING: script is {words - word_budget} words over budget — "
            "video may exceed hybridMaxDurationSec",
            flush=True,
        )

    if "--with-narration" in flags:
        print("=== Step 3: Personal Voice narration mux (hybrid speech-first) ===", flush=True)
        return _run_hybrid_mux(capture)

    print("\nNext — mux Personal Voice:", flush=True)
    print(f"  cd {_TOOLS}", flush=True)
    print(
        f"  python3 run-hybrid-recap-full.py {capture} --skip-compose --narration-only",
        flush=True,
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
