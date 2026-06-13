#!/usr/bin/env python3
"""Audible cave foley + android boot — clearly hearable SFX."""

from __future__ import annotations

import math
import random
import shutil
import subprocess
import wave
from pathlib import Path

SR = 48000


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def run(cmd: list[str]) -> None:
    subprocess.run(cmd, check=True, capture_output=True, text=True)


def append_mono(path: Path, samples: list[int]) -> None:
    import array

    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(array.array("h", samples).tobytes())


def noise_burst(dur_s: float, gain: float, *, hp: float = 200, lp: float = 4000) -> list[int]:
    """Bandpassed noise hit."""
    tmp_in = Path("/tmp") / f".sfxn_{random.randint(0, 99999)}.wav"
    tmp_out = tmp_in.with_suffix(".out.wav")
    try:
        run(
            [
                ffmpeg(),
                "-y",
                "-f",
                "lavfi",
                "-i",
                f"anoisesrc=d={dur_s}:c=pink:a={gain}:sample_rate={SR}",
                "-af",
                f"highpass=f={hp},lowpass=f={lp},afade=t=in:st=0:d=0.004,afade=t=out:st=0:d={dur_s}",
                str(tmp_in),
            ]
        )
        with wave.open(str(tmp_in), "rb") as w:
            raw = w.readframes(w.getnframes())
        import array

        return array.array("h", raw).tolist()
    finally:
        tmp_in.unlink(missing_ok=True)
        tmp_out.unlink(missing_ok=True)


def tone_decay(freq: float, dur_s: float, gain: float) -> list[int]:
    n = int(dur_s * SR)
    out = []
    for i in range(n):
        t = i / SR
        env = math.exp(-t * (8.0 if freq > 800 else 4.0))
        s = math.sin(2 * math.pi * freq * t) * env * gain
        out.append(int(max(-32767, min(32767, s * 32767))))
    return out


def mix_into(buf: list[int], clip: list[int], offset_s: float, gain: float = 1.0) -> None:
    off = int(offset_s * SR)
    for i, s in enumerate(clip):
        j = off + i
        if j < len(buf):
            buf[j] += int(s * gain)


def stereoize_mono(path_mono: Path, path_stereo: Path, *, pan: float = 0.5, reverb: bool = True) -> None:
    left = pan
    right = 1.0 - pan
    af = f"pan=stereo|c0={left:.2f}*c0|c1={right:.2f}*c0,volume=1.6"
    if reverb:
        af += ",aecho=0.88:0.84:22|38:0.28|0.16"
    run([ffmpeg(), "-y", "-i", str(path_mono), "-af", af, "-ar", str(SR), "-ac", "2", str(path_stereo)])


def gen_ambient_bed(out: Path, duration_s: float) -> None:
    """Cave rumble + air — clearly audible."""
    d = duration_s
    run(
        [
            ffmpeg(),
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"anoisesrc=d={d}:c=brown:a=0.55:sample_rate={SR}",
            "-f",
            "lavfi",
            "-i",
            f"anoisesrc=d={d}:c=pink:a=0.22:sample_rate={SR}",
            "-filter_complex",
            (
                "[0:a]lowpass=f=280,highpass=f=35,volume=1.2[a0];"
                "[1:a]highpass=f=400,lowpass=f=5000,volume=0.8[a1];"
                "[a0][a1]amix=inputs=2:duration=first,"
                "aecho=0.9:0.85:80|140|220:0.35|0.22|0.12,"
                "extrastereo=m=1.4,"
                "volume=2.2,"
                "alimiter=limit=0.98[aout]"
            ),
            "-map",
            "[aout]",
            "-ar",
            str(SR),
            "-ac",
            "2",
            str(out),
        ]
    )


