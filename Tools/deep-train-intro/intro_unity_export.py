#!/usr/bin/env python3
"""Export intro mix for Unity Resources — avoids Opus OGG (FSBTool rejects it)."""

from __future__ import annotations

import shutil
import subprocess
from pathlib import Path


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def export_unity_audio(wav: Path, resources_dir: Path, stem: str) -> Path:
    """Write Unity-importable audio beside Resources; remove stale sibling formats."""
    resources_dir.mkdir(parents=True, exist_ok=True)
    mp3 = resources_dir / f"{stem}.mp3"
    ogg = resources_dir / f"{stem}.ogg"

    attempts: list[tuple[list[str], Path]] = [
        (["-c:a", "libmp3lame", "-b:a", "192k"], mp3),
        (["-c:a", "libvorbis", "-q:a", "6"], ogg),
        (["-c:a", "vorbis", "-strict", "-2", "-q:a", "6"], ogg),
    ]

    last_err = ""
    for args, dest in attempts:
        cmd = [ffmpeg(), "-y", "-i", str(wav), *args, str(dest)]
        proc = subprocess.run(cmd, capture_output=True, text=True)
        if proc.returncode == 0 and dest.is_file() and dest.stat().st_size > 256:
            for stale in (mp3, ogg):
                if stale != dest and stale.exists():
                    stale.unlink()
            return dest
        last_err = proc.stderr or proc.stdout or f"exit {proc.returncode}"

    raise RuntimeError(f"Unity audio export failed for {stem}: {last_err[:500]}")


def export_listen_m4a(wav: Path, m4a: Path) -> None:
    m4a.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [ffmpeg(), "-y", "-i", str(wav), "-c:a", "aac", "-b:a", "192k", str(m4a)],
        check=True,
        capture_output=True,
    )
