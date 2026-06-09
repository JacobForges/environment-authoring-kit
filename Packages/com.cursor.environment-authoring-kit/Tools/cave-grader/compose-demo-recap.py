#!/usr/bin/env python3
"""
Compose a narrated demo recap MP4 from still frames + caption lines.
Used by build-demo-recap-sample.py and Unity CaveBuildDemoAutoRecorder (no drawtext required).
"""
from __future__ import annotations

import json
import math
import os
import subprocess
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont
except ImportError:
    print("Install Pillow: pip3 install --user pillow", file=sys.stderr)
    sys.exit(1)

W, H = 1920, 1080
BAR = 294
HEADER = 63
CAPTION_MIN_PT = 32
CAPTION_KEY_PT = 36
FPS = 24
SCENE_BOTTOM = H - BAR  # scene stays full brightness above caption bar


def configure_canvas(width: int = 1920, height: int = 1080) -> None:
    """Set output canvas (default 1080p). Call before rendering holds."""
    global W, H, BAR, HEADER, SCENE_BOTTOM
    W, H = int(width), int(height)
    scale = height / 1080.0
    BAR = int(294 * scale)
    HEADER = int(63 * scale)
    SCENE_BOTTOM = H - BAR
FONT = "/System/Library/Fonts/Supplemental/Arial.ttf"
FONT_BOLD = "/System/Library/Fonts/Supplemental/Arial Bold.ttf"

_VISUAL_ENHANCE = None


def _visual_enhance_module():
    global _VISUAL_ENHANCE
    if _VISUAL_ENHANCE is not None:
        return _VISUAL_ENHANCE
    import importlib.util

    path = Path(__file__).resolve().parent / "demo-recap-visual-enhance.py"
    spec = importlib.util.spec_from_file_location("demo_recap_visual_enhance", path)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    _VISUAL_ENHANCE = mod
    return mod


def find_ffmpeg() -> str:
    for candidate in ("/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg"):
        if candidate == "ffmpeg" or os.path.isfile(candidate):
            return candidate
    return "ffmpeg"


def has_drawtext(ffmpeg: str) -> bool:
    try:
        out = subprocess.run(
            [ffmpeg, "-filters"],
            capture_output=True,
            text=True,
            check=False,
        )
        return "drawtext" in (out.stdout or "") + (out.stderr or "")
    except OSError:
        return False


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    path = FONT_BOLD if bold else FONT
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        try:
            return ImageFont.truetype(FONT, size)
        except OSError:
            return ImageFont.load_default()


def wrap(draw: ImageDraw.ImageDraw, text: str, font, max_width: int) -> list[str]:
    words = (text or "").split()
    lines: list[str] = []
    current = ""
    for word in words:
        trial = f"{current} {word}".strip()
        if draw.textlength(trial, font=font) <= max_width:
            current = trial
        else:
            if current:
                lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines or ([text] if text else [])


def lerp(a: int, b: int, t: float) -> int:
    return int(a + (b - a) * t)


def sharpen_scene(img: Image.Image) -> Image.Image:
    """Upscale → sharpen → downscale for crisp terrain/detail in the video."""
    try:
        return _visual_enhance_module().enhance_scene_image(img, upscale=2.0)
    except Exception:
        w, h = img.size
        big = img.resize((int(w * 1.35), int(h * 1.35)), Image.Resampling.LANCZOS)
        big = big.filter(ImageFilter.UnsharpMask(radius=1.6, percent=180, threshold=2))
        big = ImageEnhance.Contrast(big).enhance(1.06)
        big = ImageEnhance.Sharpness(big).enhance(1.12)
        return big.resize((w, h), Image.Resampling.LANCZOS)


def apply_vignette(img: Image.Image, strength: float = 0.42) -> Image.Image:
    overlay = Image.new("L", (W, H), 0)
    draw = ImageDraw.Draw(overlay)
    cx, cy = W / 2, H / 2
    max_r = math.hypot(cx, cy)
    for y in range(0, H, 4):
        for x in range(0, W, 4):
            r = math.hypot(x - cx, y - cy) / max_r
            v = int(255 * min(1.0, max(0.0, (r - 0.35) / (1.0 - 0.35) * strength)))
            draw.rectangle([x, y, x + 3, y + 3], fill=v)
    overlay = overlay.filter(ImageFilter.GaussianBlur(radius=28))
    dark = Image.new("RGB", (W, H), (0, 0, 0))
    return Image.composite(dark, img, overlay)