def gen_drip_layer(out: Path, duration_s: float) -> None:
    """Real water drops — random ping + splash hits."""
    rng = random.Random(42)
    buf = [0] * int(duration_s * SR)
    t = 0.35
    while t < duration_s - 0.2:
        gap = rng.uniform(0.45, 1.35)
        ping = tone_decay(rng.uniform(1800, 3200), 0.09, 0.35)
        splash = noise_burst(0.05, 0.45, hp=600, lp=9000)
        mix_into(buf, splash, t, 1.0)
        mix_into(buf, ping, t, 0.9)
        t += gap
    mono = out.parent / ".drip_mono.wav"
    append_mono(mono, buf)
    stereoize_mono(mono, out, pan=rng.uniform(0.3, 0.7))
    mono.unlink(missing_ok=True)


def gen_footstep(out: Path) -> None:
    run(
        [
            ffmpeg(),
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"anoisesrc=d=0.18:c=pink:a=0.85:sample_rate={SR}",
            "-af",
            "highpass=f=100,lowpass=f=2600,equalizer=f=180:g=6.0,"
            "afade=t=in:st=0:d=0.01,afade=t=out:st=0.07:d=0.09,"
            "aecho=0.85:0.8:20|35:0.45|0.25,volume=2.0",
            "-ar",
            str(SR),
            "-ac",
            "1",
            str(out),
        ]
    )


def gen_footstep_sequence(out: Path, cues: list[tuple[float, float]], *, pad_s: float = 30.0) -> None:
    single = out.parent / ".footstep_single.wav"
    gen_footstep(single)
    if not cues:
        shutil.copy2(single, out)
        single.unlink(missing_ok=True)
        return
    parts: list[str] = []
    labels: list[str] = []
    n = len(cues)
    parts.append(f"[0:a]asplit={n}" + "".join(f"[fsin{i}]" for i in range(n)))
    for i, (t, pan) in enumerate(cues):
        ms = int(round(t * 1000))
        left = pan
        right = 1.0 - pan
        parts.append(
            f"[fsin{i}]adelay={ms}|{ms},apad=pad_dur={pad_s},atrim=0:{pad_s},"
            f"volume=1.1,pan=stereo|c0={left:.2f}*c0|c1={right:.2f}*c1[fs{i}]"
        )
        labels.append(f"[fs{i}]")
    parts.append("".join(labels) + f"amix=inputs={len(labels)}:duration=longest:normalize=0[fsout]")
    run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(single),
            "-filter_complex",
            ";".join(parts),
            "-map",
            "[fsout]",
            "-ar",
            str(SR),
            "-ac",
            "2",
            str(out),
        ]
    )
    single.unlink(missing_ok=True)


def gen_rustle(out: Path) -> None:
    run(
        [
            ffmpeg(),
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"anoisesrc=d=0.65:c=pink:a=0.7:sample_rate={SR}",
            "-af",
            "highpass=f=180,lowpass=f=7000,equalizer=f=2400:g=3.0,"
            "afade=t=in:st=0:d=0.03,afade=t=out:st=0.45:d=0.18,"
            "aecho=0.88:0.84:10|18:0.3|0.18,volume=2.2",
            "-ar",
            str(SR),
            "-ac",
            "2",
            str(out),
        ]
    )


def gen_power_on(out: Path) -> None:
    """Android boot — relay click, servo rise, cyan lamp, system hum."""
    buf = [0] * int(2.8 * SR)
    # relay click
    mix_into(buf, noise_burst(0.04, 0.9, hp=800, lp=8000), 0.0, 1.2)
    mix_into(buf, tone_decay(1200, 0.06, 0.25), 0.02, 1.0)
    # servo / motor whir
    for i in range(12):
        f = 180 + i * 35
        mix_into(buf, tone_decay(f, 0.07, 0.18), 0.08 + i * 0.05, 1.0)
    mix_into(buf, noise_burst(0.5, 0.35, hp=300, lp=2400), 0.15, 1.5)
    # cyan power lamp boot
    mix_into(buf, tone_decay(880, 0.35, 0.4), 0.55, 1.2)
    mix_into(buf, tone_decay(1760, 0.25, 0.22), 0.58, 1.0)
    # stabilizing hum
    for i in range(30):
        mix_into(buf, tone_decay(220, 0.04, 0.12), 0.9 + i * 0.04, 0.8)
    mix_into(buf, noise_burst(0.25, 0.2, hp=1000, lp=6000), 0.7, 0.9)
    mono = out.parent / ".power_mono.wav"
    append_mono(mono, buf)
    run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(mono),
            "-af",
            "equalizer=f=520:g=3.5,equalizer=f=1200:g=2.0,aecho=0.9:0.86:18|32:0.35|0.2,"
            "pan=stereo|c0=0.2*c0|c1=0.9*c1,volume=2.8,alimiter=limit=0.98",
            "-ar",
            str(SR),
            "-ac",
            "2",
            str(out),
        ]
    )
    mono.unlink(missing_ok=True)


