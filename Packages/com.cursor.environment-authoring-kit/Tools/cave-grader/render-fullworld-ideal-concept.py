#!/usr/bin/env python3
"""Render ideal FullWorld layout concept.png (play-disk labyrinth + 3 mouth trails)."""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

W, H = 1536, 1024
HUB = Path(__file__).resolve().parents[4]
OUT = HUB / "Assets/EnvironmentKit/ResearchCache/images/fullworld-layout-ideal-49/concept.png"


def main() -> None:
    img = Image.new("RGB", (W, H), (18, 32, 42))
    draw = ImageDraw.Draw(img)

    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 22)
        font_sm = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 16)
        font_lg = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 28)
    except OSError:
        font = font_sm = font_lg = ImageFont.load_default()

    cx, cy = W // 2, H // 2 - 40
    tile = 72
    grid_w, grid_h = 3, 3
    gx0 = cx - (grid_w * tile) // 2
    gy0 = cy - (grid_h * tile) // 2 + 20

    play_normal = (72, 140, 88)
    play_maze = (34, 110, 72)
    maze_path = (48, 175, 95)
    maze_wall = (22, 75, 48)
    foothill = (88, 105, 72)
    peak = (110, 95, 82)
    mouth = (40, 48, 55)
    trail = (210, 165, 90)
    horizon = (55, 75, 95)

    # Horizon frame (square 81-tile)
    frame_pad = 200
    draw.rectangle(
        [cx - frame_pad, cy - frame_pad + 10, cx + frame_pad, cy + frame_pad + 90],
        outline=horizon,
        width=3,
    )
    draw.text((cx, cy - frame_pad - 10), "81-tile square world (horizon ring)", fill=(180, 200, 210), anchor="mm", font=font_sm)

    # Play 3x3
    for row in range(grid_h):
        for col in range(grid_w):
            x0 = gx0 + col * tile
            y0 = gy0 + row * tile
            is_maze = row >= 1  # bottom two rows (row 1 middle, row 2 south in screen: row 0 north)
            # Screen: row 0 = north (top), row 2 = south (bottom)
            is_maze = row >= 1
            fill = play_maze if is_maze else play_normal
            draw.rectangle([x0 + 2, y0 + 2, x0 + tile - 2, y0 + tile - 2], fill=fill, outline=(120, 180, 130))

            if is_maze:
                # Simple maze pattern per tile
                mx, my = x0 + 12, y0 + 12
                mw, mh = tile - 24, tile - 24
                draw.rectangle([mx, my, mx + mw, my + mh], fill=maze_wall)
                draw.rectangle([mx + 8, my + 8, mx + mw - 8, my + mh - 8], fill=maze_path)
                draw.line([mx + 8, my + mh // 2, mx + mw - 8, my + mh // 2], fill=maze_wall, width=6)
                draw.line([mx + mw // 2, my + 8, mx + mw // 2, my + mh - 8], fill=maze_wall, width=5)

            if row == 0 and col == 1:
                draw.ellipse([x0 + tile // 2 - 10, y0 + 8, x0 + tile // 2 + 10, y0 + 22], fill=mouth)

    draw.text((cx, gy0 - 28), "Play disk 3×3", fill=(220, 235, 225), anchor="mm", font=font)
    draw.text((cx, gy0 + tile + 8), "LABYRINTH: south 2 rows (6 tiles)", fill=(140, 230, 170), anchor="mm", font=font_sm)
    draw.text((cx, gy0 - tile - 8), "North row: walk + primary cave", fill=(200, 220, 200), anchor="mm", font=font_sm)

    # Foothills
    fh_y0 = gy0 + grid_h * tile + 16
    fh_h = 56
    draw.rectangle([gx0 - 20, fh_y0, gx0 + grid_w * tile + 20, fh_y0 + fh_h], fill=foothill, outline=(130, 150, 110))
    draw.text((cx, fh_y0 + fh_h // 2), "Foothills — hills only (no maze)", fill=(230, 235, 210), anchor="mm", font=font_sm)

    # Peaks
    pk_y0 = fh_y0 + fh_h + 12
    pk_h = 64
    draw.rectangle([gx0 - 20, pk_y0, gx0 + grid_w * tile + 20, pk_y0 + pk_h], fill=peak, outline=(150, 130, 110))
    draw.text((cx, pk_y0 + 18), "South peaks", fill=(240, 230, 220), anchor="mm", font=font_sm)

    mouth_y = pk_y0 + pk_h - 6
    mouth_xs = [gx0 + tile * 0.5, gx0 + tile * 1.5, gx0 + tile * 2.5]
    labels = ["Cave W", "Cave C", "Cave E"]
    for mx, lab in zip(mouth_xs, labels):
        draw.ellipse([mx - 14, mouth_y - 6, mx + 14, mouth_y + 14], fill=mouth, outline=(180, 200, 220))
        draw.text((mx, mouth_y + 28), lab, fill=(220, 220, 230), anchor="mm", font=font_sm)

    # Trails from labyrinth south edge
    south_y = gy0 + grid_h * tile - 8
    exits = mouth_xs
    exit_y = south_y
    for ex, mx, col in zip(exits, mouth_xs, [trail, (180, 200, 255), (160, 220, 160)]):
        draw.line([ex, exit_y, mx, mouth_y], fill=col, width=5)
    draw.text((cx, pk_y0 + pk_h + 24), "Trails: play maze → each peak mouth", fill=trail, anchor="mm", font=font_sm)

    draw.text((cx, 36), "Ideal FullWorld layout (target concept)", fill=(230, 240, 245), anchor="mm", font=font_lg)
    draw.text(
        (cx, H - 28),
        "Similar per seed — not pixel-identical · square grid · rebuild to apply",
        fill=(150, 170, 180),
        anchor="mm",
        font=font_sm,
    )

    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT, "PNG", optimize=True)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
