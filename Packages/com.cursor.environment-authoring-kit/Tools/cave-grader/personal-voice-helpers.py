#!/usr/bin/env python3
"""
Personal Voice Helpers — named post-capture tools (humanSmooth, pitchAutotune, deEss, …).

Docs: VOICE_HELPERS.md, PERSONAL_VOICE_NARRATION.md
Configure in ApprovedCards.json:

  "voiceHelpers": {
    "preset": "human",
    "sayRate": 186.24,
    "chain": ["humanSmooth", "deEss", "warmth"]
  }

Or legacy keys still work (delivery: fluent, autotuneMode, …).
"""
from __future__ import annotations

import importlib.util
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any, Callable

import numpy as np

_TOOLS = Path(__file__).resolve().parent

# --- registry metadata (for --list-helpers) ---------------------------------

HELPER_CATALOG: dict[str, dict[str, str]] = {
    "humanSmooth": {
        "title": "Human Smooth",
        "summary": "Envelope leveling + micro-gap fill + gentle dynamics. No pitch-shift (sounds human).",
        "roboticRisk": "low",
    },
    "pitchAutotune": {
        "title": "Pitch Autotune",
        "summary": "PYIN pitch stabilization (smooth mode). Can sound robotic — use sparingly.",
        "roboticRisk": "medium-high",
    },
    "deEss": {
        "title": "De-Ess",
        "summary": "Tames harsh 4–7 kHz sibilance from Personal Voice / say.",
        "roboticRisk": "low",
    },
    "warmth": {
        "title": "Warmth EQ",
        "summary": "Light low-mid body + soft top roll-off.",
        "roboticRisk": "low",
    },
    "deClick": {
        "title": "De-Click",
        "summary": "Removes digital clicks between phonemes.",
        "roboticRisk": "low",
    },
    "raw": {
        "title": "Raw",
        "summary": "No processing — pure say+mysay capture.",
        "roboticRisk": "none",
    },
    "roomTone": {
        "title": "Room Tone (subtle)",
        "summary": "Very light ambience tail — documentary booth feel.",
        "roboticRisk": "low",
    },
    "breathSmooth": {
        "title": "Breath Smooth",
        "summary": "Softens plosives / breath spikes without removing pauses.",
        "roboticRisk": "low",
    },
    "character": {
        "title": "Character",
        "summary": "Presence, body, light dynamics — documentary warmth (not flat compression).",
        "roboticRisk": "low",
    },
    "antiStutter": {
        "title": "Anti-Stutter",
        "summary": "Long envelope glide only — no gap removal (fixes choppy tails).",
        "roboticRisk": "low",
    },
    "naturalPacing": {
        "title": "Natural Pacing",
        "summary": "Phrase-level micro tempo — slightly faster connectors, slower emphasis (algorithm phase).",
        "roboticRisk": "low",
    },
    "finalizer": {
        "title": "Finalizer",
        "summary": "Broadcast loudness + gentle limiter — ladder last phase.",
        "roboticRisk": "low",
    },
    "auditor": {
        "title": "Auditor",
        "summary": "QC pass — peak/RMS/tails/clipping; auto-fix before finalizer.",
        "roboticRisk": "none",
    },
    "autoEq20": {
        "title": "Auto EQ (multi-band)",
        "summary": "Analyzes spectrum vs speech target; auto-adjusts log-spaced bands (±dB capped).",
        "roboticRisk": "low",
    },
    "autoEq25": {
        "title": "Auto EQ (25-band)",
        "summary": "25-band speech contour — extra mid resolution, controlled treble.",
        "roboticRisk": "low",
    },
    "lowClean": {
        "title": "Low Clean",
        "summary": "Tames sub crackle / chest distortion on deep Personal Voice lows.",
        "roboticRisk": "low",
    },
    "midTreble": {
        "title": "Mid / Treble",
        "summary": "Documentary clarity — mids forward, gentle air (treble capped).",
        "roboticRisk": "low",
    },
    "voiceFlair": {
        "title": "Voice Flair",
        "summary": "Extra punch + excitement — engaged delivery without shout.",
        "roboticRisk": "low",
    },
    "spatial3d": {
        "title": "Spatial 3D",
        "summary": "Subtle stereo depth + room — more realistic, not gimmicky wide.",
        "roboticRisk": "low",
    },
    "spatialHarmony": {
        "title": "Spatial Harmony",
        "summary": "Hi-fi spatial field — bass mono, M/S width, elevation (Soundcore-style).",
        "roboticRisk": "low",
    },
    "ttsSmooth": {
        "title": "TTS Smooth",
        "summary": "De-robotize — soft clip, gentle chorus, slow glue (less synthetic edge).",
        "roboticRisk": "low",
    },
    "speakerSafe": {
        "title": "Speaker Safe",
        "summary": "Tame sub/bass — less crackle on spatial / bass-heavy speakers.",
        "roboticRisk": "low",
    },
    "steadyVoice": {
        "title": "Steady Voice",
        "summary": "Subtle even delivery — light level-ride + word clarity, not strong monotone.",
        "roboticRisk": "low",
    },
    "naturalPitch": {
        "title": "Natural Pitch",
        "summary": "Gentle pitch contour + phrase fall — human, not autotune-robot.",
        "roboticRisk": "low",
    },
    "phraseFall": {
        "title": "Phrase Fall",
        "summary": "Soft dip at phrase endings — natural downward energy.",
        "roboticRisk": "low",
    },
    "phraseDynamics": {
        "title": "Phrase Dynamics",
        "summary": "Gentle up/down phrase levels — breaks flat TTS monotone.",
        "roboticRisk": "low",
    },
    "casualTone": {
        "title": "Casual Tone",
        "summary": "Casual-scientific documentary — clear, approachable, not lecture-robot.",
        "roboticRisk": "low",
    },
    "vocalSpark": {
        "title": "Vocal Spark",
        "summary": "Slight excitement lift — engaged, not hyped.",
        "roboticRisk": "low",
    },
    "autoDynamics": {
        "title": "Auto Dynamics",
        "summary": "Gentle level rider — evens loud/quiet words automatically.",
        "roboticRisk": "low",
    },
}

HELPER_PRESETS: dict[str, list[str]] = {
    "raw": [],
    "human": ["antiStutter", "character", "deEss"],
    "humanplus": ["antiStutter", "character", "deEss"],
    "humanPlus": ["antiStutter", "character", "deEss"],
    "humancharacter": ["antiStutter", "character", "deEss"],
    "humanCharacter": ["antiStutter", "character", "deEss"],
    "documentary": ["antiStutter", "character", "deEss", "warmth"],
    "ladderdocumentary": [],  # uses voiceLadder phases, not flat chain
    "ladderDocumentary": [],
    "ladder": [],
    "pitchAutotune": ["humanSmooth", "pitchAutotune"],
    "fluent": ["humanSmooth", "deEss", "warmth"],
    "legacyFluent": ["humanSmooth"],
}


def _ffmpeg() -> str:
    return shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"


def _run_ffmpeg_af(wav: Path, af: str) -> bool:
    tmp = wav.with_suffix(".vh.wav")
    try:
        subprocess.run(
            [_ffmpeg(), "-y", "-i", str(wav), "-af", af, "-ar", "48000", "-ac", "2", str(tmp)],
            check=True,
            capture_output=True,
            text=True,
        )
        tmp.replace(wav)
        return True
    except subprocess.CalledProcessError:
        return False


def _load_mono_stereo(wav: Path, sr: int = 48000) -> tuple[np.ndarray, int]:
    import soundfile as sf

    y, file_sr = sf.read(str(wav), always_2d=True, dtype="float32")
    if y.shape[1] == 1:
        y = np.repeat(y, 2, axis=1)
    if file_sr != sr:
        import librosa

        y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
    return y, sr


def _save_stereo(wav: Path, y: np.ndarray, sr: int) -> None:
    import soundfile as sf

    sf.write(str(wav), np.clip(y, -0.995, 0.995), sr, subtype="PCM_16")


def _clean_for_speech_helpers(text: str) -> str:
    return re.sub(r"\s+", " ", str(text or "")).strip()


def narration_has_breath_cue(text: str) -> bool:
    return bool(
        re.search(r"\bBreath\b|\[\[breath\]\]", text, flags=re.IGNORECASE)
    )


