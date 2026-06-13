#!/usr/bin/env python3
"""
Sample a Unity Play Mode screen recording at intro wall-clock beats/gaps,
emit runtime gap overrides for fix-intro-playback-bot.py.

Usage:
  evaluate-intro-recording.py [recording.mov]
  (default: Hub/intro-recording.mov)
"""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Pillow required: pip install pillow", file=sys.stderr)
    raise

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_RECORDING = ROOT / "intro-recording.mov"
TIMELINE = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_timeline.json"
OUT = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_recording_overrides.json"

# Game-view crop for Unity editor recordings (2558x1426 reference).
CROP = (520, 90, 2038, 1330)
TARGET_LUMA_RATIO = 0.98


def luma(rgb: tuple[float, float, float]) -> float:
    return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]


def sample_frame(video: Path, time_s: float) -> tuple[float, float, float]:
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "frame.png"
        subprocess.run(
            [
                "ffmpeg", "-loglevel", "error", "-y",
                "-ss", f"{max(0, time_s):.4f}",
                "-i", str(video),
                "-frames:v", "1",
                str(out),
            ],
            check=True,
        )
        im = Image.open(out).convert("RGB").crop(CROP)
        px = list(im.getdata())
        n = max(len(px), 1)
        return (
            sum(p[0] for p in px) / n / 255.0,
            sum(p[1] for p in px) / n / 255.0,
            sum(p[2] for p in px) / n / 255.0,
        )


def wall_clock_map(timeline: dict) -> list[dict]:
    speed = float(timeline.get("transitionPlaybackSpeed", 5.0))
    out = []
    t = 0.0
    for seg in timeline["segments"]:
        dur = float(seg["durationS"])
        ws = dur if seg["kind"] == "beat" else dur / speed
        out.append({**seg, "wallStart": t, "wallEnd": t + ws, "wallMid": t + ws * 0.5})
        t += ws
    return out


def main() -> int:
    video = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_RECORDING
    if not video.exists():
        print(f"FAIL missing recording: {video}", file=sys.stderr)
        return 1
    if not TIMELINE.exists():
        print(f"FAIL missing timeline: {TIMELINE}", file=sys.stderr)
        return 1

    timeline = json.loads(TIMELINE.read_text(encoding="utf-8"))
    wall = wall_clock_map(timeline)
    beat_luma: dict[str, float] = {}
    gap_stats: dict[str, dict] = {}

    for seg in wall:
        sid = seg["id"]
        if seg["kind"] == "beat":
            t = seg["wallStart"] + (seg["wallEnd"] - seg["wallStart"]) * 0.5
            rgb = sample_frame(video, t)
            beat_luma[sid] = luma(rgb)
        elif seg["kind"] == "gap":
            rgb = sample_frame(video, seg["wallMid"])
            gap_stats[sid] = {
                "luma": luma(rgb),
                "greenBias": rgb[1] - rgb[0],
            }

    overrides: dict[str, dict] = {}
    print("Recording gap analysis (Unity output)")
    print("-" * 60)

    for i in range(1, 10):
        gid = f"gap_{i:02d}_{i + 1:02d}"
        if gid not in gap_stats:
            continue
        from_b = f"beat_{i:02d}"
        to_b = f"beat_{i + 1:02d}"
        ref = (beat_luma.get(from_b, 0.18) + beat_luma.get(to_b, 0.18)) * 0.5
        gs = gap_stats[gid]
        ratio = gs["luma"] / max(ref, 1e-4)
        ov: dict[str, float] = {}

        if ratio < TARGET_LUMA_RATIO:
            lift = min(1.45, max(1.08, ref / max(gs["luma"], 1e-4)))
            ov["midLift"] = round(lift, 3)
            ov["brightness"] = round(min(1.12, 1.0 + (TARGET_LUMA_RATIO - ratio) * 0.35), 3)
            ov["blackLift"] = round(min(0.09, 0.045 + (TARGET_LUMA_RATIO - ratio) * 0.12), 4)

        if gs["greenBias"] > 0.012:
            ov["greenScale"] = round(max(0.90, 1.0 - gs["greenBias"] * 2.2), 3)

        if ov:
            ov["recordingLumaRatio"] = round(ratio, 3)
            overrides[gid] = ov

        status = "PATCH" if ov else "OK"
        print(f"  {status} {gid}  luma_ratio={ratio:.2f}  green_bias={gs['greenBias']:.3f}")

    payload = {
        "version": 1,
        "sourceRecording": str(video),
        "overrides": overrides,
    }
    OUT.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print("-" * 60)
    print(f"Wrote {OUT.relative_to(ROOT)} ({len(overrides)} gap overrides)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
