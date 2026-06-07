"""Build checkpoint + subbeat timelines for demo recap (fills gaps between major holds)."""
from __future__ import annotations

from pathlib import Path
from typing import Any

CHAPTERS = [
    ("Session bootstrap", "surface_build", "editor queue", [72, 138, 220]),
    ("Grid contract", "surface_build", "nine-tile grid", [90, 168, 120]),
    ("Seam invariants", "surface_build", "tile seams", [110, 175, 200]),
    ("Play disk", "surface_build", "play disk grading", [200, 140, 80]),
    ("Terrain meat", "surface_build", "terrain meat", [210, 120, 90]),
    ("Foothills", "surface_build", "foothills", [150, 110, 210]),
    ("Mountain ring", "surface_build", "mountains", [130, 100, 190]),
    ("Labyrinth annex", "surface_build", "south annex", [100, 190, 230]),
    ("Trail bench", "surface_build", "trail queue", [220, 90, 90]),
    ("Surface phase", "surface_build", "surface lock", [72, 138, 220]),
    ("Final capture", "surface_build", "recording end", [220, 90, 90]),
]

# Sub-action beats placed in timelapse gaps (explained while motion runs).
SUBBEAT_ACTIONS = [
    ("Queue drain", "editor queue", "pending benches clearing"),
    ("Tile flatten", "nine-tile grid", "height pass on grid"),
    ("Seam weld", "tile seams", "border vertices aligned"),
    ("Disk grade", "play disk grading", "playable bowl shaping"),
    ("Noise sculpt", "terrain meat", "macro noise pass"),
    ("Foothill blend", "foothills", "transition from disk to hills"),
    ("Mountain lift", "mountains", "ring elevation pass"),
    ("Annex carve", "south annex", "labyrinth pocket cut"),
    ("Trail stamp", "trail queue", "path bench advancing"),
    ("Surface lock", "surface lock", "pre-cave surface freeze"),
]


def _checkpoint_frames(frame_count: int, n: int) -> list[int]:
    n = max(2, n)
    return [round(i * (frame_count - 1) / max(n - 1, 1)) for i in range(n)]


