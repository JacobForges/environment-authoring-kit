#!/usr/bin/env python3
"""Train competition_setup.onnx from synthetic Q&A → avatar + z + archetype labels."""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from export_competition_onnx import SETUP_HIDDEN, SETUP_IN, export_setup  # noqa: E402

ARCHETYPES = (
    "sprinter",
    "steady",
    "cautious",
    "explorer",
    "jumper",
    "safe",
    "bold",
    "balanced",
)

# Chip indices match OnboardingQuestionScript planning doc (Phase 6).
BUILD = (0.2, 0.5, 0.85)  # light med heavy
HEIGHT = (0.88, 1.0, 1.12)
STYLE = ((0.9, 0.82, 0.7), (0.45, 0.72, 0.42), (0.72, 0.68, 0.55))
COLORS = (
    (0.09, 0.92, 0.62),
    (0.78, 0.64, 0.32),
    (0.95, 0.45, 0.38),
    (0.55, 0.58, 0.62),
)
RACE = (0.95, 0.55, 0.25)
JUMP = (0.9, 0.5, 0.15)
STUCK = (0.3, 0.85, 0.1)
RISK = (0.9, 0.5, 0.15)
FOCUS = (0.85, 0.6, 0.35)
FALL = (0.8, 0.35, 0.55)


def _one_hot(idx: int, n: int) -> np.ndarray:
    v = np.zeros(n, dtype=np.float32)
    if 0 <= idx < n:
        v[idx] = 1.0
    return v


def encode_features(
    build: int,
    height: int,
    style: int,
    color: int,
    kit: int,
    race: int,
    jump: int,
    stuck: int,
    risk: int,
    focus: int,
    fall: int,
    name_seed: int = 0,
) -> np.ndarray:
    """100-d feature vector (documented layout for CompetitionFeatureEncoder)."""
    f = np.zeros(SETUP_IN, dtype=np.float32)
    off = 0
    f[off : off + 3] = _one_hot(build, 3)
    off += 3
    f[off : off + 3] = _one_hot(height, 3)
    off += 3
    f[off : off + 3] = _one_hot(style, 3)
    off += 3
    f[off : off + 4] = _one_hot(color, 4)
    off += 4
    f[off : off + 4] = _one_hot(kit, 4)
    off += 4
    f[off : off + 3] = _one_hot(race, 3)
    off += 3
    f[off : off + 3] = _one_hot(jump, 3)
    off += 3
    f[off : off + 3] = _one_hot(stuck, 3)
    off += 3
    f[off : off + 3] = _one_hot(risk, 3)
    off += 3
    f[off : off + 3] = _one_hot(focus, 3)
    off += 3
    f[off : off + 3] = _one_hot(fall, 3)
    off += 3
    rng = np.random.default_rng(name_seed & 0xFFFF)
    f[off : off + 16] = rng.normal(0, 0.15, 16).astype(np.float32)
    return f


def labels_for(
    build: int,
    height: int,
    style: int,
    color: int,
    kit: int,
    race: int,
    jump: int,
    stuck: int,
    risk: int,
    focus: int,
    fall: int,
) -> tuple[np.ndarray, np.ndarray, int]:
    avatar = np.zeros(12, dtype=np.float32)
    avatar[0] = BUILD[height]
    avatar[1] = BUILD[build]
    avatar[2] = 0.95 + build * 0.05
    avatar[3] = HEIGHT[height]
    avatar[4] = 0.92 + jump * 0.06
    avatar[5] = 1.0
    skin = STYLE[style]
    col = COLORS[color]
    avatar[6:9] = skin
    avatar[9:12] = col
    avatar[11] = float(kit)

    z = np.array(
        [
            RACE[race],
            JUMP[jump],
            STUCK[stuck],
            RISK[risk],
            FOCUS[focus],
            FALL[fall],
            BUILD[build],
            HEIGHT[height],
            float(kit) / 3.0,
            style / 2.0,
            color / 3.0,
            race - 1.0,
            jump - 1.0,
            risk - 1.0,
            focus - 1.0,
            fall - 1.0,
            0.5,
            0.5,
            0.5,
            0.5,
            0.0,
            0.0,
            0.0,
            0.0,
        ],
        dtype=np.float32,
    )

    if race == 0 and jump >= 1:
        arch = 0  # sprinter
    elif risk == 2:
        arch = 2  # cautious
    elif focus == 2:
        arch = 3  # explorer
    elif jump == 0:
        arch = 4  # jumper
    elif fall == 1:
        arch = 2
    elif race == 1 and risk == 1:
        arch = 7  # balanced
    elif risk == 0:
        arch = 5  # bold
    else:
        arch = 1  # steady
    return avatar, z, arch


