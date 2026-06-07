#!/usr/bin/env python3
"""
Example: 2-minute simulated build → ~75s auto-edited documentary (real motion + milestone pauses).
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageEnhance, ImageFilter
except ImportError:
    print("pip3 install --user pillow", file=sys.stderr)
    sys.exit(1)

HUB_IMAGES = [
    "Assets/EnvironmentKit/ResearchCache/entries/fullworld-layout-ideal-49-concept/images/concept.png",
    "Assets/EnvironmentKit/ResearchCache/images/environment-authoring-kit-style-example-2-tomb-raider-cliff-bench-annex/ref-0.png",
    "Assets/EnvironmentKit/ResearchCache/images/play-disk-bad-10/ref-0.png",
    "Assets/EnvironmentKit/ResearchCache/images/fl-jackson-hillshade/hillshade.png",
    "Assets/EnvironmentKit/ResearchCache/images/environment-authoring-kit-style-example-4-—-hub-and-spoke-vista-junction/ref-0.png",
]

MILESTONES = [
    {
        "frame": 8,
        "chapter": "Build start",
        "phase": "startup",
        "sub": "preflight and layout audit",
        "line1": "Kicking off the FullWorld pipeline.",
        "line2": "Preflight catches bad anchors and missing catalogs in minutes so we do not waste two hours on terrain that will get torn down.",
        "line3": "Watch the Scene view — this timelapse is your real recording sped up between teaching moments.",
        "accent": [72, 138, 220],
    },
    {
        "frame": 55,
        "chapter": "Nine-tile disk",
        "phase": "nine_tile_play_disk",
        "sub": "locking combat footprint",
        "line1": "Nine-tile play disk is locking in.",
        "line2": "Everything else — foothills, peaks, labyrinth — keys off this flat center; seams here fail the whole layout audit.",
        "line3": "",
        "accent": [90, 168, 120],
    },
    {
        "frame": 105,
        "chapter": "Labyrinth carve",
        "phase": "labyrinth_carve",
        "sub": "south annex benches",
        "line1": "Carving south labyrinth benches.",
        "line2": "Wide spines let players loop and sight wilderness cave mouths — we skip tight hedge mazes on purpose.",
        "line3": "",
        "accent": [200, 140, 80],
    },
    {
        "frame": 155,
        "chapter": "Plan v4",
        "phase": "worldplan_v4",
        "sub": "NPCs and landmarks",
        "line1": "Plan v4 drops characters and loot.",
        "line2": "Content lands only after terrain ladder passes so props do not float when the next meat pass moves height.",
        "line3": "",
        "accent": [150, 110, 210],
    },
    {
        "frame": 195,
        "chapter": "Build complete",
        "phase": "complete",
        "sub": "pipeline stopped here",
        "line1": "Build finished at this exact Scene state.",
        "line2": "The editor auto-compressed two hours of work into a watchable pipeline story — you did not cut this by hand.",
        "line3": "",
        "accent": [100, 190, 230],
    },
]


def resolve_hub() -> Path:
    hub = Path(os.environ.get("HUB_ROOT", "")).expanduser()
    if hub.is_dir() and (hub / "Assets").is_dir():
        return hub
    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    return hub


def blend(a: Image.Image, b: Image.Image, t: float) -> Image.Image:
    a = a.convert("RGB").resize((1280, 720), Image.Resampling.LANCZOS)
    b = b.convert("RGB").resize((1280, 720), Image.Resampling.LANCZOS)
    return Image.blend(a, b, t)


def progress_overlay(img: Image.Image, pct: float, label: str) -> Image.Image:
    out = img.copy()
    draw = ImageDraw.Draw(out)
    w = int(1180 * pct)
    draw.rectangle([50, 680, 1230, 694], fill=(40, 48, 60))
    draw.rectangle([50, 680, 50 + w, 694], fill=(72, 138, 220))
    draw.text((50, 662), label, fill=(220, 228, 240))
    return out


def generate_timelapse(out_dir: Path, hub: Path, count: int = 210) -> None:
    tl = out_dir / "timelapse"
    tl.mkdir(parents=True, exist_ok=True)
    for old in tl.glob("*.png"):
        old.unlink()

    sources = []
    for rel in HUB_IMAGES:
        p = hub / rel
        if not p.is_file():
            raise FileNotFoundError(p)
        sources.append(Image.open(p).convert("RGB"))

    per = count // (len(sources) - 1)
    idx = 1
    for seg in range(len(sources) - 1):
        a, b = sources[seg], sources[seg + 1]
        for j in range(per):
            t = j / max(per - 1, 1)
            frame = blend(a, b, t)
            frame = ImageEnhance.Sharpness(frame).enhance(1.08 + 0.04 * (seg % 2))
            # subtle pan simulation
            w, h = frame.size
            shift = int((j / per) * 40)
            frame = frame.crop((shift, 0, min(w, shift + 1280), 720)).resize((1280, 720))
            pct = idx / count
            labels = ["Startup", "Play disk", "Mountains", "DEM / surface", "Content pass", "Finishing"]
            label = labels[min(int(pct * len(labels)), len(labels) - 1)] + f"  ·  {int(pct * 100)}%"
            frame = progress_overlay(frame, pct, label)
            frame.save(tl / f"tl_{idx:06d}.png")
            idx += 1
    while idx <= count:
        frame = progress_overlay(sources[-1].resize((1280, 720)), idx / count, "Finishing · 99%")
        frame.save(tl / f"tl_{idx:06d}.png")
        idx += 1


def main() -> int:
    hub = resolve_hub()
    out_dir = hub / "Library" / "EnvironmentKit" / "DemoCapture" / "_smart_example"
    out_dir.mkdir(parents=True, exist_ok=True)

    print("Generating simulated 2h timelapse (~210 frames)...")
    generate_timelapse(out_dir, hub, 210)

    spec = {
        "captureIntervalSec": 2.0,
        "targetDurationSec": 78,
        "milestoneHoldSec": 4.8,
        "introTitle": "Full pipeline timelapse",
        "introSubtitle": "Recorded for 2 hours · auto-edited to ~8 min on real builds (this sample is ~78s)",
        "outroTitle": "Auto-edit complete",
        "outroSubtitle": "Routine steps run fast; milestones pause with AI captions — no manual NLE",
        "milestones": MILESTONES,
    }
    (out_dir / "DemoRecapTimeline.json").write_text(json.dumps(spec, indent=2))

    compose = Path(__file__).resolve().parent / "compose-smart-recap.py"
    output = out_dir / "DemoRecap_SMART_EXAMPLE.mp4"
    env = {**os.environ, "HUB_ROOT": str(hub)}
    print("Smart-editing...")
    subprocess.run([sys.executable, str(compose), str(out_dir), str(output)], check=True, env=env)
    print(str(output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