def _subbeat_frames(cp_frames: list[int], frame_count: int, n_sub: int) -> list[int]:
    """Spread subbeats across timelapse gaps (one per gap first, then largest gaps)."""
    if n_sub <= 0 or len(cp_frames) < 2:
        return []
    used = set(cp_frames)
    gaps = [(a, b) for a, b in zip(cp_frames, cp_frames[1:]) if b - a >= 4]
    if not gaps:
        return []

    out: list[int] = []

    def place(a: int, b: int, slot: int, slots: int) -> int | None:
        fi = max(1, min(frame_count - 2, a + (b - a) * slot // (slots + 1)))
        return fi if fi not in used else None

    # Pass 1: one subbeat per gap (midpoint) — even pacing across the whole build
    for a, b in gaps:
        if len(out) >= n_sub:
            break
        fi = place(a, b, 1, 2)
        if fi is not None:
            used.add(fi)
            out.append(fi)

    # Pass 2: fill remaining slots in widest gaps
    gaps_by_span = sorted(gaps, key=lambda ab: ab[1] - ab[0], reverse=True)
    slot_idx = 2
    while len(out) < n_sub:
        placed = False
        for a, b in gaps_by_span:
            span = b - a
            slots = min(max(3, span // 80), 6)
            for s in range(2, slots + 1):
                fi = place(a, b, s, slots)
                if fi is not None:
                    used.add(fi)
                    out.append(fi)
                    placed = True
                    if len(out) >= n_sub:
                        break
            if len(out) >= n_sub:
                break
        if not placed:
            break
        slot_idx += 1

    return sorted(out)


def build_layered_timeline(
    frame_count: int,
    build_mode: str,
    *,
    checkpoints: int = 15,
    subbeats: int = 10,
) -> list[dict]:
    """Major holds (checkpoints) + shorter subbeat holds in timelapse gaps."""
    cp_n = max(6, checkpoints)
    sub_n = max(0, subbeats)
    cp_frames = _checkpoint_frames(frame_count, cp_n)
    sub_frames = _subbeat_frames(cp_frames, frame_count, sub_n)

    events: list[dict] = []
    for i, fi in enumerate(cp_frames):
        ch_i = min(int(i / max(cp_n - 1, 1) * (len(CHAPTERS) - 1)), len(CHAPTERS) - 1)
        ch, ph, sub, ac = CHAPTERS[ch_i]
        events.append(
            {
                "frame": fi,
                "beatKind": "checkpoint",
                "chapter": ch,
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
        title, ph, sub = SUBBEAT_ACTIONS[j % len(SUBBEAT_ACTIONS)]
        ch_i = min(int(j / max(sub_n - 1, 1) * (len(CHAPTERS) - 1)), len(CHAPTERS) - 1)
        ac = CHAPTERS[ch_i][3]
        events.append(
            {
                "frame": fi,
                "beatKind": "subbeat",
                "chapter": f"In progress · {title}",
                "phase": ph,
                "sub": sub,
                "subAction": title,
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


def hold_seconds(milestone: dict, spec: dict) -> float:
    if milestone.get("beatKind") == "subbeat":
        return float(
            milestone.get("holdSec")
            or spec.get("subbeatHoldSec", spec.get("milestoneHoldSec", 12.0) * 0.45)
        )
    return float(milestone.get("holdSec") or spec.get("milestoneHoldSec", 12.0))


def total_hold_seconds(milestones: list[dict], spec: dict) -> float:
    return sum(hold_seconds(m, spec) for m in milestones)


def preview_milestone_indices(
    milestones: list[dict[str, Any]],
    *,
    max_beats: int = 24,
    checkpoint_slots: int = 7,
) -> list[int]:
    """Preview cut: spaced checkpoints plus every subbeat between them (sub-step captions)."""
    n = len(milestones)
    if n <= max_beats:
        return list(range(n))
    cp_idx = [i for i, m in enumerate(milestones) if m.get("beatKind") != "subbeat"]
    sub_idx = [i for i, m in enumerate(milestones) if m.get("beatKind") == "subbeat"]
    if not cp_idx:
        return list(range(min(n, max_beats)))
    slots = max(3, min(checkpoint_slots, len(cp_idx)))
    pick_cp: set[int] = {0, n - 1}
    for s in range(slots):
        pick_cp.add(cp_idx[round(s * (len(cp_idx) - 1) / max(slots - 1, 1))])
    cp_chosen = sorted(pick_cp)
    lo = int(milestones[cp_chosen[0]].get("frame", 0))
    hi = int(milestones[cp_chosen[-1]].get("frame", 0))
    subs_in = [i for i in sub_idx if lo <= int(milestones[i].get("frame", 0)) <= hi]
    out = sorted(set(cp_chosen) | set(subs_in))
    if len(out) > max_beats:
        # Keep all subs, trim checkpoints from the middle outward
        subs_set = set(subs_in)
        cps_only = [i for i in out if i not in subs_set]
        while len(out) > max_beats and len(cps_only) > 2:
            cps_only.pop(len(cps_only) // 2)
            out = sorted(subs_set | set(cps_only))
    return out


def _frame_change_scores(frames: list[Path], start: int, end: int) -> dict[int, float]:
    """Per-index diff score within [start, end] — higher = more terrain change."""
    scores: dict[int, float] = {}
    if end - start < 2:
        return scores
    try:
        import importlib.util

        tools = Path(__file__).resolve().parent
        spec = importlib.util.spec_from_file_location("diff", tools / "demo-recap-scene-diff.py")
        diff = importlib.util.module_from_spec(spec)
        assert spec.loader
        spec.loader.exec_module(diff)
        for i in range(start + 1, end + 1):
            scores[i] = diff._diff_score(frames[i - 1], frames[i])
    except Exception:
        pass
    return scores


def pick_timelapse_keyframes(
    frames: list[Path],
    max_f: int,
    *,
    chunk_start: int = 0,
    milestone_frames: list[int] | None = None,
) -> list[Path]:
    """
    Key-moment timelapse selection: endpoints + milestone hits + high-change frames,
    then uniform fill for remaining budget (not blind subsample).
    """
    n = len(frames)
    if n <= max_f:
        return frames
    if n < 2:
        return frames

    must: set[int] = {0, n - 1}
    if milestone_frames:
        for mf in milestone_frames:
            rel = mf - chunk_start
            if 0 <= rel < n:
                must.add(rel)

    scores = _frame_change_scores(frames, 0, n - 1)
    ranked = sorted(
        ((i, s) for i, s in scores.items() if i not in must),
        key=lambda t: t[1],
        reverse=True,
    )
    budget = max_f - len(must)
    picks = set(must)
    for i, _ in ranked:
        if len(picks) >= max_f:
            break
        picks.add(i)

    while len(picks) < max_f:
        step = max(1, (n - 1) // max(1, max_f - len(picks)))
        added = False
        for i in range(0, n, step):
            if i not in picks:
                picks.add(i)
                added = True
                if len(picks) >= max_f:
                    break
        if not added:
            break

    return [frames[i] for i in sorted(picks)[:max_f]]


def timelapse_sec_for_frame_gap(n_frames: int, spec: dict) -> float:
    """Motion B-roll duration from frame count (fair pace, clamped)."""
    spf = float(spec.get("timelapseSecPerFrame", 0.09))
    min_s = float(spec.get("tlMinSec", 4.0))
    max_s = float(spec.get("tlMaxSec", 8.0))
    if n_frames < 2:
        return min_s
    return min(max_s, max(min_s, n_frames * spf))
