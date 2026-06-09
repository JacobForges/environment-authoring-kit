#!/usr/bin/env python3
"""Resume lip-sync bake from existing Unity composite PNGs → DirectorPreview.mp4."""
from __future__ import annotations

import importlib.util
import json
import math
import shutil
import sys
from pathlib import Path

GRADER = Path(__file__).resolve().parent
sys.path.insert(0, str(GRADER))


def load(name: str, file: str):
    p = GRADER / file
    spec = importlib.util.spec_from_file_location(name, p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: finish-director-preview.py <capture_folder>", file=sys.stderr)
        return 1

    capture = Path(sys.argv[1]).expanduser().resolve()
    work = capture / "_presentation_compose_preview120"
    out = capture / "DirectorPreview.mp4"
    narr_wav = work / "narration.wav"
    final = work / "_final_video.mp4"
    bot_work = work / "_bot_lipsync"
    seq = bot_work / "composite"

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
    spec.update(
        {
            "botAvatarRequireUnity": True,
            "botAvatarAllowProceduralFallback": False,
        }
    )

    if not final.is_file():
        print(f"ERROR: missing {final}", file=sys.stderr)
        return 1
    if not narr_wav.is_file() or not narr.wav_is_audible(narr_wav):
        print(f"ERROR: missing audible {narr_wav}", file=sys.stderr)
        return 1

    ffmpeg = compose.find_ffmpeg()
    ffprobe = narr.find_ffprobe(ffmpeg)
    dur = probe_duration(final, ffprobe=ffprobe)
    fps = max(6, min(24, int(spec.get("botAvatarLipSyncFps") or 15)))
    frame_count = max(1, int(math.ceil(dur * fps)))
    envelope = bot._audio_envelope(narr_wav, fps, frame_count)
    png_count = len(list(seq.glob("f_*.png")))
    print(
        f"Resume: {png_count} composite PNGs, {frame_count} expected @ {fps}fps, "
        f"video {dur:.1f}s",
        flush=True,
    )
    if png_count < frame_count * 0.98:
        print(f"ERROR: too few PNGs ({png_count}/{frame_count})", file=sys.stderr)
        return 1

    print("Post-processing host mouth overlays…", flush=True)
    bot._postprocess_host_frames(seq, envelope, spec=spec, run_dir=capture)
    print("Trimming avatar tiles…", flush=True)
    bot._trim_overlay_sequence(seq, spec)

    lipsync_mov = bot_work / f"{bot.LIPSYNC_BASENAME}.mov"
    print(f"Encoding {lipsync_mov.name}…", flush=True)
    bot._encode_png_sequence(seq, "f_%04d.png", fps, lipsync_mov, ffmpeg)
    manifest = {
        "name": bot.BOT_NAME,
        "avatarEngine": "unity",
        "botAvatarSource": bot._bot_avatar_source(spec),
        "botAvatarModel": str(spec.get("botAvatarModel") or bot.UNITY_CYBORG_MODEL),
        "renderVersion": bot.AVATAR_RENDER_VERSION,
        "frameCount": frame_count,
        "fps": fps,
        "durationSec": dur,
    }
    (bot_work / f"{bot.LIPSYNC_MANIFEST_BASENAME}.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )

    bot_video = work / "_final_video_bot.mp4"
    print("Overlaying avatar on graded video…", flush=True)
    bot.apply_animated_avatar_to_video(ffmpeg, final, bot_video, lipsync_mov, spec=spec)
    if not bot_video.is_file():
        print("ERROR: avatar overlay failed", file=sys.stderr)
        return 1

    vol = max(narr.narrator_mux_volume(spec), 1.25)
    mux_wav = work / "_narration_mux.wav"
    shutil.copy2(narr_wav, mux_wav)
    print("Muxing preserved Personal Voice narration…", flush=True)
    narr.mux_narration_video_only(bot_video, mux_wav, out, volume=vol, spec=spec)
    if not out.is_file() or out.stat().st_size < 1024:
        print("ERROR: mux failed", file=sys.stderr)
        return 1

    mirror = envkit.preview_mirror_dir() / "DirectorPreview.mp4"
    try:
        shutil.copy2(out, mirror)
        print(f"Preview mirror: {mirror}", flush=True)
    except OSError as exc:
        print(f"Mirror skipped: {exc}", flush=True)

    hub_copy = (
        Path("/Users/jacob/Hub/Assets/EnvironmentKit/Generated/JacobsBot-Approval")
        / "DirectorPreview-v14.mp4"
    )
    hub_copy.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(out, hub_copy)
    print(f"Hub copy: {hub_copy}", flush=True)
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
