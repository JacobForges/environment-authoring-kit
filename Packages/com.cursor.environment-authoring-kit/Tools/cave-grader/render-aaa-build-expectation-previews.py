#!/usr/bin/env python3
"""Unity Editor / Game view mockups using project terrain textures."""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps

HUB = Path(__file__).resolve().parents[4]
OUT_DIR = HUB / "Assets/EnvironmentKit/ResearchCache/images/aaa-build-expectations"

GRASS_PSD = HUB / "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/textures_terrain/tex_grass_diff.psd"
ROCK_DIFF = HUB / "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Rocks/Textures/rock03_diff.tga"
TREE_ATLAS = HUB / "Assets/PolitePenguin/LPMagicalForest/Textures/T_ColorAtlas_LowPoly_01.png"

# Unity dark theme (approx)
U_BG = (30, 30, 30)
U_PANEL = (56, 56, 56)
U_PANEL2 = (45, 45, 45)
U_BORDER = (26, 26, 26)
U_TEXT = (210, 210, 210)
U_DIM = (150, 150, 150)
U_ACCENT = (68, 140, 202)
U_TAB_ON = (62, 95, 120)
U_TAB_OFF = (46, 46, 46)
U_HIER_SEL = (44, 93, 135)
U_SCENE_SKY = (115, 165, 205)


def load_tex(path: Path, size: int = 128) -> Image.Image:
    return ImageOps.fit(Image.open(path).convert("RGB"), (size, size), Image.Resampling.LANCZOS)


def fonts():
    try:
        return (
            ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 11),
            ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 11),
            ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 10),
            ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 13),
        )
    except OSError:
        d = ImageFont.load_default()
        return d, d, d, d


def draw_tab_bar(draw, x0, y0, w, tabs, active: int, font):
    draw.rectangle([x0, y0, x0 + w, y0 + 22], fill=U_PANEL2)
    tx = x0 + 6
    for i, label in enumerate(tabs):
        tw = 8 + len(label) * 6
        fill = U_TAB_ON if i == active else U_TAB_OFF
        draw.rectangle([tx, y0 + 3, tx + tw, y0 + 20], fill=fill)
        draw.text((tx + 6, y0 + 5), label, fill=U_TEXT, font=font)
        tx += tw + 4


def draw_hierarchy(draw, box, font, font_b, selected: str):
    x0, y0, x1, y1 = box
    draw.rectangle(box, fill=U_PANEL2)
    draw.text((x0 + 8, y0 + 6), "Hierarchy", fill=U_TEXT, font=font_b)
    lines = [
        ("EnvironmentRoot", 0, False),
        ("SurfaceWorld", 1, False),
        ("  SurfaceTerrainMain", 2, True),
        ("  SurfaceTerrainTiles", 1, False),
        ("  MountainWilderness", 1, False),
        ("  SurfaceOpenWorldTile_0_12", 1, False),
        ("  … (289 terrains)", 1, False),
        ("  HollowTitanLandmark", 1, False),
        ("  Trails", 1, False),
        ("  Vegetation", 1, False),
    ]
    y = y0 + 28
    for label, indent, sel in lines:
        ix = x0 + 8 + indent * 12
        if sel and "SurfaceTerrainMain" in label:
            draw.rectangle([x0 + 2, y - 1, x1 - 2, y + 14], fill=U_HIER_SEL)
        draw.rectangle([ix, y + 2, ix + 10, y + 12], fill=(90, 140, 90) if "Terrain" in label else (120, 120, 120))
        draw.text((ix + 14, y), label, fill=U_TEXT, font=font)
        y += 17


