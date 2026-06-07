#!/usr/bin/env python3
"""Generate Hollow Titan concept guide PNGs — master + phases 00–11 (wireframe layout guides)."""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / "Assets/EnvironmentKit/ResearchCache/images/hollow-titan-concepts"
W, H = 1536, 1024

# palette
BG = (22, 28, 36)
TRUNK = (58, 42, 30)
TRUNK_OUTLINE = (90, 68, 48)
HOLLOW = (18, 22, 28)
FLOOR = (72, 58, 42)
STAIR = (95, 78, 55)
BRANCH = (40, 32, 24)
ENTRANCE = (120, 90, 60)
LIGHT = (220, 170, 90)
FOG = (80, 100, 120, 60)
SPAWN_ENEMY = (180, 60, 50)
SPAWN_LOOT = (50, 160, 80)
SPAWN_PATROL = (60, 100, 180)
GROUND = (55, 75, 55)
KARST = (70, 85, 65)


def load_fonts():
    try:
        title = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 24)
        body = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 15)
        small = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 13)
        return title, body, small
    except OSError:
        d = ImageFont.load_default()
        return d, d, d


def trunk_params():
    """Massive landmark proportions from HollowTitanLandmarkMeatPhases defaults."""
    return {"radius": 30, "height": 82, "floors": 4, "floor_h": 6.5}


def draw_ground(draw: ImageDraw.ImageDraw, cx: int, base_y: int, w: int):
    draw.rectangle([0, base_y, w, H], fill=GROUND)
    for i in range(-6, 7):
        x = cx + i * 55
        draw.ellipse([x - 30, base_y - 8, x + 30, base_y + 18], fill=KARST, outline=None)


