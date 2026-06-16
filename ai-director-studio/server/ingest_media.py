"""Ingest arbitrary video/images into a director project timelapse folder."""
from __future__ import annotations

import json
import re
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from director_paths import atomic_write_text
from generic_timeline import build_generic_timeline_spec

MEDIA_INGEST_NAME = "MediaIngest.json"

IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tiff", ".tif"}
VIDEO_SUFFIXES = {".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi"}


def _ffmpeg() -> str:
    for candidate in ("/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg"):
        if shutil.which(candidate) or Path(candidate).is_file():
            return candidate
    raise RuntimeError("ffmpeg not found — install with: brew install ffmpeg")


def _next_frame_index(tl_dir: Path) -> int:
    existing = []
    for p in tl_dir.glob("tl_*.png"):
        stem = p.stem
        if stem.startswith("tl_") and stem[3:].isdigit():
            existing.append(int(stem[3:]))
    return (max(existing) + 1) if existing else 0


def ingest_images(project: Path, image_paths: list[Path]) -> dict[str, Any]:
    tl_dir = project / "timelapse"
    tl_dir.mkdir(parents=True, exist_ok=True)
    idx = _next_frame_index(tl_dir)
    saved: list[str] = []
    for src in image_paths:
        if not src.is_file():
            continue
        if src.suffix.lower() not in IMAGE_SUFFIXES:
            continue
        dest = tl_dir / f"tl_{idx:05d}.png"
        if src.suffix.lower() == ".png":
            shutil.copy2(src, dest)
        else:
            # Normalize to PNG via ffmpeg when available
            try:
                subprocess.run(
                    [_ffmpeg(), "-y", "-i", str(src), str(dest)],
                    check=True,
                    capture_output=True,
                    timeout=120,
                )
            except (subprocess.CalledProcessError, RuntimeError):
                shutil.copy2(src, dest.with_suffix(src.suffix))
                dest = dest.with_suffix(src.suffix)
        saved.append(dest.name)
        idx += 1
    return {"frames": saved, "count": len(saved)}


def ingest_video(
    project: Path,
    video_path: Path,
    *,
    fps: float = 2.0,
    max_frames: int = 600,
) -> dict[str, Any]:
    if not video_path.is_file():
        raise FileNotFoundError(str(video_path))
    if video_path.suffix.lower() not in VIDEO_SUFFIXES:
        raise ValueError(f"unsupported video type: {video_path.suffix}")

    tl_dir = project / "timelapse"
    tl_dir.mkdir(parents=True, exist_ok=True)
    start_idx = _next_frame_index(tl_dir)
    out_template = str(tl_dir / "tl_%05d.png")
    cmd = [
        _ffmpeg(),
        "-y",
        "-i",
        str(video_path),
        "-vf",
        f"fps={fps}",
        "-start_number",
        str(start_idx),
        "-frames:v",
        str(max_frames),
        out_template,
    ]
    subprocess.run(cmd, check=True, capture_output=True, timeout=3600)
    frames = sorted(tl_dir.glob("tl_*.png"))
    new_frames = [p.name for p in frames if p.name >= f"tl_{start_idx:05d}.png"]
    uploads = project / "uploads"
    uploads.mkdir(exist_ok=True)
    dest_video = uploads / video_path.name
    if not dest_video.exists():
        shutil.copy2(video_path, dest_video)
    return {"frames": new_frames, "count": len(new_frames), "source": str(dest_video)}


def _probe_video_duration_sec(video_path: Path) -> float | None:
    try:
        proc = subprocess.run(
            [_ffmpeg(), "-i", str(video_path)],
            capture_output=True,
            text=True,
            timeout=60,
        )
        match = re.search(r"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", proc.stderr or "")
        if not match:
            return None
        hours, minutes, seconds = match.groups()
        return int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    except (subprocess.SubprocessError, RuntimeError, ValueError):
        return None


def media_ingest_path(project: Path) -> Path:
    return project / MEDIA_INGEST_NAME


def read_media_ingest(project: Path) -> dict[str, Any] | None:
    path = media_ingest_path(project)
    if not path.is_file():
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return None


def frame_count(project: Path) -> int:
    tl_dir = project / "timelapse"
    if not tl_dir.is_dir():
        return 0
    return len(list(tl_dir.glob("tl_*.png")))


