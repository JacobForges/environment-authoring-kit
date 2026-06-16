"""Save vocal takes, performance video, and lyrics for music projects."""
from __future__ import annotations

import shutil
import subprocess
from pathlib import Path
from typing import Any

from music_project import music_dir, read_music_project, update_music_project

AUDIO_SUFFIXES = {".webm", ".wav", ".mp3", ".m4a", ".ogg"}
VIDEO_SUFFIXES = {".mp4", ".mov", ".webm", ".mkv"}


def _ffmpeg() -> str:
    for candidate in ("/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg"):
        if shutil.which(candidate) or Path(candidate).is_file():
            return candidate
    raise RuntimeError("ffmpeg not found — install with: brew install ffmpeg")


def save_vocal_take(project: Path, src: Path, *, suffix: str = ".webm") -> dict[str, Any]:
    if not read_music_project(project):
        raise ValueError("not a music project")
    mdir = music_dir(project)
    dest = mdir / f"vocal_raw{suffix}"
    shutil.copy2(src, dest)
    # Normalize to WAV for processing
    dry = mdir / "vocal_dry.wav"
    subprocess.run(
        [_ffmpeg(), "-y", "-i", str(dest), "-ar", "44100", "-ac", "1", str(dry)],
        check=True,
        capture_output=True,
        timeout=120,
    )
    update_music_project(project, {"status": {"vocalRecorded": True}})
    return {"ok": True, "vocalRaw": str(dest), "vocalDry": str(dry)}


def save_performance_video(project: Path, src: Path) -> dict[str, Any]:
    if not read_music_project(project):
        raise ValueError("not a music project")
    mdir = music_dir(project)
    dest = mdir / "performance.mp4"
    if src.suffix.lower() == ".mp4":
        shutil.copy2(src, dest)
    else:
        subprocess.run(
            [_ffmpeg(), "-y", "-i", str(src), "-c:v", "libx264", "-preset", "fast", "-c:a", "aac", str(dest)],
            check=True,
            capture_output=True,
            timeout=300,
        )
    return {"ok": True, "performance": str(dest)}


def save_lyrics(project: Path, text: str, *, fmt: str = "lines", bpm: int | None = None) -> dict[str, Any]:
    if not read_music_project(project):
        raise ValueError("not a music project")
    mdir = music_dir(project)
    lyrics_path = mdir / "lyrics.lrc"
    lyrics_path.write_text(text.strip() + "\n", encoding="utf-8")
    patch: dict[str, Any] = {"lyrics": text.strip(), "lyricsFormat": fmt}
    if bpm:
        patch["bpm"] = bpm
    update_music_project(project, patch)
    return {"ok": True, "lyrics": str(lyrics_path), "format": fmt}
