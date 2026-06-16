#!/usr/bin/env python3
"""Unity Editor Scene-view concept preview for AI Build Planner — tiles, trails, props, platforms."""
from __future__ import annotations

import hashlib
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont

W, H = 1536, 1024
CONCEPT_DENSITY_NAME = "concept-density.png"
CONCEPT_LAYOUT_DETAIL_NAME = "concept-layout-detail.png"
CONCEPT_DENSITY_DETAIL_NAME = "concept-density-detail.png"
DETAIL_W, DETAIL_H = 1152, 864
DETAIL_ZOOM = 2.45

# Must match CaveBuildPlannerConceptGuide.ClassifyConceptColor sampling.
DENSITY_TRAIL_RGB = (184, 148, 88)
DENSITY_PROP_RGB = (210, 180, 90)
DENSITY_PROP_HOT_RGB = (255, 215, 60)

COLORS = {
    "bg": (18, 22, 30),
    "panel": (28, 34, 48),
    "play": (72, 140, 88),
    "play_open": (100, 175, 110),
    "lab": (34, 110, 72),
    "lab_path": (48, 175, 95),
    "lab_wall": (22, 75, 48),
    "foot": (88, 105, 72),
    "peak": (110, 95, 82),
    "horizon": (55, 75, 95),
    "water": (66, 165, 245),
    "trail": (210, 165, 90),
    "mouth": (40, 48, 55),
    "island": (90, 110, 140),
    "island_edge": (140, 160, 200),
    "prop": (210, 180, 90),
    "npc": (100, 170, 255),
    "enemy": (230, 90, 90),
    "collect": (255, 215, 60),
    "spawn": (120, 255, 160),
    "text": (235, 240, 250),
    "muted": (170, 180, 200),
    "dim": (130, 140, 160),
}


def _load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    names = (
        ["/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/Library/Fonts/Arial Bold.ttf"]
        if bold
        else ["/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"]
    )
    for path in names:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def _wrap(draw: ImageDraw.ImageDraw, text: str, font, max_w: int) -> list[str]:
    words = (text or "").split()
    if not words:
        return []
    lines: list[str] = []
    cur = words[0]
    for word in words[1:]:
        test = f"{cur} {word}"
        if draw.textlength(test, font=font) <= max_w:
            cur = test
        else:
            lines.append(cur)
            cur = word
    lines.append(cur)
    return lines


def _play_cell_center(gx0: int, gy0: int, tile: int, row: int, col: int) -> tuple[int, int]:
    return gx0 + col * tile + tile // 2, gy0 + row * tile + tile // 2


_PLAY_TILE = {
    "c": (1, 1),
    "center": (1, 1),
    "hub-center": (1, 1),
    "play-center": (1, 1),
    "middle": (1, 1),
    "n": (0, 1),
    "north": (0, 1),
    "s": (2, 1),
    "south": (2, 1),
    "e": (1, 2),
    "east": (1, 2),
    "w": (1, 0),
    "west": (1, 0),
    "nw": (0, 0),
    "ne": (0, 2),
    "sw": (2, 0),
    "se": (2, 2),
    "hub-center": (1, 1),
    "hub-north-edge": (0, 1),
    "hub-south-edge": (2, 1),
    "hub-east-edge": (1, 2),
    "hub-west-edge": (1, 0),
}


def _coerce_play_row_col(row: Any, col: Any, default: tuple[int, int] = (1, 1)) -> tuple[int, int]:
    for val in (row, col):
        if isinstance(val, str):
            key = val.strip().lower().replace("_", "-")
            if key in _PLAY_TILE:
                return _PLAY_TILE[key]
    try:
        r = int(row) if row is not None and str(row).strip() != "" else default[0]
        c = int(col) if col is not None and str(col).strip() != "" else default[1]
        # 9×9 shell: play disk occupies rows/cols 3–5 (centered 3×3).
        if 3 <= r <= 5 and 3 <= c <= 5:
            return r - 3, c - 3
        return max(0, min(2, r)), max(0, min(2, c))
    except (TypeError, ValueError):
        return default


def _coerce_island_slot(slot: Any, default: int = 0) -> int:
    if slot is None:
        return default
    if isinstance(slot, bool):
        return default
    if isinstance(slot, int):
        return max(0, min(3, slot))
    if isinstance(slot, float):
        return max(0, min(3, int(slot)))
    s = str(slot).strip().lower().replace("_", "-")
    if s.isdigit():
        return max(0, min(3, int(s)))
    named = {
        "hub-center": 0,
        "hub-north-edge": 1,
        "hub-south-edge": 2,
        "hub-east-edge": 3,
        "hub-west-edge": 4,
        "play-center": 0,
        "center": 0,
        "mid": 0,
        "middle": 0,
        "north": 1,
        "n": 1,
        "south": 2,
        "s": 2,
        "east": 3,
        "e": 3,
        "west": 0,
        "w": 0,
    }
    if s in named:
        return max(0, min(3, named[s]))
    if s.startswith("hub-"):
        for key, val in (
            ("north", 1),
            ("south", 2),
            ("east", 3),
            ("west", 0),
            ("center", 0),
        ):
            if key in s:
                return max(0, min(3, val))
    digest = int(hashlib.md5(s.encode()).hexdigest()[:4], 16)
    return digest % 4


def _coerce_marker_slot(slot: Any, default: int = 0) -> int:
    """Scatter index for play-tile markers — LLM may emit names like hub-north-edge."""
    if slot is None:
        return default
    if isinstance(slot, bool):
        return default
    if isinstance(slot, int):
        return max(0, min(20, slot))
    if isinstance(slot, float):
        return max(0, min(20, int(slot)))
    s = str(slot).strip().lower().replace("_", "-")
    if s.isdigit():
        return max(0, min(20, int(s)))
    named = {
        "hub-center": 4,
        "hub-north-edge": 1,
        "hub-south-edge": 7,
        "hub-east-edge": 5,
        "hub-west-edge": 3,
        "north": 1,
        "south": 7,
        "east": 5,
        "west": 3,
        "center": 4,
    }
    if s in named:
        return named[s]
    if s.startswith("hub-"):
        for key, val in (
            ("north", 1),
            ("south", 7),
            ("east", 5),
            ("west", 3),
            ("center", 4),
        ):
            if key in s:
                return val
    digest = int(hashlib.md5(s.encode()).hexdigest()[:4], 16)
    return digest % 9


def _sanitize_layout_plan(layout: dict[str, Any]) -> dict[str, Any]:
    out = dict(layout)
    markers: list[dict[str, Any]] = []
    for m in layout.get("markers") or []:
        if not isinstance(m, dict):
            continue
        item = dict(m)
        zone = str(item.get("zone", "play"))
        if zone == "play":
            row, col = _coerce_play_row_col(item.get("row"), item.get("col"))
            item["row"], item["col"] = row, col
            item["slot"] = _coerce_marker_slot(item.get("slot"))
        elif zone == "island":
            item["slot"] = _coerce_island_slot(item.get("slot"))
            item["dir"] = str(item.get("dir", "N")).upper()[:1] or "N"
        else:
            item["slot"] = _coerce_marker_slot(item.get("slot"))
        markers.append(item)
    out["markers"] = markers
    return out


