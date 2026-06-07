"""OpenCV scene analysis for milestone callouts (Unity Scene view timelapse PNGs)."""
from __future__ import annotations

import re
from pathlib import Path
from typing import Any

try:
    import cv2  # type: ignore
    import numpy as np  # type: ignore

    HAS_CV2 = True
except ImportError:
    HAS_CV2 = False


# teachingFocus / phase → accurate on-screen callout (synced to DemoRecapTimeline.json)
TEACHING_FOCUS_LABELS: dict[str, tuple[str, str]] = {
    "seed_tile_origin": ("Seed anchor tile", "ellipse"),
    "editor_queue_drain": ("Bench slots clearing", "ellipse"),
    "nine_tile_height_flatten": ("Tile flatten", "ellipse"),
    "nine_tile_grid_contract": ("3×3 grid contract", "rect"),
    "tile_seam_weld": ("Seam weld", "ellipse"),
    "four_way_seam_height_lock": ("Four-way seam cross", "ellipse"),
    "playable_bowl_rim": ("Play disk rim", "ellipse"),
    "play_disk_base_plane": ("3×3 play disk", "rect"),
    "macro_noise_sculpt": ("Noise sculpt stroke", "ellipse"),
    "terrain_meat_scaffolding": ("Nine-cell wall grid", "rect"),
    "foothill_seam_blend": ("Foothill blend", "ellipse"),
    "cell_first_structure": ("Modular wall grid", "rect"),
    "outer_ring_elevation_sculpt": ("Ring elevation stroke", "ellipse"),
    "perimeter_massing": ("Mountain ring", "ellipse"),
    "labyrinth_pocket_cut": ("Labyrinth grid cut", "ellipse"),
    "staggered_annex_corridors": ("Staggered wall cells", "ellipse"),
    "trail_bench_queue_drain": ("Trail stamp", "ellipse"),
    "trail_bench_grid_queue": ("3×3 bench pad", "ellipse"),
    "cave_mouth_seam_freeze": ("Seam ring", "ellipse"),
    "forest_ring_playable_footprint": ("Forest ring tile", "ellipse"),
}

PHASE_LABELS: dict[str, tuple[str, str]] = {
    "editor queue": ("Editor queue", "ellipse"),
    "nine-tile grid": ("Nine-tile grid", "rect"),
    "tile seams": ("Tile seams", "rect"),
    "play disk grading": ("Play disk", "ellipse"),
    "terrain meat": ("Terrain meat", "ellipse"),
    "foothills": ("Foothills", "ellipse"),
    "mountains": ("Mountain ring", "rect"),
    "south annex": ("South labyrinth", "rect"),
    "trail queue": ("Trail bench", "ellipse"),
    "surface lock": ("Surface authority", "ellipse"),
}


def _norm_box(x0: float, y0: float, x1: float, y1: float) -> list[float]:
    x0, x1 = max(0.0, min(1.0, x0)), max(0.0, min(1.0, x1))
    y0, y1 = max(0.0, min(1.0, y0)), max(0.0, min(1.0, y1))
    if x1 <= x0:
        x1 = min(1.0, x0 + 0.2)
    if y1 <= y0:
        y1 = min(1.0, y0 + 0.2)
    return [x0, y0, x1, y1]


def _scene_roi(img: Any) -> tuple[Any, int, int]:
    """Unity editor: keep central game view, drop chrome bars."""
    h, w = img.shape[:2]
    y0, y1 = int(h * 0.06), int(h * 0.74)
    x0, x1 = int(w * 0.04), int(w * 0.96)
    return img[y0:y1, x0:x1], x0, y0


def _detect_blue_gizmo(scene: Any, off_x: int, off_y: int, fw: int, fh: int) -> dict[str, Any] | None:
    hsv = cv2.cvtColor(scene, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, np.array([95, 90, 70]), np.array([135, 255, 255]))
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    cnts, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not cnts:
        return None
    c = max(cnts, key=cv2.contourArea)
    if cv2.contourArea(c) < 40:
        return None
    x, y, bw, bh = cv2.boundingRect(c)
    cx = (off_x + x + bw / 2) / fw
    cy = (off_y + y + bh / 2) / fh
    return {
        "kind": "arrow",
        "from": [cx, min(0.95, cy + 0.08)],
        "to": [cx, cy],
        "label": "Active tile",
    }


