#!/usr/bin/env python3
"""Smoke-test all competition ONNX models (no Unity)."""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
TRAINED = ROOT / "trained"


def _check(onnx_path: Path, feeds: dict[str, np.ndarray], want_outputs: list[str]) -> dict:
    import onnx
    import onnxruntime as ort

    onnx.checker.check_model(str(onnx_path))
    sess = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    input_names = {i.name for i in sess.get_inputs()}
    output_names = [o.name for o in sess.get_outputs()]
    for k in feeds:
        if k not in input_names:
            raise KeyError(f"{onnx_path.name}: missing input {k}, have {input_names}")
    for w in want_outputs:
        if w not in output_names:
            raise KeyError(f"{onnx_path.name}: missing output {w}, have {output_names}")
    out = sess.run(want_outputs, feeds)
    shapes = {want_outputs[i]: list(out[i].shape) for i in range(len(want_outputs))}
    return {"file": onnx_path.name, "bytes": onnx_path.stat().st_size, "outputs": shapes}


def main() -> int:
    try:
        import onnxruntime  # noqa: F401
    except ImportError:
        print("pip install onnxruntime onnx", file=sys.stderr)
        return 1

    tests = []

    p = TRAINED / "competition_setup.onnx"
    if p.is_file():
        tests.append(
            _check(p, {"features": np.zeros((1, 100), np.float32)}, ["avatar", "z", "archetype"])
        )

    p = TRAINED / "competition_policy.onnx"
    if p.is_file():
        tests.append(_check(p, {"state": np.zeros((1, 42), np.float32)}, ["actions"]))

    p = TRAINED / "competition_gameplay.onnx"
    if p.is_file():
        tests.append(_check(p, {"state": np.zeros((1, 96), np.float32)}, ["actions"]))

    p = TRAINED / "competition_chat_intent.onnx"
    if p.is_file():
        tests.append(_check(p, {"text_features": np.zeros((1, 64), np.float32)}, ["intent_logits"]))

    report = {"ok": True, "models": tests, "trainedDir": str(TRAINED)}
    out = ROOT / "trained" / "validate_report.json"
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if tests else 1


if __name__ == "__main__":
    raise SystemExit(main())
