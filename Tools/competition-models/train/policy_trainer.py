#!/usr/bin/env python3
"""Train competition_policy.onnx from synthetic expert (checkpoint follower).

Replace with imitation from real Season-1 trajectories after map freeze.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from export_competition_onnx import POLICY_IN, POLICY_OUT  # noqa: E402

Z_DIM = 24
OBS_DIM = POLICY_IN - Z_DIM  # 18


def synthetic_episode(rng: np.random.Generator, z: np.ndarray, steps: int = 120):
    """Expert: steer toward next checkpoint in a 1D ring of waypoints."""
    waypoints = rng.uniform(-1, 1, (8, 2)).astype(np.float32)
    pos = waypoints[0].copy()
    vel = np.zeros(2, dtype=np.float32)
    cp = 0
    obs_list, act_list = [], []
    for _ in range(steps):
        target = waypoints[cp % len(waypoints)]
        delta = target - pos
        dist = float(np.linalg.norm(delta) + 1e-6)
        desired = delta / dist
        speed = float(z[0]) * 0.8 + 0.2
        vel = vel * 0.7 + desired * speed * 0.3
        jump = 1.0 if dist < 0.15 and rng.random() < float(z[1]) * 0.3 else 0.0
        turn = float(np.arctan2(delta[1], delta[0]) / np.pi)
        move_x = float(np.clip(vel[0], -1, 1))
        move_z = float(np.clip(vel[1], -1, 1))
        if dist < 0.2:
            cp += 1
        pos = pos + vel * 0.08

        obs = np.zeros(OBS_DIM, dtype=np.float32)
        obs[0:2] = pos
        obs[2:4] = vel
        obs[4] = turn
        obs[5:7] = delta / max(dist, 1e-3)
        obs[7] = min(dist, 1.0)
        obs[8] = cp / len(waypoints)
        obs[9] = 0.1
        obs[10:13] = rng.uniform(0.2, 1.0, 3)
        obs[13] = 1.0
        obs[14:16] = rng.uniform(0.3, 1.0, 2)
        obs[16] = 0.0
        obs[17] = 0.0

        state = np.concatenate([obs, z]).astype(np.float32)
        action = np.array([move_x, move_z, turn, jump], dtype=np.float32)
        obs_list.append(state)
        act_list.append(action)
    return np.stack(obs_list), np.stack(act_list)


def synthesize(episodes: int = 400) -> tuple[np.ndarray, np.ndarray]:
    rng = np.random.default_rng(99)
    xs, ys = [], []
    for i in range(episodes):
        z = rng.normal(0, 0.5, Z_DIM).astype(np.float32)
        z = np.tanh(z)
        ox, oy = synthetic_episode(rng, z, steps=rng.integers(80, 160))
        xs.append(ox)
        ys.append(oy)
    return np.concatenate(xs), np.concatenate(ys)


def train_and_export(out_dir: Path, epochs: int = 40) -> dict:
    try:
        import torch
        import torch.nn as nn
    except ImportError as e:
        raise SystemExit("pip install torch — required for training") from e

    x, y = synthesize()
    n = len(x)
    split = int(n * 0.9)
    idx = np.random.default_rng(2).permutation(n)
    tr, va = idx[:split], idx[split:]

    class PolicyNet(nn.Module):
        def __init__(self):
            super().__init__()
            self.net = nn.Sequential(
                nn.Linear(POLICY_IN, 128),
                nn.ReLU(),
                nn.Linear(128, 128),
                nn.ReLU(),
                nn.Linear(128, POLICY_OUT),
            )

        def forward(self, t):
            return self.net(t)

    device = torch.device("cpu")
    model = PolicyNet().to(device)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    loss_fn = nn.MSELoss()

    xt = torch.from_numpy(x[tr]).to(device)
    yt = torch.from_numpy(y[tr]).to(device)

    for _ in range(epochs):
        model.train()
        opt.zero_grad()
        pred = model(xt)
        loss = loss_fn(pred, yt)
        loss.backward()
        opt.step()

    model.eval()
    with torch.no_grad():
        xv = torch.from_numpy(x[va]).to(device)
        yv = torch.from_numpy(y[va]).to(device)
        val_loss = loss_fn(model(xv), yv).item()

    out_dir.mkdir(parents=True, exist_ok=True)
    onnx_path = out_dir / "competition_policy.onnx"
    dummy = torch.zeros(1, POLICY_IN)
    torch.onnx.export(
        model,
        dummy,
        str(onnx_path),
        input_names=["state"],
        output_names=["actions"],
        dynamic_axes={"state": {0: "batch"}, "actions": {0: "batch"}},
        opset_version=13,
    )

    metrics = {
        "model": "competition_policy",
        "transitions": int(n),
        "valMse": round(val_loss, 6),
        "epochs": epochs,
        "note": "Synthetic checkpoint expert — retrain on Season-1 trajectories after map freeze.",
    }
    (out_dir / "policy_metrics.json").write_text(json.dumps(metrics, indent=2) + "\n", encoding="utf-8")
    return metrics


if __name__ == "__main__":
    trained = ROOT / "trained"
    m = train_and_export(trained)
    print(json.dumps(m, indent=2))
    print(f"Wrote {trained / 'competition_policy.onnx'}")
