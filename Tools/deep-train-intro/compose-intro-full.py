#!/usr/bin/env python3
"""Compose full intro audio — locked v5 script + timeline SFX + honed preview mix."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT_DIR = Path(__file__).resolve().parent
OUT_DIR = ROOT / "Assets/Resources/DeepTrainAcademy/Intro"
STREAMING_DIR = ROOT / "Assets/StreamingAssets/DeepTrainAcademy"
WORK = SCRIPT_DIR / "work"
LISTEN_DIR = SCRIPT_DIR / "listen"


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def ffprobe() -> str:
    return shutil.which("ffprobe") or "/opt/homebrew/bin/ffprobe"


def probe_duration(path: Path) -> float:
    out = subprocess.check_output(
        [
            ffprobe(),
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        text=True,
    ).strip()
    return float(out or 0.0)


def _ms(t: float) -> int:
    return int(round(t * 1000))


def build_mix_filter(
    *,
    vo_label: str,
    music_label: str,
    ambient_label: str,
    drip_label: str,
    footsteps_label: str,
    rustle_label: str,
    power_label: str,
    checkpoint_label: str,
    chime_label: str,
    rustle_s: float,
    power_s: float,
    cp_s: float,
    chime_s: float,
    total_s: float,
) -> str:
    parts: list[str] = []

    parts.append(f"[{ambient_label}:a]volume=1.0[amb]")
    parts.append(f"[{drip_label}:a]volume=1.0[drip]")
    parts.append(f"[{footsteps_label}:a]volume=1.0[feet]")
    parts.append("[amb][drip][feet]amix=inputs=3:duration=longest:weights=1 0.95 0.75:normalize=0[cave]")

    t_pre = max(chime_s - 0.35, 0.0)
    t_post = min(chime_s + 6.0, total_s)
    parts.append(
        f"[{music_label}:a]volume='if(between(t,{t_pre:.3f},{t_post:.3f}),1.72,1.0)'[mus]"
    )

    parts.append(f"[{vo_label}:a]volume=2.05[voraw]")
    parts.append("[voraw]pan=stereo|c0=0.98*c0|c1=0.98*c1[vo]")

    parts.append(
        f"[{rustle_label}:a]adelay={_ms(rustle_s)}|{_ms(rustle_s)},apad,atrim=0:0.7,volume=1.4,"
        f"pan=stereo|c0=0.25*c0|c1=0.85*c1[rst]"
    )
    parts.append(
        f"[{power_label}:a]adelay={_ms(power_s)}|{_ms(power_s)},apad,atrim=0:2.8,volume=1.6,"
        f"pan=stereo|c0=0.18*c0|c1=0.92*c1[pwr]"
    )
    parts.append(
        f"[{checkpoint_label}:a]adelay={_ms(cp_s)}|{_ms(cp_s)},apad,atrim=0:0.8,volume=1.5[cp]"
    )
    parts.append(
        f"[{chime_label}:a]adelay={_ms(chime_s)}|{_ms(chime_s)},apad,atrim=0:1.3,volume=1.5[chl]"
    )

    parts.append(
        "[cave][mus]amix=inputs=2:duration=longest:weights=0.9 1.0:normalize=0[under];"
        "[under][vo][rst][pwr][cp][chl]amix=inputs=6:duration=longest:"
        "weights=0.44 2.45 0.85 1.0 0.9 0.95:normalize=0,"
        "stereotools=balance_in=0.04:balance_out=0.10:slev=1.38,"
        "extrastereo=m=1.42,"
        "aecho=0.88:0.84:28|48:0.10|0.06,"
        "acompressor=threshold=-11dB:ratio=1.12:attack=45:release=320:makeup=1.02,"
        f"apad=pad_dur={total_s:.3f},atrim=0:{total_s:.3f},"
        "alimiter=limit=0.99:attack=50:release=220[aout]"
    )
    return ";".join(parts)


def export_listenables(wav: Path, *, stem: str, ogg_legacy: Path, m4a: Path) -> Path:
    sys.path.insert(0, str(SCRIPT_DIR))
    from intro_unity_export import export_listen_m4a, export_unity_audio

    unity_path = export_unity_audio(wav, OUT_DIR, stem)
    export_listen_m4a(wav, m4a)
    if ogg_legacy.exists() and ogg_legacy != unity_path:
        ogg_legacy.unlink(missing_ok=True)
    return unity_path


def compose_full(
    *,
    vo_dry: Path,
    title_start_s: float,
    timeline_cues: dict,
    total_s: float,
) -> tuple[Path, float, dict]:
    sys.path.insert(0, str(SCRIPT_DIR))
    from intro_music_gen import gen_intro_music
    from intro_sfx_gen import generate_all
    from intro_vo_edge import polish_narrator

    WORK.mkdir(parents=True, exist_ok=True)
    vo_spatial = WORK / "vo_spatial_full.wav"
    polish_narrator(vo_dry, vo_spatial)
    vo_dur = probe_duration(vo_spatial)
    total_s = max(total_s, vo_dur + 0.5)

    chime_s = max(title_start_s, float(timeline_cues.get("chimeS", title_start_s)))
    music_wav = WORK / "intro_music_full.wav"
    gen_intro_music(music_wav, total_s, title_at_s=chime_s)

    foot_cues = [(float(f["t"]), float(f["pan"])) for f in timeline_cues.get("footsteps", [])]
    stems = generate_all(WORK / "sfx_full", duration_s=total_s, footstep_cues=foot_cues)

    mix_wav = WORK / "intro_vo_full_mix.wav"
    inputs = [
        vo_spatial,
        stems["ambient_bed"],
        stems["drip"],
        stems["footsteps"],
        stems["rustle"],
        stems["power_on"],
        stems["checkpoint"],
        stems["ui_chime"],
        music_wav,
    ]
    cmd = [ffmpeg(), "-y"]
    for path in inputs:
        cmd += ["-i", str(path)]
    fc = build_mix_filter(
        vo_label="0",
        ambient_label="1",
        drip_label="2",
        footsteps_label="3",
        rustle_label="4",
        power_label="5",
        checkpoint_label="6",
        chime_label="7",
        music_label="8",
        rustle_s=float(timeline_cues["rustleS"]),
        power_s=float(timeline_cues["powerOnS"]),
        cp_s=float(timeline_cues["checkpointS"]),
        chime_s=chime_s,
        total_s=total_s,
    )
    cmd += [
        "-filter_complex",
        fc,
        "-map",
        "[aout]",
        "-ar",
        "44100",
        "-ac",
        "2",
        "-channel_layout",
        "stereo",
        str(mix_wav),
    ]
    subprocess.run(cmd, check=True)

    cues_out = {
        **timeline_cues,
        "titleStartS": round(title_start_s, 3),
        "chimeS": round(chime_s, 3),
        "voDurationS": round(vo_dur, 3),
        "totalDurationS": round(total_s, 3),
        "bakedMix": True,
        "voice": "en-US-ChristopherNeural",
    }
    return mix_wav, total_s, cues_out


def main() -> int:
    sys.path.insert(0, str(SCRIPT_DIR))
    from intro_sync_constants import TITLE_HOLD_S
    from intro_timeline_cues import full_intro_cues, load_timeline
    from intro_vo_timeline_sync import synthesize_narration_led_vo

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    STREAMING_DIR.mkdir(parents=True, exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)

    timeline = load_timeline()

    vo_dry = WORK / "vo_dry_full.wav"
    print("Composing FULL intro audio (narration-led — video follows VO)")
    print("  VO: ChristopherNeural — natural pace, scene cuts on cue boundaries")

    ok, title_start, vo_raw_dur, timeline_total, schedule, grid_s = synthesize_narration_led_vo(
        vo_dry, timeline
    )
    if not ok:
        print("error: narration-led VO synthesis failed", file=sys.stderr)
        return 1

    video_end = title_start
    for entry in schedule:
        if entry.get("id") == "beat_10":
            video_end = float(entry["wallEndS"])
            break

    content_end = float(timeline.get("contentDurationS", 118.57))
    print(f"  VO {vo_raw_dur:.1f}s | grid {grid_s:.1f}s | video ends {video_end:.1f}s | title @ {title_start:.1f}s")

    timeline_cues = full_intro_cues(
        timeline,
        grid_s=grid_s,
        title_at_s=title_start,
        total_duration_s=timeline_total,
        schedule=schedule,
        video_end_s=video_end,
        video_content_end_s=content_end,
    )
    mix_wav, total_s, cues_out = compose_full(
        vo_dry=vo_dry,
        title_start_s=title_start,
        timeline_cues=timeline_cues,
        total_s=timeline_total,
    )

    listen_m4a = LISTEN_DIR / "intro_vo_full.m4a"
    unity_path = export_listenables(
        mix_wav,
        stem="intro_vo",
        ogg_legacy=OUT_DIR / "intro_vo.ogg",
        m4a=listen_m4a,
    )

    cues_path = STREAMING_DIR / "intro_audio_cues.json"
    cues_path.write_text(json.dumps(cues_out, indent=2) + "\n", encoding="utf-8")

    print(f"done → {unity_path}")
    print(f"listen → {listen_m4a}")
    print(f"cues → {cues_path}")
    print(f"duration: {total_s:.1f}s")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
