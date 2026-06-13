#!/usr/bin/env python3
"""Mux silent intro.mp4 + baked intro_vo using narration-led schedule → shareable MP4."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT_DIR = Path(__file__).resolve().parent
INTRO_MP4 = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro.mp4"
CUES_JSON = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_audio_cues.json"
AUDIO_CANDIDATES = [
    SCRIPT_DIR / "work/intro_vo_full_mix.wav",
    ROOT / "Assets/Resources/DeepTrainAcademy/Intro/intro_vo.mp3",
    SCRIPT_DIR / "listen/intro_vo_full.m4a",
]
OUT_DIR = SCRIPT_DIR / "listen"
OUT_MP4 = OUT_DIR / "intro_full_with_narration.mp4"
WORK = SCRIPT_DIR / "work" / "share_mux"


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def ffprobe() -> str:
    return shutil.which("ffprobe") or "/opt/homebrew/bin/ffprobe"


def probe_duration(path: Path) -> float:
    out = subprocess.check_output(
        [
            ffprobe(),
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        text=True,
    ).strip()
    return float(out or 0.0)


def run(cmd: list[str]) -> None:
    subprocess.run(cmd, check=True)


def pick_audio() -> Path:
    for path in AUDIO_CANDIDATES:
        if path.is_file() and path.stat().st_size > 1024:
            return path
    raise FileNotFoundError(
        "No baked intro audio — run ./Tools/deep-train-intro/render-intro-vo-full.sh first."
    )


def render_black_segment(out: Path, duration_s: float) -> None:
    run(
        [
            ffmpeg(),
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"color=c=0x020408:s=1920x1080:r=30:d={duration_s:.3f}",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-preset",
            "veryfast",
            "-crf",
            "18",
            str(out),
        ]
    )


def render_beat_hold(out: Path, mp4: Path, content_start: float, duration_s: float) -> None:
    frame = out.with_suffix(".png")
    run(
        [
            ffmpeg(),
            "-y",
            "-ss",
            f"{content_start:.3f}",
            "-i",
            str(mp4),
            "-frames:v",
            "1",
            str(frame),
        ]
    )
    run(
        [
            ffmpeg(),
            "-y",
            "-loop",
            "1",
            "-i",
            str(frame),
            "-t",
            f"{duration_s:.3f}",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-r",
            "30",
            "-preset",
            "veryfast",
            "-crf",
            "18",
            str(out),
        ]
    )
    frame.unlink(missing_ok=True)


def render_gap_segment(
    out: Path,
    mp4: Path,
    content_start: float,
    content_end: float,
    wall_duration_s: float,
) -> None:
    content_dur = max(content_end - content_start, 0.05)
    pts_factor = wall_duration_s / content_dur
    run(
        [
            ffmpeg(),
            "-y",
            "-ss",
            f"{content_start:.3f}",
            "-to",
            f"{content_end:.3f}",
            "-i",
            str(mp4),
            "-an",
            "-vf",
            f"setpts=PTS*{pts_factor:.6f},fps=30",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-preset",
            "veryfast",
            "-crf",
            "18",
            str(out),
        ]
    )


def concat_segments(parts: list[Path], out: Path) -> None:
    list_file = WORK / "concat.txt"
    list_file.write_text("\n".join(f"file '{p.resolve()}'" for p in parts) + "\n", encoding="utf-8")
    run(
        [
            ffmpeg(),
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            str(list_file),
            "-c",
            "copy",
            str(out),
        ]
    )


def mux_audio(video: Path, audio: Path, out: Path, total_s: float) -> None:
    run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(video),
            "-i",
            str(audio),
            "-map",
            "0:v:0",
            "-map",
            "1:a:0",
            "-c:v",
            "copy",
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            "-shortest",
            "-t",
            f"{total_s:.3f}",
            str(out),
        ]
    )


def main() -> int:
    if not INTRO_MP4.is_file():
        print(f"error: missing {INTRO_MP4}", file=sys.stderr)
        return 1
    if not CUES_JSON.is_file():
        print(f"error: missing {CUES_JSON}", file=sys.stderr)
        return 1

    cues = json.loads(CUES_JSON.read_text(encoding="utf-8"))
    schedule = cues.get("schedule") or []
    if cues.get("syncMode") != "narration" or not schedule:
        print("error: intro_audio_cues.json needs syncMode=narration + schedule", file=sys.stderr)
        return 1

    audio = pick_audio()
    total_s = float(cues.get("totalDurationS") or probe_duration(audio))
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    shutil.rmtree(WORK, ignore_errors=True)
    WORK.mkdir(parents=True, exist_ok=True)

    parts: list[Path] = []
    for i, seg in enumerate(schedule):
        seg_id = str(seg.get("id", f"seg_{i}"))
        kind = str(seg.get("kind", ""))
        wall_start = float(seg["wallStartS"])
        wall_end = float(seg["wallEndS"])
        wall_dur = max(wall_end - wall_start, 0.05)
        out = WORK / f"{i:02d}_{seg_id}.mp4"

        if kind == "grid":
            render_black_segment(out, wall_dur)
        elif kind == "beat":
            render_beat_hold(out, INTRO_MP4, float(seg["contentStart"]), wall_dur)
        elif kind == "gap":
            render_gap_segment(
                out,
                INTRO_MP4,
                float(seg["contentStart"]),
                float(seg["contentEnd"]),
                wall_dur,
            )
        else:
            continue
        parts.append(out)

    silent = WORK / "video_silent.mp4"
    concat_segments(parts, silent)
    mux_audio(silent, audio, OUT_MP4, total_s)

    size_mb = OUT_MP4.stat().st_size / (1024 * 1024)
    print(f"done → {OUT_MP4}")
    print(f"duration: {probe_duration(OUT_MP4):.1f}s | size: {size_mb:.1f} MB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
