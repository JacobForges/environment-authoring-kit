#!/usr/bin/env python3
"""
Seed / inspect Storyteller and Einstein speech tiers for Hub Competition agents.

Data lives under Unity persistentDataPath:
  ~/Library/Application Support/JacobForges/Jacob's Portfolio Demo/Competition/

Usage:
  python3 Tools/competition-models/seed_speech_tier.py status
  python3 Tools/competition-models/seed_speech_tier.py seed-story
  python3 Tools/competition-models/seed_speech_tier.py seed-einstein
  python3 Tools/competition-models/seed_speech_tier.py seed-story --agent-id <id>
  python3 Tools/competition-models/seed_speech_tier.py reset-tier-seed

Close Unity Play Mode before seeding (or restart play after).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

EINSTEIN_MIN = 150
STORY_MIN_CHAT_ROWS = 24
STORY_MIN_CONF = 0.55
EINSTEIN_MIN_CONF = 0.62
CHAT_TRAIN_MIN = 8

DEFAULT_COMP_ROOT = Path.home() / (
    "Library/Application Support/JacobForges/Jacob's Portfolio Demo/Competition"
)
HUB_ROOT = Path(__file__).resolve().parents[2]
TRAINED_MODELS = HUB_ROOT / "Tools/competition-models/trained"


def comp_root() -> Path:
    return Path(os.environ.get("HUB_COMPETITION_ROOT", DEFAULT_COMP_ROOT))


def load_json(path: Path, default=None):
    if not path.is_file():
        return default
    try:
        return json.loads(path.read_text())
    except json.JSONDecodeError:
        return default


def active_agent_id(root: Path) -> str | None:
    profile = load_json(root / "player_profile.json", {})
    return profile.get("activeAgentId") or profile.get("activeAgentId".lower())


def count_chat_rows(agent_dir: Path) -> int:
    chat = agent_dir / "dataset" / "chat.jsonl"
    if not chat.is_file():
        return 0
    return sum(1 for line in chat.read_text().splitlines() if line.strip())


def has_personal_chat_onnx(agent_dir: Path) -> bool:
    models = agent_dir / "models"
    marker = models / "personal_chat_trained.marker"
    if marker.is_file():
        return True
    heads = models / "competition_agent_chat_heads.onnx"
    step = models / "competition_agent_chat_step.onnx"
    return heads.is_file() and step.is_file() and heads.stat().st_size > 1024 and step.stat().st_size > 1024


def load_train_cursor(agent_dir: Path) -> dict:
    path = agent_dir / "train_cursor.json"
    data = load_json(path, {})
    if not isinstance(data, dict):
        data = {}
    if data.get("cumulativeChatCheckpointed", 0) <= 0 and data.get("lastTrainUtc", 0) > 0:
        data["cumulativeChatCheckpointed"] = data.get("chatSamplesAtTrain", 0)
    return data


def resolve_tier(
    owner_chats: int,
    conf: float,
    chat_rows: int,
    has_onnx: bool,
    cumulative: int,
    has_trained: bool,
) -> str:
    einstein_ready = has_onnx and has_trained and cumulative >= EINSTEIN_MIN
    if einstein_ready and conf >= EINSTEIN_MIN_CONF:
        return "Einstein"
    if has_onnx and chat_rows >= STORY_MIN_CHAT_ROWS and conf >= STORY_MIN_CONF:
        return "Storyteller"
    if has_onnx and chat_rows >= CHAT_TRAIN_MIN:
        return "Trained"
    if owner_chats >= 12 and conf >= 0.35:
        return "Flow"
    if owner_chats >= 6 and conf >= 0.2:
        return "Warm"
    if owner_chats >= 2:
        return "Short"
    return "Brief"


def status(agent_dir: Path) -> None:
    mem = load_json(agent_dir / "personality_memory.json", {})
    owner = int(mem.get("ownerChatCount", 0))
    conf = float(mem.get("socialConfidence", 0.12))
    chat_rows = count_chat_rows(agent_dir)
    has_onnx = has_personal_chat_onnx(agent_dir)
    cursor = load_train_cursor(agent_dir)
    cumulative = int(cursor.get("cumulativeChatCheckpointed", 0))
    has_trained = int(cursor.get("lastTrainUtc", 0)) > 0
    tier = resolve_tier(owner, conf, chat_rows, has_onnx, cumulative, has_trained)

    print(f"Agent folder: {agent_dir}")
    print(f"Resolved tier: {tier}")
    print()
    print("Storyteller needs:")
    print(f"  personal chat ONNX:     {'OK' if has_onnx else 'MISSING'}")
    print(f"  chat.jsonl rows:        {chat_rows} / {STORY_MIN_CHAT_ROWS}")
    print(f"  socialConfidence:       {conf:.3f} / {STORY_MIN_CONF}")
    print()
    print("Einstein needs:")
    print(f"  cumulative checkpoint:  {cumulative} / {EINSTEIN_MIN}")
    print(f"  trained at least once:  {'OK' if has_trained else 'MISSING'}")
    print(f"  socialConfidence:       {conf:.3f} / {EINSTEIN_MIN_CONF}")
    print()
    print("In-game test (Play Mode, near agent):")
    print("  @ tell me a good story")
    print("  @ tell me about your last race")
    print("  continue   (after a split reply)")


def append_chat_rows(agent_dir: Path, count: int) -> None:
    dataset = agent_dir / "dataset"
    dataset.mkdir(parents=True, exist_ok=True)
    chat = dataset / "chat.jsonl"
    mem = load_json(agent_dir / "personality_memory.json", {})
    owner = int(mem.get("ownerChatCount", 0))
    conf = float(mem.get("socialConfidence", 0.12))
    now = int(time.time())
    lines = []
    samples = [
        ("hey how are you", "doing ok — keeping an eye on the trail."),
        ("tell me about the cave", "cave vibes are strong — want me deeper?"),
        ("remember that fight", "yeah we handled that guard near the trail."),
        ("what's the plan", "portal or trail — you lead or me?"),
        ("thanks for sticking close", "anytime — happy to hang."),
    ]
    for i in range(count):
        user, reply = samples[i % len(samples)]
        row = {
            "user": user,
            "reply": reply,
            "isOwner": True,
            "ownerChatCount": owner + i,
            "socialConfidence": min(1.0, conf + i * 0.01),
            "actId": 0,
            "placeId": 7,
            "memoryFacts": ["talked about trails"],
            "loggedUtc": now + i,
        }
        lines.append(json.dumps(row))
    with chat.open("a") as f:
        for line in lines:
            f.write(line + "\n")


def mark_personal_chat_trained(agent_dir: Path) -> None:
    """Tier gates check marker / onnx presence; runtime inference uses Resources ModelAssets."""
    models = agent_dir / "models"
    models.mkdir(parents=True, exist_ok=True)
    marker = models / "personal_chat_trained.marker"
    marker.write_text(
        json.dumps(
            {
                "trainedUtc": int(time.time()),
                "note": "Story/Einstein tier unlock. Chat inference loads from Assets/Resources/Competition/Models.",
            },
            indent=2,
        )
    )
    # Remove broken stub copies from older seed versions.
    for name in ("competition_agent_chat_heads.onnx", "competition_agent_chat_step.onnx"):
        path = models / name
        if path.is_file() and path.stat().st_size < 1024:
            path.unlink()
    print("Wrote personal_chat_trained.marker (tier unlock — not copied raw .onnx to avoid Sentis load errors).")
    res = HUB_ROOT / "Assets/Resources/Competition/Models/competition_agent_chat_heads.onnx"
    if not res.is_file():
        print("WARN: Resources chat models missing — run: Tools/competition-models/sync-to-streaming-assets.sh")


def write_personality(agent_dir: Path, owner_chats: int, conf: float) -> None:
    path = agent_dir / "personality_memory.json"
    data = load_json(path, {})
    data["ownerChatCount"] = owner_chats
    data["socialConfidence"] = conf
    data.setdefault("topics", ["talked about trails", "talked about the portal"])
    path.write_text(json.dumps(data, indent=4))


def write_train_cursor(agent_dir: Path, chat_rows: int, cumulative: int) -> None:
    path = agent_dir / "train_cursor.json"
    data = {
        "gameplaySamplesAtTrain": 24,
        "chatSamplesAtTrain": chat_rows,
        "cumulativeChatCheckpointed": cumulative,
        "lastTrainUtc": int(time.time()),
    }
    path.write_text(json.dumps(data, indent=4))


def seed_story(agent_dir: Path) -> None:
    target_rows = max(STORY_MIN_CHAT_ROWS, count_chat_rows(agent_dir))
    needed = max(0, STORY_MIN_CHAT_ROWS - count_chat_rows(agent_dir))
    if needed:
        append_chat_rows(agent_dir, needed)
        target_rows = count_chat_rows(agent_dir)
    mark_personal_chat_trained(agent_dir)
    write_personality(agent_dir, owner_chats=max(24, 24), conf=max(STORY_MIN_CONF, 0.58))
    write_train_cursor(agent_dir, chat_rows=target_rows, cumulative=min(target_rows, 40))
    seed_world_context(agent_dir)
    print(f"Seeded Storyteller prerequisites ({target_rows} chat rows).")
    status(agent_dir)


def seed_einstein(agent_dir: Path) -> None:
    existing = count_chat_rows(agent_dir)
    target_rows = max(EINSTEIN_MIN, existing)
    needed = max(0, EINSTEIN_MIN - existing)
    if needed:
        append_chat_rows(agent_dir, needed)
        target_rows = count_chat_rows(agent_dir)
    mark_personal_chat_trained(agent_dir)
    write_personality(agent_dir, owner_chats=max(40, 40), conf=max(EINSTEIN_MIN_CONF, 0.68))
    write_train_cursor(agent_dir, chat_rows=target_rows, cumulative=EINSTEIN_MIN)
    seed_world_context(agent_dir)
    print(f"Seeded Einstein prerequisites ({EINSTEIN_MIN} cumulative checkpoint).")
    status(agent_dir)


def seed_world_context(agent_dir: Path) -> None:
    """Give story composer something to narrate."""
    exp = agent_dir / "world_experience.json"
    if not exp.is_file():
        exp.write_text(json.dumps({
            "portal": True,
            "cave": True,
            "trail": True,
            "finish": False,
            "landmark": True,
        }, indent=4))

    ledger = agent_dir / "ledger.json"
    if not ledger.is_file():
        ledger.write_text(json.dumps({
            "strikeCount": 1,
            "recentRewardSum": 0.4,
            "checkpointIndex": 1,
            "mood": "neutral",
        }, indent=4))

    convo = agent_dir / "conversation_memory.json"
    if not convo.is_file():
        convo.write_text(json.dumps({
            "longTerm": ["[combat] We defeated CourseGuard", "We talked about trails"],
            "lastSessionSummary": "You asked about the last race. We talked about trails, fought enemies.",
            "updatedUtc": int(time.time()),
        }, indent=4))

    knowledge = agent_dir / "knowledge.jsonl"
    if not knowledge.is_file():
        knowledge.write_text(
            '{"category":"event","fact":"We defeated a guard near the trail","confidence":0.9}\n'
        )


def reset_seed(agent_dir: Path) -> None:
    for name in ("train_cursor.json",):
        p = agent_dir / name
        if p.is_file():
            p.unlink()
    mem_path = agent_dir / "personality_memory.json"
    if mem_path.is_file():
        mem = load_json(mem_path, {})
        mem["socialConfidence"] = 0.12
        mem["ownerChatCount"] = 0
        mem_path.write_text(json.dumps(mem, indent=4))
    print("Reset train cursor + lowered personality stats (chat.jsonl and ONNX kept).")
    status(agent_dir)


def main() -> int:
    parser = argparse.ArgumentParser(description="Seed Storyteller / Einstein speech tiers for testing")
    parser.add_argument(
        "command",
        choices=("status", "seed-story", "seed-einstein", "reset-tier-seed"),
    )
    parser.add_argument("--agent-id", help="Agent id (default: activeAgentId from player_profile.json)")
    parser.add_argument("--comp-root", type=Path, default=None, help="Override Competition folder")
    args = parser.parse_args()

    root = args.comp_root or comp_root()
    if not root.is_dir():
        print(f"Competition root not found: {root}", file=sys.stderr)
        return 1

    agent_id = args.agent_id or active_agent_id(root)
    if not agent_id:
        print("No activeAgentId in player_profile.json — pass --agent-id", file=sys.stderr)
        return 1

    agent_dir = root / "Agents" / agent_id
    if not agent_dir.is_dir():
        print(f"Agent folder not found: {agent_dir}", file=sys.stderr)
        return 1

    if args.command == "status":
        status(agent_dir)
    elif args.command == "seed-story":
        seed_story(agent_dir)
    elif args.command == "seed-einstein":
        seed_einstein(agent_dir)
    elif args.command == "reset-tier-seed":
        reset_seed(agent_dir)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
