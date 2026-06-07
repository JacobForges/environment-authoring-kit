#!/usr/bin/env python3
"""20 creative intro + 20 outro variants — full portrait, conversational copy."""
from __future__ import annotations

import argparse
import json
import math
import random
from pathlib import Path
from typing import Any, Callable

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
wrap = _tc.wrap

FONTS = [
    "/System/Library/Fonts/SFNSDisplay.ttf",
    "/System/Library/Fonts/SFNSDisplay-Bold.otf",
    "/System/Library/Fonts/Supplemental/Avenir Next.ttc",
    "/System/Library/Fonts/Supplemental/Avenir Next Bold.ttf",
    "/System/Library/Fonts/Supplemental/Futura.ttc",
    "/System/Library/Fonts/NewYork.ttf",
]


def load_font(size: int, *, bold: bool = False, idx: int = 0) -> ImageFont.FreeTypeFont:
    paths = [FONTS[1], FONTS[3], FONTS[5], FONTS[0]] if bold else [FONTS[0], FONTS[2], FONTS[4], FONTS[6 % len(FONTS)]]
    for path in ([paths[idx % len(paths)]] + paths):
        if Path(path).is_file():
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


def fit_full_portrait(portrait: Image.Image, max_w: int, max_h: int) -> Image.Image:
    """Entire portrait visible — letterbox inside box, never center-crop."""
    p = portrait.convert("RGBA")
    scale = min(max_w / p.width, max_h / p.height)
    nw, nh = max(1, int(p.width * scale)), max(1, int(p.height * scale))
    p = p.resize((nw, nh), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (max_w, max_h), (0, 0, 0, 0))
    canvas.paste(p, ((max_w - nw) // 2, (max_h - nh) // 2), p)
    return canvas


def paste_portrait_box(
    base: Image.Image,
    portrait: Image.Image,
    box: tuple[int, int, int, int],
    *,
    radius: int = 0,
    border: tuple[int, int, int] | None = None,
    shadow: bool = True,
) -> Image.Image:
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0, y1 - y0
    fitted = fit_full_portrait(portrait, bw - 16, bh - 16)
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    px = x0 + (bw - fitted.width) // 2
    py = y0 + (bh - fitted.height) // 2
    if shadow:
        sh = Image.new("RGBA", fitted.size, (0, 0, 0, 160))
        layer.paste(sh, (px + 6, py + 8), sh)
    if border:
        bd = Image.new("RGBA", (fitted.width + 8, fitted.height + 8), (*border, 255))
        fr = Image.new("RGBA", (fitted.width + 8, fitted.height + 8), (0, 0, 0, 0))
        fr.paste(bd, (0, 0))
        if radius > 0:
            m = Image.new("L", fr.size, 0)
            ImageDraw.Draw(m).rounded_rectangle([0, 0, fr.size[0], fr.size[1]], radius=radius + 4, fill=255)
            fr.putalpha(m)
        layer.paste(fr, (px - 4, py - 4), fr)
    if radius > 0:
        mask = Image.new("L", fitted.size, 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, fitted.width, fitted.height], radius=radius, fill=255)
        fitted.putalpha(mask)
    layer.paste(fitted, (px, py), fitted)
    return Image.alpha_composite(base.convert("RGBA"), layer).convert("RGB")


def scene_base(bg: Path, dim: int = 110) -> Image.Image:
    img = Image.new("RGB", (W, H))
    img.paste(_load_scene_background(bg))
    d = Image.new("RGBA", (W, H), (0, 0, 0, dim))
    return Image.alpha_composite(img.convert("RGBA"), d).convert("RGB")


def draw_paragraph(
    draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    text: str,
    font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int],
    max_w: int,
    line_gap: int = 8,
) -> int:
    for line in wrap(draw, text, font, max_w):
        draw.text((x, y), line, fill=fill, font=font)
        y += font.size + line_gap
    return y


# --- Conversational copy (proper grammar) ---
INTRO_COPY = [
    {
        "title": "Welcome to This Build Recap",
        "lead": "Hi. I am glad you are here.",
        "body": "I captured this entire Unity world build on timelapse, and what you are about to watch was assembled by an automated pipeline with AI captions and vision callouts. I did not cut this by hand in an NLE.",
        "kicker": "Sit back — the machine did the tedious edit work.",
    },
    {
        "title": "Before We Begin",
        "lead": "Quick note, because transparency matters.",
        "body": "Every milestone caption and on-screen annotation was generated through Cursor AI integration. FFmpeg handled pacing and export. No manual timeline scrubbing was required on my end.",
        "kicker": "You are watching automation, not a weekend in Premiere.",
    },
    {
        "title": "Environment Kit · Automated Cinema",
        "lead": "Thanks for pressing play.",
        "body": "This recap exists to show how far we can push hands-off storytelling: record in Unity, narrate with AI, annotate with vision, and deliver a portfolio cut without opening a traditional editor.",
        "kicker": "If that sounds interesting, you are in the right place.",
    },
]

