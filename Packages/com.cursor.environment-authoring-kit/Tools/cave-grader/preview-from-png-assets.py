#!/usr/bin/env python3
"""Preview: existing avatar PNG sequence + silent graded video + preserved narration."""
from __future__ import annotations

import importlib.util
import json
import math
import subprocess
import sys
from pathlib import Path

GRADER = Path(__file__).resolve().parent
HUB = GRADER
while HUB != HUB.parent and not (HUB / "Assets" / "Scripts").is_dir():
    HUB = HUB.parent

DEFAULT_SEQ = Path(
    "/Volumes/Lexar/EnvironmentKit-Hub/DemoRecapApproved/JacobAdkinsBot-Avatar/_avatar_seq"
)
DEFAULT_SILENT = Path.home() / "Desktop/DemoRecap-Card-Preview/DirectorPreview-silent.mp4"
DEFAULT_AUDIO_SRC = Path.home() / "Desktop/DemoRecap-Card-Preview/DirectorPreview.mp4"
OUT_DIR = HUB / "Assets/EnvironmentKit/Generated/JacobsBot-Approval"


def load(name: str, file: str):
    p = GRADER / file
    spec = importlib.util.spec_from_file_location(name, p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def overlay_looped(
    ffmpeg: str, base: Path, avatar: Path, dst: Path, spec: dict, duration: float,
) -> None:
    bot = load("bot", "jacob-adkins-bot.py")
    scale = bot._avatar_overlay_scale(spec)
    margin_x = bot._avatar_overlay_margin_x(spec)
    margin_y = bot._avatar_overlay_margin_y(spec)
    corner = bot._avatar_overlay_corner(spec)
    x = str(margin_x) if "left" in corner else f"W-w-{margin_x}"
    y = str(margin_y) if "top" in corner else f"H-h-{margin_y}"
    filt = f"[1:v]scale=-1:{scale},format=rgba[bot];[0:v][bot]overlay={x}:{y}:format=auto"
    subprocess.run(
        [
            ffmpeg, "-y",
            "-i", str(base),
            "-stream_loop", "-1", "-i", str(avatar),
            "-filter_complex", filt,
            "-t", f"{duration:.3f}",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20",
            "-preset", "veryfast", "-an", str(dst),
        ],
        check=True,
        capture_output=True,
    )


def main() -> int:
    seq = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_SEQ
    silent = Path(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_SILENT
    audio_src = Path(sys.argv[3]) if len(sys.argv) > 3 else DEFAULT_AUDIO_SRC

    if not seq.is_dir() or not list(seq.glob("f_*.png")):
        print(f"ERROR: no avatar PNGs in {seq}", file=sys.stderr)
        return 1
    if not silent.is_file():
        print(f"ERROR: missing silent video {silent}", file=sys.stderr)
        return 1
    if not audio_src.is_file():
        print(f"ERROR: missing audio source {audio_src}", file=sys.stderr)
        return 1

    envkit = load("envkit", "envkit_paths.py")
    envkit.ensure_recap_process_env()
    narr = load("narrator", "demo-recap-narrator.py")
    compose = load("compose", "compose-presentation-recap.py")
    bot = load("bot", "jacob-adkins-bot.py")
    from hybrid_recap_common import probe_duration

    approved = envkit.approved_cards_path()
    spec: dict = {}
    if approved.is_file():
        spec = narr.flatten_narrator_personal_settings(
            json.loads(approved.read_text(encoding="utf-8"))
        )

    work = OUT_DIR / "_png_preview_work"
    work.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    ffmpeg = compose.find_ffmpeg()
    ffprobe = narr.find_ffprobe(ffmpeg)
    dur = probe_duration(silent, ffprobe=ffprobe)
    fps = max(6, min(24, int(spec.get("botAvatarLipSyncFps") or 15)))
    pngs = sorted(seq.glob("f_*.png"))
    print(f"Avatar PNGs: {len(pngs)} in {seq}", flush=True)
    print(f"Base video: {silent} ({dur:.1f}s)", flush=True)

    narr_wav = work / "narration_extracted.wav"
    print(f"Extracting narration from {audio_src}…", flush=True)
    subprocess.run(
        [ffmpeg, "-y", "-i", str(audio_src), "-vn", "-acodec", "pcm_s16le", str(narr_wav)],
        check=True,
        capture_output=True,
    )

    frame_count = len(pngs)
    envelope = bot._audio_envelope(narr_wav, fps, frame_count)
    print("Portrait mouth pass on saved PNGs…", flush=True)
    bot._postprocess_host_frames(seq, envelope, spec=spec, run_dir=work)

    avatar_mov = work / "avatar_from_pngs.mov"
    print(f"Encoding {avatar_mov.name}…", flush=True)
    bot._encode_png_sequence(seq, "f_%04d.png", fps, avatar_mov, ffmpeg)

    bot_video = work / "_silent_with_avatar.mp4"
    print("Overlaying looped avatar on graded video…", flush=True)
    overlay_looped(ffmpeg, silent, avatar_mov, bot_video, spec, dur)

    out = OUT_DIR / "DirectorPreview-from-PNGs.mp4"
    vol = max(narr.narrator_mux_volume(spec), 1.25)
    print("Muxing narration…", flush=True)
    narr.mux_narration_video_only(bot_video, narr_wav, out, volume=vol, spec=spec)

    desktop = Path.home() / "Desktop/DemoRecap-Card-Preview/DirectorPreview-from-PNGs.mp4"
    try:
        import shutil
        shutil.copy2(out, desktop)
        print(f"Desktop copy: {desktop}", flush=True)
    except OSError as exc:
        print(f"Desktop copy skipped: {exc}", flush=True)

    subprocess.run(["open", str(out)], check=False)
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
