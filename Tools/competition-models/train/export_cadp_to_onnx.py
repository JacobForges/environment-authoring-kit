#!/usr/bin/env python3
"""Export gameplay_adapter.cadp (CADP v1) to gameplay_adapter.onnx."""
from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

import numpy as np

OBS_DIM = 96
HIDDEN_DIM = 96
ACT_DIM = 12
MAGIC = 0x50444143  # CADP


def read_cadp(path: Path) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    data = path.read_bytes()
    off = 0
    magic, version, obs, hidden, act = struct.unpack_from("<IIIii", data, off)
    off += 20
    if magic != MAGIC or version != 1:
        raise ValueError(f"Bad CADP header in {path}")
    if obs != OBS_DIM or hidden != HIDDEN_DIM or act != ACT_DIM:
        raise ValueError(f"Unexpected dims {obs}/{hidden}/{act}")

    def read_floats(count: int) -> np.ndarray:
        nonlocal off
        fmt = f"<{count}f"
        size = struct.calcsize(fmt)
        arr = np.array(struct.unpack_from(fmt, data, off), dtype=np.float32)
        off += size
        return arr

    w1 = read_floats(HIDDEN_DIM * OBS_DIM).reshape(HIDDEN_DIM, OBS_DIM)
    b1 = read_floats(HIDDEN_DIM)
    w2 = read_floats(ACT_DIM * HIDDEN_DIM).reshape(ACT_DIM, HIDDEN_DIM)
    b2 = read_floats(ACT_DIM)
    return w1, b1, w2, b2


def export_onnx(w1, b1, w2, b2, out_path: Path) -> None:
    import torch
    import torch.nn as nn

    class Adapter(nn.Module):
        def __init__(self):
            super().__init__()
            self.fc1 = nn.Linear(OBS_DIM, HIDDEN_DIM)
            self.fc2 = nn.Linear(HIDDEN_DIM, ACT_DIM)

        def forward(self, state: torch.Tensor) -> torch.Tensor:
            x = torch.relu(self.fc1(state))
            return self.fc2(x)

    model = Adapter()
    with torch.no_grad():
        model.fc1.weight.copy_(torch.from_numpy(w1))
        model.fc1.bias.copy_(torch.from_numpy(b1))
        model.fc2.weight.copy_(torch.from_numpy(w2))
        model.fc2.bias.copy_(torch.from_numpy(b2))

    model.eval()
    dummy = torch.zeros(1, OBS_DIM)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        model,
        dummy,
        str(out_path),
        input_names=["state"],
        output_names=["actions"],
        opset_version=13,
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("cadp_path", type=Path)
    parser.add_argument("--out", type=Path, default=None)
    args = parser.parse_args()

    out = args.out or args.cadp_path.with_suffix(".onnx")
    w1, b1, w2, b2 = read_cadp(args.cadp_path)
    export_onnx(w1, b1, w2, b2, out)
    print(f"Wrote {out}")


if __name__ == "__main__":
    main()
