#!/usr/bin/env python3
"""Offline polish for Deep Train Academy intro narration."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def run_af(in_wav: Path, out_wav: Path, af: str) -> None:
    subprocess.run(
        [ffmpeg(), "-y", "-i", str(in_wav), "-af", af, "-ar", "48000", "-ac", "2", str(out_wav)],
        check=True,
        capture_output=True,
        text=True,
    )


def apply_true_color(in_wav: Path, work: Path) -> Path:
    """Natural timbre — documentary clarity without harsh treble or chest boom."""
    out = work / "true_color.wav"
    af = ",".join(
        [
            "highpass=f=72",
            "lowpass=f=12000",
            "equalizer=f=180:width_type=o:width=1.1:g=-1.2",
            "equalizer=f=3200:width_type=o:width=1.4:g=1.1",
            "equalizer=f=5200:width_type=o:width=1.8:g=0.45",
            "acompressor=threshold=-24dB:ratio=1.35:attack=18:release=180:makeup=1.05",
            "alimiter=limit=0.98:attack=40:release=160",
        ]
    )
    run_af(in_wav, out, af)
    print("trueColor: neutral speech contour", flush=True)
    return out


def apply_spatial_sound(in_wav: Path, work: Path) -> Path:
    """Subtle Soundcore-style width for headphones — not wide gimmick."""
    out = work / "spatial.wav"
    af = ",".join(
        [
            "pan=0.5|c0=0.5*c0+0.5*c1|c1=0.5*c1+0.5*c0",
            "aecho=0.82:0.86:7|12:0.1|0.06",
            "extrastereo=m=1.18:i=0.04",
            "alimiter=limit=0.98:attack=40:release=160",
        ]
    )
    run_af(in_wav, out, af)
    print("spatialSound: bass-centered width + light room", flush=True)
    return out


def export_ogg(wav: Path, out_ogg: Path) -> None:
    subprocess.run(
        [ffmpeg(), "-y", "-i", str(wav), "-c:a", "libvorbis", "-q:a", "6", str(out_ogg)],
        check=True,
        capture_output=True,
        text=True,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Polish intro narration WAV/OGG")
    parser.add_argument("input")
    parser.add_argument("-o", "--output")
    parser.add_argument("--skip-spatial", action="store_true")
    args = parser.parse_args()

    src = Path(args.input).expanduser().resolve()
    if not src.exists():
        print(f"missing input: {src}", file=sys.stderr)
        return 1

    out = Path(args.output).expanduser().resolve() if args.output else src
    work = out.parent / f".intro_vo_polish_{out.stem}"
    work.mkdir(parents=True, exist_ok=True)

    try:
        stage = apply_true_color(src, work)
        if not args.skip_spatial:
            stage = apply_spatial_sound(stage, work)

        if out.suffix.lower() == ".ogg":
            export_ogg(stage, out)
        else:
            shutil.copy2(stage, out)
    finally:
        shutil.rmtree(work, ignore_errors=True)

    print(f"done → {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
