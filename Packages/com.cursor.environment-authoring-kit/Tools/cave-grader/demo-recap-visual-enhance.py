"""Shared scene enhancement + caption-driven annotations for demo recap frames."""
from __future__ import annotations

import math
from typing import Any

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

# Used by compose-demo-recap (imported as module)
W = 1280
HEADER = 42
BAR = 196
SCENE_BOTTOM = 720 - BAR


def true_color_grade(img: Image.Image, *, upscale: float = 2.0) -> Image.Image:
    """Richer, deeper color — avoid blown highlights and harsh contrast stacks."""
    w, h = img.size
    uw, uh = max(w, int(w * upscale)), max(h, int(h * upscale))
    up = img.resize((uw, uh), Image.Resampling.LANCZOS)
    up = ImageEnhance.Color(up).enhance(1.10)
    up = ImageEnhance.Contrast(up).enhance(1.02)
    up = ImageEnhance.Brightness(up).enhance(0.94)
    up = up.filter(ImageFilter.UnsharpMask(radius=1.1, percent=88, threshold=4))
    return up.resize((w, h), Image.Resampling.LANCZOS)


def enhance_scene_image(img: Image.Image, *, upscale: float = 2.0) -> Image.Image:
    return true_color_grade(img, upscale=upscale)


def apply_cinematic_motion(
    img: Image.Image,
    t: float,
    *,
    mode: str = "auto",
    seed: int = 0,
) -> Image.Image:
    """Slow dolly/pan on an oversized crop — simulates Scene camera movement on stills."""
    t = max(0.0, min(1.0, t))
    w, h = img.size
    modes = ("dolly_in", "pan_right", "pan_left", "crane_up", "dolly_out")
    pick = mode if mode in modes else modes[seed % len(modes)]

    pad = 1.10
    big = img.resize((max(w, int(w * pad)), max(h, int(h * pad))), Image.Resampling.LANCZOS)
    bw, bh = big.size

    if pick == "dolly_in":
        z0, z1 = 1.0, 0.97
        z = z0 + (z1 - z0) * t
        cw, ch = int(w * z), int(h * z)
        cx = (bw - cw) // 2
        cy = (bh - ch) // 2
    elif pick == "dolly_out":
        z0, z1 = 0.90, 1.0
        z = z0 + (z1 - z0) * t
        cw, ch = int(w * z), int(h * z)
        cx = (bw - cw) // 2
        cy = (bh - ch) // 2
    elif pick == "pan_right":
        cw, ch = w, h
        cx = int((bw - cw) * 0.04 * t)
        cy = (bh - ch) // 2
    elif pick == "pan_left":
        cw, ch = w, h
        cx = int((bw - cw) * (1.0 - 0.04 * t))
        cy = (bh - ch) // 2
    else:  # crane_up
        cw, ch = w, h
        cx = (bw - cw) // 2
        cy = int((bh - ch) * (1.0 - 0.03 * t))

    cx = max(0, min(cx, bw - cw))
    cy = max(0, min(cy, bh - ch))
    out = big.crop((cx, cy, cx + cw, cy + ch))
    return out.resize((w, h), Image.Resampling.LANCZOS)


def ffmpeg_broadcast_color_vf(*, enabled: bool = True) -> str:
    """FFmpeg color — neutral HD, not lifted/faded."""
    if not enabled:
        return "scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720"
    return (
        "scale=2560:1440:force_original_aspect_ratio=increase:flags=lanczos,"
        "crop=2560:1440,"
        "scale=1280:720:flags=lanczos,"
        "eq=gamma=1.07:contrast=0.96:saturation=1.20:brightness=-0.07,"
        "colorbalance=rs=-0.03:gs=0.05:bs=0.08:rm=0.0:gm=0.02:bm=0.04,"
        "hue=s=1.04,"
        "unsharp=5:5:0.22:5:5:0.0"
    )


