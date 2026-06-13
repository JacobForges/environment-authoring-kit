#!/usr/bin/env python3
"""Character vocabulary for competition_agent_chat.onnx (mirrored in C#)."""
from __future__ import annotations

PAD = 0
SOS = 1
EOS = 2
CHAR_START = 3
CHAR_END = 3 + (126 - 32 + 1)  # printable ASCII 32..126
VOCAB_SIZE = CHAR_END
MAX_REPLY_LEN = 56


def char_to_id(ch: str) -> int:
    o = ord(ch)
    if 32 <= o <= 126:
        return CHAR_START + (o - 32)
    return PAD


def encode_reply(text: str) -> list[int]:
    t = (text or "").strip()
    ids = [SOS]
    for ch in t[: MAX_REPLY_LEN - 2]:
        ids.append(char_to_id(ch))
    ids.append(EOS)
    while len(ids) < MAX_REPLY_LEN:
        ids.append(PAD)
    return ids[:MAX_REPLY_LEN]


def decode_reply(ids: list[int] | tuple[int, ...]) -> str:
    out: list[str] = []
    for i in ids:
        if i == EOS or i == PAD:
            break
        if i == SOS:
            continue
        if CHAR_START <= i < CHAR_END:
            out.append(chr(32 + (i - CHAR_START)))
    return "".join(out).strip()
