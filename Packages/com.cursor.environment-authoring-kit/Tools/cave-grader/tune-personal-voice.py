#!/usr/bin/env python3
"""Render Personal Voice A/B samples on Desktop — pick the preset that sounds most like you."""
from __future__ import annotations

import importlib.util
import json
import shutil
import sys
from pathlib import Path

from envkit_paths import approved_dir

APPROVED = approved_dir()
TEXT = (
    "This is my world build recap voice. I'm walking through terrain layout, "
    "the grid contract, and seam passes — calm director commentary."
)
PRESETS: list[tuple[str, dict]] = [
    (
        "fluent",
        {
            "narratorRawPersonalVoice": False,
            "narratorAutotune": True,
            "narratorPersonalDelivery": "fluent",
            "narratorAutotuneMode": "fluent",
            "narratorPersonalSettings": {
                "delivery": "fluent",
                "autotuneMode": "fluent",
                "fluentEnvelope": 0.58,
                "fluentGapMs": 16,
            },
        },
    ),
    (
        "autotune",
        {
            "narratorRawPersonalVoice": False,
            "narratorAutotune": True,
            "narratorPersonalDelivery": "autotune",
            "narratorAutotuneMode": "smooth",
            "narratorPersonalSettings": {
                "rawPersonalVoice": False,
                "delivery": "autotune",
                "autotuneMode": "smooth",
                "autotuneStrength": 0.42,
                "autotuneDryMix": 0.48,
                "autotuneSmoothMs": 120,
            },
        },
    ),
    (
        "raw",
        {
            "narratorRawPersonalVoice": True,
            "narratorPersonalDelivery": "raw",
            "narratorPersonalSettings": {"rawPersonalVoice": True, "delivery": "raw"},
        },
    ),
    (
        "light",
        {
            "narratorRawPersonalVoice": False,
            "narratorPersonalDelivery": "light",
            "narratorPersonalSettings": {"rawPersonalVoice": False, "delivery": "light"},
        },
    ),
    (
        "natural",
        {
            "narratorRawPersonalVoice": False,
            "narratorPersonalDelivery": "natural",
            "narratorPersonalSettings": {"rawPersonalVoice": False, "delivery": "natural"},
        },
    ),
]


def load_narrator():
    p = Path(__file__).resolve().parent / "demo-recap-narrator.py"
    spec = importlib.util.spec_from_file_location("narr", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def base_spec(narr) -> dict:
    spec: dict = {
        "narratorEngine": "personal",
        "narratorRequirePersonal": True,
        "narratorPersonalVoice": "Jacob Adkins",
        "narratorPersonalHumanize": False,
        "narratorNaturalPauses": False,
        "narratorPreferMysay": True,
    }
    cards = APPROVED / "ApprovedCards.json"
    if cards.is_file():
        spec = narr.flatten_narrator_personal_settings(
            {**spec, **json.loads(cards.read_text(encoding="utf-8"))}
        )
    name_file = APPROVED / narr.PERSONAL_VOICE_NAME_FILE
    if name_file.is_file():
        spec["narratorPersonalVoice"] = name_file.read_text(encoding="utf-8").strip().splitlines()[0]
    return spec


def main() -> int:
    narr = load_narrator()
    base = base_spec(narr)
    rate = narr.personal_say_rate(base)
    desktop = Path.home() / "Desktop"
    print(f"Personal Voice tune @ {rate} wpm — {base.get('narratorPersonalVoice', 'Jacob Adkins')}\n")

    for label, overrides in PRESETS:
        spec = narr.flatten_narrator_personal_settings({**base, **overrides})
        out = desktop / f"PersonalVoice-Tune-{label}.wav"
        print(f"Rendering {label}…", flush=True)
        if not narr.speech_to_wav(TEXT, out, voice=str(spec["narratorPersonalVoice"]), engine="personal", spec=spec):
            print(f"  FAILED {label}", file=sys.stderr)
            return 1
        print(f"  → {out}")

    main_test = desktop / "PersonalVoice-Test.wav"
    shutil.copy2(desktop / "PersonalVoice-Tune-fluent.wav", main_test)
    print(
        f"\nListen (pick the closest to your real voice):\n"
        f"  afplay ~/Desktop/PersonalVoice-Tune-fluent.wav   ← recommended\n"
        f"  afplay ~/Desktop/PersonalVoice-Tune-autotune.wav\n"
        f"  afplay ~/Desktop/PersonalVoice-Tune-raw.wav\n"
        f"  afplay ~/Desktop/PersonalVoice-Tune-light.wav\n"
        f"  afplay ~/Desktop/PersonalVoice-Tune-natural.wav\n"
        f"\nPersonalVoice-Test.wav = autotune copy. Tell me: autotune, raw, light, or natural."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
