#!/usr/bin/env python3
"""
Per-layer intro color match — each beat + each gap gets its own grade toward ONE
global target (median beat). No global brightness/contrast wash.

Encodes from intro_pre_uniform_color.mp4 when present.

Usage: normalize-intro-color.py [--dry-run] [--from-current]
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Pillow required: pip install pillow", file=sys.stderr)
    raise

ROOT = Path(__file__).resolve().parents[2]
INTRO_MP4 = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro.mp4"
TIMELINE = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_timeline.json"
BACKUP = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_pre_uniform_color.mp4"
FALLBACK = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_1080p_backup.mp4"
REPORT = ROOT / "Assets/StreamingAssets/DeepTrainAcademy/intro_uniform_color_report.json"

# Per-layer only — tight gains, partial blend toward target.
MIN_GAIN = 0.92
MAX_GAIN = 1.08
STRENGTH = 0.72


def sample_rgb(mp4: Path, time_s: float) -> tuple[float, float, float]:
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "f.png"
        subprocess.run(
            [
                "ffmpeg", "-loglevel", "error", "-y",
                "-ss", f"{max(0, time_s):.4f}",
                "-i", str(mp4),
                "-frames:v", "1",
                "-vf", "scale=640:-1",
                str(out),
            ],
            check=True,
        )
        px = list(Image.open(out).convert("RGB").getdata())
        n = max(len(px), 1)
        return (
            sum(p[0] for p in px) / n / 255.0,
            sum(p[1] for p in px) / n / 255.0,
            sum(p[2] for p in px) / n / 255.0,
        )


def luma(rgb: tuple[float, float, float]) -> float:
    return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]


def clamp_gain(v: float) -> float:
    return max(MIN_GAIN, min(MAX_GAIN, v))


def soft_gain(sample: float, target: float) -> float:
    raw = clamp_gain(target / max(sample, 1e-4))
    return 1.0 + (raw - 1.0) * STRENGTH


def segment_center(seg: dict) -> float:
    return float(seg["start"]) + float(seg["durationS"]) * 0.5


def median(values: list[float]) -> float:
    s = sorted(values)
    n = len(s)
    if n == 0:
        return 0.0
    mid = n // 2
    return s[mid] if n % 2 else (s[mid - 1] + s[mid]) * 0.5


def global_target_rgb(
    samples: dict[str, tuple[float, float, float]],
    segments: list[dict],
) -> tuple[float, float, float]:
    """One baseline look for the entire video — median of beat stills."""
    beats = [
        samples[s["id"]]
        for s in segments
        if s.get("kind") == "beat" and s["id"] in samples
    ]
    if not beats:
        return (0.16, 0.17, 0.14)
    return (
        median([c[0] for c in beats]),
        median([c[1] for c in beats]),
        median([c[2] for c in beats]),
    )


def build_per_layer_filter(
    segments: list[dict],
    samples: dict[str, tuple[float, float, float]],
    target: tuple[float, float, float],
) -> str:
    """Individual colorchannelmixer per segment — no global eq."""
    parts: list[str] = []
    for seg in segments:
        sid = seg["id"]
        if sid not in samples:
            continue
        r, g, b = samples[sid]
        rr = soft_gain(r, target[0])
        gg = soft_gain(g, target[1])
        bb = soft_gain(b, target[2])
        t0 = float(seg["start"])
        t1 = float(seg["end"])
        parts.append(
            f"colorchannelmixer=rr={rr:.5f}:gg={gg:.5f}:bb={bb:.5f}"
            f":enable='between(t,{t0:.4f},{t1:.4f})'"
        )
    return ",".join(parts)


def probe_size(mp4: Path) -> tuple[int, int]:
    out = subprocess.check_output(
        [
            "ffprobe", "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=width,height",
            "-of", "csv=p=0", str(mp4),
        ],
        text=True,
    ).strip()
    w, h = out.split(",")
    return int(w), int(h)


def pick_source(from_current: bool) -> Path:
    if from_current and INTRO_MP4.exists():
        return INTRO_MP4
    if BACKUP.exists():
        return BACKUP
    if FALLBACK.exists():
        return FALLBACK
    return INTRO_MP4


def main() -> int:
    dry = "--dry-run" in sys.argv
    from_current = "--from-current" in sys.argv
    source = pick_source(from_current)

    if not source.exists():
        print("FAIL no source mp4", file=sys.stderr)
        return 1
    if not TIMELINE.exists():
        print(f"FAIL missing {TIMELINE}", file=sys.stderr)
        return 1

    timeline = json.loads(TIMELINE.read_text(encoding="utf-8"))
    segments = timeline.get("segments", [])

    print("=" * 64)
    print("INTRO PER-LAYER COLOR — each segment → one global target")
    print(f"Source: {source.relative_to(ROOT)}")
    print(f"Target: median beat RGB  |  strength={STRENGTH}  (no global eq)")
    print("=" * 64)

    samples: dict[str, tuple[float, float, float]] = {}
    for seg in segments:
        sid = seg["id"]
        t = segment_center(seg)
        rgb = sample_rgb(source, t)
        samples[sid] = rgb
        print(
            f"  {sid:12s} ({seg.get('kind', '?'):4s}) "
            f"luma={luma(rgb):.3f}  rgb=({rgb[0]:.2f},{rgb[1]:.2f},{rgb[2]:.2f})"
        )

    target = global_target_rgb(samples, segments)
    print("-" * 64)
    print(f"Global target rgb=({target[0]:.3f},{target[1]:.3f},{target[2]:.3f})  luma={luma(target):.3f}")

    vf = build_per_layer_filter(segments, samples, target)
    w, h = probe_size(source)

    report = {
        "version": 3,
        "mode": "per_layer_global_target",
        "source": str(source),
        "targetRgb": list(target),
        "strength": STRENGTH,
        "segmentCount": len(samples),
        "filter": vf,
        "dryRun": dry,
    }

    if dry:
        REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print("DRY RUN — wrote report")
        return 0

    fd, tmp_path = tempfile.mkstemp(suffix=".mp4", prefix="intro_layers_")
    os.close(fd)
    tmp = Path(tmp_path)
    try:
        print(f"Encoding {w}x{h} ({len(samples)} individual layers)...")
        subprocess.run(
            [
                "ffmpeg", "-y", "-i", str(source),
                "-vf", vf,
                "-c:v", "libx264", "-crf", "18", "-preset", "medium",
                "-pix_fmt", "yuv420p", "-movflags", "+faststart",
                str(tmp),
            ],
            check=True,
        )
        subprocess.run(["mv", str(tmp), str(INTRO_MP4)], check=True)
        print(f"Replaced {INTRO_MP4.relative_to(ROOT)}")
    finally:
        if tmp.exists():
            tmp.unlink()

    print("-" * 64)
    print("After (each layer sampled):")
    lumas = []
    for seg in segments:
        sid = seg["id"]
        t = segment_center(seg)
        rgb = sample_rgb(INTRO_MP4, t)
        lum = luma(rgb)
        lumas.append(lum)
        delta = lum - luma(target)
        print(f"  {sid:12s}  luma={lum:.3f}  Δtarget={delta:+.3f}")

    spread = max(lumas) - min(lumas) if lumas else 0
    report["postLumaSpread"] = round(spread, 4)
    report["postLumaMin"] = round(min(lumas), 4)
    report["postLumaMax"] = round(max(lumas), 4)
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Luma spread: {spread:.3f} (target {luma(target):.3f})")
    print("=" * 64)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