def draw_progress(draw: ImageDraw.ImageDraw, index: int, total: int, accent: tuple[int, int, int]) -> None:
    margin, height, y = 56, 5, 18
    width = W - margin * 2
    draw.rounded_rectangle([margin, y, margin + width, y + height], radius=3, fill=(60, 68, 80))
    if total > 0:
        fill_w = max(8, int(width * (index + 1) / total))
        draw.rounded_rectangle([margin, y, margin + fill_w, y + height], radius=3, fill=accent)


def render_captioned_frame(
    base: Image.Image,
    *,
    line1: str,
    line2: str,
    line3: str | None,
    chapter: str,
    index: int,
    total: int,
    accent: tuple[int, int, int],
    meta: str | None = None,
    milestone: dict | None = None,
    annotate: bool = True,
    annotation_alpha: float = 1.0,
    line1_alpha: float = 1.0,
    line2_alpha: float = 1.0,
    line3_alpha: float = 1.0,
    lecture_mode: bool = True,
    ai_regions_only: bool = True,
    chapter_focus_annotations: bool = False,
    cinematic_t: float | None = None,
    cinematic_mode: str = "auto",
    cinematic_seed: int = 0,
    scene_fit: str = "cover",
    javafx_effects: bool = False,
    frame_index: int = 0,
    total_frames: int = 1,
    javafx_spec: dict | None = None,
    caption_bullets: list[str] | None = None,
    future_hint: str | None = None,
    bullet_alphas: list[float] | None = None,
    hold_time_sec: float | None = None,
) -> Image.Image:
    # Fit scene into upper area — keep it sharp; captions only in bottom bar.
    scene_h = SCENE_BOTTOM - HEADER
    src = base.convert("RGB")
    sw, sh = src.size
    fit = (scene_fit or "cover").lower()
    if fit == "contain":
        scale = min(W / sw, scene_h / sh)
        nw, nh = int(sw * scale), int(sh * scale)
        scene = src.resize((nw, nh), Image.Resampling.LANCZOS)
        canvas = Image.new("RGB", (W, scene_h), (14, 18, 24))
        left = (W - nw) // 2
        top = (scene_h - nh) // 2
        canvas.paste(scene, (left, top))
        scene = canvas
    else:
        scale = max(W / sw, scene_h / sh)
        nw, nh = int(sw * scale), int(sh * scale)
        scene = src.resize((nw, nh), Image.Resampling.LANCZOS)
        left = (nw - W) // 2
        top = max(0, (nh - scene_h) // 2)
        scene = scene.crop((left, top, left + W, top + scene_h))
    # Grade once in ffmpeg on export — avoid stacked sharpen/contrast here.
    if cinematic_t is not None:
        try:
            jfx = javafx_spec or {}
            scene = _visual_enhance_module().apply_cinematic_motion(
                scene,
                cinematic_t,
                mode="static" if jfx.get("staticSceneMotion") else cinematic_mode,
                seed=cinematic_seed,
                disable_pan=bool(jfx.get("disablePanMotion", True)),
            )
        except Exception:
            pass
    scene = apply_vignette(scene, 0.03)

    img = Image.new("RGB", (W, H), (12, 16, 22))
    img.paste(scene, (0, HEADER))

    draw = ImageDraw.Draw(img)
    if annotate and milestone and annotation_alpha > 0.02:
        try:
            ve = _visual_enhance_module()
            regions = ve.regions_from_milestone(
                milestone,
                ai_only=ai_regions_only,
                chapter_focus=chapter_focus_annotations,
            )
            ve.draw_scene_annotations(
                draw, W, scene_h, HEADER, regions, accent, alpha=annotation_alpha
            )
        except Exception:
            pass

    draw = ImageDraw.Draw(img)
    header = Image.new("RGBA", (W, HEADER), (8, 12, 20, 175))
    img.paste(header, (0, 0), header)
    draw = ImageDraw.Draw(img)
    draw.text((24, 8), "JACOB ADKINS'S BOT · BUILD RECAP", fill=(170, 188, 210), font=load_font(14, bold=True))
    sub = "On-screen study guide · voice script is separate" if lecture_mode else "AI-narrated checkpoints"
    draw.text((24, 26), sub, fill=(230, 236, 245), font=load_font(11))

    pill = chapter[:52]
    pw = int(draw.textlength(pill, font=load_font(12))) + 22
    px = W - pw - 20
    frame_t = frame_index / max(1, total_frames - 1)
    pill_scale = 1.0
    if javafx_effects:
        try:
            import demo_recap_javafx_effects as jfx
            pill_scale = jfx.javafx_scale(frame_t, delay=0.05, duration=0.45)
        except Exception:
            pass
    if pill_scale < 0.99:
        cx, cy = px + pw // 2, 21
        pill_layer = Image.new("RGBA", (W, HEADER), (0, 0, 0, 0))
        pdraw = ImageDraw.Draw(pill_layer)
        sw, sh = int(pw * pill_scale), int(26 * pill_scale)
        sx, sy = cx - sw // 2, cy - sh // 2
        pdraw.rounded_rectangle([sx, sy, sx + sw, sy + sh], radius=9, fill=accent)
        pdraw.text((sx + 11, sy + 4), pill, fill=(255, 255, 255), font=load_font(12, bold=True))
        img = Image.alpha_composite(img.convert("RGBA"), pill_layer).convert("RGB")
        draw = ImageDraw.Draw(img)
    else:
        draw.rounded_rectangle([px, 8, px + pw, 34], radius=9, fill=accent)
        draw.text((px + 11, 12), pill, fill=(255, 255, 255), font=load_font(12, bold=True))
    draw_progress(draw, index, total, accent)
    draw.text((W - 88, 6), f"{index + 1:02}/{total:02}", fill=(255, 255, 255), font=load_font(13, bold=True))

    bar_top = H - BAR
    bar = Image.new("RGBA", (W, BAR), (0, 0, 0, 0))
    bdraw = ImageDraw.Draw(bar)
    bdraw.rectangle([0, 0, W, BAR], fill=(4, 8, 14, 230))
    bdraw.rectangle([0, 0, 6, BAR], fill=accent)
    bdraw.rectangle([0, 0, W, 2], fill=accent)
    img.paste(bar, (0, bar_top), bar)
    draw = ImageDraw.Draw(img)

    def _fade(rgb: tuple[int, int, int], a: float) -> tuple[int, int, int]:
        a = max(0.0, min(1.0, a))
        return (int(rgb[0] * a), int(rgb[1] * a), int(rgb[2] * a))

    right_inset = 0
    if javafx_spec and javafx_spec.get("botAvatarOverlay", True):
        try:
            right_inset = int(
                javafx_spec.get("botAvatarCaptionTextInsetRight")
                or (
                    int(javafx_spec.get("botAvatarOverlayScale", 240) or 240)
                    + int(javafx_spec.get("botAvatarOverlayMarginX", 12) or 12)
                    + 16
                )
            )
        except (TypeError, ValueError):
            right_inset = 0
    caption_max_w = max(240, W - 48 - max(0, right_inset))

    x = 24
    y = bar_top + 10
    jfx_mod = None
    if javafx_effects:
        try:
            import importlib.util
            _jfx_path = Path(__file__).resolve().parent / "demo-recap-javafx-effects.py"
            _spec = importlib.util.spec_from_file_location("demo_recap_javafx_effects", _jfx_path)
            jfx_mod = importlib.util.module_from_spec(_spec)
            assert _spec.loader
            _spec.loader.exec_module(jfx_mod)
        except Exception:
            jfx_mod = None

    bullets = list(caption_bullets or [])
    if not bullets and line2:
        bullets = [line2]
    future = (future_hint or line3 or "").strip()

    if line1_alpha > 0.02 and (line1 or not lecture_mode):
        ty = y
        ty = y
        l1_fill = _fade((255, 255, 255), line1_alpha)
        l1_font = load_font(max(CAPTION_MIN_PT, CAPTION_KEY_PT), bold=True)
        l1_text = line1 or "Build checkpoint"
        for l1_line in wrap(draw, l1_text, l1_font, caption_max_w):
            if jfx_mod:
                jfx_mod.javafx_glow_text(draw, (x, ty), l1_line, l1_font, l1_fill, glow_level=0.5)
            else:
                draw.text((x, ty), l1_line, fill=l1_fill, font=l1_font)
            ty += CAPTION_MIN_PT + 4
        y = ty + 2

    if meta and not lecture_mode:
        for mline in wrap(draw, meta, load_font(11), caption_max_w):
            draw.text((x, y), mline, fill=(130, 145, 165), font=load_font(11))
            y += 14
        y += 4

    if lecture_mode:
        if bullets:
            bullet_font = load_font(CAPTION_MIN_PT)
            for bi, bullet in enumerate(bullets[:3]):
                ba = (
                    bullet_alphas[bi]
                    if bullet_alphas and bi < len(bullet_alphas)
                    else line2_alpha
                )
                if ba <= 0.02:
                    continue
                ty2 = y
                bullet_text = f"•  {bullet}"
                for bline in wrap(draw, bullet_text, bullet_font, caption_max_w - 8):
                    l2_fill = _fade((236, 240, 248), ba)
                    if jfx_mod:
                        jfx_mod.javafx_glow_text(draw, (x, ty2), bline, bullet_font, l2_fill, glow_level=0.22)
                    else:
                        draw.text((x, ty2), bline, fill=l2_fill, font=bullet_font)
                    ty2 += CAPTION_MIN_PT + 2
                y = ty2 + 1
        if future and line3_alpha > 0.02:
            y += 4
            ty3 = y
            draw.rounded_rectangle([x, ty3 + 1, x + 58, ty3 + 22], radius=4, fill=accent)
            draw.text((x + 10, ty3 + 3), "NEXT", fill=(255, 255, 255), font=load_font(11, bold=True))
            fx = x + 68
            future_font = load_font(CAPTION_MIN_PT)
            for fline in wrap(draw, future, future_font, caption_max_w - (fx - x) - 8):
                l3_fill = _fade((160, 220, 190), line3_alpha)
                if jfx_mod:
                    jfx_mod.javafx_glow_text(draw, (fx, ty3), fline, future_font, l3_fill, glow_level=0.2)
                else:
                    draw.text((fx, ty3), fline, fill=l3_fill, font=future_font)
                ty3 += CAPTION_MIN_PT + 2
                y = ty3
    else:
        why_w = 44
        draw.rounded_rectangle([x, y + 1, x + why_w, y + 19], radius=4, fill=accent)
        draw.text((x + 9, y + 2), "WHY", fill=(255, 255, 255), font=load_font(9, bold=True))
        tx = x + why_w + 8
        for line in wrap(draw, line2 or "", load_font(CAPTION_MIN_PT), caption_max_w - (tx - x)):
            draw.text((tx, y), line, fill=(236, 240, 248), font=load_font(CAPTION_MIN_PT))
            y += CAPTION_MIN_PT + 2
        if line3:
            y += 4
            draw.rounded_rectangle([x, y + 1, x + 52, y + 17], radius=3, fill=(55, 65, 80))
            draw.text((x + 8, y + 2), "NOTE", fill=(200, 208, 220), font=load_font(9, bold=True))
            nx = x + 60
            for line in wrap(draw, line3, load_font(CAPTION_MIN_PT), caption_max_w - (nx - x) - 8):
                draw.text((nx, y), line, fill=(188, 196, 208), font=load_font(CAPTION_MIN_PT))
                y += CAPTION_MIN_PT + 2

    if javafx_effects and jfx_mod:
        spec_jfx = {**(javafx_spec or {}), "javafxEffects": True, "captionBarRatio": BAR / H}
        img = jfx_mod.apply_frame_effects(
            img,
            frame_index=frame_index,
            total_frames=total_frames,
            accent=accent,
            spec=spec_jfx,
        )

    return img


def render_title_card(
    title: str,
    subtitle: str,
    *,
    accent: tuple[int, int, int],
    footer: str | None = None,
) -> Image.Image:
    img = Image.new("RGB", (W, H), (10, 14, 22))
    draw = ImageDraw.Draw(img)
    for y in range(H):
        t = y / H
        col = (lerp(10, accent[0] // 4, t), lerp(14, accent[1] // 4, t), lerp(22, accent[2] // 3, t))
        draw.line([(0, y), (W, y)], fill=col)

    # decorative rings
    cx, cy = W // 2, H // 2 - 30
    for r, alpha in ((280, 30), (220, 45), (160, 70)):
        ring = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        rdraw = ImageDraw.Draw(ring)
        rdraw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(*accent, alpha), width=2)
        img = Image.alpha_composite(img.convert("RGBA"), ring).convert("RGB")

    draw = ImageDraw.Draw(img)
    tw = draw.textlength(title, font=load_font(48, bold=True))
    draw.text(((W - tw) / 2, cy - 60), title, fill=(255, 255, 255), font=load_font(48, bold=True))
    for i, line in enumerate(wrap(draw, subtitle, load_font(22), W - 160)):
        lw = draw.textlength(line, font=load_font(22))
        draw.text(((W - lw) / 2, cy + 20 + i * 30), line, fill=(200, 210, 225), font=load_font(22))

    if footer:
        fw = draw.textlength(footer, font=load_font(14))
        draw.text(((W - fw) / 2, H - 80), footer, fill=accent, font=load_font(14, bold=True))

    draw.rectangle([W // 2 - 120, H - 48, W // 2 + 120, H - 44], fill=accent)
    return apply_vignette(img, 0.25)


def segment_filter(duration: float, motion: str) -> str:
    """Fast motion: slight overscale + animated crop + fades (no zoompan)."""
    fade_in, fade_out = 0.35, 0.35
    out_start = max(0.0, duration - fade_out)
    # Overscale so we can creep crop window (Ken-Burns feel, much faster than zoompan).
    pan_expr = "0" if motion != "pan_left" else f"(iw-ow)*t/{max(duration, 0.01)}"
    return (
        f"scale=1360:765:force_original_aspect_ratio=increase,"
        f"crop={W}:{H}:{pan_expr}:(ih-oh)/2,"
        f"fade=t=in:st=0:d={fade_in},fade=t=out:st={out_start}:d={fade_out},"
        f"unsharp=5:5:0.55:5:5:0.0"
    )


def build_segment(ffmpeg: str, src: Path, dst: Path, duration: float, motion: str = "zoom") -> None:
    vf = segment_filter(duration, motion)
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-loop",
            "1",
            "-t",
            f"{duration:.3f}",
            "-i",
            str(src),
            "-vf",
            vf,
            "-c:v",
            "libx264",
            "-crf",
            "17",
            "-preset",
            "medium",
            "-pix_fmt",
            "yuv420p",
            "-r",
            str(FPS),
            str(dst),
        ],
        check=True,
        capture_output=True,
    )


def xfade_merge(ffmpeg: str, a: Path, b: Path, out: Path, xfade: float, transition: str) -> float:
    """Returns duration of output video."""
    prob = subprocess.run(
        [ffmpeg, "-i", str(a), "-hide_banner"],
        capture_output=True,
        text=True,
    )
    dur_a = parse_duration(prob.stderr)
    offset = max(0.0, dur_a - xfade)
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-i",
            str(a),
            "-i",
            str(b),
            "-filter_complex",
            f"[0:v][1:v]xfade=transition={transition}:duration={xfade}:offset={offset:.3f},format=yuv420p",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-crf",
            "18",
            "-preset",
            "slow",
            str(out),
        ],
        check=True,
        capture_output=True,
    )
    prob2 = subprocess.run(
        [ffmpeg, "-i", str(out), "-hide_banner"],
        capture_output=True,
        text=True,
    )
    return parse_duration(prob2.stderr)


