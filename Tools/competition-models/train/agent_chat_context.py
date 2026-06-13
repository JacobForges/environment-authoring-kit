#!/usr/bin/env python3
"""Context vector for agent personality chat — matches AgentPersonalityContextEncoder.cs."""
from __future__ import annotations

import re

import numpy as np

from text_encoder import encode_text

CONTEXT_DIM = 128
MAX_MEMORY_FACTS = 8
PRIMARY_MEMORY_FACTS = 4


def _encode_fact_block(fact: str, base: int, vec: np.ndarray) -> None:
    f = re.sub(r"\s+", " ", (fact or "").lower().strip())[:64]
    if not f:
        return
    vec[base] = min(len(f), 48) / 48.0
    for word in f.split()[:6]:
        vec[base + 1] += 0.12
        vec[base + 2] = (vec[base + 2] + (abs(hash(word)) % 17) / 17.0) * 0.5
    vec[base + 3] = 1.0


def encode_memory_facts(facts: list[str] | None) -> np.ndarray:
    vec = np.zeros(16, dtype=np.float32)
    if not facts:
        return vec
    for i, fact in enumerate(facts[:PRIMARY_MEMORY_FACTS]):
        _encode_fact_block(fact, i * 4, vec)
    n = float(np.linalg.norm(vec))
    if n > 1e-6:
        vec /= n
    return vec


def encode_extended_memory_facts(facts: list[str] | None) -> np.ndarray:
    """Facts 4–7 land in context[97:113] — grows effective chat memory without resizing ONNX input."""
    vec = np.zeros(16, dtype=np.float32)
    if not facts or len(facts) <= PRIMARY_MEMORY_FACTS:
        return vec
    for i, fact in enumerate(facts[PRIMARY_MEMORY_FACTS:MAX_MEMORY_FACTS]):
        _encode_fact_block(fact, i * 4, vec)
    n = float(np.linalg.norm(vec))
    if n > 1e-6:
        vec /= n
    return vec


def encode_context(
    user_text: str,
    *,
    hp_ratio: float = 1.0,
    is_owner: bool = True,
    owner_chat_count: int = 0,
    social_confidence: float = 0.2,
    permission_pending: bool = False,
    behavior_mode: int = 0,
    is_yes_no: bool = False,
    memory_facts: list[str] | None = None,
) -> np.ndarray:
    """Build float[128] context for the personality chat ONNX."""
    vec = np.zeros(CONTEXT_DIM, dtype=np.float32)
    vec[0:64] = encode_text(user_text)
    vec[64:80] = encode_memory_facts(memory_facts)
    vec[97:113] = encode_extended_memory_facts(memory_facts)

    vec[80] = float(np.clip(hp_ratio, 0.0, 1.0))
    vec[81] = 1.0 if is_owner else 0.0
    vec[82] = min(owner_chat_count, 200) / 200.0
    vec[83] = float(np.clip(social_confidence, 0.0, 1.0))
    vec[84] = 1.0 if permission_pending else 0.0
    vec[85] = 1.0 if is_yes_no else 0.0
    vec[86] = behavior_mode / 6.0

  # Game-ish situational hints from message
    t = (user_text or "").lower()
    if any(k in t for k in ("cave", "underground", "tunnel")):
        vec[87] = 1.0
    if any(k in t for k in ("portal", "mouth", "entrance")):
        vec[88] = 1.0
    if any(k in t for k in ("trail", "path", "route")):
        vec[89] = 1.0
    if any(k in t for k in ("yes", "no", "ok", "sure", "nah")):
        vec[90] = 1.0
    if "?" in t:
        vec[91] = 1.0

    n = float(np.linalg.norm(vec))
    if n > 1e-6:
        vec /= n
    return vec
