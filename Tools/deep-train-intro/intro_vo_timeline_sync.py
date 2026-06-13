#!/usr/bin/env python3
"""Narration-led intro VO — natural pace first, video schedule derived from cue times."""

from __future__ import annotations

import asyncio
import json
import re
import shutil
import subprocess
import sys
import wave
from dataclasses import replace
from pathlib import Path

from intro_sync_constants import BEAT_PLAYBACK_SPEED, GRID_S, TITLE_HOLD_S
from intro_vo_edge import SentenceCue, VOICE, ffmpeg, full_sentences

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_TIMELINE = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_timeline.json"
SR = 48000

# cue indices in full_sentences()
SEGMENT_CUES: list[tuple[str, list[int]]] = [
    ("grid", [0, 1]),
    ("beat_01", [2]),
    ("gap_01_02", [3]),
    ("beat_02", [4, 5]),
    ("gap_02_03", [6]),
    ("beat_03", [7, 8]),
    ("gap_03_04", [9]),
    ("beat_04", [10, 11]),
    ("gap_04_05", [12]),
    ("beat_05", [13, 14]),
    ("gap_05_06", [15]),
    ("beat_06", [16]),
    ("gap_06_07", [17, 18]),
    ("beat_07", [19, 20]),
    ("gap_07_08", [21]),
    ("beat_08", [22, 23]),
    ("gap_08_09", [24]),
    ("beat_09", [25, 26]),
    ("gap_09_10", [27]),
    ("beat_10", [28, 29]),
    ("title_block", [30, 31, 32]),
]

CUE_RATE_BIAS: dict[int, int] = {
    0: -4,
    1: -6,
    2: -10,
    3: +2,
    4: -8,
    5: -6,
    6: +3,
    7: -10,
    8: -8,
    9: +2,
    10: -6,
    11: -5,
    12: +4,
    13: -8,
    14: -6,
    15: +2,
    16: -8,
    17: -4,
    18: +2,
    19: -6,
    20: -8,
    21: +2,
    22: -8,
    23: -6,
    24: +2,
    25: -6,
    26: -5,
    27: +2,
    28: -8,
    29: -6,
    30: -4,
    31: 0,
    32: -2,
}


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


def load_timeline(path: Path | None = None) -> dict:
    p = path or DEFAULT_TIMELINE
    return json.loads(p.read_text(encoding="utf-8"))


def wall_segment_ranges(
    timeline: dict,
    *,
    grid_s: float = GRID_S,
    beat_playback_speed: float = BEAT_PLAYBACK_SPEED,
) -> tuple[dict[str, tuple[float, float]], float, float]:
    """Legacy fixed-speed video wall ranges (fallback only)."""
    gap_speed = float(timeline.get("transitionPlaybackSpeed", 2.25))
    cursor = 0.0
    ranges: dict[str, tuple[float, float]] = {"grid": (0.0, grid_s)}

    for seg in timeline.get("segments", []):
        start = grid_s + cursor
        dur = float(seg["durationS"])
        if seg.get("kind") == "beat":
            cursor += dur / max(beat_playback_speed, 0.05)
        else:
            cursor += dur / max(gap_speed, 0.01)
        ranges[seg["id"]] = (start, grid_s + cursor)

    video_end = grid_s + cursor
    total = video_end + TITLE_HOLD_S
    ranges["title_block"] = (video_end, total)
    return ranges, total, video_end


def parse_rate_pct(rate: str) -> int:
    m = re.match(r"([+-]?)(\d+)%", rate.strip())
    if not m:
        return 0
    sign = -1 if m.group(1) == "-" else 1
    return sign * int(m.group(2))


def format_rate_pct(pct: int) -> str:
    pct = max(-22, min(52, pct))
    if pct >= 0:
        return f"+{pct}%"
    return f"{pct}%"


def rate_for_narration(cue: SentenceCue, *, seg_id: str, cue_idx: int) -> str:
    """Natural storyteller pace — slower than default, no slot compression."""
    bias = CUE_RATE_BIAS.get(cue_idx, 0)
    base_pct = parse_rate_pct(cue.rate) + bias - 8  # global legibility cushion
    if seg_id.startswith("beat_"):
        base_pct -= 6
    elif seg_id.startswith("gap_"):
        base_pct -= 2
    return format_rate_pct(base_pct)


def cue_segment_id(cue_idx: int) -> str:
    for seg_id, indices in SEGMENT_CUES:
        if cue_idx in indices:
            return seg_id
    return "title_block"


async def _synth_mp3(cue: SentenceCue, mp3: Path) -> None:
    import edge_tts

    comm = edge_tts.Communicate(cue.text, VOICE, rate=cue.rate, pitch=cue.pitch, volume=cue.volume)
    await comm.save(str(mp3))


def synth_cue_wav(cue: SentenceCue, out_wav: Path, work: Path, *, seg_id: str, cue_idx: int) -> float:
    work.mkdir(parents=True, exist_ok=True)
    rate = rate_for_narration(cue, seg_id=seg_id, cue_idx=cue_idx)
    tuned = replace(cue, rate=rate, pause_after_s=0.0)
    mp3 = work / f"{out_wav.stem}.mp3"
    asyncio.run(_synth_mp3(tuned, mp3))
    subprocess.run(
        [ffmpeg(), "-y", "-i", str(mp3), "-ar", str(SR), "-ac", "1", str(out_wav)],
        check=True,
        capture_output=True,
    )
    return probe_duration(out_wav)


