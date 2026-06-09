#!/usr/bin/env python3
"""
~90s approval preview — screen B-roll video + short plain holds, JavaFX video polish.
No caption cards, no annotations. Personal Voice mux always follows compose.
"""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

from hybrid_recap_common import (
    card_segment,
    concat_segments,
    find_ffmpeg,
    find_ffprobe,
    list_timelapse_frames,
    pick_evenly,
    png_segment,
    probe_duration,
    resolve_screen_recording,
    screen_segment,
)

_TOOLS = Path(__file__).resolve().parent
OUT_DIR = Path.home() / "Desktop" / "HybridRecap-ApprovalPreview"

# Video-first: screen B-roll at chapter cuts; brief plain PNG breathers (no caption overlay).
PREVIEW_PLAN = [
    ("intro", 12.0),
    ("screen_1", 20.0),
    ("screen_2", 18.0),
    ("hold_1", 5.0),
    ("screen_3", 16.0),
    ("screen_4", 14.0),
    ("outro", 8.0),
]


def _load_javafx():
    spec = importlib.util.spec_from_file_location(
        "javafx", _TOOLS / "demo-recap-javafx-effects.py"
    )
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def _javafx_bake(ffmpeg: str, src: Path, dst: Path) -> None:
    """Apply JavaFX-style ffmpeg polish (glow, bloom, grain, vignette) — video only."""
    jfx = _load_javafx()
    vf = jfx.javafx_ffmpeg_vf()
    subprocess.run(
        [
            ffmpeg, "-y", "-i", str(src),
            "-vf", vf,
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20",
            "-preset", "veryfast", "-an", str(dst),
        ],
        check=True,
        capture_output=True,
    )


def _segment_with_javafx(
    ffmpeg: str, build_fn, out: Path, *, avatar_overlay: Path | None = None
) -> None:
    raw = out.with_suffix(".raw.mp4")
    fx = out.with_suffix(".fx.mp4")
    build_fn(raw)
    _javafx_bake(ffmpeg, raw, fx)
    raw.unlink(missing_ok=True)
    if avatar_overlay and avatar_overlay.is_file():
        bot = importlib.util.spec_from_file_location(
            "bot", _TOOLS / "jacob-adkins-bot.py"
        )
        bot_mod = importlib.util.module_from_spec(bot)
        assert bot.loader
        bot.loader.exec_module(bot_mod)
        bot_mod.apply_avatar_to_video(ffmpeg, fx, out, avatar_overlay)
        fx.unlink(missing_ok=True)
    else:
        fx.replace(out)


def compose_approval_preview(capture: Path) -> Path:
    capture = capture.expanduser().resolve()
    ffmpeg = find_ffmpeg()
    ffprobe = find_ffprobe()
    frames = list_timelapse_frames(capture / "timelapse")
    mov = resolve_screen_recording(capture)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    work = OUT_DIR / "_work"
    work.mkdir(exist_ok=True)
    for old in work.glob("seg_*.mp4"):
        old.unlink(missing_ok=True)
    for old in work.glob("*.raw.mp4"):
        old.unlink(missing_ok=True)

    from envkit_paths import approved_dir

    approved = approved_dir()
    avatar_overlay: Path | None = None
    bot_spec = importlib.util.spec_from_file_location(
        "bot", _TOOLS / "jacob-adkins-bot.py"
    )
    bot_mod = importlib.util.module_from_spec(bot_spec)
    assert bot_spec.loader
    bot_spec.loader.exec_module(bot_mod)
    bot_mod.ensure_animated_avatar_asset(
        approved,
        ffmpeg,
        desktop_copy=OUT_DIR / "JacobAdkinsBot-Avatar",
    )
    # Lip-sync layered avatar is baked at mux time from narration audio
    avatar_overlay = None

    intro_png = approved / "ApprovedIntro.png"
    outro_png = approved_dir() / "ApprovedOutro.png"
    if not intro_png.is_file():
        intro_png = frames[0]
    if not outro_png.is_file():
        outro_png = frames[-1]

    from hybrid_recap_common import chapter_clips
    clips = chapter_clips(mov, ffmpeg, count=4)
    hold_pngs = pick_evenly(frames, 2)

    parts: list[Path] = []
    screen_i = 0
    for i, (kind, sec) in enumerate(PREVIEW_PLAN):
        out = work / f"seg_{i:02d}_{kind}.mp4"

        if kind == "intro":
            _segment_with_javafx(
                ffmpeg,
                lambda p: card_segment(ffmpeg, intro_png, p, sec),
                out,
                avatar_overlay=avatar_overlay,
            )
        elif kind == "outro":
            _segment_with_javafx(
                ffmpeg,
                lambda p: card_segment(ffmpeg, outro_png, p, sec),
                out,
                avatar_overlay=avatar_overlay,
            )
        elif kind.startswith("screen"):
            ch = min(screen_i, len(clips) - 1)
            screen_i += 1
            clip = clips[ch]
            dur = min(sec, clip["duration"])

            def _screen(p, c=clip, d=dur):
                screen_segment(ffmpeg, mov, p, c["start"], d)

            _segment_with_javafx(ffmpeg, _screen, out, avatar_overlay=avatar_overlay)
        elif kind.startswith("hold"):
            png = hold_pngs[0]
            _segment_with_javafx(
                ffmpeg,
                lambda p, img=png: png_segment(ffmpeg, img, p, sec),
                out,
                avatar_overlay=avatar_overlay,
            )
        parts.append(out)

    silent = OUT_DIR / "HybridRecap-ApprovalPreview-SILENT.mp4"
    concat_segments(ffmpeg, parts, silent)
    dur = probe_duration(silent, ffprobe)

    meta = {
        "purpose": "approval_preview",
        "doesNotReplace": str(capture / "HybridRecapPresentation.mp4"),
        "capture": str(capture),
        "silentMp4": str(silent),
        "finalMp4": str(OUT_DIR / "HybridRecap-ApprovalPreview.mp4"),
        "durationSec": round(dur, 2),
        "persona": "Jacob Adkins Bot",
        "style": "screen_broll + JavaFX polish + bot avatar overlay",
        "effects": "JavaFX polish + animated Jacob Adkins Bot avatar (downloadable .mov/.webm)",
        "botAvatarAssets": str(OUT_DIR / "JacobAdkinsBot-Avatar"),
        "voice": "Personal Voice always muxed after compose",
        "notThisFolder": str(Path.home() / "Desktop" / "HybridRecap-ConceptPreview"),
        "approveThenRun": f"python3 run-hybrid-recap-full.py {capture} --skip-compose --regen-script --with-narration",
    }
    (OUT_DIR / "preview-meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    print(f"Approval preview silent: {silent} ({dur:.1f}s)")
    return silent


def main() -> int:
    if len(sys.argv) < 2:
        print(f"Usage: {Path(__file__).name} /path/to/DemoCapture/<timestamp>", file=sys.stderr)
        return 1
    silent = compose_approval_preview(Path(sys.argv[1]))
    if sys.platform == "darwin":
        subprocess.run(["open", str(silent)], check=False)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
