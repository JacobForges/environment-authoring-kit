#!/usr/bin/env python3
"""Fine-tune per-agent chat ONNX from dataset/chat.jsonl (+ synthetic base)."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
TRAIN = Path(__file__).resolve().parent
sys.path.insert(0, str(TRAIN))
sys.path.insert(0, str(ROOT))

from agent_chat_context import CONTEXT_DIM, encode_context  # noqa: E402
from agent_chat_trainer import train_and_export  # noqa: E402
from agent_chat_vocab import encode_reply  # noqa: E402

ACTS = {"reply": 0, "ask_question": 1, "ask_permission": 2, "ack": 3}
PLACES = {
    "portal": 0,
    "cave": 1,
    "trail": 2,
    "spawn": 3,
    "finish": 4,
    "landmark": 5,
    "follow_owner": 6,
    "wander": 7,
}


def default_agents_root() -> Path:
    return Path.home() / "Library/Application Support/DefaultCompany/Hub/Competition/Agents"


def load_agent_chat_rows(chat_path: Path) -> list[dict]:
    rows: list[dict] = []
    if not chat_path.is_file():
        return rows

    for line in chat_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return rows


def build_agent_tensors(rows: list[dict]) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray] | None:
    xs, ry, ay, py = [], [], [], []
    for row in rows:
        user = (row.get("user") or "").strip()
        reply = (row.get("reply") or "").strip()
        if len(user) < 2 or len(reply) < 2:
            continue

        facts = row.get("memoryFacts") or []
        ctx = encode_context(
            user,
            hp_ratio=float(row.get("hpRatio", 1.0)),
            is_owner=bool(row.get("isOwner", True)),
            owner_chat_count=int(row.get("ownerChatCount", 0)),
            social_confidence=float(row.get("socialConfidence", 0.2)),
            permission_pending=bool(row.get("permissionPending", False)),
            behavior_mode=int(row.get("behaviorMode", 0)),
            is_yes_no=bool(row.get("isYesNo", False)),
            memory_facts=facts if isinstance(facts, list) else None,
        )
        xs.append(ctx)
        ry.append(encode_reply(reply))
        act = row.get("act", "reply")
        ay.append(ACTS.get(act, 0))
        place = row.get("place", "wander")
        py.append(PLACES.get(place, 7))

    if not xs:
        return None

    return np.stack(xs), np.array(ry, dtype=np.int64), np.array(ay, dtype=np.int64), np.array(py, dtype=np.int64)


def finetune_agent(agent_id: str, agents_root: Path, min_rows: int = 8, epochs: int = 32) -> dict:
    chat_path = agents_root / agent_id / "dataset" / "chat.jsonl"
    agent_rows = load_agent_chat_rows(chat_path)
    if len(agent_rows) < min_rows:
        return {
            "ok": False,
            "message": f"Need {min_rows}+ chat samples (have {len(agent_rows)}). Talk to your agent more.",
        }

    out_dir = agents_root / agent_id / "models"
    ckpt_dir = out_dir / "checkpoints"
    ckpt_dir.mkdir(parents=True, exist_ok=True)

    # Reuse global trainer; agent chats are logged with full memory context for finetune.
    metrics = train_and_export(out_dir, epochs=epochs, extra_chat_path=chat_path)
    metrics["agentChatRows"] = len(agent_rows)
    metrics["ok"] = True
    metrics["message"] = (
        f"Chat ONNX fine-tuned on {len(agent_rows)} exchanges → "
        f"{out_dir / 'competition_agent_chat_heads.onnx'}"
    )

    for name in ("competition_agent_chat_heads.onnx", "competition_agent_chat_step.onnx"):
        src = out_dir / name
        if src.is_file():
            dst = ckpt_dir / f"chat_latest_{name}"
            dst.write_bytes(src.read_bytes())

    return metrics


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("agent_id")
    parser.add_argument("--agents-root", type=Path, default=default_agents_root())
    parser.add_argument("--min-rows", type=int, default=8)
    parser.add_argument("--epochs", type=int, default=32)
    args = parser.parse_args()

    result = finetune_agent(args.agent_id, args.agents_root, args.min_rows, args.epochs)
    print(json.dumps(result, indent=2))
    if not result.get("ok"):
        sys.exit(1)


if __name__ == "__main__":
    main()