def breath_cue_enabled(spec: dict[str, Any] | None) -> bool:
    """Words-only delivery — no synthetic inhale/exhale inserts."""
    if spec and spec.get("narratorWordsOnly") is True:
        return False
    cfg = _helper_cfg(spec or {}, "breathCue")
    return cfg.get("enabled", True) is not False


def _helper_cfg(spec: dict[str, Any], name: str) -> dict[str, Any]:
    vh = spec.get("voiceHelpers") or {}
    if isinstance(vh, dict):
        block = vh.get(name) or vh.get(name.replace("human", "humanSmooth"))
        if isinstance(block, dict):
            return block
    return {}


# --- humanSmooth (enhanced fluent) ------------------------------------------


def _smooth_envelope_human(
    y_mono: np.ndarray,
    sr: int,
    *,
    win_ms: float = 22.0,
    strength: float = 0.72,
) -> np.ndarray:
    try:
        from scipy.ndimage import uniform_filter1d
    except ImportError:
        return y_mono

    y = y_mono.astype(np.float64)
    env = np.abs(y)
    k = max(5, int(sr * win_ms / 1000.0))
    smooth = uniform_filter1d(env, size=k, mode="nearest")
    target = uniform_filter1d(smooth, size=max(k * 5, 7), mode="nearest")
    try:
        from scipy.signal import savgol_filter

        if len(target) > k * 6:
            wl = min(len(target) - 1, k * 5)
            wl = wl + 1 if wl % 2 == 0 else wl
            target = savgol_filter(target, wl, 3)
    except Exception:
        pass
    ratio = target / (smooth + 1e-8)
    ratio = np.clip(ratio, 0.96, 1.04)
    blend = float(np.clip(strength, 0.0, 1.0))
    gain = 1.0 + (ratio - 1.0) * blend
    return (y * gain).astype(np.float32)


def apply_anti_stutter(wav: Path, spec: dict[str, Any]) -> bool:
    """Envelope glide only — avoids silenceremove / heavy compression stutter."""
    cfg = _helper_cfg(spec, "antiStutter") or _helper_cfg(spec, "humanSmooth")
    if cfg.get("enabled") is False:
        return True
    sr = 48000
    light_join = bool(
        spec.get("narratorUsedSegmentQueue") and cfg.get("lightOnSegmentQueue", True)
    )
    if light_join:
        hp = int(cfg.get("highpassHz", 92))
        return _run_ffmpeg_af(
            wav,
            f"highpass=f={hp},alimiter=limit=0.97:attack=120:release=450",
        )
    env = float(cfg.get("envelope", 0.78))
    win_ms = float(cfg.get("winMs", 30))
    try:
        y, sr = _load_mono_stereo(wav, sr)
        mono = _smooth_envelope_human(y.mean(axis=1), sr, win_ms=win_ms, strength=env)
        peak = float(np.max(np.abs(mono)) or 1e-6)
        mono = mono / peak * min(peak, 0.98)
        _save_stereo(wav, np.stack([mono, mono], axis=1), sr)
    except Exception as exc:
        print(f"antiStutter: {exc}", file=sys.stderr)
    return _run_ffmpeg_af(wav, "highpass=f=88,alimiter=limit=0.98:attack=80:release=320")


def apply_character(wav: Path, spec: dict[str, Any]) -> bool:
    """Presence + mids — light chest (lowClean handles sub crackle)."""
    cfg = _helper_cfg(spec, "character")
    body = float(cfg.get("bodyDb", 0.55))
    presence = float(cfg.get("presenceDb", 1.85))
    air = float(cfg.get("airDb", -0.35))
    dynamic = float(cfg.get("dynamicDb", 0.25))
    mid = float(cfg.get("midDb", 0.85))
    chains = [
        ",".join(
            [
                f"highpass=f={int(cfg.get('highpassHz', 88))}",
                f"equalizer=f=165:width_type=o:width=1.4:g={body}",
                f"equalizer=f=420:width_type=o:width=1:g={mid * 0.45}",
                f"equalizer=f=1200:width_type=o:width=1.3:g={mid}",
                f"equalizer=f=2650:width_type=o:width=1.6:g={min(presence, 1.55)}",
                f"equalizer=f=3800:width_type=o:width=2:g={min(0.35 + dynamic * 0.55, 0.75)}",
                f"equalizer=f=5800:width_type=o:width=2:g={float(cfg.get('upperMidDb', -0.35))}",
                f"highshelf=f=10000:width_type=o:width=2:g={min(air, -0.35)}",
                "compand=attacks=0.10:decays=0.32:points=-80/-80|-55/-36|-35/-20|-16|-6|0/-1.5|8/0|20/6",
                "acompressor=threshold=-32dB:ratio=1.06:attack=160:release=680:makeup=1.05",
                "alimiter=limit=0.97:attack=45:release=220",
            ]
        ),
        "equalizer=f=170:width_type=o:width=1.2:g=0.7,"
        "equalizer=f=2700:width_type=o:width=1.5:g=1.0,"
        "acompressor=threshold=-28dB:ratio=1.06:attack=150:release=600:makeup=1.05",
    ]
    for af in chains:
        if _run_ffmpeg_af(wav, af):
            return True
    return False


def apply_human_smooth(wav: Path, spec: dict[str, Any]) -> bool:
    cfg = _helper_cfg(spec, "humanSmooth")
    sr = int(spec.get("narratorFluentSampleRate", 48000))
    env = float(
        cfg.get("envelope")
        or spec.get("narratorFluentEnvelope")
        or spec.get("fluentEnvelope", 0.72)
    )
    win_ms = float(cfg.get("winMs", 22))
    gap_ms = float(cfg.get("gapMs", 0))
    try:
        y, sr = _load_mono_stereo(wav, sr)
        mono = _smooth_envelope_human(y.mean(axis=1), sr, win_ms=win_ms, strength=env)
        peak = float(np.max(np.abs(mono)) or 1e-6)
        mono = mono / peak * min(peak, 0.97)
        tmp = wav.with_suffix(".hs.np.wav")
        _save_stereo(tmp, np.stack([mono, mono], axis=1), sr)
        src = tmp
    except Exception as exc:
        print(f"humanSmooth envelope: {exc}", file=sys.stderr)
        src = wav

    parts = ["highpass=f=50"]
    if gap_ms > 0:
        gap_sec = max(0.006, min(0.018, gap_ms / 1000.0))
        thr = str(cfg.get("gapThreshold", "-34dB"))
        parts.append(
            f"silenceremove=stop_periods=-1:stop_duration={gap_sec:.4f}:"
            f"stop_threshold={thr}:detection=peak"
        )
    parts.extend(
        [
            "acompressor=threshold=-28dB:ratio=1.04:attack=160:release=650:makeup=1.03",
            "alimiter=limit=0.97:attack=70:release=280",
        ]
    )
    af = ",".join(parts)
    if not _run_ffmpeg_af(Path(src), af):
        af2 = (
            "highpass=f=50,acompressor=threshold=-26dB:ratio=1.05:attack=100:release=600:makeup=1.03,"
            "equalizer=f=3000:width_type=o:width=2:g=-1.2,alimiter=limit=0.97"
        )
        if not _run_ffmpeg_af(Path(src), af2) and src != wav:
            Path(src).replace(wav)
    if src != wav and Path(src).exists():
        Path(src).unlink(missing_ok=True)
    return wav.is_file()


def apply_de_ess(wav: Path, spec: dict[str, Any]) -> bool:
    """Tame sibilance / hiss (Personal Voice + presence EQ often peaks 5–9 kHz)."""
    cfg = _helper_cfg(spec, "deEss")
    cut = float(cfg.get("cutHz", 5200))
    g = float(cfg.get("gainDb", -2.8))
    g2 = float(cfg.get("gainDbHigh", -3.2))
    cut2 = float(cfg.get("cutHz2", 7200))
    shelf = float(cfg.get("highShelfDb", -2.0))
    af = ",".join(
        [
            "highpass=f=80",
            f"equalizer=f={cut}:width_type=o:width=2:g={g}",
            f"equalizer=f={cut2}:width_type=o:width=2.5:g={g2}",
            f"highshelf=f=9500:width_type=o:width=2:g={shelf}",
        ]
    )
    return _run_ffmpeg_af(wav, af)


def apply_warmth(wav: Path, spec: dict[str, Any]) -> bool:
    af = (
        "equalizer=f=120:width_type=o:width=1.2:g=0.8,"
        "equalizer=f=240:width_type=o:width=1:g=0.4,"
        "highshelf=f=9000:width_type=o:width=2:g=-0.8"
    )
    return _run_ffmpeg_af(wav, af)


