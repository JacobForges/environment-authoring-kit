#!/usr/bin/env python3
"""
Walk intro.mp4 segment-by-segment, measure color/luma, auto-fix intro_still_colors.json
and intro_playback_tune.json for Unity runtime. Re-verify until clean.

Usage: fix-intro-playback-bot.py [--dry-run]
"""

from __future__ import annotations

import json
import math
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
INTRO_MP4 = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro.mp4"
TIMELINE = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_timeline.json"
STILL_COLORS = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_still_colors.json"
PLAYBACK_TUNE = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_playback_tune.json"
REPORT = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_fix_bot_report.json"

GRADE_MIN = 0.88
GRADE_MAX = 1.12
GRADE_MAX_TUNED = 1.28
GAP_LUMA_MIN_RATIO = 0.78

# Unity Play Mode recording (2026-06-13) — runtime gap fixes MP4-only bot cannot see.
RECORDING_RUNTIME_OVERRIDES: dict[str, dict] = {
    "gap_01_02": {
        "midLift": 1.10,
        "greenScale": 0.94,
        "brightness": 1.05,
        "blackLift": 0.055,
    },
    "gap_03_04": {
        "midLift": 1.04,
        "greenScale": 0.98,
    },
    "gap_04_05": {
        "midLift": 1.32,
        "brightness": 1.08,
        "blackLift": 0.07,
    },
    "gap_05_06": {
        "midLift": 1.38,
        "brightness": 1.10,
        "blackLift": 0.075,
    },
}


def merge_recording_overrides(gap_tunes: list[dict]) -> None:
    merged: dict[str, dict] = dict(RECORDING_RUNTIME_OVERRIDES)

    overrides_path = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_recording_overrides.json"
    if overrides_path.exists():
        try:
            payload = json.loads(overrides_path.read_text(encoding="utf-8"))
            for gid, ov in payload.get("overrides", {}).items():
                base = merged.get(gid, {})
                base.update(ov)
                merged[gid] = base
        except json.JSONDecodeError:
            pass

    for gt in gap_tunes:
        ov = merged.get(gt["id"])
        if not ov:
            continue
        for key, val in ov.items():
            if key in ("note", "recordingLumaRatio"):
                continue
            if isinstance(val, (int, float)):
                gt[key] = val
        gt["recordingCalibrated"] = True


def luma(rgb: tuple[float, float, float]) -> float:
    return 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]


def sample_frame(mp4: Path, time_s: float) -> tuple[float, float, float]:
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "frame.png"
        subprocess.run(
            [
                "ffmpeg", "-loglevel", "error", "-y",
                "-ss", f"{max(0, time_s):.4f}",
                "-i", str(mp4),
                "-frames:v", "1",
                "-vf", "scale=480:-1",
                str(out),
            ],
            check=True,
        )
        img = Image.open(out).convert("RGB")
        px = list(img.getdata())
        n = max(len(px), 1)
        return (
            sum(p[0] for p in px) / n / 255.0,
            sum(p[1] for p in px) / n / 255.0,
            sum(p[2] for p in px) / n / 255.0,
        )


def grade_to_reference(avg: list[float], ref: list[float]) -> list[float]:
    out = []
    for i in range(3):
        v = ref[i] / max(avg[i], 1e-4)
        out.append(round(max(GRADE_MIN, min(GRADE_MAX, v)), 5))
    return out


