#!/usr/bin/env python3
"""Build wizard UI chapter video from in-browser frame captures."""
from __future__ import annotations

import json
import subprocess
from pathlib import Path


def list_wizard_frames(run_dir: Path) -> list[Path]:
    frames_dir = run_dir / "wizard" / "frames"
    if not frames_dir.is_dir():
        return []
    return sorted(frames_dir.glob("wf_*.png"))


def resolve_wizard_chapter_mp4(run_dir: Path, ffmpeg: str, *, fps: int = 12) -> Path | None:
    existing = run_dir / "wizard" / "wizard-chapter.mp4"
    if existing.is_file():
        return existing
    return build_wizard_chapter_mp4(run_dir, ffmpeg, fps=fps)


def build_wizard_chapter_mp4(run_dir: Path, ffmpeg: str, *, fps: int = 12) -> Path | None:
    frames = list_wizard_frames(run_dir)
    if len(frames) < 2:
        return None
    out = run_dir / "wizard" / "wizard-chapter.mp4"
    out.parent.mkdir(parents=True, exist_ok=True)
    pattern = str((run_dir / "wizard" / "frames" / "wf_%06d.png").resolve())
    # Subtle Ken Burns + letterbox — keeps wizard UI readable while feeling cinematic.
    vf = (
        "scale=1920:1080:force_original_aspect_ratio=decrease,"
        "pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=0x12141a,"
        "zoompan=z='min(zoom+0.00055,1.06)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':"
        f"d=1:s=1920x1080:fps={fps}"
    )
    cmd = [
        ffmpeg,
        "-y",
        "-framerate",
        str(fps),
        "-i",
        pattern,
        "-vf",
        vf,
        "-c:v",
        "libx264",
        "-pix_fmt",
        "yuv420p",
        "-movflags",
        "+faststart",
        str(out),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0 or not out.is_file():
        return None
    meta = {
        "wizardChapterMp4": str(out),
        "wizardFrameCount": len(frames),
        "fps": fps,
    }
    (run_dir / "wizard" / "wizard-chapter-meta.json").write_text(
        json.dumps(meta, indent=2) + "\n", encoding="utf-8"
    )
    return out


def prepend_wizard_to_timeline(run_dir: Path, ffmpeg: str) -> bool:
    """Ensure DemoRecapTimeline.json notes wizard chapter when frames exist."""
    tl_path = run_dir / "DemoRecapTimeline.json"
    if not tl_path.is_file():
        return False
    try:
        tl = json.loads(tl_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return False
    wizard_mp4 = resolve_wizard_chapter_mp4(run_dir, ffmpeg)
    if wizard_mp4 is None:
        return False
    tl.setdefault("wizardChapter", {})
    tl["wizardChapter"] = {
        "mp4": str(wizard_mp4.relative_to(run_dir)),
        "title": "AI Planning Session",
        "chaptersManifest": "DemoRecapChapters.json",
    }
    chapters_path = run_dir / "DemoRecapChapters.json"
    if chapters_path.is_file():
        try:
            tl["chapters"] = json.loads(chapters_path.read_text(encoding="utf-8")).get("chapters") or []
        except (json.JSONDecodeError, OSError):
            pass
    tl_path.write_text(json.dumps(tl, indent=2) + "\n", encoding="utf-8")
    return True