def ensure_timeline(
    project: Path,
    *,
    title: str = "",
    source: str = "",
    media_meta: dict[str, Any] | None = None,
) -> dict[str, Any]:
    tl_path = project / "DemoRecapTimeline.json"
    tl_dir = project / "timelapse"
    frames = sorted(tl_dir.glob("tl_*.png"))
    frame_n = len(frames)

    if tl_path.is_file():
        spec = json.loads(tl_path.read_text(encoding="utf-8"))
        spec["frameCount"] = frame_n
        if title:
            spec["title"] = title
    else:
        spec = build_generic_timeline_spec(frame_n, title=title or project.name)

    if source:
        spec["source"] = source
    if media_meta:
        spec["mediaIngest"] = media_meta
    atomic_write_text(tl_path, json.dumps(spec, indent=2) + "\n")
    return spec


def write_media_ingest(project: Path, payload: dict[str, Any]) -> dict[str, Any]:
    payload = dict(payload)
    payload["updatedAt"] = datetime.now(timezone.utc).isoformat()
    atomic_write_text(media_ingest_path(project), json.dumps(payload, indent=2) + "\n")
    return payload


def finalize_user_media_ingest(
    project: Path,
    *,
    title: str,
    ingest_result: dict[str, Any],
    uploaded_names: list[str],
) -> dict[str, Any]:
    """Persist user-media summary for DirectorSession Q&A and timeline."""
    project = project.expanduser().resolve()
    frame_n = ingest_result.get("frameCount") or frame_count(project)
    videos = ingest_result.get("videos") or []
    images = ingest_result.get("images") or {}
    has_video = bool(videos)
    has_images = bool(images and images.get("count"))
    duration_sec = 0.0
    video_paths: list[str] = []
    for entry in videos:
        src = str(entry.get("source") or "")
        if src:
            video_paths.append(src)
        dur = entry.get("durationSec")
        if isinstance(dur, (int, float)):
            duration_sec += float(dur)

    uploads_dir = project / "uploads"
    upload_paths = sorted(
        str(p)
        for p in uploads_dir.iterdir()
        if p.is_file() and not p.name.startswith(".")
    ) if uploads_dir.is_dir() else []

    media_meta = {
        "source": "user_media",
        "frameCount": frame_n,
        "durationSec": round(duration_sec, 2) if duration_sec else None,
        "estimatedDurationSec": round(frame_n * 0.35, 1) if frame_n else 0,
        "hasVideo": has_video,
        "hasImages": has_images,
        "uploadPaths": upload_paths,
        "videoPaths": video_paths,
        "uploadedFileNames": uploaded_names,
        "ingestedAt": datetime.now(timezone.utc).isoformat(),
    }

    write_media_ingest(project, media_meta)
    spec = ensure_timeline(project, title=title or project.name, source="user_media", media_meta=media_meta)

    session_path = project / "DirectorSession.json"
    if session_path.is_file():
        try:
            doc = json.loads(session_path.read_text(encoding="utf-8"))
            doc["mediaIngest"] = media_meta
            doc["footageSummary"] = {
                "source": "user_media",
                "frameCount": frame_n,
                "hasVideo": has_video,
                "hasImages": has_images,
                "durationSec": media_meta.get("durationSec") or media_meta.get("estimatedDurationSec"),
            }
            atomic_write_text(session_path, json.dumps(doc, indent=2) + "\n")
        except json.JSONDecodeError:
            pass

    return {"mediaIngest": media_meta, "timeline": spec, "frameCount": frame_n}


def ingest_files(project: Path, paths: list[Path], *, title: str = "") -> dict[str, Any]:
    images = [p for p in paths if p.suffix.lower() in IMAGE_SUFFIXES]
    videos = [p for p in paths if p.suffix.lower() in VIDEO_SUFFIXES]
    result: dict[str, Any] = {
        "project": str(project),
        "source": "user_media",
        "images": None,
        "videos": [],
        "hasVideo": False,
        "hasImages": False,
    }
    if images:
        result["images"] = ingest_images(project, images)
        result["hasImages"] = bool(result["images"].get("count"))
    for video in videos:
        entry = ingest_video(project, video)
        entry["durationSec"] = _probe_video_duration_sec(video)
        result["videos"].append(entry)
        result["hasVideo"] = True
    result["frameCount"] = frame_count(project)
    result["uploadedFileNames"] = [p.name for p in paths]
    finalized = finalize_user_media_ingest(
        project,
        title=title,
        ingest_result=result,
        uploaded_names=result["uploadedFileNames"],
    )
    result.update(finalized)
    result["mediaIngested"] = result["frameCount"] > 0
    return result
