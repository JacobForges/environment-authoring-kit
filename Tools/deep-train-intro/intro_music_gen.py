#!/usr/bin/env python3
"""Real intro tune — melodic Am folk-rock trailer (audible under VO)."""

from __future__ import annotations

import math
import shutil
import subprocess
import wave
from pathlib import Path

SR = 48000
BPM = 92
BEAT = 60.0 / BPM


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def midi_hz(note: int) -> float:
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def synth_note_pcm(freq: float, dur_s: float, *, gain: float = 0.55, pluck: bool = True) -> list[int]:
    """Synthesize note in Python — reliable pluck envelope."""
    import array

    n = int(dur_s * SR)
    out = array.array("h")
    attack = int(0.02 * SR)
    decay = int(max(0.08, dur_s * 0.45) * SR)
    for i in range(n):
        t = i / SR
        env = 1.0
        if i < attack:
            env = i / max(attack, 1)
        elif i > n - decay:
            env = max(0.0, (n - i) / max(decay, 1))
        elif pluck and i < attack + int(0.06 * SR):
            env = 1.0 - 0.35 * ((i - attack) / max(int(0.06 * SR), 1))
        # fundamental + octave for body
        s = math.sin(2 * math.pi * freq * t) * 0.78
        s += math.sin(2 * math.pi * freq * 2 * t) * (0.22 if pluck else 0.35)
        if not pluck:
            s += math.sin(2 * math.pi * freq * 0.5 * t) * 0.4
        sample = int(max(-32767, min(32767, s * env * gain * 32767)))
        out.append(sample)
    return out


def append_wav(path: Path, samples: list[int]) -> None:
    import array

    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(array.array("h", samples).tobytes())


def pad_silence(dur_s: float) -> list[int]:
    return [0] * int(dur_s * SR)


# Melody: Am folk epic (midi notes) — lead + bass + pad roots
LEAD = [
    (0.0, 69, 1.0),   # A4
    (1.0, 72, 0.5),   # C5
    (1.5, 76, 0.5),   # E5
    (2.0, 81, 1.2),   # A5
    (3.5, 76, 0.5),
    (4.0, 74, 0.5),   # D5
    (4.5, 72, 1.0),
    (6.0, 69, 1.0),
    (7.0, 71, 0.5),   # B4
    (7.5, 74, 0.5),
    (8.0, 76, 1.5),   # E5 hold
    (10.0, 74, 0.5),
    (10.5, 72, 0.5),
    (11.0, 69, 1.2),
    (13.0, 72, 0.5),
    (13.5, 76, 0.5),
    (14.0, 81, 2.0),  # title swell area
    (16.5, 76, 0.5),
    (17.0, 81, 1.5),
]

BASS = [
    (0.0, 45, 2.0),   # A2
    (2.0, 45, 2.0),
    (4.0, 50, 2.0),   # D3
    (6.0, 45, 2.0),
    (8.0, 52, 2.0),   # E3
    (10.0, 50, 2.0),
    (12.0, 45, 2.5),
    (14.5, 45, 3.0),
]

PAD = [
    (0.0, 57, 4.0),   # A3
    (4.0, 62, 4.0),   # D4
    (8.0, 64, 4.0),   # E4
    (12.0, 57, 6.0),
]


def expand_looped(notes: list[tuple[float, int, float]], duration_s: float) -> list[tuple[float, int, float]]:
    if not notes:
        return []
    pattern_end = max(t + d for t, _, d in notes) + 0.25
    out: list[tuple[float, int, float]] = []
    cycle = 0
    while cycle * pattern_end < duration_s + 0.01:
        for t, midi, dur in notes:
            st = cycle * pattern_end + t
            if st >= duration_s:
                break
            out.append((st, midi, min(dur, duration_s - st)))
        cycle += 1
    return out


def render_voice(notes: list[tuple[float, int, float]], *, gain: float, pluck: bool) -> list[int]:
    if not notes:
        return []
    end_s = max(t + d for t, _, d in notes) + 0.5
    buf = [0] * int(end_s * SR)
    for start, midi, dur in notes:
        pcm = synth_note_pcm(midi_hz(midi), dur, gain=gain, pluck=pluck)
        off = int(start * SR)
        for i, s in enumerate(pcm):
            j = off + i
            if j < len(buf):
                buf[j] += s
    peak = max(abs(s) for s in buf) or 1
    scale = min(32767 / peak, 1.0)
    return [int(s * scale) for s in buf]


