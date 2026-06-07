"""Portfolio intro/outro: concept frame, text panel, portrait + signature stack."""
from __future__ import annotations

import re
from datetime import datetime
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

W, H = 1280, 720
FONT = "/System/Library/Fonts/Supplemental/Arial.ttf"
FONT_BOLD = "/System/Library/Fonts/Supplemental/Arial Bold.ttf"


def load_font(size: int, *, bold: bool = False) -> ImageFont.FreeTypeFont:
    path = FONT_BOLD if bold else FONT
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        return ImageFont.load_default()


def wrap(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.FreeTypeFont, max_w: int) -> list[str]:
    words = text.split()
    lines: list[str] = []
    cur: list[str] = []
    for w in words:
        trial = " ".join(cur + [w])
        if draw.textlength(trial, font=font) <= max_w:
            cur.append(w)
        else:
            if cur:
                lines.append(" ".join(cur))
            cur = [w]
    if cur:
        lines.append(" ".join(cur))
    return lines or [text]


def capture_display_time(run_dir: Path) -> str:
    m = re.match(r"(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})(\d{2})", run_dir.name)
    if m:
        y, mo, d, hh, mm, ss = m.groups()
        try:
            dt = datetime(int(y), int(mo), int(d), int(hh), int(mm), int(ss))
            return dt.strftime("%B %d, %Y · %I:%M %p").replace(" 0", " ")
        except ValueError:
            pass
    try:
        return datetime.fromtimestamp(run_dir.stat().st_mtime).strftime("%B %d, %Y · %I:%M %p").replace(
            " 0", " "
        )
    except OSError:
        return datetime.now().strftime("%B %d, %Y · %I:%M %p").replace(" 0", " ")


def ensure_creator_assets(run_dir: Path, card_spec: dict[str, Any] | None = None) -> tuple[Path | None, Path | None]:
    """Portrait + signature from id-source.png when present."""
    ext = Path(__file__).resolve().parent / "extract-id-signature.py"
    import importlib.util

    cfg = card_spec or {}
    mod_spec = importlib.util.spec_from_file_location("extract_id_sig", ext)
    mod = importlib.util.module_from_spec(mod_spec)
    assert mod_spec.loader
    mod_spec.loader.exec_module(mod)
    return mod.ensure_creator_assets(
        run_dir,
        mimic_signature=bool(cfg.get("mimicSignature", False)),
        signature_name=str(cfg.get("signatureName", "Jacob Adkins")),
    )


def resolve_portrait_path(run_dir: Path, spec: dict[str, Any]) -> Path | None:
    raw = spec.get("portraitImage") or spec.get("portraitPath")
    if raw:
        p = Path(str(raw)).expanduser()
        if p.is_file():
            return p
    for name in (
        "_approval_portrait.png",
        "DemoRecapPortrait.png",
        "portrait.png",
        "assets/DemoRecapPortrait.png",
    ):
        p = run_dir / name
        if p.is_file():
            return p
    return None


def resolve_signature_path(run_dir: Path, spec: dict[str, Any]) -> Path | None:
    raw = spec.get("signatureImage") or spec.get("signaturePath")
    if raw:
        p = Path(str(raw)).expanduser()
        if p.is_file():
            return p
    for name in ("DemoRecapSignature.png", "signature.png", "assets/DemoRecapSignature.png"):
        p = run_dir / name
        if p.is_file():
            return p
    return None


def pick_concept_frame_paths(
    frames: list[Path],
    milestones: list[dict] | None,
) -> tuple[Path, Path]:
    n = len(frames)
    if n < 2:
        return frames[0], frames[0]

    cps = [m for m in (milestones or []) if m.get("beatKind", "checkpoint") == "checkpoint"]
    if not cps:
        cps = list(milestones or [])

    if cps:
        intro_i = min(max(0, int(cps[min(2, len(cps) - 1)].get("frame", 0))), n - 1)
        outro_i = min(max(0, int(cps[-1].get("frame", n - 1))), n - 1)
        if intro_i == outro_i and len(cps) > 1:
            intro_i = min(max(0, int(cps[0].get("frame", 0))), n - 1)
    else:
        intro_i = min(n - 1, max(0, round(n * 0.18)))
        outro_i = min(n - 1, max(0, round(n * 0.88)))

    return frames[intro_i], frames[outro_i]


