#!/usr/bin/env python3
"""
Pitch-stabilizing autotune for macOS Personal Voice / say captures.

Default mode smooths the pitch contour (not hard semitone snap) + dry/wet mix
to kill word-tail wobble without robotic TTS artifacts.

Requires: pip install librosa soundfile numpy
Optional: pip install pyrubberband  (+ rubberband CLI)
"""
from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

import numpy as np

_HOP = 512
_FMIN = 70.0
_FMAX = 420.0


def autotune_available() -> bool:
    try:
        import librosa  # noqa: F401

        return True
    except ImportError:
        return False


def _load_stereo(path: Path, sr: int = 48000) -> tuple[np.ndarray, int]:
    import soundfile as sf

    y, file_sr = sf.read(str(path), always_2d=True, dtype="float32")
    if y.shape[1] == 1:
        y = np.repeat(y, 2, axis=1)
    if file_sr != sr:
        import librosa

        y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
    return y, sr


def _save_stereo(path: Path, y: np.ndarray, sr: int) -> None:
    import soundfile as sf

    y = np.clip(y, -0.995, 0.995)
    sf.write(str(path), y, sr, subtype="PCM_16")


def _interp_nan(x: np.ndarray) -> np.ndarray:
    out = np.array(x, dtype=np.float64, copy=True)
    mask = np.isfinite(out) & (out > 0)
    if not mask.any():
        return out
    idx = np.arange(len(out))
    out[~mask] = np.interp(idx[~mask], idx[mask], out[mask])
    return out


def _smooth_shift(shift: np.ndarray, k: int) -> np.ndarray:
    k = max(3, int(k) | 1)
    out = np.convolve(shift, np.ones(k) / k, mode="same")
    k2 = max(3, (k * 2 + 1) | 1)
    return np.convolve(out, np.ones(k2) / k2, mode="same")


def _semitone_shifts(
    y_mono: np.ndarray,
    sr: int,
    *,
    strength: float,
    max_shift: float,
    smooth_frames: int,
    mode: str,
) -> tuple[np.ndarray, int]:
    import librosa

    f0, voiced_flag, _ = librosa.pyin(
        y_mono,
        sr=sr,
        hop_length=_HOP,
        fmin=_FMIN,
        fmax=_FMAX,
    )
    f0 = _interp_nan(f0)
    midi = librosa.hz_to_midi(np.maximum(f0, 1.0))

    mode = (mode or "smooth").lower()
    if mode == "snap":
        target = np.round(midi)
        shift = (target - midi) * float(np.clip(strength, 0.0, 1.0))
    elif mode in ("contour", "fall"):
        # Natural pitch fall — long-window contour only, tiny corrections (not TTS-robot)
        k_long = max(21, smooth_frames * 3) | 1
        midi_long = _smooth_shift(midi, k_long)
        for _ in range(2):
            midi_long = _smooth_shift(midi_long, k_long)
        shift = (midi_long - midi) * float(np.clip(strength, 0.0, 1.0)) * 0.72
    else:
        # Pull toward heavily smoothed contour — avoids robotic semitone grid
        k_med = max(5, smooth_frames)
        midi_smooth = np.copy(midi)
        for _ in range(2):
            midi_smooth = _smooth_shift(midi_smooth, k_med)
        shift = (midi_smooth - midi) * float(np.clip(strength, 0.0, 1.0))

    if mode in ("contour", "fall"):
        max_shift = min(max_shift, 0.3)

    shift = np.clip(shift, -max_shift, max_shift)
    if voiced_flag is not None:
        shift = np.where(voiced_flag, shift, 0.0)

    # Limit how fast correction can change (reduces granular stutter)
    shift = _smooth_shift(shift, smooth_frames)
    max_delta = 0.035 if mode in ("contour", "fall") else max(0.06, max_shift * 0.2)
    for i in range(1, len(shift)):
        d = float(shift[i] - shift[i - 1])
        if abs(d) > max_delta:
            shift[i] = shift[i - 1] + np.sign(d) * max_delta

    return shift.astype(np.float32), _HOP


def _pitch_shift_window(seg: np.ndarray, sr: int, n_steps: float) -> np.ndarray:
    if abs(n_steps) < 0.06:
        return seg
    try:
        import pyrubberband as pyrb

        return pyrb.pitch_shift(seg, sr, n_steps).astype(np.float32)
    except Exception:
        import librosa

        return librosa.effects.pitch_shift(seg, sr=sr, n_steps=n_steps).astype(np.float32)


