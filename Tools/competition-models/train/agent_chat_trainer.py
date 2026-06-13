#!/usr/bin/env python3
"""Train competition_agent_chat.onnx (+ step) — companion personality chat."""
from __future__ import annotations

import json
import random
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
TRAIN = Path(__file__).resolve().parent
sys.path.insert(0, str(TRAIN))
sys.path.insert(0, str(ROOT))

from agent_chat_context import CONTEXT_DIM, encode_context  # noqa: E402
from agent_chat_vocab import MAX_REPLY_LEN, SOS, VOCAB_SIZE, decode_reply, encode_reply  # noqa: E402

ACTS = ["reply", "ask_question", "ask_permission", "ack"]
PLACES = ["portal", "cave", "trail", "spawn", "finish", "landmark", "follow_owner", "wander"]

REPLY_SEEDS = [
    ("hey", "hey! trails look decent — i'm keeping pace."),
    ("how are you", "still upright. hp's ok. you good?"),
    ("what's up", "scouting the route. want me to check anything?"),
    ("lol", "ha — yeah that jump was sketchy."),
    ("nice", "thanks. felt clean that time."),
    ("this cave is cool", "cave vibes are strong. want me deeper or hang back?"),
    ("where should we go", "eyeing the portal first, but i'll follow your call."),
    ("you ok", "yeah — banged up a little. rest helps."),
    ("what do you think", "slow and steady beats face-planting again."),
    ("bored", "i could wander the trail or chill here."),
    ("tell me something", "palm shadows shifted — sun's dropping."),
    ("good job", "appreciate it. keeping pace."),
    ("ready", "ready when you are."),
    ("wait", "ok — holding here."),
    ("thanks", "anytime."),
    ("sup", "sup. world looks playable today."),
    ("hi there", "hi — i'm around if you need a scout."),
    ("any tips", "stick to lit ground. pits are rude."),
]

QUESTION_SEEDS = [
    ("hey", "you want me on the trail or near the portal?"),
    ("what now", "should i scout ahead or stick on you?"),
    ("bored", "want company at the cave mouth or nah?"),
    ("ok", "you feeling rushed or explore-y today?"),
    ("hmm", "should i check the cave or hug the trail?"),
]

PERMISSION_SEEDS = [
    ("go", "mind if i check the portal real quick?", 0),
    ("explore", "can i peek into the cave mouth?", 1),
    ("trail", "ok if i jog the surface trail a bit?", 2),
    ("finish", "want me to run toward the finish marker?", 4),
    ("scout", "permission to scout the landmark by the palms?", 5),
]

ACK_SEEDS = [
    ("yes", "bet — on it."),
    ("yeah", "cool, moving."),
    ("ok", "copy that."),
    ("no", "all good — staying put."),
    ("nope", "got it — won't go."),
]


def synthesize_rows() -> tuple:
    xs, reply_y, act_y, place_y = [], [], [], []
    rng = random.Random(2026)

    def add(user: str, reply: str, act: int, place: int = 7, **ctx):
        xs.append(encode_context(user, **ctx))
        reply_y.append(encode_reply(reply))
        act_y.append(act)
        place_y.append(place)

    for _ in range(6000):
        user, reply = rng.choice(REPLY_SEEDS)
        add(user, reply, 0, rng.randint(0, 7), hp_ratio=rng.uniform(0.35, 1.0), owner_chat_count=rng.randint(0, 80), social_confidence=rng.uniform(0.1, 0.9))

    for _ in range(1400):
        user, reply = rng.choice(QUESTION_SEEDS)
        add(user, reply, 1, social_confidence=rng.uniform(0.3, 1.0), owner_chat_count=rng.randint(5, 120))

    for _ in range(1100):
        user, reply, place = rng.choice(PERMISSION_SEEDS)
        add(user, reply, 2, place, social_confidence=rng.uniform(0.4, 1.0), owner_chat_count=rng.randint(10, 150))

    for _ in range(700):
        user, reply = rng.choice(ACK_SEEDS)
        add(user, reply, 3, permission_pending=True, is_yes_no=True)

    return np.stack(xs), np.array(reply_y, dtype=np.int64), np.array(act_y, dtype=np.int64), np.array(place_y, dtype=np.int64)


def _load_extra_chat_rows(extra_chat_path: Path | None) -> list[tuple]:
    if extra_chat_path is None or not extra_chat_path.is_file():
        return []
    import json

    rows = []
    for line in extra_chat_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            continue
        user = (row.get("user") or "").strip()
        reply = (row.get("reply") or "").strip()
        if len(user) < 2 or len(reply) < 2:
            continue
        facts = row.get("memoryFacts") if isinstance(row.get("memoryFacts"), list) else None
        ctx = encode_context(
            user,
            hp_ratio=float(row.get("hpRatio", 1.0)),
            is_owner=bool(row.get("isOwner", True)),
            owner_chat_count=int(row.get("ownerChatCount", 0)),
            social_confidence=float(row.get("socialConfidence", 0.25)),
            permission_pending=bool(row.get("permissionPending", False)),
            behavior_mode=int(row.get("behaviorMode", 0)),
            is_yes_no=bool(row.get("isYesNo", False)),
            memory_facts=facts,
        )
        act = int(row.get("actId", 0))
        place = int(row.get("placeId", 7))
        rows.append((ctx, encode_reply(reply), act, place))
    return rows