def derive_layout_plan(brief: dict[str, Any], cfg: dict[str, Any]) -> dict[str, Any]:
    """Build layoutPlan from brief.layoutPlan or infer from config + summary."""
    explicit = brief.get("layoutPlan")
    has_explicit = isinstance(explicit, dict) and bool(explicit.get("markers"))

    summary = (brief.get("summary") or cfg.get("label") or "").lower()
    goals = " ".join(brief.get("userGoals") or []).lower()
    text = f"{summary} {goals}"

    labyrinth = bool(cfg.get("mountainLabyrinth")) or "labyrinth" in text or "maze" in text
    floating = bool(cfg.get("floatingTiles")) or "float" in text
    props_on = bool(cfg.get("playDiskProps")) or "prop" in text
    has_npc = "npc" in text or "character" in text
    has_enemy = "enem" in text or "combat" in text
    has_collect = "collect" in text or "loot" in text or "item" in text
    has_trails = bool(cfg.get("surfaceTrails")) or "trail" in text

    markers: list[dict[str, Any]] = [
        {"kind": "spawn", "zone": "play", "row": 0, "col": 1, "label": "Player spawn"},
    ]
    if props_on:
        markers.extend(
            [
                {"kind": "prop", "zone": "play", "row": 0, "col": 0, "label": "prop A"},
                {"kind": "prop", "zone": "play", "row": 2, "col": 2, "label": "prop B"},
                {"kind": "prop", "zone": "play", "row": 1, "col": 0, "label": "scatter"},
            ]
        )
    if has_npc:
        markers.append({"kind": "npc", "zone": "play", "row": 1, "col": 2, "label": "host NPC"})
    if has_collect:
        markers.extend(
            [
                {"kind": "collectible", "zone": "play", "row": 0, "col": 2, "label": "pickup"},
                {"kind": "collectible", "zone": "play", "row": 2, "col": 1, "label": "chest"},
            ]
        )
    if has_enemy and not floating:
        markers.append({"kind": "enemy", "zone": "play", "row": 2, "col": 0, "label": "patrol"})

    islands: list[dict[str, Any]] = []
    trails: list[dict[str, Any]] = []
    if floating:
        island_defs = [
            ("N", "North island", 1, 0, 2, 1),
            ("E", "East island", 2, 1, 1, 1),
            ("S", "South island", 2, 0, 2, 0),
            ("W", "West island", 1, 1, 1, 1),
        ]
        for d, label, enemies, npcs, props, collects in island_defs:
            islands.append(
                {
                    "dir": d,
                    "label": label,
                    "enemies": enemies if has_enemy else 0,
                    "npcs": npcs if has_npc else 0,
                    "props": props if props_on else 0,
                    "collectibles": collects if has_collect else 0,
                }
            )
            if has_enemy:
                for slot in range(min(enemies, 3)):
                    markers.append(
                        {
                            "kind": "enemy",
                            "zone": "island",
                            "dir": d,
                            "slot": slot,
                            "label": f"enemy {d}{slot + 1}",
                        }
                    )
            if has_npc and npcs:
                markers.append({"kind": "npc", "zone": "island", "dir": d, "slot": 0, "label": f"NPC {d}"})
            if has_collect and collects:
                markers.append(
                    {"kind": "collectible", "zone": "island", "dir": d, "slot": 1, "label": f"loot {d}"}
                )
            if props_on and props:
                markers.append({"kind": "prop", "zone": "island", "dir": d, "slot": 2, "label": f"props {d}"})
            trails.append({"from": "play-center", "to": f"island-{d}", "label": f"bridge → {d}"})

    if has_trails and not trails:
        trails = [
            {"from": "play-south", "to": "foothill", "label": "south trail"},
            {"from": "play-east", "to": "ring-east", "label": "perimeter trail"},
        ]

    cave_mouths: list[dict[str, Any]] = []
    if cfg.get("use3DCaveSystem") or "cave" in text:
        cave_mouths.append({"pos": "play-north-center", "label": "cave mouth"})

    tc = int(cfg.get("tileCount", 9))
    if tc == 9:
        grid_note = "9-tile play disk"
    elif tc == 13:
        grid_note = "13-tile floating islands"
    elif tc == 81:
        grid_note = "81-tile shell"
    elif tc >= 289:
        grid_note = "289 extended"
    else:
        grid_note = f"{tc}-tile (normalized to play disk)"

    ring_props: list[dict[str, Any]] = []
    if tc > 9 and not floating:
        if props_on:
            ring_props = [
                {"ring": 2, "density": "medium", "trails": bool(has_trails), "label": "foothill props"},
                {"ring": 3, "density": "low", "trails": False, "label": "peak sparse"},
            ]
        if tc > 81:
            ring_props.extend(
                [
                    {"ring": 5, "density": "low", "trails": False, "label": "mid open"},
                    {"ring": 7, "density": "none", "trails": False, "label": "horizon"},
                ]
            )
        elif cfg.get("outerRingMountains"):
            ring_props.append({"ring": 4, "density": "none", "trails": False, "label": "horizon ring"})

    inferred = {
        "gridNote": grid_note,
        "spawn": {"row": 0, "col": 1, "label": "Player spawn"},
        "playDisk": {
            "labyrinth": labyrinth,
            "labyrinthNote": "rows 1–2 maze (6 tiles) · row 0 north flat overlook" if labyrinth else "open flat 3×3",
        },
        "markers": markers,
        "ringProps": ring_props,
        "trails": trails,
        "islands": islands,
        "caveMouths": cave_mouths,
        "surfaceWater": bool(cfg.get("surfaceWater")),
        "surfaceTrails": bool(cfg.get("surfaceTrails")) or bool(trails),
    }

    if not has_explicit:
        return inferred

    base = _sanitize_layout_plan(explicit)
    if not base.get("ringProps"):
        base["ringProps"] = inferred.get("ringProps") or []
    if not base.get("trails"):
        base["trails"] = inferred.get("trails") or []
    if not base.get("islands") and inferred.get("islands"):
        base["islands"] = inferred["islands"]
    if not base.get("gridNote"):
        base["gridNote"] = inferred.get("gridNote")
    return base


def _draw_marker(draw: ImageDraw.ImageDraw, x: int, y: int, kind: str, label: str, font) -> None:
    r = 10
    if kind == "spawn":
        draw.ellipse([x - r, y - r, x + r, y + r], fill=COLORS["spawn"], outline=(20, 80, 40), width=2)
        draw.text((x, y - r - 12), "SPAWN", fill=COLORS["spawn"], anchor="mm", font=font)
    elif kind == "prop":
        draw.rectangle([x - 8, y - 8, x + 8, y + 8], fill=COLORS["prop"], outline=(120, 90, 40))
    elif kind == "npc":
        draw.ellipse([x - 9, y - 9, x + 9, y + 9], fill=COLORS["npc"], outline=(40, 80, 140))
        draw.ellipse([x - 4, y - 12, x + 4, y - 4], fill=COLORS["npc"])
    elif kind == "enemy":
        draw.polygon([(x, y - 10), (x + 10, y + 8), (x - 10, y + 8)], fill=COLORS["enemy"], outline=(120, 30, 30))
    elif kind == "collectible":
        draw.ellipse([x - 7, y - 7, x + 7, y + 7], fill=COLORS["collect"], outline=(180, 140, 20))
        draw.text((x, y), "★", fill=(80, 60, 0), anchor="mm", font=font)
    if label and kind != "spawn":
        draw.text((x, y + 14), label[:14], fill=COLORS["dim"], anchor="mm", font=font)


def _island_center(cx: int, cy: int, cell: int, direction: str) -> tuple[int, int]:
    dist = cell * 3.2
    offsets = {"N": (0, -dist), "E": (dist, 0), "S": (0, dist), "W": (-dist, 0)}
    dx, dy = offsets.get(direction.upper(), (0, -dist))
    return cx + int(dx), cy + int(dy)


def _island_slot_pos(ix: int, iy: int, cell: int, slot: int) -> tuple[int, int]:
    slots = [(-cell * 0.35, -cell * 0.2), (cell * 0.3, 0), (0, cell * 0.25), (-cell * 0.2, cell * 0.2)]
    dx, dy = slots[slot % len(slots)]
    return int(ix + dx), int(iy + dy)


UNITY = {
    "chrome": (30, 30, 30),
    "panel": (56, 56, 56),
    "panel_edge": (26, 26, 26),
    "tab": (68, 68, 68),
    "scene_bg_top": (72, 88, 110),
    "scene_bg_bot": (38, 48, 58),
    "hier_text": (200, 200, 200),
    "hier_sel": (44, 93, 135),
    "terrain_top": (96, 145, 78),
    "terrain_side": (62, 98, 52),
    "terrain_plateau": (118, 168, 95),
    "platform": (148, 142, 132),
    "trail": (184, 148, 88),
    "tree": (34, 110, 52),
    "rock": (120, 118, 112),
    "grass": (72, 150, 62),
    "spawn": (90, 220, 130),
    "water": (58, 118, 175),
    "gizmo": (255, 196, 60),
}


def _parse_specs(layout: dict[str, Any]) -> dict[str, float]:
    note = str(layout.get("gridNote") or "")
    specs = layout.get("technicalSpecs") if isinstance(layout.get("technicalSpecs"), dict) else {}
    plateau = float(specs.get("plateauHeightM") or 35)
    platform = float(specs.get("platformHeightM") or 12)
    hop = float(specs.get("platformHopM") or 4)
    if "+35" in note or "35u" in note:
        plateau = 35.0
    if "+12" in note or "12u" in note:
        platform = 12.0
    return {"plateau": plateau, "platform": platform, "hop": hop}