def _detect_grid_core(scene: Any, off_x: int, off_y: int, fw: int, fh: int) -> dict[str, Any] | None:
    gray = cv2.cvtColor(scene, cv2.COLOR_BGR2GRAY)
    gray = cv2.GaussianBlur(gray, (5, 5), 0)
    edges = cv2.Canny(gray, 40, 120)
    edges = cv2.dilate(edges, np.ones((3, 3), np.uint8), iterations=1)
    sh, sw = scene.shape[:2]
    # Center-weighted edge density → grid focal area
    mask = np.zeros((sh, sw), np.float32)
    cy, cx = sh // 2, sw // 2
    for y in range(0, sh, 8):
        for x in range(0, sw, 8):
            patch = edges[y : y + 24, x : x + 24]
            if patch.size == 0:
                continue
            score = float(patch.mean()) / (1.0 + 0.002 * (abs(x - cx) + abs(y - cy)))
            mask[y : y + 8, x : x + 8] = score
    _, _, _, max_loc = cv2.minMaxLoc(mask)
    gx, gy = max_loc
    # Expand around peak to a 3×3 tile-ish box (~35–55% of scene)
    bw = int(sw * 0.42)
    bh = int(sh * 0.42)
    x0 = max(0, gx - bw // 2)
    y0 = max(0, gy - bh // 2)
    x1 = min(sw, x0 + bw)
    y1 = min(sh, y0 + bh)
    return {
        "kind": "rect",
        "box": _norm_box(
            (off_x + x0) / fw,
            (off_y + y0) / fh,
            (off_x + x1) / fw,
            (off_y + y1) / fh,
        ),
        "label": "Nine-tile core",
    }


def _detect_change_blob(scene: Any, off_x: int, off_y: int, fw: int, fh: int) -> dict[str, Any] | None:
    """Fallback: high-contrast sculpt region."""
    lab = cv2.cvtColor(scene, cv2.COLOR_BGR2LAB)
    l = lab[:, :, 0]
    _, th = cv2.threshold(l, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    cnts, _ = cv2.findContours(th, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not cnts:
        return None
    sh, sw = scene.shape[:2]
    cx, cy = sw / 2, sh / 2
    best = None
    best_score = -1.0
    for c in cnts:
        area = cv2.contourArea(c)
        if area < sh * sw * 0.02 or area > sh * sw * 0.65:
            continue
        x, y, bw, bh = cv2.boundingRect(c)
        mx, my = x + bw / 2, y + bh / 2
        dist = abs(mx - cx) + abs(my - cy)
        score = area / (1.0 + dist)
        if score > best_score:
            best_score = score
            best = (x, y, x + bw, y + bh)
    if not best:
        return None
    x0, y0, x1, y1 = best
    return {
        "kind": "ellipse",
        "box": _norm_box(
            (off_x + x0) / fw,
            (off_y + y0) / fh,
            (off_x + x1) / fw,
            (off_y + y1) / fh,
        ),
        "label": "Focus",
    }


def annotation_label_for_milestone(milestone: dict[str, Any]) -> tuple[str, str]:
    """Return (label, kind) from timeline phase / sub / teachingFocus — not generic AI text."""
    focus = (milestone.get("teachingFocus") or "").strip()
    if focus in TEACHING_FOCUS_LABELS:
        return TEACHING_FOCUS_LABELS[focus]

    sub_action = (milestone.get("subAction") or "").strip()
    if sub_action:
        return sub_action, "ellipse"

    phase = (milestone.get("phase") or "").strip().lower()
    for key, (label, kind) in PHASE_LABELS.items():
        if key in phase:
            return label, kind

    chapter = (milestone.get("chapter") or "").strip()
    if milestone.get("beatKind") == "subbeat" and "·" in chapter:
        title = chapter.split("·", 1)[-1].strip()
        if title:
            return title, "ellipse"

    sub = (milestone.get("sub") or "").strip()
    if sub:
        short = re.sub(r"\b(pass|queue|grading)\b", "", sub, flags=re.I).strip(" -")
        if short:
            return short.title()[:24], "ellipse"

    if chapter:
        return chapter[:24], "ellipse"
    return "Focus", "ellipse"


def sync_region_labels_from_timeline(milestones: list[dict[str, Any]]) -> int:
    """Align OpenCV/Cursor box labels with actual build phase from timeline milestones."""
    updated = 0
    for m in milestones:
        label, kind = annotation_label_for_milestone(m)
        regions = m.get("regions")
        if not isinstance(regions, list) or not regions:
            continue
        for r in regions:
            if not isinstance(r, dict):
                continue
            old = (r.get("label") or "").strip()
            if old.lower() in ("focus", "active tile", "seam pass", "seam / sculpt") or old != label:
                r["label"] = label
                if r.get("kind") in ("ellipse", "rect") and kind in ("ellipse", "rect"):
                    r["kind"] = kind
                updated += 1
    return updated


def detect_regions(image_path: Path, milestone: dict[str, Any] | None = None) -> list[dict[str, Any]]:
    """Return 1–2 normalized regions for draw_scene_annotations."""
    milestone = milestone or {}
    chapter = (milestone.get("chapter") or "").lower()
    sub = (milestone.get("sub") or milestone.get("subAction") or "").lower()
    blob = f"{chapter} {sub}"

    if not HAS_CV2:
        return _fallback_regions(blob)

    img = cv2.imread(str(image_path))
    if img is None:
        return _fallback_regions(blob)

    fh, fw = img.shape[:2]
    scene, off_x, off_y = _scene_roi(img)
    regions: list[dict[str, Any]] = []

    if "grid" in blob or "nine" in blob or "tile" in blob:
        core = _detect_grid_core(scene, off_x, off_y, fw, fh)
        if core:
            regions.append(core)
        gizmo = _detect_blue_gizmo(scene, off_x, off_y, fw, fh)
        if gizmo:
            regions.append(gizmo)
    elif "seam" in blob or "bootstrap" in blob:
        ch = _detect_change_blob(scene, off_x, off_y, fw, fh)
        if ch:
            ch["label"] = "Seam / sculpt"
            regions.append(ch)
    else:
        ch = _detect_change_blob(scene, off_x, off_y, fw, fh)
        if ch:
            regions.append(ch)
        gizmo = _detect_blue_gizmo(scene, off_x, off_y, fw, fh)
        if gizmo and len(regions) < 2:
            regions.append(gizmo)

    if not regions:
        regions = _fallback_regions(blob)

    label, kind = annotation_label_for_milestone(milestone)
    for r in regions:
        r["label"] = label
        if r.get("kind") in ("ellipse", "rect", "arrow"):
            r["kind"] = kind if kind in ("ellipse", "rect") else r["kind"]
    return regions[:2]


def _fallback_regions(blob: str) -> list[dict[str, Any]]:
    if "grid" in blob or "nine" in blob:
        return [{"kind": "rect", "box": [0.22, 0.20, 0.78, 0.68], "label": "Nine-tile core"}]
    if "seam" in blob:
        return [{"kind": "rect", "box": [0.18, 0.18, 0.82, 0.70], "label": "Seam watch"}]
    return [{"kind": "ellipse", "box": [0.28, 0.22, 0.72, 0.62], "label": "Focus"}]


def apply_opencv_to_milestones(
    run_dir: Path,
    milestones: list[dict[str, Any]],
    frames: list[Path],
) -> None:
    for m in milestones:
        fi = min(max(0, int(m.get("frame", 0))), len(frames) - 1)
        path = frames[fi]
        regions = detect_regions(path, m)
        m["regions"] = regions
        m["annotationSource"] = "opencv"
        m["regionsVerified"] = HAS_CV2
    sync_region_labels_from_timeline(milestones)
