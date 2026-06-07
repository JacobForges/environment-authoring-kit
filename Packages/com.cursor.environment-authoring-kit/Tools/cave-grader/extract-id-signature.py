#!/usr/bin/env python3
"""Headshot-only crop + signature ink from ID card for recap title cards."""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageEnhance, ImageFilter, ImageOps, ImageStat


def find_id_card_box(img: Image.Image) -> tuple[int, int, int, int]:
    w, h = img.size
    gray = img.convert("L")
    best_x, best_score = int(w * 0.58), 0
    step = max(2, w // 120)
    for x in range(int(w * 0.38), w - int(w * 0.08), step):
        strip = gray.crop((x, int(h * 0.08), min(x + w // 10, w), int(h * 0.82)))
        m = ImageStat.Stat(strip).mean[0]
        if m > best_score:
            best_score = m
            best_x = x
    left = max(0, best_x - int(w * 0.05))
    right = min(w, best_x + int(w * 0.36))
    return left, int(h * 0.05), right, int(h * 0.92)


def _is_green_map(r: int, g: int, b: int) -> bool:
    lum = (r + g + b) / 3
    return g > r + 16 and g > b + 8 and 70 < lum < 210


def _is_card_white(r: int, g: int, b: int) -> bool:
    lum = (r + g + b) / 3
    chroma = max(abs(r - g), abs(r - b), abs(g - b))
    return lum > 238 and chroma < 18


def _is_flesh_edge(r: int, g: int, b: int) -> bool:
    lum = (r + g + b) / 3
    return r > g + 4 and r > b + 2 and 95 < lum < 235


def _is_ink(r: int, g: int, b: int) -> bool:
    lum = (r + g + b) / 3
    if lum > 120:
        return False
    if b > r + 8 and b > g - 5:
        return True
    return lum < 85 and (r + g + b) < 240


def _is_signature_ink(r: int, g: int, b: int) -> bool:
    """Blurry blue pen on white card — looser than body ink."""
    lum = (r + g + b) / 3
    if lum > 198 or _is_green_map(r, g, b):
        return False
    if _is_flesh_edge(r, g, b) and lum > 105:
        return False
    if b >= r - 22 and lum < 178:
        return True
    return lum < 145


def find_headshot_rect(card: Image.Image) -> tuple[int, int, int, int]:
    """Axis-aligned DMV photo window — face only, no signature or map."""
    cw, ch = card.size
    # Anchor to standard FL ID photo placement, then trim finger/map edges.
    x0, y0 = int(cw * 0.081), int(ch * 0.141)
    x1, y1 = int(cw * 0.420), int(ch * 0.373)
    pw, ph = x1 - x0, y1 - y0
    cpx = card.load()

    for row in range(y1 - 1, max(y0 + int(ph * 0.5), y1 - int(ph * 0.25)), -1):
        ink = sum(1 for x in range(x0, x1) if _is_ink(*cpx[x, row]))
        if ink > max(4, pw // 24):
            y1 = row - 4
            break

    # Only trim the outer edge (holder finger), not the face inside the photo.
    for x in range(x0, min(x0 + int(pw * 0.18), x1)):
        flesh = sum(
            1
            for y in range(y0, y1)
            if _is_flesh_edge(*cpx[x, y]) and not _is_card_white(*cpx[x, y])
        )
        if flesh > (y1 - y0) * 0.38:
            x0 = x + 1
        else:
            break

    for x in range(x1 - 1, max(x0, x1 - int(pw * 0.5)), -1):
        green = sum(1 for y in range(y0, y1) if _is_green_map(*cpx[x, y]))
        if green > (y1 - y0) * 0.16:
            x1 = x
        else:
            break

    return x0, y0, x1, y1


def trim_photo_margins(portrait: Image.Image) -> Image.Image:
    """Remove white card margins inside the photo frame."""
    g = portrait.convert("L")
    inner = g.point(lambda p: 255 if p < 248 else 0)
    bbox = inner.getbbox()
    if bbox:
        portrait = portrait.crop(bbox)
    return portrait


def extract_portrait_from_card(card: Image.Image) -> Image.Image:
    box = find_headshot_rect(card)
    portrait = card.crop(box).convert("RGB")
    portrait = trim_photo_margins(portrait)

    scale = max(4.0, 520 / max(portrait.width, portrait.height))
    portrait = portrait.resize(
        (int(portrait.width * scale), int(portrait.height * scale)),
        Image.Resampling.LANCZOS,
    )
    portrait = ImageEnhance.Sharpness(portrait).enhance(2.2)
    portrait = ImageEnhance.Contrast(portrait).enhance(1.12)
    portrait = portrait.filter(ImageFilter.UnsharpMask(radius=1.8, percent=200, threshold=2))
    return portrait.convert("RGB")


def find_signature_rect(card: Image.Image, photo_box: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    x0, _y0, x1, _y1 = photo_box
    cw, ch = card.size
    # Signature ink only (right of holder finger under photo).
    return (
        max(0, int(cw * 0.17)),
        int(ch * 0.377),
        min(cw, int(cw * 0.424)),
        min(ch, int(ch * 0.485)),
    )


def _trim_sig_flesh_columns(band: Image.Image) -> Image.Image:
    """Drop holder finger on the left of the signature strip."""
    w, h = band.size
    px = band.load()
    start = 0
    for x in range(min(int(w * 0.35), w)):
        ink = flesh = 0
        for y in range(h):
            r, g, b = px[x, y]
            lum = (r + g + b) / 3
            if lum < 168:
                ink += 1
            if _is_flesh_edge(r, g, b) and lum > 95:
                flesh += 1
        if ink > max(8, h // 20):
            start = max(0, x - 1)
            break
        if flesh > h * 0.5 and ink < 2:
            start = x + 1
    if start > 0:
        band = band.crop((start, 0, w, h))
    return band


def _is_orange_flesh(r: int, g: int, b: int) -> bool:
    return r > g + 8 and r > b + 12 and g > b - 25


def preserve_signature_from_card(band: Image.Image) -> Image.Image:
    """Your real ID ink — original stroke color/weight, transparent card white."""
    scale = 16
    src = band.resize((band.width * scale, band.height * scale), Image.Resampling.LANCZOS)
    src = ImageEnhance.Contrast(src).enhance(1.65)
    src = ImageEnhance.Sharpness(src).enhance(1.8)
    w, h = src.size
    px = src.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    opx = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            lum = (r + g + b) / 3
            if lum > 218:
                continue
            if _is_green_map(r, g, b) or _is_orange_flesh(r, g, b):
                continue
            # Blurry ID pen: keep all non-white, non-flesh dark pixels as-is.
            if lum > 155:
                k = (lum - 155) / 70
                r = int(r * (1 - 0.45 * k))
                g = int(g * (1 - 0.45 * k))
                b = int(b * (1 - 0.45 * k))
            opx[x, y] = (r, g, b, 255)
    bbox = out.getbbox()
    if not bbox or (bbox[2] - bbox[0]) < 50:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0))
    out = out.crop(bbox)
    target_w = 480
    ratio = target_w / max(out.width, 1)
    out = out.resize(
        (int(out.width * ratio), int(out.height * ratio)),
        Image.Resampling.LANCZOS,
    )
    pad = 12
    canvas = Image.new("RGBA", (out.width + pad * 2, out.height + pad * 2), (0, 0, 0, 0))
    canvas.paste(out, (pad, pad), out)
    return canvas


def raster_signature_from_card(band: Image.Image) -> Image.Image:
    """Keep your real pen strokes; drop white card + flesh edge."""
    scale = 10
    src = band.resize((band.width * scale, band.height * scale), Image.Resampling.LANCZOS)
    src = ImageEnhance.Contrast(src).enhance(1.6)
    w, h = src.size
    px = src.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    opx = out.load()
    ink = (14, 38, 82, 255)
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            lum = (r + g + b) / 3
            if lum > 228 or _is_green_map(r, g, b):
                continue
            if _is_flesh_edge(r, g, b) and lum > 90:
                continue
            if _is_signature_ink(r, g, b):
                opx[x, y] = ink
    bbox = out.getbbox()
    if not bbox or (bbox[2] - bbox[0]) < 60 or (bbox[3] - bbox[1]) < 8:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0))
    out = out.crop(bbox)
    ratio = 420 / max(out.width, 1)
    out = out.resize((int(out.width * ratio), int(out.height * ratio)), Image.Resampling.LANCZOS)
    pad = 12
    canvas = Image.new("RGBA", (out.width + pad * 2, out.height + pad * 2), (0, 0, 0, 0))
    canvas.paste(out, (pad, pad), out)
    return canvas


def clean_signature_ink(band: Image.Image) -> Image.Image:
    """Isolate blue/black signature strokes; transparent background."""
    scale = 10
    band = band.resize((band.width * scale, band.height * scale), Image.Resampling.LANCZOS)
    band = ImageEnhance.Contrast(band).enhance(1.5)
    w, h = band.size
    px = band.load()
    mask = Image.new("L", (w, h), 0)
    mp = mask.load()
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            if _is_signature_ink(r, g, b):
                mp[x, y] = 255
    mask = mask.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(3))
    if ImageStat.Stat(mask).mean[0] < 1.5:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0))

    ink_rgb = (14, 38, 82, 255)
    sig = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sig.paste(Image.new("RGBA", (w, h), ink_rgb), (0, 0), mask)
    bbox = sig.getbbox()
    if not bbox or (bbox[2] - bbox[0]) < 40:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0))

    sig = sig.crop(bbox)
    target_w = 420
    ratio = target_w / max(sig.width, 1)
    sig = sig.resize(
        (int(sig.width * ratio), int(sig.height * ratio)),
        Image.Resampling.LANCZOS,
    )
    sig = sig.filter(ImageFilter.UnsharpMask(radius=1.2, percent=140, threshold=2))
    pad = 14
    out = Image.new("RGBA", (sig.width + pad * 2, sig.height + pad * 2), (0, 0, 0, 0))
    out.paste(sig, (pad, pad), sig)
    return out