def _apply_variable_pitch(y_mono: np.ndarray, sr: int, shift_frames: np.ndarray, hop: int) -> np.ndarray:
    # Longer windows + denser overlap = fewer OLA clicks / word-tail chatter
    win = max(int(0.072 * sr), hop * 6)
    win = win + (win % 2)
    hop_s = max(hop // 2, win // 8)
    n = len(y_mono)
    out = np.zeros(n + win, dtype=np.float64)
    norm = np.zeros_like(out)
    window = np.hanning(win).astype(np.float64)

    frame_idx = 0
    pos = 0
    n_frames = len(shift_frames)
    while pos < n:
        chunk = y_mono[pos : pos + win]
        if len(chunk) < win // 3:
            break
        if len(chunk) < win:
            chunk = np.pad(chunk, (0, win - len(chunk)))
        fi = min(frame_idx, n_frames - 1)
        corrected = _pitch_shift_window(chunk.astype(np.float32), sr, float(shift_frames[fi]))
        corrected = corrected[:win] * window
        out[pos : pos + win] += corrected
        norm[pos : pos + win] += window
        pos += hop_s
        frame_idx += 1

    norm = np.maximum(norm, 1e-8)
    return (out[:n] / norm[:n]).astype(np.float32)


def autotune_mono(
    y_mono: np.ndarray,
    sr: int,
    *,
    strength: float = 0.56,
    max_shift: float = 0.95,
    smooth_frames: int = 15,
    dry_mix: float = 0.40,
    mode: str = "smooth",
) -> np.ndarray:
    shift, hop = _semitone_shifts(
        y_mono,
        sr,
        strength=strength,
        max_shift=max_shift,
        smooth_frames=smooth_frames,
        mode=mode,
    )
    wet = _apply_variable_pitch(y_mono, sr, shift, hop)
    dry_mix = float(np.clip(dry_mix, 0.0, 1.0))
    return (y_mono * dry_mix + wet * (1.0 - dry_mix)).astype(np.float32)


def autotune_stereo(
    y: np.ndarray,
    sr: int,
    *,
    strength: float = 0.56,
    max_shift: float = 0.95,
    smooth_frames: int = 15,
    dry_mix: float = 0.40,
    mode: str = "smooth",
) -> np.ndarray:
    mono = y.mean(axis=1).astype(np.float32)
    tuned = autotune_mono(
        mono,
        sr,
        strength=strength,
        max_shift=max_shift,
        smooth_frames=smooth_frames,
        dry_mix=dry_mix,
        mode=mode,
    )
    peak_in = float(np.max(np.abs(mono)) or 1e-6)
    peak_out = float(np.max(np.abs(tuned)) or 1e-6)
    tuned = tuned * (peak_in / peak_out)
    return np.stack([tuned, tuned], axis=1)


def autotune_wav_file(
    wav: Path,
    spec: dict[str, Any] | None = None,
    *,
    inplace: bool = True,
) -> bool:
    spec = spec or {}
    strength = float(spec.get("narratorAutotuneStrength", spec.get("autotuneStrength", 0.56)))
    if strength <= 0.01:
        return True
    if not wav.is_file() or wav.stat().st_size < 2048:
        return False
    if not autotune_available():
        print(
            "Autotune skipped — install: python3 -m pip install librosa soundfile numpy",
            file=sys.stderr,
        )
        return False

    max_shift = float(spec.get("narratorAutotuneMaxSemitones", 0.95))
    dry_mix = float(spec.get("narratorAutotuneDryMix", spec.get("autotuneDryMix", 0.40)))
    smooth_ms = float(spec.get("narratorAutotuneSmoothMs", 115))
    mode = str(spec.get("narratorAutotuneMode", spec.get("autotuneMode", "smooth")))
    sr = int(spec.get("narratorAutotuneSampleRate", 48000))
    smooth_frames = max(5, int((smooth_ms / 1000.0) * sr / _HOP))

    try:
        y, sr = _load_stereo(wav, sr=sr)
        if float(np.max(np.abs(y))) < 1e-5:
            return False
        y_out = autotune_stereo(
            y,
            sr,
            strength=strength,
            max_shift=max_shift,
            smooth_frames=smooth_frames,
            dry_mix=dry_mix,
            mode=mode,
        )
        tmp = wav.with_suffix(".autotune.wav")
        _save_stereo(tmp, y_out, sr)
        if inplace:
            tmp.replace(wav)
        else:
            shutil.copy2(tmp, wav)
            tmp.unlink(missing_ok=True)
        return True
    except Exception as exc:
        print(f"Autotune failed: {exc}", file=sys.stderr)
        return False


def autotune_then_limiter(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Light tail only — heavy compression causes robotic pumping."""
    spec = spec or {}
    if not autotune_wav_file(wav, spec):
        return
    ffmpeg = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
    tmp = wav.with_suffix(".atlim.wav")
    parts = [
        "highpass=f=70",
        "equalizer=f=2800:width_type=o:width=2:g=-0.8",
        "adeclick=w=25:o=2",
        "alimiter=limit=0.98:attack=40:release=200",
    ]
    af = ",".join(parts)
    try:
        subprocess.run(
            [ffmpeg, "-y", "-i", str(wav), "-af", af, "-ar", "48000", "-ac", "2", str(tmp)],
            check=True,
            capture_output=True,
        )
        tmp.replace(wav)
    except subprocess.CalledProcessError:
        af = "highpass=f=70,alimiter=limit=0.98:attack=40:release=200"
        subprocess.run(
            [ffmpeg, "-y", "-i", str(wav), "-af", af, "-ar", "48000", "-ac", "2", str(tmp)],
            check=True,
            capture_output=True,
        )
        tmp.replace(wav)


if __name__ == "__main__":
    import argparse

    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("wav")
    p.add_argument("--strength", type=float, default=0.56)
    p.add_argument("--dry", type=float, default=0.40)
    p.add_argument("--out")
    args = p.parse_args()
    src = Path(args.wav).expanduser()
    out = Path(args.out).expanduser() if args.out else src
    if out != src:
        shutil.copy2(src, out)
    ok = autotune_wav_file(
        out,
        {
            "narratorAutotuneStrength": args.strength,
            "narratorAutotuneDryMix": args.dry,
        },
        inplace=True,
    )
    if ok:
        autotune_then_limiter(out, {})
    raise SystemExit(0 if ok else 1)
