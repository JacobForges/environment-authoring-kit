#!/usr/bin/env python3
"""Hi-fi spatial 3D polish — headphone cave stage for intro preview."""

from __future__ import annotations

import shutil
import subprocess
from pathlib import Path


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def spatial_cave_af() -> str:
    """Wide cave stage — depth, elevation, bass-centered mono."""
    return ",".join(
        [
            "stereotools=balance_in=0.02:balance_out=0.06:slev=1.15",
            "extrastereo=m=1.32",
            "aecho=0.87:0.84:11|18|30:0.12|0.08|0.05",
            "equalizer=f=140:width_type=o:width=1.0:g=1.2",
            "equalizer=f=6500:width_type=o:width=2.0:g=-1.4",
            "acompressor=threshold=-20dB:ratio=1.25:attack=25:release=220:makeup=1.04",
            "alimiter=limit=0.97:attack=35:release=160",
        ]
    )


def narrator_spatial_af() -> str:
    """Narrator slightly forward-center with chest warmth."""
    return ",".join(
        [
            "highpass=f=75",
            "lowpass=f=11000",
            "equalizer=f=120:width_type=o:width=1.0:g=2.0",
            "equalizer=f=220:width_type=o:width=1.1:g=1.4",
            "equalizer=f=3800:width_type=o:width=1.5:g=-0.8",
            "pan=stereo|c0=0.94*c0|c1=0.94*c1",
            "stereotools=balance_in=0.0:balance_out=0.0",
            "extrastereo=m=1.12",
            "aecho=0.9:0.88:6|10:0.08|0.05",
            "acompressor=threshold=-21dB:ratio=1.3:attack=18:release=190:makeup=1.05",
        ]
    )


def apply_spatial_master(in_wav: Path, out_wav: Path) -> None:
    subprocess.run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(in_wav),
            "-af",
            spatial_cave_af(),
            "-ar",
            "48000",
            "-ac",
            "2",
            str(out_wav),
        ],
        check=True,
    )


def polish_narrator(in_wav: Path, out_wav: Path) -> None:
    subprocess.run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(in_wav),
            "-af",
            narrator_spatial_af(),
            "-ar",
            "48000",
            "-ac",
            "2",
            str(out_wav),
        ],
        check=True,
    )