def draw_inspector(draw, box, font, font_b):
    x0, y0, x1, y1 = box
    draw.rectangle(box, fill=U_PANEL2)
    draw.text((x0 + 8, y0 + 6), "Inspector", fill=U_TEXT, font=font_b)
    rows = [
        ("SurfaceTerrainMain", True),
        ("Tag: Ground", False),
        ("Transform", False),
        ("  Position  (locked grid)", False),
        ("Terrain", True),
        ("  Material: Built-in", False),
        ("  Layers: tex_grass_diff …", False),
        ("  Neighbors: 4", False),
        ("Terrain Collider", False),
    ]
    y = y0 + 30
    for text, header in rows:
        draw.text((x0 + 10, y), text, fill=U_ACCENT if header else U_DIM, font=font_b if header else font)
        y += 16 if header else 14


def project_quad(img, draw, quad, texture: Image.Image):
    """Simple affine-ish fill: paste scaled texture into bounding box of quad."""
    xs = [p[0] for p in quad]
    ys = [p[1] for p in quad]
    x0, x1 = int(min(xs)), int(max(xs))
    y0, y1 = int(min(ys)), int(max(ys))
    if x1 <= x0 or y1 <= y0:
        return
    patch = texture.resize((x1 - x0, y1 - y0))
    mask = Image.new("L", patch.size, 255)
    img.paste(patch, (x0, y0), mask)


