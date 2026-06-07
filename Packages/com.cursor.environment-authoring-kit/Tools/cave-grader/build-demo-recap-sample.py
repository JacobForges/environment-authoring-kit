#!/usr/bin/env python3
"""
Prime example using YOUR Environment Kit imagery + AI-style narration (what the live AI pass writes).
Scene view captures from your next build replace these reference stills automatically.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path


def resolve_hub() -> Path:
    hub = Path(os.environ.get("HUB_ROOT", "")).expanduser()
    if hub.is_dir() and (hub / "Assets").is_dir():
        return hub
    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    return hub


# AI-polished caption style (same shape as demo-recap-narrate.ts output)
AI_STYLE_SLIDES = [
    {
        "chapter": "Layout · play disk",
        "phase": "nine_tile_play_disk",
        "sub": "locking 3×3 combat footprint before outer rings",
        "line1": "We're locking the nine-tile play disk first.",
        "line2": "Every later ring (foothills, peaks, labyrinth) keys off this flat, seam-clean arena — if the center warps, the whole FullWorld layout audit fails downstream.",
        "line3": "In frame: central green tiles = walkable contract; brown labyrinth ring is still rough layout, not final carve.",
        "accent": [72, 138, 220],
        "image_rel": "Assets/EnvironmentKit/ResearchCache/entries/fullworld-layout-ideal-49-concept/images/concept.png",
    },
    {
        "chapter": "Mountains · annex bench",
        "phase": "labyrinth_research",
        "sub": "south annex wide benches — not hedge maze",
        "line1": "Planning the south labyrinth as readable benches.",
        "line2": "We're avoiding tight hedge grids: wide spines let players retreat, loop, and sight wilderness cave mouths from the play side before heightmap carve commits.",
        "line3": "Compare cliff ref: horizontal shelf + vertical drop reads traversal, not decoration-only rock.",
        "accent": [200, 140, 80],
        "image_rel": "Assets/EnvironmentKit/ResearchCache/images/environment-authoring-kit-style-example-2-tomb-raider-cliff-bench-annex/ref-0.png",
    },
    {
        "chapter": "Terrain · crater repair",
        "phase": "play_smooth",
        "sub": "crater repair tile 5/9 on play disk",
        "line1": "Repairing craters on play tile 5 of 9.",
        "line2": "Tile-by-tile repair keeps the combat disk walkable before foothills consume the border — pits here become player trips and bad line-of-sight, not cosmetic noise.",
        "line3": "Watch for shallow bowls in the heightmap; we remove them, not paint over with grass.",
        "accent": [90, 168, 120],
        "image_rel": "Assets/EnvironmentKit/ResearchCache/images/play-disk-bad-10/ref-0.png",
    },
    {
        "chapter": "Surface · DEM anchor",
        "phase": "dem_georef",
        "sub": "macro elevation from real-world hillshade",
        "line1": "Anchoring macro hills to georeferenced DEM data.",
        "line2": "Believable silhouettes come from real elevation first; local sculpt only adds game-readable detail after the massif reads correctly at distance.",
        "line3": "Hillshade pass informs peak placement — not hand-sculpted random bumps.",
        "accent": [120, 170, 200],
        "image_rel": "Assets/EnvironmentKit/ResearchCache/images/fl-jackson-hillshade/hillshade.png",
    },
    {
        "chapter": "Content · Plan v4",
        "phase": "worldplan_v4",
        "sub": "CC0 NPCs and landmarks on stable terrain",
        "line1": "Dropping Plan v4 characters and landmarks onto stable terrain.",
        "line2": "Props and NPCs land only after terrain ladder passes — otherwise loot and encounters float or sink when the next meat pass moves height.",
        "line3": "Hub-and-spoke vista junctions help players orient after the open world stops being empty heightmap.",
        "accent": [150, 110, 210],
        "image_rel": "Assets/EnvironmentKit/ResearchCache/images/environment-authoring-kit-style-example-4-—-hub-and-spoke-vista-junction/ref-0.png",
    },
]


def main() -> int:
    hub = resolve_hub()
    if not (hub / "Assets").is_dir():
        print("Set HUB_ROOT to your Unity Hub project.", file=sys.stderr)
        return 1

    out_dir = hub / "Library" / "EnvironmentKit" / "DemoCapture" / "_prime_example"
    out_dir.mkdir(parents=True, exist_ok=True)

    compose_frames = []
    for slide in AI_STYLE_SLIDES:
        img = hub / slide["image_rel"]
        if not img.is_file():
            print(f"Missing image: {img}", file=sys.stderr)
            return 1
        compose_frames.append(
            {
                "image": str(img),
                "chapter": slide["chapter"],
                "phase": slide["phase"],
                "sub": slide["sub"],
                "line1": slide["line1"],
                "line2": slide["line2"],
                "line3": slide["line3"],
                "accent": slide["accent"],
            }
        )

    spec = {
        "slideDuration": 3.4,
        "xfadeDuration": 0.45,
        "introTitle": "World Build Recap",
        "introSubtitle": "Your world imagery + AI-narrated captions (Scene view slides look like this after your next recorded build)",
        "outroTitle": "Record your next build",
        "outroSubtitle": "Enable demo recording in Environment Kit Hub — we keep frames/ and compose this same way from your Scene view PNGs",
        "frames": compose_frames,
    }
    spec_path = out_dir / "DemoRecapCompose.json"
    spec_path.write_text(json.dumps(spec, indent=2))

    compose_py = Path(__file__).resolve().parent / "compose-demo-recap.py"
    output = out_dir / "DemoRecap_PRIME_EXAMPLE.mp4"
    env = {**os.environ, "HUB_ROOT": str(hub)}
    subprocess.run([sys.executable, str(compose_py), str(spec_path), str(output)], check=True, env=env)
    print(str(output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
