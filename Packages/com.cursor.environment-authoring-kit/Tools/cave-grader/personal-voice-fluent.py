#!/usr/bin/env python3
"""
Fluent speech polish for Personal Voice — NO pitch-shift autotune (that causes robotic OLA).

- Smooths volume envelope (word-tail wobble / micro-dips)
- Removes sub-20ms silence gaps (stuttery breaks)
- Gentle dynamics + de-harsh EQ + declick

Use mode=fluent in ApprovedCards (recommended over pitch autotune).
"""
from __future__ import annotations

import importlib.util
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

import numpy as np


def _ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def smooth_envelope(
    y_mono: np.ndarray,
    sr: int,
    *,
    win_ms: float = 14.0,
    strength: float = 0.55,
) -> np.ndarray:
    """Even out micro-dips between phonemes without changing pitch."""
    try:
        from scipy.ndimage import uniform_filter1d
    except ImportError:
        return y_mono

    y = y_mono.astype(np.float64)
    env = np.abs(y)
    k = max(3, int(sr * win_ms / 1000.0))
    smooth = uniform_filter1d(env, size=k, mode="nearest")
    target = uniform_filter1d(smooth, size=max(k * 4, 5), mode="nearest")
    ratio = target / (smooth + 1e-8)
    ratio = np.clip(ratio, 0.88, 1.14)
    blend = float(np.clip(strength, 0.0, 1.0))
    gain = 1.0 + (ratio - 1.0) * blend
    return (y * gain).astype(np.float32)


def fluent_wav_file(
    wav: Path,
    spec: dict[str, Any] | None = None,
    *,
    inplace: bool = True,
) -> bool:
    """Deprecated — use personal-voice-helpers.apply_human_smooth."""
    mod_path = Path(__file__).resolve().parent / "personal-voice-helpers.py"
    if mod_path.is_file():
        sp = importlib.util.spec_from_file_location("pvh", mod_path)
        mod = importlib.util.module_from_spec(sp)
        assert sp.loader
        sp.loader.exec_module(mod)
        return mod.apply_human_smooth(wav, spec or {})

    if not wav.is_file() or wav.stat().st_size < 2048:
        return False
    spec = spec or {}
    sr = int(spec.get("narratorFluentSampleRate", 48000))
    env_strength = float(spec.get("narratorFluentEnvelope", spec.get("fluentEnvelope", 0.55)))

    try:
        import soundfile as sf

        y, file_sr = sf.read(str(wav), always_2d=True, dtype="float32")
        if y.shape[1] == 1:
            y = np.repeat(y, 2, axis=1)
        if file_sr != sr:
            import librosa

            y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
        mono = y.mean(axis=1)
        mono = smooth_envelope(mono, sr, strength=env_strength)
        peak = float(np.max(np.abs(mono)) or 1e-6)
        mono = mono / peak * min(peak, 0.98)
        y = np.stack([mono, mono], axis=1)
        tmp = wav.with_suffix(".fluent.np.wav")
        sf.write(str(tmp), np.clip(y, -0.995, 0.995), sr, subtype="PCM_16")
        src = tmp
    except Exception as exc:
        print(f"Fluent envelope skip ({exc})", file=sys.stderr)
        src = wav

    gap_ms = float(spec.get("narratorFluentGapMs", 16))
    gap_sec = max(0.008, min(0.028, gap_ms / 1000.0))
    thr = str(spec.get("narratorFluentGapThreshold", "-38dB"))

    chains = [
        ",".join(
            [
                "highpass=f=55",
                f"silenceremove=stop_periods=-1:stop_duration={gap_sec:.4f}:"
                f"stop_threshold={thr}:detection=peak",
                "acompressor=threshold=-24dB:ratio=1.12:attack=65:release=450:makeup=1.05",
                "equalizer=f=160:width_type=o:width=1.3:g=0.5",
                "equalizer=f=3200:width_type=o:width=2:g=-1.4",
                "lowpass=f=12000",
                "alimiter=limit=0.98:attack=50:release=220",
            ]
        ),
        "highpass=f=55,acompressor=threshold=-24dB:ratio=1.08:attack=90:release=500:makeup=1.03,"
        "equalizer=f=3000:width_type=o:width=2:g=-1.0,alimiter=limit=0.98",
    ]
    out = wav.with_suffix(".fluent.wav")
    ffmpeg = _ffmpeg()
    ok_ffmpeg = False
    for af in chains:
        try:
            subprocess.run(
                [ffmpeg, "-y", "-i", str(src), "-af", af, "-ar", "48000", "-ac", "2", str(out)],
                check=True,
                capture_output=True,
                text=True,
            )
            ok_ffmpeg = True
            break
        except subprocess.CalledProcessError:
            continue
    if not ok_ffmpeg:
        print("Fluent ffmpeg: using envelope-only pass", file=sys.stderr)
        if src != wav and Path(src).is_file():
            Path(src).replace(wav)
        return wav.is_file() and wav.stat().st_size > 2048

    if src != wav:
        src.unlink(missing_ok=True)
    if inplace:
        out.replace(wav)
    else:
        shutil.copy2(out, wav)
        out.unlink(missing_ok=True)
    return wav.is_file() and wav.stat().st_size > 2048


def deliver_fluent(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Fluent polish; optional micro pitch assist only if explicitly enabled."""
    spec = spec or {}
    fluent_wav_file(wav, spec)
    pitch_assist = float(spec.get("narratorFluentPitchAssist", 0.0))
    if pitch_assist > 0.05:
        try:
            from pathlib import Path as P

            tools = P(__file__).resolve().parent
            import importlib.util

            at_path = tools / "personal-voice-autotune.py"
            if at_path.is_file():
                sp = importlib.util.spec_from_file_location("pv_at", at_path)
                mod = importlib.util.module_from_spec(sp)
                assert sp.loader
                sp.loader.exec_module(mod)
                mini = {
                    **spec,
                    "narratorAutotuneStrength": pitch_assist,
                    "narratorAutotuneDryMix": 0.72,
                    "narratorAutotuneMaxSemitones": 0.35,
                    "narratorAutotuneMode": "smooth",
                }
                if mod.autotune_available():
                    mod.autotune_wav_file(wav, mini)
        except Exception:
            pass
