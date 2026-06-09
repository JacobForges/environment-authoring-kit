"""
JavaFX-inspired animated effects for demo recap frames.

Mirrors common JavaFX Effects + Transitions in Pillow:
  Glow, DropShadow, Bloom, GaussianBlur, FadeTransition, TranslateTransition,
  ScaleTransition, pulse Timeline keyframes, ambient Particles.

Used by compose-demo-recap and hybrid approval preview when javafxEffects=true.
"""
from __future__ import annotations

import math
import random
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter



def _ease_out_cubic(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return 1.0 - (1.0 - t) ** 3


def _ease_in_out_quad(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return 2 * t * t if t < 0.5 else 1 - (-2 * t + 2) ** 2 / 2


def javafx_glow(img: Image.Image, level: float = 0.35, *, accent: tuple[int, int, int] | None = None) -> Image.Image:
    """JavaFX Glow — brighten highlights and add colored bloom."""
    level = max(0.0, min(1.0, level))
    if level < 0.02:
        return img
    rgb = img.convert("RGB")
    bright = ImageEnhance.Brightness(rgb).enhance(1.0 + level * 0.45)
    bright = ImageEnhance.Contrast(bright).enhance(1.0 + level * 0.12)
    mask = bright.convert("L")
    mask = mask.point(lambda p: max(0, min(255, int((p - 140) * 2.2))))
    mask = mask.filter(ImageFilter.GaussianBlur(radius=6))
    glow = Image.new("RGB", rgb.size, accent or (24, 235, 158))
    out = Image.composite(glow, rgb, mask)
    return Image.blend(rgb, out, level * 0.55)


def javafx_drop_shadow(
    layer: Image.Image,
    *,
    offset: tuple[int, int] = (0, 4),
    blur: int = 8,
    opacity: float = 0.55,
    color: tuple[int, int, int] = (0, 0, 0),
) -> Image.Image:
    """JavaFX DropShadow on an RGBA layer."""
    if layer.mode != "RGBA":
        layer = layer.convert("RGBA")
    alpha = layer.split()[3]
    shadow = Image.new("RGBA", layer.size, (*color, 0))
    shadow.putalpha(alpha.filter(ImageFilter.GaussianBlur(radius=blur)))
    base = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    base.alpha_composite(shadow, dest=offset)
    base.alpha_composite(layer)
    return base


def javafx_bloom(img: Image.Image, threshold: int = 180, strength: float = 0.28) -> Image.Image:
    """JavaFX Bloom — soft highlight expansion."""
    if strength < 0.02:
        return img
    rgb = img.convert("RGB")
    lum = rgb.convert("L")
    mask = lum.point(lambda p: 255 if p >= threshold else 0)
    mask = mask.filter(ImageFilter.GaussianBlur(radius=10))
    bloom = ImageEnhance.Brightness(rgb).enhance(1.35)
    return Image.composite(bloom, rgb, mask.point(lambda p: int(p * strength)))


def javafx_fade_alpha(t: float, delay: float = 0.0, duration: float = 1.0) -> float:
    """FadeTransition opacity 0→1."""
    if duration <= 0:
        return 1.0 if t >= delay else 0.0
    return _ease_out_cubic(max(0.0, min(1.0, (t - delay) / duration)))


def javafx_slide_offset(t: float, delay: float = 0.0, duration: float = 0.8, distance: int = 28) -> int:
    """TranslateTransition Y slide (returns pixel offset, 0 = final)."""
    a = javafx_fade_alpha(t, delay, duration)
    return int((1.0 - a) * distance)


def javafx_scale(t: float, delay: float = 0.0, duration: float = 0.6, from_scale: float = 0.92) -> float:
    """ScaleTransition — 0.92→1.0 around center."""
    a = javafx_fade_alpha(t, delay, duration)
    return from_scale + (1.0 - from_scale) * a


def javafx_pulse(t: float, period: float = 2.2, lo: float = 0.55, hi: float = 1.0) -> float:
    """Timeline pulse keyframe."""
    phase = (t * period) % 1.0
    return lo + (hi - lo) * (0.5 + 0.5 * math.sin(phase * math.pi * 2))


def _particle_positions(seed: int, count: int, w: int, h: int) -> list[tuple[float, float, float]]:
    rng = random.Random(seed)
    return [(rng.uniform(0, w), rng.uniform(0, h), rng.uniform(0.4, 1.4)) for _ in range(count)]


def javafx_particles(
    img: Image.Image,
    t: float,
    *,
    seed: int = 0,
    count: int = 18,
    accent: tuple[int, int, int] = (24, 235, 158),
    region: tuple[int, int, int, int] | None = None,
) -> Image.Image:
    """Ambient floating particles (JavaFX Particle / Timeline style)."""
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    x0, y0, x1, y1 = region or (0, 0, img.width, img.height // 2)
    rw, rh = x1 - x0, y1 - y0
    for i, (bx, by, spd) in enumerate(_particle_positions(seed, count, rw, rh)):
        px = x0 + (bx + math.sin(t * spd * 3.1 + i) * 12) % rw
        py = y0 + (by + math.cos(t * spd * 2.7 + i * 0.7) * 10) % rh
        alpha = int(40 + 50 * (0.5 + 0.5 * math.sin(t * 4 + i)))
        r = 2 if i % 3 else 1
        draw.ellipse([px - r, py - r, px + r, py + r], fill=(*accent, alpha))
    return Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")


def javafx_ripple_ring(
    draw: ImageDraw.ImageDraw,
    cx: float,
    cy: float,
    t: float,
    *,
    accent: tuple[int, int, int],
    max_r: float = 90,
    alpha: float = 0.5,
) -> None:
    """Radial ripple on annotation focus."""
    phase = (t * 1.8) % 1.0
    r = 18 + phase * max_r
    a = int(180 * alpha * (1.0 - phase))
    if a < 8:
        return
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(*accent, a), width=2)


def javafx_glow_text(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int],
    text: str,
    font,
    fill: tuple[int, int, int],
    glow_level: float = 0.6,
) -> None:
    """Text with DropShadow + Glow stack."""
    glow_level = max(0.0, min(1.0, glow_level))
    if glow_level > 0.05:
        glow_fill = (fill[0] // 4, fill[1] // 4, fill[2] // 4)
        for dx, dy in [(-2, 0), (2, 0), (0, -2), (0, 2), (-1, -1), (1, 1)]:
            draw.text((xy[0] + dx, xy[1] + dy), text, fill=glow_fill, font=font)
    draw.text(xy, text, fill=fill, font=font)


def javafx_caption_bar_shimmer(
    img: Image.Image,
    bar_top: int,
    t: float,
    accent: tuple[int, int, int],
) -> Image.Image:
    """Subtle accent shimmer across caption bar (TranslateTransition sweep)."""
    w, h = img.size
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    sweep = int((t % 1.0) * (w + 120)) - 60
    draw.rectangle([sweep, bar_top, sweep + 80, h], fill=(*accent, 28))
    return Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")


def apply_frame_effects(
    img: Image.Image,
    *,
    frame_index: int,
    total_frames: int,
    accent: tuple[int, int, int],
    spec: dict[str, Any] | None = None,
) -> Image.Image:
    """Post-compose JavaFX stack on a full captioned frame."""
    spec = spec or {}
    if spec.get("javafxEffects") is False:
        return img
    t = frame_index / max(1, total_frames - 1)
    seed = int(spec.get("javafxSeed", 0)) + frame_index
    strength = float(spec.get("javafxGlowLevel", 0.22))
    img = javafx_glow(img, strength * javafx_pulse(t, period=3.0, lo=0.7, hi=1.0), accent=accent)
    img = javafx_bloom(img, strength=float(spec.get("javafxBloom", 0.16)))
    img = javafx_particles(img, t, seed=seed, count=int(spec.get("javafxParticleCount", 12)), accent=accent)
    if spec.get("javafxBarShimmer", True):
        bar_ratio = float(spec.get("captionBarRatio", 294 / 1080))
        bar_top = int(img.height * (1.0 - bar_ratio))
        img = javafx_caption_bar_shimmer(img, bar_top, t, accent)
    return img


def caption_slide_y(line_alpha: float, base_y: int, frame_t: float, delay: float) -> int:
    """Combine line alpha with TranslateTransition offset."""
    off = javafx_slide_offset(frame_t, delay=delay, duration=0.55, distance=22)
    return base_y + off if line_alpha < 0.98 else base_y


def javafx_ffmpeg_vf(*, strength: float = 1.0) -> str:
    """
    JavaFX-style video polish via ffmpeg — glow, bloom, grain, vignette.
    Applied to hybrid screen B-roll and card segments (no caption overlay).
    """
    s = max(0.5, min(2.0, strength))
    sat = 1.0 + 0.22 * s
    gamma = 1.05 + 0.05 * s
    grain = max(3, int(6 * s))
    vig = 0.28 + 0.12 * s
    bloom = 0.08 + 0.06 * s
    return (
        "scale=1920:1080:force_original_aspect_ratio=decrease,"
        "pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=0x0E1218,"
        f"eq=gamma={gamma:.3f}:contrast=1.06:saturation={sat:.3f}:brightness=-0.02,"
        "colorbalance=rs=-0.03:gs=0.06:bs=0.10,"
        f"boxblur=luma_radius={bloom:.3f}:luma_power=1:chroma_radius=0,"
        "unsharp=5:5:0.28:5:5:0.0,"
        f"noise=alls={grain}:allf=t+u,"
        f"vignette=angle=PI/4:mode=forward:a={vig:.3f}"
    )
