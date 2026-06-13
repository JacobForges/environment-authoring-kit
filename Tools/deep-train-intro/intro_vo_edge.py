#!/usr/bin/env python3
"""Deep male narration — ChristopherNeural, Morgan register (sentence delivery)."""

from __future__ import annotations

import asyncio
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

# Locked — same voice as first approved pass. Do not swap voices.
VOICE = "en-US-ChristopherNeural"
RATE = "-4%"
PITCH = "-5Hz"


@dataclass(frozen=True)
class SentenceCue:
    text: str
    rate: str = RATE
    pitch: str = PITCH
    volume: str = "+0%"
    pause_after_s: float = 0.28


def ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def split_sentences(text: str) -> list[str]:
    parts = re.split(r"(?<=[.!?])\s+", text.strip())
    return [p.strip() for p in parts if p.strip()]


def full_sentences() -> list[SentenceCue]:
    """Locked v5 script — grid through title (ChristopherNeural, Morgan register)."""
    short = 0.16
    med = 0.20
    beat = 0.22
    return [
        SentenceCue(
            "Way back when, before anybody stamped a name on these hills, I was a fool with a headlamp "
            "and a habit of going where the maps quit.",
            pause_after_s=0.14,
        ),
        SentenceCue(
            "The karst don't care about your plans. She just opens her mouth… "
            "and you either listen… or you get swallowed learning.",
            pause_after_s=beat,
        ),
        SentenceCue(
            "First miles down, it was only me and the drip of water counting time on the stone. "
            "Breath too loud. Boots slipping.",
            rate="-5%",
            pitch="-6Hz",
            pause_after_s=short,
        ),
        SentenceCue(
            "I reckon I wasn't hunting treasure — I was hunting quiet. The kind you can't find topside.",
            pause_after_s=med,
        ),
        SentenceCue(
            "Then my light caught something that didn't belong to the cave.",
            rate="-3%",
            pitch="-4Hz",
            volume="+3%",
            pause_after_s=short,
        ),
        SentenceCue(
            "Slumped there like a sack of ore — except ore don't have a face. Pale plating. Seams along the neck. "
            "Power gone out of it like a camp fire left in the rain.",
            pause_after_s=short,
        ),
        SentenceCue(
            "I stood there shaking, old man, and I'll tell you true… I almost walked on. Almost.",
            rate="-6%",
            pitch="-6Hz",
            pause_after_s=med,
        ),
        SentenceCue(
            "But the mountain has a way of planting what you're meant to find.",
            pause_after_s=short,
        ),
        SentenceCue(
            "I touched the chest plate and that little cyan lamp inside woke up — slow, like a coal catching. "
            "The eyes opened on me. Empty as a new shaft. Blank as boot firmware.",
            rate="-4%",
            pitch="-4Hz",
            volume="+4%",
            pause_after_s=short,
        ),
        SentenceCue(
            "The android didn't know up from down… but it knew I was standing there… and that was enough to begin.",
            pause_after_s=med,
        ),
        SentenceCue(
            "Now I had two lamps in the dark — mine… and that faint glow riding its ribs.",
            pause_after_s=short,
        ),
        SentenceCue(
            "So I showed it how a body crosses trouble. Stepped the bad stones first. "
            "Leaped the gap with my heart in my throat.",
            rate="-3%",
            pitch="-3Hz",
            pause_after_s=short,
        ),
        SentenceCue(
            "Not heroic — just stubborn. When you're leading, you move like the ground might forgive you.",
            pause_after_s=med,
        ),
        SentenceCue(
            "I looked back once. The chassis was watching like a student at a claim stake.",
            pause_after_s=short,
        ),
        SentenceCue(
            "Then it tried… and the wet rock won the argument. Limbs tangled. Metal kissed stone.",
            rate="-4%",
            pitch="-4Hz",
            pause_after_s=short,
        ),
        SentenceCue(
            "I grabbed what I could — cables, arm, pride — and hauled the android back from the edge. "
            "Reckon I'd never held a machine that heavy that awake before.",
            pause_after_s=med,
        ),
        SentenceCue(
            "Down here, falling ain't failure. It's just the part of the road you haven't finished walking.",
            pause_after_s=short,
        ),
        SentenceCue(
            "I squared its shoulders the way my daddy would've squared mine — if he'd stayed in the world long enough to teach. "
            "Breathe. Again. The cave don't lecture. She makes you repeat till your bones remember.",
            rate="-3%",
            pitch="-3Hz",
            pause_after_s=med,
        ),
        SentenceCue(
            "Every honest run has a line you cross when you're ready to be timed — a racing checkpoint, "
            "beam on the floor, mark on the wall, the world saying: prove it.",
            volume="+5%",
            pause_after_s=short,
        ),
        SentenceCue(
            "Not a doorway to heaven. A start stripe. From here, you run it for real.",
            pause_after_s=short,
        ),
        SentenceCue(
            "I pointed at that strip of light across the wet stone — checkpoint clean as a finish tape. "
            "I'd crossed a thousand in my youth.",
            rate="-3%",
            pitch="-3Hz",
            volume="+5%",
            pause_after_s=short,
        ),
        SentenceCue(
            "Never thought I'd be standing at one beside an android made of gears and good intentions. "
            "I broke the line first. And glory be… it followed.",
            pause_after_s=med,
        ),
        SentenceCue(
            "That's when I stopped waiting for the cave to take something else from me.",
            pause_after_s=short,
        ),
        SentenceCue(
            "It climbed. Ugly. Beautiful. All grit. I hung back in the shadow and felt this old chest loosen up — "
            "not triumph, mind you. Relief.",
            rate="-4%",
            pitch="-4Hz",
            pause_after_s=short,
        ),
        SentenceCue(
            "Like the world had one more decent thing in it than the newspapers said.",
            pause_after_s=med,
        ),
        SentenceCue(
            "After that it didn't just mirror my back… it learned my line.",
            pause_after_s=short,
        ),
        SentenceCue(
            "Deeper on, the cavern opened wide — teeth of stone, pools like buried sky. Same glow far ahead. "
            "I raised my hand. The chassis raised its hand.",
            rate="-3%",
            pitch="-3Hz",
            pause_after_s=short,
        ),
        SentenceCue(
            "Same bearing. Same hunger. Two lights… one bearing on the dark.",
            pause_after_s=med,
        ),
        SentenceCue(
            "The deep goes on longer than any map I ever drew. I know that now.",
            pause_after_s=short,
        ),
        SentenceCue(
            "We walked the last stretch side by side — boot and servo on ancient floor, "
            "lamp and heartbeat-light keeping time together.",
            rate="-4%",
            pitch="-4Hz",
            pause_after_s=short,
        ),
        SentenceCue(
            "Whatever waited in that bright mouth of the world, I quit fearing it like a stranger… "
            "and started fearing only the thought of climbing back up alone.",
            pause_after_s=med,
        ),
        SentenceCue(
            "That was my chapter. The tunnels are still down there. The stone's still teaching. "
            "And the android's still learning — for every soul brave enough to carry a lamp into the karst.",
            rate="-3%",
            pitch="-3Hz",
            volume="+4%",
            pause_after_s=0.32,
        ),
        SentenceCue(
            "DEEP TRAIN ACADEMY!",
            rate="+22%",
            pitch="+10Hz",
            volume="+58%",
            pause_after_s=0.14,
        ),
        SentenceCue(
            "Train deep — and make the mountain remember YOUR name!",
            rate="-3%",
            pitch="-2Hz",
            volume="+14%",
            pause_after_s=0.0,
        ),
    ]


