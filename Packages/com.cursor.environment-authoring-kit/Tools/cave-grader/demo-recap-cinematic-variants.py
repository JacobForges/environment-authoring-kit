#!/usr/bin/env python3
"""Generate 4 cinematic intro + 4 outro card variants for user approval."""
from __future__ import annotations

import argparse
import json
import random
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

import importlib.util

_tc_path = Path(__file__).resolve().parent / "demo-recap-title-cards.py"
_spec = importlib.util.spec_from_file_location("demo_recap_title_cards", _tc_path)
_tc = importlib.util.module_from_spec(_spec)
assert _spec.loader
_spec.loader.exec_module(_tc)

W, H = _tc.W, _tc.H
pick_concept_frame_paths = _tc.pick_concept_frame_paths
_load_scene_background = _tc._load_scene_background
_rounded_photo = _tc._rounded_photo
wrap = _tc.wrap

FONT_COOL = "/System/Library/Fonts/SFNSDisplay.ttf"
FONT_COOL_BOLD = "/System/Library/Fonts/SFNSDisplay-Bold.otf"
FONT_ALT = "/System/Library/Fonts/Supplemental/Avenir Next.ttc"
FONT_ALT_BOLD = "/System/Library/Fonts/Supplemental/Avenir Next Bold.ttf"


def load_font(size: int, *, bold: bool = False) -> ImageFont.FreeTypeFont:
    for path in (FONT_COOL_BOLD if bold else FONT_COOL, FONT_ALT_BOLD if bold else FONT_ALT):
        if Path(path).is_file():
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


VARIANTS = [
    {
        "id": "v1",
        "name": "Cinematic Noir",
        "accent": (72, 168, 255),
        "grade": "cool",
        "letterbox": 0.08,
        "grain": 18,
        "vignette": 0.55,
        "glow": True,
    },
    {
        "id": "v2",
        "name": "Gold Hour Bloom",
        "accent": (255, 196, 110),
        "grade": "warm",
        "letterbox": 0.1,
        "grain": 12,
        "vignette": 0.45,
        "glow": True,
    },
    {
        "id": "v3",
        "name": "Neon Editorial",
        "accent": (160, 120, 255),
        "grade": "neon",
        "letterbox": 0.06,
        "grain": 22,
        "vignette": 0.5,
        "glow": True,
    },
    {
        "id": "v4",
        "name": "Anamorphic Scope",
        "accent": (90, 220, 180),
        "grade": "teal",
        "letterbox": 0.14,
        "grain": 14,
        "vignette": 0.62,
        "glow": False,
    },
]


def _film_grain(img: Image.Image, strength: int) -> Image.Image:
    rng = random.Random(42)
    px = img.load()
    w, h = img.size
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            n = rng.randint(-strength, strength)
            r, g, b = px[x, y]
            px[x, y] = (
                max(0, min(255, r + n)),
                max(0, min(255, g + n)),
                max(0, min(255, b + n)),
            )
    return img