def parse_duration(ffmpeg_stderr: str) -> float:
    for line in ffmpeg_stderr.splitlines():
        if "Duration:" in line:
            part = line.split("Duration:")[1].split(",")[0].strip()
            h, m, s = part.split(":")
            return int(h) * 3600 + int(m) * 60 + float(s)
    return 3.0


def compose(
    frames: list[dict],
    output: Path,
    *,
    slide_duration: float = 3.0,
    xfade_duration: float = 0.45,
    intro_title: str = "World Build Recap",
    intro_subtitle: str = "Narrated pipeline checkpoints from your Unity build",
    outro_title: str = "Recap complete",
    outro_subtitle: str = "Re-open Scene view to iterate on terrain, mountains, and caves",
    work_dir: Path,
) -> Path:
    ffmpeg = find_ffmpeg()
    work_dir.mkdir(parents=True, exist_ok=True)
    staged = work_dir / "staged"
    segments = work_dir / "segments"
    for d in (staged, segments):
        d.mkdir(exist_ok=True)
        for f in d.glob("*"):
            f.unlink()

    accent = (72, 138, 220)
    total = len(frames)
    motions = ("zoom", "pan_left", "zoom", "pan_left", "zoom")

    # intro
    intro_png = staged / "intro.png"
    render_title_card(intro_title, intro_subtitle, accent=accent, footer="Environment Kit").save(intro_png)
    intro_seg = segments / "intro.mp4"
    build_segment(ffmpeg, intro_png, intro_seg, 2.2, "zoom")

    seg_paths: list[Path] = [intro_seg]
    transitions = ("fadeblack", "fade", "slideleft", "fade", "wiperight", "fade")

    for i, fr in enumerate(frames):
        src = Path(fr["image"])
        if not src.is_file():
            raise FileNotFoundError(src)

        base = Image.open(src)
        chapter = fr.get("chapter") or fr.get("phase") or f"Milestone {i + 1}"
        ac = tuple(fr.get("accent", accent))
        meta_parts = [p for p in (fr.get("phase"), fr.get("sub")) if p]
        meta = " · ".join(str(p) for p in meta_parts)[:160] if meta_parts else fr.get("meta")
        captioned = render_captioned_frame(
            base,
            line1=fr.get("line1", ""),
            line2=fr.get("line2", ""),
            line3=fr.get("line3"),
            chapter=str(chapter)[:52],
            index=i,
            total=total,
            accent=ac,
            meta=meta,
        )
        png = staged / f"slide_{i:04d}.png"
        captioned.save(png)
        motion = motions[i % len(motions)]
        seg = segments / f"slide_{i:04d}.mp4"
        build_segment(ffmpeg, png, seg, slide_duration, motion)
        seg_paths.append(seg)

    outro_png = staged / "outro.png"
    render_title_card(outro_title, outro_subtitle, accent=(90, 180, 140), footer="Happy building").save(outro_png)
    outro_seg = segments / "outro.mp4"
    build_segment(ffmpeg, outro_png, outro_seg, 2.4, "zoom")
    seg_paths.append(outro_seg)

    # Prefer single-pass concat (fast). Optional xfade chain when few segments.
    final = output
    final.parent.mkdir(parents=True, exist_ok=True)
    if len(seg_paths) <= 12 and xfade_duration > 0.01:
        merged = seg_paths[0]
        for j in range(1, len(seg_paths)):
            out = work_dir / f"merged_{j:03d}.mp4"
            tr = transitions[(j - 1) % len(transitions)]
            xfade_merge(ffmpeg, merged, seg_paths[j], out, xfade_duration, tr)
            if j > 1:
                merged.unlink(missing_ok=True)
            merged = out
        if merged.resolve() != final.resolve():
            subprocess.run(
                [ffmpeg, "-y", "-i", str(merged), "-c", "copy", str(final)],
                check=True,
                capture_output=True,
            )
    else:
        list_path = work_dir / "segments_concat.txt"
        list_path.write_text(
            "\n".join(f"file '{p.as_posix()}'" for p in seg_paths) + "\n"
        )
        subprocess.run(
            [
                ffmpeg,
                "-y",
                "-f",
                "concat",
                "-safe",
                "0",
                "-i",
                str(list_path),
                "-c",
                "copy",
                str(final),
            ],
            check=True,
            capture_output=True,
        )
    return final