def append_silence(samples: list[int], duration_s: float) -> None:
    samples.extend([0] * int(duration_s * SR))


def read_mono_pcm(path: Path) -> list[int]:
    with wave.open(str(path), "rb") as w:
        frames = w.readframes(w.getnframes())
    import array

    return array.array("h", frames).tolist()


def write_mono_pcm(path: Path, samples: list[int]) -> None:
    import array

    path.parent.mkdir(parents=True, exist_ok=True)
    peak = max(abs(s) for s in samples) or 1
    if peak > 30000:
        scale = 30000 / peak
        samples = [int(s * scale) for s in samples]
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(array.array("h", samples).tobytes())


def build_narration_schedule(
    cue_starts: dict[int, float],
    vo_end_s: float,
    timeline: dict,
) -> tuple[list[dict], float, float, float, float, float]:
    """Return schedule entries + grid_s, video_end, title_start, video_content_end."""
    by_id = {seg["id"]: seg for seg in timeline.get("segments", [])}
    schedule: list[dict] = []

    grid_end = cue_starts.get(2, GRID_S)
    schedule.append(
        {
            "id": "grid",
            "kind": "grid",
            "wallStartS": 0.0,
            "wallEndS": grid_end,
            "contentStart": 0.0,
            "contentEnd": 0.0,
        }
    )

    for idx, (seg_id, cue_indices) in enumerate(SEGMENT_CUES):
        if seg_id in {"grid", "title_block"}:
            continue

        wall_start = cue_starts[cue_indices[0]]
        if idx + 1 < len(SEGMENT_CUES):
            next_cues = SEGMENT_CUES[idx + 1][1]
            wall_end = cue_starts[next_cues[0]]
        else:
            wall_end = vo_end_s

        vid = by_id.get(seg_id)
        if vid is None:
            continue

        schedule.append(
            {
                "id": seg_id,
                "kind": vid.get("kind", "beat"),
                "wallStartS": wall_start,
                "wallEndS": wall_end,
                "contentStart": float(vid["start"]),
                "contentEnd": float(vid["end"]),
            }
        )

    title_start = cue_starts.get(30, vo_end_s)
    video_end = title_start
    video_wall = max(video_end - grid_end, 0.0)
    last_beat = by_id.get("beat_10", {})
    content_end = float(last_beat.get("end", timeline.get("contentDurationS", 118.57)))

    return schedule, grid_end, video_end, title_start, video_wall, content_end


def synthesize_narration_led_vo(
    out_dry: Path,
    timeline: dict | None = None,
) -> tuple[bool, float, float, float, list[dict], float]:
    """Synth VO at natural pace; video follows cue wall times.

    Returns ok, title_start_s, vo_duration_s, total_timeline_s, schedule, grid_s.
    """
    try:
        import edge_tts  # noqa: F401
    except ImportError:
        return False, 0.0, 0.0, 0.0, [], GRID_S

    tl = timeline or load_timeline()
    cues = full_sentences()
    work = out_dry.parent / ".narration_vo_build"
    shutil.rmtree(work, ignore_errors=True)
    work.mkdir(parents=True, exist_ok=True)

    cue_starts: dict[int, float] = {}
    pcm: list[int] = []
    cursor = 0.0
    title_start_s = 0.0

    try:
        for i, cue in enumerate(cues):
            cue_starts[i] = cursor
            if i == 30:
                title_start_s = cursor

            seg_id = cue_segment_id(i)
            seg_wav = work / f"cue_{i:02d}.wav"
            dur = synth_cue_wav(cue, seg_wav, work / "raw", seg_id=seg_id, cue_idx=i)
            pcm.extend(read_mono_pcm(seg_wav))
            cursor += dur

            if cue.pause_after_s > 0.01:
                append_silence(pcm, cue.pause_after_s)
                cursor += cue.pause_after_s

        vo_end = cursor
        schedule, grid_s, video_end, title_start_s, _video_wall, content_end = build_narration_schedule(
            cue_starts, vo_end, tl
        )
        total_s = vo_end + TITLE_HOLD_S

        write_mono_pcm(out_dry, pcm)
        vo_dur = probe_duration(out_dry)

        # enrich schedule metadata for compose
        for entry in schedule:
            entry["wallStartS"] = round(float(entry["wallStartS"]), 3)
            entry["wallEndS"] = round(float(entry["wallEndS"]), 3)
            entry["contentStart"] = round(float(entry["contentStart"]), 3)
            entry["contentEnd"] = round(float(entry["contentEnd"]), 3)

        return True, title_start_s, vo_dur, total_s, schedule, grid_s
    except Exception as exc:
        print(f"narration-led VO synth failed: {exc}", file=sys.stderr)
        return False, 0.0, 0.0, 0.0, [], GRID_S
    finally:
        shutil.rmtree(work, ignore_errors=True)


def synthesize_timeline_vo(
    out_dry: Path,
    timeline: dict | None = None,
) -> tuple[bool, float, float, float]:
    """Compatibility wrapper — narration-led only."""
    ok, title_start, vo_dur, total_s, _schedule, _grid = synthesize_narration_led_vo(out_dry, timeline)
    return ok, title_start, vo_dur, total_s
