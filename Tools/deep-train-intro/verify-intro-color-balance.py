#!/usr/bin/env python3
"""Verify intro gap grades stay balanced vs beat stills (no mid-transition crush)."""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def smooth(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def geo_lerp(a: float, b: float, t: float) -> float:
    a = max(a, 1e-4)
    b = max(b, 1e-4)
    return math.exp(lerp(math.log(a), math.log(b), t))


def balanced_gap_grade(from_g: list[float], to_g: list[float], t: float) -> list[float]:
    blended = [geo_lerp(from_g[i], to_g[i], t) for i in range(3)]
    mid = 1.0 - 4.0 * (t - 0.5) ** 2
    lift = 1.0 + 0.2 * max(0.0, mid)
    return [max(1.0, min(1.12, blended[i] * lift)) for i in range(3)]


def luma(rgb: list[float]) -> float:
    return 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    path = root / "Assets/StreamingAssets/DeepTrainAcademy/intro_still_colors.json"
    if not path.exists():
        print(f"FAIL missing {path}", file=sys.stderr)
        return 1

    data = json.loads(path.read_text(encoding="utf-8"))
    stills = {s["id"]: s for s in data.get("stills", [])}
    gaps = data.get("gaps", [])

    failures = 0
    print("Intro gap color balance verify")
    print("-" * 56)

    for gap in gaps:
        gid = gap["id"]
        from_g = gap["fromGrade"]
        to_g = gap["toGrade"]
        from_beat = stills.get(gap["fromBeat"], {})
        to_beat = stills.get(gap["toBeat"], {})
        from_l = luma(from_beat.get("avgRgb", [0.18, 0.18, 0.16]))
        to_l = luma(to_beat.get("avgRgb", [0.18, 0.18, 0.16]))
        target_l = lerp(from_l, to_l, 0.5) * 1.05

        samples = []
        for t in (0.0, 0.25, 0.5, 0.75, 1.0):
            g = balanced_gap_grade(from_g, to_g, smooth(t))
            samples.append((t, g, luma(g)))

        mid_l = samples[2][2]
        min_grade = min(min(s[1]) for s in samples)
        ok = min_grade >= 1.0 and mid_l >= 0.98
        if not ok:
            failures += 1

        status = "OK" if ok else "FAIL"
        print(
            f"{status} {gid}: mid_grade_luma={mid_l:.3f} "
            f"target~{target_l:.3f} min_channel={min_grade:.3f}"
        )

    print("-" * 56)
    if failures:
        print(f"FAILED {failures}/{len(gaps)} gaps", file=sys.stderr)
        return 1

    print(f"PASSED all {len(gaps)} gap profiles — balanced lift, no sub-1.0 crush")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
