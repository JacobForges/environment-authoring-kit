#!/usr/bin/env python3
"""Generate biome layout concept guide PNGs — master + presets 0–9 with feather bands."""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / "Assets/EnvironmentKit/ResearchCache/images/biome-layout-concepts"
PENDING = OUT / "_pending-review"
W, H = 1536, 1024
MAX_CHEBYSHEV_RADIUS = 8
TILE_SIZE_M = 220
TOTAL_TILES = 289

# Biome zone colors (prop identity — not terrain alphamap)
BIOMES = {
    "play_karst": (88, 165, 110),
    "foothill": (95, 130, 72),
    "peak": (110, 95, 82),
    "horizon": (55, 85, 110),
    "mixed": (72, 100, 78),
    "water": (66, 165, 245),
    "annex": (34, 110, 72),
    "preset": (45, 65, 55),
    "feather": (180, 160, 90),
    "grid": (30, 45, 55),
}


def cheb_ring_color(ring: int) -> tuple[int, int, int]:
    if ring <= 1:
        return BIOMES["play_karst"]
    if ring <= 4:
        return BIOMES["foothill"]
    if ring <= 7:
        return BIOMES["peak"]
    if ring <= 9:
        return BIOMES["mixed"]
    return BIOMES["preset"]


def draw_feather_band(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, cheb: float, width: float = 1.75):
    """Dashed feather band at Chebyshev boundary."""
    side = int((cheb * 2 + 1) * cell)
    half = side // 2
    x0, y0 = cx - half, cy - half
    x1, y1 = cx + half, cy + half
    for i in range(0, side, max(4, cell // 2)):
        draw.line([(x0 + i, y0), (x0 + i, y1)], fill=BIOMES["feather"], width=1)
        draw.line([(x0, y0 + i), (x1, y0 + i)], fill=BIOMES["feather"], width=1)


def draw_chebyshev_grid(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, max_ring: int = MAX_CHEBYSHEV_RADIUS):
    for ring in range(max_ring, -1, -1):
        side = 2 * ring + 1
        half = ring * cell
        x0, y0 = cx - half, cy - half
        x1, y1 = cx + half, cy + half
        color = cheb_ring_color(ring)
        if ring == 0:
            draw.rectangle([x0, y0, x1, y1], fill=color, outline=BIOMES["grid"])
        else:
            draw.rectangle([x0, y0, x1, y1], outline=color, width=max(1, cell // 10))

    # Feather bands at zone boundaries
    draw_feather_band(draw, cx, cy, cell, 1.5)
    draw_feather_band(draw, cx, cy, cell, 9.5)


def draw_play_disk(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, labyrinth: bool = False):
    play_cell = max(4, cell // 3)
    gx0 = cx - play_cell
    gy0 = cy - play_cell
    for row in range(3):
        for col in range(3):
            x0 = gx0 + col * play_cell
            y0 = gy0 + row * play_cell
            is_lab = labyrinth and row >= 1
            fill = BIOMES["annex"] if is_lab else BIOMES["play_karst"]
            draw.rectangle([x0, y0, x0 + play_cell - 1, y0 + play_cell - 1], fill=fill, outline=(20, 50, 30))


def draw_water_band(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, wide: bool = False):
    h = 14 if not wide else 22
    draw.rectangle([cx - cell * 3, cy + cell, cx + cell * 3, cy + cell + h], fill=BIOMES["water"])


def draw_preset_wedge_hints(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, highlight: int | None = None):
    for sector in range(10):
        angle = sector * (2 * math.pi / 10) - math.pi / 2
        r0 = cell * 10
        r1 = cell * 16
        x0 = cx + math.cos(angle) * r0
        y0 = cy + math.sin(angle) * r0
        x1 = cx + math.cos(angle + 0.25) * r1
        y1 = cy + math.sin(angle + 0.25) * r1
        color = BIOMES["preset"] if sector != highlight else BIOMES["foothill"]
        draw.line([(x0, y0), (x1, y1)], fill=color, width=3)


def draw_prop_legend(draw: ImageDraw.ImageDraw, x: int, y: int, items: list[tuple[str, tuple[int, int, int]]], font):
    for i, (label, color) in enumerate(items):
        yy = y + i * 22
        draw.rectangle([x, yy, x + 16, yy + 16], fill=color)
        draw.text((x + 22, yy), label, fill=(210, 220, 230), font=font)


def draw_master(draw: ImageDraw.ImageDraw, cx: int, cy: int, cell: int, font, small):
    draw_chebyshev_grid(draw, cx, cy, cell)
    draw_play_disk(draw, cx, cy, cell, labyrinth=True)
    draw_water_band(draw, cx, cy, cell)
    draw_preset_wedge_hints(draw, cx, cy, cell)

    legend = [
        ("Play karst meadow", BIOMES["play_karst"]),
        ("Foothill green", BIOMES["foothill"]),
        ("Peak stone", BIOMES["peak"]),
        ("Horizon mist", BIOMES["horizon"]),
        ("Mixed transition", BIOMES["mixed"]),
        ("Feather band (~1.75 tiles)", BIOMES["feather"]),
        ("Water/coastal", BIOMES["water"]),
        ("Preset wedge", BIOMES["preset"]),
    ]
    draw_prop_legend(draw, 40, 120, legend, small)

    draw.text(
        (cx, H - 52),
        f"289 tiles (17×17 @ {TILE_SIZE_M}m) — prop feather transitions, not alphamap fade",
        fill=(160, 180, 190),
        anchor="mm",
        font=small,
    )


def draw_preset(draw: ImageDraw.ImageDraw, index: int, cx: int, cy: int, cell: int):
    draw_chebyshev_grid(draw, cx, cy, cell)
    draw_preset_wedge_hints(draw, cx, cy, cell, highlight=index)

    if index == 0:
        draw_play_disk(draw, cx, cy, cell, labyrinth=True)
    elif index == 2:
        draw_play_disk(draw, cx, cy, cell)
        draw_water_band(draw, cx, cy, cell)
    elif index == 3:
        draw_play_disk(draw, cx, cy, cell)
        draw.rectangle([cx - cell * 3, cy - cell * 3, cx + cell * 3, cy + cell * 3], outline=BIOMES["peak"], width=4)
    elif index == 4:
        draw_play_disk(draw, cx, cy, cell)
        draw_water_band(draw, cx, cy, cell, wide=True)
    elif index == 5:
        draw_play_disk(draw, cx, cy, cell, labyrinth=True)
    elif index == 7:
        draw_play_disk(draw, cx, cy, cell, labyrinth=True)
        draw_water_band(draw, cx, cy, cell)
    elif index == 8:
        draw_play_disk(draw, cx, cy, cell)
        draw.ellipse([cx - cell * 2, cy - cell * 2, cx + cell * 2, cy + cell * 2], outline=BIOMES["foothill"], width=4)
    elif index == 9:
        draw.rectangle([cx - cell, cy - cell, cx + cell, cy + cell], fill=BIOMES["play_karst"], outline=BIOMES["grid"], width=2)
    else:
        draw_play_disk(draw, cx, cy, cell)


CAPTIONS = [
    "Ideal: karst play + foothill/peak feather → preset 00 wedge",
    "Classic: concentric biome rings with feather bands",
    "Florida karst: flat play + water/coastal props",
    "Appalachian: peak-dominant mixed ring",
    "Coastal fog: horizon mist + water edge",
    "Tomb Raider: labyrinth annex + peak mouths",
    "Sparse trails: light prop density",
    "Water labyrinth: basins + foothill maze",
    "Perimeter trail ring: bush band cheb 2–3",
    "Speed minimal: play disk props only",
]


def main():
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 22)
        small = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 14)
    except OSError:
        font = ImageFont.load_default()
        small = font

    cx, cy = W // 2, H // 2 + 20
    cell = 14

    # Master
    master_dir = OUT / "master"
    master_dir.mkdir(parents=True, exist_ok=True)
    pending_master = PENDING / "master"
    pending_master.mkdir(parents=True, exist_ok=True)

    img = Image.new("RGB", (W, H), (18, 32, 42))
    draw = ImageDraw.Draw(img)
    draw.text((cx, 36), "Biome layout master — prop feather transitions", fill=(230, 240, 245), anchor="mm", font=font)
    draw.text((cx, 62), "Mystical Florida karst — 289-tile FullWorld", fill=(180, 200, 210), anchor="mm", font=small)
    draw_master(draw, cx, cy, cell, font, small)
    img.save(master_dir / "concept.png")
    img.save(pending_master / "option-01.png")
    print("wrote", master_dir / "concept.png")

    for i in range(10):
        folder = OUT / f"{i:02d}"
        folder.mkdir(parents=True, exist_ok=True)
        img = Image.new("RGB", (W, H), (18, 32, 42))
        draw = ImageDraw.Draw(img)
        draw.text((cx, 36), f"Biome layout preset {i}", fill=(230, 240, 245), anchor="mm", font=font)
        draw.text((cx, 62), CAPTIONS[i], fill=(180, 200, 210), anchor="mm", font=small)
        draw_preset(draw, i, cx, cy, cell)
        draw.text(
            (cx, H - 28),
            f"Feather ~1.75 Chebyshev tiles — mix adjacent biome prop pools",
            fill=(150, 170, 180),
            anchor="mm",
            font=small,
        )
        img.save(folder / "concept.png")
        (folder / "README.md").write_text(
            f"# Biome layout preset {i}\n\n{CAPTIONS[i]}\n\nRef: fullworld-concepts/{i:02d}/concept.png\n",
            encoding="utf-8",
        )
        print("wrote", folder / "concept.png")


if __name__ == "__main__":
    main()
