#!/usr/bin/env python3
"""Preview ONLY the approved emerald intro (c) + outro (b) — not milestone holds."""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

from PIL import Image


def main() -> int:
    ap = argparse.ArgumentParser(description="Copy approved intro/outro for visual check before compose")
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--regen", action="store_true")
    args = ap.parse_args()
    run = args.run_dir.expanduser().resolve()
    tools = Path(__file__).resolve().parent

    cmd = [sys.executable, str(tools / "apply-approved-cards.py"), str(run)]
    if args.regen:
        cmd.append("--regen")
    subprocess.run(cmd, check=True)

    intro = run / "ApprovedIntro.png"
    outro = run / "ApprovedOutro.png"
    if not intro.is_file() or not outro.is_file():
        raise SystemExit("Missing ApprovedIntro.png or ApprovedOutro.png")

    # What you will actually see at start/end of DemoRecap.mp4 (full 1280×720 cards).
    preview_intro = run / "PreviewIntro.png"
    preview_outro = run / "PreviewOutro.png"
    Image.open(intro).save(preview_intro, quality=95)
    Image.open(outro).save(preview_outro, quality=95)

    combo = Image.new("RGB", (1280, 1440))
    combo.paste(Image.open(preview_intro).convert("RGB"), (0, 0))
    combo.paste(Image.open(preview_outro).convert("RGB"), (0, 720))
    combo_path = run / "PreviewIntroOutro_combo.png"
    combo.save(combo_path, quality=95)

    cards = json.loads((run / "ApprovedCards.json").read_text()) if (run / "ApprovedCards.json").is_file() else {}
    html = run / "PreviewCards.html"
    html.write_text(
        f"""<!DOCTYPE html><html><head><meta charset=utf-8>
<title>Approved cards — intro {cards.get('introVariant','c')} · outro {cards.get('outroVariant','b')}</title>
<style>body{{font-family:system-ui;background:#0a1210;color:#e8fff4;padding:24px;max-width:1320px;margin:auto}}
h1{{color:#18eb9e}}img{{width:100%;border-radius:12px;border:2px solid #18eb9e44;margin:12px 0}}
p{{color:#9ab}}</style></head><body>
<h1>These are the intro/outro frames in your next encode</h1>
<p>Intro c — text only, emerald UI, no portrait. Outro b — thank you + portrait + next video line.</p>
<h2>Intro</h2><img src="{preview_intro.name}" alt="intro">
<h2>Outro</h2><img src="{preview_outro.name}" alt="outro">
</body></html>""",
        encoding="utf-8",
    )

    print("\nYour approved cards (intro c · outro b):")
    print(f"  Intro:  {preview_intro}")
    print(f"  Outro:  {preview_outro}")
    print(f"  Both:   {combo_path}")
    print(f"  Web:    {html}")
    print("\n(Ignore PreviewBeat_* — that was a mistake.)")

    desktop = Path.home() / "Desktop" / "DemoRecap-Card-Preview"
    desktop.mkdir(parents=True, exist_ok=True)
    copies = [
        (preview_intro, desktop / "1-Intro-emerald-no-portrait.png"),
        (preview_outro, desktop / "2-Outro-emerald-with-portrait.png"),
        (combo_path, desktop / "3-Intro-and-Outro-stacked.png"),
        (html, desktop / "OPEN-IN-SAFARI.html"),
    ]
    for src, dst in copies:
        dst.write_bytes(src.read_bytes())
    print(f"\nCopied to Desktop (double-click in Finder):")
    print(f"  {desktop}/")
    for _, dst in copies:
        print(f"    {dst.name}")

    if sys.platform == "darwin":
        import subprocess

        subprocess.run(["open", str(desktop)], check=False)
        print("\nFinder should open: Desktop → DemoRecap-Card-Preview")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
