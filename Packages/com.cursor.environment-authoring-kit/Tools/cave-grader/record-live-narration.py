#!/usr/bin/env python3
"""
Use YOUR real voice (mic recording) on the approval preview — not Apple sentence-queue TTS.

Workflow:
  1) Read PersonalVoice-ReadAlong-Script.txt (Voice Memos or QuickTime → Desktop)
  2) python3 record-live-narration.py --import ~/Desktop/Jacob-Recap-Voice-Take.m4a
  3) python3 record-live-narration.py --mux-preview

Apple Personal Voice cannot be trained from a custom script — Settings uses fixed phrases.
This path bypasses say+mysay entirely and muxes your recording onto the preview video.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

from envkit_paths import approved_dir

_TOOLS = Path(__file__).resolve().parent
PREVIEW_SILENT = Path.home() / "Desktop" / "HybridRecap-ApprovalPreview" / "HybridRecap-ApprovalPreview-SILENT.mp4"
PREVIEW_OUT = Path.home() / "Desktop" / "HybridRecap-ApprovalPreview" / "HybridRecap-ApprovalPreview-LIVE.mp4"
READ_SCRIPT = approved_dir() / "PersonalVoice-ReadAlong-Script.txt"
LIVE_WAV = approved_dir() / "Jacob-Recap-Live-Narration.wav"


def find_ffmpeg() -> str:
    for c in ("ffmpeg", "/opt/homebrew/bin/ffmpeg"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise SystemExit("ffmpeg not found")


def find_ffprobe() -> str:
    for c in ("ffprobe", "/opt/homebrew/bin/ffprobe"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise SystemExit("ffprobe not found")


def probe_duration(path: Path, ffprobe: str) -> float:
    out = subprocess.run(
        [
            ffprobe, "-v", "error", "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1", str(path),
        ],
        capture_output=True, text=True, check=True,
    )
    return float(out.stdout.strip())


def import_audio(src: Path) -> Path:
    src = src.expanduser().resolve()
    if not src.is_file():
        raise SystemExit(f"Missing recording: {src}")
    ffmpeg = find_ffmpeg()
    LIVE_WAV.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            ffmpeg, "-y", "-i", str(src),
            "-af", "highpass=f=80,afftdn=nf=-28,acompressor=threshold=-22dB:ratio=1.4:attack=25:release=180:makeup=1.08,loudnorm=I=-16:TP=-1.5:LRA=9",
            "-ar", "48000", "-ac", "2", str(LIVE_WAV),
        ],
        check=True, capture_output=True,
    )
    print(f"Live narration WAV: {LIVE_WAV}")
    return LIVE_WAV


def mux_preview(wav: Path | None = None) -> int:
    wav = wav or LIVE_WAV
    if not PREVIEW_SILENT.is_file():
        print(f"Missing {PREVIEW_SILENT} — run compose-hybrid-approval-preview.py first", file=sys.stderr)
        return 1
    if not wav.is_file():
        print(f"Missing {wav} — record yourself, then: record-live-narration.py --import <your.m4a>", file=sys.stderr)
        return 1
    ffmpeg, ffprobe = find_ffmpeg(), find_ffprobe()
    vid_dur = probe_duration(PREVIEW_SILENT, ffprobe)
    aud_dur = probe_duration(wav, ffprobe)
    print(f"Video {vid_dur:.1f}s · your voice {aud_dur:.1f}s")
    subprocess.run(
        [
            ffmpeg, "-y", "-i", str(PREVIEW_SILENT), "-i", str(wav),
            "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
            "-shortest", str(PREVIEW_OUT),
        ],
        check=True, capture_output=True,
    )
    print(f"Live voice preview: {PREVIEW_OUT}")
    if sys.platform == "darwin":
        subprocess.run(["open", str(PREVIEW_OUT)], check=False)
    return 0


def main() -> int:
    args = sys.argv[1:]
    if "--print-script" in args or not args:
        if READ_SCRIPT.is_file():
            print(READ_SCRIPT.read_text(encoding="utf-8"))
            print(f"\nScript file: {READ_SCRIPT}")
        print(
            "\nRecord in Voice Memos → export to Desktop → "
            "python3 record-live-narration.py --import ~/Desktop/YourTake.m4a --mux-preview"
        )
        return 0
    wav: Path | None = None
    if "--import" in args:
        i = args.index("--import")
        if i + 1 >= len(args):
            raise SystemExit("--import requires a path to .m4a / .wav")
        wav = import_audio(Path(args[i + 1]))
    if "--mux-preview" in args:
        return mux_preview(wav)
    print("Usage: record-live-narration.py [--print-script] | --import <file> --mux-preview")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
