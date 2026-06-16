"""Build DemoRecapTimeline.json from arbitrary media — no Unity build semantics."""
from __future__ import annotations

from typing import Any


GENERIC_CHAPTERS = [
    ("Opening", "intro", "establish the scene", [72, 138, 220]),
    ("Context", "setup", "set the stage", [90, 168, 120]),
    ("Development", "middle", "main story beats", [110, 175, 200]),
    ("Turn", "pivot", "key moment", [200, 140, 80]),
    ("Detail", "closeup", "highlight details", [210, 120, 90]),
    ("Momentum", "flow", "keep energy", [150, 110, 210]),
    ("Climax", "peak", "strongest beat", [130, 100, 190]),
    ("Reflection", "pause", "breathing room", [100, 190, 230]),
    ("Bridge", "transition", "move forward", [220, 90, 90]),
    ("Closing", "outro", "wrap up", [72, 138, 220]),
]

SUBBEATS = [
    ("Cut in", "detail", "visual emphasis"),
    ("Hold", "pause", "let it land"),
    ("Push", "motion", "forward energy"),
    ("Reveal", "focus", "draw the eye"),
    ("Shift", "transition", "change pace"),
]


def _checkpoint_frames(frame_count: int, n: int) -> list[int]:
    n = max(2, min(n, max(2, frame_count)))
    if frame_count <= 1:
        return [0]
    return [round(i * (frame_count - 1) / max(n - 1, 1)) for i in range(n)]


def _subbeat_frames(cp_frames: list[int], frame_count: int, n_sub: int) -> list[int]:
    if n_sub <= 0 or len(cp_frames) < 2 or frame_count < 3:
        return []
    used = set(cp_frames)
    out: list[int] = []
    for a, b in zip(cp_frames, cp_frames[1:]):
        if len(out) >= n_sub or b - a < 4:
            continue
        fi = max(1, min(frame_count - 2, a + (b - a) // 2))
        if fi not in used:
            used.add(fi)
            out.append(fi)
    return sorted(out)


def build_generic_events(
    frame_count: int,
    *,
    checkpoints: int = 8,
    subbeats: int = 5,
    title: str = "",
) -> list[dict[str, Any]]:
    frame_count = max(1, frame_count)
    cp_n = max(3, min(checkpoints, 12))
    cp_frames = _checkpoint_frames(frame_count, cp_n)
    sub_frames = _subbeat_frames(cp_frames, frame_count, subbeats)
    events: list[dict[str, Any]] = []

    for i, fi in enumerate(cp_frames):
        ch_i = min(int(i / max(cp_n - 1, 1) * (len(GENERIC_CHAPTERS) - 1)), len(GENERIC_CHAPTERS) - 1)
        ch, ph, sub, ac = GENERIC_CHAPTERS[ch_i]
        label = title if i == 0 and title else ch
        events.append(
            {
                "frame": fi,
                "beatKind": "checkpoint",
                "chapter": label,
                "phase": ph,
                "sub": sub,
                "line1": "",
                "line2": "",
                "line3": "",
                "accent": ac,
                "regions": [],
            }
        )

    for j, fi in enumerate(sub_frames):
        title_b, ph, sub = SUBBEATS[j % len(SUBBEATS)]
        ch_i = min(int(j / max(len(sub_frames) - 1, 1) * (len(GENERIC_CHAPTERS) - 1)), len(GENERIC_CHAPTERS) - 1)
        ac = GENERIC_CHAPTERS[ch_i][3]
        events.append(
            {
                "frame": fi,
                "beatKind": "subbeat",
                "chapter": f"In progress · {title_b}",
                "phase": ph,
                "sub": sub,
                "subAction": title_b,
                "line1": "",
                "line2": "",
                "line3": "",
                "accent": ac,
                "regions": [],
            }
        )

    events.sort(key=lambda e: int(e["frame"]))
    for i, e in enumerate(events):
        e["i"] = i
    return events


def build_generic_timeline_spec(
    frame_count: int,
    *,
    title: str = "",
    checkpoints: int = 8,
    subbeats: int = 5,
) -> dict[str, Any]:
    events = build_generic_events(
        frame_count,
        checkpoints=checkpoints,
        subbeats=subbeats,
        title=title,
    )
    return {
        "version": 2,
        "standalone": True,
        "title": title or "AI Director project",
        "buildMode": "generic",
        "frameCount": max(1, frame_count),
        "milestoneHoldSec": 10.0,
        "subbeatHoldSec": 5.0,
        "introSec": 4.0,
        "outroSec": 5.0,
        "timelapseSecPerFrame": 0.35,
        "captionReadPauseSec": 0.4,
        "segmentXfadeSec": 0.35,
        "videoPlaybackFactor": 1.0,
        "cursorFullNarration": True,
        "narrationMode": "fullScript",
        "narratorEngine": "personal",
        "events": events,
        "milestones": events,
    }