def gen_pulse_layer(dur_s: float) -> list[int]:
    """Soft kick pulse — trailer heartbeat."""
    import array

    beats = int(dur_s / BEAT) + 2
    buf = [0] * int(dur_s * SR)
    for b in range(beats):
        off = int(b * BEAT * SR)
        for i in range(int(0.08 * SR)):
            t = i / SR
            env = math.exp(-t * 42)
            s = env * (math.sin(2 * math.pi * 90 * t) * 0.7 + math.sin(2 * math.pi * 55 * t) * 0.5)
            j = off + i
            if j < len(buf):
                buf[j] += int(s * 12000)
    return buf


def sustained_bed(duration_s: float, title_at_s: float) -> list[int]:
    """Pad + bass drone — runs full length, lifts into title."""
    root = synth_note_pcm(midi_hz(45), duration_s, gain=0.30, pluck=False)
    fifth = synth_note_pcm(midi_hz(52), duration_s, gain=0.20, pluck=False)
    pad = synth_note_pcm(midi_hz(57), duration_s, gain=0.22, pluck=False)
    buf = [0] * int(duration_s * SR)
    for layer in (root, fifth, pad):
        for i, s in enumerate(layer):
            if i < len(buf):
                buf[i] += s
    swell = int(title_at_s * SR)
    tail = int(min(duration_s, title_at_s + 6.0) * SR)
    for i in range(swell, len(buf)):
        if i < tail:
            prog = (i - swell) / max(tail - swell, 1)
            buf[i] = int(buf[i] * (1.0 + 0.9 * prog))
    peak = max(abs(s) for s in buf) or 1
    return [int(s * (28000 / peak)) for s in buf]


def title_finale(title_at_s: float, duration_s: float) -> list[int]:
    """Premiere finish — big Am stack when DEEP TRAIN ACADEMY hits."""
    notes = [
        (0.0, 45, 6.0),
        (0.0, 57, 5.8),
        (0.0, 69, 5.5),
        (0.05, 76, 5.0),
        (0.08, 81, 4.8),
        (0.12, 88, 4.5),
        (0.2, 93, 4.0),
        (1.2, 88, 3.0),
        (2.2, 81, 2.5),
        (3.0, 76, 2.0),
    ]
    shifted = [(title_at_s + t, m, min(d, duration_s - title_at_s - t + 0.5)) for t, m, d in notes]
    shifted = [(t, m, d) for t, m, d in shifted if d > 0.1]
    return render_voice(shifted, gain=0.68, pluck=False)


def gen_intro_music(out: Path, duration_s: float, *, title_at_s: float | None = None) -> None:
    work = out.parent / ".music_build"
    work.mkdir(parents=True, exist_ok=True)
    title_t = title_at_s if title_at_s is not None else duration_s * 0.78
    try:
        lead = render_voice(expand_looped(LEAD, duration_s), gain=0.40, pluck=True)
        bass = render_voice(expand_looped(BASS, duration_s), gain=0.50, pluck=False)
        pad = render_voice(expand_looped(PAD, duration_s), gain=0.26, pluck=False)
        pulse = gen_pulse_layer(duration_s)
        bed = sustained_bed(duration_s, title_t)
        finale = title_finale(title_t, duration_s)

        total_n = int(duration_s * SR)
        mix = [0] * total_n
        for layer in (bed, pad, bass, pulse, lead, finale):
            for i, s in enumerate(layer):
                if i < total_n:
                    mix[i] += s

        peak = max(abs(s) for s in mix) or 1
        mix = [int(s * (30000 / peak)) for s in mix]

        raw = work / "raw_mono.wav"
        append_wav(raw, mix)

        subprocess.run(
            [
                ffmpeg(),
                "-y",
                "-i",
                str(raw),
                "-af",
                ",".join(
                    [
                        "afade=t=in:st=0:d=1.8",
                        f"afade=t=out:st={max(duration_s - 1.2, 1):.2f}:d=1.2",
                        "extrastereo=m=1.35",
                        "aecho=0.9:0.86:50|90:0.18|0.10",
                        "equalizer=f=110:width_type=o:width=1.0:g=2.0",
                        "equalizer=f=280:width_type=o:width=1.2:g=1.5",
                        "acompressor=threshold=-20dB:ratio=1.4:attack=18:release=220:makeup=1.15",
                        "volume=2.8",
                        "alimiter=limit=0.97",
                    ]
                ),
                "-ar",
                str(SR),
                "-ac",
                "2",
                str(out),
            ],
            check=True,
            capture_output=True,
        )
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    import sys

    dur = float(sys.argv[2]) if len(sys.argv) > 2 else 40.0
    dest = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "work" / "intro_music.wav"
    dest.parent.mkdir(parents=True, exist_ok=True)
    gen_intro_music(dest, dur)
    print(dest)
