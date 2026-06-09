#!/usr/bin/env python3
"""
Jacob Adkins Bot approval preview — screen B-roll, JavaFX, avatar, Personal Voice always.
Per-segment scripts aligned to on-screen content; seamless narration timeline.
"""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
OUT_DIR = Path.home() / "Desktop" / "HybridRecap-ApprovalPreview"

# Each cue matches seg_00..seg_06 video content in order
# Partial build on this capture — only describe on-screen facts; suggest future ideas, never promise.
PREVIEW_MAP_STATUS = "partial"

PREVIEW_SEGMENT_CUES = [
    # intro card
    (
        "Hey there — Jacob Adkins's Bot here, your guide for this world build. "
        "This is real Unity footage, not a slideshow, and this session is still mid-build — "
        "which is exactly why it is fun to watch. What you see now sets up deeper episodes later."
    ),
    # screen_1 — session start / FullWorld queue
    (
        "Watch the editor wake up — session start, right on screen. "
        "FullWorld is queued on tagged ground, and that starting disk is already shaping every choice ahead. "
        "I could imagine spawn routes and landmarks growing from here — suggestion only, not a promise."
    ),
    # screen_2 — opening terrain / slope read
    (
        "Now that opening slope — it is right there in frame. "
        "I can picture a climb-and-return loop along that ridge someday, maybe a gate and a key run. "
        "That is me guessing from the terrain, not something built yet — but the hill already tells a story."
    ),
    # hold_1 — brief milestone still
    (
        "Quick milestone pause — the queue keeps working underneath while the frame holds still. "
        "Calm picture, real progress happening off-camera in the build."
    ),
    # screen_3 — flat grid
    (
        "Flat grid phase — tiles landing one at a time, clean and readable on screen. "
        "You can see the spacing breathe before bigger drama shows up later. "
        "Later episodes might push mountains into this frame — for now, enjoy the layout taking shape."
    ),
    # screen_4 — mid terraform / foothills
    (
        "And there — foothills and peaks starting to wake up on screen. "
        "Color and height contrast are showing on the play disk now. "
        "I could see rope paths or mount routes fitting this terrain someday — pure imagination from what we have."
    ),
    # outro
    (
        "If this bot voice and pacing feel right — say approve. "
        "We will scale the full recap with the same energy, same truth rules: show what is real, suggest what might come. "
        "Hope you stick around for the next build."
    ),
]

PREVIEW_SCRIPT = " ".join(PREVIEW_SEGMENT_CUES)


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / file)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def main() -> int:
    if len(sys.argv) < 2:
        print(
            f"Usage: python3 {Path(__file__).name} /path/to/DemoCapture/<timestamp>",
            file=sys.stderr,
        )
        return 1

    capture = Path(sys.argv[1]).expanduser().resolve()
    wc = len(PREVIEW_SCRIPT.split())

    print("=== Jacob Adkins Bot approval preview ===", flush=True)
    r = subprocess.run(
        [sys.executable, str(_TOOLS / "compose-hybrid-approval-preview.py"), str(capture)],
        cwd=str(_TOOLS),
    )
    if r.returncode != 0:
        return r.returncode

    OUT_DIR.mkdir(exist_ok=True)
    preview_json = OUT_DIR / "DemoRecapPreviewNarration.json"
    preview_json.write_text(
        json.dumps(
            {
                "script": PREVIEW_SCRIPT,
                "segmentCues": list(PREVIEW_SEGMENT_CUES),
                "wordCount": wc,
                "persona": "Jacob Adkins Bot",
                "narrationScriptDensityMultiplier": 2.0,
                "syncMode": "speech_first",
                "narratorSeamlessTimeline": True,
                "mapCompletionStatus": PREVIEW_MAP_STATUS,
                "tone": "streamer_hype_truthful_suggestions",
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    (OUT_DIR / "DemoRecapPreviewNarration.txt").write_text(PREVIEW_SCRIPT + "\n", encoding="utf-8")
    print(f"Bot script: {preview_json} ({wc} words, {len(PREVIEW_SEGMENT_CUES)} aligned segments)")

    print("=== Personal Voice (Jacob Adkins Bot) ===", flush=True)
    mux = _load("mux", "mux-hybrid-approval-preview-narration.py")
    return mux.mux_preview_voice(capture)


if __name__ == "__main__":
    raise SystemExit(main())
