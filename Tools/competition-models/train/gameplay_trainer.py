#!/usr/bin/env python3
"""Train competition_gameplay.onnx v2 — BC on Unity-aligned synthetic expert.

Obs layout matches CompetitionObsBuilder (96) including ray slots 18–25 from
AgentEnvironmentSense.WriteObs. Expert jumps blocked jumpable gaps, flanks
obstacles, and eases off cliffs.

Replace with Season-1 trajectories after map freeze.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

OBS_DIM = 96
ACT_DIM = 12
Z_DIM = 24
Z_START = 52
PROBE_RANGE = 4.5
BLOCKED_DIST = 1.35


def synthetic_step(rng: np.random.Generator, z: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Expert aligned with Unity obs indices + environment sense rays."""
    obs = np.zeros(OBS_DIM, dtype=np.float32)

    pos = rng.uniform(-40, 40, 2).astype(np.float32)
    goal = pos + rng.uniform(8, 28, 2).astype(np.float32) * rng.choice([-1, 1], size=2)
    enemy = pos + rng.uniform(-6, 6, 2).astype(np.float32)

    delta = goal - pos
    dist_g = float(np.linalg.norm(delta) + 1e-6)
    goal_dir = delta / dist_g

    delta_e = enemy - pos
    dist_e = float(np.linalg.norm(delta_e) + 1e-6)

    yaw = float(np.arctan2(goal_dir[1], goal_dir[0]))
    fwd = np.array([np.sin(yaw), np.cos(yaw)], dtype=np.float32)

    forward_m = float(rng.uniform(0.15, PROBE_RANGE))
    blocked = forward_m < BLOCKED_DIST
    jumpable = blocked and rng.random() < 0.42
    cliff_risk = float(rng.uniform(0.0, 1.0))
    flank_sign = 1.0 if rng.random() > 0.5 else -1.0

    hp = float(rng.uniform(0.25, 1.0))
    enemy_hp = float(rng.uniform(0.2, 1.0))
    stamina = float(rng.uniform(0.4, 1.0))

    obs[0] = pos[0] * 0.01
    obs[1] = pos[1] * 0.01
    obs[4] = fwd[0]
    obs[5] = fwd[1]
    obs[6] = goal_dir[0]
    obs[7] = goal_dir[1]
    obs[8] = min(dist_g * 0.01, 1.0)
    obs[9] = float(rng.uniform(0, 1))

    obs[18] = forward_m / PROBE_RANGE
    obs[19] = float(rng.uniform(0.25, 1.0))
    obs[20] = float(rng.uniform(0.25, 1.0))
    obs[21] = float(rng.uniform(0.3, 1.0))
    obs[22] = float(rng.uniform(0.3, 1.0))
    obs[23] = 1.0 - cliff_risk
    obs[24] = 1.0 if blocked else 0.0
    obs[25] = flank_sign * 0.5 + 0.5

    obs[26] = hp
    obs[27] = stamina
    obs[38] = min(dist_e * 0.01, 1.0)
    obs[39] = float(np.arctan2(delta_e[1], delta_e[0]) / np.pi)
    obs[40] = enemy_hp

    obs[Z_START : Z_START + Z_DIM] = z
    obs[84] = float(rng.uniform(-0.3, 0.3))
    obs[85] = float(rng.uniform(0, 0.4))
    obs[90] = 1.0

    move = goal_dir.copy()
    turn = float(np.clip(yaw * 0.35, -1, 1))
    jump = 0.0
    attack = 1.0 if dist_e < 4.0 and enemy_hp > 0.1 else 0.0
    defend = 1.0 if hp < 0.35 and dist_e < 4.5 else 0.0
    skill = 1.0 if attack > 0.5 and stamina > 0.6 and rng.random() < 0.3 else 0.0

    if blocked and jumpable:
        jump = 0.92
        move *= 0.55
    elif blocked:
        lateral = np.array([-fwd[1], fwd[0]], dtype=np.float32) * flank_sign
        move = lateral * 0.82 + goal_dir * 0.25
        move /= np.linalg.norm(move) + 1e-6

    if cliff_risk > 0.45:
        lateral = np.array([-fwd[1], fwd[0]], dtype=np.float32) * flank_sign
        move = lateral * 0.7 + move * 0.3
        move /= np.linalg.norm(move) + 1e-6
        jump = 0.0

    bark_trig = 1.0 if (hp < 0.3 or dist_g < 3.0) and rng.random() < 0.15 else 0.0
    bark_cat = float(rng.uniform(-1, 1))

    actions = np.array(
        [
            float(np.clip(move[0], -1, 1)),
            float(np.clip(move[1], -1, 1)),
            turn,
            jump,
            attack,
            defend,
            skill,
            skill * 0.8,
            skill * 0.5,
            0.0,
            bark_trig,
            bark_cat,
        ],
        dtype=np.float32,
    )
    return obs, actions