def draw_trunk_wireframe(draw: ImageDraw.ImageDraw, cx: int, base_y: int, scale: float, highlight: str | None = None):
    p = trunk_params()
    r = int(p["radius"] * scale * 0.9)
    h = int(p["height"] * scale * 0.55)
    top = base_y - h
    fill = TRUNK if highlight != "trunk" else (78, 58, 40)
    draw.ellipse([cx - r, top - r // 6, cx + r, top + r // 6], fill=fill, outline=TRUNK_OUTLINE, width=3)
    draw.rectangle([cx - r, top, cx + r, base_y], fill=fill, outline=TRUNK_OUTLINE, width=3)
    draw.ellipse([cx - r, base_y - r // 5, cx + r, base_y + r // 5], fill=fill, outline=TRUNK_OUTLINE, width=2)
    return r, h, top


def draw_hollow_void(draw: ImageDraw.ImageDraw, cx: int, base_y: int, scale: float, r: int, h: int, top: int):
    ir = int(r * 0.62)
    ih = int(h * 0.88)
    itop = top + int(h * 0.06)
    draw.ellipse([cx - ir, itop - ir // 8, cx + ir, itop + ir // 8], fill=HOLLOW, outline=(50, 60, 75), width=2)
    draw.rectangle([cx - ir, itop, cx + ir, itop + ih], fill=HOLLOW, outline=(50, 60, 75), width=2)


def draw_floors(draw: ImageDraw.ImageDraw, cx: int, scale: float, base_y: int, r: int, h: int, top: int, count: int = 4):
    p = trunk_params()
    floor_h = p["floor_h"] * scale * 0.55
    for i in range(count):
        fy = base_y - (i + 1) * floor_h * (h / (p["height"] * scale * 0.55)) * 0.22
        fr = int(r * (0.95 - i * 0.08))
        draw.ellipse([cx - fr, fy - 4, cx + fr, fy + 4], fill=FLOOR, outline=(110, 90, 65), width=2)


def draw_spiral_stairs(draw: ImageDraw.ImageDraw, cx: int, r: int, base_y: int, h: int, top: int):
    steps = 28
    for s in range(steps):
        t = s / steps
        angle = t * math.pi * 3.2
        y = base_y - t * h * 0.85
        x = cx + math.cos(angle) * r * 0.55
        zoff = math.sin(angle) * 8
        draw.rectangle([x - 8 + zoff, y - 3, x + 8 + zoff, y + 3], fill=STAIR)


def draw_branches(draw: ImageDraw.ImageDraw, cx: int, base_y: int, r: int, h: int, top: int, count: int = 8):
    for i in range(count):
        angle = i * (360 / count) + 15
        rad = math.radians(angle)
        height = top + h * (0.35 + (i % 5) * 0.1)
        bx = cx + math.sin(rad) * r * 0.85
        by = height
        ex = bx + math.sin(rad) * r * 0.5
        ey = by - r * 0.25
        draw.line([bx, by, ex, ey], fill=BRANCH, width=5)


def draw_entrance(draw: ImageDraw.ImageDraw, cx: int, base_y: int, r: int):
    ex = cx
    ey = base_y - r * 0.35
    draw.rectangle([ex - r * 0.35, ey - r * 0.45, ex + r * 0.35, ey + r * 0.2], fill=ENTRANCE, outline=(150, 110, 70), width=2)
    draw.rectangle([ex - r * 0.22, ey - r * 0.3, ex + r * 0.22, ey + r * 0.05], fill=HOLLOW)


def draw_site_marker(draw: ImageDraw.ImageDraw, cx: int, base_y: int):
    draw.ellipse([cx - 12, base_y + 8, cx + 12, base_y + 32], outline=(200, 220, 100), width=3)
    draw.line([cx, base_y - 120, cx, base_y + 20], fill=(200, 220, 100), width=2)


def draw_spawn_dots(draw: ImageDraw.ImageDraw, cx: int, base_y: int, r: int, h: int, kind: str):
    colors = {"enemy": SPAWN_ENEMY, "loot": SPAWN_LOOT, "patrol": SPAWN_PATROL}
    c = colors.get(kind, SPAWN_ENEMY)
    for floor in range(4):
        fy = base_y - (floor + 1) * h * 0.18
        n = 3 if kind == "patrol" else 2
        for i in range(n):
            ang = math.radians(i * (360 / n) + floor * 40)
            x = cx + math.cos(ang) * r * 0.45
            y = fy
            draw.ellipse([x - 6, y - 6, x + 6, y + 6], fill=c)


def draw_lights(draw: ImageDraw.ImageDraw, cx: int, base_y: int, h: int):
    for floor in range(4):
        fy = base_y - (floor + 1) * h * 0.18
        draw.ellipse([cx - 10, fy - 10, cx + 10, fy + 10], fill=LIGHT, outline=(255, 220, 150))


def draw_fog(draw: ImageDraw.ImageDraw, cx: int, base_y: int, h: int, r: int):
    fog_y = base_y - h * 0.45
    draw.ellipse([cx - r * 0.9, fog_y - r * 0.4, cx + r * 0.9, fog_y + r * 0.4], fill=(60, 80, 100), outline=(90, 110, 130), width=2)


def caption(phase: int | None) -> str:
    caps = {
        None: "Master — mystical medieval hollow dead tree, Florida karst peak tile, 4 floors, spiral stairs, dead branches, entrance arch",
        0: "Site pick — random peak surface tile, exclude mouths/labyrinth/trails",
        1: "Base snap — SampleHeight + clearance on karst ground",
        2: "Trunk shell — L01 dead tree / bark cylinder mass",
        3: "Hollow carve — inner void ~62% radius",
        4: "Floor plates — 4 ring platforms inside trunk",
        5: "Stair spiral — ramp between all floors",
        6: "Dead branches — exterior broken stubs",
        7: "Entrance framing — arched gate + portal approach",
        8: "Per-floor enemy spawns — landmark-only markers",
        9: "Lighting & fog — warm interior mood",
        10: "Landmark loot table — per-floor loot markers + manifest",
        11: "Enemy patrol nodes — ring patrol per floor",
    }
    return caps.get(phase, caps[None])


def render_master(draw: ImageDraw.ImageDraw, cx: int, cy: int, title_font, body_font):
    base_y = cy + 180
    scale = 2.2
    draw_ground(draw, cx, base_y, W)
    r, h, top = draw_trunk_wireframe(draw, cx, base_y, scale)
    draw_hollow_void(draw, cx, base_y, scale, r, h, top)
    draw_floors(draw, cx, scale, base_y, r, h, top)
    draw_spiral_stairs(draw, cx, r, base_y, h, top)
    draw_branches(draw, cx, base_y, r, h, top, 12)
    draw_entrance(draw, cx, base_y, r)
    draw_lights(draw, cx, base_y, h)
    draw_fog(draw, cx, base_y, h, r)
    draw_spawn_dots(draw, cx, base_y, r, h, "enemy")
    draw_spawn_dots(draw, cx, base_y, r, h, "loot")
    draw_spawn_dots(draw, cx, base_y, r, h, "patrol")


def render_phase(draw: ImageDraw.ImageDraw, phase: int, cx: int, cy: int):
    base_y = cy + 160
    scale = 2.4
    draw_ground(draw, cx, base_y, W)
    r, h, top = draw_trunk_wireframe(draw, cx, base_y, scale, highlight="trunk" if phase == 2 else None)

    if phase == 0:
        draw_site_marker(draw, cx, base_y)
        draw_trunk_wireframe(draw, cx - 200, base_y, scale * 0.35)
        draw_trunk_wireframe(draw, cx + 220, base_y, scale * 0.3)
    if phase >= 1:
        draw.line([cx, base_y + 5, cx, base_y + 25], fill=(255, 230, 120), width=4)
    if phase >= 3:
        draw_hollow_void(draw, cx, base_y, scale, r, h, top)
    if phase >= 4:
        draw_floors(draw, cx, scale, base_y, r, h, top)
    if phase >= 5:
        draw_spiral_stairs(draw, cx, r, base_y, h, top)
    if phase >= 6:
        draw_branches(draw, cx, base_y, r, h, top, 10)
    if phase >= 7:
        draw_entrance(draw, cx, base_y, r)
    if phase >= 8:
        draw_spawn_dots(draw, cx, base_y, r, h, "enemy")
    if phase >= 9:
        draw_lights(draw, cx, base_y, h)
        draw_fog(draw, cx, base_y, h, r)
    if phase >= 10:
        draw_spawn_dots(draw, cx, base_y, r, h, "loot")
    if phase >= 11:
        draw_spawn_dots(draw, cx, base_y, r, h, "patrol")


def write_image(folder: Path, name: str, phase: int | None, title_font, body_font, small_font):
    folder.mkdir(parents=True, exist_ok=True)
    img = Image.new("RGB", (W, H), BG)
    draw = ImageDraw.Draw(img)
    cx, cy = W // 2, H // 2 - 40
    label = "Hollow Titan — master overview" if phase is None else f"Hollow Titan — phase {phase:02d}"
    draw.text((cx, 36), label, fill=(230, 240, 245), anchor="mm", font=title_font)
    draw.text((cx, 64), caption(phase), fill=(180, 200, 210), anchor="mm", font=body_font)
    if phase is None:
        render_master(draw, cx, cy, title_font, body_font)
    else:
        render_phase(draw, phase, cx, cy)
    footer = "trunk ~30m r × ~82m h · 4 floors · landmark-only spawns · ground snap · similar per seed"
    draw.text((cx, H - 28), footer, fill=(150, 170, 180), anchor="mm", font=small_font)
    path = folder / name
    img.save(path)
    readme = folder / "README.md"
    readme.write_text(f"# {label}\n\n{caption(phase)}\n\n{footer}\n", encoding="utf-8")
    print("wrote", path)


def main():
    title_font, body_font, small_font = load_fonts()
    write_image(OUT / "master", "concept.png", None, title_font, body_font, small_font)
    pending = OUT / "_pending-review" / "master"
    write_image(pending, "option-01.png", None, title_font, body_font, small_font)
    for i in range(12):
        write_image(OUT / f"{i:02d}", "concept.png", i, title_font, body_font, small_font)


if __name__ == "__main__":
    main()
