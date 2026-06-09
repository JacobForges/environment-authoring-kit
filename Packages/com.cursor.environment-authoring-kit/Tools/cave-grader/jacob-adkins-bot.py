#!/usr/bin/env python3
"""
Jacob Adkins's Bot — layered 3D animated avatar with audio-driven lip sync.

Procedural robot mascot (no portrait). Depth layers for XR/VR parallax; mouth
driven from narration envelope at mux time.
"""
from __future__ import annotations

import json
import math
import os
import shutil
import subprocess
import sys
import time
import uuid
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

BOT_NAME = "Jacob Adkins's Bot"
BOT_LABEL = "Jacob Adkins's Bot"
BOT_TAGLINE = "your digital build guide"
EMERALD = (24, 235, 158)
ACCENT_GLOW = (80, 255, 200)
DARK = (8, 14, 20)
METAL_DARK = (18, 42, 52)
METAL_MID = (32, 78, 92)
METAL_LIGHT = (72, 140, 155)

AVATAR_W = 240
CHAR_SIZE = 168
LOOP_FPS = 30
LOOP_SEC = 4.0
ASSET_BASENAME = "JacobAdkinsBot-Avatar-Loop"
LIPSYNC_BASENAME = "JacobAdkinsBot-Avatar-LipSync"
LIPSYNC_MANIFEST_BASENAME = "JacobAdkinsBot-Avatar-LipSync"
AVATAR_RENDER_VERSION = 33
UNITY_CYBORG_MODEL = "kenney-cyborg-male"

# XR depth offsets: (x, y, scale) — front layer pops toward viewer
DEPTH = {
    "back": {"x": -2, "y": 6, "scale": 0.94, "z": -2},
    "mid": {"x": 0, "y": 0, "scale": 1.0, "z": 0},
    "front": {"x": 5, "y": -8, "scale": 1.07, "z": 2},
}


def _font(size: int, bold: bool = False):
    name = (
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold
        else "/System/Library/Fonts/Supplemental/Arial.ttf"
    )
    try:
        return ImageFont.truetype(name, size)
    except OSError:
        return ImageFont.load_default()


def _lerp_rgb(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    t = max(0.0, min(1.0, t))
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def _draw_sphere(
    layer: Image.Image,
    cx: int,
    cy: int,
    radius: int,
    base: tuple[int, int, int],
    highlight: tuple[int, int, int],
    shadow: tuple[int, int, int],
) -> None:
    draw = ImageDraw.Draw(layer)
    for r in range(radius, 0, -1):
        t = r / max(1, radius)
        col = _lerp_rgb(shadow, base, t * 0.85)
        if t > 0.55:
            col = _lerp_rgb(col, highlight, (t - 0.55) / 0.45)
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(*col, 255))


def _draw_rounded_body(
    layer: Image.Image,
    box: tuple[int, int, int, int],
    fill: tuple[int, int, int],
    edge: tuple[int, int, int],
    *,
    radius: int = 14,
) -> None:
    draw = ImageDraw.Draw(layer)
    draw.rounded_rectangle(box, radius=radius, fill=(*fill, 255))
    x0, y0, x1, y1 = box
    draw.line([(x0 + 6, y0 + 4), (x1 - 6, y0 + 4)], fill=(*edge, 180), width=2)
    draw.line([(x0 + 4, y1 - 6), (x1 - 4, y1 - 6)], fill=(*_lerp_rgb(fill, (0, 0, 0), 0.35), 200), width=3)


def _draw_back_layer(
    slot: Image.Image,
    cx: int,
    cy: int,
    size: int,
    phase: float,
    bob: int,
) -> None:
    """Halo disc + ambient glow — farthest XR layer."""
    draw = ImageDraw.Draw(slot)
    glow_a = int(45 + 40 * (0.5 + 0.5 * math.sin(phase * 2)))
    draw.ellipse([10, 10 + bob, AVATAR_W - 10, AVATAR_W - 10 + bob], fill=(*DARK, 200))
    draw.ellipse([14, 14 + bob, AVATAR_W - 14, AVATAR_W - 14 + bob], outline=(*EMERALD, glow_a), width=4)
    # Floor shadow plate
    sh_y = cy + int(size * 0.38)
    draw.ellipse(
        [cx - int(size * 0.34), sh_y, cx + int(size * 0.34), sh_y + int(size * 0.12)],
        fill=(0, 0, 0, 90),
    )


def _draw_mid_layer(
    slot: Image.Image,
    cx: int,
    cy: int,
    size: int,
    phase: float,
    *,
    amp: float,
) -> None:
    """Torso, arms, chest core — middle depth."""
    sway = int(math.sin(phase) * 4)
    head_r = int(size * 0.28)
    head_cx = cx + sway
    head_cy = cy - int(size * 0.14) + int(math.sin(phase * 1.3) * 2)

    body = Image.new("RGBA", slot.size, (0, 0, 0, 0))
    _draw_rounded_body(
        body,
        (head_cx - int(size * 0.38), head_cy + head_r - 6, head_cx + int(size * 0.38), cy + int(size * 0.36)),
        METAL_MID,
        METAL_LIGHT,
        radius=18,
    )
    bd = ImageDraw.Draw(body)
    core_w = int(size * 0.22)
    core_h = int(size * 0.18)
    core_x = head_cx - core_w // 2
    core_y = head_cy + head_r + 8
    pulse = 0.45 + 0.55 * amp
    bd.ellipse(
        [core_x, core_y, core_x + core_w, core_y + core_h],
        fill=(*ACCENT_GLOW, int(70 + 100 * pulse)),
    )
    bd.ellipse(
        [core_x + 6, core_y + 5, core_x + core_w - 6, core_y + core_h - 5],
        outline=(*EMERALD, 220),
        width=2,
    )
    for side in (-1, 1):
        ax = head_cx + side * int(size * 0.42)
        ay = head_cy + head_r + 14
        bd.rounded_rectangle(
            [ax - 10, ay, ax + 10, ay + int(size * 0.22)],
            radius=8,
            fill=(*METAL_DARK, 255),
        )
        bd.ellipse([ax - 8, ay + int(size * 0.18), ax + 8, ay + int(size * 0.28)], fill=(*METAL_LIGHT, 255))
    slot.alpha_composite(body)

    if amp > 0.08:
        draw = ImageDraw.Draw(slot)
        bar_y = cy + int(size * 0.34)
        for bi in range(5):
            bx = cx - 28 + bi * 12 + sway
            h = int(6 + 18 * amp * abs(math.sin(phase * 7 + bi * 0.9)))
            draw.rounded_rectangle([bx, bar_y, bx + 6, bar_y + h], radius=2, fill=(*EMERALD, 200))


