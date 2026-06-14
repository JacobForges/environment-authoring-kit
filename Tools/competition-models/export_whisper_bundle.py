#!/usr/bin/env python3
"""Export whisper-tiny.en metadata + optional ONNX stub for competition tooling."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VOICE = ROOT / "Tools" / "competition-voice"
MODELS = VOICE / "models"
STREAMING = ROOT / "Assets" / "StreamingAssets" / "Competition" / "Voice"


def main() -> None:
    parser = argparse.ArgumentParser(description="Whisper bundle manifest for Hub competition voice.")
    parser.add_argument("--write-manifest", action="store_true")
    args = parser.parse_args()

    manifest = {
        "model": "ggml-tiny.en.bin",
        "engine": "whisper.cpp",
        "sampleRate": 16000,
        "language": "en",
        "setupScript": "Tools/competition-voice/setup-whisper-local.sh",
        "chatOnnxNote": "Personal chat ONNX (competition_agent_chat_heads) is the other tiered model — trained via Train Agent.",
    }

    if args.write_manifest:
        MODELS.mkdir(parents=True, exist_ok=True)
        out = MODELS / "whisper_manifest.json"
        out.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print(f"Wrote {out}")

    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
