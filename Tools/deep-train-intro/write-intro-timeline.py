#!/usr/bin/env python3
"""Emit intro_timeline.json — measured segment boundaries for Unity playback."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path


def probe_duration(path: Path) -> float:
    out = subprocess.check_output(
        [
            "ffprobe",
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


def segment_kind(path: Path) -> str:
    name = path.name
    if name.startswith("beat_"):
        return "beat"
    if name.startswith("seg_") or name.startswith("clip_gap_"):
        return "gap"
    return "segment"


def build_measured(concat_list: Path, *, transition_speed: float, fps: int) -> dict:
    cursor = 0.0
    segments: list[dict] = []
    pending_gap: dict | None = None

    for raw in concat_list.read_text().splitlines():
        raw = raw.strip()
        if not raw.startswith("file "):
            continue
        quoted = raw[5:].strip().strip("'")
        path = Path(quoted)
        dur = probe_duration(path)
        kind = segment_kind(path)

        if kind == "beat":
            if pending_gap is not None:
                pending_gap["end"] = cursor
                pending_gap["durationS"] = pending_gap["end"] - pending_gap["start"]
                segments.append(pending_gap)
                pending_gap = None

            seg_id = path.stem
            segments.append(
                {
                    "id": seg_id,
                    "kind": "beat",
                    "start": round(cursor, 6),
                    "end": round(cursor + dur, 6),
                    "durationS": round(dur, 6),
                    "playbackSpeed": 1.0,
                }
            )
            cursor += dur
            continue

        if pending_gap is None:
            pending_gap = {
                "id": f"gap_{path.parent.name.removeprefix('gap_')}",
                "kind": "gap",
                "start": cursor,
                "end": cursor,
                "durationS": 0.0,
                "playbackSpeed": transition_speed,
            }
        cursor += dur

    if pending_gap is not None:
        pending_gap["end"] = cursor
        pending_gap["durationS"] = round(pending_gap["end"] - pending_gap["start"], 6)
        segments.append(pending_gap)

    return finalize(segments, transition_speed=transition_speed, fps=fps)


def build_scaled(
    *,
    content_duration_s: float,
    beat_s: float,
    gap_s: float,
    shots: int,
    transition_speed: float,
    fps: int,
) -> dict:
    target = shots * beat_s + (shots - 1) * gap_s
    scale = content_duration_s / target if target > 0 else 1.0
    beat_d = beat_s * scale
    gap_d = gap_s * scale

    cursor = 0.0
    segments: list[dict] = []
    for i in range(1, shots + 1):
        num = f"{i:02d}"
        segments.append(
            {
                "id": f"beat_{num}",
                "kind": "beat",
                "start": round(cursor, 6),
                "end": round(cursor + beat_d, 6),
                "durationS": round(beat_d, 6),
                "playbackSpeed": 1.0,
            }
        )
        cursor += beat_d
        if i < shots:
            next_num = f"{i + 1:02d}"
            segments.append(
                {
                    "id": f"gap_{num}_{next_num}",
                    "kind": "gap",
                    "start": round(cursor, 6),
                    "end": round(cursor + gap_d, 6),
                    "durationS": round(gap_d, 6),
                    "playbackSpeed": transition_speed,
                }
            )
            cursor += gap_d

    return finalize(segments, transition_speed=transition_speed, fps=fps)


def finalize(segments: list[dict], *, transition_speed: float, fps: int) -> dict:
    content = segments[-1]["end"] if segments else 0.0
    wall = 0.0
    for seg in segments:
        dur = float(seg["durationS"])
        speed = float(seg.get("playbackSpeed", 1.0) or 1.0)
        wall += dur / max(speed, 0.01)

    return {
        "version": 1,
        "fps": fps,
        "transitionPlaybackSpeed": transition_speed,
        "contentDurationS": round(content, 6),
        "wallClockContentDurationS": round(wall, 6),
        "segments": segments,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--fps", type=int, default=72)
    parser.add_argument("--transition-speed", type=float, default=5.0)
    parser.add_argument("--concat-list")
    parser.add_argument("--scaled", action="store_true")
    parser.add_argument("--content-duration", type=float)
    parser.add_argument("--beat-s", type=float, default=3.2)
    parser.add_argument("--gap-s", type=float, default=9.8)
    parser.add_argument("--shots", type=int, default=10)
    args = parser.parse_args()

    if args.concat_list:
        data = build_measured(
            Path(args.concat_list),
            transition_speed=args.transition_speed,
            fps=args.fps,
        )
    elif args.scaled:
        if args.content_duration is None:
            print("scaled mode requires --content-duration", file=sys.stderr)
            return 1
        data = build_scaled(
            content_duration_s=args.content_duration,
            beat_s=args.beat_s,
            gap_s=args.gap_s,
            shots=args.shots,
            transition_speed=args.transition_speed,
            fps=args.fps,
        )
    else:
        print("provide --concat-list or --scaled", file=sys.stderr)
        return 1

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    print(
        f"timeline: {len(data['segments'])} segments | "
        f"content {data['contentDurationS']:.3f}s | "
        f"wall {data['wallClockContentDurationS']:.3f}s @ gap {args.transition_speed}x",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
