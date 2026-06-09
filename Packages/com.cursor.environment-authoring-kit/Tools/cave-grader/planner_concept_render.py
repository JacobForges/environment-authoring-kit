#!/usr/bin/env python3
"""Rich top-down concept map for AI Build Planner — props, NPCs, trails, enemies, collectibles."""
from __future__ import annotations

from typing import Any

from PIL import Image, ImageDraw, ImageFont

W, H = 1536, 1024

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
}


def _coerce_play_row_col(row: Any, col: Any, default: tuple[int, int] = (1, 1)) -> tuple[int, int]:
    for val in (row, col):
        if isinstance(val, str):
            key = val.strip().lower()
            if key in _PLAY_TILE:
                return _PLAY_TILE[key]
    try:
        r = int(row) if row is not None and str(row).strip() != "" else default[0]
        c = int(col) if col is not None and str(col).strip() != "" else default[1]
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
    return max(0, min(3, named.get(s, default)))


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
        elif zone == "island":
            item["slot"] = _coerce_island_slot(item.get("slot"))
            item["dir"] = str(item.get("dir", "N")).upper()[:1] or "N"
        markers.append(item)
    out["markers"] = markers
    return out


def derive_layout_plan(brief: dict[str, Any], cfg: dict[str, Any]) -> dict[str, Any]:
    """Build layoutPlan from brief.layoutPlan or infer from config + summary."""
    explicit = brief.get("layoutPlan")
    if isinstance(explicit, dict) and explicit.get("markers"):
        return _sanitize_layout_plan(explicit)

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

    return {
        "gridNote": "289 extended" if int(cfg.get("tileCount", 81)) > 81 else "81-tile shell",
        "spawn": {"row": 0, "col": 1, "label": "Player spawn"},
        "playDisk": {
            "labyrinth": labyrinth,
            "labyrinthNote": "rows 1–2 maze (6 tiles) · row 0 north flat overlook" if labyrinth else "open flat 3×3",
        },
        "markers": markers,
        "trails": trails,
        "islands": islands,
        "caveMouths": cave_mouths,
        "surfaceWater": bool(cfg.get("surfaceWater")),
        "surfaceTrails": bool(cfg.get("surfaceTrails")) or bool(trails),
    }


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