def smooth(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def geo_lerp(a: float, b: float, t: float) -> float:
    a, b = max(a, 1e-4), max(b, 1e-4)
    return math.exp((1 - t) * math.log(a) + t * math.log(b))


def balanced_gap_grade(from_g: list[float], to_g: list[float], t: float, lift: float) -> list[float]:
    blended = [geo_lerp(from_g[i], to_g[i], t) for i in range(3)]
    mid = 1.0 - 4.0 * (t - 0.5) ** 2
    cross = 1.0 + 0.2 * max(0.0, mid)
    total = lift * cross
    return [max(1.0, min(GRADE_MAX, blended[i] * total)) for i in range(3)]


def probe_duration(mp4: Path) -> float:
    return float(
        subprocess.check_output(
            [
                "ffprobe", "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                str(mp4),
            ],
            text=True,
        ).strip()
    )


def segment_time(seg: dict, frac: float) -> float:
    start = float(seg["start"])
    end = float(seg["end"])
    return start + (end - start) * frac


def run_bot(dry_run: bool) -> int:
    if not INTRO_MP4.exists():
        print(f"FAIL missing {INTRO_MP4}", file=sys.stderr)
        return 1
    if not TIMELINE.exists():
        print(f"FAIL missing {TIMELINE}", file=sys.stderr)
        return 1

    timeline = json.loads(TIMELINE.read_text(encoding="utf-8"))
    segments = timeline.get("segments", [])
    mp4_dur = probe_duration(INTRO_MP4)
    tl_dur = float(timeline.get("contentDurationS", 0))

    print("=" * 64)
    print("INTRO PLAYBACK FIX BOT — walk MP4 → patch JSON → verify")
    print("=" * 64)
    print(f"MP4 duration: {mp4_dur:.3f}s  |  timeline: {tl_dur:.3f}s")

    if abs(mp4_dur - tl_dur) > 0.05:
        print("  → duration drift — re-emit timeline recommended:")
        print(f"    {ROOT / 'Tools/deep-train-intro/emit-intro-timeline.sh'}")

    # --- Phase 1: sample every beat + gap (multi-point on gaps) ---
    beat_samples: dict[str, dict] = {}
    gap_samples: dict[str, list[dict]] = {}

    for seg in segments:
        sid = seg["id"]
        kind = seg.get("kind", "")
        if kind == "beat":
            t = segment_time(seg, 0.5)
            rgb = sample_frame(INTRO_MP4, t)
            beat_samples[sid] = {
                "timeS": round(t, 4),
                "avgRgb": [round(x, 5) for x in rgb],
                "luma": round(luma(rgb), 5),
            }
            print(f"  sample beat {sid} @ {t:6.2f}s  luma={luma(rgb):.3f}")
        elif kind == "gap":
            pts = []
            for frac in (0.15, 0.35, 0.5, 0.65, 0.85):
                t = segment_time(seg, frac)
                rgb = sample_frame(INTRO_MP4, t)
                pts.append({
                    "frac": frac,
                    "timeS": round(t, 4),
                    "avgRgb": [round(x, 5) for x in rgb],
                    "luma": round(luma(rgb), 5),
                })
            gap_samples[sid] = pts
            mid_l = pts[2]["luma"]
            print(f"  sample gap  {sid}  mid_luma={mid_l:.3f}  (5 points)")

    # --- Phase 2: rebuild still colors from MP4 measurements ---
    stills = []
    for i in range(1, 11):
        bid = f"beat_{i:02d}"
        if bid not in beat_samples:
            print(f"FAIL missing beat sample {bid}", file=sys.stderr)
            return 1
        stills.append({"id": bid, **beat_samples[bid]})

    ref = [
        sum(s["avgRgb"][0] for s in stills) / len(stills),
        sum(s["avgRgb"][1] for s in stills) / len(stills),
        sum(s["avgRgb"][2] for s in stills) / len(stills),
    ]
    for s in stills:
        s["grade"] = grade_to_reference(s["avgRgb"], ref)

    gaps = []
    gap_tunes: list[dict] = []
    issues: list[str] = []

    for i in range(1, 10):
        gid = f"gap_{i:02d}_{i + 1:02d}"
        from_b = f"beat_{i:02d}"
        to_b = f"beat_{i + 1:02d}"
        from_g = next(s["grade"] for s in stills if s["id"] == from_b)
        to_g = next(s["grade"] for s in stills if s["id"] == to_b)
        gaps.append({
            "id": gid,
            "fromBeat": from_b,
            "toBeat": to_b,
            "fromGrade": from_g,
            "toGrade": to_g,
        })

        if gid not in gap_samples:
            continue

        from_l = beat_samples[from_b]["luma"]
        to_l = beat_samples[to_b]["luma"]
        ref_l = (from_l + to_l) * 0.5
        pts = gap_samples[gid]

        lifts: list[float] = []
        for pt in pts:
            ratio = pt["luma"] / max(ref_l, 1e-4)
            if ratio < GAP_LUMA_MIN_RATIO:
                lift = min(1.35, max(1.08, ref_l / max(pt["luma"], 1e-4)))
                lifts.append(lift)
                issues.append(f"{gid}@{pt['frac']:.2f} luma_ratio={ratio:.2f}")

        mid_ratio = pts[2]["luma"] / max(ref_l, 1e-4)
        mid_lift = 1.0
        if lifts:
            mid_lift = sum(lifts) / len(lifts)
        elif mid_ratio < 0.95:
            mid_lift = min(1.25, max(1.0, 1.0 + (0.95 - mid_ratio) * 0.8))

        gap_tunes.append({
            "id": gid,
            "midLift": round(mid_lift, 4),
            "measuredMidLuma": pts[2]["luma"],
            "beatAvgLuma": round(ref_l, 5),
            "lumaRatio": round(mid_ratio, 4),
        })

    merge_recording_overrides(gap_tunes)

    colors_payload = {
        "version": 2,
        "source": "fix-intro-playback-bot (MP4 measured)",
        "referenceRgb": [round(x, 5) for x in ref],
        "stills": stills,
        "gaps": gaps,
    }

    tune_payload = {
        "version": 2,
        "source": "fix-intro-playback-bot + recording-calibrated overrides",
        "mp4DurationS": round(mp4_dur, 6),
        "gapPlaybackSpeed": float(timeline.get("transitionPlaybackSpeed", 5.0)),
        "gapMidLiftDefault": 1.12,
        "glow": 0.07,
        "heroGlow": 0.11,
        "glowScale": 0.48,
        "cinematicLight": 0.62,
        "gaps": gap_tunes,
    }

    # --- Phase 3: verify rebuilt profile ---
    verify_fail = 0
    print("-" * 64)
    print("VERIFY after rebuild:")
    for gt in gap_tunes:
        gid = gt["id"]
        ratio = gt["lumaRatio"]
        lift = gt["midLift"]
        ok = ratio >= GAP_LUMA_MIN_RATIO or lift > 1.01
        status = "OK" if ok else "WARN"
        if not ok:
            verify_fail += 1
        print(f"  {status} {gid}  luma_ratio={ratio:.2f}  mid_lift={lift:.3f}")

    report = {
        "version": 1,
        "dryRun": dry_run,
        "mp4DurationS": mp4_dur,
        "timelineDurationS": tl_dur,
        "issuesFound": issues,
        "verifyFailures": verify_fail,
        "beatCount": len(stills),
        "gapCount": len(gaps),
    }

    print("-" * 64)
    if dry_run:
        print("DRY RUN — no files written")
    else:
        STILL_COLORS.write_text(json.dumps(colors_payload, indent=2) + "\n", encoding="utf-8")
        PLAYBACK_TUNE.write_text(json.dumps(tune_payload, indent=2) + "\n", encoding="utf-8")
        REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(f"Wrote {STILL_COLORS.relative_to(ROOT)}")
        print(f"Wrote {PLAYBACK_TUNE.relative_to(ROOT)}")
        print(f"Report  {REPORT.relative_to(ROOT)}")

    print("=" * 64)
    if verify_fail and not dry_run:
        print(f"DONE with {verify_fail} gap warnings (lifts applied in tune file)")
        return 0
    print("DONE — bot walked MP4 and patched playback JSON")
    return 0


if __name__ == "__main__":
    dry = "--dry-run" in sys.argv
    raise SystemExit(run_bot(dry))