def apply_de_click(wav: Path, spec: dict[str, Any]) -> bool:
    cfg = _helper_cfg(spec, "deClick")
    hp = int(cfg.get("highpassHz", 85))
    w = int(cfg.get("window", 52))
    o = int(cfg.get("overlap", 3))
    af = f"highpass=f={hp},adeclick=w={w}:o={o}"
    if _run_ffmpeg_af(wav, af):
        return True
    return _run_ffmpeg_af(wav, f"highpass=f={hp}")


def apply_low_clean(wav: Path, spec: dict[str, Any]) -> bool:
    """Remove chest crackle / sub thump on deep voice — before heavy EQ."""
    cfg = _helper_cfg(spec, "lowClean")
    if cfg.get("enabled") is False:
        return True
    hp = int(cfg.get("highpassHz", 94))
    sub = float(cfg.get("subCutDb", -2.0))
    chest = float(cfg.get("chestCutDb", -1.1))
    mud = float(cfg.get("mudCutDb", -0.65))
    af = ",".join(
        [
            f"highpass=f={hp}",
            f"equalizer=f=125:width_type=o:width=1.1:g={sub}",
            f"equalizer=f=210:width_type=o:width=1:g={chest}",
            f"equalizer=f=340:width_type=o:width=1.2:g={mud}",
            "adeclip=window=25:overlap=2:threshold=-8dB",
            f"adeclick=w={int(cfg.get('declickWindow', 58))}:o=3",
            "acompressor=threshold=-26dB:ratio=1.06:attack=28:release=200:makeup=1.0",
            "alimiter=limit=0.96:attack=45:release=180",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print("  lowClean: sub/chest tame + de-click (deep voice)", flush=True)
    return ok


def apply_speaker_safe(wav: Path, spec: dict[str, Any]) -> bool:
    """Extra low-end tame for spatial speakers — less sub crackle / port rattle."""
    cfg = _helper_cfg(spec, "speakerSafe")
    if cfg.get("enabled") is False:
        return True
    hp = int(cfg.get("highpassHz", 102))
    sub = float(cfg.get("subCutDb", -2.8))
    bass = float(cfg.get("bassCutDb", -1.4))
    af = ",".join(
        [
            f"highpass=f={hp}",
            f"equalizer=f=110:width_type=o:width=1:g={sub}",
            f"equalizer=f=185:width_type=o:width=1.2:g={bass}",
            f"equalizer=f=280:width_type=o:width=1:g={float(cfg.get('mudCutDb', -0.5))}",
            "bass=g=-2.5:f=140:width_type=o:width=1.2",
            "acompressor=threshold=-24dB:ratio=1.12:attack=12:release=180:makeup=1.0",
            "alimiter=limit=0.95:attack=6:release=120",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print("  speakerSafe: lows rolled off for hi-fi / spatial playback", flush=True)
    return ok


def apply_tts_smooth(wav: Path, spec: dict[str, Any]) -> bool:
    """Soften synthetic TTS edges — glue, warmth, micro-width (not robotic shine)."""
    cfg = _helper_cfg(spec, "ttsSmooth")
    if cfg.get("enabled") is False:
        return True
    if cfg.get("skipOnWordQueue", True) and (
        spec.get("narratorCaptureMode") == "word"
        or spec.get("narratorUsedSegmentQueue")
    ):
        return True
    chorus = str(cfg.get("chorus", "0.42:0.82:48|52:0.12:0.28:2.2"))
    af = ",".join(
        [
            "afftdn=nf=-32",
            "asoftclip=type=tanh:param=0.72",
            f"chorus={chorus}",
            "equalizer=f=380:width_type=o:width=1.2:g=-0.55",
            "equalizer=f=2100:width_type=o:width=1.4:g=0.45",
            "acompressor=threshold=-30dB:ratio=1.04:attack=220:release=950:makeup=1.02",
            "alimiter=limit=0.97:attack=55:release=280",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print("  ttsSmooth: de-robotized (soft + chorus glue)", flush=True)
    return ok


def _bass_mono_crossover(
    left: np.ndarray, right: np.ndarray, sr: int, cross_hz: float
) -> tuple[np.ndarray, np.ndarray]:
    """Keep sub bass centered — spatial width on mids/highs only."""
    try:
        from scipy.signal import butter, sosfilt

        mid = ((left + right) * 0.5).astype(np.float64)
        sos_lp = butter(3, cross_hz, btype="low", fs=sr, output="sos")
        sos_hp = butter(3, cross_hz, btype="high", fs=sr, output="sos")
        low = sosfilt(sos_lp, mid).astype(np.float32)
        high_l = sosfilt(sos_hp, left.astype(np.float64)).astype(np.float32)
        high_r = sosfilt(sos_hp, right.astype(np.float64)).astype(np.float32)
        return low + high_l, low + high_r
    except Exception:
        return left, right


def apply_spatial_harmony_core(wav: Path, cfg: dict[str, Any]) -> bool:
    """Soundcore-style spatial: bass mono, M/S harmony, Haas elevation."""
    sr = int(cfg.get("sampleRate", 48000))
    side_boost = float(cfg.get("sideBoost", 0.18))
    delay_l_ms = float(cfg.get("delayLeftMs", 0.35))
    delay_r_ms = float(cfg.get("delayRightMs", 0.85))
    room = float(cfg.get("roomMix", 0.04))
    cross_hz = float(cfg.get("bassMonoCrossHz", 145))
    elevate_db = float(cfg.get("elevationDb", 0.35))
    try:
        y, sr = _load_mono_stereo(wav, sr)
        if y.ndim == 1:
            left = right = y.astype(np.float32)
        else:
            left = y[:, 0].astype(np.float32)
            right = y[:, 1].astype(np.float32)

        left, right = _bass_mono_crossover(left, right, sr, cross_hz)

        mid = (left + right) * 0.5
        side = (left - right) * 0.5
        side = side * (1.0 + side_boost)

        n_l = max(1, int(round(sr * delay_l_ms / 1000.0)))
        n_r = max(1, int(round(sr * delay_r_ms / 1000.0)))
        wide_l = np.roll(mid + side, n_l)
        wide_r = np.roll(mid - side, n_r)

        if elevate_db > 0.01:
            try:
                from scipy.signal import butter, sosfilt

                sos = butter(2, [2200, 5200], btype="band", fs=sr, output="sos")
                band_l = sosfilt(sos, wide_l.astype(np.float64))
                band_r = sosfilt(sos, wide_r.astype(np.float64))
                g = 10 ** (elevate_db / 20.0)
                wide_l = wide_l + band_l.astype(np.float32) * (g - 1.0) * 0.55
                wide_r = wide_r + band_r.astype(np.float32) * (g - 1.0) * 0.42
            except Exception:
                pass

        if room > 0.005:
            tail_len = max(32, int(sr * 0.014))
            k = np.exp(-np.linspace(0.0, 5.5, tail_len)).astype(np.float32)
            k /= float(np.sum(k) or 1.0)
            tail = np.convolve(mid, k, mode="same") * room
            wide_l = wide_l + tail
            wide_r = wide_r + tail * 0.88

        mix = float(cfg.get("dryMix", 0.82))
        wide_l = left * (1.0 - mix) + wide_l * mix
        wide_r = right * (1.0 - mix) + wide_r * mix

        peak = float(np.max(np.abs(np.stack([wide_l, wide_r]))) or 1e-6)
        scale = min(0.98 / peak, 1.0)
        stereo = np.stack([wide_l * scale, wide_r * scale], axis=1).astype(np.float32)
        _save_stereo(wav, stereo, sr)
        print(
            f"  spatialHarmony: bass mono <{cross_hz:.0f}Hz, "
            f"M/S +{side_boost:.0%}, delays {delay_l_ms:.2f}|{delay_r_ms:.2f}ms",
            flush=True,
        )
        return True
    except Exception as exc:
        print(f"spatialHarmony: {exc}", file=sys.stderr)
        return False


def apply_breath_smooth(wav: Path, spec: dict[str, Any]) -> bool:
    """Gentle plosive control — slow attack avoids slurred pumping."""
    cfg = _helper_cfg(spec, "breathSmooth")
    if cfg.get("enabled") is False:
        return True
    return _run_ffmpeg_af(
        wav,
        "acompressor=threshold=-22dB:ratio=1.25:attack=35:release=220:makeup=1.0,"
        "acompressor=threshold=-30dB:ratio=1.05:attack=180:release=700:makeup=1.02",
    )


def apply_room_tone(wav: Path, spec: dict[str, Any]) -> bool:
    cfg = _helper_cfg(spec, "roomTone")
    mix = float(cfg.get("mix", 0.04))
    if mix <= 0.001:
        return True
    af = f"aecho=0.02:0.03:12:0.02:18:0.01,volume={mix}"
    return _run_ffmpeg_af(wav, af)


def apply_pitch_autotune(wav: Path, spec: dict[str, Any]) -> bool:
    cfg = _helper_cfg(spec, "pitchAutotune")
    if cfg.get("enabled") is False:
        return True
    at_path = _TOOLS / "personal-voice-autotune.py"
    if not at_path.is_file():
        return False
    sp = importlib.util.spec_from_file_location("pv_at", at_path)
    mod = importlib.util.module_from_spec(sp)
    assert sp.loader
    sp.loader.exec_module(mod)
    mini = {
        **spec,
        "narratorAutotuneStrength": float(
            cfg.get("strength", spec.get("narratorAutotuneStrength", 0.35))
        ),
        "narratorAutotuneDryMix": float(cfg.get("dryMix", 0.68)),
        "narratorAutotuneMaxSemitones": float(cfg.get("maxSemitones", 0.5)),
        "narratorAutotuneMode": str(cfg.get("mode", "smooth")),
    }
    if not mod.autotune_available():
        return False
    ok = mod.autotune_wav_file(wav, mini)
    if ok:
        mod.autotune_then_limiter(wav, {**spec, "narratorAutotuneWarmth": False})
    return ok


def apply_raw(wav: Path, spec: dict[str, Any]) -> bool:
    return True


def _crossfade_join(chunks: list[np.ndarray], sr: int, fade_ms: float = 8.0) -> np.ndarray:
    if not chunks:
        return np.array([], dtype=np.float32)
    if len(chunks) == 1:
        return chunks[0].astype(np.float32)
    fade = max(32, int(sr * fade_ms / 1000.0))
    out = chunks[0].astype(np.float64)
    for nxt in chunks[1:]:
        nxt = nxt.astype(np.float64)
        if len(out) < fade or len(nxt) < fade:
            out = np.concatenate([out, nxt])
            continue
        ramp = np.linspace(0.0, 1.0, fade)
        out[-fade:] = out[-fade:] * (1.0 - ramp) + nxt[:fade] * ramp
        out = np.concatenate([out, nxt[fade:]])
    return out.astype(np.float32)


def _phrase_tempo_factor(chunk: np.ndarray, sr: int, cfg: dict[str, Any]) -> float:
    """Heuristic micro tempo per phrase — documentary naturalism."""
    dur = len(chunk) / max(sr, 1)
    rms = float(np.sqrt(np.mean(chunk.astype(np.float64) ** 2)) + 1e-12)
    crest = float(np.max(np.abs(chunk)) / (rms + 1e-8))
    fast = float(cfg.get("fastFactor", 1.035))
    slow = float(cfg.get("slowFactor", 0.965))
    emph = float(cfg.get("emphasisFactor", 0.955))
    max_delta = float(cfg.get("maxDelta", 0.06))

    if dur < 0.22:
        rate = fast
    elif dur > 2.2:
        rate = slow
    elif crest > 5.5:
        rate = emph
    elif crest < 2.8 and dur < 0.55:
        rate = fast
    else:
        rate = 1.0

    return float(np.clip(rate, 1.0 - max_delta, 1.0 + max_delta))


def apply_natural_pacing(wav: Path, spec: dict[str, Any]) -> bool:
    """
    Algorithm phase: phrase-boundary micro tempo (slow on emphasis, slight rush on connectors).
    """
    cfg = _helper_cfg(spec, "naturalPacing")
    if cfg.get("enabled") is False:
        return True
    sr = 48000
    try:
        import librosa

        y, file_sr = librosa.load(str(wav), sr=sr, mono=True)
        top_db = float(cfg.get("splitTopDb", 26))
        intervals = librosa.effects.split(y, top_db=top_db)
        if len(intervals) < 2:
            return True

        min_len = int(sr * float(cfg.get("minPhraseSec", 0.08)))
        stretched: list[np.ndarray] = []
        for start, end in intervals:
            if end - start < min_len:
                continue
            chunk = y[start:end]
            rate = _phrase_tempo_factor(chunk, sr, cfg)
            if abs(rate - 1.0) < 0.008:
                stretched.append(chunk)
            else:
                stretched.append(
                    librosa.effects.time_stretch(chunk, rate=rate)
                )

        if len(stretched) < 2:
            return True
        # Longer crossfade smears consonants → slur; keep joins tight.
        merged = _crossfade_join(stretched, sr, float(cfg.get("crossfadeMs", 4)))
        peak = float(np.max(np.abs(merged)) or 1e-6)
        merged = merged / peak * min(peak, 0.98)
        _save_stereo(wav, np.stack([merged, merged], axis=1), sr)
        return True
    except Exception as exc:
        print(f"naturalPacing: {exc} — skipping micro tempo", file=sys.stderr)
        return True


def run_voice_auditor(wav: Path, spec: dict[str, Any]) -> dict[str, Any]:
    """Measure levels and apply safe fixes (ladder auditor phase)."""
    cfg = _helper_cfg(spec, "auditor")
    if cfg.get("enabled") is False:
        return {"status": "skipped", "fixes_applied": False}

    report: dict[str, Any] = {
        "status": "ok",
        "fixes_applied": False,
        "issues": [],
        "metrics": {},
    }
    if not wav.is_file() or wav.stat().st_size < 256:
        report["status"] = "missing"
        return report

    try:
        y, sr = _load_mono_stereo(wav, 48000)
        mono = y.mean(axis=1).astype(np.float64)
    except Exception as exc:
        report["status"] = f"read_error: {exc}"
        return report

    peak = float(np.max(np.abs(mono)))
    rms = float(np.sqrt(np.mean(mono**2)))
    peak_db = 20 * np.log10(peak + 1e-12)
    rms_db = 20 * np.log10(rms + 1e-12)
    report["metrics"] = {"peak_db": round(peak_db, 2), "rms_db": round(rms_db, 2)}

    max_peak_db = float(cfg.get("maxPeakDb", -0.8))
    min_rms_db = float(cfg.get("minRmsDb", -28))
    fixes: list[str] = []

    if peak_db > max_peak_db:
        report["issues"].append("peak_hot")
        gain_db = max_peak_db - peak_db - 0.3
        af = f"volume={gain_db:.2f}dB"
        if _run_ffmpeg_af(wav, af):
            fixes.append("peak_reduce")
            report["fixes_applied"] = True

    if rms_db < min_rms_db and not report["fixes_applied"]:
        report["issues"].append("quiet")
        boost = min(6.0, min_rms_db - rms_db + 1.5)
        af = (
            f"volume={boost:.2f}dB,"
            "acompressor=threshold=-32dB:ratio=1.2:attack=40:release=400:makeup=1.0"
        )
        if _run_ffmpeg_af(wav, af):
            fixes.append("level_boost")
            report["fixes_applied"] = True

    trim_lead = float(cfg.get("trimLeadSec", 0.04))
    trim_tail = float(cfg.get("trimTailSec", 0.12))
    tf = _helper_cfg(spec, "tailFinish")
    if tf.get("enabled") is not False:
        trim_tail = float(tf.get("trimTailSec", 0.0))
        trim_lead = float(tf.get("trimLeadSec", 0.0))
    if trim_lead > 0 or trim_tail > 0:
        dur = len(mono) / sr
        if dur > trim_lead + trim_tail + 0.5:
            af = (
                f"silenceremove=start_periods=1:start_duration={trim_lead:.3f}:"
                f"start_threshold=-42dB:detection=peak,"
                f"areverse,silenceremove=start_periods=1:start_duration={trim_tail:.3f}:"
                f"start_threshold=-42dB:detection=peak,areverse"
            )
            if _run_ffmpeg_af(wav, af):
                fixes.append("trim_edges")
                report["fixes_applied"] = True

    report["fixes"] = fixes
    if report["issues"]:
        print(
            f"  auditor: {', '.join(report['issues'])}"
            + (f" → fixed {fixes}" if fixes else " (no fix)"),
            flush=True,
        )
    else:
        print(f"  auditor: ok (peak {peak_db:.1f} dB, rms {rms_db:.1f} dB)", flush=True)
    return report


def apply_auditor(wav: Path, spec: dict[str, Any]) -> bool:
    run_voice_auditor(wav, spec)
    return True


def _auto_eq20_band_centers(n: int, f_min: float, f_max: float) -> np.ndarray:
    return np.geomspace(float(f_min), float(f_max), int(n))


def _auto_eq20_speech_reference_db(freqs: np.ndarray) -> np.ndarray:
    """Speech target — controlled lows, forward mids, gentle treble (not harsh)."""
    ref = np.zeros(len(freqs), dtype=np.float64)
    for i, f in enumerate(freqs):
        if f < 160:
            ref[i] = -4.0
        elif f < 260:
            ref[i] = -2.2
        elif f < 420:
            ref[i] = -0.2
        elif f < 750:
            ref[i] = 1.0
        elif f < 1100:
            ref[i] = 1.45
        elif f < 1700:
            ref[i] = 1.55
        elif f < 2400:
            ref[i] = 1.25
        elif f < 3200:
            ref[i] = 0.85
        elif f < 4500:
            ref[i] = 0.35
        elif f < 6200:
            ref[i] = -0.15
        elif f < 8500:
            ref[i] = -1.2
        else:
            ref[i] = -2.8
    return ref - float(np.mean(ref))


def _auto_eq20_measure_levels(
    mag: np.ndarray, fft_freqs: np.ndarray, centers: np.ndarray
) -> np.ndarray:
    """Per-band log energy (dB relative)."""
    edges = np.sqrt(centers[:-1] * centers[1:])
    low_edge = centers[0] / np.sqrt(centers[1] / centers[0])
    high_edge = centers[-1] * np.sqrt(centers[-1] / centers[-2])
    bounds = np.concatenate([[low_edge], edges, [high_edge]])
    levels = np.zeros(len(centers), dtype=np.float64)
    for i in range(len(centers)):
        mask = (fft_freqs >= bounds[i]) & (fft_freqs < bounds[i + 1])
        if not np.any(mask):
            levels[i] = -80.0
            continue
        band_mag = mag[mask, :]
        rms = float(np.sqrt(np.mean(band_mag**2)) + 1e-12)
        levels[i] = 20.0 * np.log10(rms)
    levels -= float(np.mean(levels))
    return levels


def _auto_eq20_compute_gains(
    measured_db: np.ndarray,
    target_db: np.ndarray,
    *,
    strength: float,
    max_gain_db: float,
    smooth_passes: int,
) -> np.ndarray:
    raw = (target_db - measured_db) * float(np.clip(strength, 0.0, 1.0))
    gains = np.clip(raw, -max_gain_db, max_gain_db)
    try:
        from scipy.ndimage import uniform_filter1d

        k = max(3, 3 + 2 * int(smooth_passes))
        for _ in range(max(1, smooth_passes)):
            gains = uniform_filter1d(gains, size=k, mode="nearest")
    except ImportError:
        pass
    return np.clip(gains, -max_gain_db, max_gain_db)


def _auto_eq20_apply_stft(
    y_mono: np.ndarray, sr: int, centers: np.ndarray, gains_db: np.ndarray
) -> np.ndarray:
    import librosa

    n_fft = 2048
    hop = 512
    D = librosa.stft(y_mono, n_fft=n_fft, hop_length=hop)
    mag = np.abs(D)
    phase = np.angle(D)
    fft_freqs = librosa.fft_frequencies(sr=sr, n_fft=n_fft)
    gain_lin = np.interp(
        fft_freqs,
        centers.astype(np.float64),
        np.power(10.0, gains_db.astype(np.float64) / 20.0),
    )
    gain_lin = np.clip(gain_lin, 0.5, 2.0)
    out = librosa.istft(mag * gain_lin[:, np.newaxis] * np.exp(1j * phase), hop_length=hop)
    if len(out) < len(y_mono):
        out = np.pad(out, (0, len(y_mono) - len(out)))
    elif len(out) > len(y_mono):
        out = out[: len(y_mono)]
    return out.astype(np.float32)


def apply_natural_pitch(wav: Path, spec: dict[str, Any]) -> bool:
    """Light pitch contour smoothing + high dry mix — natural fall, not robotic."""
    cfg = _helper_cfg(spec, "naturalPitch")
    if cfg.get("enabled") is False:
        return True
    at_path = _TOOLS / "personal-voice-autotune.py"
    if not at_path.is_file():
        return True
    sp = importlib.util.spec_from_file_location("pv_at", at_path)
    mod = importlib.util.module_from_spec(sp)
    assert sp.loader
    sp.loader.exec_module(mod)
    if not mod.autotune_available():
        print("naturalPitch: install librosa — skipped", file=sys.stderr)
        return True
    mini = {
        **spec,
        "narratorAutotuneStrength": float(cfg.get("strength", 0.16)),
        "narratorAutotuneDryMix": float(cfg.get("dryMix", 0.88)),
        "narratorAutotuneMaxSemitones": float(cfg.get("maxSemitones", 0.28)),
        "narratorAutotuneSmoothMs": float(cfg.get("smoothMs", 180)),
        "narratorAutotuneMode": str(cfg.get("mode", "contour")),
    }
    ok = mod.autotune_wav_file(wav, mini)
    if ok:
        print(
            f"  naturalPitch: contour {mini['narratorAutotuneStrength']:.0%} "
            f"dry {mini['narratorAutotuneDryMix']:.0%}",
            flush=True,
        )
    return ok


def apply_phrase_fall(wav: Path, spec: dict[str, Any]) -> bool:
    """Gentle energy dip at phrase ends — skip only for per-word queue captures."""
    cfg = _helper_cfg(spec, "phraseFall")
    if cfg.get("enabled") is False:
        return True
    if spec.get("narratorCaptureWordQueue"):
        return True
    strength = float(cfg.get("strength", 0.35))
    sr = 48000
    try:
        import librosa

        y, sr = librosa.load(str(wav), sr=sr, mono=True)
        intervals = librosa.effects.split(y, top_db=float(cfg.get("splitTopDb", 28)))
        out = y.astype(np.float64)
        tail_ratio = float(cfg.get("tailRatio", 0.12))
        for start, end in intervals:
            if end - start < int(sr * 0.15):
                continue
            tail = max(1, int((end - start) * tail_ratio))
            t0 = end - tail
            ramp = np.linspace(1.0, 1.0 - 0.06 * strength, tail)
            out[t0:end] *= ramp
        peak = float(np.max(np.abs(out)) or 1e-6)
        out = (out / peak * min(peak, 0.98)).astype(np.float32)
        _save_stereo(wav, np.stack([out, out], axis=1), sr)
        return True
    except Exception as exc:
        print(f"phraseFall: {exc}", file=sys.stderr)
        return True


def apply_phrase_dynamics(wav: Path, spec: dict[str, Any]) -> bool:
    """Alternate gentle phrase gain — keeps energy moving without heavy compression."""
    cfg = _helper_cfg(spec, "phraseDynamics")
    if cfg.get("enabled") is False:
        return True
    if spec.get("narratorCaptureWordQueue"):
        return True
    strength = float(cfg.get("strength", 0.045))
    sr = 48000
    try:
        import librosa

        y, sr = librosa.load(str(wav), sr=sr, mono=True)
        intervals = librosa.effects.split(y, top_db=float(cfg.get("splitTopDb", 30)))
        out = y.astype(np.float64)
        n = 0
        for i, (start, end) in enumerate(intervals):
            if end - start < int(sr * float(cfg.get("minPhraseSec", 0.22))):
                continue
            n += 1
            swing = strength * (1.0 if i % 2 == 0 else -0.55)
            out[start:end] *= 1.0 + swing
        if n < 2:
            return True
        peak = float(np.max(np.abs(out)) or 1e-6)
        out = (out / peak * min(peak, 0.98)).astype(np.float32)
        _save_stereo(wav, np.stack([out, out], axis=1), sr)
        print(f"  phraseDynamics: {n} phrases, ±{strength * 100:.0f}% swing", flush=True)
        return True
    except Exception as exc:
        print(f"phraseDynamics: {exc}", file=sys.stderr)
        return True


def apply_casual_tone(wav: Path, spec: dict[str, Any]) -> bool:
    """Casual-scientific clarity — warm, clear mids, not stiff professor."""
    cfg = _helper_cfg(spec, "casualTone")
    if cfg.get("enabled") is False:
        return True
    clarity = float(cfg.get("clarityDb", 0.55))
    warmth = float(cfg.get("warmthDb", 0.45))
    af = ",".join(
        [
            f"equalizer=f=240:width_type=o:width=1.2:g={warmth}",
            f"equalizer=f=1650:width_type=o:width=1.4:g={clarity}",
            f"equalizer=f=4200:width_type=o:width=2:g={clarity * 0.35}",
            "highpass=f=68",
            "acompressor=threshold=-30dB:ratio=1.03:attack=160:release=780:makeup=1.02",
        ]
    )
    return _run_ffmpeg_af(wav, af)


def apply_vocal_spark(wav: Path, spec: dict[str, Any]) -> bool:
    """Excitement lift — forward mids, light treble sparkle (capped)."""
    cfg = _helper_cfg(spec, "vocalSpark")
    if cfg.get("enabled") is False:
        return True
    lift = float(cfg.get("liftDb", 0.45))
    treble = min(float(cfg.get("trebleDb", 0.42)), 0.65)
    af = ",".join(
        [
            f"equalizer=f=1450:width_type=o:width=1.4:g={lift * 0.55}",
            f"equalizer=f=2400:width_type=o:width=1.5:g={lift}",
            f"equalizer=f=5200:width_type=o:width=2:g={treble}",
            f"highshelf=f=9200:width_type=o:width=2:g={treble * 0.35}",
            "acompressor=threshold=-22dB:ratio=1.1:attack=28:release=200:makeup=1.03",
            "alimiter=limit=0.97:attack=35:release=180",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print(f"  vocalSpark: light engagement (+{lift:.1f} dB presence)", flush=True)
    return ok


def apply_auto_dynamics(wav: Path, spec: dict[str, Any]) -> bool:
    """Auto level-rider — lifts quiet syllables so delivery breathes, not one flat level."""
    cfg = _helper_cfg(spec, "autoDynamics")
    if cfg.get("enabled") is False:
        return True
    expand = float(cfg.get("expandDb", 1.4))
    af = ",".join(
        [
            "compand=attacks=0.22:decays=0.85:points="
            f"-80/-80|-52/-34|-28|-18|-10|-5|0/-{min(expand, 1.0):.1f}|8/0|20/3",
            "acompressor=threshold=-30dB:ratio=1.03:attack=140:release=750:makeup=1.02",
        ]
    )
    return _run_ffmpeg_af(wav, af)


def apply_steady_voice(wav: Path, spec: dict[str, Any]) -> bool:
    """
    Slight steady / word-even delivery — levels out phrase-to-phrase swings without robotic snap.
    """
    cfg = _helper_cfg(spec, "steadyVoice")
    if cfg.get("enabled") is False:
        return True
    if spec.get("narratorCaptureMode") == "word" and cfg.get("skipOnWordQueue", True):
        return True

    strength = float(cfg.get("strength", 0.42))
    env_blend = float(cfg.get("envelopeBlend", 0.32))
    sr = 48000

    try:
        y, sr = _load_mono_stereo(wav, sr)
        mono = y.mean(axis=1).astype(np.float64)
        if env_blend > 0.01:
            smoothed = _smooth_envelope_human(
                mono.astype(np.float32),
                sr,
                win_ms=float(cfg.get("winMs", 18)),
                strength=min(0.55, env_blend * strength * 1.2),
            ).astype(np.float64)
            mono = mono * (1.0 - env_blend) + smoothed * env_blend
            peak = float(np.max(np.abs(mono)) or 1e-6)
            mono = (mono / peak * min(peak, 0.97)).astype(np.float32)
            _save_stereo(wav, np.stack([mono, mono], axis=1), sr)
    except Exception as exc:
        print(f"steadyVoice envelope: {exc}", file=sys.stderr)

    ratio = 1.02 + 0.06 * strength
    makeup = 1.01 + 0.03 * strength
    mid_g = 0.35 * strength
    low_g = 0.2 * strength
    af = ",".join(
        [
            "highpass=f=70",
            f"equalizer=f=320:width_type=o:width=1:g={low_g:.2f}",
            f"equalizer=f=1950:width_type=o:width=1.3:g={mid_g:.2f}",
            f"acompressor=threshold=-27dB:ratio={ratio:.3f}:attack=130:release=820:makeup={makeup:.3f}",
            "acompressor=threshold=-32dB:ratio=1.02:attack=200:release=950:makeup=1.0",
            "alimiter=limit=0.97:attack=60:release=240",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print(f"  steadyVoice: strength {strength:.0%} (subtle even words)", flush=True)
    return ok


def apply_auto_eq_20(wav: Path, spec: dict[str, Any]) -> bool:
    """
    Auto-adjusting 20-band EQ: measure → compare to speech contour → apply STFT gains.
    """
    cfg = _helper_cfg(spec, "autoEq25") or _helper_cfg(spec, "autoEq20")
    if cfg.get("enabled") is False:
        return True

    n_bands = int(cfg.get("bands", 25))
    f_min = float(cfg.get("freqMinHz", 90))
    f_max = float(cfg.get("freqMaxHz", 11000))
    strength = float(cfg.get("strength", 0.62))
    max_gain = float(cfg.get("maxGainDb", 3.5))
    smooth = int(cfg.get("smoothPasses", 2))
    sr = int(cfg.get("sampleRate", 48000))

    try:
        import librosa

        y, file_sr = librosa.load(str(wav), sr=sr, mono=True)
        centers = _auto_eq20_band_centers(n_bands, f_min, f_max)
        target = _auto_eq20_speech_reference_db(centers)

        n_fft = 2048
        D = np.abs(librosa.stft(y, n_fft=n_fft, hop_length=512))
        fft_freqs = librosa.fft_frequencies(sr=sr, n_fft=n_fft)
        measured = _auto_eq20_measure_levels(D, fft_freqs, centers)
        gains = _auto_eq20_compute_gains(
            measured, target, strength=strength, max_gain_db=max_gain, smooth_passes=smooth
        )

        out = _auto_eq20_apply_stft(y, sr, centers, gains)
        peak = float(np.max(np.abs(out)) or 1e-6)
        out = out / peak * min(peak, 0.98)
        _save_stereo(wav, np.stack([out, out], axis=1), sr)

        hot = int(np.sum(np.abs(gains) > 0.4))
        label = f"autoEq{n_bands}"
        print(
            f"  {label}: {n_bands} bands, "
            f"gains {gains.min():+.1f}…{gains.max():+.1f} dB ({hot} bands adjusted)",
            flush=True,
        )
        return True
    except ImportError:
        print(
            "autoEq20: needs librosa (pip install librosa soundfile scipy) — skipping",
            file=sys.stderr,
        )
        return True
    except Exception as exc:
        print(f"autoEq20: {exc}", file=sys.stderr)
        return True


def apply_mid_treble(wav: Path, spec: dict[str, Any]) -> bool:
    """Explicit mid + treble polish — mids up, treble restrained."""
    cfg = _helper_cfg(spec, "midTreble")
    if cfg.get("enabled") is False:
        return True
    mid = float(cfg.get("midDb", 1.05))
    treble = min(float(cfg.get("trebleDb", 0.48)), float(cfg.get("trebleMaxDb", 0.72)))
    air = float(cfg.get("airShelfDb", 0.28))
    af = ",".join(
        [
            "highpass=f=90",
            f"equalizer=f=880:width_type=o:width=1.2:g={mid * 0.55}",
            f"equalizer=f=1550:width_type=o:width=1.4:g={mid}",
            f"equalizer=f=2550:width_type=o:width=1.5:g={mid * 0.75}",
            f"equalizer=f=4200:width_type=o:width=2:g={treble}",
            f"highshelf=f=7800:width_type=o:width=2:g={air}",
            f"highshelf=f=11500:width_type=o:width=2:g={-0.35}",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print(f"  midTreble: mids +{mid:.1f} dB, treble +{treble:.1f} dB (capped)", flush=True)
    return ok


def apply_voice_flair(wav: Path, spec: dict[str, Any]) -> bool:
    """Extra flair / excitement — punchy dynamics + smile EQ."""
    cfg = _helper_cfg(spec, "voiceFlair")
    if cfg.get("enabled") is False:
        return True
    punch = float(cfg.get("punchDb", 0.95))
    af = ",".join(
        [
            f"equalizer=f=1100:width_type=o:width=1.3:g={punch * 0.5}",
            f"equalizer=f=2800:width_type=o:width=1.6:g={punch}",
            "compand=attacks=0.14:decays=0.42:points=-80/-80|-58/-38|-32|-22|-14|-8|0/-1.2|6/0|18/4",
            "acompressor=threshold=-22dB:ratio=1.08:attack=45:release=220:makeup=1.03",
            "alimiter=limit=0.97:attack=55:release=200",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print(f"  voiceFlair: excitement punch (+{punch:.1f} dB presence)", flush=True)
    return ok


def apply_spatial_3d(wav: Path, spec: dict[str, Any]) -> bool:
    """Spatial playback — `style: soundcore` = hi-fi harmony; else legacy Haas width."""
    cfg = _helper_cfg(spec, "spatialHarmony") or _helper_cfg(spec, "spatial3d")
    if cfg.get("enabled") is False:
        return True
    style = str(cfg.get("style", "soundcore")).lower()
    if style in ("soundcore", "harmony", "hifi", "spatial", "spatialharmony"):
        if apply_spatial_harmony_core(wav, cfg):
            return True
    width = float(cfg.get("width", 0.12))
    delay_ms = float(cfg.get("delayMs", 0.42))
    room = float(cfg.get("roomMix", 0.05))
    sr = int(cfg.get("sampleRate", 48000))
    try:
        y, sr = _load_mono_stereo(wav, sr)
        mono = y.mean(axis=1).astype(np.float32)
        n_delay = max(1, int(round(sr * delay_ms / 1000.0)))
        right = np.roll(mono, n_delay)
        left = mono.copy()
        cross = width * 0.28
        left = left * (1.0 - width * 0.2) + right * cross
        right = right * (1.0 - width * 0.15) + mono * cross * 0.85
        if room > 0.008:
            tail_len = max(32, int(sr * 0.012))
            kernel = np.exp(-np.linspace(0.0, 6.0, tail_len)).astype(np.float32)
            kernel /= float(np.sum(kernel) or 1.0)
            tail = np.convolve(mono, kernel, mode="same") * room
            left = left + tail
            right = right + tail * 0.9
        peak = float(np.max(np.abs(np.stack([left, right]))) or 1e-6)
        scale = min(0.98 / peak, 1.0)
        stereo = np.stack([left * scale, right * scale], axis=1).astype(np.float32)
        _save_stereo(wav, stereo, sr)
        print(
            f"  spatial3d: width {width:.0%}, {delay_ms:.2f}ms Haas, room {room:.0%}",
            flush=True,
        )
        return True
    except Exception as exc:
        print(f"spatial3d: {exc} — ffmpeg fallback", file=sys.stderr)
        af = (
            f"aecho=0.82:0.86:6|10:0.12|0.07,"
            f"extrastereo=m={1.0 + width * 2.2}:i={room * 0.35}"
        )
        return _run_ffmpeg_af(wav, af)


def apply_tail_finish(wav: Path, spec: dict[str, Any]) -> bool:
    """Pad + stretch the tail so the last word always lands — never clipped short."""
    cfg = _helper_cfg(spec, "tailFinish")
    if cfg.get("enabled") is False:
        return True
    sr = int(cfg.get("sampleRate", 48000))
    pad_ms = float(cfg.get("tailPadMs", 100))
    stretch_ms = float(cfg.get("lastWordStretchMs", 50))
    try:
        import librosa

        y, sr = librosa.load(str(wav), sr=sr, mono=True)
        intervals = librosa.effects.split(y, top_db=float(cfg.get("splitTopDb", 32)))
        if len(intervals) == 0:
            tail_slice = y[-max(1, int(sr * 0.2)) :]
            pre = y[: -len(tail_slice)]
        else:
            last_start, last_end = int(intervals[-1][0]), int(intervals[-1][1])
            pre = y[:last_start]
            tail_slice = y[last_start:last_end]
        skip_stretch = spec.get("narratorUsedWordQueue") or cfg.get("skipStretchAfterWordQueue")
        if tail_slice.size > 8 and stretch_ms > 0 and not skip_stretch:
            tail_slice = stretch_word_tail_np(tail_slice, sr, stretch_ms)
        pad_n = max(0, int(round(sr * pad_ms / 1000.0)))
        if pad_n:
            mode = "constant" if skip_stretch else "edge"
            tail_slice = np.pad(tail_slice.astype(np.float32), (0, pad_n), mode=mode)
        out = np.concatenate([pre.astype(np.float32), tail_slice.astype(np.float32)])
        peak = float(np.max(np.abs(out)) or 1e-6)
        out = (out / peak * min(peak, 0.98)).astype(np.float32)
        _save_stereo(wav, np.stack([out, out], axis=1), sr)
        print(
            f"  tailFinish: stretch {stretch_ms:g}ms + pad {pad_ms:g}ms on final word",
            flush=True,
        )
        return True
    except Exception as exc:
        print(f"tailFinish: {exc}", file=sys.stderr)
        return True


def generate_breath_wav(out: Path, spec: dict[str, Any] | None = None) -> bool:
    """Synthetic inhale + exhale — not the spoken word 'breath'."""
    cfg = _helper_cfg(spec or {}, "breathCue")
    if isinstance(spec, dict) and spec.get("voiceHelpers"):
        vh = spec.get("voiceHelpers")
        if isinstance(vh, dict) and isinstance(vh.get("breathCue"), dict):
            cfg = {**cfg, **vh.get("breathCue", {})}
    sr = int(cfg.get("sampleRate", 48000))
    duration = float(cfg.get("durationSec", 0.9))
    n = max(int(sr * duration), 1)
    t = np.linspace(0.0, 1.0, n, dtype=np.float64)
    inhale_end = float(cfg.get("inhaleRatio", 0.42))
    inhale = np.sin(np.clip(t / inhale_end, 0.0, 1.0) * (np.pi / 2))
    exhale = np.cos(np.clip((t - inhale_end) / max(1e-6, 1.0 - inhale_end), 0.0, 1.0) * (np.pi / 2))
    env = np.where(t < inhale_end, inhale, exhale * float(cfg.get("exhaleLevel", 0.82)))
    rng = np.random.default_rng(42)
    noise = rng.standard_normal(n).astype(np.float32)
    breath = noise * env.astype(np.float32) * float(cfg.get("level", 0.11))
    try:
        from scipy.signal import butter, sosfilt

        sos = butter(4, [180, 2400], btype="band", fs=sr, output="sos")
        breath = sosfilt(sos, breath).astype(np.float32)
    except Exception:
        pass
    peak = float(np.max(np.abs(breath)) or 1e-6)
    breath = breath / peak * min(peak, float(cfg.get("peak", 0.22)))
    out.parent.mkdir(parents=True, exist_ok=True)
    _save_stereo(out, np.stack([breath, breath], axis=1), sr)
    return out.is_file()


def insert_breath_performance_cue(
    wav: Path, text: str, spec: dict[str, Any] | None = None
) -> bool:
    """Insert inhale/exhale audio where script had [[breath]] / 'Breath,' — not spoken."""
    if not breath_cue_enabled(spec):
        return True
    if not narration_has_breath_cue(text):
        return True
    cfg = _helper_cfg(spec or {}, "breathCue")
    if cfg.get("enabled") is False:
        return True
    before_word = str(cfg.get("beforeWord", "grin")).lower()
    words = re.findall(r"[A-Za-z0-9']+", _clean_for_speech_helpers(text).lower())
    marker_frac: float | None = None
    if "[[breath]]" in text.lower():
        m = re.search(r"\[\[breath\]\]", text, flags=re.IGNORECASE)
        if m:
            pre = _clean_for_speech_helpers(text[: m.start()])
            post = _clean_for_speech_helpers(text[m.end() :])
            pre_w = len(re.findall(r"[A-Za-z0-9']+", pre))
            post_w = len(re.findall(r"[A-Za-z0-9']+", post))
            total = pre_w + post_w
            if total > 0:
                marker_frac = pre_w / total
    if marker_frac is None:
        if before_word not in words:
            marker_frac = float(cfg.get("defaultPosition", 0.88))
        else:
            marker_frac = words.index(before_word) / max(len(words), 1)
    sr = 48000
    try:
        import soundfile as sf

        y, file_sr = sf.read(str(wav), dtype="float32", always_2d=True)
        if file_sr != sr:
            import librosa

            y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
        mono = (y.mean(axis=1) if y.ndim > 1 else y).astype(np.float32)
        insert_at = int(np.clip(marker_frac, 0.05, 0.95) * mono.size)
        try:
            import librosa

            intervals = librosa.effects.split(mono, top_db=float(cfg.get("splitTopDb", 28)))
            if before_word in words and len(intervals) >= len(words) // 3:
                target_i = words.index(before_word)
                if target_i < len(intervals):
                    insert_at = max(insert_at, int(intervals[target_i][0]) - int(sr * 0.08))
        except Exception:
            pass
        breath_path = wav.parent / "_breath_cue.wav"
        if not generate_breath_wav(breath_path, spec):
            return True
        b, _ = sf.read(str(breath_path), dtype="float32", always_2d=True)
        breath = (b.mean(axis=1) if b.ndim > 1 else b).astype(np.float32)
        gap_n = max(0, int(sr * float(cfg.get("gapMs", 40)) / 1000.0))
        fade_n = max(1, int(sr * float(cfg.get("fadeMs", 25)) / 1000.0))
        pre = mono[:insert_at]
        post = mono[insert_at:]
        if gap_n:
            pre = np.concatenate([pre, np.zeros(gap_n, dtype=np.float32)])
        if fade_n and breath.size > fade_n * 2:
            f = np.linspace(0.0, 1.0, fade_n, dtype=np.float32)
            breath[:fade_n] *= f
            breath[-fade_n:] *= f[::-1]
        merged = np.concatenate([pre, breath, post])
        peak = float(np.max(np.abs(merged)) or 1e-6)
        merged = merged / peak * min(peak, 0.98)
        sf.write(str(wav), np.stack([merged, merged], axis=1), sr, subtype="PCM_16")
        print(
            f"  breathCue: inhale/exhale inserted before {before_word!r} (not spoken)",
            flush=True,
        )
        return True
    except Exception as exc:
        print(f"breathCue: {exc}", file=sys.stderr)
        return True


def stretch_word_tail_np(mono: np.ndarray, sr: int, stretch_ms: float) -> np.ndarray:
    extra = max(1, int(round(sr * stretch_ms / 1000.0)))
    target = mono.size + extra
    x_old = np.linspace(0.0, 1.0, mono.size, dtype=np.float64)
    x_new = np.linspace(0.0, 1.0, target, dtype=np.float64)
    return np.interp(x_new, x_old, mono.astype(np.float64)).astype(np.float32)


def apply_finalizer(wav: Path, spec: dict[str, Any]) -> bool:
    """Hi-fi final — gentle lows, spatial-safe peak, broadcast level."""
    cfg = _helper_cfg(spec, "finalizer")
    target_i = float(cfg.get("loudnormI", -16))
    tp = float(cfg.get("truePeak", -1.5))
    lra = float(cfg.get("lra", 8))
    af = ",".join(
        [
            "highpass=f=95",
            "equalizer=f=140:width_type=o:width=1.1:g=-1.2",
            "equalizer=f=240:width_type=o:width=1:g=-0.7",
            "equalizer=f=6200:width_type=o:width=2:g=-0.45",
            f"loudnorm=I={target_i}:TP={tp}:LRA={lra}:print_format=none",
            "alimiter=limit=0.93:attack=14:release=140",
        ]
    )
    ok = _run_ffmpeg_af(wav, af)
    if ok:
        print(f"  finalizer: hi-fi loudnorm I={target_i} TP={tp}", flush=True)
    return ok


HELPER_APPLY: dict[str, Callable[[Path, dict[str, Any]], bool]] = {
    "humanSmooth": apply_human_smooth,
    "antiStutter": apply_anti_stutter,
    "character": apply_character,
    "pitchAutotune": apply_pitch_autotune,
    "deEss": apply_de_ess,
    "warmth": apply_warmth,
    "deClick": apply_de_click,
    "breathSmooth": apply_breath_smooth,
    "roomTone": apply_room_tone,
    "naturalPacing": apply_natural_pacing,
    "auditor": apply_auditor,
    "finalizer": apply_finalizer,
    "autoEq20": apply_auto_eq_20,
    "autoEQ20": apply_auto_eq_20,
    "autoEq25": apply_auto_eq_20,
    "autoEQ25": apply_auto_eq_20,
    "lowClean": apply_low_clean,
    "speakerSafe": apply_speaker_safe,
    "ttsSmooth": apply_tts_smooth,
    "midTreble": apply_mid_treble,
    "voiceFlair": apply_voice_flair,
    "spatial3d": apply_spatial_3d,
    "spatial3D": apply_spatial_3d,
    "spatialHarmony": apply_spatial_3d,
    "tailFinish": apply_tail_finish,
    "steadyVoice": apply_steady_voice,
    "naturalPitch": apply_natural_pitch,
    "phraseFall": apply_phrase_fall,
    "phraseDynamics": apply_phrase_dynamics,
    "casualTone": apply_casual_tone,
    "vocalSpark": apply_vocal_spark,
    "autoDynamics": apply_auto_dynamics,
    "raw": apply_raw,
}


def resolve_helper_chain(spec: dict[str, Any]) -> list[str]:
    """Order of helpers to run after Personal Voice capture."""
    spec = dict(spec)
    vh = spec.get("voiceHelpers")
    if isinstance(vh, dict):
        if isinstance(vh.get("chain"), list) and vh["chain"]:
            return [str(x) for x in vh["chain"]]
        preset = str(vh.get("preset", "")).lower()
        if preset in ("custom", "chain"):
            pass
        elif preset in HELPER_PRESETS:
            return list(HELPER_PRESETS[preset])

    delivery = str(spec.get("narratorPersonalDelivery", "")).lower()
    if delivery in HELPER_PRESETS:
        return list(HELPER_PRESETS[delivery])
    if delivery == "fluent" or str(spec.get("narratorAutotuneMode", "")).lower() in (
        "fluent",
        "flow",
    ):
        return list(HELPER_PRESETS["human"])
    if delivery == "autotune" or (
        spec.get("narratorAutotune")
        and str(spec.get("narratorAutotuneMode", "")).lower() not in ("fluent", "flow")
    ):
        return list(HELPER_PRESETS["pitchAutotune"])
    if spec.get("narratorRawPersonalVoice"):
        return []
    return list(HELPER_PRESETS["human"])


def apply_voice_helpers(wav: Path, spec: dict[str, Any] | None = None) -> list[str]:
    """Run helper chain or voice ladder; returns names that ran."""
    spec = spec or {}
    try:
        ladder_path = _TOOLS / "personal-voice-ladder.py"
        if ladder_path.is_file():
            sp = importlib.util.spec_from_file_location("pvl", ladder_path)
            ladder_mod = importlib.util.module_from_spec(sp)
            assert sp.loader
            sp.loader.exec_module(ladder_mod)
            if ladder_mod.resolve_ladder_phases(spec):
                summary = ladder_mod.apply_voice_ladder(wav, spec)
                return summary.get("helpers", [])
    except Exception as exc:
        print(f"voice ladder fallback: {exc}", file=sys.stderr)

    chain = resolve_helper_chain(spec)
    if not chain:
        return []
    applied: list[str] = []
    for name in chain:
        fn = HELPER_APPLY.get(name)
        if not fn:
            print(f"Unknown voice helper: {name}", file=sys.stderr)
            continue
        try:
            fn(wav, spec)
            applied.append(name)
        except Exception as exc:
            print(f"Voice helper {name} failed: {exc}", file=sys.stderr)
    return applied


def print_helper_catalog() -> None:
    print("Personal Voice Helpers\n")
    for preset, chain in HELPER_PRESETS.items():
        print(f"  preset '{preset}': {', '.join(chain) or '(none)'}")
    print("\nHelpers:\n")
    for key, meta in HELPER_CATALOG.items():
        print(f"  {key} — {meta['title']}")
        print(f"      {meta['summary']}")
        print(f"      Robotic risk: {meta['roboticRisk']}\n")
    print("Ladder presets: ladderDocumentary (phased: algorithm → character → auditor → finalizer)")
    print("\nFuture helpers (not implemented yet):")
    for name in (
        "noiseGate",
        "voiceCloneMatch",
        "transcriptAlign",
    ):
        print(f"  - {name}")


if __name__ == "__main__":
    print_helper_catalog()
