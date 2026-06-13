#!/usr/bin/env python3
"""
Sample intro.mp4 at beat + gap timestamps; verify gap color/luma vs adjacent beats.
Auto-writes corrected gap lift hints to intro_still_colors.json when drift exceeds threshold.
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
REPORT = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_color_verify_report.json"

# Gap mid luma should stay within this ratio of adjacent beat average.
MIN_GAP_TO_BEAT_LUMA = 0.72
MAX_CHANNEL_DRIFT = 0.18


def luma(rgb: tuple[float, float, float]) -> float:
    return 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]


def sample_frame(mp4: Path, time_s: float) -> tuple[float, float, float]:
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "frame.png"
        subprocess.run(
            [
                "ffmpeg",
                "-loglevel",
                "error",
                "-y",
                "-ss",
                f"{time_s:.4f}",
                "-i",
                str(mp4),
                "-frames:v",
                "1",
                "-vf",
                "scale=320:-1",
                str(out),
            ],
            check=True,
        )
        img = Image.open(out).convert("RGB")
        px = list(img.getdata())
        n = max(len(px), 1)
        r = sum(p[0] for p in px) / n / 255.0
        g = sum(p[1] for p in px) / n / 255.0
        b = sum(p[2] for p in px) / n / 255.0
        return (r, g, b)


def geo_lerp(a: float, b: float, t: float) -> float:
    a = max(a, 1e-4)
    b = max(b, 1e-4)
    return math.exp((1 - t) * math.log(a) + t * math.log(b))


def balanced_gap_grade(from_g: list[float], to_g: list[float], lift: float = 1.2) -> list[float]:
    t = 0.5
    blended = [geo_lerp(from_g[i], to_g[i], t) for i in range(3)]
    return [max(1.0, min(1.12, blended[i] * lift)) for i in range(3)]


def main() -> int:
    apply_fix = "--apply" in sys.argv

    for p in (INTRO_MP4, TIMELINE, STILL_COLORS):
        if not p.exists():
            print(f"FAIL missing {p}", file=sys.stderr)
            return 1

    timeline = json.loads(TIMELINE.read_text(encoding="utf-8"))
    colors = json.loads(STILL_COLORS.read_text(encoding="utf-8"))
    segments = timeline.get("segments", [])

    beat_luma: dict[str, float] = {}
    gap_rows: list[dict] = []
    failures: list[str] = []
    fixes: list[dict] = []

    print(f"Intro MP4 color bot — {INTRO_MP4.name}")
    print("-" * 64)

    for seg in segments:
        sid = seg["id"]
        kind = seg.get("kind", "")
        start = float(seg["start"])
        end = float(seg["end"])
        mid = (start + end) * 0.5

        rgb = sample_frame(INTRO_MP4, mid)
        lu = luma(rgb)
        row = {"id": sid, "kind": kind, "timeS": round(mid, 4), "avgRgb": [round(x, 5) for x in rgb], "luma": round(lu, 5)}

        if kind == "beat":
            beat_luma[sid] = lu
            print(f"  beat {sid} @ {mid:6.2f}s  luma={lu:.3f}  rgb=({rgb[0]:.2f},{rgb[1]:.2f},{rgb[2]:.2f})")
        elif kind == "gap":
            gap_rows.append(row)

    print("-" * 64)

    gap_map = {g["id"]: g for g in colors.get("gaps", [])}

    for row in gap_rows:
        gid = row["id"]
        gap_l = row["luma"]
        entry = gap_map.get(gid)
        if not entry:
            failures.append(f"{gid}: missing from intro_still_colors.json")
            continue

        from_b = entry["fromBeat"]
        to_b = entry["toBeat"]
        from_l = beat_luma.get(from_b)
        to_l = beat_luma.get(to_b)
        if from_l is None or to_l is None:
            failures.append(f"{gid}: missing adjacent beat luma")
            continue

        ref_l = (from_l + to_l) * 0.5
        ratio = gap_l / max(ref_l, 1e-4)
        from_rgb = sample_frame(INTRO_MP4, next(s["start"] + s["durationS"] * 0.5 for s in segments if s["id"] == from_b))
        to_rgb = sample_frame(INTRO_MP4, next(s["start"] + s["durationS"] * 0.5 for s in segments if s["id"] == to_b))
        gap_rgb = tuple(row["avgRgb"])

        drift_r = abs(gap_rgb[0] - (from_rgb[0] + to_rgb[0]) * 0.5)
        drift_g = abs(gap_rgb[1] - (from_rgb[1] + to_rgb[1]) * 0.5)
        drift_b = abs(gap_rgb[2] - (from_rgb[2] + to_rgb[2]) * 0.5)
        max_drift = max(drift_r, drift_g, drift_b)

        ok = ratio >= MIN_GAP_TO_BEAT_LUMA and max_drift <= MAX_CHANNEL_DRIFT + 0.08
        status = "OK" if ok else "WARN"

        print(
            f"{status} {gid} @ {row['timeS']:.2f}s  "
            f"gap_luma={gap_l:.3f}  beat_avg={ref_l:.3f}  ratio={ratio:.2f}  drift={max_drift:.3f}"
        )

        if not ok:
            failures.append(gid)
            # Compute lift needed so runtime grade compensates measured darkness.
            lift = min(1.35, max(1.12, ref_l / max(gap_l, 1e-4)))
            new_grade = balanced_gap_grade(entry["fromGrade"], entry["toGrade"], lift)
            fixes.append(
                {
                    "gapId": gid,
                    "measuredGapLuma": round(gap_l, 5),
                    "beatAvgLuma": round(ref_l, 5),
                    "recommendedLift": round(lift, 4),
                    "recommendedMidGrade": new_grade,
                }
            )

    report = {
        "version": 1,
        "mp4": str(INTRO_MP4.relative_to(ROOT)),
        "passed": len(failures) == 0,
        "failureCount": len(failures),
        "failures": failures,
        "fixes": fixes,
        "beats": beat_luma,
        "gaps": gap_rows,
    }
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("-" * 64)
    print(f"Report → {REPORT.relative_to(ROOT)}")

    if fixes and apply_fix:
        for fix in fixes:
            gid = fix["gapId"]
            for gap in colors["gaps"]:
                if gap["id"] != gid:
                    continue
                mid = fix["recommendedMidGrade"]
                # Nudge from/to toward balanced midpoint so runtime lerp matches MP4.
                gap["fromGrade"] = [round((a + mid[i]) * 0.5, 5) for i, a in enumerate(gap["fromGrade"])]
                gap["toGrade"] = [round((b + mid[i]) * 0.5, 5) for i, b in enumerate(gap["toGrade"])]
                for i in range(3):
                    gap["fromGrade"][i] = max(1.0, min(1.12, gap["fromGrade"][i]))
                    gap["toGrade"][i] = max(1.0, min(1.12, gap["toGrade"][i]))
        STILL_COLORS.write_text(json.dumps(colors, indent=2) + "\n", encoding="utf-8")
        print(f"Applied {len(fixes)} gap grade corrections → {STILL_COLORS.relative_to(ROOT)}")

    if failures and not apply_fix:
        print(f"\n{len(failures)} gap(s) need correction. Re-run with --apply to patch intro_still_colors.json", file=sys.stderr)
        return 1

    # Timeline vs runtime conflicts
    conflicts: list[str] = []
    mp4_dur = float(
        subprocess.check_output(
            [
                "ffprobe",
                "-v",
                "error",
                "-show_entries",
                "format=duration",
                "-of",
                "default=noprint_wrappers=1:nokey=1",
                str(INTRO_MP4),
            ],
            text=True,
        ).strip()
    )
    tl_dur = float(timeline.get("contentDurationS", 0))
    if abs(mp4_dur - tl_dur) > 0.05:
        conflicts.append(f"duration: mp4={mp4_dur:.3f}s timeline={tl_dur:.3f}s")
    json_gap_speed = float(timeline.get("transitionPlaybackSpeed", 0))
    if json_gap_speed > 0.1:
        conflicts.append(
            f"gap_playback_speed: timeline={json_gap_speed}x — Unity IntroVideoPlaybackHelper must use seg.playbackSpeed from JSON (fixed)"
        )
    report["conflicts"] = conflicts
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if conflicts:
        print("CONFLICTS (metadata vs runtime):")
        for c in conflicts:
            print(f"  • {c}")

    if failures and apply_fix:
        print(f"\nPatched {len(fixes)} gaps — re-run verify to confirm.", file=sys.stderr)
        return 2

    print(f"\nPASSED — all {len(gap_rows)} gaps within luma/color tolerance vs MP4 beats")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