def _vignette(img: Image.Image, strength: float) -> Image.Image:
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    w, h = img.size
    for i in range(24):
        a = int(255 * strength * (i / 24) ** 1.6)
        margin_x = int(w * 0.02 * i)
        margin_y = int(h * 0.02 * i)
        draw.rectangle(
            [margin_x, margin_y, w - margin_x, h - margin_y],
            outline=(0, 0, 0, a),
            width=max(2, (w + h) // 180),
        )
    return Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")


def _letterbox(img: Image.Image, ratio: float) -> Image.Image:
    bar = int(H * ratio)
    out = img.copy()
    draw = ImageDraw.Draw(out)
    draw.rectangle([0, 0, W, bar], fill=(0, 0, 0))
    draw.rectangle([0, H - bar, W, H], fill=(0, 0, 0))
    return out


def _color_grade(img: Image.Image, mode: str) -> Image.Image:
    if mode == "warm":
        r, g, b = img.split()
        r = ImageEnhance.Brightness(r).enhance(1.06)
        b = ImageEnhance.Brightness(b).enhance(0.88)
        return Image.merge("RGB", (r, g, b))
    if mode == "cool":
        return ImageEnhance.Color(img).enhance(1.12)
    if mode == "neon":
        return ImageEnhance.Color(ImageEnhance.Contrast(img).enhance(1.08)).enhance(1.25)
    if mode == "teal":
        px = img.load()
        w, h = img.size
        for y in range(h):
            for x in range(w):
                r, g, b = px[x, y]
                px[x, y] = (
                    max(0, min(255, int(r * 0.85))),
                    max(0, min(255, int(g * 1.02))),
                    max(0, min(255, int(b * 1.05))),
                )
        return img
    return img


def _light_leak(img: Image.Image, accent: tuple[int, int, int]) -> Image.Image:
    leak = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(leak)
    d.ellipse([W - 420, -80, W + 120, 280], fill=(*accent, 38))
    d.ellipse([-100, H - 220, 280, H + 80], fill=(accent[2], accent[0], accent[1], 28))
    leak = leak.filter(ImageFilter.GaussianBlur(28))
    return Image.alpha_composite(img.convert("RGBA"), leak).convert("RGB")


def _draw_glow_text(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int],
    text: str,
    font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int],
    glow: bool,
) -> None:
    if glow:
        glow_fill = (fill[0] // 3, fill[1] // 3, fill[2] // 3)
        for dx, dy in [(-3, 0), (3, 0), (0, -3), (0, 3), (-2, -2), (2, 2)]:
            draw.text((xy[0] + dx, xy[1] + dy), text, fill=glow_fill, font=font)
    draw.text(xy, text, fill=fill, font=font)


def apply_cinematic_fx(img: Image.Image, v: dict[str, Any]) -> Image.Image:
    img = _color_grade(img, v["grade"])
    img = _light_leak(img, v["accent"])
    img = _vignette(img, v["vignette"])
    img = _letterbox(img, v["letterbox"])
    img = _film_grain(img, v["grain"])
    return img


def _text_panel(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], accent: tuple[int, int, int]) -> None:
    draw.rounded_rectangle(list(box), radius=20, fill=(6, 10, 18, 228), outline=(*accent, 180), width=2)


def build_intro_variant(
    bg_path: Path,
    v: dict[str, Any],
    *,
    title: str,
    automation_line: str,
) -> Image.Image:
    scene = _load_scene_background(bg_path)
    img = Image.new("RGB", (W, H))
    img.paste(scene)
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 120))
    img = Image.alpha_composite(img.convert("RGBA"), dim).convert("RGB")

    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    panel = (80, 120, W - 80, H - 120)
    _text_panel(draw, panel, v["accent"])
    img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")

    draw = ImageDraw.Draw(img)
    x0, y0, x1, y1 = panel
    pad = 40
    y = y0 + pad
    accent = v["accent"]

    bf = load_font(16, bold=True)
    badge = "AUTOMATED · AI EDIT"
    bw = draw.textlength(badge, font=bf) + 24
    draw.rounded_rectangle(
        [x0 + pad, y, x0 + pad + bw, y + 30],
        radius=10,
        fill=(accent[0] // 5, accent[1] // 5, accent[2] // 5),
        outline=accent,
        width=2,
    )
    draw.text((x0 + pad + 12, y + 6), badge, fill=accent, font=bf)
    y += 44

    tf = load_font(56, bold=True)
    for line in wrap(draw, title, tf, x1 - x0 - pad * 2):
        _draw_glow_text(draw, (x0 + pad, y), line, tf, (255, 255, 255), v["glow"])
        y += 62

    y += 16
    af = load_font(34, bold=True)
    for line in wrap(draw, automation_line, af, x1 - x0 - pad * 2):
        _draw_glow_text(draw, (x0 + pad, y), line, af, accent, v["glow"])
        y += 42

    y += 8
    sf = load_font(22)
    sub = "No manual timeline. No hands-on NLE — capture, AI, export."
    for line in wrap(draw, sub, sf, x1 - x0 - pad * 2):
        draw.text((x0 + pad, y), line, fill=(200, 210, 225), font=sf)
        y += 28

    return apply_cinematic_fx(img, v)


def build_outro_variant(
    bg_path: Path,
    portrait_path: Path,
    v: dict[str, Any],
) -> Image.Image:
    scene = _load_scene_background(bg_path)
    img = Image.new("RGB", (W, H))
    img.paste(scene)
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 130))
    img = Image.alpha_composite(img.convert("RGBA"), dim).convert("RGB")

    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    text_panel = (56, 96, W - 420, H - 96)
    portrait_col = (W - 392, 72, W - 48, H - 72)
    _text_panel(draw, text_panel, v["accent"])
    draw.rounded_rectangle(list(portrait_col), radius=20, fill=(8, 12, 22, 235), outline=(*v["accent"], 200), width=2)
    img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")

    # Signed portrait (signature baked in)
    portrait = Image.open(portrait_path).convert("RGB")
    col_x0, col_y0, col_x1, col_y1 = portrait_col
    col_w, col_h = col_x1 - col_x0, col_y1 - col_y0
    ph = col_h - 28
    pw = col_w - 28
    photo = _rounded_photo(portrait, (pw, ph), v["accent"])
    px = col_x0 + (col_w - photo.width) // 2
    py = col_y0 + (col_h - photo.height) // 2
    img = img.convert("RGBA")
    img.paste(photo, (px, py), photo)
    img = img.convert("RGB")

    draw = ImageDraw.Draw(img)
    x0, y0, x1, y1 = text_panel
    pad = 36
    y = y0 + pad
    accent = v["accent"]

    tf = load_font(50, bold=True)
    for line in wrap(draw, "Thank you for watching", tf, x1 - x0 - pad * 2):
        _draw_glow_text(draw, (x0 + pad, y), line, tf, (255, 255, 255), v["glow"])
        y += 56

    y += 12
    cf = load_font(30, bold=True)
    catch = "Keep an eye out for the next video"
    for line in wrap(draw, catch, cf, x1 - x0 - pad * 2):
        _draw_glow_text(draw, (x0 + pad, y), line, cf, accent, v["glow"])
        y += 38

    y += 20
    sf = load_font(18)
    draw.text((x0 + pad, y), "— Jacob", fill=(175, 185, 200), font=sf)

    return apply_cinematic_fx(img, v)