def render_planner_concept(
    out_path,
    brief: dict[str, Any],
    cfg: dict[str, Any],
    checklist: list[dict[str, Any]] | None = None,
) -> None:
    layout = derive_layout_plan(brief, cfg)
    brief = brief or {}
    cfg = cfg or {}

    img = Image.new("RGB", (W, H), COLORS["bg"])
    draw = ImageDraw.Draw(img)
    title_font = _load_font(26, bold=True)
    body_font = _load_font(16)
    small_font = _load_font(12)
    tiny_font = _load_font(11)
    map_font = _load_font(14, bold=True)

    title = (brief.get("title") or cfg.get("label") or "Build concept")[:72]
    draw.text((24, 20), title, fill=COLORS["text"], font=title_font)
    draw.text((24, 52), "Top-down layout map — approve before build", fill=COLORS["muted"], font=body_font)

    # Map panel
    map_x, map_y, map_w, map_h = 24, 88, 980, 860
    draw.rectangle([map_x, map_y, map_x + map_w, map_y + map_h], fill=COLORS["panel"], outline=(60, 70, 95), width=2)

    cx = map_x + map_w // 2
    cy = map_y + map_h // 2 + 20
    tile = 58
    gx0 = cx - (3 * tile) // 2
    gy0 = cy - (3 * tile) // 2

    extended = int(cfg.get("tileCount", 81)) > 81
    frame = tile * (5 if extended else 3.5)
    draw.rectangle(
        [cx - frame, cy - frame, cx + frame, cy + frame],
        outline=COLORS["horizon"],
        width=2,
    )
    draw.text(
        (cx, map_y + 14),
        layout.get("gridNote", "81-tile shell"),
        fill=COLORS["muted"],
        anchor="mm",
        font=small_font,
    )

    labyrinth = bool((layout.get("playDisk") or {}).get("labyrinth"))
    row_labels = ["N row", "M row", "S row"]

    for row in range(3):
        for col in range(3):
            x0 = gx0 + col * tile
            y0 = gy0 + row * tile
            is_maze = labyrinth and row >= 1
            fill = COLORS["lab"] if is_maze else COLORS["play_open"]
            draw.rectangle([x0 + 2, y0 + 2, x0 + tile - 2, y0 + tile - 2], fill=fill, outline=(20, 50, 30), width=2)
            if is_maze:
                mx, my = x0 + 10, y0 + 10
                mw, mh = tile - 20, tile - 20
                draw.rectangle([mx, my, mx + mw, my + mh], fill=COLORS["lab_wall"])
                draw.rectangle([mx + 6, my + 6, mx + mw - 6, my + mh - 6], fill=COLORS["lab_path"])
                draw.line([mx + 6, my + mh // 2, mx + mw - 6, my + mh // 2], fill=COLORS["lab_wall"], width=4)
                draw.line([mx + mw // 2, my + 6, mx + mw // 2, my + mh - 6], fill=COLORS["lab_wall"], width=3)
            if row == 0 and col == 1:
                draw.ellipse([x0 + tile // 2 - 8, y0 + 6, x0 + tile // 2 + 8, y0 + 18], fill=COLORS["mouth"])
        draw.text((gx0 - 36, gy0 + row * tile + tile // 2), row_labels[row], fill=COLORS["dim"], anchor="rm", font=tiny_font)

    draw.text((cx, gy0 - 22), "9 PLAY TILES (3×3)", fill=(140, 230, 170), anchor="mm", font=map_font)
    lab_note = (layout.get("playDisk") or {}).get("labyrinthNote", "")
    if lab_note:
        draw.text((cx, gy0 + 3 * tile + 16), lab_note, fill=(140, 230, 170), anchor="mm", font=small_font)

    if cfg.get("outerRingMountains"):
        ring = tile * 2.2
        draw.rectangle([cx - ring, cy - ring, cx + ring, cy + ring], outline=COLORS["foot"], width=3)
        draw.text((cx + ring + 8, cy), "foothills", fill=COLORS["foot"], anchor="lm", font=tiny_font)

    if layout.get("surfaceWater") or cfg.get("surfaceWater"):
        draw.rectangle([gx0 - 20, gy0 + 3 * tile + 40, gx0 + 3 * tile + 20, gy0 + 3 * tile + 56], fill=COLORS["water"])
        draw.text((cx, gy0 + 3 * tile + 70), "water band", fill=COLORS["water"], anchor="mm", font=tiny_font)

    # Floating islands
    island_info = layout.get("islands") or []
    for isl in island_info:
        d = str(isl.get("dir", "N")).upper()
        ix, iy = _island_center(cx, cy, tile, d)
        iw, ih = int(tile * 1.6), int(tile * 1.1)
        draw.rounded_rectangle(
            [ix - iw // 2, iy - ih // 2, ix + iw // 2, iy + ih // 2],
            radius=14,
            fill=COLORS["island"],
            outline=COLORS["island_edge"],
            width=2,
        )
        label = str(isl.get("label", d))[:16]
        draw.text((ix, iy - ih // 2 - 10), label, fill=COLORS["island_edge"], anchor="mm", font=small_font)
        counts = []
        if isl.get("enemies"):
            counts.append(f"⚔ {isl['enemies']}")
        if isl.get("npcs"):
            counts.append(f"NPC {isl['npcs']}")
        if isl.get("props"):
            counts.append(f"▪ {isl['props']}")
        if isl.get("collectibles"):
            counts.append(f"★ {isl['collectibles']}")
        if counts:
            draw.text((ix, iy + ih // 2 + 12), " · ".join(counts), fill=COLORS["dim"], anchor="mm", font=tiny_font)

    # Trails
    trail_colors = [COLORS["trail"], (180, 200, 255), (160, 220, 160), (255, 190, 120)]
    for i, tr in enumerate(layout.get("trails") or []):
        dest = str(tr.get("to", ""))
        col = trail_colors[i % len(trail_colors)]
        if dest.startswith("island-"):
            d = dest.split("-")[-1].upper()
            tx, ty = _island_center(cx, cy, tile, d)
            sx, sy = cx, gy0 + 3 * tile - 4
            if d == "N":
                sx, sy = cx, gy0 - 4
            elif d == "E":
                sx, sy = gx0 + 3 * tile - 4, cy
            elif d == "W":
                sx, sy = gx0 + 4, cy
            draw.line([sx, sy, tx, ty], fill=col, width=4)
            mx, my = (sx + tx) // 2, (sy + ty) // 2
            draw.text((mx, my - 8), str(tr.get("label", "trail"))[:18], fill=col, anchor="mm", font=tiny_font)
        elif "foothill" in dest or "ring" in dest:
            draw.arc([cx - tile * 2, cy - tile * 2, cx + tile * 2, cy + tile * 2], 200, 340, fill=col, width=3)

    if layout.get("surfaceTrails") or cfg.get("surfaceTrails"):
        draw.ellipse([cx - tile * 2.1, cy - tile * 2.1, cx + tile * 2.1, cy + tile * 2.1], outline=COLORS["trail"], width=3)
        draw.text((cx + tile * 2.2, cy - tile * 2), "perimeter trail", fill=COLORS["trail"], font=tiny_font)

    for mouth in layout.get("caveMouths") or []:
        draw.text((cx, gy0 - 6), str(mouth.get("label", "cave"))[:12], fill=COLORS["muted"], anchor="mm", font=tiny_font)

    # Markers
    for m in layout.get("markers") or []:
        kind = str(m.get("kind", "prop"))
        label = str(m.get("label", ""))
        zone = str(m.get("zone", "play"))
        if zone == "play":
            row, col = _coerce_play_row_col(m.get("row", 1), m.get("col", 1))
            x, y = _play_cell_center(gx0, gy0, tile, row, col)
        elif zone == "island":
            d = str(m.get("dir", "N")).upper()
            ix, iy = _island_center(cx, cy, tile, d)
            slot = _coerce_island_slot(m.get("slot", 0))
            x, y = _island_slot_pos(ix, iy, tile, slot)
        else:
            continue
        _draw_marker(draw, x, y, kind, label, tiny_font)

    # Right sidebar: symbol legend + manifest
    leg_x = 1020
    draw.rectangle([leg_x, 88, W - 20, H - 48], fill=(24, 30, 42), outline=(50, 60, 80))
    draw.text((leg_x + 14, 104), "Symbol legend", fill=COLORS["text"], font=body_font)
    legend = [
        ("spawn", "● green", "Player spawn"),
        ("prop", "■ tan", "Props / scatter"),
        ("npc", "● blue", "NPCs"),
        ("enemy", "▲ red", "Enemies"),
        ("collectible", "★ gold", "Collectibles"),
        ("trail", "— orange", "Trails / bridges"),
        ("mouth", "● dark", "Cave mouths"),
    ]
    ly = 132
    for key, sym, desc in legend:
        if key in ("spawn", "npc"):
            draw.ellipse([leg_x + 18, ly, leg_x + 30, ly + 12], fill=COLORS.get(key, COLORS["npc"]))
        elif key == "prop":
            draw.rectangle([leg_x + 18, ly, leg_x + 30, ly + 12], fill=COLORS["prop"])
        elif key == "enemy":
            draw.polygon(
                [(leg_x + 24, ly), (leg_x + 32, ly + 12), (leg_x + 16, ly + 12)],
                fill=COLORS["enemy"],
            )
        elif key == "trail":
            draw.line([leg_x + 16, ly + 6, leg_x + 32, ly + 6], fill=COLORS["trail"], width=3)
        elif key == "mouth":
            draw.ellipse([leg_x + 18, ly, leg_x + 30, ly + 12], fill=COLORS["mouth"])
        else:
            draw.ellipse([leg_x + 18, ly, leg_x + 30, ly + 12], fill=COLORS["collect"])
        draw.text((leg_x + 40, ly), f"{sym}  {desc}", fill=COLORS["dim"], font=tiny_font)
        ly += 20

    ly += 8
    draw.text((leg_x + 14, ly), "Placed content", fill=COLORS["text"], font=body_font)
    ly += 22
    for m in layout.get("markers") or []:
        kind = str(m.get("kind", "")).upper()
        label = str(m.get("label", ""))[:28]
        zone = m.get("zone", "play")
        if zone == "play":
            loc = f"play [{m.get('row', '?')},{m.get('col', '?')}]"
        elif zone == "island":
            loc = f"island {m.get('dir', '?')}"
        else:
            loc = zone
        line = f"{kind}: {label} @ {loc}"
        draw.text((leg_x + 14, ly), line[:42], fill=COLORS["dim"], font=tiny_font)
        ly += 16
        if ly > H - 200:
            draw.text((leg_x + 14, ly), "…", fill=COLORS["dim"], font=tiny_font)
            break

    if checklist:
        ly = max(ly + 12, H - 190)
        draw.text((leg_x + 14, ly), "Decisions", fill=COLORS["text"], font=body_font)
        ly += 20
        for item in checklist[:6]:
            mark = "✓" if item.get("done") else "○"
            color = (120, 210, 140) if item.get("done") else (120, 130, 150)
            draw.text((leg_x + 14, ly), f"{mark} {str(item.get('label', ''))[:30]}", fill=color, font=tiny_font)
            ly += 16

    draw.text(
        (24, H - 28),
        "Concept guide — similar per seed, not pixel-identical · approve to continue",
        fill=(120, 200, 140),
        font=small_font,
    )

    out_path = str(out_path)
    img.save(out_path, format="PNG", optimize=True)