def preview_sentences() -> list[SentenceCue]:
    """Full sentences — one consistent storyteller, title shouted separately."""
    return [
        SentenceCue(
            "Way back when, I was a fool with a headlamp, going where the maps quit.",
            pause_after_s=0.22,
        ),
        SentenceCue(
            "Miles down, just me and water on the stone.",
            rate="-6%",
            pitch="-6Hz",
            pause_after_s=0.26,
        ),
        SentenceCue(
            "Then my light hit pale plating on the rock — an android, powered down, dark as a dead claim.",
            rate="-4%",
            pitch="-4Hz",
            volume="+4%",
            pause_after_s=0.24,
        ),
        SentenceCue(
            "I almost walked on. Almost.",
            rate="-7%",
            pitch="-6Hz",
            pause_after_s=0.28,
        ),
        SentenceCue(
            "Every honest run has a checkpoint — beam on the floor, where talk ends and time starts.",
            rate="-3%",
            pitch="-3Hz",
            volume="+6%",
            pause_after_s=0.26,
        ),
        SentenceCue(
            "DEEP TRAIN ACADEMY!",
            rate="+22%",
            pitch="+10Hz",
            volume="+58%",
            pause_after_s=0.14,
        ),
        SentenceCue(
            "Train deep — and make the mountain remember your name!",
            rate="-3%",
            pitch="-2Hz",
            volume="+14%",
            pause_after_s=0.0,
        ),
    ]