OUTRO_COPY = [
    {
        "thanks": "Thank you for watching.",
        "body": "I hope this gave you a clear picture of how the world came together and how much of the edit can run on its own.",
        "catch": "Keep an eye out for the next video — I will keep refining the pipeline and the worlds it captures.",
        "sign": "— Jacob",
    },
    {
        "thanks": "Thank you for staying to the end.",
        "body": "I appreciate your time. This project is as much about the automated workflow as it is about the terrain, caves, and systems you saw on screen.",
        "catch": "Keep an eye out for the next video. There is more coming.",
        "sign": "— Jacob Adkins",
    },
]


STYLES: list[dict[str, Any]] = [
    {"id": "01", "name": "Noir Split", "accent": (90, 175, 255), "fx": "noir"},
    {"id": "02", "name": "Gold Documentary", "accent": (255, 200, 120), "fx": "warm"},
    {"id": "03", "name": "Neon Broadcast", "accent": (180, 100, 255), "fx": "neon"},
    {"id": "04", "name": "Scope Wide", "accent": (80, 220, 190), "fx": "scope"},
    {"id": "05", "name": "VHS Retro", "accent": (255, 120, 180), "fx": "vhs"},
    {"id": "06", "name": "Terminal Hacker", "accent": (80, 255, 140), "fx": "terminal"},
    {"id": "07", "name": "Magazine Spread", "accent": (255, 255, 255), "fx": "clean"},
    {"id": "08", "name": "Polaroid Stack", "accent": (240, 240, 235), "fx": "polaroid"},
    {"id": "09", "name": "Blockbuster Trailer", "accent": (255, 180, 60), "fx": "trailer"},
    {"id": "10", "name": "Aurora Glass", "accent": (120, 220, 255), "fx": "aurora"},
    {"id": "11", "name": "Comic Panel", "accent": (255, 220, 0), "fx": "comic"},
    {"id": "12", "name": "Credits Roll", "accent": (200, 200, 210), "fx": "credits"},
    {"id": "13", "name": "Swiss Minimal", "accent": (255, 80, 80), "fx": "swiss"},
    {"id": "14", "name": "Grunge Poster", "accent": (220, 90, 70), "fx": "grunge"},
    {"id": "15", "name": "Hologram HUD", "accent": (0, 255, 255), "fx": "hud"},
    {"id": "16", "name": "Watercolor Soft", "accent": (180, 140, 255), "fx": "soft"},
    {"id": "17", "name": "News Lower Third", "accent": (200, 40, 40), "fx": "news"},
    {"id": "18", "name": "Synthwave Grid", "accent": (255, 60, 200), "fx": "synth"},
    {"id": "19", "name": "Film Strip", "accent": (255, 255, 255), "fx": "film"},
    {"id": "20", "name": "Imax Center", "accent": (255, 255, 255), "fx": "imax"},
]


def apply_fx(img: Image.Image, fx: str, accent: tuple[int, int, int]) -> Image.Image:
    rng = random.Random(hash(fx) & 0xFFFF)
    if fx == "warm":
        r, g, b = img.split()
        img = Image.merge("RGB", (ImageEnhance.Brightness(r).enhance(1.05), g, ImageEnhance.Brightness(b).enhance(0.9)))
    elif fx == "cool" or fx == "noir":
        img = ImageEnhance.Color(img).enhance(0.85)
        img = ImageEnhance.Contrast(img).enhance(1.1)
    elif fx == "neon" or fx == "synth" or fx == "hud":
        img = ImageEnhance.Color(img).enhance(1.35)
        img = ImageEnhance.Contrast(img).enhance(1.12)
    if fx == "scope" or fx == "imax" or fx == "trailer":
        bar = int(H * (0.12 if fx == "scope" else 0.08))
        d = ImageDraw.Draw(img)
        d.rectangle([0, 0, W, bar], fill=(0, 0, 0))
        d.rectangle([0, H - bar, W, H], fill=(0, 0, 0))
    if fx == "vhs":
        px = img.load()
        for y in range(0, H, 3):
            for x in range(W):
                r, g, b = px[x, y]
                px[x, y] = (min(255, r + 8), g, min(255, b + 6))
    if fx == "terminal":
        overlay = Image.new("RGBA", (W, H), (0, 40, 0, 40))
        img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")
    if fx in ("noir", "scope", "grunge", "vhs", "film"):
        vig = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        vd = ImageDraw.Draw(vig)
        for i in range(20):
            a = int(200 * (i / 20) ** 2)
            m = int(i * 18)
            vd.rectangle([m, m, W - m, H - m], outline=(0, 0, 0, a), width=14)
        img = Image.alpha_composite(img.convert("RGBA"), vig).convert("RGB")
    leak = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ld = ImageDraw.Draw(leak)
    ld.ellipse([W - 300, -60, W + 80, 200], fill=(*accent, 45))
    leak = leak.filter(ImageFilter.GaussianBlur(35))
    img = Image.alpha_composite(img.convert("RGBA"), leak).convert("RGB")
    # grain
    px = img.load()
    for y in range(0, H, 2):
        for x in range(0, W, 2):
            n = rng.randint(-12, 12)
            r, g, b = px[x, y]
            px[x, y] = tuple(max(0, min(255, c + n)) for c in (r, g, b))
    return img