def draw_scene_terrain_perspective(img, draw, scene_box, grass, rock):
    x0, y0, x1, y1 = scene_box
    sw, sh = x1 - x0, y1 - y0
    # sky gradient
    for row in range(sh // 2):
        t = row / max(1, sh // 2)
        c = (
            int(95 + 40 * t),
            int(145 + 35 * t),
            int(195 + 25 * t),
        )
        draw.line([(x0, y0 + row), (x1, y0 + row)], fill=c)

    cx = (x0 + x1) // 2
    horizon = y0 + int(sh * 0.38)
    vanish = (cx, horizon)

    # distant open-world ring (rock/mist)
    for i in range(12):
        t = i / 11
        z = 0.15 + t * 0.55
        half_w = int(sw * 0.12 * (1 - z) + sw * 0.42 * z)
        y = horizon + int((sh * 0.52 - horizon) * z)
        color = rock.getpixel((30 + i * 3, 30))
        draw.rectangle([cx - half_w, y, cx + half_w, y + int(8 + 14 * z)], fill=color)

    # terrain grid in perspective (17x17 implied — draw 9x9 sample)
    grid_n = 15
    for gz in range(grid_n):
        for gx in range(grid_n):
            u = (gx - grid_n / 2) / (grid_n / 2)
            v = (gz - grid_n / 2) / (grid_n / 2)
            dist = math.sqrt(u * u + v * v)
            if dist > 1.05:
                continue
            depth = 1 - dist * 0.85
            sx = cx + int(u * sw * 0.38 * depth)
            sy = horizon + int(v * sh * 0.42 * depth) + int(sh * 0.08 * (1 - depth))
            tile_w = int(28 * depth)
            tile_h = int(18 * depth)
            if tile_w < 4:
                continue
            cheb = max(abs(gx - grid_n // 2), abs(gz - grid_n // 2))
            tex = rock if cheb > 5 else grass if cheb > 2 else grass
            quad = [
                (sx - tile_w, sy),
                (sx + tile_w, sy),
                (sx + tile_w - 4, sy + tile_h),
                (sx - tile_w + 4, sy + tile_h),
            ]
            project_quad(img, draw, quad, tex)

    # play disk maze highlight (center)
    play_cx, play_cy = cx, horizon + int(sh * 0.22)
    pw, ph = int(sw * 0.14), int(sh * 0.08)
    for row in range(3):
        for col in range(3):
            tx = play_cx - pw + col * (pw // 3)
            ty = play_cy - ph // 2 + row * (ph // 3)
            fill = (42, 88, 52) if row >= 1 else (58, 105, 62)
            draw.rectangle([tx, ty, tx + pw // 3 - 2, ty + ph // 3 - 2], fill=fill)
            if row >= 1:
                draw.rectangle([tx + 3, ty + 3, tx + pw // 3 - 5, ty + ph // 3 - 5], fill=(32, 72, 42))

    # hollow titan
    tx = cx + int(sw * 0.08)
    ty = horizon - int(sh * 0.05)
    draw.rectangle([tx - 12, ty - 90, tx + 12, ty], fill=(48, 40, 36))

    # trees from atlas
    if TREE_ATLAS.exists():
        atlas = Image.open(TREE_ATLAS).convert("RGBA")
        for u, v, sc in [(-0.35, 0.15, 0.5), (0.3, 0.12, 0.55), (-0.1, 0.2, 0.65)]:
            depth = 0.7
            sx = cx + int(u * sw * 0.35 * depth)
            sy = horizon + int(v * sh * 0.25 * depth) + int(sh * 0.1)
            patch = atlas.crop((30, 10, 150, 200)).resize((int(50 * sc), int(85 * sc)))
            img.paste(patch, (sx - patch.width // 2, sy - patch.height), patch)


def draw_scene_gizmo(draw, x1, y1):
    ox, oy = x1 - 55, y1 - 55
    draw.line([(ox, oy), (ox + 28, oy)], fill=(180, 60, 60), width=2)
    draw.line([(ox, oy), (ox, oy - 28)], fill=(90, 180, 90), width=2)
    draw.line([(ox, oy), (ox - 18, oy + 18)], fill=(80, 120, 200), width=2)
    draw.text((ox + 30, oy - 4), "X", fill=(200, 90, 90), font=ImageFont.load_default())
    draw.text((ox - 4, oy - 32), "Y", fill=(90, 200, 90), font=ImageFont.load_default())


def render_unity_editor(grass, rock) -> Image.Image:
    w, h = 1920, 1080
    img = Image.new("RGB", (w, h), U_BG)
    draw = ImageDraw.Draw(img)
    font, font_b, font_sm, title = fonts()

    # macOS title strip
    draw.rectangle([0, 0, w, 28], fill=(50, 50, 50))
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        draw.ellipse([14 + i * 22, 10, 26 + i * 22, 22], fill=c)
    draw.text((w // 2, 14), "Hub — Unity 6 — EnvironmentAuthoringKit", fill=U_DIM, anchor="mm", font=font)

    # tool bar
    draw.rectangle([0, 28, w, 54], fill=U_PANEL)
    draw.text((12, 36), "Unity 6", fill=U_TEXT, font=font_b)
    for i, t in enumerate(["Hand", "Move", "Rotate", "Scale", "Rect"]):
        draw.rectangle([90 + i * 52, 32, 136 + i * 52, 50], fill=U_TAB_OFF if i else U_TAB_ON)
        draw.text((98 + i * 52, 35), t, fill=U_DIM, font=font_sm)

    hier_w = 260
    insp_w = 310
    y_top = 54
    scene = [hier_w, y_top + 22, w - insp_w, h - 22]
    draw_tab_bar(draw, hier_w, y_top, w - hier_w - insp_w, ["Scene", "Game", "Asset Store"], 0, font_sm)
    draw_hierarchy(draw, [0, y_top, hier_w, h - 22], font_sm, font_b, "SurfaceTerrainMain")
    draw_inspector(draw, [w - insp_w, y_top, w, h - 22], font_sm, font_b)

    sx0, sy0, sx1, sy1 = scene
    draw.rectangle(scene, fill=(25, 32, 38))
    draw_scene_terrain_perspective(img, draw, scene, grass, rock)
    draw = ImageDraw.Draw(img)
    draw_scene_gizmo(draw, sx1, sy1)

    # scene overlay labels (Unity style)
    draw.rectangle([sx0, sy0, sx0 + 220, sy0 + 24], fill=(0, 0, 0, 0))
    draw.text((sx0 + 8, sy0 + 6), "Scene", fill=U_TEXT, font=font_b)
    draw.text(
        (sx0 + 8, sy1 - 22),
        "Persp | Full AAA complete — ~289 Terrain tiles (17×17)",
        fill=U_DIM,
        font=font_sm,
    )

    # status / progress (like build running)
    draw.rectangle([hier_w, h - 22, w, h], fill=U_PANEL2)
    draw.text(
        (hier_w + 10, h - 18),
        "[Surface] terraform open world 245/289 (SurfaceOpenWorldTile_6_4) — Environment Kit",
        fill=U_ACCENT,
        font=font_sm,
    )

    return img


def render_unity_game_view(grass, rock, tree_atlas) -> Image.Image:
    w, h = 1920, 1080
    img = Image.new("RGB", (w, h), U_BG)
    draw = ImageDraw.Draw(img)
    font, font_b, font_sm, _ = fonts()

    draw.rectangle([0, 0, w, 28], fill=(50, 50, 50))
    draw.text((w // 2, 14), "Hub — Play Mode", fill=U_DIM, anchor="mm", font=font)
    draw.rectangle([0, 28, w, 50], fill=U_PANEL)
    draw_tab_bar(draw, 0, 28, w, ["Game", "Scene"], 0, font_sm)

    game = [0, 50, w, h - 22]
    gx0, gy0, gx1, gy1 = game
    draw.rectangle(game, fill=(20, 28, 34))

    # first-person style ground + mountains
    gw, gh = gx1 - gx0, gy1 - gy0
    grass_big = grass.resize((256, 256))
    for row in range(gy0 + gh // 2, gy1):
        t = (row - (gy0 + gh // 2)) / (gh // 2)
        band = grass_big.resize((gw, 4))
        img.paste(band, (gx0, row))

    # sky
    for row in range(gy0, gy0 + gh // 2):
        t = row / max(1, gh // 2)
        c = (int(100 + 50 * t), int(155 + 40 * t), int(210 + 20 * t))
        draw.line([(gx0, row), (gx1, row)], fill=c)

    cx = (gx0 + gx1) // 2
    horizon = gy0 + int(gh * 0.42)
    rock_strip = rock.resize((gw, 100))
    img.paste(rock_strip, (gx0, horizon - 30))

    # trail path
    draw.polygon(
        [
            (cx - 80, gy1 - 20),
            (cx + 80, gy1 - 20),
            (cx + 40, horizon + 60),
            (cx - 40, horizon + 60),
        ],
        fill=(82, 108, 68),
    )

    if tree_atlas:
        atlas = tree_atlas.convert("RGBA")
        for u, sc in [(-0.4, 0.9), (-0.15, 1.1), (0.2, 1.0), (0.45, 0.85)]:
            sx = cx + int(u * gw * 0.35)
            patch = atlas.crop((25, 5, 170, 210)).resize((int(100 * sc), int(170 * sc)))
            img.paste(patch, (sx - patch.width // 2, horizon + 20), patch)

    # boss tree
    draw.rectangle([cx - 35, horizon - 200, cx + 35, horizon + 40], fill=(45, 38, 34))
    draw.ellipse([cx - 45, horizon - 230, cx + 45, horizon - 170], fill=(35, 30, 28))

    draw = ImageDraw.Draw(img)
    draw.text((gx0 + 12, gy0 + 8), "Game", fill=U_TEXT, font=font_b)
    draw.text((gx0 + 12, gy0 + 26), "Display 1 · 1920×1080 · Scale 1x", fill=U_DIM, font=font_sm)

    draw.rectangle([0, h - 22, w, h], fill=U_PANEL2)
    draw.text((10, h - 18), "Play Mode | FPS 60 | Vegetation + trails active", fill=U_DIM, font=font_sm)

    return img


def main() -> None:
    grass = load_tex(GRASS_PSD)
    rock = load_tex(ROCK_DIFF)
    tree = Image.open(TREE_ATLAS) if TREE_ATLAS.exists() else None

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    editor = render_unity_editor(grass, rock)
    play = render_unity_game_view(grass, rock, tree)
    p1 = OUT_DIR / "expect-unity-editor-scene-final.png"
    p2 = OUT_DIR / "expect-unity-game-view-final.png"
    editor.save(p1, "PNG")
    play.save(p2, "PNG")
    print(p1)
    print(p2)


if __name__ == "__main__":
    main()
