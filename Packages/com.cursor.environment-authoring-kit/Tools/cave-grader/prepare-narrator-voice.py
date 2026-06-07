#!/usr/bin/env python3
"""Set up your voice for demo recaps (macOS Personal Voice + optional reference WAV)."""
from __future__ import annotations

import argparse
import importlib.util
import json
import subprocess
import sys
from pathlib import Path

from envkit_paths import approved_dir

APPROVED = approved_dir()
SCRIPT = """\
This is my world-build recap. I'm walking through the terrain layout, the grid contract, \
and the seam passes. Calm pace. Like a director commentary, not a robot reading a spec sheet.\
"""


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--list-voices", action="store_true", help="List macOS say voices (incl. Personal Voice)")
    p.add_argument(
        "--list-helpers",
        action="store_true",
        help="List voice helper presets (humanSmooth, pitchAutotune, deEss, …)",
    )
    p.add_argument("--name", help="Exact Personal Voice name from say -v ?")
    p.add_argument("--record-reference", action="store_true", help="Record reference WAV with say")
    p.add_argument("--test", action="store_true", help="Synthesize test line with Personal Voice to Desktop")
    p.add_argument(
        "--test-line",
        help="Words for --test (default: narratorTestPhrase in ApprovedCards or built-in)",
    )
    p.add_argument(
        "--tune",
        action="store_true",
        help="Render raw/light/natural A/B samples on Desktop (tune-personal-voice.py)",
    )
    p.add_argument(
        "--install-autotune",
        action="store_true",
        help="pip install librosa soundfile numpy scipy pyrubberband",
    )
    args = p.parse_args()

    mod_path = Path(__file__).resolve().parent / "demo-recap-narrator.py"
    import importlib.util

    spec = importlib.util.spec_from_file_location("narr", mod_path)
    narr = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(narr)

    swift_tool = Path(__file__).resolve().parent / "personal-voice-speak.swift"

    if args.list_helpers:
        tools = Path(__file__).resolve().parent
        subprocess.run([sys.executable, str(tools / "personal-voice-helpers.py")], check=False)
        subprocess.run([sys.executable, str(tools / "personal-voice-ladder.py")], check=False)
        return 0

    if args.list_voices:
        print("=== AVSpeech voices (use this list for Personal Voice) ===")
        subprocess.run(["swift", str(swift_tool), "--list", "--personal-only"], check=False)
        print("\n=== say -v ? (Personal Voice often missing here) ===")
        for v in narr.list_macos_say_voices():
            if "jacob" in v.lower() or "personal" in v.lower():
                print(v)
        return 0

    if args.install_autotune:
        subprocess.run(
            [
                sys.executable,
                "-m",
                "pip",
                "install",
                "-q",
                "librosa>=0.10",
                "soundfile>=0.12",
                "numpy>=1.24",
                "scipy>=1.10",
                "pyrubberband>=0.3",
            ],
            check=False,
        )
        at_path = Path(__file__).resolve().parent / "personal-voice-autotune.py"
        at_spec = importlib.util.spec_from_file_location("at", at_path)
        at_mod = importlib.util.module_from_spec(at_spec)
        assert at_spec.loader
        at_spec.loader.exec_module(at_mod)
        print(f"Autotune ready: {at_mod.autotune_available()}")
        return 0

    if args.tune:
        tune = Path(__file__).resolve().parent / "tune-personal-voice.py"
        return subprocess.call([sys.executable, str(tune)])

    if args.test:
        name = args.name or "Jacob Adkins"
        if (APPROVED / narr.PERSONAL_VOICE_NAME_FILE).is_file():
            name = (APPROVED / narr.PERSONAL_VOICE_NAME_FILE).read_text(encoding="utf-8").strip().splitlines()[0]
        out = Path.home() / "Desktop" / "PersonalVoice-Test.wav"
        spec: dict = {
            "narratorEngine": "personal",
            "narratorRequirePersonal": True,
            "narratorPersonalVoice": name,
        }
        cards = APPROVED / "ApprovedCards.json"
        if cards.is_file():
            spec = narr.flatten_narrator_personal_settings(
                {**spec, **json.loads(cards.read_text(encoding="utf-8"))}
            )
        say_f = float(
            spec.get("narratorPersonalSayRate")
            or spec.get("narratorRate")
            or narr.personal_say_rate(spec)
        )
        print(f"Using sayRate={say_f} wpm (say -r {narr.personal_say_rate(spec)}) from ApprovedCards")
        wq = (spec.get("voiceHelpers") or {}).get("wordQueue") or {}
        if isinstance(wq, dict) and wq.get("enabled", True):
            print(
                f"Phrase queue: captureMode={narr.capture_queue_mode(spec)} "
                f"(portfolio={spec.get('narratorPortfolioMode', wq.get('portfolioFirst'))})",
                flush=True,
            )
        test_line = (args.test_line or spec.get("narratorTestPhrase") or "").strip()
        if not test_line:
            test_line = "DAD come here HURRY!"
        print(f"Test line: {test_line!r}")
        try:
            out.unlink(missing_ok=True)
        except OSError:
            pass
        ok = narr.speech_to_wav(
            test_line,
            out,
            voice=name,
            engine="personal",
            spec=spec,
        )
        if ok and out.is_file() and out.stat().st_size > 2048 and narr.wav_is_audible(out):
            print(f"Wrote {out} ({out.stat().st_size // 1024} KB) — play with: afplay {out}")
            return 0
        try:
            out.unlink(missing_ok=True)
        except OSError:
            pass
        print(
            "Test failed — no audible audio (Personal Voice not captured).\n"
            "  Run in Terminal.app (not Cursor): bash test-personal-voice.sh\n"
            "  swift personal-voice-speak.swift --authorize",
            file=sys.stderr,
        )
        print(
            "Then: DYLD_INSERT_LIBRARIES=./mysay.dylib say -v \"Jacob Adkins\" -o ~/Desktop/test.caf \"hello\"",
            file=sys.stderr,
        )
        return 1

    APPROVED.mkdir(parents=True, exist_ok=True)

    if args.name:
        (APPROVED / narr.PERSONAL_VOICE_NAME_FILE).write_text(args.name.strip() + "\n", encoding="utf-8")
        print(f"Saved Personal Voice name → {APPROVED / narr.PERSONAL_VOICE_NAME_FILE}")
        print("Re-run producer recap; narratorEngine can stay 'auto' or use 'personal'.")

    if args.record_reference:
        wav = APPROVED / "NarratorVoiceReference.wav"
        aiff = wav.with_suffix(".aiff")
        voice = args.name
        if not voice and (APPROVED / narr.PERSONAL_VOICE_NAME_FILE).is_file():
            voice = (APPROVED / narr.PERSONAL_VOICE_NAME_FILE).read_text(encoding="utf-8").strip().splitlines()[0]
        voice = voice or "Daniel"
        print(f"Recording reference with say -v {voice} …")
        subprocess.run(["say", "-v", voice, "-o", str(aiff), SCRIPT], check=True)
        subprocess.run(
            ["ffmpeg", "-y", "-i", str(aiff), "-ar", "48000", "-ac", "1", str(wav)],
            check=True,
        )
        aiff.unlink(missing_ok=True)
        print(f"Wrote {wav}")

    cards = APPROVED / "ApprovedCards.json"
    data: dict = {}
    if cards.is_file():
        data = json.loads(cards.read_text())
    data["narratorEngine"] = "auto"
    if args.name:
        data["narratorPersonalVoice"] = args.name.strip()
    if args.record_reference or (APPROVED / "NarratorVoiceReference.wav").is_file():
        data["narratorReferenceWav"] = str((APPROVED / "NarratorVoiceReference.wav").resolve())
    cards.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    print(f"Updated {cards}")
    print(
        "\nPersonal Voice on Mac (macOS 14+):\n"
        "  1. System Settings → Accessibility → Speech → Personal Voice\n"
        "     (scroll the Accessibility list; not under Privacy & Security)\n"
        "  2. Voice status must be Ready · allow Terminal when prompted\n"
        "  3. python3 prepare-narrator-voice.py --list-voices\n"
        "  4. python3 prepare-narrator-voice.py --test  → Desktop/PersonalVoice-Test.wav\n"
        "\nRun authorize-personal-voice.sh from the SAME app you use for recaps (Terminal vs Cursor).\n"
        "Docs: PERSONAL_VOICE_NARRATION.md, VOICE_HELPERS.md, open-personal-voice-settings.md\n"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
