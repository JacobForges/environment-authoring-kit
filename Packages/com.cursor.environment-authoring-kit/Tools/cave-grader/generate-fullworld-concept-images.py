#!/usr/bin/env python3
"""Generate FullWorld concept guide PNGs (0–9) — top-down layout on 17×17 Chebyshev grid."""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts"
W, H = 1536, 1024

# Grid vocabulary (SurfaceOpenWorldGridExpansion.cs, FullWorldBiomeZoneLayout.cs)
TILE_SIZE_M = 220
MAX_CHEBYSHEV_RADIUS = 8
GRID_SIDE = 2 * MAX_CHEBYSHEV_RADIUS + 1  # 17
TOTAL_TILES = GRID_SIDE * GRID_SIDE  # 289
PLAY_TILES = 9
MIXED_TILES = 72
PRESET_ZONE_TILES = 208

# zone colors
ZONES = {
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
    "ring": (100, 181, 246),
    "min": (120, 120, 120),
    "mouth": (40, 48, 55),
}


def cheb_ring_color(ring: int) -> tuple[int, int, int]:
    if ring <= 1:
        return ZONES["play"]
    if ring == 2:
        return ZONES["foot"]
    if ring == 3:
        return ZONES["peak"]
    if ring == 4:
        return ZONES["horizon"]
    if ring <= 9:
        return ZONES["mixed"]
    return ZONES["open"]


def draw_chebyshev_grid(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, max_ring: int = MAX_CHEBYSHEV_RADIUS):
    """Draw 17×17 grid as concentric Chebyshev squares."""
    for ring in range(max_ring, -1, -1):
        side = 2 * ring + 1
        half = ring * cell
        x0, y0 = cx - half, cy - half
        x1, y1 = cx + half, cy + half
        color = cheb_ring_color(ring)
        if ring == 0:
            draw.rectangle([x0, y0, x1, y1], fill=color, outline=(30, 30, 30))
        else:
            draw.rectangle([x0, y0, x1, y1], outline=color, width=max(1, cell // 8))


def draw_play_disk_3x3(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, labyrinth: bool = False):
    """Center 3×3 play disk. If labyrinth: south 2 rows (6 tiles) carved."""
    play_cell = max(4, cell // 3)
    gx0 = cx - play_cell
    gy0 = cy - play_cell
    for row in range(3):
        for col in range(3):
            x0 = gx0 + col * play_cell
            y0 = gy0 + row * play_cell
            is_lab = labyrinth and row >= 1  # middle + south rows = 6 tiles
            fill = ZONES["lab"] if is_lab else ZONES["play_open"]
            draw.rectangle([x0, y0, x0 + play_cell - 1, y0 + play_cell - 1], fill=fill, outline=(20, 50, 30))
            if is_lab:
                # maze hint per tile
                mx, my = x0 + 2, y0 + 2
                mw, mh = play_cell - 4, play_cell - 4
                draw.rectangle([mx, my, mx + mw, my + mh], fill=(22, 75, 48))
                draw.rectangle([mx + 2, my + 2, mx + mw - 2, my + mh - 2], fill=(48, 175, 95))
            if row == 0 and col == 1:
                draw.ellipse([x0 + play_cell // 2 - 3, y0 + 2, x0 + play_cell // 2 + 3, y0 + 8], fill=ZONES["mouth"])


def draw_concentric_rings(draw: ImageDraw.ImageDraw, cx: int, cy: int, radii: list[int], color_key: str = "ring"):
    c = ZONES.get(color_key, ZONES["ring"])
    for r in radii:
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=c, width=3)


def draw_south_peaks_mouths(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int):
    """Three south peak cave mouths below play disk."""
    pk_y = cy + cell * 2
    mouth_xs = [cx - cell, cx, cx + cell]
    for mx in mouth_xs:
        draw.ellipse([mx - 8, pk_y - 4, mx + 8, pk_y + 10], fill=ZONES["mouth"], outline=(180, 200, 220))
        draw.line([cx, cy + cell, mx, pk_y], fill=ZONES["trail"], width=3)


def draw_water_band(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int):
    draw.rectangle([cx - cell * 3, cy + cell, cx + cell * 3, cy + cell + 12], fill=ZONES["water"])


def draw_trail_ring(draw: ImageDraw.ImageDraw, cx: int, cy: int, r: int):
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=ZONES["trail"], width=4)


def draw_preset(draw: ImageDraw.ImageDraw, index: int, cx: int, cy: int, cell: int):
    draw_chebyshev_grid(draw, cx, cy, cell)
    if index == 0:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=True)
        draw_south_peaks_mouths(draw, cx, cy, cell)
    elif index == 1:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=False)
        draw_concentric_rings(draw, cx, cy, [cell, cell * 2, cell * 3, cell * 5])
    elif index == 2:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=False)
        draw_water_band(draw, cx, cy, cell)
    elif index == 3:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=False)
        draw.rectangle([cx - cell * 3, cy - cell * 3, cx + cell * 3, cy + cell * 3], outline=ZONES["peak"], width=4)
    elif index == 4:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=False)
        draw_water_band(draw, cx, cy, cell * 4)
    elif index == 5:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=True)
        draw.rectangle([cx + cell, cy - cell * 2, cx + cell + 20, cy + cell * 2], fill=ZONES["lab"])
    elif index == 6:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=False)
        for dx, dy in [(-cell * 2, 0), (cell * 2, -cell), (cell * 2, cell), (-cell * 2, cell)]:
            draw.ellipse([cx + dx - 4, cy + dy - 4, cx + dx + 4, cy + dy + 4], fill=ZONES["trail"])
    elif index == 7:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=True)
        draw_water_band(draw, cx, cy, cell)
    elif index == 8:
        draw_play_disk_3x3(draw, cx, cy, cell, labyrinth=True)
        draw_trail_ring(draw, cx, cy, cell * 2)
    else:
        draw.rectangle([cx - 8, cy - 8, cx + 8, cy + 8], fill=ZONES["min"])


