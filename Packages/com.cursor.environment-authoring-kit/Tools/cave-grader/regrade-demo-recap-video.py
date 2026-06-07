#!/usr/bin/env python3
"""
Re-grade an existing capture run — video-only re-encode with improved quality settings.

Does NOT re-capture PNGs, regenerate approved intro/outro, or change videoPlaybackFactor.
Preserves Personal Voice narration unless you pass --narration-only to remux only.

Usage (Terminal.app):
  python3 regrade-demo-recap-video.py /path/to/DemoCapture/<timestamp>
  python3 regrade-demo-recap-video.py /path/to/DemoCapture/<timestamp> --narration-only

Example:
  python3 regrade-demo-recap-video.py /Volumes/Lexar/EnvironmentKit-Hub/DemoCapture/20260605-224357
"""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path


def load(name: str, file: str):
    p = Path(__file__).resolve().parent / file
    spec = importlib.util.spec_from_file_location(name, p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


QUALITY_KEYS = (
    "outputWidth",
    "outputHeight",
    "encodeCrf",
    "encodePreset",
    "encodeTune",
    "segmentXfadeSec",
    "sceneUpscale",
    "timelapseEncodeFps",
    "tlMaxFrames",
    "videoEnhance",
    "holdKeyframeMode",
)


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    run = Path(sys.argv[1]).expanduser().resolve()
    flags = set(sys.argv[2:])
    narration_only = "--narration-only" in flags

    tl_path = run / "DemoRecapTimeline.json"
    if not tl_path.is_file():
        print(f"Missing {tl_path}", file=sys.stderr)
        return 1

    producer = load("producer", "demo-recap-producer.py")
    opencv = load("opencv", "demo-recap-opencv-annotate.py")
    captions = load("captions", "demo-recap-captions.py")
    compose = load("compose_pres", "compose-presentation-recap.py")

    spec = json.loads(tl_path.read_text(encoding="utf-8"))
    for key in QUALITY_KEYS:
        if key in producer.PRODUCER_SPEC_DEFAULTS:
            spec[key] = producer.PRODUCER_SPEC_DEFAULTS[key]
    spec["videoPlaybackFactor"] = 1.0
    spec["segmentCache"] = False
    spec["composeWorkDir"] = str(run / "_presentation_compose_regrade")

    milestones = spec.get("milestones", [])
    for m in milestones:
        captions.fill_milestone_captions(m)
    n = opencv.sync_region_labels_from_timeline(milestones)
    spec["milestones"] = milestones
    print(f"Re-grade: synced {n} annotation label(s); 1080p crf={spec.get('encodeCrf')}")

    tl_path.write_text(json.dumps(spec, indent=2) + "\n", encoding="utf-8")
    (run / "DemoRecapOpenCV.json").write_text(
        json.dumps(
            [{"i": m.get("i"), "frame": m.get("frame"), "regions": m.get("regions", [])} for m in milestones],
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )

    out = run / "DemoRecapPresentation.mp4"
    compose.compose_presentation(run, out, spec, narration_only=narration_only)
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
