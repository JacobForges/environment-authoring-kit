#!/usr/bin/env python3
"""BC fine-tune competition_gameplay.onnx from Unity-exported episode JSONL."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

OBS_DIM = 96
ACT_DIM = 12


def default_episodes_dir() -> Path:
    override = os.environ.get("HUB_EPISODES_DIR")
    if override:
        return Path(override).expanduser()
    home = Path.home()
    return home / "Library/Application Support/DefaultCompany/Hub/Competition/Episodes"


def iter_dataset_folders() -> list[Path]:
    folders: list[Path] = []
    primary = default_episodes_dir()
    if primary.is_dir():
        folders.append(primary)

    agents_root = primary.parent / "Agents"
    if agents_root.is_dir():
        for dataset in sorted(agents_root.glob("*/dataset")):
            if dataset.is_dir():
                folders.append(dataset)

    extra = os.environ.get("HUB_AGENT_DATASET")
    if extra:
        p = Path(extra).expanduser()
        if p.is_dir() and p not in folders:
            folders.append(p)

    return folders


def load_episode_rows(folders: list[Path]) -> tuple[np.ndarray, np.ndarray]:
    states: list[np.ndarray] = []
    actions: list[np.ndarray] = []
    activity_filter = (os.environ.get("HUB_ACTIVITY_FILTER") or "").strip().lower()

    for folder in folders:
        if not folder.is_dir():
            continue

        files = sorted(folder.glob("episode_*.jsonl"))
        for path in files[-24:]:
            for line in path.read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    continue

                activity = (row.get("activity") or "").strip().lower()
                if activity_filter and activity != activity_filter:
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
    import torch
    import torch.nn as nn

    folders = iter_dataset_folders()
    x_np, y_np = load_episode_rows(folders)
    if x_np.shape[0] < 32:
        print(f"Only {x_np.shape[0]} transitions in {folders} — falling back to synthetic gameplay_trainer.")
        from gameplay_trainer import main as synthetic_main

        synthetic_main()
        return

    print(f"Training from {x_np.shape[0]} embodied episode transitions across {len(folders)} folder(s)")

    x = torch.from_numpy(x_np)
    y = torch.from_numpy(y_np)

    model = nn.Sequential(
        nn.Linear(OBS_DIM, 192),
        nn.ReLU(),
        nn.Linear(192, 128),
        nn.ReLU(),
        nn.Linear(128, ACT_DIM),
    )

    opt = torch.optim.Adam(model.parameters(), lr=2e-3)
    loss_fn = nn.MSELoss()
    model.train()
    for epoch in range(40):
        opt.zero_grad()
        pred = model(x)
        loss = loss_fn(pred, y)
        loss.backward()
        opt.step()
        if epoch % 10 == 0:
            print(f"epoch {epoch} loss {loss.item():.5f}")

    out_dir = ROOT / "trained"
    out_dir.mkdir(parents=True, exist_ok=True)
    onnx_path = out_dir / "competition_gameplay.onnx"

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
