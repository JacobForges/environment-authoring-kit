"""Cheap 'what changed' hints between milestone holds (opencv or PIL fallback)."""
from __future__ import annotations

from pathlib import Path
from typing import Any

try:
    import cv2  # type: ignore
    import numpy as np  # type: ignore

    HAS_CV = True
except ImportError:
    HAS_CV = False


def _diff_score(a_path: Path, b_path: Path) -> float:
    if HAS_CV:
        a = cv2.imread(str(a_path))
        b = cv2.imread(str(b_path))
        if a is None or b is None:
            return 0.0
        h, w = min(a.shape[0], b.shape[0]), min(a.shape[1], b.shape[1])
        a = cv2.resize(a, (w, h))
        b = cv2.resize(b, (w, h))
        g = cv2.absdiff(cv2.cvtColor(a, cv2.COLOR_BGR2GRAY), cv2.cvtColor(b, cv2.COLOR_BGR2GRAY))
        return float(g.mean()) / 255.0
    from PIL import Image, ImageChops

    a = Image.open(a_path).convert("L").resize((320, 180))
    b = Image.open(b_path).convert("L").resize((320, 180))
    diff = ImageChops.difference(a, b)
    return sum(diff.getdata()) / (320 * 180 * 255)


def enrich_milestones_with_diff(frames: list[Path], milestones: list[dict[str, Any]]) -> None:
    if len(milestones) < 2:
        return
    ordered = sorted(milestones, key=lambda m: int(m.get("frame", 0)))
    prev_path: Path | None = None
    for m in ordered:
        fi = min(int(m.get("frame", 0)), len(frames) - 1)
        cur = frames[fi]
        if prev_path is not None:
            score = _diff_score(prev_path, cur)
            if score < 0.02:
                hint = "Scene is nearly unchanged from the prior hold — queue may be draining."
            elif score < 0.06:
                hint = "Subtle terrain edits since the last hold — compare tile borders."
            else:
                hint = "Clear terrain change since the last hold — new sculpt or grid work landed."
            # On-screen hint only (line3) — never append to line2 (was read aloud twice).
            if not m.get("line3"):
                m["line3"] = hint
        prev_path = cur