def _draw_front_layer(
    slot: Image.Image,
    cx: int,
    cy: int,
    size: int,
    phase: float,
    *,
    jaw_open: float,
    amp: float,
) -> None:
    """Head, visor, eyes, mouth — nearest XR layer (pops toward viewer)."""
    sway = int(math.sin(phase) * 5)
    head_r = int(size * 0.28)
    head_cx = cx + sway
    head_cy = cy - int(size * 0.14) + int(math.sin(phase * 1.3) * 2)

    head_layer = Image.new("RGBA", slot.size, (0, 0, 0, 0))
    _draw_sphere(head_layer, head_cx, head_cy, head_r, METAL_MID, METAL_LIGHT, METAL_DARK)
    hd = ImageDraw.Draw(head_layer)

    visor_y = head_cy - int(head_r * 0.12)
    visor_h = int(head_r * 0.55)
    hd.rounded_rectangle(
        [head_cx - int(head_r * 0.78), visor_y, head_cx + int(head_r * 0.78), visor_y + visor_h],
        radius=10,
        fill=(4, 18, 24, 230),
    )
    hd.rounded_rectangle(
        [head_cx - int(head_r * 0.74), visor_y + 3, head_cx + int(head_r * 0.74), visor_y + visor_h - 3],
        radius=8,
        outline=(*EMERALD, 200),
        width=2,
    )

    blink = phase % (2 * math.pi) < 0.14 and amp < 0.12
    eye_y = visor_y + int(visor_h * 0.38)
    for side in (-1, 1):
        ex = head_cx + side * int(head_r * 0.38) + int(math.sin(phase * 1.1) * 3)
        ey = eye_y
        if blink:
            hd.line([(ex - 10, ey), (ex + 10, ey)], fill=(*ACCENT_GLOW, 255), width=3)
        else:
            hd.ellipse([ex - 11, ey - 9, ex + 11, ey + 9], fill=(*ACCENT_GLOW, 255))
            hd.ellipse([ex - 5, ey - 5, ex + 2, ey + 2], fill=(255, 255, 255, 220))

    mouth_y = head_cy + int(head_r * 0.42)
    mouth_w = int(head_r * (0.22 + 0.28 * jaw_open))
    mouth_h = int(3 + 16 * jaw_open)
    hd.rounded_rectangle(
        [head_cx - mouth_w, mouth_y, head_cx + mouth_w, mouth_y + mouth_h],
        radius=4,
        fill=(*ACCENT_GLOW, int(140 + 110 * jaw_open)),
    )
    if jaw_open > 0.15:
        glow = Image.new("RGBA", slot.size, (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        gd.ellipse(
            [head_cx - mouth_w - 8, mouth_y - 6, head_cx + mouth_w + 8, mouth_y + mouth_h + 12],
            fill=(*ACCENT_GLOW, int(55 * jaw_open)),
        )
        head_layer = Image.alpha_composite(head_layer, glow.filter(ImageFilter.GaussianBlur(4)))

    ax = head_cx + int(head_r * 0.15)
    hd.line([(ax, head_cy - head_r - 2), (ax, head_cy - head_r - 18)], fill=(*EMERALD, 200), width=2)
    led_on = amp > 0.1 or math.sin(phase * 4) > 0.15
    hd.ellipse([ax - 5, head_cy - head_r - 26, ax + 5, head_cy - head_r - 16], fill=(*ACCENT_GLOW, 255 if led_on else 70))

    highlight = Image.new("RGBA", slot.size, (0, 0, 0, 0))
    hl = ImageDraw.Draw(highlight)
    hl.ellipse(
        [head_cx - int(head_r * 0.55), head_cy - int(head_r * 0.75), head_cx - int(head_r * 0.1), head_cy - int(head_r * 0.2)],
        fill=(255, 255, 255, 40),
    )
    head_layer = Image.alpha_composite(head_layer, highlight)
    slot.alpha_composite(head_layer)


def _composite_depth_layers(
    layers: dict[str, Image.Image],
    bob: int,
) -> Image.Image:
    """Parallax composite — front layer pops toward viewer."""
    canvas_h = AVATAR_W + 58
    out = Image.new("RGBA", (AVATAR_W, canvas_h), (0, 0, 0, 0))
    for name in ("back", "mid", "front"):
        layer = layers[name]
        cfg = DEPTH[name]
        scale = cfg["scale"]
        if abs(scale - 1.0) > 0.01:
            nw = max(1, int(layer.width * scale))
            nh = max(1, int(layer.height * scale))
            layer = layer.resize((nw, nh), Image.Resampling.LANCZOS)
        ox = (AVATAR_W - layer.width) // 2 + cfg["x"]
        oy = cfg["y"] + bob
        out.paste(layer, (ox, oy), layer)

    draw = ImageDraw.Draw(out)
    bf = _font(9, bold=True)
    label = BOT_LABEL
    tw = int(draw.textlength(label, font=bf)) + 14
    bx = (AVATAR_W - tw) // 2
    by = AVATAR_W + 4 + bob
    draw.rounded_rectangle([bx, by, bx + tw, by + 22], radius=7, fill=(*EMERALD, 240))
    draw.text((bx + 7, by + 5), label, fill=DARK, font=bf)
    sf = _font(9)
    sw = int(draw.textlength(BOT_TAGLINE, font=sf))
    draw.text(((AVATAR_W - sw) // 2, by + 26), BOT_TAGLINE, fill=(190, 220, 205), font=sf)

    # Corner brackets on composite
    for corner in ("tl", "tr", "bl", "br"):
        ox = 18 if "l" in corner else AVATAR_W - 34
        oy = 18 + bob if "t" in corner else AVATAR_W - 34 + bob
        if corner == "tl":
            draw.line([(ox, oy + 14), (ox, oy), (ox + 14, oy)], fill=(*EMERALD, 220), width=2)
        elif corner == "tr":
            draw.line([(ox, oy), (ox, oy + 14)], fill=(*EMERALD, 220), width=2)
            draw.line([(ox - 14, oy), (ox, oy)], fill=(*EMERALD, 220), width=2)
        elif corner == "bl":
            draw.line([(ox, oy - 14), (ox, oy), (ox + 14, oy)], fill=(*EMERALD, 220), width=2)
        else:
            draw.line([(ox - 14, oy), (ox, oy), (ox, oy - 14)], fill=(*EMERALD, 220), width=2)
    return out


def _render_bot_layers(
    frame_i: int,
    total_frames: int,
    *,
    jaw_open: float = 0.0,
    amp: float = 0.0,
) -> dict[str, Image.Image]:
    t = frame_i / max(1, total_frames)
    phase = t * math.pi * 2
    bob = int(math.sin(phase) * 3)
    breathe = 1.0 + 0.018 * math.sin(phase * 1.3)
    cx, cy = AVATAR_W // 2, AVATAR_W // 2 - 6 + bob
    fs = int(CHAR_SIZE * breathe)

    canvas_h = AVATAR_W + 58
    back = Image.new("RGBA", (AVATAR_W, canvas_h), (0, 0, 0, 0))
    mid = Image.new("RGBA", (AVATAR_W, canvas_h), (0, 0, 0, 0))
    front = Image.new("RGBA", (AVATAR_W, canvas_h), (0, 0, 0, 0))

    _draw_back_layer(back, cx, cy, fs, phase, bob)
    _draw_mid_layer(mid, cx, cy, fs, phase, amp=amp)
    _draw_front_layer(front, cx, cy, fs, phase, jaw_open=jaw_open, amp=amp)

    return {"back": back, "mid": mid, "front": front, "composite": _composite_depth_layers(
        {"back": back, "mid": mid, "front": front}, bob
    )}


def _render_bot_frame(
    frame_i: int,
    total_frames: int,
    *,
    jaw_open: float = 0.0,
    amp: float = 0.0,
) -> Image.Image:
    return _render_bot_layers(frame_i, total_frames, jaw_open=jaw_open, amp=amp)["composite"]


def _audio_envelope(wav_path: Path, fps: int, frame_count: int) -> list[float]:
    """Per-frame RMS + peak envelope from narration — drives visible mouth sync."""
    import numpy as np
    import soundfile as sf

    y, sr = sf.read(str(wav_path), always_2d=True, dtype="float32")
    mono = y.mean(axis=1)
    hop = max(1, int(sr / fps))
    env: list[float] = []
    for fi in range(frame_count):
        start = fi * hop
        chunk = mono[start : start + hop]
        if chunk.size < 1:
            env.append(0.0)
            continue
        rms = float(np.sqrt(np.mean(chunk.astype(np.float64) ** 2)))
        peak = float(np.max(np.abs(chunk)))
        env.append(max(rms, peak * 0.78))
    peak = max(env) if env else 1e-6
    if peak < 1e-8:
        return [0.0] * frame_count
    norm = [min(1.0, (v / peak) ** 0.82) for v in env]
    smooth: list[float] = []
    for i, v in enumerate(norm):
        prev = norm[i - 1] if i else v
        nxt = norm[i + 1] if i + 1 < len(norm) else v
        s = prev * 0.16 + v * 0.62 + nxt * 0.22
        smooth.append(0.0 if s < 0.045 else min(1.0, s * 1.12))
    return smooth


def _probe_video_fps(video: Path, ffprobe: str) -> float:
    try:
        out = subprocess.run(
            [
                ffprobe, "-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=r_frame_rate", "-of", "csv=p=0",
                str(video),
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        raw = (out.stdout or "30/1").strip().split("\n")[0]
        if "/" in raw:
            n, d = raw.split("/", 1)
            return max(1.0, float(n) / float(d))
        return max(1.0, float(raw))
    except (subprocess.CalledProcessError, ValueError, ZeroDivisionError):
        return 30.0


def _encode_png_sequence(seq_dir: Path, pattern: str, fps: int, out: Path, ffmpeg: str) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        [
            ffmpeg, "-y", "-framerate", str(fps),
            "-i", str(seq_dir / pattern),
            "-c:v", "prores_ks", "-profile:v", "4444",
            "-pix_fmt", "yuva444p10le", str(out),
        ],
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        tail = (result.stderr or result.stdout or "").strip()[-800:]
        raise RuntimeError(
            f"ffmpeg prores encode failed ({result.returncode}) → {out}\n{tail}"
        )


def _resolve_unity_executable() -> Path | None:
    for cand in (
        os.environ.get("UNITY_PATH"),
        "/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity",
    ):
        if cand and Path(cand).is_file():
            return Path(cand)
    return None


def _bot_avatar_source(spec: dict | None) -> str:
    spec = spec or {}
    return str(spec.get("botAvatarSource") or "unity").strip().lower()


# Kenney characterMedium cyborg atlas — front head tile (256×256 in 1024 skin).
_KENNEY_HEAD_FRONT_BOX = (256, 128, 512, 384)
_KENNEY_MOUTH_UV = (128, 172)
_KENNEY_BASE_SKIN = (
    Path(__file__).resolve().parents[4]
    / "Assets/EnvironmentKit/CC0Imports/kenney-animated-characters-2/Skins/cyborgMaleA.png"
)


def _trim_portrait_white_edges(face: Image.Image) -> Image.Image:
    """Drop white photo backdrop beside hair before compositing."""
    rgba = face.convert("RGBA")
    w, h = rgba.size
    px = rgba.load()
    col_has_hair = [False] * w
    for x in range(w):
        for y in range(h):
            r, g, b, a = px[x, y]
            if a < 40:
                continue
            if not (r > 168 and g > 168 and b > 168):
                col_has_hair[x] = True
                break
    xs = [i for i, ok in enumerate(col_has_hair) if ok]
    if len(xs) < 8:
        return face
    pad = 2
    return rgba.crop((max(0, xs[0] - pad), 0, min(w, xs[-1] + pad + 1), h))


def _crop_face_reference(img: Image.Image) -> Image.Image:
    """Center headshot crop from approved portrait or outro card."""
    img = img.convert("RGBA")
    w, h = img.size
    cx, cy = w // 2, int(h * 0.36)
    side = int(min(w, h) * 0.48)
    left = max(0, cx - side // 2 + int(side * 0.1))
    top = max(0, cy - int(side * 0.58))
    right = min(w, left + side)
    bottom = min(h, top + side)
    face = img.crop((left, top, right, bottom))
    face = _trim_portrait_white_edges(face)
    return face.resize((188, 208), Image.Resampling.LANCZOS)


def _face_oval_mask(size: tuple[int, int], *, wide: bool = False) -> Image.Image:
    mask = Image.new("L", size, 0)
    inset = 1 if wide else 4
    ImageDraw.Draw(mask).ellipse(
        [inset, inset, size[0] - inset - 1, size[1] - inset - 1],
        fill=255,
    )
    return mask.filter(ImageFilter.GaussianBlur(1.0 if wide else 1.2))


def _clear_cyborg_face_tile(head: Image.Image) -> Image.Image:
    """Neutral skin base so portrait maps cleanly (no orange visor scanlines)."""
    head = head.copy()
    draw = ImageDraw.Draw(head)
    skin = (222, 194, 174, 255)
    draw.ellipse([28, 28, 228, 220], fill=skin)
    draw.rectangle([44, 108, 212, 206], fill=skin)
    draw.rectangle([72, 138, 184, 156], fill=skin)
    return head


def _resolve_portrait_references(spec: dict | None, run_dir: Path | None) -> list[Path]:
    spec = spec or {}
    refs: list[Path] = []
    try:
        from envkit_paths import approved_dir

        approved = approved_dir()
    except ImportError:
        approved = Path(__file__).resolve().parents[4]

    for key in ("botAvatarPortraitPath", "portraitImage"):
        raw = spec.get(key)
        if not raw:
            continue
        p = Path(str(raw))
        if not p.is_absolute():
            for root in (run_dir, approved):
                if root is None:
                    continue
                cand = root / p.name if key == "portraitImage" else root / p
                if cand.is_file():
                    p = cand
                    break
        if p.is_file():
            refs.append(p)

    for name in ("DemoRecapPortrait.png", "_approval_portrait.png", "ApprovedOutro.png"):
        for root in (run_dir, approved):
            if root is None:
                continue
            cand = root / name
            if cand.is_file() and cand not in refs:
                refs.append(cand)
    return refs


def build_portrait_cyborg_skin(
    *,
    out_path: Path,
    spec: dict | None = None,
    run_dir: Path | None = None,
) -> Path | None:
    """Blend approved portrait/outro face onto Kenney cyborg skin for reuse across recaps."""
    spec = spec or {}
    if not spec.get("botAvatarPortraitSkin", True):
        return None
    base_path = Path(str(spec.get("botAvatarBaseSkin") or _KENNEY_BASE_SKIN))
    if not base_path.is_file():
        print(f"WARNING: Kenney base skin missing → {base_path}", flush=True)
        return None

    reuse_key = str(spec.get("botAvatarReuseKey") or "jacob-adkins-portrait-v1")
    try:
        from envkit_paths import approved_dir

        reuse_dir = approved_dir() / "JacobAdkinsBot-Avatar"
    except ImportError:
        reuse_dir = out_path.parent / "JacobAdkinsBot-Avatar"
    cached = reuse_dir / f"{reuse_key}-skin.png"
    if not spec.get("botAvatarForceSkinRebuild", False) and cached.is_file() and cached.stat().st_size > 4096:
        try:
            shutil.copy2(cached, out_path)
            return out_path
        except OSError:
            pass

    refs = _resolve_portrait_references(spec, run_dir)
    if not refs:
        print("WARNING: No portrait/outro reference for cyborg skin.", flush=True)
        return None
    refs.sort(key=lambda p: (0 if "portrait" in p.name.lower() else 1, p.name))

    base = Image.open(base_path).convert("RGBA")
    head = _clear_cyborg_face_tile(base.crop(_KENNEY_HEAD_FRONT_BOX))
    blended = head.copy()
    for ref in refs[:2]:
        try:
            face = _crop_face_reference(Image.open(ref))
        except OSError as exc:
            print(f"WARNING: portrait skin ref skipped ({ref.name}): {exc}", flush=True)
            continue
        if ref.name.lower().startswith("approvedoutro"):
            face = face.crop((int(face.width * 0.12), 0, face.width, face.height))
        is_portrait = "portrait" in ref.name.lower()
        alpha = 0.62 if is_portrait else 0.16
        px, py = 34, 24
        mask = _face_oval_mask(face.size)
        layer = Image.new("RGBA", head.size, (0, 0, 0, 0))
        layer.paste(face, (px, py), mask)
        blended = Image.alpha_composite(blended, Image.blend(Image.new("RGBA", head.size, (0, 0, 0, 0)), layer, alpha=alpha))

    blended = _paint_jacob_bot_face(blended)

    base.paste(blended, _KENNEY_HEAD_FRONT_BOX[:2])
    out_path.parent.mkdir(parents=True, exist_ok=True)
    base.save(out_path)
    reuse_dir.mkdir(parents=True, exist_ok=True)
    try:
        shutil.copy2(out_path, cached)
    except OSError:
        pass
    print(f"Portrait + host smile skin → {out_path} (reuse key {reuse_key})", flush=True)
    return out_path


def _paint_jacob_bot_face(head: Image.Image) -> Image.Image:
    """Warm host smile at Kenney mouth UV — same mouth size, no enlargement."""
    head = head.copy()
    draw = ImageDraw.Draw(head)
    skin = (222, 194, 174, 255)
    lip = (156, 88, 72, 255)
    mouth_cx, mouth_cy = _KENNEY_MOUTH_UV
    draw.rectangle([mouth_cx - 38, mouth_cy - 14, mouth_cx + 38, mouth_cy + 12], fill=skin)
    draw.ellipse([mouth_cx - 14, mouth_cy - 6, mouth_cx + 14, mouth_cy + 7], fill=skin)
    draw.arc(
        [mouth_cx - 15, mouth_cy - 8, mouth_cx + 15, mouth_cy + 4],
        start=198,
        end=342,
        fill=lip,
        width=4,
    )
    draw.arc(
        [mouth_cx - 11, mouth_cy - 3, mouth_cx + 11, mouth_cy + 5],
        start=205,
        end=335,
        fill=(176, 102, 82, 255),
        width=2,
    )
    return head


def _paint_host_smile_mouth(head: Image.Image) -> Image.Image:
    return _paint_jacob_bot_face(head)


def build_host_smile_skin(*, out_path: Path, spec: dict | None = None) -> Path | None:
    """Friendly game-show host smile on Kenney cyborg (fallback when portrait refs missing)."""
    spec = spec or {}
    if not spec.get("botAvatarHostSmile", True):
        return None
    base_path = Path(str(spec.get("botAvatarBaseSkin") or _KENNEY_BASE_SKIN))
    if not base_path.is_file():
        return None

    reuse_key = str(spec.get("botAvatarReuseKey") or "gameshow-host-v1")
    try:
        from envkit_paths import approved_dir

        cached = approved_dir() / "JacobAdkinsBot-Avatar" / f"{reuse_key}-skin.png"
    except ImportError:
        cached = out_path.parent / f"{reuse_key}-skin.png"
    if cached.is_file() and cached.stat().st_size > 4096:
        shutil.copy2(cached, out_path)
        return out_path

    base = Image.open(base_path).convert("RGBA")
    head = _paint_host_smile_mouth(base.crop(_KENNEY_HEAD_FRONT_BOX))
    base.paste(head, _KENNEY_HEAD_FRONT_BOX[:2])
    out_path.parent.mkdir(parents=True, exist_ok=True)
    base.save(out_path)
    try:
        cached.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(out_path, cached)
    except OSError:
        pass
    print(f"Game-show host smile skin → {out_path}", flush=True)
    return out_path


def _prepare_unity_host_skin(work: Path, spec: dict | None, run_dir: Path | None) -> str | None:
    spec = spec or {}
    if run_dir is None:
        run_dir = work.parent.parent if work.parent.name.startswith("_presentation") else work.parent

    built: Path | None = None
    skin_out = work / "portrait-host-skin.png"
    if spec.get("botAvatarPortraitSkin", True):
        built = build_portrait_cyborg_skin(out_path=skin_out, spec=spec, run_dir=run_dir)
    if built is None and spec.get("botAvatarHostSmile", True):
        skin_out = work / "host-smile-skin.png"
        built = build_host_smile_skin(out_path=skin_out, spec=spec)
    if built is None:
        return None
    hub_skin = (
        Path(__file__).resolve().parents[4]
        / "Assets/EnvironmentKit/Generated/RecapBotHostSkin.png"
    )
    hub_skin.parent.mkdir(parents=True, exist_ok=True)
    try:
        shutil.copy2(built, hub_skin)
    except OSError as exc:
        print(f"WARNING: Hub skin copy skipped: {exc}", flush=True)
        return str(built.resolve())
    kenney_default = (
        Path(__file__).resolve().parents[4]
        / "Assets/EnvironmentKit/CC0Imports/kenney-animated-characters-2/Skins/cyborgMaleA.png"
    )
    if kenney_default.parent.is_dir():
        try:
            shutil.copy2(built, kenney_default)
        except OSError as exc:
            print(f"WARNING: Kenney default skin sync skipped: {exc}", flush=True)
    return str(hub_skin.resolve())


def _is_white_fringe_pixel(r: int, g: int, b: int, a: int) -> bool:
    return a > 32 and r > 172 and g > 172 and b > 172


def _is_skin_halo_pixel(r: int, g: int, b: int, a: int) -> bool:
    if a <= 32:
        return False
    return abs(r - 214) < 28 and abs(g - 186) < 28 and abs(b - 168) < 28


def _is_cyborg_fringe_pixel(r: int, g: int, b: int, a: int) -> bool:
    if a <= 32:
        return False
    if _is_white_fringe_pixel(r, g, b, a):
        return True
    if _is_skin_halo_pixel(r, g, b, a):
        return True
    if g > 130 and b > 120 and r < 210 and (g - r) > 8:
        return True
    return False


def _character_alpha_bbox(
    im: Image.Image,
    *,
    exclude_white_fringe: bool = False,
) -> tuple[int, int, int, int] | None:
    rgba = im.convert("RGBA")
    w, h = rgba.size
    xs: list[int] = []
    ys: list[int] = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = rgba.getpixel((x, y))
            if a <= 48:
                continue
            if exclude_white_fringe and (
                _is_white_fringe_pixel(r, g, b, a)
                or _is_skin_halo_pixel(r, g, b, a)
            ):
                continue
            xs.append(x)
            ys.append(y)
    if not xs:
        return None
    return min(xs), min(ys), max(xs), max(ys)


def _prepare_bot_overlay_tile(bot: Image.Image, spec: dict | None) -> Image.Image:
    """Crop Unity padding and scale the host to the configured on-screen height."""
    spec = spec or {}
    desired_h = _avatar_overlay_scale(spec)
    rgba = bot.convert("RGBA")
    bbox = _character_alpha_bbox(rgba, exclude_white_fringe=True)
    if bbox is None:
        bbox = _character_alpha_bbox(rgba)
    if bbox is None:
        return rgba.resize((desired_h, desired_h), Image.Resampling.LANCZOS)
    x0, y0, x1, y1 = bbox
    pad = int(spec.get("botAvatarOverlayCropPad") or 12)
    crop = rgba.crop((
        max(0, x0 - pad),
        max(0, y0 - pad),
        min(rgba.width, x1 + pad),
        min(rgba.height, y1 + pad),
    ))
    cw, ch = crop.size
    if ch <= 0:
        return crop
    scale = desired_h / ch
    new_w = max(1, int(cw * scale))
    return crop.resize((new_w, desired_h), Image.Resampling.LANCZOS)


def _trim_overlay_sequence(seq: Path, spec: dict | None) -> None:
    """Replace padded Unity frames with tight bottom-right-ready overlay tiles."""
    for path in sorted(seq.glob("f_*.png")):
        try:
            tile = _prepare_bot_overlay_tile(Image.open(path), spec)
        except OSError:
            continue
        tile.save(path)


def _portrait_face_width_stretch(spec: dict | None) -> float:
    spec = spec or {}
    try:
        return max(1.0, float(spec.get("botAvatarPortraitFaceWidth", 2.0) or 2.0))
    except (TypeError, ValueError):
        return 2.0


def _portrait_face_height_stretch(spec: dict | None) -> float:
    spec = spec or {}
    try:
        return max(1.0, float(spec.get("botAvatarPortraitFaceHeight", 1.15) or 1.15))
    except (TypeError, ValueError):
        return 1.15


def _portrait_mouth_x_offset(spec: dict | None) -> int:
    spec = spec or {}
    try:
        return int(spec.get("botAvatarPortraitMouthXOffset", 0) or 0)
    except (TypeError, ValueError):
        return 0


def _portrait_mouth_x_fraction(spec: dict | None) -> float:
    """Shift mouth from face center as a fraction of face width (negative = left)."""
    spec = spec or {}
    try:
        return float(spec.get("botAvatarPortraitMouthXFraction", 0) or 0)
    except (TypeError, ValueError):
        return 0.0


def _portrait_mouth_y_ratio(spec: dict | None) -> float:
    """Mouth row in stretched portrait crop (0=top, 1=chin)."""
    spec = spec or {}
    try:
        return max(0.55, min(0.9, float(spec.get("botAvatarPortraitMouthY", 0.77) or 0.77)))
    except (TypeError, ValueError):
        return 0.77


def _erase_cyborg_head_before_portrait(
    im: Image.Image,
    head: tuple[int, int, int, int],
) -> None:
    """Hide Kenney head mesh so it cannot show through portrait edges."""
    hx0, hy0, hx1, hy1 = head
    hw, hh = hx1 - hx0, hy1 - hy0
    pad = max(10, int(max(hw, hh) * 0.18))
    draw = ImageDraw.Draw(im)
    skin = (214, 186, 168, 255)
    draw.ellipse(
        [hx0 - pad, hy0 - pad, hx1 + pad, hy1 + pad],
        fill=skin,
    )


def _strip_fringe_around_portrait(
    im: Image.Image,
    paste_x: int,
    paste_y: int,
    face_w: int,
    face_h: int,
    portrait_mask: Image.Image,
) -> Image.Image:
    """Remove halo pixels in a tight ring around the portrait — never body/arms."""
    rgba = im.convert("RGBA")
    ring = max(4, int(min(face_w, face_h) * 0.06))
    px = rgba.load()
    mask_px = portrait_mask.load()
    y0 = max(0, paste_y - ring)
    y1 = min(rgba.height, paste_y + face_h + ring)
    x0 = max(0, paste_x - ring)
    x1 = min(rgba.width, paste_x + face_w + ring)
    for y in range(y0, y1):
        for x in range(x0, x1):
            mx, my = x - paste_x, y - paste_y
            if (
                0 <= mx < face_w
                and 0 <= my < face_h
                and mask_px[mx, my] > 48
            ):
                continue
            near_edge = False
            for dy in range(-ring, ring + 1):
                for dx in range(-ring, ring + 1):
                    nx, ny = mx + dx, my + dy
                    if (
                        0 <= nx < face_w
                        and 0 <= ny < face_h
                        and mask_px[nx, ny] > 48
                    ):
                        near_edge = True
                        break
                if near_edge:
                    break
            if not near_edge:
                continue
            r, g, b, a = px[x, y]
            hood_only = my < int(face_h * 0.55)
            if _is_white_fringe_pixel(r, g, b, a) or _is_skin_halo_pixel(r, g, b, a):
                px[x, y] = (0, 0, 0, 0)
            elif hood_only and g > 130 and b > 120 and r < 210 and (g - r) > 8:
                px[x, y] = (0, 0, 0, 0)
    return rgba


def _head_box(bbox: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    x0, y0, x1, y1 = bbox
    bw, bh = x1 - x0, y1 - y0
    head_h = max(20, int(bh * 0.46))
    head_w = max(20, int(bw * 0.56))
    cx = (x0 + x1) // 2
    top = y0 + int(bh * 0.01)
    return cx - head_w // 2, top, cx + head_w // 2, top + head_h


def _portrait_mouth_width_scale(spec: dict | None) -> float:
    spec = spec or {}
    try:
        return max(0.35, min(1.0, float(spec.get("botAvatarPortraitMouthWidth", 0.67) or 0.67)))
    except (TypeError, ValueError):
        return 0.67


def _draw_portrait_mouth_at(
    draw: ImageDraw.ImageDraw,
    mx: int,
    my: int,
    amp: float,
    *,
    head_w: int,
    mouth_w_scale: float = 0.67,
) -> None:
    """Lip-sync mouth overlay — anchor (mx, my) is the upper lip line; do not shift."""
    talk = max(0.0, min(1.0, amp**0.48))
    lip = (132, 72, 58, 230)
    smile_w = max(6, int(head_w * 0.14 * mouth_w_scale))
    if talk < 0.09:
        draw.arc(
            [mx - smile_w, my - 4, mx + smile_w, my + 3],
            212,
            328,
            fill=lip,
            width=2,
        )
        return
    # Open speech only: hairline nudge onto portrait lips (closed smile keeps mx/my).
    lip_lift = max(2, int(head_w * 0.021))
    lip_shift_x = max(2, int(head_w * 0.014))
    ox = mx - lip_shift_x
    lip_line = my - lip_lift
    # Slimmer speech slit — wide enough to read, not a tall shout.
    max_open_h = max(3, int(head_w * 0.036))
    open_h = max(2, int(1 + talk * max_open_h))
    open_w = max(7, int(head_w * 0.12 * mouth_w_scale * (0.5 + 0.4 * talk)))
    open_w = max(open_w, int(open_h * 1.75))
    y0, y1 = lip_line, lip_line + open_h
    # Cyborg mouth — soft teal glow (matches bot accent), not brown flesh.
    glow_outer = (24, 190, 255, 75)
    glow_core = (48, 220, 255, 155)
    glow_hot = (160, 245, 255, 200)
    lip_robot = (90, 230, 250, 215)
    draw.ellipse(
        [ox - int(open_w * 1.06), y0 - 1, ox + int(open_w * 1.06), y1 + 2],
        fill=glow_outer,
    )
    draw.ellipse(
        [ox - open_w, y0, ox + open_w, y1],
        fill=glow_core,
    )
    if talk > 0.28:
        draw.ellipse(
            [
                ox - int(open_w * 0.5),
                y0 + open_h // 3,
                ox + int(open_w * 0.5),
                y1 - max(1, open_h // 4),
            ],
            fill=glow_hot,
        )
    draw.arc(
        [ox - open_w, y0 - 1, ox + open_w, y0 + max(2, open_h // 2)],
        200,
        340,
        fill=lip_robot,
        width=max(1, open_h // 3),
    )
    if talk > 0.38:
        draw.arc(
            [ox - int(open_w * 0.88), y0 + open_h // 3, ox + int(open_w * 0.88), y1 + 1],
            25,
            155,
            fill=lip_robot,
            width=max(1, open_h // 5),
        )


def _apply_jacob_portrait_on_frame(
    frame_path: Path,
    amp: float,
    *,
    spec: dict | None = None,
    run_dir: Path | None = None,
) -> None:
    """Portrait stretched 1.5× wide on head; lip-sync anchored to your mouth."""
    refs = _resolve_portrait_references(spec, run_dir)
    if not refs:
        return
    try:
        portrait = _crop_face_reference(Image.open(refs[0]))
        im = Image.open(frame_path).convert("RGBA")
    except OSError:
        return
    bbox = _character_alpha_bbox(im)
    if bbox is None:
        return
    head = _head_box(bbox)
    hx0, hy0, hx1, hy1 = head
    hw, hh = hx1 - hx0, hy1 - hy0
    _erase_cyborg_head_before_portrait(im, head)
    stretch_w = _portrait_face_width_stretch(spec)
    stretch_h = _portrait_face_height_stretch(spec)
    face_w = max(hw, int(hw * stretch_w))
    face_h = max(hh, int(hh * stretch_h))
    face = portrait.resize((face_w, face_h), Image.Resampling.LANCZOS)
    paste_x = hx0 - (face_w - hw) // 2 + max(0, int(hw * 0.05))
    paste_y = hy0 - (face_h - hh) // 2 + max(0, int(hh * 0.04))
    mask = _face_oval_mask((face_w, face_h), wide=True)
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    layer.paste(face, (paste_x, paste_y), mask)
    im = Image.alpha_composite(im, layer)
    im = _strip_fringe_around_portrait(
        im, paste_x, paste_y, face_w, face_h, mask,
    )
    draw = ImageDraw.Draw(im)
    mouth_ratio = _portrait_mouth_y_ratio(spec)
    mx = (
        paste_x
        + face_w // 2
        + int(face_w * _portrait_mouth_x_fraction(spec))
        + _portrait_mouth_x_offset(spec)
    )
    my = paste_y + int(face_h * mouth_ratio)
    _draw_portrait_mouth_at(
        draw,
        mx,
        my,
        amp,
        head_w=face_w,
        mouth_w_scale=_portrait_mouth_width_scale(spec),
    )
    im.save(frame_path)


def _postprocess_host_frames(
    seq: Path,
    envelope: list[float],
    *,
    spec: dict | None = None,
    run_dir: Path | None = None,
) -> None:
    for fi, amp in enumerate(envelope):
        path = seq / f"f_{fi:04d}.png"
        if path.is_file():
            _apply_jacob_portrait_on_frame(
                path, amp, spec=spec, run_dir=run_dir,
            )


def _render_procedural_lipsync_frames(
    envelope: list[float],
    frame_count: int,
    seq: Path,
    seq_back: Path,
    seq_mid: Path,
    seq_front: Path,
) -> None:
    for fi in range(frame_count):
        amp = envelope[fi] if fi < len(envelope) else 0.0
        jaw = min(1.0, amp ** 0.72 * 1.15)
        layers = _render_bot_layers(fi, frame_count, jaw_open=jaw, amp=amp)
        layers["composite"].save(seq / f"f_{fi:04d}.png")
        layers["back"].save(seq_back / f"f_{fi:04d}.png")
        layers["mid"].save(seq_mid / f"f_{fi:04d}.png")
        layers["front"].save(seq_front / f"f_{fi:04d}.png")


def _hub_unity_editor_running() -> bool:
    """True when the Hub Unity Editor binary is running (not just Hub app or a stale lockfile)."""
    try:
        result = subprocess.run(
            ["pgrep", "-f", "Unity.app/Contents/MacOS/Unity"],
            check=False,
            capture_output=True,
            text=True,
        )
    except OSError:
        return False
    return result.returncode == 0 and bool((result.stdout or "").strip())


def _hub_unity_project_locked() -> bool:
    hub = Path(__file__).resolve().parents[4]
    lock = hub / "Temp" / "UnityLockfile"
    return lock.is_file() and _hub_unity_editor_running()


def _unity_batch_output_locked(output: str) -> bool:
    low = (output or "").lower()
    return "another unity instance" in low or "multiple unity instances" in low


def _render_unity_chunk_batch(
    script: Path,
    env_json: Path,
    seq: Path,
    log_path: Path,
    env: dict[str, str],
    offset: int,
) -> bool:
    chunk_env = {**env, "RECAP_BOT_FRAME_OFFSET": str(offset)}
    try:
        result = subprocess.run(
            ["bash", str(script), str(env_json), str(seq), str(log_path)],
            check=False,
            capture_output=True,
            text=True,
            env=chunk_env,
            timeout=3600,
        )
    except OSError as exc:
        print(f"WARNING: Unity recap bot avatar skipped: {exc}", flush=True)
        return False
    except subprocess.TimeoutExpired:
        print(f"WARNING: Unity batch timed out at offset {offset}", flush=True)
        return False

    if result.returncode == 0:
        return True

    if _unity_batch_output_locked((result.stderr or "") + (result.stdout or "")):
        print("Unity batch blocked (project open) — will try live Editor…", flush=True)
        return False

    tail = (result.stderr or result.stdout or "").strip()[-600:]
    print(
        f"Unity batch failed at offset {offset} ({result.returncode}) — "
        f"will try live Editor if available…\n{tail}",
        flush=True,
    )
    return False


def _render_unity_chunk_via_live_editor(
    env_json: Path,
    seq: Path,
    offset: int,
    *,
    skin_path: str | None = None,
    timeout_sec: float = 420.0,
) -> bool:
    """Render via open Hub Editor — avoids batchmode compile/OOM."""
    from envkit_paths import unity_recap_scratch_dir

    scratch = unity_recap_scratch_dir()
    req_path = scratch / "recap-bot-avatar-request.json"
    done_path = scratch / "recap-bot-avatar-done.json"
    proc_path = scratch / "recap-bot-avatar-request.processing"
    for p in (done_path, proc_path):
        try:
            p.unlink(missing_ok=True)
        except OSError:
            pass

    req_id = str(uuid.uuid4())
    skin = skin_path or os.environ.get("RECAP_BOT_SKIN_PATH") or ""
    if skin:
        try:
            from envkit_paths import unity_recap_scratch_dir

            sidecar = unity_recap_scratch_dir() / "recap-bot-skin-path.txt"
            sidecar.parent.mkdir(parents=True, exist_ok=True)
            sidecar.write_text(skin.strip() + "\n", encoding="utf-8")
        except ImportError:
            pass
    payload = {
        "id": req_id,
        "envelopeJson": str(env_json.resolve()),
        "frameDir": str(seq.resolve()),
        "frameOffset": int(offset),
        "width": int(os.environ.get("RECAP_BOT_WIDTH", "320") or 320),
        "skinPath": skin,
    }
    req_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(
        f"Unity cyborg: live Editor request offset {offset} "
        f"(keep Hub Unity open on this project)…",
        flush=True,
    )

    deadline = time.monotonic() + timeout_sec
    while time.monotonic() < deadline:
        if done_path.is_file():
            try:
                done = json.loads(done_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                done = {}
            if str(done.get("id")) == req_id:
                ok = bool(done.get("ok"))
                msg = str(done.get("message") or "")
                if msg:
                    print(msg, flush=True)
                return ok
        time.sleep(1.0)

    print(
        "ERROR: Unity live Editor avatar timed out — is Hub Unity open on /Users/jacob/Hub?",
        file=sys.stderr,
    )
    return False


def _render_unity_cyborg_lipsync_frames(
    envelope: list[float],
    fps: int,
    seq: Path,
    work: Path,
    spec: dict | None = None,
) -> bool:
    unity = _resolve_unity_executable()
    script = Path(__file__).resolve().parents[1] / "run-recap-bot-avatar.sh"
    if unity is None or not script.is_file():
        return False

    spec = spec or {}
    chunk_frames = int(spec.get("botAvatarUnityChunkFrames", 450))
    chunk_frames = max(60, min(chunk_frames, len(envelope)))

    from envkit_paths import ensure_recap_process_env, mac_data_volume_free_gb, unity_recap_scratch_dir

    data_root = ensure_recap_process_env()
    free_gb = mac_data_volume_free_gb()
    if free_gb is not None and free_gb < 2.0:
        print(
            f"WARNING: Mac internal disk low ({free_gb:.1f} GiB) — Unity batch still loads "
            f"Hub project on internal SSD; free space or run migrate-envkit-to-external.sh",
            flush=True,
        )
    log_path = unity_recap_scratch_dir() / f"unity-recap-bot-{work.parent.name}.log"
    print(f"Recap storage: {data_root} (TMPDIR={os.environ.get('TMPDIR')})", flush=True)
    print(
        f"Rendering Unity male cyborg avatar ({len(envelope)} frames @ {fps} fps, "
        f"chunks of {chunk_frames})…",
        flush=True,
    )

    env = os.environ.copy()
    env["RECAP_BOT_WIDTH"] = str(int((spec or {}).get("botAvatarRenderWidth") or 640))
    run_dir = work.parent.parent if work.parent.name.startswith("_presentation") else work.parent
    skin_path = _prepare_unity_host_skin(work, spec, run_dir)
    if skin_path:
        env["RECAP_BOT_SKIN_PATH"] = skin_path
    prefer_live = _hub_unity_project_locked()
    if prefer_live:
        print(
            "Hub Unity project is open — using live Editor avatar path (no batchmode).",
            flush=True,
        )

    for offset in range(0, len(envelope), chunk_frames):
        chunk = envelope[offset : offset + chunk_frames]
        env_json = work / f"envelope_{offset:05d}.json"
        point_frames = _resolve_point_gesture_frames(run_dir, fps, len(envelope))
        env_json.write_text(
            json.dumps({"envelope": chunk, "fps": fps, "pointFrames": point_frames})
            + "\n",
            encoding="utf-8",
        )
        if _hub_unity_editor_running():
            chunk_ok = _render_unity_chunk_via_live_editor(
                env_json, seq, offset, skin_path=env.get("RECAP_BOT_SKIN_PATH")
            )
            if not chunk_ok:
                print("Live Editor failed — trying Unity batch…", flush=True)
                chunk_ok = _render_unity_chunk_batch(
                    script, env_json, seq, log_path, env, offset
                )
        else:
            chunk_ok = _render_unity_chunk_batch(
                script, env_json, seq, log_path, env, offset
            )
            if not chunk_ok:
                print(
                    "Unity batch failed — open /Users/jacob/Hub in Unity Editor and retry.",
                    flush=True,
                )

        if not chunk_ok:
            return False

        got = len(list(seq.glob("f_*.png")))
        print(f"Unity cyborg chunk OK: offset {offset}, {len(chunk)} frames (total png {got})", flush=True)

    expected = len(envelope)
    png_count = len(list(seq.glob("f_*.png")))
    if png_count < expected * 0.98:
        print(
            f"WARNING: Unity recap bot avatar incomplete ({png_count}/{expected} frames).",
            flush=True,
        )
        return False

    print(f"Post-processing host mouth overlays ({len(envelope)} frames)…", flush=True)
    _postprocess_host_frames(seq, envelope, spec=spec, run_dir=run_dir)
    print("Trimming avatar tiles for bottom-right overlay…", flush=True)
    _trim_overlay_sequence(seq, spec)
    return True


def generate_lipsync_avatar_for_narration(
    narration_wav: Path,
    base_video: Path,
    out_video: Path,
    ffmpeg: str,
    ffprobe: str,
    *,
    layer_export: Path | None = None,
    spec: dict | None = None,
) -> bool:
    """Render lip-sync avatar from narration and overlay on video."""
    from hybrid_recap_common import probe_duration

    if not narration_wav.is_file() or not base_video.is_file():
        return False
    dur = probe_duration(base_video, ffprobe=ffprobe)
    fps = int(round(_probe_video_fps(base_video, ffprobe)))
    spec_fps = (spec or {}).get("botAvatarLipSyncFps")
    if spec_fps is not None:
        try:
            fps = max(6, min(24, int(spec_fps)))
        except (TypeError, ValueError):
            pass
    elif dur > 600:
        fps = min(fps, 10)
    elif dur > 180:
        fps = min(fps, 10)
    frame_count = max(1, int(math.ceil(dur * fps)))

    print(f"Rendering lip-sync avatar ({frame_count} frames @ {fps} fps)…", flush=True)
    envelope = _audio_envelope(narration_wav, fps, frame_count)

    work = out_video.parent / "_bot_lipsync"
    if work.is_dir():
        shutil.rmtree(work, ignore_errors=True)
    work.mkdir(parents=True, exist_ok=True)
    seq = work / "composite"
    seq_back = work / "layer_back"
    seq_mid = work / "layer_mid"
    seq_front = work / "layer_front"
    for d in (seq, seq_back, seq_mid, seq_front):
        d.mkdir(parents=True, exist_ok=True)

    source = _bot_avatar_source(spec)
    require_unity = bool(spec.get("botAvatarRequireUnity", False))
    allow_fallback = bool(spec.get("botAvatarAllowProceduralFallback", not require_unity))
    used_unity = False
    if source in ("unity", "cyborg", "kenney", "3d"):
        used_unity = _render_unity_cyborg_lipsync_frames(envelope, fps, seq, work, spec)
    if not used_unity:
        if require_unity or not allow_fallback:
            print(
                "ERROR: Unity cyborg avatar required — procedural fallback disabled.",
                file=sys.stderr,
            )
            return False
        if source not in ("procedural", "2d", "pil"):
            print("Falling back to procedural layered bot avatar.", flush=True)
        _render_procedural_lipsync_frames(
            envelope, frame_count, seq, seq_back, seq_mid, seq_front,
        )

    lipsync_mov = work / f"{LIPSYNC_BASENAME}.mov"
    _encode_png_sequence(seq, "f_%04d.png", fps, lipsync_mov, ffmpeg)
    manifest = {
        "name": BOT_NAME,
        "avatarEngine": "unity" if used_unity else "procedural",
        "botAvatarSource": _bot_avatar_source(spec),
        "botAvatarModel": str((spec or {}).get("botAvatarModel") or UNITY_CYBORG_MODEL),
        "renderVersion": AVATAR_RENDER_VERSION,
        "frameCount": frame_count,
        "fps": fps,
        "durationSec": dur,
    }
    manifest_path = work / f"{LIPSYNC_MANIFEST_BASENAME}.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    if layer_export and not used_unity:
        xr_dir = layer_export / "XR-Layers"
        xr_dir.mkdir(parents=True, exist_ok=True)
        for name, sdir in (("Layer-Back", seq_back), ("Layer-Mid", seq_mid), ("Layer-Front", seq_front)):
            layer_mov = xr_dir / f"{name}.mov"
            try:
                _encode_png_sequence(sdir, "f_%04d.png", fps, layer_mov, ffmpeg)
            except RuntimeError as exc:
                print(f"WARNING: XR layer export skipped ({name}): {exc}", flush=True)
                break
        else:
            manifest = {
                "name": BOT_NAME,
                "renderVersion": AVATAR_RENDER_VERSION,
                "layers": [
                    {"id": "back", "file": "Layer-Back.mov", **DEPTH["back"]},
                    {"id": "mid", "file": "Layer-Mid.mov", **DEPTH["mid"]},
                    {"id": "front", "file": "Layer-Front.mov", **DEPTH["front"]},
                ],
                "usage": "Composite in XR/VR with parallax offsets — front layer toward viewer",
                "lipSyncSource": str(narration_wav),
            }
            (xr_dir / "layers-manifest.json").write_text(
                json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
            )
            print(f"XR depth layers exported → {xr_dir}", flush=True)
        try:
            shutil.copy2(lipsync_mov, layer_export / f"{LIPSYNC_BASENAME}.mov")
        except OSError as exc:
            print(f"WARNING: lip-sync mov copy skipped: {exc}", flush=True)

    apply_animated_avatar_to_video(ffmpeg, base_video, out_video, lipsync_mov, spec=spec)
    return out_video.is_file()


def generate_animated_avatar_loop(
    out_dir: Path,
    ffmpeg: str,
    *,
    speaking: bool = True,
) -> tuple[Path, Path]:
    """Idle loop asset (downloadable) — lip-sync version generated at mux."""
    out_dir.mkdir(parents=True, exist_ok=True)
    seq = out_dir / "_avatar_seq"
    if seq.is_dir():
        shutil.rmtree(seq, ignore_errors=True)
    seq.mkdir(parents=True, exist_ok=True)

    total = max(1, int(LOOP_SEC * LOOP_FPS))
    for fi in range(total):
        phase = fi / total * math.pi * 2
        jaw = 0.35 + 0.65 * abs(math.sin(phase * 6.5)) if speaking else 0.0
        amp = jaw
        fr = _render_bot_frame(fi, total, jaw_open=jaw, amp=amp)
        fr.save(seq / f"f_{fi:04d}.png")

    prores = out_dir / f"{ASSET_BASENAME}.mov"
    webm = out_dir / f"{ASSET_BASENAME}.webm"
    _encode_png_sequence(seq, "f_%04d.png", LOOP_FPS, prores, ffmpeg)
    subprocess.run(
        [
            ffmpeg, "-y", "-framerate", str(LOOP_FPS),
            "-i", str(seq / "f_%04d.png"),
            "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p", "-b:v", "800k",
            str(webm),
        ],
        check=True,
        capture_output=True,
    )

    meta = {
        "name": BOT_NAME,
        "label": BOT_LABEL,
        "tagline": BOT_TAGLINE,
        "renderVersion": AVATAR_RENDER_VERSION,
        "characterType": "kenney_cyborg_half_ai",
        "botAvatarSource": "unity",
        "botAvatarModel": UNITY_CYBORG_MODEL,
        "usesPortrait": False,
        "depthLayers": DEPTH,
        "loopSec": LOOP_SEC,
        "fps": LOOP_FPS,
        "proresMov": str(prores),
        "webm": str(webm),
        "xrLayersDir": "XR-Layers/",
        "speakingAnimation": speaking,
        "personalSpace": "circular lower-left overlay",
    }
    (out_dir / f"{ASSET_BASENAME}.json").write_text(
        json.dumps(meta, indent=2) + "\n", encoding="utf-8"
    )
    return prores, webm


def _avatar_is_stale(asset_dir: Path, prores: Path, webm: Path) -> bool:
    if not prores.is_file() or not webm.is_file():
        return True
    meta_path = asset_dir / f"{ASSET_BASENAME}.json"
    if not meta_path.is_file():
        return True
    try:
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return True
    if meta.get("renderVersion") != AVATAR_RENDER_VERSION:
        return True
    if meta.get("usesPortrait"):
        return True
    return False


def ensure_animated_avatar_asset(
    approved_dir: Path,
    ffmpeg: str,
    *,
    desktop_copy: Path | None = None,
    portrait_path: Path | None = None,
) -> Path:
    del portrait_path
    asset_dir = approved_dir / "JacobAdkinsBot-Avatar"
    prores = asset_dir / f"{ASSET_BASENAME}.mov"
    webm = asset_dir / f"{ASSET_BASENAME}.webm"
    if _avatar_is_stale(asset_dir, prores, webm):
        print(f"Generating 3D layered {BOT_NAME} avatar loop…", flush=True)
        generate_animated_avatar_loop(asset_dir, ffmpeg)

    overlay = prores if prores.is_file() else webm
    if desktop_copy:
        desktop_copy.mkdir(parents=True, exist_ok=True)
        for name in (
            f"{ASSET_BASENAME}.mov", f"{ASSET_BASENAME}.webm",
            f"{ASSET_BASENAME}.json", f"{LIPSYNC_BASENAME}.mov",
        ):
            src = asset_dir / name
            if src.is_file():
                shutil.copy2(src, desktop_copy / name)
        xr = asset_dir / "XR-Layers"
        if xr.is_dir():
            dest_xr = desktop_copy / "XR-Layers"
            dest_xr.mkdir(parents=True, exist_ok=True)
            for f in xr.iterdir():
                if f.is_file():
                    shutil.copy2(f, dest_xr / f.name)
        print(f"Bot avatar assets (download): {desktop_copy}", flush=True)
    return overlay


def _avatar_overlay_margin_x(spec: dict | None) -> int:
    spec = spec or {}
    for key in ("botAvatarOverlayMarginX", "botAvatarOverlayMargin"):
        if spec.get(key) is not None:
            try:
                return max(0, int(spec.get(key) or 0))
            except (TypeError, ValueError):
                break
    return 24


def _caption_bar_height_px(spec: dict | None) -> int:
    spec = spec or {}
    if spec.get("botAvatarCaptionBarPx") is not None:
        try:
            return max(0, int(spec["botAvatarCaptionBarPx"]))
        except (TypeError, ValueError):
            pass
    video_h = int(spec.get("botAvatarOverlayVideoHeight") or 720)
    return int(294 * video_h / 1080)


def _avatar_overlay_margin_y(spec: dict | None) -> int:
    spec = spec or {}
    if spec.get("botAvatarOverlayMarginY") is not None:
        try:
            return max(0, int(spec.get("botAvatarOverlayMarginY") or 0))
        except (TypeError, ValueError):
            pass
    if spec.get("botAvatarOverlayCaptionSafe", True):
        gap = int(spec.get("botAvatarOverlayCaptionGap") or 10)
        return _caption_bar_height_px(spec) + gap
    if spec.get("botAvatarOverlayOutroSafe", True):
        return 112
    return _avatar_overlay_margin_x(spec)


def _avatar_overlay_margin(spec: dict | None) -> int:
    return _avatar_overlay_margin_x(spec)


def _avatar_overlay_scale(spec: dict | None) -> int:
    spec = spec or {}
    try:
        video_h = int(spec.get("botAvatarOverlayVideoHeight") or 720)
        scale = int(spec.get("botAvatarOverlayScale", 240) or 240)
        floor = int(spec.get("botAvatarOverlayScaleMin") or 0)
        if floor > 0:
            scale = max(scale, floor)
        cap = int(spec.get("botAvatarOverlayScaleMax") or (video_h - 24))
        return max(96, min(scale, cap))
    except (TypeError, ValueError):
        return 240


def _avatar_overlay_corner(spec: dict | None) -> str:
    return str((spec or {}).get("botAvatarOverlayCorner") or "lower-right").lower().replace("_", "-")


def _avatar_overlay_xy(spec: dict | None, *, static: bool = False) -> tuple[str, str]:
    corner = _avatar_overlay_corner(spec)
    margin_x = _avatar_overlay_margin_x(spec)
    margin_y = _avatar_overlay_margin_y(spec)
    w_ref = "overlay_w" if not static else "w"
    h_ref = "overlay_h" if not static else "h"
    main_w = "W" if static else "main_w"
    main_h = "H" if static else "main_h"
    x = str(margin_x) if "left" in corner else f"{main_w}-{w_ref}-{margin_x}"
    y = str(margin_y) if "top" in corner else f"{main_h}-{h_ref}-{margin_y}"
    return x, y


def lipsync_cache_matches_spec(manifest_path: Path, spec: dict | None) -> bool:
    """Reject procedural lip-sync cache when Unity cyborg is requested."""
    spec = spec or {}
    if not manifest_path.is_file():
        return False
    try:
        meta = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return False
    if meta.get("renderVersion") != AVATAR_RENDER_VERSION:
        return False
    want = _bot_avatar_source(spec)
    got = str(meta.get("avatarEngine") or meta.get("botAvatarSource") or "procedural").lower()
    if want in ("unity", "cyborg", "kenney", "3d"):
        return got == "unity"
    return got == want or (want in ("procedural", "2d", "pil") and got == "procedural")


def _animated_avatar_overlay_filter(spec: dict | None, *, rgba: bool = True) -> str:
    scale = _avatar_overlay_scale(spec)
    x, y = _avatar_overlay_xy(spec, static=False)
    scale_filt = f"[1:v]scale=-1:{scale}"
    if rgba:
        scale_filt += ",format=rgba"
    return f"{scale_filt}[bot];[0:v][bot]overlay={x}:{y}:format=auto:shortest=1"


def apply_animated_avatar_to_video(
    ffmpeg: str,
    src: Path,
    dst: Path,
    avatar_video: Path,
    *,
    spec: dict | None = None,
) -> None:
    loop_input = str(avatar_video)
    filt = _animated_avatar_overlay_filter(spec, rgba=True)
    cmd = [
        ffmpeg, "-y",
        "-i", str(src),
        "-i", loop_input,
        "-filter_complex", filt,
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20",
        "-preset", "veryfast", "-an", str(dst),
    ]
    try:
        subprocess.run(cmd, check=True, capture_output=True)
    except subprocess.CalledProcessError:
        x, y = _avatar_overlay_xy(spec, static=False)
        scale = _avatar_overlay_scale(spec)
        subprocess.run(
            [
                ffmpeg, "-y", "-i", str(src), "-i", loop_input,
                "-filter_complex",
                f"[1:v]scale=-1:{scale},format=rgba[bot];"
                f"[0:v][bot]overlay={x}:{y}:format=auto:shortest=1",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20",
                "-preset", "veryfast", "-an", str(dst),
            ],
            check=True,
            capture_output=True,
        )


def build_avatar_overlay(out_png: Path, **kwargs) -> Path:
    out_png.parent.mkdir(parents=True, exist_ok=True)
    fr = _render_bot_frame(0, 1, jaw_open=0.4, amp=0.5)
    fr.save(out_png)
    return out_png


def apply_avatar_to_video(
    ffmpeg: str,
    src: Path,
    dst: Path,
    overlay: Path,
    *,
    spec: dict | None = None,
) -> None:
    if overlay.suffix.lower() in (".mov", ".webm", ".mp4"):
        apply_animated_avatar_to_video(ffmpeg, src, dst, overlay, spec=spec)
    else:
        x, y = _avatar_overlay_xy(spec, static=True)
        subprocess.run(
            [
                ffmpeg, "-y", "-i", str(src), "-i", str(overlay),
                "-filter_complex", f"[0:v][1:v]overlay={x}:{y}:format=auto",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20",
                "-preset", "veryfast", "-an", str(dst),
            ],
            check=True,
            capture_output=True,
        )


def _detect_intro_card_bottom(bg: Image.Image) -> int:
    """Bottom row of emerald intro border (y from top)."""
    rgb = bg.convert("RGB")
    w, h = rgb.size
    bottom = int(h * 0.74)
    for y in range(int(h * 0.42), h - 8):
        hits = 0
        for x in range(int(w * 0.06), int(w * 0.94), 4):
            r, g, b = rgb.getpixel((x, y))
            if g > 170 and g > r * 1.8 and g > b * 1.15:
                hits += 1
        if hits > (w * 0.12):
            bottom = y
    return bottom


def _fit_scale_below_intro_card(bg: Image.Image, spec: dict | None, desired: int) -> int:
    """Never shove avatar up onto the green line — shrink to fit the strip below it."""
    spec = spec or {}
    if not spec.get("botAvatarBelowIntroCard", True):
        return desired
    margin_y = _avatar_overlay_margin_y(spec)
    gap = int(spec.get("botAvatarIntroCardGap") or 6)
    card_bottom = _detect_intro_card_bottom(bg)
    available = bg.height - margin_y - card_bottom - gap
    if available < 48:
        return max(48, min(desired, available))
    return max(48, min(desired, available))


def _intro_card_bounds(bg: Image.Image) -> tuple[int, int, int, int]:
    """Emerald border outer bounds: left, top, right, bottom."""
    rgb = bg.convert("RGB")
    w, h = rgb.size
    minx, miny, maxx, maxy = w, h, 0, 0
    for y in range(h):
        for x in range(w):
            r, g, b = rgb.getpixel((x, y))
            if g > 170 and g > r * 1.8 and g > b * 1.15:
                minx = min(minx, x)
                miny = min(miny, y)
                maxx = max(maxx, x)
                maxy = max(maxy, y)
    return minx, miny, maxx, maxy


def _composite_bot_on_slide(bg: Image.Image, bot: Image.Image, spec: dict | None) -> Image.Image:
    """Crop host to visible pixels and pin bottom-right of the video frame (never the intro card)."""
    spec = spec or {}
    margin_x = _avatar_overlay_margin_x(spec)
    margin_y = _avatar_overlay_margin_y(spec)
    offset_x = int(spec.get("botAvatarOverlayOffsetX") or 0)
    offset_y = int(spec.get("botAvatarOverlayOffsetY") or 0)
    canvas = bg.convert("RGBA").copy()
    placement = str(spec.get("botAvatarOverlayPlacement") or "frame-corner").lower()
    tile_spec = dict(spec)
    desired = _avatar_overlay_scale(spec)

    if placement in ("frame-corner", "screen-corner"):
        tile_spec["botAvatarOverlayScale"] = desired
        bot_rgba = _prepare_bot_overlay_tile(bot, tile_spec)
        x = max(0, canvas.width - bot_rgba.width - margin_x + offset_x)
        y = max(0, canvas.height - bot_rgba.height - margin_y + offset_y)
    elif placement == "outside-green-corner":
        # Legacy alias — same as frame-corner (screen edge), not card-relative.
        tile_spec["botAvatarOverlayScale"] = desired
        bot_rgba = _prepare_bot_overlay_tile(bot, tile_spec)
        x = max(0, canvas.width - bot_rgba.width - margin_x + offset_x)
        y = max(0, canvas.height - bot_rgba.height - margin_y + offset_y)
    else:
        _card_l, _card_t, card_r, card_b = _intro_card_bounds(canvas)
        gap = int(spec.get("botAvatarIntroCardGap") or 8)
        avail_h = canvas.height - margin_y - card_b - gap
        avail_w = canvas.width - margin_x - card_r - gap
        strip_scale = min(desired, max(72, avail_h))
        if avail_w > 48:
            strip_scale = min(strip_scale, max(72, avail_w))
        tile_spec["botAvatarOverlayScale"] = strip_scale
        bot_rgba = _prepare_bot_overlay_tile(bot, tile_spec)
        x = max(0, canvas.width - bot_rgba.width - margin_x)
        y = max(0, card_b + gap)

    canvas.paste(bot_rgba, (x, y), bot_rgba)
    return canvas.convert("RGB")


def _resolve_point_gesture_frames(
    run_dir: Path | None,
    avatar_fps: int,
    frame_count: int,
) -> list[int]:
    """Avatar frame indices for game-show point-at-video on milestone holds."""
    if run_dir is None:
        return []
    manifest = run_dir / "HybridRecapManifest.json"
    if not manifest.is_file():
        return []
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
        segments = data.get("segments") or []
    except (json.JSONDecodeError, OSError):
        return []
    points: list[int] = []
    t = 0.0
    for seg in segments:
        kind = str(seg.get("kind") or seg.get("beatKind") or "").lower()
        if kind in ("hold", "checkpoint", "intro", "outro", "milestone"):
            fi = int(t * avatar_fps)
            if 0 <= fi < frame_count:
                points.append(fi)
        try:
            t += float(seg.get("durationSec") or 0.0)
        except (TypeError, ValueError):
            pass
    return sorted(set(points))


def generate_avatar_approval_preview(
    capture: Path,
    out_png: Path,
    spec: dict | None = None,
) -> bool:
    """Render 3 hero poses on intro slide — approve before full lip-sync bake."""
    spec = dict(spec or {})
    spec["botAvatarForceSkinRebuild"] = True
    spec.setdefault("botAvatarReuseKey", "jacobs-bot-approval-preview")

    try:
        from envkit_paths import approved_dir, ensure_recap_process_env

        ensure_recap_process_env()
        approved = approved_dir()
    except ImportError:
        approved = Path(__file__).resolve().parents[4] / "DemoRecapApproved"

    intro = None
    for cand in (
        approved / "ApprovedIntro.png",
        capture / "ApprovedIntro.png",
    ):
        if cand.is_file():
            intro = cand
            break
    if intro is None:
        print("ERROR: ApprovedIntro.png required for avatar approval preview.", file=sys.stderr)
        return False

    work = capture / "_bot_approval_preview"
    shutil.rmtree(work, ignore_errors=True)
    seq = work / "composite"
    seq.mkdir(parents=True, exist_ok=True)

    skin_path = _prepare_unity_host_skin(work, spec, capture)
    if skin_path:
        os.environ["RECAP_BOT_SKIN_PATH"] = skin_path
    os.environ["RECAP_BOT_WIDTH"] = str(
        int(spec.get("botAvatarRenderWidth") or 640)
    )
    spec["botAvatarPortraitSkin"] = False
    spec["botAvatarOverlayPlacement"] = "frame-corner"

    samples = (
        (0, 0.0, "REST — your face, friendly smile"),
        (50, 0.75, "TALKING — lip sync"),
        (127, 0.0, "POINT — arm at video"),
    )
    for _fi, (global_fi, amp, _label) in enumerate(samples):
        env_json = work / f"envelope_{global_fi:05d}.json"
        point_frames = [127] if global_fi == 127 else []
        env_json.write_text(
            json.dumps({"envelope": [amp], "fps": 15, "pointFrames": point_frames})
            + "\n",
            encoding="utf-8",
        )
        if not _render_unity_chunk_via_live_editor(
            env_json, seq, global_fi, skin_path=skin_path, timeout_sec=300.0
        ):
            print(
                "ERROR: Unity approval preview failed — open Hub Unity on /Users/jacob/Hub.",
                file=sys.stderr,
            )
            return False
        src = seq / f"f_{global_fi:04d}.png"
        if src.is_file():
            _apply_jacob_portrait_on_frame(
                src, amp, spec=spec, run_dir=capture,
            )

    bg = Image.open(intro).convert("RGB")
    if bg.size != (1280, 720):
        bg = bg.resize((1280, 720), Image.Resampling.LANCZOS)

    rest_path = seq / "f_0000.png"
    if not rest_path.is_file():
        print(f"ERROR: missing preview frame {rest_path}", file=sys.stderr)
        return False

    spec["botAvatarBelowIntroCard"] = False
    out_png.parent.mkdir(parents=True, exist_ok=True)
    hero = _composite_bot_on_slide(bg, Image.open(rest_path), spec)
    hero.save(out_png, quality=95)
    print(
        f"Video overlay scale {_avatar_overlay_scale(spec)}px "
        f"(~{round(_avatar_overlay_scale(spec) / 72)}× vs old intro speck)",
        flush=True,
    )

    face_proof = out_png.parent / "JacobsBot-FaceProof.png"
    rest_raw = Image.open(rest_path).convert("RGBA")
    bbox = _character_alpha_bbox(rest_raw, exclude_white_fringe=True)
    if bbox is None:
        bbox = _character_alpha_bbox(rest_raw)
    if bbox:
        x0, y0, x1, y1 = bbox
        pad = 20
        crop = rest_raw.crop((
            max(0, x0 - pad),
            max(0, y0 - pad),
            min(rest_raw.width, x1 + pad),
            min(rest_raw.height, y1 + pad),
        ))
        side = max(400, max(crop.width, crop.height))
        scaled = crop.resize((side, side), Image.Resampling.LANCZOS)
        proof = Image.new("RGB", (side, side), (8, 14, 20))
        proof.paste(scaled, (0, 0), scaled)
        proof.save(face_proof, quality=95)
        print(f"Face proof (your portrait on 3D host) → {face_proof}", flush=True)

    poses_path = out_png.parent / "JacobsBot-ApprovalPoses.png"
    font = _font(13, bold=True)
    pose_panels: list[Image.Image] = []
    for global_fi, _amp, label in samples:
        frame_path = seq / f"f_{global_fi:04d}.png"
        panel = _composite_bot_on_slide(bg.copy(), Image.open(frame_path), spec)
        draw = ImageDraw.Draw(panel)
        draw.rectangle([8, 8, 8 + len(label) * 7 + 18, 30], fill=(8, 14, 20, 220))
        draw.text((12, 11), label, fill=EMERALD, font=font)
        pose_panels.append(panel)
    gap = 8
    poses_w = sum(p.width for p in pose_panels) + gap * (len(pose_panels) - 1)
    poses_sheet = Image.new("RGB", (poses_w, pose_panels[0].height), (12, 16, 22))
    px = 0
    for panel in pose_panels:
        poses_sheet.paste(panel, (px, 0))
        px += panel.width + gap
    poses_sheet.save(poses_path, quality=95)
    print(f"Pose variants → {poses_path}", flush=True)

    meta = {
        "purpose": "avatar_approval_preview",
        "renderVersion": AVATAR_RENDER_VERSION,
        "likeness": "portrait_composite_on_3d_host",
        "samples": [s[2] for s in samples],
        "spec": {
            "placement": spec.get("botAvatarOverlayPlacement", "below-green-outside"),
            "scale": _avatar_overlay_scale(spec),
            "marginX": _avatar_overlay_margin_x(spec),
            "marginY": _avatar_overlay_margin_y(spec),
            "introCardBounds": list(_intro_card_bounds(bg)),
        },
    }
    out_png.with_suffix(".json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    print(f"Avatar approval preview → {out_png}", flush=True)
    return True


def narration_density_multiplier(spec: dict | None) -> float:
    spec = spec or {}
    return max(1.0, float(
        spec.get("narrationScriptDensityMultiplier")
        or spec.get("narrationDensityMultiplier")
        or 2.0
    ))
