#!/usr/bin/env python3
"""Print effective ONNX config for each wizard tab brain.

Usage (from Tools/cave-grader):
  python3 world_tab_brains/print_onnx_config.py
"""

from __future__ import annotations

import os
from pathlib import Path

from world_tab_brains.model_paths import brain_model_filename


_TAB_ENV_SUFFIX = {
    "terrain": "TERRAIN",
    "surface-content": "SURFACE",
    "caves": "CAVES",
    "mazes": "MAZES",
    "interior-content": "INTERIOR",
    "atmosphere": "ATMOSPHERE",
    "music": "MUSIC",
    "video": "VIDEO",
}


def _models_dir() -> Path:
    return Path(__file__).resolve().parent / "models"


def main() -> None:
    models = _models_dir()
    print(f"Models dir: {models}")
    print(f"WORLD_TAB_BRAIN_ONNX={os.environ.get('WORLD_TAB_BRAIN_ONNX', '1')}")
    print(f"WORLD_TAB_BRAIN_ONNX_THRESHOLD={os.environ.get('WORLD_TAB_BRAIN_ONNX_THRESHOLD', '0.70')}")
    print()

    tabs = [
        "terrain",
        "surface-content",
        "caves",
        "mazes",
        "interior-content",
        "atmosphere",
        "music",
    ]

    for tab_id in tabs:
        suffix = _TAB_ENV_SUFFIX.get(tab_id, tab_id.upper().replace("-", "_"))
        per_tab_key = f"WORLD_TAB_BRAIN_{suffix}_ONNX_PATH"
        env_val = os.environ.get(per_tab_key, "")
        if not env_val and tab_id == "surface-content":
            # Back-compat
            per_tab_key = "WORLD_TAB_BRAIN_SURFACE_ONNX_PATH"
            env_val = os.environ.get(per_tab_key, "")

        manifest_path = models / f"{tab_id}_brain.manifest.json"
        model_file = models / brain_model_filename(tab_id)

        print(f"[{tab_id}]")
        print(f"  env var key     : {per_tab_key}")
        print(f"  env model path  : {env_val or '(not set)'}")
        print(f"  manifest exists : {manifest_path.is_file()}")
        print(f"  model exists    : {model_file.is_file()}")
        print()


if __name__ == "__main__":
    main()