def layout_panels(style: dict, intro: bool) -> tuple[tuple[int, int, int, int], tuple[int, int, int, int]]:
    sid = style["id"]
    if sid in ("01", "06", "13", "17"):
        return ((48, 72, W // 2 + 40, H - 72), (W // 2 + 24, 48, W - 40, H - 48))
    if sid in ("02", "07", "12", "20"):
        return ((56, H // 2 - 20, W - 56, H - 56), (56, 56, W - 56, H // 2 - 40))
    if sid in ("05", "08", "11", "19"):
        return ((W // 2 + 20, 80, W - 48, H - 80), (48, 80, W // 2 - 20, H - 80))
    if sid in ("09", "14", "18"):
        return ((80, 100, W - 480, H - 100), (W - 460, 60, W - 40, H - 60))
    return ((64, 96, W - 500, H - 96), (W - 480, 72, W - 48, H - 72))


def render_intro(bg: Path, portrait: Image.Image, style: dict, copy: dict) -> Image.Image:
    accent = style["accent"]
    img = scene_base(bg, dim=120)
    text_box, photo_box = layout_panels(style, True)
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    d.rounded_rectangle(list(text_box), radius=16, fill=(8, 12, 22, 220), outline=(*accent, 200), width=2)
    if style["fx"] == "polaroid":
        d.rectangle(list(photo_box), fill=(250, 248, 242, 255))
    else:
        d.rounded_rectangle(list(photo_box), radius=14, fill=(12, 16, 28, 230))
    img = Image.alpha_composite(img.convert("RGBA"), ov).convert("RGB")
    img = paste_portrait_box(img, portrait, photo_box, radius=12 if style["fx"] != "polaroid" else 0, border=accent)

    draw = ImageDraw.Draw(img)
    x0, y0, x1, y1 = text_box
    pad = 28
    y = y0 + pad
    y = draw_paragraph(draw, x0 + pad, y, copy["lead"], load_font(20, idx=1), (200, 210, 225), x1 - x0 - pad * 2)
    y += 6
    y = draw_paragraph(draw, x0 + pad, y, copy["title"], load_font(44, bold=True, idx=0), (255, 255, 255), x1 - x0 - pad * 2, 10)
    y += 8
    y = draw_paragraph(draw, x0 + pad, y, copy["body"], load_font(19, idx=2), (210, 218, 230), x1 - x0 - pad * 2, 6)
    y += 10
    draw_paragraph(draw, x0 + pad, y, copy["kicker"], load_font(22, bold=True, idx=0), accent, x1 - x0 - pad * 2)

    if style["fx"] == "comic":
        bd = ImageDraw.Draw(img)
        bd.rounded_rectangle([text_box[0] - 4, text_box[1] - 4, text_box[2] + 4, text_box[3] + 4], radius=8, outline=(0, 0, 0), width=4)
    if style["fx"] == "hud":
        bd = ImageDraw.Draw(img)
        for i in range(0, W, 80):
            bd.line([(i, 0), (i, H)], fill=(0, 255, 255, 30), width=1)
    return apply_fx(img, style["fx"], accent)


def render_outro(bg: Path, portrait: Image.Image, style: dict, copy: dict) -> Image.Image:
    accent = style["accent"]
    img = scene_base(bg, dim=130)
    text_box, photo_box = layout_panels(style, False)
    # Outro: wide portrait column (full figure visible)
    photo_box = (W - 500, 48, W - 28, H - 48)
    text_x1 = max(420, photo_box[0] - 20)
    text_box = (40, 72, text_x1, H - 72)

    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    d.rounded_rectangle(list(text_box), radius=16, fill=(6, 10, 18, 225), outline=(*accent, 180), width=2)
    d.rounded_rectangle(list(photo_box), radius=18, fill=(10, 14, 24, 240), outline=(*accent, 220), width=3)
    img = Image.alpha_composite(img.convert("RGBA"), ov).convert("RGB")
    img = paste_portrait_box(img, portrait, photo_box, radius=16, border=accent, shadow=True)

    draw = ImageDraw.Draw(img)
    tx0, ty0, tx1, ty1 = text_box
    pad = 30
    y = ty0 + pad
    y = draw_paragraph(draw, tx0 + pad, y, copy["thanks"], load_font(46, bold=True, idx=0), (255, 255, 255), tx1 - tx0 - pad * 2, 10)
    y += 12
    y = draw_paragraph(draw, tx0 + pad, y, copy["body"], load_font(20, idx=1), (205, 215, 228), tx1 - tx0 - pad * 2, 8)
    y += 14
    y = draw_paragraph(draw, tx0 + pad, y, copy["catch"], load_font(26, bold=True, idx=2), accent, tx1 - tx0 - pad * 2, 8)
    draw.text((tx0 + pad, ty1 - pad - 24), copy["sign"], fill=(170, 180, 195), font=load_font(18))

    return apply_fx(img, style["fx"], accent)


def generate(run_dir: Path, spec: dict[str, Any]) -> Path:
    out = run_dir / "card-variants"
    out.mkdir(parents=True, exist_ok=True)
    frames = sorted((run_dir / "timelapse").glob("tl_*.png"))
    if not frames:
        raise SystemExit("No timelapse frames")
    intro_bg, outro_bg = pick_concept_frame_paths(frames, spec.get("milestones"))
    portrait_path = run_dir / "_approval_portrait.png"
    if not portrait_path.is_file():
        portrait_path = run_dir / "_approval_portrait_signed.png"
    portrait = Image.open(portrait_path)

    manifest = []
    for i, style in enumerate(STYLES):
        ic = INTRO_COPY[i % len(INTRO_COPY)]
        oc = OUTRO_COPY[i % len(OUTRO_COPY)]
        intro_img = render_intro(intro_bg, portrait, style, ic)
        outro_img = render_outro(outro_bg, portrait, style, oc)
        intro_p = out / f"intro_{style['id']}.png"
        outro_p = out / f"outro_{style['id']}.png"
        intro_img.save(intro_p, quality=94)
        outro_img.save(outro_p, quality=94)
        manifest.append(
            {
                "id": style["id"],
                "name": style["name"],
                "intro": str(intro_p),
                "outro": str(outro_p),
            }
        )

    (out / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    rows = []
    rows.append("<!DOCTYPE html><html><head><meta charset=utf-8><title>20×2 card variants</title>")
    rows.append(
        "<style>body{font-family:system-ui;background:#0a0a0c;color:#eee;padding:20px;max-width:1400px;margin:auto}"
        "h1{font-weight:600}h2{margin:2rem 0 .5rem;border-bottom:1px solid #333;padding-bottom:.25rem}"
        ".pair{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:2rem}"
        "img{width:100%;border-radius:10px;border:1px solid #444}code{background:#222;padding:2px 6px;border-radius:4px}"
        ".tag{color:#9cf;font-size:.9rem}</style></head><body>"
    )
    rows.append("<h1>Pick one intro + one outro</h1>")
    rows.append("<p>Reply: <code>intro 07 outro 12</code> — full portrait, not cropped.</p>")
    for m in manifest:
        rows.append(f"<h2>{m['name']} <span class=tag>({m['id']})</span></h2><div class=pair>")
        rows.append(f"<div><strong>Intro</strong><br><img src='intro_{m['id']}.png'></div>")
        rows.append(f"<div><strong>Outro</strong><br><img src='outro_{m['id']}.png'></div></div>")
    rows.append("</body></html>")
    (out / "index.html").write_text("".join(rows), encoding="utf-8")
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    args = ap.parse_args()
    run_dir = args.run_dir.expanduser().resolve()
    spec: dict[str, Any] = {}
    tl = run_dir / "DemoRecapTimeline.json"
    if tl.is_file():
        spec = json.loads(tl.read_text(encoding="utf-8"))
    out = generate(run_dir, spec)
    print(out)
    print(f"Generated {len(STYLES)} intro + {len(STYLES)} outro variants")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
