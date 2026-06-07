#!/usr/bin/env python3
"""3 emerald intro + 3 outro approval variants — gold signature only on portrait."""
from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw

_cr = Path(__file__).resolve().parent / "demo-recap-creative-variants.py"
_spec = importlib.util.spec_from_file_location("creative", _cr)
creative = importlib.util.module_from_spec(_spec)
assert _spec.loader
_spec.loader.exec_module(creative)

W, H = creative.W, creative.H
scene_base = creative.scene_base
apply_fx = creative.apply_fx
paste_portrait_box = creative.paste_portrait_box
load_font = creative.load_font
draw_paragraph = creative.draw_paragraph
pick_concept_frame_paths = creative.pick_concept_frame_paths


def _fit_signature_ink(sig: Image.Image, target_w: int, max_h: int) -> Image.Image:
    sig = sig.convert("RGBA")
    if sig.width > target_w:
        ratio = target_w / sig.width
        sig = sig.resize(
            (max(1, int(sig.width * ratio)), max(1, int(sig.height * ratio))),
            Image.Resampling.LANCZOS,
        )
    if sig.height > max_h:
        ratio = max_h / sig.height
        sig = sig.resize(
            (max(1, int(sig.width * ratio)), max(1, int(sig.height * ratio))),
            Image.Resampling.LANCZOS,
        )
    return sig


def portrait_with_signature(
    portrait: Image.Image,
    signature: Image.Image | None,
    *,
    signature_max_h: int = 72,
    gap: int = 10,
) -> Image.Image:
    """Stack pen signature under headshot for outro portrait column."""
    if signature is None:
        return portrait.convert("RGB")
    head = portrait.convert("RGBA")
    sig = _fit_signature_ink(signature, int(head.width * 0.92), signature_max_h)
    canvas = Image.new("RGBA", (head.width, head.height + gap + sig.height), (0, 0, 0, 0))
    canvas.paste(head, (0, 0), head)
    sx = (head.width - sig.width) // 2
    canvas.paste(sig, (sx, head.height + gap), sig)
    return canvas.convert("RGB")

INTRO_TITLE = "World Build Recap"

# Emerald UI — portrait file keeps its own gold autograph untouched.
EMERALD_STYLE: dict[str, Any] = {
    "id": "emerald",
    "name": "Emerald Documentary",
    "accent": (24, 235, 158),
    "fx": "warm",
}

INTRO_VARIANTS = [
    {
        "id": "a",
        "label": "Pipeline explainer",
        "body": (
            "This video is a timelapse of a full Unity world build. Nothing here was trimmed by hand in "
            "an NLE. After capture, an automated pipeline runs: Cursor AI writes milestone captions, "
            "vision models place callouts on the frame, and ffmpeg assembles pacing and export. "
            "You are seeing the end result of that chain — record once, narrate and annotate with AI, "
            "then render. I stay out of the timeline."
        ),
        "kicker": "Hands-off edit by design, not a weekend in Premiere.",
    },
    {
        "id": "b",
        "label": "What you will see",
        "body": (
            "Here is how to watch this recap. The camera stays in Scene view while terrain, mountains, "
            "caves, and systems land in order. When a milestone matters, the video holds, captions explain "
            "what changed, and annotations highlight regions on screen. All of that text and markup was "
            "generated through integrated AI tooling — I did not type every line or draw every box manually."
        ),
        "kicker": "Education-first: the automation is part of the demo.",
    },
    {
        "id": "c",
        "label": "Why automation",
        "body": (
            "I am building Environment Kit toward portfolio-grade cinema without babysitting a cut. "
            "Unity records PNG timelapse frames on an interval. Later, scripts and agents turn those "
            "frames into a lecture-style recap: dense captions, staggered reveals, vision overlays, "
            "and a single ffmpeg output. No manual timeline means I can iterate on worlds, not on "
            "clicking razor blades."
        ),
        "kicker": "If that workflow interests you, this is the proof-of-concept.",
    },
]