def resolve_hub() -> Path:
    hub = Path(os.environ.get("HUB_ROOT", "")).expanduser()
    if hub.is_dir() and (hub / "Assets").is_dir():
        return hub
    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    return hub


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: compose-demo-recap.py <run_or_json> [output.mp4]", file=sys.stderr)
        return 1

    arg = Path(sys.argv[1]).expanduser()
    if arg.suffix == ".json":
        spec = json.loads(arg.read_text())
        work_dir = arg.parent
        output = Path(sys.argv[2]) if len(sys.argv) > 2 else work_dir / "DemoRecap.mp4"
    else:
        work_dir = arg
        spec_path = work_dir / "DemoRecapCompose.json"
        if not spec_path.is_file():
            print(f"Missing {spec_path}", file=sys.stderr)
            return 1
        spec = json.loads(spec_path.read_text())
        output = work_dir / "DemoRecap.mp4"

    frames = spec.get("frames", [])
    if not frames:
        print("No frames in compose spec.", file=sys.stderr)
        return 1

    compose(
        frames,
        output,
        slide_duration=float(spec.get("slideDuration", 3.0)),
        xfade_duration=float(spec.get("xfadeDuration", 0.45)),
        intro_title=spec.get("introTitle", "World Build Recap"),
        intro_subtitle=spec.get("introSubtitle", "Narrated pipeline checkpoints from your Unity build"),
        outro_title=spec.get("outroTitle", "Recap complete"),
        outro_subtitle=spec.get("outroSubtitle", "Re-open Scene view to iterate on terrain, mountains, and caves"),
        work_dir=work_dir / "_compose_work",
    )
    print(str(output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