def _corner_maze_cells(layout: dict[str, Any]) -> set[tuple[int, int]]:
    cells: set[tuple[int, int]] = set()
    for m in layout.get("markers") or []:
        label = str(m.get("label", "")).lower()
        if "maze" in label and ("plateau" in label or "corner" in label):
            row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
            cells.add((row, col))
    if cells:
        return cells
    note = str((layout.get("playDisk") or {}).get("labyrinthNote") or "").lower()
    if "corner" in note or "nw" in note:
        return {(0, 0), (0, 2), (2, 0), (2, 2)}
    if (layout.get("playDisk") or {}).get("labyrinth"):
        return {(1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2)}
    return set()


def _iso(gx: float, gy: float, gz: float, ox: float, oy: float, scale: float) -> tuple[int, int]:
    """Isometric-ish scene view projection (Unity Scene camera ~30°)."""
    sx = ox + (gx - gy) * scale * 0.92
    sy = oy + (gx + gy) * scale * 0.46 - gz * scale * 0.55
    return int(sx), int(sy)


def _draw_iso_box(
    draw: ImageDraw.ImageDraw,
    gx: float,
    gy: float,
    gz: float,
    w: float,
    d: float,
    h: float,
    ox: float,
    oy: float,
    scale: float,
    top_color: tuple[int, int, int],
    side_color: tuple[int, int, int],
) -> None:
    hw, hd = w * 0.5, d * 0.5
    top = [
        _iso(gx - hw, gy - hd, gz + h, ox, oy, scale),
        _iso(gx + hw, gy - hd, gz + h, ox, oy, scale),
        _iso(gx + hw, gy + hd, gz + h, ox, oy, scale),
        _iso(gx - hw, gy + hd, gz + h, ox, oy, scale),
    ]
    right = [
        top[1],
        top[2],
        _iso(gx + hw, gy + hd, gz, ox, oy, scale),
        _iso(gx + hw, gy - hd, gz, ox, oy, scale),
    ]
    left = [
        top[0],
        top[3],
        _iso(gx - hw, gy + hd, gz, ox, oy, scale),
        _iso(gx - hw, gy - hd, gz, ox, oy, scale),
    ]
    draw.polygon(right, fill=side_color)
    draw.polygon(left, fill=tuple(max(0, c - 18) for c in side_color))
    draw.polygon(top, fill=top_color, outline=(30, 40, 30))


def _draw_unity_chrome(
    draw: ImageDraw.ImageDraw,
    title: str,
    scene_rect: tuple[int, int, int, int],
    font,
    small,
    tiny,
) -> tuple[int, int, int, int]:
    draw.rectangle([0, 0, W, 36], fill=UNITY["chrome"])
    draw.text((12, 8), "Unity 6", fill=(210, 210, 210), font=small)
    draw.text((88, 8), "MainScene.unity — Environment Kit Hub build preview", fill=(170, 170, 170), font=small)
    draw.text((W - 220, 8), "Scene | Game | Asset Store", fill=(140, 140, 140), font=tiny)

    hier_w = 248
    draw.rectangle([0, 36, hier_w, H - 28], fill=UNITY["panel"], outline=UNITY["panel_edge"])
    draw.text((10, 44), "Hierarchy", fill=UNITY["hier_text"], font=small)

    insp_x = W - 268
    draw.rectangle([insp_x, 36, W, H - 28], fill=UNITY["panel"], outline=UNITY["panel_edge"])
    draw.text((insp_x + 10, 44), "Inspector", fill=UNITY["hier_text"], font=small)

    sx0, sy0, sx1, sy1 = scene_rect
    draw.rectangle([sx0, sy0, sx1, sy1], fill=UNITY["scene_bg_bot"])
    for y in range(sy0, sy1):
        t = (y - sy0) / max(1, sy1 - sy0)
        r = int(UNITY["scene_bg_top"][0] * (1 - t) + UNITY["scene_bg_bot"][0] * t)
        g = int(UNITY["scene_bg_top"][1] * (1 - t) + UNITY["scene_bg_bot"][1] * t)
        b = int(UNITY["scene_bg_top"][2] * (1 - t) + UNITY["scene_bg_bot"][2] * t)
        draw.line([(sx0, y), (sx1, y)], fill=(r, g, b))

    draw.rectangle([sx0, sy0, sx1, sy1], outline=(20, 20, 20), width=2)
    draw.text((sx0 + 10, sy0 + 6), "Scene", fill=(220, 220, 220), font=tiny)
    draw.text((sx0 + 58, sy0 + 6), "Shaded | Wireframe off | 2D off", fill=(130, 130, 130), font=tiny)

    draw.rectangle([0, H - 28, W, H], fill=UNITY["chrome"])
    draw.text((12, H - 22), "Console  |  [CaveBuild] Surface props complete — planner layout preview", fill=(120, 200, 140), font=tiny)

    return sx0, sy0, sx1, sy1


def _draw_hierarchy(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    cfg: dict[str, Any],
    checklist: list[dict[str, Any]] | None,
    font,
    tiny,
) -> None:
    lines = [
        ("▼ Environment", False),
        ("  ▼ Surface", False),
        ("    Terrain (3×3 play disk)", False),
        ("    PlannerLayout", True),
        ("      JumpPlatforms", False),
        ("      PlannerFallVolume", False),
        ("    ▼ Vegetation", False),
    ]
    prop_count = sum(1 for m in layout.get("markers") or [] if str(m.get("kind")) == "prop" and "plat" not in str(m.get("label", "")).lower())
    plat_count = sum(1 for m in layout.get("markers") or [] if "plat" in str(m.get("label", "")).lower())
    if prop_count:
        lines.append((f"      Props ×{prop_count}", False))
    if plat_count:
        lines.append((f"      Platforms ×{plat_count}", False))
    if cfg.get("surfaceTrails") or layout.get("trails"):
        lines.append(("    Trails", False))
    if cfg.get("use3DCaveSystem"):
        lines.append(("  LavaTubeCaveSystem", False))
    if cfg.get("floatingTiles"):
        lines.append(("  Wilderness tiles (N/E/S/W)", False))
    lines.append(("▼ PlayerSpawnPoint", False))

    y = 68
    for text, selected in lines:
        if selected:
            draw.rectangle([4, y - 2, 244, y + 14], fill=UNITY["hier_sel"])
        color = (240, 240, 240) if selected else (175, 175, 175)
        draw.text((12, y), text[:34], fill=color, font=tiny)
        y += 18
        if y > H - 120:
            break

    y = max(y + 8, H - 200)
    draw.text((10, y), "Build manifest", fill=UNITY["hier_text"], font=font)
    y += 20
    for m in (layout.get("markers") or [])[:8]:
        kind = str(m.get("kind", ""))[:4].upper()
        label = str(m.get("label", ""))[:22]
        draw.text((14, y), f"{kind} {label}", fill=(130, 140, 155), font=tiny)
        y += 15


def _draw_inspector(
    draw: ImageDraw.ImageDraw,
    brief: dict[str, Any],
    cfg: dict[str, Any],
    layout: dict[str, Any],
    checklist: list[dict[str, Any]] | None,
    font,
    tiny,
) -> None:
    x = W - 256
    y = 68
    title = (brief.get("title") or cfg.get("label") or "Build")[:40]
    draw.text((x, y), title, fill=(230, 230, 230), font=font)
    y += 24
    rows = [
        ("Tiles", str(cfg.get("tileCount", 9))),
        ("Props", "on" if cfg.get("playDiskProps") else "off"),
        ("Trails", "on" if cfg.get("surfaceTrails") or layout.get("trails") else "off"),
        ("Caves", "on" if cfg.get("use3DCaveSystem") else "off"),
        ("Floating", "on" if cfg.get("floatingTiles") else "off"),
        ("Seed", "random" if cfg.get("randomSeedEachBuild") else "fixed"),
    ]
    specs = _parse_specs(layout)
    rows.extend(
        [
            ("Plateau +u", str(int(specs["plateau"]))),
            ("Platform +u", str(int(specs["platform"]))),
            ("Hop m", str(int(specs["hop"]))),
        ]
    )
    for k, v in rows:
        draw.text((x, y), k, fill=(140, 145, 155), font=tiny)
        draw.text((x + 120, y), v, fill=(210, 210, 210), font=tiny)
        y += 16

    if checklist:
        y += 8
        draw.text((x, y), "Decisions", fill=(200, 200, 200), font=font)
        y += 18
        for item in checklist[:5]:
            mark = "✓" if item.get("done") else "○"
            color = (120, 210, 140) if item.get("done") else (120, 130, 150)
            draw.text((x, y), f"{mark} {str(item.get('label', ''))[:28]}", fill=color, font=tiny)
            y += 15