def train_and_export(out_dir: Path, epochs: int = 48, extra_chat_path: Path | None = None) -> dict:
    try:
        import torch
        import torch.nn as nn
        import torch.nn.functional as F
    except ImportError as e:
        raise SystemExit("pip install torch") from e

    x, ry, ay, py = synthesize_rows()
    extra = _load_extra_chat_rows(extra_chat_path)
    if extra:
        ex_x = np.stack([r[0] for r in extra])
        ex_ry = np.array([r[1] for r in extra], dtype=np.int64)
        ex_ay = np.array([r[2] for r in extra], dtype=np.int64)
        ex_py = np.array([r[3] for r in extra], dtype=np.int64)
        # Weight owner chat rows — grows personality + context slots used during training.
        boost = 4
        x = np.concatenate([x] + [ex_x] * boost, axis=0)
        ry = np.concatenate([ry] + [ex_ry] * boost, axis=0)
        ay = np.concatenate([ay] + [ex_ay] * boost, axis=0)
        py = np.concatenate([py] + [ex_py] * boost, axis=0)
    n = len(x)
    perm = np.random.default_rng(9).permutation(n)
    split = int(n * 0.88)
    tr, va = perm[:split], perm[split:]

    H = 176

    class Core(nn.Module):
        """Unity Inference Engine cannot import ONNX GRU — use MLP state instead."""

        def __init__(self):
            super().__init__()
            self.ctx_enc = nn.Sequential(nn.Linear(CONTEXT_DIM, H), nn.ReLU(), nn.Linear(H, H), nn.ReLU())
            self.token_emb = nn.Embedding(VOCAB_SIZE, 72)
            self.step_core = nn.Sequential(nn.Linear(72 + H + H, H), nn.ReLU(), nn.Linear(H, H), nn.ReLU())
            self.token_fc = nn.Linear(H, VOCAB_SIZE)
            self.act_head = nn.Linear(H, len(ACTS))
            self.place_head = nn.Linear(H, len(PLACES))

        def ctx_hidden(self, ctx: torch.Tensor) -> torch.Tensor:
            return self.ctx_enc(ctx)

        def step(self, ctx_h: torch.Tensor, hidden: torch.Tensor, token: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
            if token.dim() == 2:
                token = token.squeeze(1)
            emb = self.token_emb(token).unsqueeze(1)
            fused = torch.cat([emb, ctx_h.unsqueeze(1), hidden], dim=-1)
            out = self.step_core(fused)
            logits = self.token_fc(out[:, -1, :])
            return logits, out

        def decode_train(self, ctx_h: torch.Tensor, tokens: torch.Tensor) -> torch.Tensor:
            _, steps = tokens.shape
            hidden = ctx_h.unsqueeze(1)
            logits = []
            for i in range(steps):
                token = tokens[:, i]
                step_logits, hidden = self.step(ctx_h, hidden, token)
                logits.append(step_logits.unsqueeze(1))
            return torch.cat(logits, dim=1)

    class HeadsExport(nn.Module):
        def __init__(self, core: Core):
            super().__init__()
            self.core = core

        def forward(self, ctx: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
            h = self.core.ctx_hidden(ctx)
            return self.core.act_head(h), self.core.place_head(h), h.unsqueeze(0)

    class StepExport(nn.Module):
        def __init__(self, core: Core):
            super().__init__()
            self.core = core

        def forward(self, ctx: torch.Tensor, hidden: torch.Tensor, token: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
            h = self.core.ctx_hidden(ctx)
            logits, hidden = self.core.step(h, hidden, token)
            return logits, hidden

    device = torch.device("cpu")
    core = Core().to(device)
    opt = torch.optim.Adam(core.parameters(), lr=4e-4, weight_decay=1e-5)

    xt = torch.from_numpy(x[tr]).to(device)
    ryt = torch.from_numpy(ry[tr]).to(device)
    ayt = torch.from_numpy(ay[tr]).to(device)
    pyt = torch.from_numpy(py[tr]).to(device)

    ckpt_dir = out_dir / "checkpoints"
    ckpt_dir.mkdir(parents=True, exist_ok=True)

    for ep in range(epochs):
        core.train()
        opt.zero_grad()
        ctx_h = core.ctx_hidden(xt)
        inp = ryt[:, :-1]
        tgt = ryt[:, 1:]
        logits = core.decode_train(ctx_h, inp)
        loss_r = F.cross_entropy(logits.reshape(-1, VOCAB_SIZE), tgt.reshape(-1), ignore_index=0)
        loss_a = F.cross_entropy(core.act_head(ctx_h), ayt)
        loss_p = F.cross_entropy(core.place_head(ctx_h), pyt)
        loss = loss_r + 0.42 * loss_a + 0.28 * loss_p
        loss.backward()
        opt.step()
        if extra and (ep + 1) % 8 == 0:
            torch.save(core.state_dict(), ckpt_dir / f"chat_epoch_{ep + 1:03d}.pt")

    def greedy_decode(ctx_vec: np.ndarray) -> str:
        core.eval()
        with torch.no_grad():
            ctx = torch.from_numpy(ctx_vec.reshape(1, -1)).to(device)
            h = core.ctx_hidden(ctx)
            hidden = h.unsqueeze(1)
            token = torch.tensor([SOS], device=device)
            ids = []
            for _ in range(MAX_REPLY_LEN):
                logits, hidden = core.step(h, hidden, token)
                nxt = int(logits.argmax(1).item())
                if nxt == 2:  # EOS
                    break
                if nxt != 0 and nxt != 1:
                    ids.append(nxt)
                token = torch.tensor([nxt], device=device)
        return decode_reply(ids)

    core.eval()
    with torch.no_grad():
        xv = torch.from_numpy(x[va]).to(device)
        ctx_h = core.ctx_hidden(xv)
        inp = torch.from_numpy(ry[va]).to(device)[:, :-1]
        tgt = torch.from_numpy(ry[va]).to(device)[:, 1:]
        logits = core.decode_train(ctx_h, inp)
        val_loss = (
            F.cross_entropy(logits.reshape(-1, VOCAB_SIZE), tgt.reshape(-1), ignore_index=0)
            + 0.42 * F.cross_entropy(core.act_head(ctx_h), torch.from_numpy(ay[va]).to(device))
            + 0.28 * F.cross_entropy(core.place_head(ctx_h), torch.from_numpy(py[va]).to(device))
        ).item()
        act_acc = float((core.act_head(ctx_h).argmax(1).cpu().numpy() == ay[va]).mean())

    canary = {}
    with torch.no_grad():
        for user, want_act in [("hey", "reply"), ("what now", "ask_question"), ("scout", "ask_permission"), ("yes", "ack")]:
            feat = encode_context(
                user,
                social_confidence=0.7,
                owner_chat_count=20,
                permission_pending=(want_act == "ack"),
                is_yes_no=(want_act == "ack"),
            )
            ctx = torch.from_numpy(feat.reshape(1, -1)).to(device)
            act_i = int(core.act_head(core.ctx_hidden(ctx)).argmax(1).item())
            canary[user] = {"want_act": want_act, "got_act": ACTS[act_i], "reply": greedy_decode(feat)}

    out_dir.mkdir(parents=True, exist_ok=True)
    heads = HeadsExport(core).eval()
    step = StepExport(core).eval()

    torch.onnx.export(
        heads,
        torch.zeros(1, CONTEXT_DIM),
        str(out_dir / "competition_agent_chat_heads.onnx"),
        input_names=["context_features"],
        output_names=["act_logits", "place_logits", "initial_hidden"],
        dynamic_axes={"context_features": {0: "batch"}},
        opset_version=13,
    )
    torch.onnx.export(
        step,
        (torch.zeros(1, CONTEXT_DIM), torch.zeros(1, 1, H), torch.zeros(1, dtype=torch.long)),
        str(out_dir / "competition_agent_chat_step.onnx"),
        input_names=["context_features", "hidden", "token"],
        output_names=["token_logits", "hidden_out"],
        dynamic_axes={"context_features": {0: "batch"}, "hidden": {1: "batch"}, "token": {0: "batch"}},
        opset_version=13,
    )

    metrics = {
        "model": "competition_agent_chat",
        "contextDim": CONTEXT_DIM,
        "hiddenDim": H,
        "vocabSize": VOCAB_SIZE,
        "maxReplyLen": MAX_REPLY_LEN,
        "acts": ACTS,
        "places": PLACES,
        "valLoss": round(val_loss, 4),
        "actAccuracy": round(act_acc, 4),
        "epochs": epochs,
        "canary": canary,
        "files": ["competition_agent_chat_heads.onnx", "competition_agent_chat_step.onnx"],
    }
    (out_dir / "agent_chat_metrics.json").write_text(json.dumps(metrics, indent=2) + "\n", encoding="utf-8")

    manifest = {
        "version": 2,
        "headsFile": "competition_agent_chat_heads.onnx",
        "stepFile": "competition_agent_chat_step.onnx",
        "inputs": {"context_features": [1, CONTEXT_DIM]},
        "hiddenDim": H,
        "acts": {str(i): a for i, a in enumerate(ACTS)},
        "places": {str(i): p for i, p in enumerate(PLACES)},
        "wakeWord": "{ownerPlayerName}bot",
        "permissionTimeoutSeconds": 30,
    }
    (out_dir / "agent_chat_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return metrics


if __name__ == "__main__":
    trained = ROOT / "trained"
    m = train_and_export(trained)
    print(json.dumps(m, indent=2))
