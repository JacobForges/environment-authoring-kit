#!/usr/bin/env python3
"""Train per-agent gameplay_adapter.onnx (BC on agent dataset JSONL)."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

OBS_DIM = 96
ACT_DIM = 12


def default_agents_root() -> Path:
    return (
        Path.home()
        / "Library/Application Support/DefaultCompany/Hub/Competition/Agents"
    )


def load_agent_rows(dataset_dir: Path) -> tuple[np.ndarray, np.ndarray]:
    states: list[np.ndarray] = []
    actions: list[np.ndarray] = []
    if not dataset_dir.is_dir():
        return np.zeros((0, OBS_DIM), np.float32), np.zeros((0, ACT_DIM), np.float32)

    for path in sorted(dataset_dir.glob("*.jsonl")):
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            s = np.asarray(row.get("state") or [], dtype=np.float32)
            a = np.asarray(row.get("actions") or [], dtype=np.float32)
            if s.shape[0] != OBS_DIM:
                continue
            if a.shape[0] < ACT_DIM:
                padded = np.zeros(ACT_DIM, dtype=np.float32)
                padded[: a.shape[0]] = a
                a = padded
            else:
                a = a[:ACT_DIM]
            states.append(s)
            actions.append(a)

    if not states:
        return np.zeros((0, OBS_DIM), np.float32), np.zeros((0, ACT_DIM), np.float32)

    return np.stack(states), np.stack(actions)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("agent_id", help="Agent folder name under Competition/Agents")
    parser.add_argument(
        "--agents-root",
        type=Path,
        default=default_agents_root(),
        help="Override Agents root (default: Hub persistentDataPath)",
    )
    args = parser.parse_args()

    dataset = args.agents_root / args.agent_id / "dataset"
    out_dir = args.agents_root / args.agent_id / "models"
    out_dir.mkdir(parents=True, exist_ok=True)

    x_np, y_np = load_agent_rows(dataset)
    if x_np.shape[0] < 24:
        print(f"Only {x_np.shape[0]} rows in {dataset} — need at least 24 transitions.")
        sys.exit(1)

    import torch
    import torch.nn as nn

    print(f"Training adapter on {x_np.shape[0]} transitions for agent {args.agent_id}")

    x = torch.from_numpy(x_np)
    y = torch.from_numpy(y_np)

    model = nn.Sequential(
        nn.Linear(OBS_DIM, 96),
        nn.ReLU(),
        nn.Linear(96, ACT_DIM),
    )

    opt = torch.optim.Adam(model.parameters(), lr=3e-3)
    loss_fn = nn.MSELoss()
    model.train()
    ckpt_dir = out_dir / "checkpoints"
    ckpt_dir.mkdir(parents=True, exist_ok=True)

    for epoch in range(60):
        opt.zero_grad()
        pred = model(x)
        loss = loss_fn(pred, y)
        loss.backward()
        opt.step()
        if epoch % 15 == 0:
            print(f"epoch {epoch} loss {loss.item():.5f}")
        if (epoch + 1) % 15 == 0 or epoch == 59:
            cadp = ckpt_dir / f"gameplay_epoch_{epoch + 1:03d}.pt"
            torch.save(model.state_dict(), cadp)

    onnx_path = out_dir / "gameplay_adapter.onnx"
    model.eval()
    dummy = torch.zeros(1, OBS_DIM)
    torch.onnx.export(
        model,
        dummy,
        str(onnx_path),
        input_names=["state"],
        output_names=["actions"],
        opset_version=13,
    )
    print(f"Wrote {onnx_path}")


if __name__ == "__main__":
    main()
