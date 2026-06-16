#!/usr/bin/env python3
"""Wizard tab brain model filenames (Python onnxruntime only — not Unity Sentis)."""

from __future__ import annotations

from pathlib import Path

# skl2onnx TF-IDF + string inputs crash Unity's ONNXModelImporter — keep out of .onnx pipeline.
BRAIN_MODEL_EXT = ".tabbrain"
LEGACY_BRAIN_MODEL_EXT = ".onnx"


def brain_model_filename(tab_id: str) -> str:
    return f"{tab_id}_brain{BRAIN_MODEL_EXT}"


def resolve_brain_model_path(models_dir: Path, tab_id: str) -> Path | None:
    primary = models_dir / brain_model_filename(tab_id)
    if primary.is_file():
        return primary
    legacy = models_dir / f"{tab_id}_brain{LEGACY_BRAIN_MODEL_EXT}"
    if legacy.is_file():
        return legacy
    return None