OUTRO_VARIANTS = [
    {
        "id": "a",
        "label": "Grateful close",
        "thanks": "Thank you for watching.",
        "body": (
            "I appreciate you staying through the full arc. My goal was to show both the world "
            "and the automated edit path that documents it."
        ),
        "catch": "Keep an eye out for the next video — more worlds and a smarter pipeline each time.",
        "sign": "— Jacob Adkins",
    },
    {
        "id": "b",
        "label": "What is next",
        "thanks": "Thank you for watching.",
        "body": (
            "If anything felt unclear, the captions and on-screen notes were AI-assisted — "
            "I will keep tightening that narration on future builds."
        ),
        "catch": "Keep an eye out for the next video. I will post the next timelapse when the build is ready.",
        "sign": "— Jacob",
    },
    {
        "id": "c",
        "label": "Community tone",
        "thanks": "Thank you for watching.",
        "body": (
            "I hope this helped you see how far hands-off recap can go: real Unity frames, "
            "real milestones, and a finished MP4 without opening a traditional editor."
        ),
        "catch": "Keep an eye out for the next video — subscribe or follow if you want the next drop.",
        "sign": "— Jacob Adkins",
    },
]


def _glow_text(draw: ImageDraw.ImageDraw, xy: tuple[int, int], text: str, font, fill: tuple[int, int, int]) -> None:
    g = (fill[0] // 4, fill[1] // 4, fill[2] // 4)
    for dx, dy in [(-2, 0), (2, 0), (0, -2), (0, 2), (-1, -1), (1, 1)]:
        draw.text((xy[0] + dx, xy[1] + dy), text, fill=g, font=font)
    draw.text(xy, text, fill=fill, font=font)


def build_intro(bg: Path, copy: dict[str, Any], style: dict[str, Any]) -> Image.Image:
    accent = style["accent"]
    img = scene_base(bg, dim=115)
    panel = (72, 88, W - 72, H - 88)
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    d.rounded_rectangle(list(panel), radius=20, fill=(6, 14, 12, 232), outline=(*accent, 230), width=3)
    img = Image.alpha_composite(img.convert("RGBA"), ov).convert("RGB")

    draw = ImageDraw.Draw(img)
    x0, y0, x1, y1 = panel
    pad = 38
    y = y0 + pad
    bf = load_font(15, bold=True)
    badge = "AUTOMATED · AI · NO MANUAL NLE"
    bw = draw.textlength(badge, font=bf) + 24
    draw.rounded_rectangle(
        [x0 + pad, y, x0 + pad + bw, y + 28],
        radius=8,
        fill=(accent[0] // 8, accent[1] // 8, accent[2] // 8),
        outline=accent,
        width=2,
    )
    draw.text((x0 + pad + 12, y + 5), badge, fill=accent, font=bf)
    y += 40

    tf = load_font(52, bold=True)
    for line in creative.wrap(draw, INTRO_TITLE, tf, x1 - x0 - pad * 2):
        _glow_text(draw, (x0 + pad, y), line, tf, (255, 255, 255))
        y += 56

    y += 12
    body_f = load_font(28)
    y = draw_paragraph(draw, x0 + pad, y, copy["body"], body_f, (215, 235, 225), x1 - x0 - pad * 2, 10)

    y += 10
    kf = load_font(24, bold=True)
    for line in creative.wrap(draw, copy["kicker"], kf, x1 - x0 - pad * 2):
        _glow_text(draw, (x0 + pad, y), line, kf, accent)
        y += 30

    return apply_fx(img, style["fx"], accent)


def build_outro(
    bg: Path,
    portrait: Image.Image,
    copy: dict[str, Any],
    style: dict[str, Any],
    *,
    signature: Image.Image | None = None,
    signature_max_h: int = 72,
) -> Image.Image:
    accent = style["accent"]
    img = scene_base(bg, dim=130)
    photo_box = (W - 500, 48, W - 28, H - 48)
    text_box = (40, 72, photo_box[0] - 20, H - 72)
    outro_portrait = portrait_with_signature(
        portrait, signature, signature_max_h=signature_max_h
    )

    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    d.rounded_rectangle(list(text_box), radius=16, fill=(6, 14, 12, 225), outline=(*accent, 200), width=2)
    d.rounded_rectangle(list(photo_box), radius=18, fill=(10, 16, 14, 240), outline=(80, 100, 90), width=2)
    img = Image.alpha_composite(img.convert("RGBA"), ov).convert("RGB")
    img = paste_portrait_box(img, outro_portrait, photo_box, radius=16, border=None, shadow=True)

    draw = ImageDraw.Draw(img)
    tx0, ty0, tx1, ty1 = text_box
    pad = 30
    y = ty0 + pad
    tf = load_font(48, bold=True)
    for line in creative.wrap(draw, copy["thanks"], tf, tx1 - tx0 - pad * 2):
        _glow_text(draw, (tx0 + pad, y), line, tf, (255, 255, 255))
        y += 54

    y += 10
    y = draw_paragraph(
        draw, tx0 + pad, y, copy["body"], load_font(28), (205, 228, 218), tx1 - tx0 - pad * 2, 10
    )
    y += 12
    cf = load_font(30, bold=True)
    for line in creative.wrap(draw, copy["catch"], cf, tx1 - tx0 - pad * 2):
        _glow_text(draw, (tx0 + pad, y), line, cf, accent)
        y += 34

    if signature is None and copy.get("sign"):
        draw.text(
            (tx0 + pad, ty1 - pad - 24),
            copy["sign"],
            fill=(170, 190, 180),
            font=load_font(20),
        )
    return apply_fx(img, style["fx"], accent)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    args = ap.parse_args()
    run_dir = args.run_dir.expanduser().resolve()
    frames = sorted((run_dir / "timelapse").glob("tl_*.png"))
    intro_bg, outro_bg = pick_concept_frame_paths(frames, None)
    portrait = Image.open(run_dir / "_approval_portrait.png")

    rows = [
        "<!DOCTYPE html><html><head><meta charset=utf-8><title>Emerald picks</title>",
        "<style>body{font-family:system-ui;background:#0a1210;color:#e8fff4;padding:20px;max-width:1200px;margin:auto}",
        "h1{color:#18eb9e}h2{border-bottom:1px solid #1a4a3a;padding-bottom:.3rem}",
        ".grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}.grid3{display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px}",
        "img{width:100%;border-radius:10px;border:2px solid #18eb9e33}",
        "code{background:#122820;padding:2px 8px;border-radius:4px;color:#7dffc8}</style></head><body>",
        "<h1>Pick one intro + one outro (emerald UI, gold signature on photo only)</h1>",
        "<p>Reply: <code>intro a outro b</code></p><h2>Intros</h2><div class=grid3>",
    ]

    for iv in INTRO_VARIANTS:
        img = build_intro(intro_bg, iv, EMERALD_STYLE)
        p = run_dir / f"_approval_intro_{iv['id']}.png"
        img.save(p, quality=95)
        rows.append(f"<div><h3>{iv['label']} ({iv['id']})</h3><img src='{p.name}'></div>")

    rows.append("</div><h2>Outros</h2><div class=grid3>")
    for ov in OUTRO_VARIANTS:
        img = build_outro(outro_bg, portrait, ov, EMERALD_STYLE)
        p = run_dir / f"_approval_outro_{ov['id']}.png"
        img.save(p, quality=95)
        rows.append(f"<div><h3>{ov['label']} ({ov['id']})</h3><img src='{p.name}'></div>")

    rows.append("</div></body></html>")
    (run_dir / "_approval_emerald_pick.html").write_text("".join(rows), encoding="utf-8")
    print("Wrote 3 intro + 3 outro emerald variants")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