async def _synth(cue: SentenceCue, mp3: Path) -> None:
    import edge_tts

    comm = edge_tts.Communicate(cue.text, VOICE, rate=cue.rate, pitch=cue.pitch, volume=cue.volume)
    await comm.save(str(mp3))


def ffprobe() -> str:
    return shutil.which("ffprobe") or "/opt/homebrew/bin/ffprobe"


def _probe_wav_duration(path: Path) -> float:
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


def _resolve_cues(*, use_preview_cues: bool, use_full_cues: bool, text: str) -> list[SentenceCue]:
    if use_preview_cues:
        return preview_sentences()
    if use_full_cues:
        return full_sentences()
    return [SentenceCue(s, pause_after_s=0.28) for s in split_sentences(text)]


def synthesize_wav(
    text: str,
    out_wav: Path,
    *,
    use_preview_cues: bool = True,
    use_full_cues: bool = False,
    target_title_at_s: float | None = None,
) -> bool:
    ok, _, _ = synthesize_wav_meta(
        text,
        out_wav,
        use_preview_cues=use_preview_cues,
        use_full_cues=use_full_cues,
        target_title_at_s=target_title_at_s,
    )
    return ok


def synthesize_wav_meta(
    text: str,
    out_wav: Path,
    *,
    use_preview_cues: bool = False,
    use_full_cues: bool = False,
    target_title_at_s: float | None = None,
) -> tuple[bool, float, float]:
    """Returns (ok, title_start_s, total_duration_s)."""
    try:
        import edge_tts  # noqa: F401
    except ImportError:
        return False, 0.0, 0.0

    cues = _resolve_cues(use_preview_cues=use_preview_cues, use_full_cues=use_full_cues, text=text)
    title_idx = len(cues) - 2 if use_full_cues or use_preview_cues else -1
    work = out_wav.parent / f".edge_intro_{out_wav.stem}"
    work.mkdir(parents=True, exist_ok=True)
    segments: list[Path] = []
    title_start_s = 0.0

    try:
        for i, cue in enumerate(cues):
            mp3 = work / f"seg_{i:03d}.mp3"
            asyncio.run(_synth(cue, mp3))
            if not mp3.is_file() or mp3.stat().st_size < 32:
                return False, 0.0, 0.0
            segments.append(mp3)

            if cue.pause_after_s > 0.01:
                pause_wav = work / f"pause_{i:03d}.wav"
                subprocess.run(
                    [
                        ffmpeg(),
                        "-y",
                        "-f",
                        "lavfi",
                        "-i",
                        f"anullsrc=r=48000:cl=mono:d={cue.pause_after_s:.3f}",
                        str(pause_wav),
                    ],
                    check=True,
                    capture_output=True,
                )
                segments.append(pause_wav)

            if i + 1 == title_idx:
                body_merged = work / "body_pre_title.wav"
                _concat_segments(work, segments, body_merged)
                title_start_s = _probe_wav_duration(body_merged)

        merged = work / "merged.wav"
        _concat_segments(work, segments, merged)

        if target_title_at_s is not None and title_idx > 0 and title_start_s > target_title_at_s + 0.08:
            merged = _fit_body_before_title(work, merged, title_start_s, target_title_at_s, title_idx)
            title_start_s = target_title_at_s

        subprocess.run(
            [ffmpeg(), "-y", "-i", str(merged), "-ar", "48000", "-ac", "1", str(out_wav)],
            check=True,
            capture_output=True,
        )
        total = _probe_wav_duration(out_wav)
        ok = out_wav.is_file() and out_wav.stat().st_size > 256
        return ok, title_start_s, total
    except Exception as exc:
        print(f"edge-tts synth failed: {exc}", file=sys.stderr)
        return False, 0.0, 0.0
    finally:
        shutil.rmtree(work, ignore_errors=True)


