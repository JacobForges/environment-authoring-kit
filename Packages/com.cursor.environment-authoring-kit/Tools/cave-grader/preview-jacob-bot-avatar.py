#!/usr/bin/env python3
"""Jacob's Bot approval preview — 3 poses on intro slide. Approve before full avatar bake."""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path


def _load():
    p = Path(__file__).resolve().parent / "jacob-adkins-bot.py"
    spec = importlib.util.spec_from_file_location("bot", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def main() -> int:
    if len(sys.argv) < 2:
        print(
            "Usage: preview-jacob-bot-avatar.py <capture_folder>\n"
            "Writes DemoRecapApproved/JacobAdkinsBot-Avatar/JacobsBot-ApprovalPreview.png",
            file=sys.stderr,
        )
        return 1

    capture = Path(sys.argv[1]).expanduser().resolve()
    bot = _load()

    try:
        from envkit_paths import approved_dir, approved_cards_path, ensure_recap_process_env

        ensure_recap_process_env()
        cards = {}
        if approved_cards_path().is_file():
            cards = json.loads(approved_cards_path().read_text(encoding="utf-8"))
        out = approved_dir() / "JacobAdkinsBot-Avatar" / "JacobsBot-ApprovalPreview.png"
    except ImportError:
        cards = {}
        out = capture / "JacobsBot-ApprovalPreview.png"

    ok = bot.generate_avatar_approval_preview(capture, out, spec=cards)
    if not ok:
        return 1
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