def caption(index: int) -> str:
    caps = [
        "Ideal: 3×3 play + 6-tile labyrinth (south 2 rows) + peaks — 17×17 grid",
        "Classic: open concentric rings on 289-tile grid — minimal maze",
        "Florida karst: flat play + water on 17×17 grid",
        "Appalachian: tall peak ring on full grid",
        "Coastal fog: low hills + water edge — full extent",
        "Tomb Raider: vertical cave + play labyrinth — full grid",
        "Sparse: jumps + scattered trails — 289 tiles",
        "Water + foothill labyrinth — full grid",
        "Perimeter trail ring on foothills — 17×17",
        "Speed minimal: small play core — full grid muted",
    ]
    return caps[index]


def grid_footer() -> str:
    return (
        f"17×17 Chebyshev (r={MAX_CHEBYSHEV_RADIUS}) ≈ {TOTAL_TILES} tiles @ {TILE_SIZE_M}m — "
        "9 play + 72 mixed + 208 open — similar per seed, not pixel-identical"
    )


def main():
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 22)
        small = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 14)
    except OSError:
        font = ImageFont.load_default()
        small = font

    cx, cy = W // 2, H // 2 + 20
    cell = 28  # ~17×17 fits in ~490px

    for i in range(10):
        folder = OUT / f"{i:02d}"
        folder.mkdir(parents=True, exist_ok=True)
        img = Image.new("RGB", (W, H), (18, 32, 42))
        draw = ImageDraw.Draw(img)
        draw.text((cx, 36), f"FullWorld concept {i}", fill=(230, 240, 245), anchor="mm", font=font)
        draw.text((cx, 62), caption(i), fill=(180, 200, 210), anchor="mm", font=small)
        draw_preset(draw, i, cx, cy, cell)
        draw.text((cx, H - 28), grid_footer(), fill=(150, 170, 180), anchor="mm", font=small)
        img.save(folder / "concept.png")
        (folder / "README.md").write_text(
            f"# FullWorld concept {i}\n\n{caption(i)}\n\n{grid_footer()}\n",
            encoding="utf-8",
        )
        print("wrote", folder / "concept.png")


if __name__ == "__main__":
    main()
