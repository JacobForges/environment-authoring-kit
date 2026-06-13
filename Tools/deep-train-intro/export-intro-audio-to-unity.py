#!/usr/bin/env python3
"""Export work/*.wav mixes to Assets/Resources/DeepTrainAcademy/Intro/*.mp3."""

from __future__ import annotations

import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
ROOT = SCRIPT_DIR.parents[1]
RES = ROOT / "Assets/Resources/DeepTrainAcademy/Intro"
WORK = SCRIPT_DIR / "work"
LISTEN = SCRIPT_DIR / "listen"

sys.path.insert(0, str(SCRIPT_DIR))
from intro_unity_export import export_listen_m4a, export_unity_audio


def main() -> int:
    pairs = [
        ("intro_vo", WORK / "intro_vo_full_mix.wav", LISTEN / "intro_vo_full.m4a"),
        ("intro_vo_preview", WORK / "intro_vo_preview_mix.wav", LISTEN / "intro_vo_preview.m4a"),
    ]
    ok = 0
    for stem, wav, m4a in pairs:
        if not wav.is_file():
            print(f"skip {stem}: missing {wav}", file=sys.stderr)
            continue
        out = export_unity_audio(wav, RES, stem)
        export_listen_m4a(wav, m4a)
        print(f"{stem} → {out.relative_to(ROOT)} ({out.stat().st_size // 1024} KB)")
        ok += 1

    if ok == 0:
        print("No mixes found — run render-intro-vo-full.sh first.", file=sys.stderr)
        return 1
    print("Reimport in Unity (or focus Hub) — intro_vo loads via Resources.Load without extension.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