def _resolve_build_scope(cfg: dict[str, Any]) -> dict[str, Any]:
    """Map sessionConfig to concept render scope (matches Unity ResolveFullWorldPlaceOffsets tiers)."""
    tc = int(cfg.get("tileCount", 9))
    floating = bool(cfg.get("floatingTiles"))
    if tc <= 9:
        return {
            "mode": "play_disk",
            "tile_count": 9,
            "max_ring": 1,
            "topdown": True,
            "grid_side": 3,
            "label": "9-tile play disk",
        }
    if tc == 13 or (floating and tc not in (81, 289)):
        return {
            "mode": "floating",
            "tile_count": 13,
            "max_ring": 2,
            "topdown": False,
            "grid_side": 5,
            "label": "13-tile floating islands",
        }
    if tc > 81:
        return {
            "mode": "extended",
            "tile_count": 289,
            "max_ring": 8,
            "topdown": True,
            "grid_side": 17,
            "label": "289-tile extended",
        }
    if tc == 81:
        return {
            "mode": "fullworld",
            "tile_count": 81,
            "max_ring": 4,
            "topdown": True,
            "grid_side": 9,
            "label": "81-tile FullWorld",
        }
    return {
        "mode": "play_disk",
        "tile_count": 9,
        "max_ring": 1,
        "topdown": True,
        "grid_side": 3,
        "label": "9-tile play disk",
    }


TOPDOWN_ZONES = {
    "play": (72, 140, 88),
    "play_open": (100, 175, 110),
    "lab": (34, 110, 72),
    "foot": (88, 105, 72),
    "peak": (110, 95, 82),
    "horizon": (55, 75, 95),
    "mixed": (70, 90, 75),
    "open": (45, 65, 55),
    "water": (66, 165, 245),
    "trail": (210, 165, 90),
    "mouth": (40, 48, 55),
    "empty": (28, 34, 42),
}


def _cheb_ring_color(ring: int) -> tuple[int, int, int]:
    if ring <= 1:
        return TOPDOWN_ZONES["play"]
    if ring == 2:
        return TOPDOWN_ZONES["foot"]
    if ring == 3:
        return TOPDOWN_ZONES["peak"]
    if ring == 4:
        return TOPDOWN_ZONES["horizon"]
    if ring <= 7:
        return TOPDOWN_ZONES["mixed"]
    return TOPDOWN_ZONES["open"]


