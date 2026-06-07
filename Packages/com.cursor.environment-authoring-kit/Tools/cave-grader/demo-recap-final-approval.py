#!/usr/bin/env python3
"""Single intro + outro approval pair from chosen style (default: Gold Documentary 02)."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

_cr = Path(__file__).resolve().parent / "demo-recap-creative-variants.py"
_spec = importlib.util.spec_from_file_location("creative", _cr)
creative = importlib.util.module_from_spec(_spec)
assert _spec.loader
_spec.loader.exec_module(creative)

W, H = creative.W, creative.H
scene_base = creative.scene_base
apply_fx = creative.apply_fx
fit_full_portrait = creative.fit_full_portrait
paste_portrait_box = creative.paste_portrait_box
wrap = creative.wrap
load_font = creative.load_font
draw_paragraph = creative.draw_paragraph
pick_concept_frame_paths = creative.pick_concept_frame_paths

STYLE_02 = next(s for s in creative.STYLES if s["id"] == "02")

INTRO_TITLE = "World Build Recap"  # unchanged main title

INTRO_BODY = (
    "Hi. I am glad you pressed play. I recorded this entire Unity world build as a timelapse, "
    "and the recap you are about to watch was assembled by an automated pipeline with AI-generated "
    "captions, vision callouts, and ffmpeg export. I did not sit in Premiere or another NLE and cut "
    "this timeline by hand. Milestone pacing, on-screen annotations, and the final render were handled "
    "by integrated tooling so the edit stays repeatable and hands-off. If you care about how far "
    "automation can carry a portfolio piece, this is the workflow I am pushing."
)

INTRO_KICKER = (
    "Transparency first: you are watching machine-assisted cinema, not a secret manual edit."
)

OUTRO = {
    "thanks": "Thank you for watching.",
    "body": (
        "I hope this gave you a clear picture of how the world came together, and how much of the "
        "edit can run on its own without me babysitting a timeline."
    ),
    "catch": "Keep an eye out for the next video. I will keep refining the pipeline and the worlds it captures.",
    "sign": "— Jacob Adkins",
}


def build_intro(bg: Path, style: dict[str, Any]) -> Image.Image:
    accent = style["accent"]
    img = scene_base(bg, dim=115)
    panel = (72, 100, W - 72, H - 100)
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    d.rounded_rectangle(list(panel), radius=20, fill=(8, 12, 22, 228), outline=(*accent, 210), width=2)
    img = Image.alpha_composite(img.convert("RGBA"), ov).convert("RGB")

    draw = ImageDraw.Draw(img)
    x0, y0, x1, y1 = panel
    pad = 40
    y = y0 + pad
    bf = load_font(15, bold=True)
    badge = "AUTOMATED · AI EDIT"
    bw = draw.textlength(badge, font=bf) + 24
    draw.rounded_rectangle(
        [x0 + pad, y, x0 + pad + bw, y + 28],
        radius=8,
        fill=(accent[0] // 5, accent[1] // 5, accent[2] // 5),
        outline=accent,
        width=2,
    )
    draw.text((x0 + pad + 12, y + 5), badge, fill=accent, font=bf)
    y += 40

    tf = load_font(52, bold=True)
    y = draw_paragraph(draw, x0 + pad, y, INTRO_TITLE, tf, (255, 255, 255), x1 - x0 - pad * 2, 12)

    y += 14
    body_f = load_font(28)
    y = draw_paragraph(draw, x0 + pad, y, INTRO_BODY, body_f, (210, 218, 230), x1 - x0 - pad * 2, 10)

    y += 12
    kf = load_font(24, bold=True)
    draw_paragraph(draw, x0 + pad, y, INTRO_KICKER, kf, accent, x1 - x0 - pad * 2, 8)

    return apply_fx(img, style["fx"], accent)


def build_outro(bg: Path, portrait: Image.Image, style: dict[str, Any]) -> Image.Image:
    accent = style["accent"]
    img = scene_base(bg, dim=130)
    photo_box = (W - 500, 48, W - 28, H - 48)
    text_box = (40, 72, photo_box[0] - 20, H - 72)

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
    y = draw_paragraph(
        draw, tx0 + pad, y, OUTRO["thanks"], load_font(48, bold=True), (255, 255, 255), tx1 - tx0 - pad * 2, 10
    )
    y += 12
    y = draw_paragraph(
        draw, tx0 + pad, y, OUTRO["body"], load_font(28), (205, 215, 228), tx1 - tx0 - pad * 2, 10
    )
    y += 14
    y = draw_paragraph(
        draw, tx0 + pad, y, OUTRO["catch"], load_font(30, bold=True), accent, tx1 - tx0 - pad * 2, 10
    )
    draw.text((tx0 + pad, ty1 - pad - 24), OUTRO["sign"], fill=(170, 180, 195), font=load_font(20))

    return apply_fx(img, style["fx"], accent)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--style", default="02", help="Variant id from card-variants (default 02 Gold Documentary)")
    args = ap.parse_args()
    run_dir = args.run_dir.expanduser().resolve()
    style = next((s for s in creative.STYLES if s["id"] == args.style), STYLE_02)

    frames = sorted((run_dir / "timelapse").glob("tl_*.png"))
    intro_bg, outro_bg = pick_concept_frame_paths(frames, None)
    portrait = Image.open(run_dir / "_approval_portrait.png")

    intro = build_intro(intro_bg, style)
    outro = build_outro(outro_bg, portrait, style)

    intro.save(run_dir / "_approval_intro_final.png", quality=95)
    outro.save(run_dir / "_approval_outro_final.png", quality=95)

    html = f"""<!DOCTYPE html><html><head><meta charset=utf-8><title>Final approval</title>
<style>body{{font-family:system-ui;background:#111;color:#eee;padding:24px}}
img{{width:100%;max-width:960px;border-radius:12px;border:1px solid #444;margin:12px 0}}</style>
</head><body><h1>Approve intro + outro ({style['name']})</h1>
<p>Reply: <code>apply final cards</code> or request tweaks.</p>
<h2>Intro (no portrait, 28pt body)</h2><img src='_approval_intro_final.png'>
<h2>Outro (full portrait, 28pt context)</h2><img src='_approval_outro_final.png'>
</body></html>"""
    (run_dir / "_approval_final.html").write_text(html, encoding="utf-8")
    print(run_dir / "_approval_intro_final.png")
    print(run_dir / "_approval_outro_final.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