def ffmpeg_timelapse_enhance_chain(
    pts: float,
    output_fps: float,
    frame_count: int,
    *,
    enabled: bool = True,
) -> str:
    """Six ffmpeg enhancements: upscale/downscale, true-color eq, denoise, sharpen, motion fps, setpts."""
    if enabled:
        base = ffmpeg_broadcast_color_vf(enabled=True) + f",setpts={pts}*PTS"
    else:
        base = (
            "scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,"
            f"setpts={pts}*PTS"
        )
    # Motion interpolation to target fps (heavy; cap chunk length for encode time).
    if frame_count <= 64:
        return (
            base
            + f",minterpolate=fps={output_fps:.3f}:mi_mode=mci:mc_mode=aobmc:me_mode=epzs:vsbmc=1"
        )
    if frame_count <= 140:
        return base + f",minterpolate=fps={output_fps:.3f}:mi_mode=blend"
    return base + f",fps={output_fps:.3f}"


def append_edge_fades(vf: str, duration: float, fade: float = 0.45) -> str:
    fade = min(fade, max(0.08, duration * 0.45))
    if duration < fade * 2.2:
        return vf
    out_start = max(0.0, duration - fade)
    return vf + f",fade=t=in:st=0:d={fade:.3f},fade=t=out:st={out_start:.3f}:d={fade:.3f}"


CHAPTER_FOCUS: dict[str, tuple[str, list[float], str]] = {
    "session bootstrap": ("ellipse", [0.22, 0.18, 0.78, 0.68], "Build overview"),
    "grid contract": ("rect", [0.08, 0.12, 0.92, 0.86], "Terrain grid"),
    "seam invariants": ("rect", [0.12, 0.14, 0.88, 0.84], "Tile seams"),
    "play disk": ("ellipse", [0.32, 0.22, 0.68, 0.58], "Play disk"),
    "terrain meat": ("ellipse", [0.24, 0.18, 0.76, 0.72], "Terrain meat"),
    "foothills": ("ellipse", [0.10, 0.16, 0.90, 0.80], "Foothills"),
    "mountain ring": ("rect", [0.06, 0.08, 0.94, 0.82], "Mountain ring"),
    "labyrinth annex": ("rect", [0.52, 0.46, 0.94, 0.88], "South labyrinth"),
    "trail bench": ("ellipse", [0.40, 0.32, 0.60, 0.52], "Trail bench"),
    "surface phase": ("ellipse", [0.30, 0.22, 0.70, 0.62], "Surface authority"),
    "surface lock": ("ellipse", [0.30, 0.22, 0.70, 0.62], "Surface authority"),
    "pipeline step": ("ellipse", [0.28, 0.20, 0.72, 0.65], "Surface phase"),
    "final capture": ("rect", [0.16, 0.12, 0.84, 0.74], "Final capture"),
    "recording tail": ("rect", [0.16, 0.12, 0.84, 0.74], "Final capture"),
}


def single_focus_region(milestone: dict[str, Any]) -> list[dict[str, Any]]:
    chapter = (milestone.get("chapter") or "").lower()
    focus = (milestone.get("teachingFocus") or "").replace("_", " ")
    for key, (kind, box, label) in CHAPTER_FOCUS.items():
        if key in chapter or key in focus:
            return [{"kind": kind, "box": box, "label": label}]
    label = (milestone.get("line1") or milestone.get("chapter") or "Focus")[:24]
    return [{"kind": "ellipse", "box": [0.30, 0.22, 0.70, 0.60], "label": label}]