def extract_signature_from_card(card: Image.Image, photo_box: tuple[int, int, int, int]) -> Image.Image | None:
    band = card.crop(find_signature_rect(card, photo_box))
    sig = preserve_signature_from_card(band)
    if sig.width < 40:
        sig = raster_signature_from_card(band)
    if sig.width < 40:
        sig = clean_signature_ink(band)
    if sig.width < 40:
        return None
    if sig.height > sig.width * 0.55:
        return None
    return sig


def ensure_creator_assets(
    run_dir: Path,
    source: Path | None = None,
    *,
    mimic_signature: bool = False,
    signature_name: str = "Jacob Adkins",
) -> tuple[Path | None, Path | None]:
    src = source
    if src is None:
        for name in ("id-source.png", "id-source.jpg", "id-photo.jpg"):
            p = run_dir / name
            if p.is_file():
                src = p
                break
    if src is None or not src.is_file():
        p = run_dir / "DemoRecapPortrait.png"
        s = run_dir / "DemoRecapSignature.png"
        return (p if p.is_file() else None, s if s.is_file() else None)

    img = ImageOps.exif_transpose(Image.open(src).convert("RGB"))
    card = img.crop(find_id_card_box(img))
    card.save(run_dir / "id-card-crop.png")

    photo_box = find_headshot_rect(card)
    card.crop(photo_box).save(run_dir / "id-photo-only-crop.png")

    portrait = extract_portrait_from_card(card)
    portrait_path = run_dir / "DemoRecapPortrait.png"
    portrait.save(portrait_path, quality=95)

    sig_path = run_dir / "DemoRecapSignature.png"
    extracted = extract_signature_from_card(card, photo_box)
    sig_rect = find_signature_rect(card, photo_box)
    card.crop(sig_rect).save(run_dir / "id-signature-raw-crop.png")

    if mimic_signature:
        from PIL import ImageDraw, ImageFont

        def _font(size: int):
            for path in (
                "/System/Library/Fonts/Supplemental/Brush Script.ttf",
                "/System/Library/Fonts/Supplemental/SnellRoundhand.ttf",
            ):
                if Path(path).is_file():
                    try:
                        return ImageFont.truetype(path, size)
                    except OSError:
                        pass
            return ImageFont.load_default()

        tmp = Image.new("RGBA", (900, 200), (0, 0, 0, 0))
        draw = ImageDraw.Draw(tmp)
        draw.text((20, 50), signature_name, fill=(14, 38, 82, 255), font=_font(58))
        bbox = tmp.getbbox()
        if bbox:
            tmp.crop(bbox).save(sig_path)
    elif extracted is not None:
        extracted.save(sig_path)
        extracted.save(run_dir / "DemoRecapSignature-from-card.png")
    else:
        # Last resort: high-contrast signature band from card (your actual strokes).
        raw = card.crop(sig_rect).convert("RGBA")
        raw = ImageEnhance.Contrast(raw).enhance(1.8)
        raw.save(sig_path)

    return portrait_path, sig_path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("source", type=Path, nargs="?")
    ap.add_argument("-o", "--out-dir", type=Path, required=True)
    ap.add_argument(
        "--mimic-font",
        action="store_true",
        help="Use script font instead of ink extracted from the card",
    )
    ap.add_argument("--name", default="Jacob Adkins")
    args = ap.parse_args()
    out_dir = args.out_dir.expanduser()
    src = args.source.expanduser() if args.source else None
    portrait_path, sig_path = ensure_creator_assets(
        out_dir,
        src,
        mimic_signature=args.mimic_font,
        signature_name=args.name,
    )
    if not portrait_path:
        print("no source", file=sys.stderr)
        return 1
    print(portrait_path)
    print(sig_path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