def _load_scene_background(path: Path) -> Image.Image:
    import importlib.util

    ve_path = Path(__file__).resolve().parent / "demo-recap-visual-enhance.py"
    ve_spec = importlib.util.spec_from_file_location("demo_recap_visual_enhance", ve_path)
    ve = importlib.util.module_from_spec(ve_spec)
    assert ve_spec.loader
    ve_spec.loader.exec_module(ve)

    raw = Image.open(path).convert("RGB")
    scene = ve.true_color_grade(raw)
    scene = ImageEnhance.Brightness(scene).enhance(0.68)
    scene = scene.filter(ImageFilter.GaussianBlur(radius=0.8))
    sw, sh = scene.size
    target_ar = W / H
    src_ar = sw / sh
    if src_ar > target_ar:
        nw = int(sh * target_ar)
        left = (sw - nw) // 2
        scene = scene.crop((left, 0, left + nw, sh))
    else:
        nh = int(sw / target_ar)
        top = (sh - nh) // 2
        scene = scene.crop((0, top, sw, top + nh))
    return scene.resize((W, H), Image.Resampling.LANCZOS)


def _rounded_photo(portrait: Image.Image, size: tuple[int, int], accent: tuple[int, int, int]) -> Image.Image:
    tw, th = size
    src = portrait.convert("RGB")
    src_ar = src.width / max(src.height, 1)
    dst_ar = tw / max(th, 1)
    if src_ar > dst_ar:
        nh = th
        nw = int(src_ar * nh)
    else:
        nw = tw
        nh = int(nw / src_ar)
    src = src.resize((nw, nh), Image.Resampling.LANCZOS)
    left = max(0, (nw - tw) // 2)
    top = max(0, (nh - th) // 2)
    src = src.crop((left, top, left + tw, top + th))

    mask = Image.new("L", (tw, th), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([0, 0, tw, th], radius=14, fill=255)
    framed = Image.new("RGBA", (tw + 6, th + 6), (0, 0, 0, 0))
    border = Image.new("RGBA", (tw + 6, th + 6), (*accent, 255))
    framed.paste(border, (0, 0))
    inner = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    inner.paste(src, (0, 0))
    inner.putalpha(mask)
    framed.paste(inner, (3, 3), inner)
    return framed


def _fit_signature(sig: Image.Image, target_w: int, max_h: int) -> Image.Image:
    sig = sig.convert("RGBA")
    ratio = min(target_w / max(sig.width, 1), max_h / max(sig.height, 1), 1.6)
    if ratio < 0.99 or ratio > 1.01:
        sig = sig.resize((max(1, int(sig.width * ratio)), max(1, int(sig.height * ratio))), Image.Resampling.LANCZOS)
    return sig


def _paste_creator_stack(
    base: Image.Image,
    *,
    portrait_path: Path | None,
    signature_path: Path | None,
    column: tuple[int, int, int, int],
    accent: tuple[int, int, int],
    portrait_height: int,
    signature_max_h: int,
) -> Image.Image:
    if not portrait_path or not portrait_path.is_file():
        return base

    x0, y0, x1, y1 = column
    col_w = x1 - x0
    img = base.convert("RGBA")
    portrait = Image.open(portrait_path).convert("RGB")
    ph = min(portrait_height, y1 - y0 - signature_max_h - 48)
    pw = min(col_w - 24, int(ph * 0.82))
    photo = _rounded_photo(portrait, (pw, ph), accent)
    px = x0 + (col_w - photo.width) // 2
    py = y0 + 12
    img.paste(photo, (px, py), photo)

    if signature_path and signature_path.is_file():
        sig = _fit_signature(Image.open(signature_path), int(pw * 0.92), signature_max_h)
        sx = x0 + (col_w - sig.width) // 2
        sy = py + photo.height + 14
        img.paste(sig, (sx, sy), sig)

    return img.convert("RGB")


def render_portfolio_title_card(
    background: Path,
    *,
    title: str,
    subtitle: str,
    process_note: str,
    footer_tagline: str,
    accent: tuple[int, int, int],
    badge: str | None = None,
    portrait_path: Path | None = None,
    signature_path: Path | None = None,
    portrait_height: int = 228,
    signature_max_h: int = 58,
    panel_position: str = "center",
) -> Image.Image:
    scene = _load_scene_background(background)
    img = Image.new("RGB", (W, H))
    img.paste(scene)

    dim = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(dim).rectangle([0, 0, W, H], fill=(0, 0, 0, 100))
    img = Image.alpha_composite(img.convert("RGBA"), dim).convert("RGB")

    creator_col = (W - 372, 64, W - 52, H - 64)
    text_panel = (52, 96, W - 400, H - 96) if panel_position != "lower" else (52, 72, W - 400, H - 220)

    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for box in (text_panel, creator_col):
        draw.rounded_rectangle(list(box), radius=18, fill=(8, 12, 22, 215))
    img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")

    img = _paste_creator_stack(
        img,
        portrait_path=portrait_path,
        signature_path=signature_path,
        column=creator_col,
        accent=accent,
        portrait_height=portrait_height,
        signature_max_h=signature_max_h,
    )

    draw = ImageDraw.Draw(img)
    x0, y0, x1, y1 = text_panel
    pad = 32
    y = y0 + pad

    if badge:
        bf = load_font(14, bold=True)
        label = badge.upper()
        bw = draw.textlength(label, font=bf) + 22
        draw.rounded_rectangle(
            [x0 + pad, y, x0 + pad + bw, y + 26],
            radius=8,
            fill=(accent[0] // 4, accent[1] // 4, accent[2] // 4),
            outline=accent,
            width=2,
        )
        draw.text((x0 + pad + 11, y + 4), label, fill=accent, font=bf)
        y += 36

    tf = load_font(42, bold=True)
    for line in wrap(draw, title, tf, x1 - x0 - pad * 2):
        draw.text((x0 + pad, y), line, fill=(255, 255, 255), font=tf)
        y += 48

    y += 6
    sf = load_font(20)
    for line in wrap(draw, subtitle, sf, x1 - x0 - pad * 2):
        draw.text((x0 + pad, y), line, fill=(210, 218, 230), font=sf)
        y += 26

    y += 10
    df = load_font(15)
    for line in wrap(draw, process_note, df, x1 - x0 - pad * 2):
        draw.text((x0 + pad, y), line, fill=(165, 178, 198), font=df)
        y += 20

    tag_f = load_font(22, bold=True)
    draw.text((x0 + pad, y1 - pad - 28), footer_tagline, fill=accent, font=tag_f)

    return img


def _process_note(spec: dict[str, Any], primary: str, legacy: str, default: str) -> str:
    return str(spec.get(primary) or spec.get(legacy) or default).strip()


def _creator_assets(run_dir: Path, spec: dict[str, Any]) -> tuple[Path | None, Path | None]:
    if spec.get("ensureCreatorAssets", True):
        ensure_creator_assets(run_dir, spec)
    return resolve_portrait_path(run_dir, spec), resolve_signature_path(run_dir, spec)


def build_intro_card(
    run_dir: Path,
    frames: list[Path],
    spec: dict[str, Any],
    milestones: list[dict] | None,
) -> Image.Image:
    intro_bg, _ = pick_concept_frame_paths(frames, milestones)
    no_portrait = bool(spec.get("introNoPortrait", True))
    portrait, sig = (None, None) if no_portrait else _creator_assets(run_dir, spec)
    accent = tuple(spec.get("introAccent", [24, 235, 158]))
    if no_portrait:
        import importlib.util

        em_path = Path(__file__).resolve().parent / "demo-recap-emerald-variants.py"
        em_spec = importlib.util.spec_from_file_location("emerald", em_path)
        em = importlib.util.module_from_spec(em_spec)
        assert em_spec.loader
        em_spec.loader.exec_module(em)
        copy = next(v for v in em.INTRO_VARIANTS if v["id"] == "c")
        return em.build_intro(intro_bg, copy, em.EMERALD_STYLE)
    return render_portfolio_title_card(
        intro_bg,
        title=spec.get("introTitle", "Unity World Build Recap"),
        subtitle=spec.get(
            "introSubtitle",
            "Procedural terrain timelapse · automated AI captions & vision annotations",
        ),
        process_note=_process_note(
            spec,
            "introProcessNote",
            "introDisclosure",
            "Fully automated: timelapse captures → Cursor AI narration & callouts → ffmpeg assembly. "
            "No manual NLE timeline.",
        ),
        footer_tagline=spec.get("introFooter", "Built in Unity · cut by code"),
        accent=accent,
        badge=spec.get("introBadge", "Automated · AI-integrated"),
        portrait_path=portrait,
        signature_path=sig,
        portrait_height=int(spec.get("introPortraitHeight", 210)),
        signature_max_h=int(spec.get("introSignatureMaxHeight", 52)),
        panel_position="center",
    )


def build_outro_card(
    run_dir: Path,
    frames: list[Path],
    spec: dict[str, Any],
    milestones: list[dict] | None,
) -> Image.Image:
    _, outro_bg = pick_concept_frame_paths(frames, milestones)
    portrait, sig = _creator_assets(run_dir, spec)
    return render_portfolio_title_card(
        outro_bg,
        title=spec.get("outroTitle", "Recap complete · automated assembly"),
        subtitle=spec.get(
            "outroSubtitle",
            "AI-integrated world-build cinema — portfolio piece",
        ),
        process_note=_process_note(
            spec,
            "outroProcessNote",
            "outroDisclosure",
            "Capture in Unity, then automation handles pacing, captions, annotations, and export — "
            "no hand-cut edit required.",
        ),
        footer_tagline=spec.get("outroFooter", "Until the next build"),
        accent=tuple(spec.get("outroAccent", [90, 180, 140])),
        badge=spec.get("outroBadge", "Environment Kit"),
        portrait_path=portrait,
        signature_path=sig,
        portrait_height=int(spec.get("outroPortraitHeight", 290)),
        signature_max_h=int(spec.get("outroSignatureMaxHeight", 70)),
        panel_position="lower",
    )