def synthesize(n: int = 8000) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    xs, av, zz, ac = [], [], [], []
    rng = np.random.default_rng(2026)
    for i in range(n):
        picks = [rng.integers(0, 3) for _ in range(3)]
        picks += [rng.integers(0, 4) for _ in range(2)]
        picks += [rng.integers(0, 3) for _ in range(6)]
        avatar, z, arch = labels_for(*picks)
        xs.append(encode_features(*picks, name_seed=i))
        av.append(avatar)
        zz.append(z)
        ac.append(arch)
    return (
        np.stack(xs),
        np.stack(av),
        np.stack(zz),
        np.array(ac, dtype=np.int64),
    )


def train_and_export(out_dir: Path, epochs: int = 80) -> dict:
    try:
        import torch
        import torch.nn as nn
    except ImportError as e:
        raise SystemExit("pip install torch — required for training") from e

    x, av, z, ac = synthesize()
    n = len(x)
    split = int(n * 0.9)
    idx = np.random.default_rng(1).permutation(n)
    tr, va = idx[:split], idx[split:]

    class SetupNet(nn.Module):
        def __init__(self):
            super().__init__()
            h1, h2, h3 = SETUP_HIDDEN
            self.trunk = nn.Sequential(
                nn.Linear(SETUP_IN, h1),
                nn.ReLU(),
                nn.Linear(h1, h2),
                nn.ReLU(),
                nn.Linear(h2, h3),
                nn.ReLU(),
            )
            self.avatar = nn.Linear(h3, 12)
            self.z = nn.Linear(h3, 24)
            self.arch = nn.Linear(h3, 8)

        def forward(self, t):
            h = self.trunk(t)
            return self.avatar(h), self.z(h), self.arch(h)

    device = torch.device("cpu")
    model = SetupNet().to(device)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    mse = nn.MSELoss()
    ce = nn.CrossEntropyLoss()

    xt = torch.from_numpy(x[tr]).to(device)
    avt = torch.from_numpy(av[tr]).to(device)
    zt = torch.from_numpy(z[tr]).to(device)
    act = torch.from_numpy(ac[tr]).to(device)

    for ep in range(epochs):
        model.train()
        opt.zero_grad()
        pa, pz, pcl = model(xt)
        loss = mse(pa, avt) + mse(pz, zt) + 0.5 * ce(pcl, act)
        loss.backward()
        opt.step()

    model.eval()
    with torch.no_grad():
        xv = torch.from_numpy(x[va]).to(device)
        pav, pzv, pclv = model(xv)
        va_mse = mse(pav, torch.from_numpy(av[va]).to(device)).item()
        va_z = mse(pzv, torch.from_numpy(z[va]).to(device)).item()
        acc = (pclv.argmax(1).cpu().numpy() == ac[va]).mean()

    out_dir.mkdir(parents=True, exist_ok=True)
    onnx_path = out_dir / "competition_setup.onnx"
    dummy = torch.zeros(1, SETUP_IN)
    torch.onnx.export(
        model,
        dummy,
        str(onnx_path),
        input_names=["features"],
        output_names=["avatar", "z", "archetype"],
        dynamic_axes={
            "features": {0: "batch"},
            "avatar": {0: "batch"},
            "z": {0: "batch"},
            "archetype": {0: "batch"},
        },
        opset_version=13,
    )

    metrics = {
        "model": "competition_setup",
        "trainRows": int(split),
        "valRows": int(n - split),
        "valAvatarMse": round(va_mse, 6),
        "valZMse": round(va_z, 6),
        "valArchetypeAcc": round(float(acc), 4),
        "epochs": epochs,
        "note": "Synthetic supervised — replace with real onboarding logs later.",
    }
    (out_dir / "setup_metrics.json").write_text(json.dumps(metrics, indent=2) + "\n", encoding="utf-8")
    return metrics


if __name__ == "__main__":
    trained = ROOT / "trained"
    m = train_and_export(trained)
    print(json.dumps(m, indent=2))
    print(f"Wrote {trained / 'competition_setup.onnx'}")