def regions_from_milestone(
    milestone: dict[str, Any],
    *,
    ai_only: bool = True,
    chapter_focus: bool = False,
) -> list[dict[str, Any]]:
    """One focus region — chapter heuristics beat stale vision boxes when chapter_focus set."""
    if milestone.get("annotationSource") in ("opencv", "cursor") and milestone.get("regions"):
        raw = milestone.get("regions")
        if isinstance(raw, list) and raw:
            return [r for r in raw[:2] if isinstance(r, dict)]
    if chapter_focus or milestone.get("annotationSource") == "chapter":
        return single_focus_region(milestone)[:1]
    raw = milestone.get("regions")
    if isinstance(raw, list) and raw and not chapter_focus:
        out: list[dict[str, Any]] = []
        for r in raw[:1]:
            if isinstance(r, dict) and (r.get("label") or r.get("box")):
                out.append(r)
        if out and not ai_only:
            return out
        if out and ai_only and milestone.get("regionsVerified"):
            return out
    if ai_only:
        return single_focus_region(milestone)[:1]
    return annotation_regions_for_milestone(milestone)[:2]


def annotation_regions_for_milestone(milestone: dict[str, Any]) -> list[dict[str, Any]]:
    chapter = (milestone.get("chapter") or "").lower()
    sub = (milestone.get("sub") or "").lower()
    blob = " ".join(
        [
            chapter,
            sub,
            milestone.get("line1") or "",
            milestone.get("line2") or "",
            milestone.get("line3") or "",
            milestone.get("phase") or "",
        ]
    ).lower()

    regions: list[dict[str, Any]] = []

    def box(x0: float, y0: float, x1: float, y1: float, label: str, kind: str = "ellipse") -> None:
        regions.append({"kind": kind, "box": [x0, y0, x1, y1], "label": label})

    if "grid" in chapter or "grid" in blob or "nine-tile" in blob or "tile" in blob:
        for t in (1 / 3, 2 / 3):
            regions.append({"kind": "line_v", "pos": t, "label": "Grid"})
            regions.append({"kind": "line_h", "pos": t, "label": ""})
        box(0.08, 0.12, 0.92, 0.88, "Fullworld grid", "rect")

    if "seam" in chapter or "seam" in blob or "border" in blob:
        box(0.12, 0.14, 0.88, 0.86, "Seam watch", "rect")
        for t in (0.33, 0.66):
            regions.append({"kind": "line_v", "pos": t, "label": "Seam"})
            regions.append({"kind": "line_h", "pos": t, "label": ""})

    if "play disk" in blob or ("disk" in chapter and "play" in blob) or chapter == "play disk grading":
        box(0.30, 0.20, 0.70, 0.58, "Play disk", "ellipse")

    if "meat" in chapter or "meat" in blob or "heightfield" in blob:
        box(0.22, 0.16, 0.78, 0.70, "Terrain meat", "ellipse")

    if "foothill" in chapter or "foothill" in blob:
        box(0.10, 0.18, 0.90, 0.78, "Foothills", "ellipse")

    if "mountain" in chapter or "mountain" in blob or "silhouette" in blob:
        box(0.05, 0.08, 0.95, 0.82, "Mountain ring", "rect")

    if "labyrinth" in chapter or "labyrinth" in blob or "maze" in blob or "annex" in blob:
        box(0.52, 0.48, 0.94, 0.88, "South annex / labyrinth", "rect")

    if "trail" in chapter or "trail" in blob or "radial" in blob or "queue" in blob:
        box(0.38, 0.30, 0.62, 0.52, "Trail bench focus", "ellipse")
        regions.append({"kind": "arrow", "from": [0.50, 0.52], "to": [0.50, 0.38], "label": "Queue / trail"})

    if "lock" in chapter or "lock" in blob or "surface" in chapter and "pending" in chapter:
        box(0.34, 0.24, 0.66, 0.62, "Surface authority", "ellipse")

    if "step 1" in blob or "1/122" in blob or "pipeline step" in chapter:
        regions.append({"kind": "badge", "pos": [0.04, 0.14], "label": "Surface phase"})

    if "tail" in chapter or "final" in blob or "last" in chapter:
        box(0.18, 0.14, 0.82, 0.72, "Final capture", "rect")

    if "bootstrap" in chapter or "session" in chapter:
        box(0.20, 0.16, 0.80, 0.68, "Scene overview", "ellipse")

    if not regions:
        box(0.28, 0.22, 0.72, 0.60, chapter[:28] or "Focus", "ellipse")

    return regions


