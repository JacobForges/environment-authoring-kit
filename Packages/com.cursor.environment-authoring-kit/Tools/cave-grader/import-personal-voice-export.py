#!/usr/bin/env python3
"""
Import an exported macOS Personal Voice folder (Settings → Personal Voice → Export).

Export is usually a folder of .caf training clips. We merge a few into one reference WAV
for the recap pipeline and save the path in DemoRecapApproved.

Usage:
  python3 import-personal-voice-export.py ~/Downloads/"Jacob Adkins"
  python3 import-personal-voice-export.py   # prompts if one folder on Desktop/Downloads
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

from envkit_paths import approved_dir

APPROVED = approved_dir()
OUT_WAV = APPROVED / "NarratorVoiceReference.wav"
VOICE_NAME_FILE = APPROVED / "NarratorPersonalVoice.txt"
CARDS = APPROVED / "ApprovedCards.json"


def find_ffmpeg() -> str:
    for c in ("ffmpeg", "/opt/homebrew/bin/ffmpeg"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    return "ffmpeg"


def pick_export_dir(arg: str | None) -> Path:
    if arg:
        p = Path(arg).expanduser().resolve()
        if p.is_dir():
            return p
        raise SystemExit(f"Not a folder: {p}")
    for base in (Path.home() / "Downloads", Path.home() / "Desktop"):
        if not base.is_dir():
            continue
        hits = [d for d in base.iterdir() if d.is_dir() and list(d.glob("*.caf"))]
        if len(hits) == 1:
            return hits[0]
        for d in sorted(hits, key=lambda x: x.stat().st_mtime, reverse=True):
            if "jacob" in d.name.lower() or "personal" in d.name.lower() or "voice" in d.name.lower():
                return d
    raise SystemExit("Pass export folder path, e.g. python3 import-personal-voice-export.py ~/Downloads/Jacob\\ Adkins")


def merge_cafs(cafs: list[Path], out_wav: Path) -> None:
    ffmpeg = find_ffmpeg()
    lst = out_wav.parent / "_caf_list.txt"
    lst.write_text("".join(f"file '{p.resolve()}'\n" for p in cafs), encoding="utf-8")
    tmp = out_wav.parent / "_caf_merged.caf"
    subprocess.run(
        [ffmpeg, "-y", "-f", "concat", "-safe", "0", "-i", str(lst), "-c", "copy", str(tmp)],
        check=True,
        capture_output=True,
    )
    subprocess.run(
        [ffmpeg, "-y", "-i", str(tmp), "-ar", "48000", "-ac", "1", str(out_wav)],
        check=True,
        capture_output=True,
    )
    tmp.unlink(missing_ok=True)
    lst.unlink(missing_ok=True)


def main() -> int:
    export_dir = pick_export_dir(sys.argv[1] if len(sys.argv) > 1 else None)
    cafs = sorted(export_dir.glob("*.caf"))
    if not cafs:
        raise SystemExit(f"No .caf files in {export_dir}")

    APPROVED.mkdir(parents=True, exist_ok=True)
    # Use first ~30s of clips for a stable reference timbre
    pick = cafs[: min(12, len(cafs))]
    merge_cafs(pick, OUT_WAV)

    voice_name = export_dir.name.strip()
    if voice_name.lower().endswith(".voice"):
        voice_name = voice_name[:-6]
    VOICE_NAME_FILE.write_text(voice_name + "\n", encoding="utf-8")

    cards: dict = {}
    if CARDS.is_file():
        cards = json.loads(CARDS.read_text(encoding="utf-8"))
    cards["narratorPersonalVoice"] = voice_name
    cards["narratorReferenceWav"] = str(OUT_WAV.resolve())
    cards["narratorEngine"] = "personal"
    CARDS.write_text(json.dumps(cards, indent=2) + "\n", encoding="utf-8")

    print(f"Imported {len(pick)} clips from {export_dir}")
    print(f"Reference WAV: {OUT_WAV}")
    print(f"Voice name:    {voice_name}")
    print("\nNext — run recap from **Terminal.app** (Personal Voice is allowed there in your screenshot):")
    print(
        "  python3 ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/"
        "run-producer-recap.py ~/Hub/Library/EnvironmentKit/DemoCapture/20260602-174525 --preview"
    )
    print("\nIf you use Cursor's terminal, run authorize-personal-voice.sh there and allow **Cursor** too.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
