#!/usr/bin/env python3
"""Wall-clock SFX cues — narration-led schedule when available."""

from __future__ import annotations

import json
from pathlib import Path

from intro_sync_constants import BEAT_PLAYBACK_SPEED, GRID_S, NARRATION_VIDEO_DELAY_S, TITLE_HOLD_S

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_TIMELINE = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_timeline.json"


def load_timeline(path: Path | None = None) -> dict:
    p = path or DEFAULT_TIMELINE
    return json.loads(p.read_text(encoding="utf-8"))


def beat_wall_starts(
    timeline: dict,
    *,
    grid_s: float = GRID_S,
    beat_playback_speed: float = BEAT_PLAYBACK_SPEED,
) -> dict[str, float]:
    gap_speed = float(timeline.get("transitionPlaybackSpeed", 2.25))
    wall = 0.0
    beats: dict[str, float] = {}
    for seg in timeline.get("segments", []):
        dur = float(seg["durationS"])
        if seg.get("kind") == "beat":
            beats[seg["id"]] = grid_s + wall
            wall += dur / max(beat_playback_speed, 0.05)
        else:
            wall += dur / max(gap_speed, 0.01)
    return beats


def full_intro_cues(
    timeline: dict | None = None,
    *,
    grid_s: float = GRID_S,
    title_at_s: float | None = None,
    total_duration_s: float | None = None,
    beat_playback_speed: float = BEAT_PLAYBACK_SPEED,
    narration_start_s: float | None = None,
    schedule: list[dict] | None = None,
    video_end_s: float | None = None,
    video_content_end_s: float | None = None,
) -> dict:
    tl = timeline or load_timeline()

    if schedule:
        grid_s = next((float(s["wallEndS"]) for s in schedule if s.get("id") == "grid"), grid_s)
        beats: dict[str, float] = {}
        for entry in schedule:
            if entry.get("kind") == "beat":
                beats[str(entry["id"])] = float(entry["wallStartS"])
        video_end = video_end_s if video_end_s is not None else max(
            (float(s["wallEndS"]) for s in schedule if s.get("kind") == "beat"),
            default=grid_s,
        )
        video_wall = max(video_end - grid_s, 0.0)
        title = title_at_s if title_at_s is not None else video_end
        total = total_duration_s if total_duration_s is not None else video_end + TITLE_HOLD_S
        content_end = video_content_end_s if video_content_end_s is not None else float(
            tl.get("contentDurationS", 118.57)
        )
    else:
        beats = beat_wall_starts(tl, grid_s=grid_s, beat_playback_speed=beat_playback_speed)
        gap_speed = float(tl.get("transitionPlaybackSpeed", 2.25))
        wall = 0.0
        for seg in tl.get("segments", []):
            dur = float(seg["durationS"])
            if seg.get("kind") == "beat":
                wall += dur / max(beat_playback_speed, 0.05)
            else:
                wall += dur / max(gap_speed, 0.01)
        video_wall = wall
        video_end = grid_s + video_wall
        title = title_at_s if title_at_s is not None else video_end + 2.0
        total = total_duration_s if total_duration_s is not None else video_end + TITLE_HOLD_S
        content_end = float(tl.get("contentDurationS", 118.57))
        schedule = []

    narr_start = narration_start_s if narration_start_s is not None else 0.0

    foot_start = grid_s + 1.5
    foot_end = min(title - 1.0, video_end - 0.5)
    footstep_times: list[float] = []
    t = foot_start
    while t < foot_end:
        footstep_times.append(round(t, 3))
        t += 5.2

    pans = [0.35, 0.65, 0.40, 0.60, 0.45, 0.55]
    footsteps = [(ts, pans[i % len(pans)]) for i, ts in enumerate(footstep_times)]

    out: dict = {
        "version": 1,
        "syncMode": "narration" if schedule else "video",
        "gridDurationS": round(grid_s, 3),
        "videoWallDurationS": round(video_wall, 3),
        "videoEndS": round(video_end, 3),
        "videoContentEndS": round(content_end, 3),
        "narrationStartS": round(narr_start, 3),
        "narrationVideoDelayS": NARRATION_VIDEO_DELAY_S,
        "beatPlaybackSpeed": beat_playback_speed,
        "titleStartS": round(title, 3),
        "rustleS": round(beats.get("beat_02", grid_s + 7.5), 3),
        "powerOnS": round(beats.get("beat_03", grid_s + 15.0), 3),
        "checkpointS": round(beats.get("beat_07", grid_s + 45.0), 3),
        "chimeS": round(title, 3),
        "footsteps": [{"t": t, "pan": p} for t, p in footsteps],
        "beats": {k: round(v, 3) for k, v in beats.items()},
        "totalDurationS": round(total, 3),
    }
    if schedule:
        out["schedule"] = schedule
    return out
