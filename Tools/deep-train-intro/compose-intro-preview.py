#!/usr/bin/env python3
"""Compose intro audio preview — natural-speed VO + music + spatial SFX."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT_DIR = Path(__file__).resolve().parent
OUT_DIR = ROOT / "Assets/Resources/DeepTrainAcademy/Intro"
WORK = SCRIPT_DIR / "work"
LISTEN_DIR = SCRIPT_DIR / "listen"

FOOTSTEP_FRAC = [0.04, 0.09, 0.14, 0.19, 0.24, 0.30]
FOOTSTEP_PAN = [0.35, 0.65, 0.40, 0.60, 0.45, 0.55]
RUSTLE_FRAC = 0.36
POWER_FRAC = 0.40
CHECKPOINT_FRAC = 0.70
CHIME_FRAC = 0.78


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
    vo_dur: float,
    total_s: float,
) -> str:
    rustle_s = RUSTLE_FRAC * vo_dur
    power_s = POWER_FRAC * vo_dur
    cp_s = CHECKPOINT_FRAC * vo_dur
    chime_s = CHIME_FRAC * vo_dur

    parts: list[str] = []

    # Cave underscore bus — audible bed + drips + feet
    parts.append(f"[{ambient_label}:a]volume=1.0[amb]")
    parts.append(f"[{drip_label}:a]volume=1.0[drip]")
    parts.append(f"[{footsteps_label}:a]volume=1.0[feet]")
    parts.append("[amb][drip][feet]amix=inputs=3:duration=longest:weights=1 0.95 0.75:normalize=0[cave]")

    # Music — continues full length; premiere swell at title
    t_pre = max(chime_s - 0.35, 0.0)
    t_post = min(chime_s + 6.0, total_s)
    parts.append(
        f"[{music_label}:a]volume='if(between(t,{t_pre:.3f},{t_post:.3f}),1.72,1.0)'[mus]"
    )

    # Narrator — on top
    parts.append(f"[{vo_label}:a]volume=1.32[voraw]")
    parts.append("[voraw]pan=stereo|c0=0.96*c0|c1=0.96*c1[vo]")

    # One-shot SFX — loud, positioned
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

    # Final: music+cave under voice — weights keep VO king
    parts.append(
        "[cave][mus]amix=inputs=2:duration=longest:weights=0.9 1.0:normalize=0[under];"
        "[under][vo][rst][pwr][cp][chl]amix=inputs=6:duration=longest:"
        "weights=0.44 1.72 0.85 1.0 0.9 0.95:normalize=0,"
        "stereotools=balance_in=0.04:balance_out=0.10:slev=1.38,"
        "extrastereo=m=1.42,"
        "aecho=0.88:0.84:28|48:0.10|0.06,"
        "acompressor=threshold=-15dB:ratio=1.25:attack=20:release=240:makeup=1.04,"
        f"apad=pad_dur={total_s:.3f},atrim=0:{total_s:.3f},"
        "alimiter=limit=0.98:attack=25:release=150[aout]"
    )
    return ";".join(parts)


def compose_preview(
    *,
    vo_dry: Path,
    sfx_dir: Path,
    music_wav: Path,
    out_wav: Path,
) -> float:
    sys.path.insert(0, str(SCRIPT_DIR))
    from intro_music_gen import gen_intro_music
    from intro_sfx_gen import generate_all
    from intro_vo_edge import polish_narrator

    WORK.mkdir(parents=True, exist_ok=True)
    out_wav.parent.mkdir(parents=True, exist_ok=True)

    vo_spatial = WORK / "vo_spatial.wav"
    polish_narrator(vo_dry, vo_spatial)
    vo_dur = probe_duration(vo_spatial)
    total_s = vo_dur + 2.0

    gen_intro_music(music_wav, total_s, title_at_s=CHIME_FRAC * vo_dur)

    foot_cues = [(f * vo_dur, pan) for f, pan in zip(FOOTSTEP_FRAC, FOOTSTEP_PAN)]
    stems = generate_all(sfx_dir, duration_s=total_s, footstep_cues=foot_cues)

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
        vo_dur=vo_dur,
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
        str(out_wav),
    ]
    subprocess.run(cmd, check=True)
    return total_s


def export_listenables(wav: Path, *, stem: str, ogg_legacy: Path, m4a: Path) -> Path:
    sys.path.insert(0, str(SCRIPT_DIR))
    from intro_unity_export import export_listen_m4a, export_unity_audio

    unity_path = export_unity_audio(wav, OUT_DIR, stem)
    export_listen_m4a(wav, m4a)
    if ogg_legacy.exists() and ogg_legacy != unity_path:
        ogg_legacy.unlink(missing_ok=True)
    return unity_path


def main() -> int:
    text_file = SCRIPT_DIR / "intro-vo-preview.txt"
    if not text_file.is_file():
        print(f"missing {text_file}", file=sys.stderr)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)
    vo_dry = WORK / "vo_dry.wav"
    mix_wav = WORK / "intro_vo_preview_mix.wav"
    music_wav = WORK / "intro_music.wav"
    listen_m4a = LISTEN_DIR / "intro_vo_preview.m4a"

    sys.path.insert(0, str(SCRIPT_DIR))
    from intro_vo_edge import synthesize_wav

    text = " ".join(text_file.read_text(encoding="utf-8").split())
    print("Composing intro audio preview (natural speed)")
    print("  VO: en-US-ChristopherNeural — deep Morgan register (original voice)")
    print("  Music: melodic Am folk-rock tune (audible under VO)")
    print("  SFX: cave bed, water drips, footsteps, android boot, checkpoint, chime")

    if not synthesize_wav(text, vo_dry, use_preview_cues=True):
        print("error: VO synthesis failed", file=sys.stderr)
        return 1

    total_s = compose_preview(vo_dry=vo_dry, sfx_dir=WORK / "sfx", music_wav=music_wav, out_wav=mix_wav)
    unity_path = export_listenables(
        mix_wav,
        stem="intro_vo_preview",
        ogg_legacy=OUT_DIR / "intro_vo_preview.ogg",
        m4a=listen_m4a,
    )

    print(f"done → {unity_path}")
    print(f"listen → {listen_m4a}")
    print(f"duration: {total_s:.1f}s (natural speed — not time-compressed)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