def generate_variants(run_dir: Path, spec: dict[str, Any]) -> Path:
    out_dir = run_dir / "card-variants"
    out_dir.mkdir(parents=True, exist_ok=True)

    frames = sorted((run_dir / "timelapse").glob("tl_*.png"))
    if not frames:
        raise SystemExit(f"No timelapse frames in {run_dir / 'timelapse'}")

    intro_bg, outro_bg = pick_concept_frame_paths(frames, spec.get("milestones"))
    portrait = run_dir / "_approval_portrait.png"
    if not portrait.is_file():
        portrait = run_dir / "_approval_portrait_signed.png"
    if not portrait.is_file():
        raise SystemExit("Missing _approval_portrait.png — approve portrait first")

    title = spec.get("introTitle", "Unity World Build Recap")
    automation = (
        "Fully automated: AI captions, vision callouts & ffmpeg assembly. "
        "No manual editing required."
    )

    manifest: list[dict[str, str]] = []
    for v in VARIANTS:
        intro = build_intro_variant(intro_bg, v, title=title, automation_line=automation)
        outro = build_outro_variant(outro_bg, portrait, v)
        intro_path = out_dir / f"intro_{v['id']}.png"
        outro_path = out_dir / f"outro_{v['id']}.png"
        intro.save(intro_path, quality=95)
        outro.save(outro_path, quality=95)
        manifest.append(
            {
                "id": v["id"],
                "name": v["name"],
                "intro": str(intro_path),
                "outro": str(outro_path),
            }
        )

    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    html = [
        "<!DOCTYPE html><html><head><meta charset=utf-8><title>Card variants</title>",
        "<style>body{font-family:system-ui;background:#111;color:#eee;padding:24px}",
        "h2{margin-top:32px} .grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}",
        "img{width:100%;border-radius:12px;border:2px solid #333}",
        ".pick{color:#8cf}</style></head><body>",
        "<h1>Pick one intro + one outro</h1>",
        "<p class=pick>Tell the agent: <code>intro v2 outro v3</code> (example)</p>",
    ]
    for m in manifest:
        html.append(f"<h2>{m['name']} ({m['id']})</h2><div class=grid>")
        html.append(f"<div><h3>Intro</h3><img src='intro_{m['id']}.png'></div>")
        html.append(f"<div><h3>Outro</h3><img src='outro_{m['id']}.png'></div></div>")
    html.append("</body></html>")
    (out_dir / "index.html").write_text("".join(html), encoding="utf-8")
    return out_dir


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    args = ap.parse_args()
    run_dir = args.run_dir.expanduser().resolve()
    spec: dict[str, Any] = {}
    tl = run_dir / "DemoRecapTimeline.json"
    if tl.is_file():
        spec = json.loads(tl.read_text(encoding="utf-8"))
    out = generate_variants(run_dir, spec)
    print(out)
    for p in sorted(out.glob("intro_*.png")):
        print(p)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