def _topdown_scene_metrics(scene_rect: tuple[int, int, int, int], grid_side: int) -> dict[str, Any]:
    sx0, sy0, sx1, sy1 = scene_rect
    w, h = sx1 - sx0, sy1 - sy0
    cx = (sx0 + sx1) // 2
    cy = sy0 + int(h * 0.52)
    cell = max(5, min(w, h) // max(5, grid_side + 1))
    return {"cx": cx, "cy": cy, "cell": cell, "scene_rect": scene_rect}


def _draw_topdown_play_disk(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cell: int,
    *,
    labyrinth: bool = False,
) -> None:
    play_cell = max(4, cell // 3)
    gx0 = cx - play_cell
    gy0 = cy - play_cell
    for row in range(3):
        for col in range(3):
            x0 = gx0 + col * play_cell
            y0 = gy0 + row * play_cell
            is_lab = labyrinth and row >= 1
            fill = TOPDOWN_ZONES["lab"] if is_lab else TOPDOWN_ZONES["play_open"]
            draw.rectangle([x0, y0, x0 + play_cell - 1, y0 + play_cell - 1], fill=fill, outline=(20, 50, 30))
            if row == 0 and col == 1:
                draw.ellipse(
                    [x0 + play_cell // 2 - 3, y0 + 2, x0 + play_cell // 2 + 3, y0 + 8],
                    fill=TOPDOWN_ZONES["mouth"],
                )


def _draw_topdown_chebyshev_shell(
    draw: ImageDraw.ImageDraw,
    metrics: dict[str, Any],
    max_ring: int,
    *,
    labyrinth: bool = False,
) -> None:
    cx, cy, cell = metrics["cx"], metrics["cy"], metrics["cell"]
    draw.rectangle(list(metrics["scene_rect"]), fill=TOPDOWN_ZONES["empty"])
    for ring in range(max_ring, -1, -1):
        side = 2 * ring + 1
        half = ring * cell
        x0, y0 = cx - half, cy - half
        x1, y1 = cx + half, cy + half
        color = _cheb_ring_color(ring)
        if ring <= 1:
            continue
        draw.rectangle([x0, y0, x1, y1], outline=color, width=max(1, cell // 5))
    _draw_topdown_play_disk(draw, cx, cy, cell, labyrinth=labyrinth)


def _density_rgb(level: str) -> tuple[int, int, int]:
    key = (level or "medium").lower()
    if key in ("none", "off", "0"):
        return (40, 48, 44)
    if key in ("low", "sparse"):
        return (90, 120, 70)
    if key in ("high", "dense"):
        return (200, 150, 60)
    return (140, 170, 80)


def _draw_topdown_ring_props(
    draw: ImageDraw.ImageDraw,
    metrics: dict[str, Any],
    layout: dict[str, Any],
    tiny,
    *,
    density_mode: bool,
) -> None:
    cx, cy, cell = metrics["cx"], metrics["cy"], metrics["cell"]
    for rp in layout.get("ringProps") or []:
        try:
            ring = int(rp.get("ring", 0))
        except (TypeError, ValueError):
            continue
        if ring < 2:
            continue
        half = ring * cell
        x0, y0 = cx - half, cy - half
        x1, y1 = cx + half, cy + half
        if density_mode:
            fill = _density_rgb(str(rp.get("density", "medium")))
            draw.rectangle([x0 + 2, y0 + 2, x1 - 2, y1 - 2], outline=fill, width=max(2, cell // 4))
            label = str(rp.get("label", f"ring {ring}"))[:20]
            draw.text((x0 + 4, y0 + 4), label, fill=(220, 210, 180), font=tiny)
        elif rp.get("trails"):
            draw.ellipse([x0, y0, x1, y1], outline=TOPDOWN_ZONES["trail"], width=2)


def _draw_topdown_markers(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    metrics: dict[str, Any],
    tiny,
) -> None:
    cx, cy, cell = metrics["cx"], metrics["cy"], metrics["cell"]
    play_cell = max(4, cell // 3)
    gx0, gy0 = cx - play_cell, cy - play_cell
    island_offsets = {"N": (0, -2), "E": (2, 0), "S": (0, 2), "W": (-2, 0)}

    for m in layout.get("markers") or []:
        kind = str(m.get("kind", "prop"))
        label = str(m.get("label", ""))[:14]
        zone = str(m.get("zone", "play"))
        if zone == "island":
            d = str(m.get("dir", "N")).upper()[:1]
            dx, dy = island_offsets.get(d, (0, -2))
            px = cx + dx * cell
            py = cy + dy * cell
        else:
            row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
            px = gx0 + col * play_cell + play_cell // 2
            py = gy0 + row * play_cell + play_cell // 2
        color = COLORS.get(kind, COLORS["prop"])
        if kind == "spawn":
            draw.ellipse([px - 8, py - 8, px + 8, py + 8], fill=UNITY["spawn"], outline=(20, 80, 40), width=2)
        elif kind == "enemy":
            draw.polygon([(px, py - 8), (px + 8, py + 6), (px - 8, py + 6)], fill=color)
        else:
            draw.ellipse([px - 6, py - 6, px + 6, py + 6], fill=color)
        if label:
            draw.text((px, py + 10), label, fill=(210, 210, 200), anchor="mm", font=tiny)


def _draw_topdown_trails(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    metrics: dict[str, Any],
    *,
    color: tuple[int, int, int],
    width: int = 3,
) -> None:
    cx, cy, cell = metrics["cx"], metrics["cy"], metrics["cell"]
    for tr in layout.get("trails") or []:
        dest = str(tr.get("to", "")).lower()
        if dest.startswith("island-"):
            d = dest.split("-")[-1].upper()
            offsets = {"N": (0, -2), "E": (2, 0), "S": (0, 2), "W": (-2, 0)}
            dx, dy = offsets.get(d, (0, -2))
            draw.line([(cx, cy), (cx + dx * cell, cy + dy * cell)], fill=color, width=width)
        elif "foothill" in dest or "peak" in dest or "ring" in dest:
            draw.line([(cx, cy), (cx, cy + cell * 2)], fill=color, width=width)
        elif "south" in dest:
            draw.line([(cx, cy), (cx, cy + cell * 2)], fill=color, width=width)


def _draw_topdown_layout_world(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    cfg: dict[str, Any],
    scene_rect: tuple[int, int, int, int],
    tiny,
    scope: dict[str, Any],
) -> None:
    metrics = _topdown_scene_metrics(scene_rect, scope["grid_side"])
    labyrinth = bool((layout.get("playDisk") or {}).get("labyrinth"))
    _draw_topdown_chebyshev_shell(draw, metrics, scope["max_ring"], labyrinth=labyrinth)
    _draw_topdown_ring_props(draw, metrics, layout, tiny, density_mode=False)
    _draw_topdown_trails(draw, layout, metrics, color=TOPDOWN_ZONES["trail"], width=3)
    _draw_topdown_markers(draw, layout, metrics, tiny)
    sx0, sy0, _, _ = scene_rect
    note = str(layout.get("gridNote") or scope["label"])
    draw.text((sx0 + 10, sy0 + 8), note[:72], fill=(200, 210, 220), font=tiny)


def _draw_topdown_density_world(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    cfg: dict[str, Any],
    scene_rect: tuple[int, int, int, int],
    tiny,
    scope: dict[str, Any],
) -> None:
    metrics = _topdown_scene_metrics(scene_rect, scope["grid_side"])
    draw.rectangle(list(scene_rect), fill=(18, 22, 28))
    for ring in range(scope["max_ring"], 0, -1):
        half = ring * metrics["cell"]
        x0 = metrics["cx"] - half
        y0 = metrics["cy"] - half
        x1 = metrics["cx"] + half
        y1 = metrics["cy"] + half
        draw.rectangle([x0, y0, x1, y1], outline=(38, 48, 42), width=1)
    _draw_topdown_play_disk(draw, metrics["cx"], metrics["cy"], metrics["cell"])
    _draw_topdown_ring_props(draw, metrics, layout, tiny, density_mode=True)
    _draw_topdown_trails(draw, layout, metrics, color=DENSITY_TRAIL_RGB, width=5)
    prop_counts = _prop_density_by_cell(layout)
    play_cell = max(4, metrics["cell"] // 3)
    gx0, gy0 = metrics["cx"] - play_cell, metrics["cy"] - play_cell
    max_count = max(prop_counts.values(), default=1)
    for row in range(3):
        for col in range(3):
            n = prop_counts.get(("play", row, col), 0)
            if n <= 0:
                continue
            px = gx0 + col * play_cell + play_cell // 2
            py = gy0 + row * play_cell + play_cell // 2
            t = n / max_count
            heat = (int(50 + 120 * t), int(90 + 80 * t), int(40 + 30 * t))
            r = int(10 + 14 * t)
            draw.ellipse([px - r, py - r, px + r, py + r], fill=heat)
            draw.text((px, py), str(n), fill=(240, 250, 235), anchor="mm", font=tiny)
    sx0, sy0, _, _ = scene_rect
    draw.text(
        (sx0 + 10, sy0 + 8),
        f"Prop density — {scope['label']}",
        fill=(210, 200, 170),
        font=tiny,
    )


def _scene_layout_params(
    cfg: dict[str, Any],
    scene_rect: tuple[int, int, int, int],
    *,
    zoom: float = 1.0,
) -> dict[str, Any]:
    sx0, sy0, sx1, sy1 = scene_rect
    floating = bool(cfg.get("floatingTiles"))
    return {
        "scene_rect": scene_rect,
        "ox": (sx0 + sx1) // 2,
        "oy": sy0 + int((sy1 - sy0) * 0.58),
        "scale": 38.0 * zoom,
        "tile_step": 1.55 if floating else 1.15,
        "tile_w": 1.05,
        "floating": floating,
    }


def _draw_legend_key(
    draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    font,
    tiny,
    *,
    density_mode: bool = False,
) -> None:
    pad = 8
    if density_mode:
        rows = [
            ("Trail corridor", DENSITY_TRAIL_RGB, "line"),
            ("Prop scatter (low)", DENSITY_PROP_RGB, "dot"),
            ("Prop scatter (high)", DENSITY_PROP_HOT_RGB, "dot"),
            ("Tile prop count", (72, 150, 62), "text"),
        ]
        title = "Density legend"
        note = "Gold = walk paths · Amber = prop targets · Brighter = denser"
    else:
        rows = [
            ("Play tile", UNITY["terrain_top"], "tile"),
            ("Maze plateau", UNITY["terrain_plateau"], "tile"),
            ("Trail", UNITY["trail"], "line"),
            ("Jump platform", UNITY["platform"], "tile"),
            ("Prop / tree / rock", COLORS["prop"], "square"),
            ("Player spawn", UNITY["spawn"], "spawn"),
            ("NPC", COLORS["npc"], "dot"),
            ("Enemy", COLORS["enemy"], "tri"),
            ("Collectible", COLORS["collect"], "star"),
            ("Cave mouth", UNITY["water"], "oval"),
        ]
        title = "Layout legend"
        note = "Scene colors match Unity terrain sculpt sampling"

    line_h = 16
    box_w = 248
    box_h = pad * 2 + 18 + line_h * len(rows) + 20
    draw.rectangle([x, y, x + box_w, y + box_h], fill=(18, 22, 30), outline=(70, 80, 100))
    draw.text((x + pad, y + pad), title, fill=(220, 228, 240), font=font)
    cy = y + pad + 18
    for label, rgb, kind in rows:
        ix = x + pad
        if kind == "line":
            draw.line([(ix, cy + 6), (ix + 18, cy + 6)], fill=rgb, width=4)
        elif kind == "tile":
            draw.rectangle([ix, cy, ix + 14, cy + 12], fill=rgb, outline=(40, 50, 40))
        elif kind == "square":
            draw.rectangle([ix + 2, cy + 2, ix + 12, cy + 12], fill=rgb)
        elif kind == "spawn":
            draw.ellipse([ix, cy, ix + 12, cy + 12], fill=rgb, outline=(20, 80, 40))
        elif kind == "dot":
            draw.ellipse([ix + 2, cy + 2, ix + 12, cy + 12], fill=rgb)
        elif kind == "tri":
            draw.polygon([(ix + 7, cy), (ix + 14, cy + 12), (ix, cy + 12)], fill=rgb)
        elif kind == "star":
            draw.ellipse([ix + 2, cy + 2, ix + 12, cy + 12], fill=rgb)
        elif kind == "oval":
            draw.ellipse([ix, cy + 2, ix + 14, cy + 10], fill=rgb)
        elif kind == "text":
            draw.text((ix, cy), "3", fill=rgb, font=tiny)
        draw.text((ix + 24, cy), label[:26], fill=(175, 185, 200), font=tiny)
        cy += line_h
    draw.text((x + pad, y + box_h - 16), note[:42], fill=(120, 130, 150), font=tiny)


def _draw_trail_lines(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    cfg: dict[str, Any],
    params: dict[str, Any],
    *,
    trail_color: tuple[int, int, int],
    width: int,
) -> None:
    ox = params["ox"]
    oy = params["oy"]
    scale = params["scale"]
    tile_step = params["tile_step"]
    tiny = _load_font(11)

    if cfg.get("surfaceTrails") or layout.get("trails"):
        ring_pts = []
        for row, col in ((0, 1), (1, 2), (2, 1), (1, 0), (0, 1)):
            gx = (col - 1) * tile_step
            gy = (1 - row) * tile_step
            ring_pts.append(_iso(gx, gy, 0.15, ox, oy, scale))
        if len(ring_pts) > 1:
            draw.line(ring_pts, fill=trail_color, width=width)
    for tr in layout.get("trails") or []:
        dest = str(tr.get("to", ""))
        if dest.startswith("island-"):
            d = dest.split("-")[-1].upper()
            offsets = {"N": (0, 2.2), "E": (2.2, 0), "S": (0, -2.2), "W": (-2.2, 0)}
            dx, dy = offsets.get(d, (0, 2.2))
            a = _iso(0, 0, 0.2, ox, oy, scale)
            b = _iso(dx * tile_step, dy * tile_step, 0.1, ox, oy, scale)
            draw.line([a, b], fill=trail_color, width=width + 1)
            mid = ((a[0] + b[0]) // 2, (a[1] + b[1]) // 2)
            draw.text(mid, str(tr.get("label", ""))[:18], fill=(220, 200, 160), anchor="mm", font=tiny)


def _prop_density_by_cell(layout: dict[str, Any]) -> dict[tuple[str, int, int], int]:
    counts: dict[tuple[str, int, int], int] = {}
    for m in layout.get("markers") or []:
        if str(m.get("kind")) not in ("prop", "collectible"):
            continue
        zone = str(m.get("zone", "play"))
        if zone == "play":
            row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
            key = ("play", row, col)
        elif zone == "island":
            slot = _coerce_island_slot(m.get("slot"))
            dir_n = str(m.get("dir", "N")).upper()[:1]
            key = ("island", ord(dir_n), slot)
        else:
            continue
        counts[key] = counts.get(key, 0) + 1
    return counts


def _draw_scene_world(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    cfg: dict[str, Any],
    scene_rect: tuple[int, int, int, int],
    tiny,
    *,
    zoom: float = 1.0,
) -> None:
    params = _scene_layout_params(cfg, scene_rect, zoom=zoom)
    sx0, sy0, sx1, sy1 = params["scene_rect"]
    ox = params["ox"]
    oy = params["oy"]
    scale = params["scale"]
    tile_step = params["tile_step"]
    tile_w = params["tile_w"]
    specs = _parse_specs(layout)
    maze_cells = _corner_maze_cells(layout)
    floating = params["floating"]
    scope = _resolve_build_scope(cfg)
    if scope["topdown"]:
        _draw_topdown_layout_world(draw, layout, cfg, scene_rect, tiny, scope)
        return

    # Ground grid lines (Unity scene grid)
    for i in range(-6, 7):
        a = _iso(i * 1.2, -7, 0, ox, oy, scale * 0.35)
        b = _iso(i * 1.2, 7, 0, ox, oy, scale * 0.35)
        draw.line([a, b], fill=(50, 58, 68), width=1)
        a = _iso(-7, i * 1.2, 0, ox, oy, scale * 0.35)
        b = _iso(7, i * 1.2, 0, ox, oy, scale * 0.35)
        draw.line([a, b], fill=(50, 58, 68), width=1)

    tile_step = 1.55 if floating else 1.15
    tile_w = 1.05

    # Play disk 3×3 terrains
    for row in range(3):
        for col in range(3):
            gx = (col - 1) * tile_step
            gy = (1 - row) * tile_step
            is_maze = (row, col) in maze_cells
            h = (specs["plateau"] / 18.0) if is_maze else 0.08
            top = UNITY["terrain_plateau"] if is_maze else UNITY["terrain_top"]
            side = UNITY["terrain_side"]
            _draw_iso_box(draw, gx, gy, 0, tile_w, tile_w, h, ox, oy, scale, top, side)
            if is_maze:
                # Simple maze walls on plateau
                cx, cy = _iso(gx, gy, h + 0.02, ox, oy, scale)
                draw.rectangle([cx - 14, cy - 14, cx + 14, cy + 14], outline=(28, 70, 42), width=2)
                draw.line([cx - 10, cy, cx + 10, cy], fill=(28, 70, 42), width=2)
                draw.line([cx, cy - 10, cx, cy + 10], fill=(28, 70, 42), width=2)

    # Floating wilderness cardinal tiles
    if floating:
        for d, (dx, dy) in {"N": (0, 2.2), "E": (2.2, 0), "S": (0, -2.2), "W": (-2.2, 0)}.items():
            _draw_iso_box(draw, dx * tile_step, dy * tile_step, -0.15, tile_w * 0.9, tile_w * 0.9, 0.12, ox, oy, scale, UNITY["terrain_top"], UNITY["terrain_side"])
            tx, ty = _iso(dx * tile_step, dy * tile_step, 0.3, ox, oy, scale)
            draw.text((tx, ty - 28), f"{d} wild", fill=(180, 200, 220), anchor="mm", font=tiny)

    _draw_trail_lines(draw, layout, cfg, params, trail_color=UNITY["trail"], width=4)

    # Jump platforms from plat-* markers
    for m in layout.get("markers") or []:
        label = str(m.get("label", "")).lower()
        if "plat" not in label:
            continue
        row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
        gx = (col - 1) * tile_step
        gy = (1 - row) * tile_step
        slot = _coerce_marker_slot(m.get("slot"))
        if "-gap-" in label:
            leg = "n" if "-n-gap" in label else "e" if "-e-gap" in label else "s" if "-s-gap" in label else "w"
            spread = 0.35
            t = (slot % 7) / 6.0 - 0.5
            if leg == "n":
                gx += t * spread
                gy += 0.55
            elif leg == "s":
                gx -= t * spread
                gy -= 0.55
            elif leg == "e":
                gx += 0.55
                gy += t * spread
            else:
                gx -= 0.55
                gy -= t * spread
        pz = specs["platform"] / 20.0
        _draw_iso_box(draw, gx, gy, pz, 0.22, 0.22, 0.06, ox, oy, scale, UNITY["platform"], (110, 105, 98))

    # Props, NPCs, enemies, collectibles
    for m in layout.get("markers") or []:
        kind = str(m.get("kind", "prop"))
        label = str(m.get("label", "")).lower()
        if kind == "spawn" or "plat" in label:
            continue
        row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
        if (row, col) in maze_cells and kind == "prop":
            continue
        slot = _coerce_marker_slot(m.get("slot"))
        gx = (col - 1) * tile_step + (slot % 3 - 1) * 0.12
        gy = (1 - row) * tile_step + (slot // 3 - 1) * 0.1
        px, py = _iso(gx, gy, 0.2, ox, oy, scale)
        if "tree" in label:
            draw.ellipse([px - 5, py - 16, px + 5, py - 4], fill=UNITY["tree"])
            draw.rectangle([px - 2, py - 4, px + 2, py + 2], fill=(90, 60, 30))
        elif "rock" in label:
            draw.polygon([(px, py - 8), (px + 8, py + 2), (px - 6, py + 4)], fill=UNITY["rock"])
        elif "grass" in label:
            for i in range(-2, 3):
                draw.line([(px + i * 2, py), (px + i * 2, py - 8)], fill=UNITY["grass"], width=2)
        elif kind == "npc":
            draw.ellipse([px - 6, py - 14, px + 6, py - 2], fill=COLORS["npc"])
            draw.rectangle([px - 4, py - 2, px + 4, py + 8], fill=COLORS["npc"])
        elif kind == "enemy":
            draw.polygon([(px, py - 10), (px + 9, py + 6), (px - 9, py + 6)], fill=COLORS["enemy"])
        elif kind == "collectible":
            draw.ellipse([px - 6, py - 6, px + 6, py + 6], fill=COLORS["collect"])
        else:
            draw.rectangle([px - 5, py - 5, px + 5, py + 5], fill=COLORS["prop"])

    # Player spawn gizmo
    for m in layout.get("markers") or []:
        if str(m.get("kind")) != "spawn":
            continue
        row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
        gx = (col - 1) * tile_step
        gy = (1 - row) * tile_step
        sx, sy = _iso(gx, gy, 0.25, ox, oy, scale)
        draw.ellipse([sx - 10, sy - 22, sx + 10, sy - 2], fill=UNITY["spawn"], outline=(20, 80, 40), width=2)
        draw.line([(sx - 12, sy), (sx + 12, sy)], fill=UNITY["gizmo"], width=2)
        draw.line([(sx, sy - 12), (sx, sy + 12)], fill=UNITY["gizmo"], width=2)
        draw.text((sx, sy - 30), "PlayerSpawn", fill=UNITY["spawn"], anchor="mm", font=tiny)

    # Cave mouth
    if layout.get("caveMouths") or cfg.get("use3DCaveSystem"):
        mx, my = _iso(0, tile_step, 0.15, ox, oy, scale)
        draw.ellipse([mx - 16, my - 20, mx + 16, my - 4], fill=UNITY["water"])
        draw.text((mx, my - 28), "cave mouth", fill=(200, 220, 240), anchor="mm", font=tiny)

    if cfg.get("surfaceWater") or layout.get("surfaceWater"):
        wx, wy = _iso(0, -2.4, -0.05, ox, oy, scale)
        draw.ellipse([wx - 80, wy - 18, wx + 80, wy + 18], fill=(45, 95, 140))

    # Scene gizmo (Unity axis widget)
    gx0, gy0 = sx1 - 54, sy0 + 12
    draw.line([(gx0, gy0), (gx0, gy0 - 22)], fill=(180, 60, 60), width=2)
    draw.line([(gx0, gy0), (gx0 + 18, gy0)], fill=(60, 160, 80), width=2)
    draw.line([(gx0, gy0), (gx0 - 12, gy0 + 12)], fill=(70, 110, 200), width=2)


def _draw_scene_density(
    draw: ImageDraw.ImageDraw,
    layout: dict[str, Any],
    cfg: dict[str, Any],
    scene_rect: tuple[int, int, int, int],
    tiny,
    *,
    zoom: float = 1.0,
) -> None:
    """Trail + prop density map — same projection as layout (Unity samples these colors)."""
    scope = _resolve_build_scope(cfg)
    if scope["topdown"]:
        _draw_topdown_density_world(draw, layout, cfg, scene_rect, tiny, scope)
        return

    params = _scene_layout_params(cfg, scene_rect, zoom=zoom)
    sx0, sy0, sx1, sy1 = params["scene_rect"]
    ox = params["ox"]
    oy = params["oy"]
    scale = params["scale"]
    tile_step = params["tile_step"]
    tile_w = params["tile_w"]
    floating = params["floating"]

    draw.rectangle([sx0, sy0, sx1, sy1], fill=(22, 26, 34))

    for row in range(3):
        for col in range(3):
            gx = (col - 1) * tile_step
            gy = (1 - row) * tile_step
            _draw_iso_box(
                draw,
                gx,
                gy,
                0,
                tile_w,
                tile_w,
                0.04,
                ox,
                oy,
                scale,
                (38, 48, 42),
                (28, 36, 32),
            )

    if floating:
        for d, (dx, dy) in {"N": (0, 2.2), "E": (2.2, 0), "S": (0, -2.2), "W": (-2.2, 0)}.items():
            _draw_iso_box(
                draw,
                dx * tile_step,
                dy * tile_step,
                -0.12,
                tile_w * 0.9,
                tile_w * 0.9,
                0.05,
                ox,
                oy,
                scale,
                (34, 44, 38),
                (24, 32, 28),
            )

    prop_counts = _prop_density_by_cell(layout)
    max_count = max(prop_counts.values(), default=1)

    for row in range(3):
        for col in range(3):
            n = prop_counts.get(("play", row, col), 0)
            if n <= 0:
                continue
            gx = (col - 1) * tile_step
            gy = (1 - row) * tile_step
            t = n / max_count
            heat = (
                int(50 + 120 * t),
                int(90 + 80 * t),
                int(40 + 30 * t),
            )
            cx, cy = _iso(gx, gy, 0.12, ox, oy, scale)
            r = int(12 + 16 * t)
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=heat, outline=(30, 60, 30))
            draw.text((cx, cy), str(n), fill=(240, 250, 235), anchor="mm", font=tiny)

    _draw_trail_lines(draw, layout, cfg, params, trail_color=DENSITY_TRAIL_RGB, width=7)

    for m in layout.get("markers") or []:
        kind = str(m.get("kind", "prop"))
        if kind not in ("prop", "collectible"):
            continue
        label = str(m.get("label", "")).lower()
        if "plat" in label:
            continue
        zone = str(m.get("zone", "play"))
        if zone == "island":
            d = str(m.get("dir", "N")).upper()[:1]
            slot = _coerce_island_slot(m.get("slot"))
            offsets = {"N": (0, 2.2), "E": (2.2, 0), "S": (0, -2.2), "W": (-2.2, 0)}
            dx, dy = offsets.get(d, (0, 2.2))
            gx = dx * tile_step + ((slot % 3) - 1) * 0.15
            gy = dy * tile_step + ((slot // 3) - 1) * 0.1
            px, py = _iso(gx, gy, 0.22, ox, oy, scale)
        else:
            row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
            slot = _coerce_marker_slot(m.get("slot"))
            gx = (col - 1) * tile_step + (slot % 3 - 1) * 0.12
            gy = (1 - row) * tile_step + (slot // 3 - 1) * 0.1
            px, py = _iso(gx, gy, 0.22, ox, oy, scale)
        rgb = DENSITY_PROP_HOT_RGB if "tree" in label or "scatter" in label else DENSITY_PROP_RGB
        draw.ellipse([px - 7, py - 7, px + 7, py + 7], fill=rgb, outline=(120, 90, 40))
        if len(label) > 2:
            draw.text((px, py + 11), label[:12], fill=(200, 190, 160), anchor="mm", font=tiny)

    draw.text(
        (sx0 + 12, sy0 + 8),
        "Trail & prop density — generation overlay",
        fill=(210, 200, 170),
        font=tiny,
    )


def _draw_revision_banner(
    draw: ImageDraw.ImageDraw,
    scene_rect: tuple[int, int, int, int],
    revision_label: str | None,
    font: ImageFont.FreeTypeFont | ImageFont.ImageFont,
) -> None:
    if not revision_label:
        return
    sx0, sy0, sx1, _ = scene_rect
    text = revision_label.strip()[:140]
    if not text:
        return
    draw.rectangle([sx0 + 8, sy0 + 8, min(sx1 - 8, sx0 + 8 + len(text) * 7), sy0 + 30], fill=(6, 18, 32))
    draw.rectangle([sx0 + 8, sy0 + 8, min(sx1 - 8, sx0 + 8 + len(text) * 7), sy0 + 30], outline=(0, 196, 232), width=1)
    draw.text((sx0 + 14, sy0 + 12), text, fill=(110, 235, 255), font=font)


def render_layout_detail_crop(
    out_path,
    brief: dict[str, Any],
    cfg: dict[str, Any],
    layout: dict[str, Any] | None = None,
    checklist: list[dict[str, Any]] | None = None,
    revision_label: str | None = None,
) -> None:
    """Readable play-disk zoom — scene only, no Unity chrome."""
    layout = layout or derive_layout_plan(brief, cfg)
    brief = brief or {}
    cfg = cfg or {}

    img = Image.new("RGB", (DETAIL_W, DETAIL_H), UNITY["scene_bg_top"])
    draw = ImageDraw.Draw(img)
    body_font = _load_font(14)
    small_font = _load_font(12)
    tiny_font = _load_font(11)

    scene_rect = (16, 48, DETAIL_W - 16, DETAIL_H - 56)
    params = _scene_layout_params(cfg, scene_rect, zoom=DETAIL_ZOOM)
    sx0, sy0, sx1, sy1 = scene_rect
    for y in range(sy0, sy1):
        t = (y - sy0) / max(1, sy1 - sy0)
        r = int(UNITY["scene_bg_top"][0] * (1 - t) + UNITY["scene_bg_bot"][0] * t)
        g = int(UNITY["scene_bg_top"][1] * (1 - t) + UNITY["scene_bg_bot"][1] * t)
        b = int(UNITY["scene_bg_top"][2] * (1 - t) + UNITY["scene_bg_bot"][2] * t)
        draw.line([(sx0, y), (sx1, y)], fill=(r, g, b))
    draw.rectangle([sx0, sy0, sx1, sy1], outline=(40, 48, 62), width=2)

    _draw_scene_world(draw, layout, cfg, scene_rect, tiny_font, zoom=DETAIL_ZOOM)
    _draw_revision_banner(draw, scene_rect, revision_label, tiny_font)
    _draw_legend_key(draw, sx0 + 12, sy1 - 210, body_font, tiny_font, density_mode=False)

    title = (brief.get("title") or cfg.get("label") or "Layout")[:56]
    draw.text((20, 14), f"{title} — play disk (zoomed)", fill=(220, 228, 240), font=body_font)
    draw.text(
        (20, DETAIL_H - 36),
        "Layout + legend — readable play-disk view for approval",
        fill=(120, 200, 140),
        font=small_font,
    )
    img.save(str(out_path), format="PNG", optimize=True)


def render_density_detail_crop(
    out_path,
    brief: dict[str, Any],
    cfg: dict[str, Any],
    layout: dict[str, Any] | None = None,
    revision_label: str | None = None,
) -> None:
    layout = layout or derive_layout_plan(brief, cfg)
    brief = brief or {}
    cfg = cfg or {}

    img = Image.new("RGB", (DETAIL_W, DETAIL_H), UNITY["scene_bg_top"])
    draw = ImageDraw.Draw(img)
    body_font = _load_font(14)
    small_font = _load_font(12)
    tiny_font = _load_font(11)

    scene_rect = (16, 48, DETAIL_W - 16, DETAIL_H - 56)
    sx0, sy0, sx1, sy1 = scene_rect
    for y in range(sy0, sy1):
        t = (y - sy0) / max(1, sy1 - sy0)
        r = int(UNITY["scene_bg_top"][0] * (1 - t) + UNITY["scene_bg_bot"][0] * t)
        g = int(UNITY["scene_bg_top"][1] * (1 - t) + UNITY["scene_bg_bot"][1] * t)
        b = int(UNITY["scene_bg_top"][2] * (1 - t) + UNITY["scene_bg_bot"][2] * t)
        draw.line([(sx0, y), (sx1, y)], fill=(r, g, b))
    draw.rectangle([sx0, sy0, sx1, sy1], outline=(40, 48, 62), width=2)

    _draw_scene_density(draw, layout, cfg, scene_rect, tiny_font, zoom=DETAIL_ZOOM)
    _draw_revision_banner(draw, scene_rect, revision_label, tiny_font)
    _draw_legend_key(draw, sx0 + 12, sy1 - 128, body_font, tiny_font, density_mode=True)

    title = (brief.get("title") or cfg.get("label") or "Density")[:56]
    draw.text((20, 14), f"{title} — trail & prop density (zoomed)", fill=(220, 228, 240), font=body_font)
    draw.text(
        (20, DETAIL_H - 36),
        "Gold trails · amber prop scatter · green heat = count per tile",
        fill=(200, 180, 120),
        font=small_font,
    )
    img.save(str(out_path), format="PNG", optimize=True)


def render_planner_concept(
    out_path,
    brief: dict[str, Any],
    cfg: dict[str, Any],
    checklist: list[dict[str, Any]] | None = None,
) -> str | None:
    """Write layout concept.png; returns relative-style density filename if written."""
    layout = derive_layout_plan(brief, cfg)
    brief = brief or {}
    cfg = cfg or {}

    img = Image.new("RGB", (W, H), UNITY["chrome"])
    draw = ImageDraw.Draw(img)
    title_font = _load_font(22, bold=True)
    body_font = _load_font(14)
    small_font = _load_font(12)
    tiny_font = _load_font(11)

    title = (brief.get("title") or cfg.get("label") or "Build concept")[:72]
    scene_rect = (252, 36, W - 268, H - 28)
    _draw_unity_chrome(draw, title, scene_rect, body_font, small_font, tiny_font)
    _draw_scene_world(draw, layout, cfg, scene_rect, tiny_font)
    _draw_hierarchy(draw, layout, cfg, checklist, body_font, tiny_font)
    _draw_inspector(draw, brief, cfg, layout, checklist, body_font, tiny_font)

    sx0, sy0, sx1, sy1 = scene_rect
    _draw_legend_key(draw, sx0 + 10, sy1 - 198, body_font, tiny_font, density_mode=False)

    note = layout.get("gridNote", "81-tile shell")
    draw.text(
        (sx0 + 12, sy0 + 24),
        f"Post-build preview — {note}",
        fill=(190, 210, 225),
        font=small_font,
    )
    draw.text(
        (sx0 + 12, H - 52),
        "Layout + legend — tiles, trails, props, platforms · paired with density map for generation",
        fill=(120, 200, 140),
        font=small_font,
    )

    out_path = str(out_path)
    img.save(out_path, format="PNG", optimize=True)

    density_path = render_planner_concept_density(
        out_path,
        brief,
        cfg,
        layout,
        checklist,
    )

    detail_dir = out_path.parent if hasattr(out_path, "parent") else Path(str(out_path)).parent
    render_layout_detail_crop(detail_dir / CONCEPT_LAYOUT_DETAIL_NAME, brief, cfg, layout, checklist)
    render_density_detail_crop(detail_dir / CONCEPT_DENSITY_DETAIL_NAME, brief, cfg, layout)

    return CONCEPT_DENSITY_NAME if density_path else None


def render_planner_concept_density(
    layout_path,
    brief: dict[str, Any],
    cfg: dict[str, Any],
    layout: dict[str, Any] | None = None,
    checklist: list[dict[str, Any]] | None = None,
    revision_label: str | None = None,
) -> str | None:
    from pathlib import Path

    layout = layout or derive_layout_plan(brief, cfg)
    brief = brief or {}
    cfg = cfg or {}

    density_path = Path(layout_path).parent / CONCEPT_DENSITY_NAME
    img = Image.new("RGB", (W, H), UNITY["chrome"])
    draw = ImageDraw.Draw(img)
    body_font = _load_font(14)
    small_font = _load_font(12)
    tiny_font = _load_font(11)

    title = (brief.get("title") or cfg.get("label") or "Density map")[:72]
    scene_rect = (252, 36, W - 268, H - 28)
    _draw_unity_chrome(draw, f"{title} — density", scene_rect, body_font, small_font, tiny_font)
    _draw_scene_density(draw, layout, cfg, scene_rect, tiny_font)
    _draw_revision_banner(draw, scene_rect, revision_label, tiny_font)
    _draw_hierarchy(draw, layout, cfg, checklist, body_font, tiny_font)

    prop_n = sum(1 for m in layout.get("markers") or [] if str(m.get("kind")) == "prop")
    trail_n = len(layout.get("trails") or [])
    sx0, sy0, sx1, sy1 = scene_rect
    draw.text((W - 256, 68), "Density stats", fill=(230, 230, 230), font=body_font)
    y = 92
    for k, v in [
        ("Prop markers", str(prop_n)),
        ("Trail links", str(trail_n)),
        ("Target coverage", "65–85% tile" if cfg.get("playDiskProps") else "off"),
    ]:
        draw.text((W - 256, y), k, fill=(140, 145, 155), font=tiny_font)
        draw.text((W - 120, y), v, fill=(210, 210, 210), font=tiny_font)
        y += 16

    _draw_legend_key(draw, sx0 + 10, sy1 - 118, body_font, tiny_font, density_mode=True)
    draw.text(
        (sx0 + 12, H - 52),
        "Trail & prop density — Unity uses with layout concept for scatter + path authoring",
        fill=(200, 180, 120),
        font=small_font,
    )

    img.save(str(density_path), format="PNG", optimize=True)
    return str(density_path)


def render_planner_concept_layout_only(
    out_path,
    brief: dict[str, Any],
    cfg: dict[str, Any],
    checklist: list[dict[str, Any]] | None = None,
    revision_label: str | None = None,
) -> None:
    """Regenerate layout concept.png only (paired density unchanged)."""
    layout = derive_layout_plan(brief, cfg)
    brief = brief or {}
    cfg = cfg or {}

    img = Image.new("RGB", (W, H), UNITY["chrome"])
    draw = ImageDraw.Draw(img)
    title_font = _load_font(22, bold=True)
    body_font = _load_font(14)
    small_font = _load_font(12)
    tiny_font = _load_font(11)

    title = (brief.get("title") or cfg.get("label") or "Build concept")[:72]
    scene_rect = (252, 36, W - 268, H - 28)
    _draw_unity_chrome(draw, title, scene_rect, body_font, small_font, tiny_font)
    _draw_scene_world(draw, layout, cfg, scene_rect, tiny_font)
    _draw_revision_banner(draw, scene_rect, revision_label, tiny_font)
    _draw_hierarchy(draw, layout, cfg, checklist, body_font, tiny_font)
    _draw_inspector(draw, brief, cfg, layout, checklist, body_font, tiny_font)

    sx0, sy0, sx1, sy1 = scene_rect
    _draw_legend_key(draw, sx0 + 10, sy1 - 198, body_font, tiny_font, density_mode=False)

    note = layout.get("gridNote", "81-tile shell")
    draw.text(
        (sx0 + 12, sy0 + 24),
        f"Post-build preview — {note}",
        fill=(190, 210, 225),
        font=small_font,
    )
    draw.text(
        (sx0 + 12, H - 52),
        "Layout + legend — tiles, trails, props, platforms · paired with density map for generation",
        fill=(120, 200, 140),
        font=small_font,
    )

    img.save(str(out_path), format="PNG", optimize=True)
    detail_dir = Path(str(out_path)).parent
    render_layout_detail_crop(
        detail_dir / CONCEPT_LAYOUT_DETAIL_NAME,
        brief,
        cfg,
        layout,
        checklist,
        revision_label=revision_label,
    )