def gen_checkpoint(out: Path) -> None:
    run(
        [
            ffmpeg(),
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"sine=f=880:duration=0.7:sample_rate={SR}",
            "-f",
            "lavfi",
            "-i",
            f"sine=f=1760:duration=0.35:sample_rate={SR}",
            "-f",
            "lavfi",
            "-i",
            f"anoisesrc=d=0.5:c=white:a=0.25:sample_rate={SR}",
            "-filter_complex",
            (
                "[0:a]afade=t=in:st=0:d=0.02,afade=t=out:st=0.5:d=0.15,volume=0.9[a0];"
                "[1:a]afade=t=in:st=0.1:d=0.02,afade=t=out:st=0.3:d=0.08,volume=0.7[a1];"
                "[2:a]highpass=f=2000,afade=t=in:st=0:d=0.01,afade=t=out:st=0.2:d=0.15,volume=0.5[a2];"
                "[a0][a1][a2]amix=inputs=3,"
                "aecho=0.9:0.86:25|45:0.4|0.25,extrastereo=m=1.4,volume=2.5,alimiter=limit=0.98[aout]"
            ),
            "-map",
            "[aout]",
            "-ar",
            str(SR),
            "-ac",
            "2",
            str(out),
        ]
    )


def gen_ui_chime(out: Path) -> None:
    run(
        [
            ffmpeg(),
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"sine=f=660:duration=1.2:sample_rate={SR}",
            "-f",
            "lavfi",
            "-i",
            f"sine=f=990:duration=0.9:sample_rate={SR}",
            "-f",
            "lavfi",
            "-i",
            f"sine=f=1320:duration=0.7:sample_rate={SR}",
            "-filter_complex",
            (
                "[0:a]volume=0.8[a0];[1:a]volume=0.55[a1];[2:a]volume=0.35[a2];"
                "[a0][a1][a2]amix=inputs=3,"
                "aecho=0.9:0.88:40|80|120:0.45|0.28|0.14,"
                "afade=t=out:st=1.0:d=0.35,volume=2.8,alimiter=limit=0.98[aout]"
            ),
            "-map",
            "[aout]",
            "-ar",
            str(SR),
            "-ac",
            "2",
            str(out),
        ]
    )


def generate_all(
    out_dir: Path,
    *,
    duration_s: float = 30.0,
    footstep_cues: list[tuple[float, float]] | None = None,
) -> dict[str, Path]:
    out_dir.mkdir(parents=True, exist_ok=True)
    paths = {
        "ambient_bed": out_dir / "sfx_ambient_bed.wav",
        "drip": out_dir / "sfx_drip_layer.wav",
        "footstep": out_dir / "sfx_footstep.wav",
        "footsteps": out_dir / "sfx_footsteps_seq.wav",
        "rustle": out_dir / "sfx_rustle.wav",
        "power_on": out_dir / "sfx_power_on.wav",
        "checkpoint": out_dir / "sfx_checkpoint.wav",
        "ui_chime": out_dir / "sfx_ui_chime.wav",
    }
    gen_ambient_bed(paths["ambient_bed"], duration_s)
    gen_drip_layer(paths["drip"], duration_s)
    gen_footstep(paths["footstep"])
    gen_footstep_sequence(paths["footsteps"], footstep_cues or [], pad_s=duration_s)
    gen_rustle(paths["rustle"])
    gen_power_on(paths["power_on"])
    gen_checkpoint(paths["checkpoint"])
    gen_ui_chime(paths["ui_chime"])
    return paths
