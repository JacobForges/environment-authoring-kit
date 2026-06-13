#!/usr/bin/env python3
"""Export competition_setup.onnx + competition_policy.onnx (scaffold weights).

Requires: pip install onnx numpy
Output: Assets/StreamingAssets/Competition/Models/

These are architecture-correct placeholders — train and replace weights before shipping.
"""
from __future__ import annotations

import json
import os
from pathlib import Path

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

HUB = Path(__file__).resolve().parents[2]
OUT = HUB / "Assets/StreamingAssets/Competition/Models"
MANIFEST = OUT / "model_manifest.json"

SETUP_IN = 100
SETUP_HIDDEN = (128, 128, 64)
POLICY_IN = 42
POLICY_HIDDEN = (128, 128)
POLICY_OUT = 4  # move_x, move_z, turn, jump_logit — apply Tanh/Tanh/Sigmoid in C#


def _mlp(prefix: str, input_name: str, hidden_dims: tuple[int, ...], weights: dict):
    nodes, inits = [], []
    cur = input_name
    for i, h in enumerate(hidden_dims):
        w = numpy_helper.from_array(weights[f"{prefix}_w{i}"].astype(np.float32), f"{prefix}_w{i}")
        b = numpy_helper.from_array(weights[f"{prefix}_b{i}"].astype(np.float32), f"{prefix}_b{i}")
        out = f"{prefix}_h{i}"
        nodes.append(helper.make_node("Gemm", [cur, w.name, b.name], [out], alpha=1.0, beta=1.0, transB=1))
        inits.extend([w, b])
        if i < len(hidden_dims) - 1:
            relu_out = f"{prefix}_relu{i}"
            nodes.append(helper.make_node("Relu", [out], [relu_out]))
            cur = relu_out
        else:
            cur = out
    return nodes, inits, cur


def export_setup(path: Path) -> None:
    rng = np.random.default_rng(42)
    h1, h2, h3 = SETUP_HIDDEN
    w = {
        "setup_w0": rng.normal(0, 0.08, (SETUP_IN, h1)),
        "setup_b0": np.zeros(h1),
        "setup_w1": rng.normal(0, 0.08, (h1, h2)),
        "setup_b1": np.zeros(h2),
        "setup_w2": rng.normal(0, 0.08, (h2, h3)),
        "setup_b2": np.zeros(h3),
    }
    for head, dim in (("avatar", 12), ("z", 24), ("archetype", 8)):
        w[f"{head}_w"] = rng.normal(0, 0.08, (h3, dim))
        w[f"{head}_b"] = np.zeros(dim)

    nodes, inits, trunk = _mlp("setup", "features", SETUP_HIDDEN, w)
    outputs = []
    for head in ("avatar", "z", "archetype"):
        wi = numpy_helper.from_array(w[f"{head}_w"].astype(np.float32), f"{head}_w")
        bi = numpy_helper.from_array(w[f"{head}_b"].astype(np.float32), f"{head}_b")
        inits.extend([wi, bi])
        nodes.append(helper.make_node("Gemm", [trunk, wi.name, bi.name], [head], alpha=1.0, beta=1.0, transB=1))
        outputs.append(helper.make_tensor_value_info(head, TensorProto.FLOAT, [1, w[f"{head}_w"].shape[1]]))

    x = helper.make_tensor_value_info("features", TensorProto.FLOAT, [1, SETUP_IN])
    graph = helper.make_graph(nodes, "competition_setup", [x], outputs, inits)
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
    model.ir_version = 8
    onnx.checker.check_model(model)
    path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(model, str(path))


def export_policy(path: Path) -> None:
    rng = np.random.default_rng(7)
    h1, h2 = POLICY_HIDDEN
    w = {
        "policy_w0": rng.normal(0, 0.08, (POLICY_IN, h1)),
        "policy_b0": np.zeros(h1),
        "policy_w1": rng.normal(0, 0.08, (h1, h2)),
        "policy_b1": np.zeros(h2),
        "policy_w2": rng.normal(0, 0.08, (h2, POLICY_OUT)),
        "policy_b2": np.zeros(POLICY_OUT),
    }
    nodes, inits, penult = _mlp("policy", "state", POLICY_HIDDEN, w)
    wi = numpy_helper.from_array(w["policy_w2"].astype(np.float32), "policy_w2")
    bi = numpy_helper.from_array(w["policy_b2"].astype(np.float32), "policy_b2")
    inits.extend([wi, bi])
    nodes.append(helper.make_node("Gemm", [penult, wi.name, bi.name], ["actions"], alpha=1.0, beta=1.0, transB=1))

    x = helper.make_tensor_value_info("state", TensorProto.FLOAT, [1, POLICY_IN])
    outs = [helper.make_tensor_value_info("actions", TensorProto.FLOAT, [1, POLICY_OUT])]
    graph = helper.make_graph(nodes, "competition_policy", [x], outs, inits)
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
    model.ir_version = 8
    onnx.checker.check_model(model)
    path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(model, str(path))


def write_manifest() -> None:
    setup = OUT / "competition_setup.onnx"
    policy = OUT / "competition_policy.onnx"
    doc = {
        "version": 1,
        "note": "Scaffold weights (random init). Replace after training.",
        "setup": {
            "file": "competition_setup.onnx",
            "bytes": setup.stat().st_size if setup.is_file() else 0,
            "inputs": {"features": [1, SETUP_IN]},
            "outputs": {
                "avatar": [1, 12],
                "z": [1, 24],
                "archetype": [1, 8],
            },
        },
        "policy": {
            "file": "competition_policy.onnx",
            "bytes": policy.stat().st_size if policy.is_file() else 0,
            "inputs": {"state": [1, POLICY_IN]},
            "outputs": {
                "actions": [1, POLICY_OUT],
                "actionLayout": ["move_x", "move_z", "turn", "jump_logit"],
            },
            "inferenceHz": 10,
            "backend": "CPU",
        },
    }
    MANIFEST.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    export_setup(OUT / "competition_setup.onnx")
    export_policy(OUT / "competition_policy.onnx")
    write_manifest()
    print(f"Exported → {OUT}")


if __name__ == "__main__":
    main()
