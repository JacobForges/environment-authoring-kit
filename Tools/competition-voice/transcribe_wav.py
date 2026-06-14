#!/usr/bin/env python3
"""Transcribe mono 16 kHz WAV for Hub competition Listen (offline)."""
from __future__ import annotations

import argparse
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("wav")
    parser.add_argument("--model", default="tiny.en")
    args = parser.parse_args()

    wav = Path(args.wav)
    if not wav.is_file():
        print("", end="")
        return 1

    try:
        import whisper  # type: ignore
    except ImportError:
        print("", end="")
        return 2

    model = whisper.load_model(args.model)
    result = model.transcribe(str(wav), language="en", fp16=False)
    text = (result.get("text") or "").strip()
    print(text)
    return 0 if text else 1


if __name__ == "__main__":
    sys.exit(main())