def _concat_segments(work: Path, segments: list[Path], out_wav: Path) -> None:
    list_file = work / f"concat_{out_wav.stem}.txt"
    lines: list[str] = []
    for seg in segments:
        if seg.suffix == ".mp3":
            wav_seg = work / f"{seg.stem}.wav"
            subprocess.run(
                [ffmpeg(), "-y", "-i", str(seg), "-ar", "48000", "-ac", "1", str(wav_seg)],
                check=True,
                capture_output=True,
            )
            lines.append(f"file '{wav_seg.resolve()}'")
        else:
            lines.append(f"file '{seg.resolve()}'")
    list_file.write_text("\n".join(lines) + "\n", encoding="utf-8")
    subprocess.run(
        [ffmpeg(), "-y", "-f", "concat", "-safe", "0", "-i", str(list_file), "-c", "copy", str(out_wav)],
        check=True,
        capture_output=True,
    )


def _fit_body_before_title(work: Path, merged: Path, title_start_s: float, target_s: float, title_idx: int) -> Path:
    ratio = title_start_s / max(target_s, 1.0)
    tempo_filters: list[str] = []
    remaining = ratio
    while remaining > 2.001:
        tempo_filters.append("atempo=2.0")
        remaining /= 2.0
    if remaining > 1.001:
        tempo_filters.append(f"atempo={remaining:.4f}")
    af = ",".join(tempo_filters) if tempo_filters else "anull"

    body = work / "body_fit.wav"
    tail = work / "tail_fit.wav"
    fitted = work / "merged_fitted.wav"
    subprocess.run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(merged),
            "-t",
            f"{title_start_s:.4f}",
            "-af",
            af,
            str(body),
        ],
        check=True,
        capture_output=True,
    )
    subprocess.run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(merged),
            "-ss",
            f"{title_start_s:.4f}",
            str(tail),
        ],
        check=True,
        capture_output=True,
    )
    concat_list = work / "fit_concat.txt"
    concat_list.write_text(
        f"file '{body.resolve()}'\nfile '{tail.resolve()}'\n",
        encoding="utf-8",
    )
    subprocess.run(
        [ffmpeg(), "-y", "-f", "concat", "-safe", "0", "-i", str(concat_list), "-c", "copy", str(fitted)],
        check=True,
        capture_output=True,
    )
    return fitted


def morgan_freeman_af() -> str:
    """Deep chest polish — keep low end under mix (slower comp, no bass pump)."""
    return ",".join(
        [
            "highpass=f=48",
            "lowpass=f=10500",
            "equalizer=f=85:width_type=o:width=1.0:g=3.0",
            "equalizer=f=110:width_type=o:width=1.0:g=3.6",
            "equalizer=f=180:width_type=o:width=1.1:g=2.5",
            "equalizer=f=420:width_type=o:width=1.3:g=1.2",
            "equalizer=f=2400:width_type=o:width=1.5:g=0.7",
            "equalizer=f=4000:width_type=o:width=1.6:g=-0.8",
            "equalizer=f=7000:width_type=o:width=2.0:g=-1.8",
            "acompressor=threshold=-20dB:ratio=1.22:attack=42:release=340:makeup=1.08",
            "aecho=0.82:0.88:12|18:0.06|0.04",
            "alimiter=limit=0.98:attack=70:release=260",
        ]
    )


def polish_narrator(in_wav: Path, out_wav: Path) -> None:
    subprocess.run(
        [
            ffmpeg(),
            "-y",
            "-i",
            str(in_wav),
            "-af",
            morgan_freeman_af(),
            "-ar",
            "48000",
            "-ac",
            "2",
            "-channel_layout",
            "stereo",
            str(out_wav),
        ],
        check=True,
    )