def synthesize(episodes: int = 600, steps_range: tuple[int, int] = (120, 200)) -> tuple[np.ndarray, np.ndarray]:
    rng = np.random.default_rng(42)
    xs, ys = [], []
    for _ in range(episodes):
        z = np.tanh(rng.normal(0, 0.6, Z_DIM)).astype(np.float32)
        steps = int(rng.integers(steps_range[0], steps_range[1]))
        for _ in range(steps):
            o, a = synthetic_step(rng, z)
            xs.append(o)
            ys.append(a)
    return np.stack(xs), np.stack(ys)


def train_and_export(out_dir: Path, epochs: int = 55) -> dict:
    try:
        import torch
        import torch.nn as nn
    except ImportError as e:
        raise SystemExit("pip install torch") from e

    x, y = synthesize()
    n = len(x)
    perm = np.random.default_rng(5).permutation(n)
    split = int(n * 0.9)
    tr, va = perm[:split], perm[split:]

    class GameplayNet(nn.Module):
        def __init__(self):
            super().__init__()
            self.net = nn.Sequential(
                nn.Linear(OBS_DIM, 256),
                nn.ReLU(),
                nn.Linear(256, 256),
                nn.ReLU(),
                nn.Linear(256, 128),
                nn.ReLU(),
                nn.Linear(128, ACT_DIM),
            )

        def forward(self, t):
            return self.net(t)

    device = torch.device("cpu")
    model = GameplayNet().to(device)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    loss_fn = nn.MSELoss()

    xt = torch.from_numpy(x[tr]).to(device)
    yt = torch.from_numpy(y[tr]).to(device)

    for _ in range(epochs):
        model.train()
        opt.zero_grad()
        loss = loss_fn(model(xt), yt)
        loss.backward()
        opt.step()

    model.eval()
    with torch.no_grad():
        xv = torch.from_numpy(x[va]).to(device)
        yv = torch.from_numpy(y[va]).to(device)
        val_mse = loss_fn(model(xv), yv).item()

    out_dir.mkdir(parents=True, exist_ok=True)
    onnx_path = out_dir / "competition_gameplay.onnx"
    dummy = torch.zeros(1, OBS_DIM)
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
        "model": "competition_gameplay",
        "version": 2,
        "obsDim": OBS_DIM,
        "actDim": ACT_DIM,
        "transitions": int(n),
        "valMse": round(val_mse, 6),
        "epochs": epochs,
        "note": "Unity-aligned ray BC expert — retrain on Season-1 combat+route logs.",
    }
    (out_dir / "gameplay_metrics.json").write_text(json.dumps(metrics, indent=2) + "\n", encoding="utf-8")

    manifest = {
        "gameplayVersion": 2,
        "file": "competition_gameplay.onnx",
        "inputs": {"state": [1, OBS_DIM]},
        "outputs": {"actions": [1, ACT_DIM]},
        "actionLayout": [
            "move_x",
            "move_z",
            "turn",
            "jump_logit",
            "attack_logit",
            "defend_logit",
            "skill1_logit",
            "skill2_logit",
            "skill3_logit",
            "target_cycle_logit",
            "bark_trigger_logit",
            "bark_category_raw",
        ],
        "obsBlocks": {
            "locomotion": [0, 18],
            "rays": [18, 26],
            "combat_self": [26, 38],
            "combat_target": [38, 52],
            "agent_z": [52, 76],
            "memory_hint": [76, 84],
            "ledger_mood": [84, 90],
            "season": [90, 96],
        },
        "raySlots": {
            "forward": 18,
            "forwardLeft": 19,
            "forwardRight": 20,
            "left": 21,
            "right": 22,
            "cliffSafe": 23,
            "forwardBlocked": 24,
            "flankBias": 25,
        },
        "inferenceHz": 15,
        "backend": "CPU",
        "activationsInUnity": {
            "move_x": "Tanh",
            "move_z": "Tanh",
            "turn": "Tanh",
            "jump_logit": "Sigmoid",
            "attack_logit": "Sigmoid",
            "defend_logit": "Sigmoid",
            "skill1_logit": "Sigmoid",
            "skill2_logit": "Sigmoid",
            "skill3_logit": "Sigmoid",
            "target_cycle_logit": "Sigmoid",
            "bark_trigger_logit": "Sigmoid",
            "bark_category_raw": "Clamp -1..1 → bucket",
        },
    }
    (out_dir / "gameplay_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return metrics


if __name__ == "__main__":
    trained = ROOT / "trained"
    m = train_and_export(trained)
    print(json.dumps(m, indent=2))
    print(f"Wrote {trained / 'competition_gameplay.onnx'}")
