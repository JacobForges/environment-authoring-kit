#!/usr/bin/env python3
"""Shared text → float[64] encoder for chat intent (trainer + test_platform)."""
from __future__ import annotations

import re

import numpy as np

FEATURE_DIM = 64
MAX_CHARS = 96

# Fixed slots 0..17 mirror INTENTS order in chat_intent_trainer.py
INTENT_KEYWORDS: tuple[tuple[str, ...], ...] = (
    ("hold", "stop", "halt", "freeze", "stay put", "don't move", "wait here"),
    ("follow me", "follow", "come with", "stick with", "on your six", "trail me"),
    ("rush", "sprint", "hurry", "go go", "run fast", "full speed", "speed run"),
    ("attack", "fight", "engage", "strike", "kill it", "melee", "hit them"),
    ("defend", "block", "guard", "shield", "parry", "brace", "protect yourself"),
    ("skill", "ability", "cast", "hotbar", "use skill", "use your power"),
    ("cancel", "abort", "never mind", "stand down", "forget it", "clear order"),
    ("hi", "hello", "hey", "howdy", "good morning", "what's up", "sup"),
    ("how are you", "hp", "health", "status report", "stamina", "are you ok", "condition"),
    ("plan", "strategy", "which way", "checkpoint", "route", "tactics", "where should"),
    ("nice", "good job", "keep going", "well done", "you got this", "let's go", "cheer"),
    ("slow", "weak", "loser", "trash", "bet you can't", "too slow", "roast"),
    ("what did you learn", "memory", "remember", "knowledge", "recall", "lessons", "facts"),
    ("ouch", "you're hurt", "low hp", "dying", "painful", "took damage", "critical"),
    ("we won", "victory", "finished", "goal", "made it", "success", "cleared"),
    ("stuck", "wedged", "snagged", "can't move", "path blocked", "help stuck"),
    ("nearby", "over here", "whisper", "local chat", "psst", "tell them"),
)


def encode_text(text: str) -> np.ndarray:
    t = re.sub(r"\s+", " ", (text or "").lower().strip())[:MAX_CHARS]
    vec = np.zeros(FEATURE_DIM, dtype=np.float32)
    if not t:
        return vec

    # Keyword slots (strong signal)
    for i, keys in enumerate(INTENT_KEYWORDS):
        if i >= 18:
            break
        for kw in keys:
            if kw in t:
                vec[i] = 1.0
                break

    # Exact short greetings / queries
    if t in ("hi", "hey", "yo", "hello", "howdy", "sup"):
        vec[7] = 2.0
    if t in ("how are you", "how are you?", "status", "hp", "hp?"):
        vec[8] = 2.0

    # Direct agent lines (no leading @) boost command slots when command verb present
    if not t.startswith("@") or t.startswith("agent "):
        vec[62] = 1.0
        rest = t[6:].strip() if t.lower().startswith("agent ") else t
        for i in range(7):
            for kw in INTENT_KEYWORDS[i]:
                if kw in rest:
                    vec[i] = max(vec[i], 1.5)

    # Stable word hashes (slots 18-61)
    for word in t.split():
        h = abs(hash(word)) % 44
        vec[18 + h] += 0.35

    vec[63] = min(len(t), 80) / 80.0
    n = float(np.linalg.norm(vec))
    if n > 1e-6:
        vec /= n
    return vec
