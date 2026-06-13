#!/usr/bin/env python3
"""Synthesize intro VO with Piper narration voices (local ONNX)."""

from __future__ import annotations

import re
import wave
from pathlib import Path

import numpy as np
from piper import PiperVoice
from piper.config import SynthesisConfig

VOICES_DIR = Path(__file__).resolve().parent / "voices"


def load_voice_manifest() -> dict:
    import json

    path = VOICES_DIR / "voices.json"
    if path.is_file():
        return json.loads(path.read_text(encoding="utf-8"))
    return {"default": "en_US-lessac-high", "prospector": "en_US-john-medium"}


def voice_path(name: str) -> Path:
    onnx = VOICES_DIR / f"{name}.onnx"
    if not onnx.is_file():
        raise FileNotFoundError(f"missing voice model: {onnx} — run download-intro-narration-voices.sh")
    return onnx


def split_sentences(text: str) -> list[str]:
    parts = re.split(r"(?<=[.!?])\s+", text.strip())
    return [p.strip() for p in parts if p.strip()]


def synthesize_to_mono_int16(
    text: str,
    voice_name: str,
    *,
    length_scale: float = 1.12,
    sentence_pause_s: float = 0.28,
) -> tuple[np.ndarray, int]:
    voice = PiperVoice.load(str(voice_path(voice_name)))
    sample_rate = voice.config.sample_rate
    chunks: list[np.ndarray] = []

    for sentence in split_sentences(text):
        shout = "DEEP TRAIN ACADEMY" in sentence.upper()
        syn = SynthesisConfig(
            length_scale=0.90 if shout else length_scale,
            volume=1.22 if shout else 1.0,
            noise_scale=0.62 if shout else 0.667,
            noise_w_scale=0.78,
        )
        for audio_chunk in voice.synthesize(sentence, syn_config=syn):
            samples = np.frombuffer(audio_chunk.audio_int16_bytes, dtype=np.int16)
            if audio_chunk.sample_channels > 1:
                samples = samples.reshape(-1, audio_chunk.sample_channels).mean(axis=1).astype(np.int16)
            chunks.append(samples)
        pause = int(sample_rate * (0.42 if shout else sentence_pause_s))
        chunks.append(np.zeros(pause, dtype=np.int16))

    if not chunks:
        return np.zeros(0, dtype=np.int16), sample_rate
    return np.concatenate(chunks), sample_rate


def write_wav(path: Path, samples: np.ndarray, sample_rate: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(samples.astype(np.int16).tobytes())