def _alpha_color(rgb: tuple[int, int, int], alpha: float) -> tuple[int, int, int]:
    a = max(0.0, min(1.0, alpha))
    return (int(rgb[0] * a), int(rgb[1] * a), int(rgb[2] * a))


def draw_scene_annotations(
    draw: ImageDraw.ImageDraw,
    scene_w: int,
    scene_h: int,
    origin_y: int,
    regions: list[dict[str, Any]],
    accent: tuple[int, int, int],
    *,
    alpha: float = 1.0,
) -> None:
    if alpha <= 0.02 or not regions:
        return
    glow = _alpha_color(
        (min(255, accent[0] + 60), min(255, accent[1] + 40), min(255, accent[2] + 30)),
        alpha,
    )
    accent_a = _alpha_color(accent, alpha)
    label_font_size = 11

    for r in regions:
        kind = r.get("kind", "ellipse")
        label = (r.get("label") or "").strip()

        if kind == "line_v":
            x = int(r["pos"] * scene_w)
            draw.line([(x, origin_y), (x, origin_y + scene_h)], fill=glow, width=max(1, int(2 * alpha)))
            continue
        if kind == "line_h":
            y = origin_y + int(r["pos"] * scene_h)
            draw.line([(0, y), (scene_w, y)], fill=glow, width=max(1, int(2 * alpha)))
            continue
        if kind == "arrow":
            fx, fy = r["from"]
            tx, ty = r["to"]
            x0, y0 = int(fx * scene_w), origin_y + int(fy * scene_h)
            x1, y1 = int(tx * scene_w), origin_y + int(ty * scene_h)
            draw.line([(x0, y0), (x1, y1)], fill=glow, width=3)
            ang = math.atan2(y1 - y0, x1 - x0)
            for da in (2.6, -2.6):
                ax = x1 - int(14 * math.cos(ang - da))
                ay = y1 - int(14 * math.sin(ang - da))
                draw.line([(x1, y1), (ax, ay)], fill=glow, width=3)
            if label:
                draw.rounded_rectangle([x0 - 4, y0 - 22, x0 + 120, y0 - 4], radius=5, fill=accent_a)
                draw.text((x0 + 4, y0 - 20), label[:22], fill=(255, 255, 255))
            continue
        if kind == "badge":
            bx, by = r["pos"]
            x, y = int(bx * scene_w), origin_y + int(by * scene_h)
            draw.rounded_rectangle([x, y, x + 140, y + 26], radius=8, fill=accent_a)
            draw.text((x + 8, y + 5), label[:20], fill=(255, 255, 255))
            continue

        box = r.get("box") or [0.25, 0.2, 0.75, 0.65]
        x0 = int(box[0] * scene_w)
        y0 = origin_y + int(box[1] * scene_h)
        x1 = int(box[2] * scene_w)
        y1 = origin_y + int(box[3] * scene_h)

        if kind == "rect":
            draw.rounded_rectangle([x0 - 3, y0 - 3, x1 + 3, y1 + 3], radius=10, outline=glow, width=2)
            draw.rounded_rectangle([x0, y0, x1, y1], radius=8, outline=accent_a, width=max(1, int(3 * alpha)))
        else:
            draw.ellipse([x0 - 4, y0 - 4, x1 + 4, y1 + 4], outline=glow, width=max(1, int(2 * alpha)))
            draw.ellipse([x0, y0, x1, y1], outline=accent_a, width=max(1, int(3 * alpha)))

        if label:
            lx, ly = x0, max(origin_y + 4, y0 - 24)
            tw = len(label) * 7 + 16
            draw.rounded_rectangle([lx, ly, lx + tw, ly + 20], radius=6, fill=accent_a)
            draw.text((lx + 6, ly + 3), label[:24], fill=(255, 255, 255))
