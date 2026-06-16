"""Shared helpers for hybrid screen-recording + milestone PNG recap."""
from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path
from typing import Any

W, H, FPS = 1920, 1080, 30
TARGET_DURATION_SEC = 510.0  # 8:30 target (portfolio sweet spot)
HYBRID_MAX_DURATION_SEC = 720.0  # 12:00 hard cap
_COMPOSE_FPS: int | None = None


def set_compose_fps(fps: int) -> None:
    global _COMPOSE_FPS
    _COMPOSE_FPS = max(24, min(120, int(fps)))


def compose_fps() -> int:
    return _COMPOSE_FPS if _COMPOSE_FPS else FPS


def find_ffmpeg() -> str:
    for c in ("ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise SystemExit("ffmpeg not found")


def find_ffprobe() -> str:
    for c in ("ffprobe", "/opt/homebrew/bin/ffprobe", "/usr/local/bin/ffprobe"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise SystemExit("ffprobe not found")


def probe_duration(path: Path, ffprobe: str | None = None) -> float:
    ffprobe = ffprobe or find_ffprobe()
    out = subprocess.run(
        [
            ffprobe, "-v", "error", "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1", str(path),
        ],
        capture_output=True,
        text=True,
        check=True,
    )
    return float(out.stdout.strip())


def encode_args(crf: int = 20, preset: str = "veryfast") -> list[str]:
    return [
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", str(crf),
        "-preset", preset, "-tune", "film", "-r", str(compose_fps()),
    ]


def vf_canvas() -> str:
    return (
        f"scale={W}:{H}:force_original_aspect_ratio=decrease,"
        f"pad={W}:{H}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1"
    )


def list_timelapse_frames(tl_dir: Path) -> list[Path]:
    frames = sorted(tl_dir.glob("tl_*.png"))
    if not frames:
        raise SystemExit(f"No timelapse PNGs in {tl_dir}")
    return frames


def pick_evenly(frames: list[Path], count: int) -> list[Path]:
    if len(frames) <= count:
        return frames
    idx = [int(i * (len(frames) - 1) / max(1, count - 1)) for i in range(count)]
    return [frames[i] for i in idx]


def pick_timelapse_chunk(frames: list[Path], start_frac: float, end_frac: float, max_frames: int = 72) -> list[Path]:
    n = len(frames)
    a = int(n * start_frac)
    b = max(a + 2, int(n * end_frac))
    chunk = frames[a:b]
    if len(chunk) <= max_frames:
        return chunk
    step = len(chunk) / max_frames
    return [chunk[int(i * step)] for i in range(max_frames)]


def resolve_screen_recording(capture: Path) -> Path:
    found = resolve_playthrough_recordings(capture)
    if found:
        return found[0]
    movs = sorted(Path.home().glob("Desktop/Screen Recording*.mov"), key=lambda p: p.stat().st_size, reverse=True)
    if not movs:
        raise SystemExit("No Screen Recording*.mov on Desktop — upload one in Hub or copy to capture/uploads/")
    uploads = capture / "uploads" / "build-screen.mov"
    uploads.parent.mkdir(parents=True, exist_ok=True)
    if not uploads.exists():
        uploads.symlink_to(movs[0].resolve())
    return movs[0]


def resolve_playthrough_recordings(capture: Path) -> list[Path]:
    """Play Mode screen recordings for presentation/hybrid compose (oldest → newest)."""
    out: list[Path] = []
    play_dir = capture / "uploads" / "playthroughs"
    if play_dir.is_dir():
        candidates: list[Path] = []
        for pattern in ("*.mp4", "*.mov"):
            candidates.extend(play_dir.glob(pattern))
        for p in sorted(candidates, key=lambda x: x.stat().st_mtime):
            try:
                if p.is_file() and p.stat().st_size > 64 * 1024:
                    out.append(p.resolve())
            except OSError:
                continue
    legacy = capture / "uploads" / "build-screen.mov"
    if legacy.is_file() or legacy.is_symlink():
        try:
            resolved = legacy.resolve()
            if resolved.is_file() and resolved.stat().st_size > 64 * 1024:
                if resolved not in out:
                    out.append(resolved)
        except OSError:
            pass
    return out


def scene_chapter_starts(mov: Path, ffmpeg: str, count: int = 4) -> list[float]:
    """Scene-change hints; fall back to even splits on the recording."""
    dur = probe_duration(mov)
    try:
        proc = subprocess.run(
            [
                ffmpeg, "-i", str(mov), "-filter:v", "select='gt(scene,0.014)',metadata=print",
                "-an", "-f", "null", "-",
            ],
            capture_output=True,
            text=True,
            timeout=120,
        )
        times = []
        for line in (proc.stderr or "").splitlines():
            m = re.search(r"pts_time:([\d.]+)", line)
            if m:
                times.append(float(m.group(1)))
        if len(times) >= count + 2:
            picks = [times[0]]
            for i in range(1, count):
                picks.append(times[int(i * len(times) / count)])
            return picks[:count]
    except (subprocess.TimeoutExpired, subprocess.CalledProcessError):
        pass
    return [dur * (i / count) * 0.85 + 2.0 for i in range(count)]


def chapter_clips(mov: Path, ffmpeg: str, count: int = 4) -> list[dict[str, Any]]:
    """Split screen recording into equal-duration chapter clips (full mov used once)."""
    dur = probe_duration(mov)
    labels = ["session_start", "flat_grid", "mid_terraform", "late_polish"]
    slice_sec = dur / count
    clips = []
    for i in range(count):
        start = i * slice_sec
        end = min(dur, (i + 1) * slice_sec)
        clips.append({
            "label": labels[i] if i < len(labels) else f"chapter_{i + 1}",
            "start": start,
            "duration": end - start,
            "chapter": labels[i].replace("_", " ").title() if i < len(labels) else f"Chapter {i + 1}",
        })
    return clips


def png_segment(ffmpeg: str, png: Path, out: Path, sec: float) -> None:
    subprocess.run(
        [
            ffmpeg, "-y", "-loop", "1", "-framerate", str(FPS), "-i", str(png),
            "-vf", vf_canvas(), "-t", f"{sec:.3f}", *encode_args(), "-an", str(out),
        ],
        check=True,
        capture_output=True,
    )


def card_segment(ffmpeg: str, png: Path, out: Path, sec: float) -> None:
    png_segment(ffmpeg, png, out, sec)


def screen_segment(ffmpeg: str, mov: Path, out: Path, start: float, duration: float) -> None:
    """Fixed-duration screen clip — letterbox only, no pan/zoom; freeze last frame if source is short."""
    duration = max(0.5, duration)
    vf = vf_canvas()
    subprocess.run(
        [
            ffmpeg, "-y", "-ss", f"{start:.3f}", "-i", str(mov),
            "-vf", vf, "-t", f"{duration:.3f}", *encode_args(), "-an", str(out),
        ],
        check=True,
        capture_output=True,
    )
    got = probe_duration(out)
    if got + 0.35 < duration:
        pad = duration - got
        padded = out.with_suffix(".pad.mp4")
        subprocess.run(
            [
                ffmpeg, "-y", "-i", str(out),
                "-vf", f"tpad=stop_mode=clone:stop_duration={pad:.3f}",
                "-t", f"{duration:.3f}", *encode_args(), "-an", str(padded),
            ],
            check=True,
            capture_output=True,
        )
        padded.replace(out)


def stable_slideshow_segment(
    ffmpeg: str,
    frames: list[Path],
    out: Path,
    sec: float,
    *,
    max_frames: int = 6,
) -> None:
    """
    Slow hold per still — no fast scrub (orbiting Scene view between captures reads as panicking camera).
    """
    if not frames:
        raise ValueError("stable_slideshow_segment needs frames")
    picked = frames if len(frames) <= max_frames else [
        frames[int(i * (len(frames) - 1) / (max_frames - 1))] for i in range(max_frames)
    ]
    if len(picked) == 1 or sec <= 0.5:
        png_segment(ffmpeg, picked[0], out, sec)
        return
    sec_per = sec / len(picked)
    rate = max(0.05, 1.0 / sec_per)
    w = out.parent / f"_stable_{out.stem}"
    seq = w / "seq"
    seq.mkdir(parents=True, exist_ok=True)
    for p in seq.glob("f_*.png"):
        p.unlink()
    for i, fr in enumerate(picked):
        dst = seq / f"f_{i + 1:06d}.png"
        dst.symlink_to(fr.resolve())
    subprocess.run(
        [
            ffmpeg, "-y", "-framerate", f"{rate:.4f}", "-i", str(seq / "f_%06d.png"),
            "-vf", vf_canvas(), "-t", f"{sec:.3f}", *encode_args(), "-an", str(out),
        ],
        check=True,
        capture_output=True,
    )


def timelapse_segment(ffmpeg: str, frames: list[Path], out: Path, sec: float) -> None:
    """Deprecated for hybrid — use stable_slideshow_segment (short) or png holds."""
    stable_slideshow_segment(ffmpeg, frames, out, sec, max_frames=min(6, len(frames)))


def concat_segments(ffmpeg: str, parts: list[Path], out: Path) -> None:
    lst = out.parent / "_concat_list.txt"
    lst.write_text("\n".join(f"file '{p.resolve()}'" for p in parts) + "\n")
    subprocess.run(
        [ffmpeg, "-y", "-f", "concat", "-safe", "0", "-i", str(lst), "-c", "copy", str(out)],
        check=True,
        capture_output=True,
    )


def ordered_hybrid_segment_paths(work: Path) -> list[Path]:
    """Intro → 4×(screen + 3 holds) → outro — must match compose-hybrid-recap.py."""
    names = ["seg_intro.mp4"]
    for ch in range(1, 5):
        names.append(f"seg_screen_{ch}.mp4")
        for h in range(1, 4):
            names.append(f"seg_hold_{ch}_{h}.mp4")
    names.append("seg_outro.mp4")
    paths = [work / n for n in names]
    missing = [p.name for p in paths if not p.is_file()]
    if missing:
        raise SystemExit(f"Missing hybrid segments in {work}: {', '.join(missing)}")
    return paths


def extend_segment_duration(ffmpeg: str, seg: Path, target_sec: float, *, ffprobe: str | None = None) -> float:
    """Freeze last frame (tpad) until segment is at least target_sec — speech-first sync."""
    ffprobe = ffprobe or find_ffprobe()
    cur = probe_duration(seg, ffprobe)
    target_sec = max(cur, target_sec)
    if target_sec <= cur + 0.05:
        return cur
    pad = target_sec - cur
    ext = seg.with_suffix(".ext.mp4")
    subprocess.run(
        [
            ffmpeg, "-y", "-i", str(seg),
            "-vf", f"tpad=stop_mode=clone:stop_duration={pad:.3f}",
            "-t", f"{target_sec:.3f}", *encode_args(), "-an", str(ext),
        ],
        check=True,
        capture_output=True,
    )
    ext.replace(seg)
    return probe_duration(seg, ffprobe)


def trim_segment_duration(ffmpeg: str, seg: Path, target_sec: float, *, ffprobe: str | None = None) -> float:
    """Trim segment when speech is shorter than composed slot — tight speech-first sync."""
    ffprobe = ffprobe or find_ffprobe()
    cur = probe_duration(seg, ffprobe)
    target_sec = max(0.2, target_sec)
    if cur <= target_sec + 0.05:
        return cur
    trimmed = seg.with_suffix(".trim.mp4")
    subprocess.run(
        [
            ffmpeg, "-y", "-i", str(seg),
            "-t", f"{target_sec:.3f}", *encode_args(), "-an", str(trimmed),
        ],
        check=True,
        capture_output=True,
    )
    trimmed.replace(seg)
    return probe_duration(seg, ffprobe)


def milestone_from_segment(i: int, seg: dict[str, Any]) -> dict[str, Any]:
    return {
        "i": i,
        "beatKind": seg.get("beatKind", "checkpoint"),
        "chapter": seg.get("chapter", ""),
        "phase": seg.get("phase", seg.get("kind", "")),
        "sub": seg.get("sub", ""),
        "subAction": seg.get("subAction", ""),
        "line1": seg.get("line1", ""),
        "line2": seg.get("line2", ""),
        "line3": seg.get("line3", ""),
        "teachingFocus": seg.get("teachingFocus", ""),
        "durationSec": seg.get("durationSec", 0),
        "kind": seg.get("kind", ""),
        "frame": seg.get("frame", 0),
    }


def write_timeline(capture: Path, segments: list[dict[str, Any]], spec_extra: dict[str, Any]) -> None:
    milestones = [milestone_from_segment(i, s) for i, s in enumerate(segments)]
    spec = {
        "recapMode": "hybrid_screencast",
        "targetDurationSec": TARGET_DURATION_SEC,
        "videoPlaybackFactor": 1.0,
        "narrationMode": "fullScript",
        "narrationSyncMode": "speech_first",
        "outputFps": FPS,
        "outputWidth": W,
        "outputHeight": H,
        "milestones": milestones,
        **spec_extra,
    }
    (capture / "DemoRecapTimeline.json").write_text(json.dumps(spec, indent=2) + "\n", encoding="utf-8")
    (capture / "HybridRecapManifest.json").write_text(
        json.dumps({"targetDurationSec": TARGET_DURATION_SEC, "segments": segments}, indent=2) + "\n",
        encoding="utf-8",
    )
