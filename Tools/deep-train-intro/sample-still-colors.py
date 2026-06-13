#!/usr/bin/env python3
"""Sample keyframe still colors for per-gap seamless transition grading."""

from __future__ import annotations

import json
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Pillow required: pip install pillow", file=sys.stderr)
    raise


def sample_still(path: Path) -> dict:
    img = Image.open(path).convert("RGB")
    img = img.resize((320, int(320 * img.height / img.width)), Image.Resampling.BILINEAR)
    px = list(img.getdata())
    n = max(len(px), 1)
    r = sum(p[0] for p in px) / n / 255.0
    g = sum(p[1] for p in px) / n / 255.0
    b = sum(p[2] for p in px) / n / 255.0
    return {"avgRgb": [round(r, 5), round(g, 5), round(b, 5)]}


def grade_to_reference(avg: list[float], ref: list[float]) -> list[float]:
    out = []
    for i in range(3):
        v = ref[i] / max(avg[i], 1e-4)
        out.append(round(max(0.88, min(1.12, v)), 5))
    return out


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    intro_dir = root / "Assets/Resources/DeepTrainAcademy/Intro"
    out = root / "Assets/StreamingAssets/DeepTrainAcademy/intro_still_colors.json"

    stills = []
    for i in range(1, 11):
        num = f"{i:02d}"
        path = intro_dir / f"intro_{num}.png"
        if not path.exists():
            print(f"missing {path}", file=sys.stderr)
            return 1
        avg = sample_still(path)["avgRgb"]
        stills.append({"id": f"beat_{num}", "avgRgb": avg})

    ref = [
        sum(s["avgRgb"][0] for s in stills) / len(stills),
        sum(s["avgRgb"][1] for s in stills) / len(stills),
        sum(s["avgRgb"][2] for s in stills) / len(stills),
    ]

    for s in stills:
        s["grade"] = grade_to_reference(s["avgRgb"], ref)

    gaps = []
    for i in range(1, 10):
        a = f"{i:02d}"
        b = f"{i + 1:02d}"
        from_s = next(x for x in stills if x["id"] == f"beat_{a}")
        to_s = next(x for x in stills if x["id"] == f"beat_{b}")
        gaps.append(
            {
                "id": f"gap_{a}_{b}",
                "fromBeat": f"beat_{a}",
                "toBeat": f"beat_{b}",
                "fromGrade": from_s["grade"],
                "toGrade": to_s["grade"],
            }
        )

    payload = {
        "version": 1,
        "referenceRgb": [round(x, 5) for x in ref],
        "stills": stills,
        "gaps": gaps,
    }

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {out} — {len(stills)} stills, {len(gaps)} gap profiles", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
