"""Narration: Personal Voice (say+mysay, word queue) → voice ladder → breath/tail finish.

Docs: PERSONAL_VOICE_NARRATION.md. Config: DemoRecapApproved/ApprovedCards.json.
Fallback: edge-tts. Slot-synced to each clip when not in fullScript mode.
"""
from __future__ import annotations

import asyncio
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any

import numpy as np

_TOOLS_DIR = Path(__file__).resolve().parent
_PERSONAL_VOICE_SWIFT = _TOOLS_DIR / "personal-voice-speak.swift"
_MYSAY_DYLIB = _TOOLS_DIR / "mysay.dylib"

from envkit_paths import approved_dir

APPROVED_VOICE_DIR = approved_dir()
REFERENCE_WAV_NAMES = (
    "NarratorVoiceReference.wav",
    "NarratorVoice.wav",
    "_narrator_voice.wav",
)
PERSONAL_VOICE_NAME_FILE = "NarratorPersonalVoice.txt"


def find_ffprobe(ffmpeg_hint: str | None = None) -> str:
    if ffmpeg_hint:
        sibling = Path(ffmpeg_hint).resolve().parent / "ffprobe"
        if sibling.is_file():
            return str(sibling)
    for c in ("ffprobe", "/opt/homebrew/bin/ffprobe", "/usr/local/bin/ffprobe"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise FileNotFoundError("ffprobe not found (install ffmpeg; ffprobe ships with it)")


def find_ffmpeg() -> str:
    for c in ("ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    return "ffmpeg"


def edge_tts_available() -> bool:
    try:
        import edge_tts  # noqa: F401

        return True
    except ImportError:
        return False


def install_edge_tts() -> bool:
    print(f"Installing edge-tts into {sys.executable} …")
    subprocess.run(
        [sys.executable, "-m", "pip", "install", "-q", "edge-tts>=6.1"],
        check=False,
    )
    return edge_tts_available()


EDGE_VOICE_MAP = {
    "daniel": "en-US-AndrewNeural",
    "andrew": "en-US-AndrewNeural",
    "alex": "en-US-AndrewNeural",
    "guy": "en-US-GuyNeural",
    "christopher": "en-US-ChristopherNeural",
    "brian": "en-US-BrianNeural",
    "eric": "en-US-EricNeural",
    "samantha": "en-US-JennyNeural",
    "jenny": "en-US-JennyNeural",
    "karen": "en-AU-NatashaNeural",
    "moira": "en-IE-EmilyNeural",
    "fred": "en-US-ChristopherNeural",
}


def _clean_for_speech(text: str) -> str:
    t = (text or "").strip()
    t = re.sub(r"\s+", " ", t)
    t = t.replace("—", ", ").replace("–", ", ")
    t = re.sub(r"[^\w\s.,!?'%-]", "", t)
    return t


def _clean_phrase_for_capture(phrase: str) -> str:
    """Keep em-dashes for natural clause delivery in sentence-queue captures."""
    t = (phrase or "").strip()
    t = re.sub(r"\s+", " ", t)
    t = re.sub(r"[^\w\s.,!?'%—–-]", "", t)
    return t.strip()


_NARRATION_META_PATTERNS: tuple[re.Pattern[str], ...] = tuple(
    re.compile(p, re.IGNORECASE)
    for p in (
        r"\bbeat\s+\d+",
        r"\bframe\s*#?\s*\d+",
        r"\bcheckpoint\s+frames?\b",
        r"\b120\s*fps\b",
        r"\btimelapse\b",
        r"\benvironment\s+kit\b",
        r"\bcave\s+grader\b",
        r"\bfullworld\b",
        r"\badditive\s+surface[_ ]build\b",
        r"\bsurface[_ ]build\b",
        r"\beditor\s+queue\b",
        r"\bopencv\b",
        r"\bautomated\b.*\b(edit|recap|unity)\b",
        r"\bai[- ]assisted\b",
        r"\bcaptions?\s+and\s+on[- ]screen\b",
        r"clear terrain change since the last hold",
        r"compare this\b.*\b(footprint|checkpoint|next)\b",
        r"\banchor\b.*\bcompare\b",
        r"staggered lecture captions",
        r"without (a\s+)?manual editor",
        r"without babysitting",
    )
)

_SIGNATURE_NARRATION_PATTERNS = tuple(
    re.compile(p, re.IGNORECASE)
    for p in (
        r"\bmy signature\b",
        r"\bsignature on\b",
        r"\bsee my signature\b",
        r"\bsigned (here|below|under)\b",
        r"\bautograph\b",
        r"\bsigning (off|the video)\b",
        r"\bname (written|printed) on\b",
        r"\bportrait\b.*\bsignature\b",
        r"\bsignature\b.*\bportrait\b",
    )
)


def _is_signature_narration_line(line: str) -> bool:
    t = (line or "").strip()
    if not t:
        return False
    return any(p.search(t) for p in _SIGNATURE_NARRATION_PATTERNS)


def _is_narration_meta_line(line: str) -> bool:
    t = (line or "").strip()
    if not t or len(t) < 10:
        return True
    return any(p.search(t) for p in _NARRATION_META_PATTERNS)


def sanitize_narration_text(text: str) -> str:
    """Strip pipeline / beat / fps jargon for spoken documentary copy."""
    t = _clean_for_speech(text)
    if not t:
        return t
    parts = re.split(r"(?<=[.!?])\s+", t)
    kept = [
        p.strip()
        for p in parts
        if p.strip() and not _is_narration_meta_line(p) and not _is_signature_narration_line(p)
    ]
    if kept:
        t = " ".join(kept)
    t = re.sub(
        r"\bFullWorld additive surface_build\b",
        "the world build",
        t,
        flags=re.IGNORECASE,
    )
    return re.sub(r"\s+", " ", t).strip()


def _narration_lines_from_milestone(m: dict[str, Any]) -> list[str]:
    lines: list[str] = []
    for key in ("line1", "line2"):
        raw = _clean_for_speech(str(m.get(key, "") or ""))
        if raw and not _is_narration_meta_line(raw):
            lines.append(raw)
    l3 = _clean_for_speech(str(m.get("line3", "") or ""))
    if l3 and not _is_narration_meta_line(l3):
        lines.append(l3)
    out: list[str] = []
    for line in lines:
        norm = line.lower()[:80]
        if any(norm in o.lower() or o.lower() in norm for o in out):
            continue
        out.append(line)
    return out


def _humanize_personal_light(text: str) -> str:
    """Documentary tone — contractions + flow, keep dramatic dashes for TTS inflection."""
    t = _clean_for_speech(text)
    if not t:
        return t
    subs = (
        (r"\bI am\b", "I'm"),
        (r"\bI will\b", "I'll"),
        (r"\bwe are\b", "we're"),
        (r"\bthat is\b", "that's"),
        (r"\bit is\b", "it's"),
        (r"\bdo not\b", "don't"),
        (r"\bcannot\b", "can't"),
        (r"\bdoes not\b", "doesn't"),
        (r"\bwe have\b", "we've"),
    )
    for pat, rep in subs:
        t = re.sub(pat, rep, t, flags=re.IGNORECASE)
    t = re.sub(r"\s+and then\s+", ", then ", t, flags=re.IGNORECASE)
    t = re.sub(r"\s+;\s+", ", ", t)
    t = expand_breath_performance_markers(t)
    return t


def expand_breath_performance_markers(text: str) -> str:
    """Replace spoken 'Breath' with [[breath]] — filled with real inhale/exhale in post."""
    t = text
    t = re.sub(r"\bBreath,?\s*", "[[breath]] ", t, flags=re.IGNORECASE)
    t = re.sub(r"\s+", " ", t).strip()
    return t


def strip_breath_markers_for_capture(text: str) -> str:
    """Never send 'breath' to say — cue is inserted as audio later."""
    t = re.sub(r"\[\[breath\]\]\s*", "", text, flags=re.IGNORECASE)
    t = re.sub(r"\bBreath,?\s*", "", t, flags=re.IGNORECASE)
    return re.sub(r"\s+", " ", t).strip()


def narration_has_breath_cue(text: str) -> bool:
    return bool(re.search(r"\bBreath\b|\[\[breath\]\]", text, flags=re.IGNORECASE))


def humanize_speech_text(text: str, spec: dict[str, Any] | None = None) -> str:
    """Softer, conversational phrasing for TTS — contractions, flow, micro-pauses."""
    if spec is not None and not spec.get("narratorHumanize", True):
        return _clean_for_speech(text)
    if spec is not None and spec.get("narratorPersonalHumanize", False):
        eng = str(spec.get("narratorEngine", "auto")).lower()
        if eng in ("personal", "auto") and (
            spec.get("narratorRequirePersonal") or spec.get("narratorPersonalVoice")
        ):
            return _humanize_personal_light(text)
    t = _clean_for_speech(text)
    if not t:
        return t
    # Natural contractions (sounds more human on Personal Voice / neural TTS)
    subs = (
        (r"\bI am\b", "I'm"),
        (r"\bI will\b", "I'll"),
        (r"\bwe are\b", "we're"),
        (r"\bthat is\b", "that's"),
        (r"\bit is\b", "it's"),
        (r"\bdo not\b", "don't"),
        (r"\bcannot\b", "can't"),
        (r"\bdoes not\b", "doesn't"),
        (r"\bwe have\b", "we've"),
        (r"\byou will\b", "you'll"),
    )
    for pat, rep in subs:
        t = re.sub(pat, rep, t, flags=re.IGNORECASE)
    t = re.sub(r"\s*;\s*", ", ", t)
    t = re.sub(r"\s+and then\s+", ", then ", t, flags=re.IGNORECASE)
    t = re.sub(r"\s+—\s+", ", ", t)
    # Light comma breaths on long technical chains
    t = re.sub(r",\s+and\s+", ", and ", t)
    t = re.sub(r"\.\s+([A-Z])", r". \1", t)
    stiff = (
        (r"\bSession online\b", "Session's online"),
        (r"\bWe are proving\b", "We're proving"),
        (r"\bCompare borders\b", "Compare the borders"),
        (r"\bQueue may be\b", "The queue might be"),
        (r"\bPreserve this frame\b", "Keep this frame"),
    )
    for pat, rep in stiff:
        t = re.sub(pat, rep, t, flags=re.IGNORECASE)
    t = t.replace("This is the proof of concept", "That's the proof of concept")
    t = re.sub(r"\.\s+(Now|Here|Next|So)\b", r", \1", t)
    t = re.sub(r"\s+", " ", t).strip()
    return t


def caption_readable_end_sec(spec: dict[str, Any], milestone: dict[str, Any] | None = None) -> float:
    """Seconds until all caption lines have been on screen long enough to read."""
    l3 = float(spec.get("captionLine3DelaySec", 3.0))
    if milestone and milestone.get("beatKind") == "subbeat":
        l3 = min(l3, float(spec.get("captionLine3DelaySecSubbeat", 1.8)))
    l1 = float(spec.get("captionLine1FadeSec", 1.1))
    pause = float(spec.get("captionReadPauseSec", 3.0))
    return l3 + l1 + pause


def hold_narrator_offset_sec(spec: dict[str, Any], milestone: dict[str, Any] | None = None) -> float:
    """Start voice after captions are readable (never talk over unrevealed lines)."""
    readable = caption_readable_end_sec(spec, milestone)
    return max(float(spec.get("narratorStartOffsetSec", 5.5)), readable)


def estimate_speech_duration_sec(text: str, spec: dict[str, Any] | None = None) -> float:
    words = max(1, len((text or "").split()))
    spec = spec or {}
    rate = int(spec.get("narratorRate", 200))
    pace = max(0.75, float(spec.get("narratorSpeechPace", 1.0)))
    wps = 2.35 * (rate / 200.0) / pace
    return words / wps + 0.4


def measure_speech_duration(
    text: str,
    *,
    voice: str,
    rate: int,
    engine: str,
    spec: dict[str, Any] | None,
    run_dir: Path | None,
    plan_dir: Path,
) -> float:
    t = (text or "").strip()
    if not t:
        return 0.5
    import hashlib

    flat = flatten_narrator_personal_settings(spec or {})
    say_r = personal_say_rate(flat)
    key = hashlib.sha256(f"{engine}|{say_r}|{t}".encode()).hexdigest()[:18]
    cached = plan_dir / f"plan_{key}.wav"
    plan_dir.mkdir(parents=True, exist_ok=True)
    if cached.is_file() and cached.stat().st_size > 256:
        return probe_duration(cached)
    sync_mode = str(flat.get("narrationSyncMode", "")).lower()
    plan_mode = str(flat.get("narrationPlanMode", "estimate")).lower()
    if sync_mode != "speech_first" and plan_mode in ("estimate", "fast", "words"):
        return estimate_speech_duration_sec(t, flat)
    if speech_to_wav(t, cached, voice=voice, rate=say_r, engine=engine, spec=spec, run_dir=run_dir):
        return probe_duration(cached)
    return estimate_speech_duration_sec(t, flat)


def plan_wav_path(plan_dir: Path, text: str, spec: dict[str, Any] | None, engine: str) -> Path:
    import hashlib

    flat = flatten_narrator_personal_settings(spec or {})
    say_r = personal_say_rate(flat)
    key = hashlib.sha256(f"{engine}|{say_r}|{(text or '').strip()}".encode()).hexdigest()[:18]
    return plan_dir / f"plan_{key}.wav"


def clip_sec_for_narration(
    speech_sec: float,
    spec: dict[str, Any],
    *,
    start_offset_sec: float,
    milestone: dict[str, Any] | None = None,
) -> float:
    """Video hold length = captions readable, then full speech, then tail."""
    tail = float(spec.get("narrationTailPadSec", 0.5))
    pad = float(spec.get("narrationPlanPaddingSec", 0.35))
    caption_end = caption_readable_end_sec(spec, milestone) if milestone else 0.0
    base = float(
        (milestone or {}).get("holdSec")
        or (
            spec.get("subbeatHoldSec", 8.0)
            if milestone and milestone.get("beatKind") == "subbeat"
            else spec.get("milestoneHoldSec", 12.0)
        )
    )
    need = max(
        base,
        caption_end + speech_sec + tail + pad,
        start_offset_sec + speech_sec + tail + pad,
    )
    sync_mode = str(spec.get("narrationSyncMode", "")).lower()
    if sync_mode != "speech_first":
        cap = float(spec.get("maxMilestoneHoldSec", 0) or 0)
        if cap > 0 and (not milestone or milestone.get("beatKind") != "subbeat"):
            need = min(need, cap)
        sub_cap = float(spec.get("maxSubbeatHoldSec", 0) or 0)
        if sub_cap > 0 and milestone and milestone.get("beatKind") == "subbeat":
            need = min(need, sub_cap)
    return need


def apply_speech_first_sync_plan(
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    *,
    voice: str,
    rate: int,
    engine: str,
    run_dir: Path | None,
    plan_dir: Path,
) -> None:
    """Synthesize each cue once; size holds from measured speech (video matches voice)."""
    plan_dir.mkdir(parents=True, exist_ok=True)
    n = len(milestones)
    for i, m in enumerate(milestones):
        if m.get("_skipNarration"):
            m["_plannedHoldSec"] = float(
                spec.get("subbeatHoldSec", 7.0)
                if m.get("beatKind") == "subbeat"
                else spec.get("milestoneHoldSec", 11.0)
            )
            continue
        script = beat_narration_script(m, spec)
        off = hold_narrator_offset_sec(spec, m)
        if not script.strip():
            m["_plannedHoldSec"] = float(
                spec.get("subbeatHoldSec", 7.0)
                if m.get("beatKind") == "subbeat"
                else spec.get("milestoneHoldSec", 11.0)
            )
            continue
        speech = measure_speech_duration(
            script,
            voice=voice,
            rate=rate,
            engine=engine,
            spec=spec,
            run_dir=run_dir,
            plan_dir=plan_dir,
        )
        m["_plannedSpeechSec"] = speech
        m["_plannedNarratorOffsetSec"] = off
        m["_plannedHoldSec"] = clip_sec_for_narration(
            speech, spec, start_offset_sec=off, milestone=m
        )
        kind = m.get("beatKind", "checkpoint")
        print(
            f"  Sync {i + 1}/{n} ({kind}): speech {speech:.1f}s → hold {m['_plannedHoldSec']:.1f}s",
            flush=True,
        )

    intro_script = intro_narration_script(spec)
    off_i = float(spec.get("introNarratorOffsetSec", 0.8))
    sp_i = measure_speech_duration(
        intro_script,
        voice=voice,
        rate=rate,
        engine=engine,
        spec=spec,
        run_dir=run_dir,
        plan_dir=plan_dir,
    )
    spec["_plannedIntroSec"] = max(
        float(spec.get("introSec", 9.0)),
        off_i + sp_i + float(spec.get("narrationTailPadSec", 0.5)),
    )

    outro_script = outro_narration_script(spec)
    off_o = float(spec.get("outroNarratorOffsetSec", 0.8))
    sp_o = measure_speech_duration(
        outro_script,
        voice=voice,
        rate=rate,
        engine=engine,
        spec=spec,
        run_dir=run_dir,
        plan_dir=plan_dir,
    )
    spec["_plannedOutroSec"] = max(
        float(spec.get("outroSec", 10.0)),
        off_o + sp_o + float(spec.get("narrationTailPadSec", 0.5)),
    )


def planned_hold_sec(
    milestone: dict[str, Any],
    spec: dict[str, Any],
    *,
    voice: str,
    rate: int,
    engine: str,
    run_dir: Path | None,
    plan_dir: Path,
) -> float:
    """Hold length: captions readable → full narration → tail pad before transition."""
    if milestone.get("_plannedHoldSec") is not None:
        return float(milestone["_plannedHoldSec"])
    base = float(
        milestone.get("holdSec")
        or (
            spec.get("subbeatHoldSec", 8.0)
            if milestone.get("beatKind") == "subbeat"
            else spec.get("milestoneHoldSec", 14.0)
        )
    )
    if not spec.get("syncHoldToNarration", True):
        return base
    script = beat_narration_script(milestone, spec)
    if not script.strip():
        return base
    off = hold_narrator_offset_sec(spec, milestone)
    speech = measure_speech_duration(
        script, voice=voice, rate=rate, engine=engine, spec=spec, run_dir=run_dir, plan_dir=plan_dir
    )
    return clip_sec_for_narration(speech, spec, start_offset_sec=off, milestone=milestone)


def planned_intro_sec(spec: dict[str, Any], script: str, *, voice: str, rate: int, engine: str, run_dir: Path | None, plan_dir: Path) -> float:
    base = float(spec.get("introSec", 9.0))
    if not spec.get("syncHoldToNarration", True):
        return base
    off = float(spec.get("introNarratorOffsetSec", 0.8))
    speech = measure_speech_duration(
        script, voice=voice, rate=rate, engine=engine, spec=spec, run_dir=run_dir, plan_dir=plan_dir
    )
    return max(base, off + speech + float(spec.get("narrationTailPadSec", 1.8)))


def planned_outro_sec(spec: dict[str, Any], script: str, *, voice: str, rate: int, engine: str, run_dir: Path | None, plan_dir: Path) -> float:
    base = float(spec.get("outroSec", 10.0))
    if not spec.get("syncHoldToNarration", True):
        return base
    off = float(spec.get("outroNarratorOffsetSec", 0.8))
    speech = measure_speech_duration(
        script, voice=voice, rate=rate, engine=engine, spec=spec, run_dir=run_dir, plan_dir=plan_dir
    )
    return max(base, off + speech + float(spec.get("narrationTailPadSec", 1.8)))


_PERSONAL_PAUSE_KEYS = (
    ("pauseSentMs", "narratorPauseSentMs"),
    ("pauseCommaMs", "narratorPauseCommaMs"),
    ("pausePeriodMs", "narratorPausePeriodMs"),
    ("pauseQuestionMs", "narratorPauseQuestionMs"),
    ("pauseExclamationMs", "narratorPauseExclamationMs"),
    ("pauseQuotesMs", "narratorPauseQuotesMs"),
    ("pauseParenthesesMs", "narratorPauseParenthesesMs"),
    ("pauseBracesMs", "narratorPauseBracesMs"),
    ("pauseBracketsMs", "narratorPauseBracketsMs"),
    ("pauseColonMs", "narratorPauseColonMs"),
    ("pauseSemicolonMs", "narratorPauseSemicolonMs"),
    ("pauseApostropheMs", "narratorPauseApostropheMs"),
)


def flatten_narrator_personal_settings(data: dict[str, Any]) -> dict[str, Any]:
    """Merge ApprovedCards.json narratorPersonalSettings into flat spec keys."""
    out = dict(data)
    explicit = set(data.keys())
    nested = data.get("narratorPersonalSettings")
    if not isinstance(nested, dict):
        return out
    voice = nested.get("voice")
    if voice and "narratorPersonalVoice" not in explicit:
        out["narratorPersonalVoice"] = str(voice).strip()
    rate = nested.get("sayRate")
    vh = data.get("voiceHelpers")
    if isinstance(vh, dict) and vh.get("sayRate") is not None:
        rate = vh.get("sayRate")
    if rate is not None:
        r = float(rate)
        out["narratorPersonalSayRate"] = r
        out["narratorRate"] = r
    if isinstance(vh, dict):
        out["voiceHelpers"] = vh
    for nk, sk in _PERSONAL_PAUSE_KEYS:
        if nested.get(nk) is not None and sk not in explicit:
            out[sk] = int(nested[nk])
    if nested.get("naturalPauses") is not None and "narratorNaturalPauses" not in explicit:
        out["narratorNaturalPauses"] = bool(nested["naturalPauses"])
    if nested.get("delivery") is not None and "narratorPersonalDelivery" not in explicit:
        out["narratorPersonalDelivery"] = str(nested["delivery"]).strip().lower()
    if nested.get("personalHumanize") is not None and "narratorPersonalHumanize" not in explicit:
        out["narratorPersonalHumanize"] = bool(nested["personalHumanize"])
    if nested.get("rawPersonalVoice") is not None and "narratorRawPersonalVoice" not in explicit:
        out["narratorRawPersonalVoice"] = bool(nested["rawPersonalVoice"])
    if nested.get("autotune") is not None and "narratorAutotune" not in explicit:
        out["narratorAutotune"] = bool(nested["autotune"])
    if nested.get("autotuneStrength") is not None and "narratorAutotuneStrength" not in explicit:
        out["narratorAutotuneStrength"] = float(nested["autotuneStrength"])
    if nested.get("autotuneSmoothMs") is not None and "narratorAutotuneSmoothMs" not in explicit:
        out["narratorAutotuneSmoothMs"] = float(nested["autotuneSmoothMs"])
    if nested.get("maxPauseMs") is not None and "narratorMaxPauseMs" not in explicit:
        out["narratorMaxPauseMs"] = int(nested["maxPauseMs"])
    return out


def personal_delivery_preset(spec: dict[str, Any] | None) -> str:
    """raw | fluent | autotune | light | natural — post-capture delivery."""
    spec = flatten_narrator_personal_settings(spec or {})
    d = str(spec.get("narratorPersonalDelivery", "")).lower()
    if d in ("fluent", "flow"):
        return "fluent"
    if d == "autotune" or (
        spec.get("narratorAutotune")
        and str(spec.get("narratorAutotuneMode", "fluent")).lower() not in ("fluent", "flow")
    ):
        return "autotune"
    if spec.get("narratorAutotune") and str(spec.get("narratorAutotuneMode", "")).lower() in (
        "fluent",
        "flow",
    ):
        return "fluent"
    if spec.get("narratorRawPersonalVoice"):
        return "raw"
    return d or "fluent"


def _pause_ms(spec: dict[str, Any], spec_key: str, default: int) -> int:
    return int(spec.get(spec_key, default))


def wpm_speed_multiplier(spec: dict[str, Any] | None) -> float:
    """
    Speed up delivery when narratorSpeechDurationFactor is 0.85 → 1/0.85 ≈ 1.176× wpm.
    Or set narratorWpmMultiplier explicitly (e.g. 1.176).
    """
    spec = flatten_narrator_personal_settings(spec or {})
    if spec.get("narratorWpmMultiplier") is not None:
        return max(0.5, min(2.0, float(spec["narratorWpmMultiplier"])))
    dur = float(spec.get("narratorSpeechDurationFactor", 1.0))
    if 0.0 < dur < 1.0:
        return 1.0 / dur
    return 1.0


def apply_wpm_speed(rate: float | int, spec: dict[str, Any] | None) -> int:
    return int(round(float(rate) * wpm_speed_multiplier(spec)))


def personal_say_rate_raw(spec: dict[str, Any] | None) -> int:
    """Base wpm from cards before narratorSpeechDurationFactor."""
    spec = flatten_narrator_personal_settings(spec or {})
    raw = spec.get("narratorPersonalSayRate") or spec.get("narratorRate", 186.24)
    return int(round(float(raw)))


def personal_say_rate(spec: dict[str, Any] | None) -> int:
    """macOS `say -r` is words-per-minute — lower = calmer, less robotic."""
    return apply_wpm_speed(personal_say_rate_raw(spec), spec)


def segment_queue_say_rate(spec: dict[str, Any] | None, unit_index: int = 0) -> int:
    """Natural documentary rate — sentences use base sayRate, not word-queue boost."""
    cfg = word_queue_cfg(spec)
    raw_base = personal_say_rate_raw(spec)
    if capture_queue_mode(spec) == "sentence":
        sent_rate = float(cfg.get("sayRate", raw_base))
        step = float(cfg.get("perUnitStepWpm", cfg.get("perWordStepWpm", 0.25)))
        boost = float(cfg.get("perSentenceWpmBoost", cfg.get("perWordWpmBoost", 0)))
        rate = apply_wpm_speed(sent_rate + boost + step * unit_index, spec)
        return int(min(float(cfg.get("maxSayRate", 228)), max(160, rate)))
    wq = float(cfg.get("sayRate", raw_base * float(cfg.get("rateBoost", 1.08))))
    return int(min(float(cfg.get("maxSayRate", 222)), apply_wpm_speed(wq, spec)))


def personal_word_queue_say_rate(spec: dict[str, Any] | None) -> int:
    """
    Per-word capture runs faster than full phrases — isolated tokens sound sluggish
    at documentary pace. Uses wordQueue.sayRate or rateBoost on the base sayRate.
    """
    spec = flatten_narrator_personal_settings(spec or {})
    cfg = word_queue_cfg(spec)
    if cfg.get("sayRate") is not None:
        return apply_wpm_speed(float(cfg["sayRate"]), spec)
    base = personal_say_rate(spec)
    boost = float(cfg.get("rateBoost", 1.1))
    boosted = int(round(base * boost))
    lo = int(cfg.get("minSayRate", base))
    hi = int(cfg.get("maxSayRate", 225))
    return max(lo, min(hi, boosted))


def strip_slnc_markup(text: str) -> str:
    """Remove say [[slnc N]] tags (used when they produce empty audio)."""
    t = re.sub(r"\s*\[\[slnc\s+\d+\]\]\s*", " ", text, flags=re.IGNORECASE)
    return re.sub(r"\s+", " ", t).strip()


def _cap_pause_ms(spec: dict[str, Any], spec_key: str, default: int) -> int:
    """Long [[slnc]] values (e.g. 1200ms) often make say+mysay output silent 4KB CAF files."""
    raw = _pause_ms(spec, spec_key, default)
    cap = int(spec.get("narratorMaxPauseMs", 650))
    nested = spec.get("narratorPersonalSettings") or {}
    if isinstance(nested, dict) and nested.get("maxPauseMs") is not None:
        cap = int(nested["maxPauseMs"])
    return min(raw, cap)


def wav_is_audible(path: Path, *, min_sec: float = 0.25, min_mean_db: float = -52.0) -> bool:
    if not path.is_file() or path.stat().st_size < 512:
        return False
    try:
        dur = probe_duration(path)
        if dur < min_sec:
            return False
        proc = subprocess.run(
            [
                find_ffmpeg(),
                "-hide_banner",
                "-i",
                str(path),
                "-af",
                "volumedetect",
                "-f",
                "null",
                "-",
            ],
            capture_output=True,
            text=True,
        )
        for line in (proc.stderr or "").splitlines():
            if "mean_volume:" in line:
                db = float(line.split("mean_volume:")[1].split("dB")[0].strip())
                return db > min_mean_db
    except (ValueError, subprocess.CalledProcessError, FileNotFoundError):
        pass
    return dur >= min_sec


def word_queue_enabled(spec: dict[str, Any] | None) -> bool:
    spec = flatten_narrator_personal_settings(spec or {})
    if spec.get("narratorWordQueue") is False:
        return False
    vh = spec.get("voiceHelpers") if isinstance(spec.get("voiceHelpers"), dict) else {}
    wq = vh.get("wordQueue") if isinstance(vh.get("wordQueue"), dict) else {}
    if wq.get("enabled") is False:
        return False
    return bool(spec.get("narratorWordQueue", wq.get("enabled", True)))


def portfolio_word_queue_first(spec: dict[str, Any] | None) -> bool:
    spec = flatten_narrator_personal_settings(spec or {})
    if spec.get("narratorPortfolioMode") is True:
        return True
    cfg = word_queue_cfg(spec)
    return bool(cfg.get("portfolioFirst", False))


def word_queue_max_words(spec: dict[str, Any] | None) -> int:
    cfg = word_queue_cfg(spec)
    if portfolio_word_queue_first(spec):
        return int(cfg.get("portfolioMaxWords", 220))
    return int(cfg.get("maxWords", 28))


def word_queue_should_use(text: str, spec: dict[str, Any] | None) -> bool:
    """Word/phrase capture for short lines or portfolio mode (natural per-unit delivery)."""
    if not word_queue_enabled(spec):
        return False
    max_words = word_queue_max_words(spec)
    tokens = tokenize_words_for_queue(text)
    if not tokens:
        return False
    if len(tokens) > max_words:
        print(
            f"Word queue skipped ({len(tokens)} words > max {max_words}); "
            "using single-pass capture with comma pacing.",
            flush=True,
        )
        return False
    return True


def word_queue_cfg(spec: dict[str, Any] | None) -> dict[str, Any]:
    spec = flatten_narrator_personal_settings(spec or {})
    vh = spec.get("voiceHelpers") if isinstance(spec.get("voiceHelpers"), dict) else {}
    wq = vh.get("wordQueue") if isinstance(vh.get("wordQueue"), dict) else {}
    return wq if isinstance(wq, dict) else {}


def tokenize_words_for_queue(text: str) -> list[str]:
    """One say invocation per token — each word must finish before the next."""
    t = _clean_for_speech(text)
    if not t:
        return []
    return re.findall(r"[A-Za-z0-9']+", t) or t.split()


def _word_queue_pause_ms_after(
    source_text: str, word_idx: int, word_spans: list[re.Match[str]], cfg: dict[str, Any]
) -> float:
    """Pause after this word from punctuation / grammar in the original script."""
    default = float(cfg.get("gapMs", 28))
    if word_idx >= len(word_spans) - 1:
        return float(cfg.get("endPauseMs", 180))
    end = word_spans[word_idx].end()
    tail = source_text[end : end + 8].lstrip()
    if not tail:
        return default
    if tail.startswith("..."):
        return float(cfg.get("ellipsisPauseMs", 420))
    ch = tail[0]
    pauses = {
        ".": float(cfg.get("periodPauseMs", 380)),
        "!": float(cfg.get("exclamationPauseMs", 400)),
        "?": float(cfg.get("questionPauseMs", 400)),
        ",": float(cfg.get("commaPauseMs", 160)),
        ";": float(cfg.get("semicolonPauseMs", 260)),
        ":": float(cfg.get("colonPauseMs", 240)),
        "—": float(cfg.get("dashPauseMs", 300)),
        "-": float(cfg.get("dashPauseMs", 280)),
    }
    return pauses.get(ch, default)


def should_pair_words_for_capture(w1: str, w2: str, cfg: dict[str, Any]) -> bool:
    """Pair 2 words only when one-word capture is likely to fail (mysay hang / clip)."""
    if cfg.get("pairRiskyWords", True) is False:
        return False
    a, b = w1.strip(), w2.strip()
    if not a or not b:
        return False
    tiny = {str(x).lower() for x in (cfg.get("pairTinyWords") or ["i", "a", "oh", "ah", "ok", "no", "so"])}
    if len(a) <= 2 or len(b) <= 2:
        return True
    if a.lower() in tiny or b.lower() in tiny:
        return True
    if a.isupper() and len(a) <= 5:
        return True
    return False


def _pause_ms_for_trailing_punct(ch: str, cfg: dict[str, Any], *, is_final: bool) -> float:
    """Human-like gap after a captured phrase from its ending punctuation."""
    if is_final:
        return float(cfg.get("endPauseMs", 220))
    pauses = {
        ".": float(cfg.get("periodPauseMs", 310)),
        "!": float(cfg.get("exclamationPauseMs", 330)),
        "?": float(cfg.get("questionPauseMs", 330)),
        "…": float(cfg.get("ellipsisPauseMs", 380)),
        ",": float(cfg.get("commaPauseMs", 140)),
        ";": float(cfg.get("semicolonPauseMs", 220)),
        ":": float(cfg.get("colonPauseMs", 200)),
        "—": float(cfg.get("dashPauseMs", 210)),
        "-": float(cfg.get("dashPauseMs", 200)),
    }
    return pauses.get(ch, float(cfg.get("gapMs", 22)))


def _trailing_punct_char(fragment: str) -> str:
    m = re.search(r"([.!?…,;:\"'—\-]+)\s*$", fragment.strip())
    if not m:
        return ""
    tail = m.group(1)
    for ch in ".!?…,;:":
        if ch in tail:
            return ch
    if "—" in tail:
        return "—"
    if "-" in tail:
        return "-"
    return tail[-1]


def _split_long_phrase_on_commas(phrase: str, max_words: int) -> list[str]:
    words = tokenize_words_for_queue(phrase)
    if len(words) <= max_words:
        return [phrase.strip()] if phrase.strip() else []
    parts: list[str] = []
    chunk: list[str] = []
    # Split on commas in the source while preserving punctuation in fragments
    for segment in re.split(r"(\s*,\s*)", phrase):
        if not segment.strip():
            continue
        if re.match(r"^\s*,\s*$", segment):
            if chunk:
                chunk[-1] = chunk[-1] + ","
            continue
        candidate = (" ".join(chunk) + " " + segment).strip() if chunk else segment.strip()
        w = len(tokenize_words_for_queue(candidate))
        if w > max_words and chunk:
            parts.append(" ".join(chunk).strip())
            chunk = [segment.strip()]
        else:
            chunk.append(segment.strip())
    if chunk:
        parts.append(" ".join(chunk).strip())
    return [p for p in parts if p]


def _split_phrases_by_punctuation(source: str, cfg: dict[str, Any]) -> list[str]:
    """Break script into speakable phrases (sentences/clauses) from punctuation."""
    text = strip_breath_markers_for_capture(source).strip()
    if not text:
        return []
    max_words = int(cfg.get("maxWordsPerUnit", 22))
    min_words = int(cfg.get("minWordsBeforeCommaSplit", 14))
    split_dash = cfg.get("splitOnDash", True) is not False

    # Strong sentence endings, then em-dash clauses
    chunks: list[str] = []
    min_dash_words = int(cfg.get("minWordsBeforeDashSplit", 4))
    for block in re.split(r"(?<=[.!?…])\s+", text):
        block = block.strip()
        if not block:
            continue
        if split_dash and "—" in block:
            raw_parts = [p.strip() for p in re.split(r"\s*—\s*", block) if p.strip()]
            subblocks: list[str] = []
            buf = raw_parts[0] if raw_parts else ""
            max_right = int(cfg.get("maxWordsDashRightKeepTogether", 8))
            for part in raw_parts[1:]:
                left_w = len(tokenize_words_for_queue(buf))
                right_w = len(tokenize_words_for_queue(part))
                if left_w < min_dash_words or right_w <= max_right:
                    buf = f"{buf}— {part}"
                else:
                    if buf:
                        subblocks.append(buf)
                    buf = part
            if buf:
                subblocks.append(buf)
            if not subblocks:
                subblocks = [block]
        else:
            subblocks = [block]
        for sub in subblocks:
            wcount = len(tokenize_words_for_queue(sub))
            if wcount > max(min_words, max_words) and cfg.get("splitOnComma", True):
                chunks.extend(_split_long_phrase_on_commas(sub, max_words))
            else:
                chunks.append(sub)
    return chunks


def build_sentence_queue_units(text: str, spec: dict[str, Any] | None) -> list[dict[str, Any]]:
    """
    One mysay capture per punctuation phrase (sentence / clause).
    Pauses follow the punctuation that ends each phrase.
    """
    cfg = word_queue_cfg(spec)
    source = expand_breath_performance_markers(text)
    phrases = _split_phrases_by_punctuation(source, cfg)
    units: list[dict[str, Any]] = []
    for i, phrase in enumerate(phrases):
        capture = _clean_phrase_for_capture(phrase)
        if not capture:
            continue
        trail = _trailing_punct_char(phrase)
        pause = _pause_ms_for_trailing_punct(
            trail, cfg, is_final=(i >= len(phrases) - 1)
        )
        n_words = len(tokenize_words_for_queue(capture))
        units.append(
            {
                "capture": capture,
                "pause_after_ms": pause,
                "n_words": n_words,
                "unit_kind": "sentence",
                "paired": False,
            }
        )
    return units


def build_word_queue_units(text: str, spec: dict[str, Any] | None) -> list[dict[str, Any]]:
    """
    Word-by-word capture plan; merge to 2-word units only when risky.
    Each unit: {capture, pause_after_ms, n_words}.
    """
    cfg = word_queue_cfg(spec)
    source = expand_breath_performance_markers(text)
    words = tokenize_words_for_queue(source)
    if not words:
        return []
    spans = list(re.finditer(r"[A-Za-z0-9']+", source))
    units: list[dict[str, Any]] = []
    i = 0
    while i < len(words):
        if i + 1 < len(words) and should_pair_words_for_capture(words[i], words[i + 1], cfg):
            capture = f"{words[i]} {words[i + 1]}"
            pause = _word_queue_pause_ms_after(source, i + 1, spans, cfg)
            units.append(
                {
                    "capture": capture,
                    "pause_after_ms": pause,
                    "n_words": 2,
                    "paired": True,
                    "unit_kind": "pair",
                }
            )
            i += 2
        else:
            capture = words[i]
            pause = _word_queue_pause_ms_after(source, i, spans, cfg)
            units.append(
                {
                    "capture": capture,
                    "pause_after_ms": pause,
                    "n_words": 1,
                    "paired": False,
                    "unit_kind": "word",
                }
            )
            i += 1
    return units


def capture_queue_mode(spec: dict[str, Any] | None) -> str:
    cfg = word_queue_cfg(spec)
    mode = str(cfg.get("captureMode", "sentence")).lower().strip()
    if mode in ("word", "words", "perword", "per_word"):
        return "word"
    return "sentence"


def build_capture_queue_units(text: str, spec: dict[str, Any] | None) -> list[dict[str, Any]]:
    if capture_queue_mode(spec) == "word":
        return build_word_queue_units(text, spec)
    return build_sentence_queue_units(text, spec)


def word_queue_capture_units(text: str, spec: dict[str, Any] | None) -> tuple[list[str], str]:
    """Legacy helper — texts only."""
    units = build_capture_queue_units(text, spec)
    labels = []
    for u in units:
        if u.get("paired"):
            labels.append("pair")
        else:
            labels.append("word")
    kind = "pairs" if all(x == "pair" for x in labels) and labels else "words"
    if any(u.get("paired") for u in units):
        kind = "words+pairs"
    return [str(u["capture"]) for u in units], kind


def word_queue_watchdog_enabled(spec: dict[str, Any] | None) -> bool:
    cfg = word_queue_cfg(spec)
    if cfg.get("watchdog") is False:
        return False
    return bool(cfg.get("watchdog", True))


def word_queue_mysay_timeout_sec(spec: dict[str, Any] | None, text: str = "") -> int:
    """Per-unit timeouts — short for words, longer for full sentences."""
    cfg = word_queue_cfg(spec)
    w = (text or "").strip()
    n = len(w)
    sentence_mode = capture_queue_mode(spec) == "sentence"
    base = int(cfg.get("mysayTimeoutSec", 45 if sentence_mode else 20))
    per_char = float(cfg.get("mysayTimeoutPerCharSec", 0.55 if sentence_mode else 0.35))
    floor = int(cfg.get("mysayTimeoutMinSec", 18 if sentence_mode else 10))
    cap = int(cfg.get("mysayTimeoutMaxSec", 120 if sentence_mode else 42))
    if not sentence_mode and n <= 2:
        return min(cap, max(floor, int(cfg.get("mysayTimeoutTinySec", 14))))
    if not sentence_mode and n <= 8:
        est = base + int(n * per_char)
        return max(floor, min(cap, est))
    est = base + int(n * per_char) + (6 if sentence_mode else 4)
    return max(floor, min(cap, est))


def word_queue_est_seconds(spec: dict[str, Any] | None, unit_count: int) -> int:
    cfg = word_queue_cfg(spec)
    per = float(cfg.get("estSecPerUnit", cfg.get("estSecPerWord", 14)))
    return max(30, int(unit_count * per))


def _ms_to_samples(ms: float, sr: int) -> int:
    return max(0, int(round(sr * float(ms) / 1000.0)))


def stretch_word_pronunciation(mono: "np.ndarray", stretch_ms: float, sr: int) -> "np.ndarray":
    """Hold/extend the tail slightly so each word finishes without adding a audible gap."""
    import numpy as np

    extra = _ms_to_samples(stretch_ms, sr)
    if extra <= 0 or mono.size < 8:
        return mono
    if extra == 1:
        return np.pad(mono.astype(np.float32), (0, 1), mode="edge")
    target = mono.size + extra
    x_old = np.linspace(0.0, 1.0, mono.size, dtype=np.float64)
    x_new = np.linspace(0.0, 1.0, target, dtype=np.float64)
    return np.interp(x_new, x_old, mono.astype(np.float64)).astype(np.float32)


def _fade_word_tail(mono: "np.ndarray", sr: int, fade_ms: float) -> "np.ndarray":
    """Gentle release on each word clip so joins don't sound chopped."""
    import numpy as np

    n = _ms_to_samples(fade_ms, sr)
    if n < 4 or mono.size < n + 8:
        return mono.astype(np.float32)
    out = mono.astype(np.float32).copy()
    ramp = np.linspace(1.0, 0.92, n, dtype=np.float32)
    out[-n:] *= ramp
    return out


def _phrase_envelope_ramp(n: int, *, floor: float = 0.0) -> "np.ndarray":
    """Cosine ease — smooth down/up between phrases."""
    import numpy as np

    if n < 2:
        return np.ones(max(n, 1), dtype=np.float32)
    t = np.linspace(0.0, 1.0, n, dtype=np.float64)
    curve = 0.5 - 0.5 * np.cos(t * np.pi)
    return (floor + (1.0 - floor) * curve).astype(np.float32)


def _phrase_tail_release(
    mono: "np.ndarray", sr: int, release_ms: float, *, floor: float = 0.06
) -> "np.ndarray":
    """Lower voice at phrase end so the next section doesn't pop in."""
    import numpy as np

    n = _ms_to_samples(release_ms, sr)
    if n < 6 or mono.size < n + 12:
        return mono.astype(np.float32)
    out = mono.astype(np.float32).copy()
    ramp = _phrase_envelope_ramp(n, floor=floor)[::-1]
    out[-n:] *= ramp
    return out


def _phrase_head_fade_in(
    mono: "np.ndarray", sr: int, attack_ms: float, *, ceiling: float = 1.0
) -> "np.ndarray":
    """Ease into the next phrase from a low level — no hard pickup."""
    import numpy as np

    n = _ms_to_samples(attack_ms, sr)
    if n < 6 or mono.size < n + 12:
        return mono.astype(np.float32)
    out = mono.astype(np.float32).copy()
    ramp = _phrase_envelope_ramp(n, floor=0.0) * float(ceiling)
    out[:n] *= ramp
    return out


def _remove_dc_offset(mono: "np.ndarray") -> "np.ndarray":
    import numpy as np

    if mono.size < 8:
        return mono.astype(np.float32)
    return (mono.astype(np.float32) - float(np.mean(mono)))


def _prep_clip_for_join(mono: "np.ndarray", sr: int, edge_ms: float = 2.5) -> "np.ndarray":
    """DC offset + micro edge taper — prevents join clicks/crackle."""
    import numpy as np

    out = _remove_dc_offset(mono)
    n = _ms_to_samples(edge_ms, sr)
    if n < 3 or out.size < n * 4:
        return out
    ramp = _phrase_envelope_ramp(n, floor=0.88)
    out = out.copy()
    out[:n] *= ramp
    out[-n:] *= ramp[::-1]
    return out


def _extend_phrase_tail_fade(mono: "np.ndarray", sr: int, fade_ms: float) -> "np.ndarray":
    """Cosine fade on tail — finishes phrase without a flat DC hold (which clicks)."""
    import numpy as np

    n = _ms_to_samples(fade_ms, sr)
    if n < 4 or mono.size < n + 8:
        return mono.astype(np.float32)
    out = mono.astype(np.float32).copy()
    ramp = _phrase_envelope_ramp(n, floor=0.0)[::-1]
    out[-n:] *= ramp
    return out


def _soft_limit_mono(mono: "np.ndarray", ceiling: float = 0.97) -> "np.ndarray":
    """Soft knee peak control — avoids hard clip crackle on normalize."""
    import numpy as np

    peak = float(np.max(np.abs(mono)) or 1e-6)
    if peak <= ceiling:
        return mono.astype(np.float32)
    x = mono.astype(np.float64) / peak
    return (np.tanh(x * 1.15) * ceiling).astype(np.float32)


def _crossfade_append_chunk(
    prev: np.ndarray,
    nxt: np.ndarray,
    sr: int,
    gap_ms: float,
    crossfade_ms: float,
    *,
    phrase_boundary: bool = False,
    boundary_dip: float = 0.06,
    release_ms: float = 0.0,
    attack_ms: float = 0.0,
) -> np.ndarray:
    """Blend phrase tails/heads; optional dip before the next section starts."""
    import numpy as np

    prev = prev.astype(np.float32)
    nxt = nxt.astype(np.float32)
    if phrase_boundary and release_ms > 0:
        prev = _phrase_tail_release(prev, sr, release_ms, floor=boundary_dip)
    if phrase_boundary and attack_ms > 0:
        nxt = _phrase_head_fade_in(nxt, sr, attack_ms)

    gap_n = _ms_to_samples(gap_ms, sr)
    xf_n = max(0, _ms_to_samples(crossfade_ms, sr))
    if prev.size < 8:
        return np.concatenate([nxt, np.zeros(gap_n, dtype=np.float32)])
    if xf_n < 4 or nxt.size < 4:
        parts = [prev]
        if gap_n:
            parts.append(np.zeros(gap_n, dtype=np.float32))
        parts.append(nxt)
        return np.concatenate(parts)
    o = min(xf_n, prev.size // 2, nxt.size // 2)
    if phrase_boundary:
        fade_out = np.sqrt(np.linspace(1.0, 0.0, o, dtype=np.float32))
        fade_in = np.sqrt(np.linspace(0.0, 1.0, o, dtype=np.float32))
        blend = prev[-o:] * fade_out + nxt[:o] * fade_in
        effective_gap = max(0, gap_n - o // 3)
        return np.concatenate([prev[:-o], blend, np.zeros(effective_gap, dtype=np.float32), nxt[o:]])
    fade = np.linspace(1.0, 0.0, o, dtype=np.float32)
    blend = prev[-o:] * fade + nxt[:o] * (1.0 - fade)
    return np.concatenate([prev[:-o], blend, np.zeros(gap_n, dtype=np.float32), nxt[o:]])


def concat_word_queue_wavs(
    part_paths: list[Path],
    out: Path,
    *,
    gap_ms: float = 28.0,
    pauses_ms: list[float] | None = None,
    pronunciation_stretch_ms: float = 0.5,
    tail_pad_ms: float = 0.0,
    crossfade_ms: float = 12.0,
    word_tail_fade_ms: float = 0.0,
    phrase_boundary: bool = False,
    phrase_release_ms: float = 0.0,
    phrase_attack_ms: float = 0.0,
    boundary_dip: float = 0.06,
    tail_hold_ms: float = 0.0,
    phrase_tail_fade_ms: float = 0.0,
    boundary_pause_factor: float = 1.0,
    join_declick: bool = False,
    join_declick_cfg: dict[str, Any] | None = None,
    sr: int = 48000,
) -> bool:
    """Join clips with grammar pauses + crossfade — fluid, not batch-robotic."""
    if not part_paths:
        return False
    try:
        import numpy as np
        import soundfile as sf
    except ImportError:
        print("wordQueue concat needs numpy + soundfile", file=sys.stderr)
        return False

    tail_n = _ms_to_samples(tail_pad_ms, sr)
    merged: np.ndarray | None = None
    for i, p in enumerate(part_paths):
        y, file_sr = sf.read(str(p), dtype="float32", always_2d=True)
        if file_sr != sr:
            import librosa

            y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
        mono = y.mean(axis=1) if y.ndim > 1 else y
        mono = _prep_clip_for_join(mono, sr)
        if pronunciation_stretch_ms > 0:
            mono = stretch_word_pronunciation(mono, pronunciation_stretch_ms, sr)
        if phrase_tail_fade_ms > 0:
            mono = _extend_phrase_tail_fade(mono, sr, phrase_tail_fade_ms)
        elif tail_hold_ms > 0:
            mono = _extend_phrase_tail_fade(mono, sr, min(tail_hold_ms, 16.0))
        if word_tail_fade_ms > 0 and not phrase_boundary:
            mono = _fade_word_tail(mono, sr, word_tail_fade_ms)
        if tail_n:
            mono = np.pad(mono, (0, tail_n), mode="constant")
        mono = mono.astype(np.float32)
        if merged is None:
            if phrase_boundary and phrase_attack_ms > 0:
                mono = _phrase_head_fade_in(mono, sr, phrase_attack_ms)
            merged = mono
            continue
        pause = float(pauses_ms[i - 1]) if pauses_ms and i - 1 < len(pauses_ms) else gap_ms
        if phrase_boundary and boundary_pause_factor < 1.0:
            pause = max(12.0, pause * boundary_pause_factor)
        merged = _crossfade_append_chunk(
            merged,
            mono,
            sr,
            pause,
            crossfade_ms,
            phrase_boundary=phrase_boundary,
            boundary_dip=boundary_dip,
            release_ms=phrase_release_ms,
            attack_ms=phrase_attack_ms,
        )
    if merged is None:
        return False
    merged = _soft_limit_mono(merged.astype(np.float32), 0.97)
    stereo = np.stack([merged, merged], axis=1)
    out.parent.mkdir(parents=True, exist_ok=True)
    sf.write(str(out), stereo, sr, subtype="PCM_16")
    if join_declick and out.is_file():
        try:
            helpers_path = _TOOLS_DIR / "personal-voice-helpers.py"
            import importlib.util

            sp = importlib.util.spec_from_file_location("pvh_join", helpers_path)
            pvh = importlib.util.module_from_spec(sp)
            assert sp.loader
            sp.loader.exec_module(pvh)
            mini = {"voiceHelpers": {"deClick": join_declick_cfg or {}}}
            pvh.apply_de_click(out, mini)
            pvh.apply_de_click(out, mini)
        except Exception as exc:
            print(f"join deClick: {exc}", file=sys.stderr)
    return out.is_file() and out.stat().st_size > 512


def emphasis_moments_cfg(spec: dict[str, Any] | None) -> list[dict[str, Any]]:
    spec = flatten_narrator_personal_settings(spec or {})
    raw = spec.get("narrationEmphasisMoments")
    if not isinstance(raw, list):
        vh = spec.get("voiceHelpers") if isinstance(spec.get("voiceHelpers"), dict) else {}
        em = vh.get("emphasisMoments")
        raw = em if isinstance(em, list) else []
    out: list[dict[str, Any]] = []
    for item in raw:
        if isinstance(item, dict) and str(item.get("phrase", "")).strip():
            out.append(item)
    return out


def emphasis_capture_enabled(text: str, spec: dict[str, Any] | None) -> bool:
    if spec and spec.get("narrationEmphasisCapture") is False:
        return False
    if word_queue_enabled(spec) and spec.get("narrationEmphasisWithWordQueue", False) is not True:
        return False
    moments = emphasis_moments_cfg(spec)
    if not moments:
        return False
    low = _clean_for_speech(text).lower()
    return any(str(m.get("phrase", "")).lower() in low for m in moments)


def split_text_emphasis_segments(text: str, moments: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Split narration into normal vs excited segments (phrase must appear in order)."""
    t = _clean_for_speech(text)
    if not t:
        return []
    ordered: list[tuple[int, dict[str, Any]]] = []
    for m in moments:
        phrase = _clean_for_speech(str(m.get("phrase", "")))
        if not phrase:
            continue
        idx = t.lower().find(phrase.lower())
        if idx >= 0:
            ordered.append((idx, {**m, "phrase": phrase}))
    if not ordered:
        return [{"text": t, "excited": False}]
    ordered.sort(key=lambda x: x[0])
    segments: list[dict[str, Any]] = []
    pos = 0
    for idx, m in ordered:
        phrase = str(m["phrase"])
        if idx > pos:
            chunk = t[pos:idx].strip()
            if chunk:
                segments.append({"text": chunk, "excited": False})
        segments.append(
            {
                "text": phrase,
                "excited": True,
                "sayRateBoost": int(m.get("sayRateBoost", 14)),
                "pitchUpSemitones": float(m.get("pitchUpSemitones", 0.42)),
                "fallSemitones": float(m.get("fallSemitones", -0.28)),
                "gainDb": float(m.get("gainDb", 1.6)),
            }
        )
        pos = idx + len(phrase)
    if pos < len(t):
        tail = t[pos:].strip()
        if tail:
            segments.append({"text": tail, "excited": False})
    return segments


def shape_excited_segment_audio(
    mono: np.ndarray,
    sr: int,
    *,
    pitch_up: float,
    fall: float,
    gain_db: float,
) -> np.ndarray:
    """Raise energy mid-phrase, then let pitch fall like natural speech."""
    import numpy as np

    y = mono.astype(np.float32)
    n = y.size
    if n < int(sr * 0.12):
        return y
    t = np.linspace(0.0, 1.0, n, dtype=np.float64)
    rise = np.clip(t / 0.38, 0.0, 1.0)
    drop = np.where(t > 0.58, (t - 0.58) / 0.42, 0.0)
    env = (0.92 + 0.14 * np.sin(np.pi * rise)) * (1.0 - 0.2 * drop)
    y = y * env.astype(np.float32) * float(10 ** (gain_db / 20.0))
    split = int(n * 0.52)
    head, tail = y[:split], y[split:]
    try:
        import pyrubberband as pyrb

        head = pyrb.pitch_shift(head, sr, pitch_up).astype(np.float32)
        tail = pyrb.pitch_shift(tail, sr, fall).astype(np.float32)
    except Exception:
        try:
            import librosa

            head = librosa.effects.pitch_shift(head, sr=sr, n_steps=pitch_up)
            tail = librosa.effects.pitch_shift(tail, sr=sr, n_steps=fall)
        except Exception:
            pass
    out = np.concatenate([head, tail])
    peak = float(np.max(np.abs(out)) or 1e-6)
    return (out / peak * min(peak, 0.97)).astype(np.float32)


def concat_capture_segments(
    part_paths: list[Path],
    out: Path,
    *,
    gap_ms: float = 14.0,
    sr: int = 48000,
) -> bool:
    if not part_paths:
        return False
    try:
        import soundfile as sf
    except ImportError:
        return False
    gap_n = _ms_to_samples(gap_ms, sr)
    chunks: list[np.ndarray] = []
    for p in part_paths:
        y, file_sr = sf.read(str(p), dtype="float32", always_2d=True)
        if file_sr != sr:
            import librosa

            y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
        chunks.append((y.mean(axis=1) if y.ndim > 1 else y).astype(np.float32))
    merged_parts: list[np.ndarray] = []
    for i, c in enumerate(chunks):
        merged_parts.append(c)
        if i < len(chunks) - 1 and gap_n:
            merged_parts.append(np.zeros(gap_n, dtype=np.float32))
    merged = np.concatenate(merged_parts)
    peak = float(np.max(np.abs(merged)) or 1e-6)
    merged = merged / peak * min(peak, 0.98)
    stereo = np.stack([merged, merged], axis=1)
    out.parent.mkdir(parents=True, exist_ok=True)
    sf.write(str(out), stereo, sr, subtype="PCM_16")
    return out.is_file() and out.stat().st_size > 512


def synthesize_capture_segment(
    segment_text: str,
    part: Path,
    voice_name: str,
    *,
    rate: int,
    spec: dict[str, Any],
    excited: bool = False,
    say_rate_boost: int = 0,
) -> bool:
    seg_rate = rate + (say_rate_boost if excited else 0)
    prefer_mysay = bool(spec.get("narratorPreferMysay", True)) and _MYSAY_DYLIB.is_file()
    plain = prepare_say_capture_text(segment_text, spec)
    if prefer_mysay:
        try:
            if personal_voice_mysay_to_wav(
                plain,
                part,
                voice_name,
                rate=seg_rate,
                prefer_plain=True,
                spec=spec,
            ):
                return True
        except Exception:
            pass
    if strict_personal_narration(spec):
        return False
    return say_to_wav(plain, part, voice=voice_name, rate=seg_rate)


def personal_voice_emphasis_capture_wav(
    text: str,
    wav: Path,
    voice_name: str,
    *,
    rate: int,
    spec: dict[str, Any] | None = None,
) -> bool:
    """Capture excited phrases faster/brighter; normal phrases at base rate."""
    spec = flatten_narrator_personal_settings(spec or {})
    moments = emphasis_moments_cfg(spec)
    segments = split_text_emphasis_segments(text, moments)
    if len(segments) < 2 and not any(s.get("excited") for s in segments):
        return False

    work = wav.parent / "_emphasis_capture"
    work.mkdir(parents=True, exist_ok=True)
    part_paths: list[Path] = []
    sr = 48000

    print(
        f"Personal Voice: emphasis capture ({len(segments)} segments)…",
        flush=True,
    )
    for i, seg in enumerate(segments):
        st = str(seg.get("text", "")).strip()
        if not st:
            continue
        part = work / f"seg_{i:03d}.wav"
        excited = bool(seg.get("excited"))
        ok = synthesize_capture_segment(
            st,
            part,
            voice_name,
            rate=rate,
            spec=spec,
            excited=excited,
            say_rate_boost=int(seg.get("sayRateBoost", 14)),
        )
        if not ok or not wav_is_audible(part, min_sec=0.08):
            print(f"  emphasis segment skip: {st[:48]!r}…", file=sys.stderr)
            continue
        if excited:
            try:
                import soundfile as sf

                y, file_sr = sf.read(str(part), dtype="float32", always_2d=True)
                if file_sr != sr:
                    import librosa

                    y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
                mono = shape_excited_segment_audio(
                    y.mean(axis=1) if y.ndim > 1 else y,
                    sr,
                    pitch_up=float(seg.get("pitchUpSemitones", 0.42)),
                    fall=float(seg.get("fallSemitones", -0.28)),
                    gain_db=float(seg.get("gainDb", 1.6)),
                )
                sf.write(str(part), np.stack([mono, mono], axis=1), sr, subtype="PCM_16")
            except Exception as exc:
                print(f"  emphasis shape: {exc}", file=sys.stderr)
        part_paths.append(part)
        tag = "EXCITED" if excited else "normal"
        print(f"  segment {len(part_paths)} [{tag}]: {st[:56]!r}", flush=True)

    if not part_paths:
        return False
    gap = float(
        (spec.get("voiceHelpers") or {}).get("emphasisMoments", {}).get("segmentGapMs", 14)
        if isinstance(spec.get("voiceHelpers"), dict)
        else 14
    )
    return concat_capture_segments(part_paths, wav, gap_ms=gap)


def synthesize_single_word_wav(
    word: str,
    out: Path,
    voice_name: str,
    *,
    rate: int,
    spec: dict[str, Any],
    tail_pad_ms: float = 75.0,
) -> bool:
    work = out.parent / "_final_word"
    work.mkdir(parents=True, exist_ok=True)
    part = work / "last.wav"
    if not synthesize_capture_segment(word, part, voice_name, rate=rate, spec=spec):
        return False
    if not wav_is_audible(part, min_sec=0.04):
        return False
    return concat_word_queue_wavs(
        [part],
        out,
        gap_ms=0.0,
        pronunciation_stretch_ms=0.0,
        tail_pad_ms=float(tail_pad_ms),
    )


def finish_narration_tail(
    wav: Path,
    text: str,
    voice_name: str,
    *,
    rate: int,
    spec: dict[str, Any] | None = None,
) -> None:
    """Force last word to complete; pad tail so nothing gets clipped."""
    spec = flatten_narrator_personal_settings(spec or {})
    vh = spec.get("voiceHelpers") if isinstance(spec.get("voiceHelpers"), dict) else {}
    cfg = vh.get("tailFinish") if isinstance(vh.get("tailFinish"), dict) else {}
    if cfg.get("enabled") is False:
        return

    helpers_path = _TOOLS_DIR / "personal-voice-helpers.py"
    if helpers_path.is_file():
        import importlib.util

        sp = importlib.util.spec_from_file_location("pvh_tail", helpers_path)
        pvh = importlib.util.module_from_spec(sp)
        assert sp.loader
        sp.loader.exec_module(pvh)
        if not spec.get("narratorBreathCueInserted") and pvh.breath_cue_enabled(spec):
            pvh.insert_breath_performance_cue(wav, text, spec)
        pvh.apply_tail_finish(wav, spec)

    if not cfg.get("forceLastWordQueue", True):
        return

    tokens = tokenize_words_for_queue(text)
    if not tokens:
        return
    last = tokens[-1].strip()
    if not last:
        return

    try:
        import soundfile as sf
    except ImportError:
        return

    sr = 48000
    y, file_sr = sf.read(str(wav), dtype="float32", always_2d=True)
    if file_sr != sr:
        import librosa

        y = librosa.resample(y.T, orig_sr=file_sr, target_sr=sr, axis=1).T
    mono = (y.mean(axis=1) if y.ndim > 1 else y).astype(np.float32)

    try:
        import librosa

        intervals = librosa.effects.split(mono, top_db=float(cfg.get("splitTopDb", 30)))
    except Exception:
        intervals = np.zeros((0, 2), dtype=int)

    if len(intervals) > 0:
        last_start = int(intervals[-1][0])
        back_ms = float(cfg.get("replaceBackMs", 180))
        keep_end = max(0, last_start - _ms_to_samples(back_ms, sr))
        head = mono[:keep_end]
    else:
        trim_ms = float(cfg.get("replaceBackMs", 220))
        keep_end = max(0, mono.size - _ms_to_samples(trim_ms, sr))
        head = mono[:keep_end]

    tail_wav = wav.parent / "_final_word" / "last_word.wav"
    tail_pad = float(cfg.get("lastWordTailPadMs", 80))
    last_rate = int(cfg.get("lastWordSayRate", rate))
    if not synthesize_single_word_wav(
        last, tail_wav, voice_name, rate=last_rate, spec=spec, tail_pad_ms=tail_pad
    ):
        return

    tail_y, t_sr = sf.read(str(tail_wav), dtype="float32", always_2d=True)
    if t_sr != sr:
        import librosa

        tail_y = librosa.resample(tail_y.T, orig_sr=t_sr, target_sr=sr, axis=1).T
    tail = (tail_y.mean(axis=1) if tail_y.ndim > 1 else tail_y).astype(np.float32)
    xf_ms = float(cfg.get("lastWordCrossfadeMs", 18))
    xf_n = _ms_to_samples(xf_ms, sr)
    if head.size >= xf_n + 8 and tail.size >= xf_n + 8:
        o = min(xf_n, head.size // 2, tail.size // 2)
        fade = np.linspace(1.0, 0.0, o, dtype=np.float32)
        blend = head[-o:] * fade + tail[:o] * (1.0 - fade)
        merged = np.concatenate([head[:-o], blend, tail[o:]])
    else:
        gap_n = _ms_to_samples(float(cfg.get("lastWordGapMs", 2)), sr)
        parts = [head]
        if gap_n:
            parts.append(np.zeros(gap_n, dtype=np.float32))
        parts.append(tail)
        merged = np.concatenate(parts)
    peak = float(np.max(np.abs(merged)) or 1e-6)
    merged = merged / peak * min(peak, 0.98)
    sf.write(str(wav), np.stack([merged, merged], axis=1), sr, subtype="PCM_16")
    print(f"  tailFinish: re-captured last word {last!r} (full ending)", flush=True)


def pronunciation_say_attempts(capture_text: str, spec: dict[str, Any] | None) -> list[str]:
    """Multiple say strings to try — checks pick the most natural take."""
    cfg = word_queue_cfg(spec or {})
    say_as = cfg.get("sayAs") if isinstance(cfg.get("sayAs"), dict) else {}
    raw = capture_text.strip()
    key = raw.lower().rstrip(".,!?")
    attempts: list[str] = []
    seen: set[str] = set()

    def add(t: str) -> None:
        t = t.strip()
        if t and t not in seen:
            seen.add(t)
            attempts.append(t)

    add(say_capture_phrase_for_word(raw))
    if key in say_as:
        add(str(say_as[key]))
    if raw.isupper() and len(raw) > 1:
        add(raw.capitalize())
    if key in ("dad", "hurray", "hurry"):
        add("DAD!" if key == "dad" else "HURRY!")
    if " " not in raw and len(raw) <= 4 and not raw.endswith(","):
        add(f"{raw},")
    return attempts


def validate_pronunciation_capture(
    wav_path: Path,
    capture_text: str,
    spec: dict[str, Any] | None,
    *,
    min_sec: float = 0.05,
) -> tuple[bool, str]:
    """QC one clip — reject clipped, silent, or absurdly short/long takes."""
    cfg = word_queue_cfg(spec or {})
    if not wav_is_audible(wav_path, min_sec=min_sec):
        return False, "silent"
    try:
        import soundfile as sf

        y, sr = sf.read(str(wav_path), dtype="float32", always_2d=True)
        mono = y.mean(axis=1) if y.ndim > 1 else y
        dur = mono.size / max(sr, 1)
        letters = max(1, sum(c.isalpha() for c in capture_text))
        min_d = float(cfg.get("minDurPerChar", 0.028)) * letters
        max_d = float(cfg.get("maxDurPerChar", 0.42)) * letters + 0.35
        if dur < min_d:
            return False, f"too_short({dur:.2f}s)"
        if dur > max_d:
            return False, f"too_long({dur:.2f}s)"
        peak = float(np.max(np.abs(mono)))
        if peak > float(cfg.get("maxPeak", 0.985)):
            return False, "clipped"
        rms = float(np.sqrt(np.mean(mono.astype(np.float64) ** 2)))
        if rms < float(cfg.get("minRms", 0.004)):
            return False, "quiet"
        return True, "ok"
    except Exception as exc:
        return False, str(exc)


def capture_unit_with_pronunciation_checks(
    capture_text: str,
    part: Path,
    voice_name: str,
    *,
    word_rate: int,
    spec: dict[str, Any],
    prefer_mysay: bool,
    use_watchdog: bool,
) -> tuple[bool, str]:
    """Try several pronunciations; keep first clip that passes QC."""
    cfg = word_queue_cfg(spec or {})
    min_sec = float(cfg.get("minWordSec", 0.05))
    for attempt in pronunciation_say_attempts(capture_text, spec):
        per_timeout = word_queue_mysay_timeout_sec(spec, attempt)
        wq_spec = {
            **spec,
            "narratorWordQueueCapture": True,
            "narratorMysayWatchdog": use_watchdog,
            "narratorMysayTimeoutSec": per_timeout,
        }
        try:
            part.unlink(missing_ok=True)
        except OSError:
            pass
        captured = False
        if prefer_mysay:
            try:
                captured = personal_voice_mysay_to_wav(
                    attempt,
                    part,
                    voice_name,
                    rate=word_rate,
                    prefer_plain=True,
                    spec=wq_spec,
                )
            except subprocess.TimeoutExpired:
                continue
            except Exception:
                continue
        if not captured and not strict_personal_narration(spec):
            captured = say_to_wav(attempt, part, voice=voice_name, rate=word_rate)
        ok, reason = validate_pronunciation_capture(
            part, capture_text, spec, min_sec=min_sec
        )
        if captured and ok:
            return True, attempt
        if captured:
            print(f"    pron check fail ({reason}): {attempt!r}", file=sys.stderr)
    return False, ""


def personal_voice_word_queue_wav(
    text: str,
    wav: Path,
    voice_name: str,
    *,
    rate: int,
    spec: dict[str, Any] | None = None,
    run_dir: Path | None = None,
) -> bool:
    """
    Punctuation phrase queue (default: sentence/clause) or per-word mode.
    Grammar pauses + crossfade + pronunciation QC.
    """
    cfg = word_queue_cfg(spec or {})
    mode = capture_queue_mode(spec)
    units = build_capture_queue_units(text, spec or {})
    if not units:
        return False

    pronunciation_stretch_ms = float(
        cfg.get("pronunciationStretchMs", cfg.get("stretchMs", 2.5 if mode == "sentence" else 0.5))
    )
    tail_pad_ms = float(cfg.get("tailPadMs", 0.0))
    crossfade_ms = float(cfg.get("crossfadeMs", 22 if mode == "sentence" else 12))
    word_tail_fade_ms = float(cfg.get("wordTailFadeMs", 8 if mode == "sentence" else 0))
    phrase_boundary = mode == "sentence" and cfg.get("phraseBoundarySmooth", True) is not False
    phrase_release_ms = float(cfg.get("phraseReleaseMs", 72 if phrase_boundary else 0))
    phrase_attack_ms = float(cfg.get("phraseAttackMs", 42 if phrase_boundary else 0))
    boundary_dip = float(cfg.get("boundaryDip", 0.07))
    tail_hold_ms = float(cfg.get("tailHoldMs", 0))
    phrase_tail_fade_ms = float(cfg.get("phraseTailFadeMs", 14 if phrase_boundary else 0))
    boundary_pause_factor = float(cfg.get("boundaryPauseFactor", 0.42 if phrase_boundary else 1.0))
    join_declick = bool(cfg.get("joinDeclick", phrase_boundary))
    join_declick_cfg = cfg.get("joinDeclick") if isinstance(cfg.get("joinDeclick"), dict) else {}
    if phrase_boundary and pronunciation_stretch_ms > 0 and cfg.get("allowStretchMs") is not True:
        pronunciation_stretch_ms = 0.0
    work = wav.parent / ("_sentence_queue" if mode == "sentence" else "_word_queue")
    work.mkdir(parents=True, exist_ok=True)

    part_paths: list[Path] = []
    pauses_ms: list[float] = []
    prefer_mysay = bool(spec.get("narratorPreferMysay", True)) and _MYSAY_DYLIB.is_file()
    est_min = max(1, int(round(estimate_word_queue_minutes(len(units), spec))))
    n_pairs = sum(1 for u in units if u.get("paired"))
    rate0 = segment_queue_say_rate(spec, 0)
    label = "sentence queue" if mode == "sentence" else "word queue"
    extra = f", {n_pairs} pairs" if n_pairs else ""
    print(
        f"Personal Voice: {label} ({len(units)} phrases{extra} @ ~{rate0} wpm, "
        f"natural pauses, ~{est_min} min est.)…",
        flush=True,
    )
    t0 = time.monotonic()
    use_watchdog = word_queue_watchdog_enabled(spec)
    flat_spec = flatten_narrator_personal_settings(spec or {})
    flat_spec["narratorCaptureMode"] = mode

    for i, unit in enumerate(units):
        capture = str(unit.get("capture", "")).strip()
        if not capture:
            continue
        unit_rate = segment_queue_say_rate(spec, i)
        part = work / f"s_{i:04d}.wav"
        captured, used = capture_unit_with_pronunciation_checks(
            capture,
            part,
            voice_name,
            word_rate=unit_rate,
            spec=flat_spec,
            prefer_mysay=prefer_mysay,
            use_watchdog=use_watchdog,
        )
        if captured:
            part_paths.append(part)
            pauses_ms.append(float(unit.get("pause_after_ms", cfg.get("gapMs", 22))))
            tag = str(unit.get("unit_kind", "phrase"))
            elapsed = time.monotonic() - t0
            preview = capture if len(capture) <= 72 else capture[:69] + "…"
            print(
                f"  {tag} {len(part_paths)}/{len(units)}: {preview!r} "
                f"(say {used!r}, {unit_rate} wpm, pause {pauses_ms[-1]:.0f}ms, {elapsed:.0f}s)",
                flush=True,
            )
        else:
            print(f"  skip (no good take): {capture[:80]!r}", file=sys.stderr)

    if not part_paths:
        return False

    ok = concat_word_queue_wavs(
        part_paths,
        wav,
        pauses_ms=pauses_ms,
        pronunciation_stretch_ms=pronunciation_stretch_ms,
        tail_pad_ms=tail_pad_ms,
        crossfade_ms=crossfade_ms,
        word_tail_fade_ms=word_tail_fade_ms,
        phrase_boundary=phrase_boundary,
        phrase_release_ms=phrase_release_ms,
        phrase_attack_ms=phrase_attack_ms,
        boundary_dip=boundary_dip,
        tail_hold_ms=tail_hold_ms,
        phrase_tail_fade_ms=phrase_tail_fade_ms,
        boundary_pause_factor=boundary_pause_factor,
        join_declick=join_declick,
        join_declick_cfg=join_declick_cfg if isinstance(join_declick_cfg, dict) else {},
    )
    if ok:
        flat_spec["narratorUsedWordQueue"] = True
        flat_spec["narratorUsedSegmentQueue"] = True
        if isinstance(spec, dict):
            spec["narratorUsedWordQueue"] = True
            spec["narratorUsedSegmentQueue"] = True
            spec["narratorCaptureMode"] = mode
        print(
            f"{label} merged → {wav.name} ({len(part_paths)} phrases, crossfade {crossfade_ms:g}ms)",
            flush=True,
        )
    return ok


def prepare_say_capture_text(text: str, spec: dict[str, Any] | None = None) -> str:
    """Plain text for say+mysay — commas between words when word queue is on."""
    spec = flatten_narrator_personal_settings(spec or {})
    t = strip_breath_markers_for_capture(_clean_for_speech(expand_breath_performance_markers(text)))
    vh = spec.get("voiceHelpers") if isinstance(spec.get("voiceHelpers"), dict) else {}
    preset = str(vh.get("preset", spec.get("narratorPersonalDelivery", ""))).lower()
    want_character = preset in (
        "humancharacter",
        "human",
        "humanplus",
        "documentary",
    ) or bool(vh.get("character"))
    if spec.get("narratorPersonalHumanize") or want_character:
        t = _humanize_personal_light(t)
    return t


def prepare_personal_speech_text(text: str, spec: dict[str, Any] | None = None) -> str:
    """Personal Voice: shorter cadence + micro-pauses for say/AVSpeech."""
    spec = flatten_narrator_personal_settings(spec or {})
    t = humanize_speech_text(text, spec)
    if not spec.get("narratorNaturalPauses", False):
        return t
    t = re.sub(r"\s+—\s+", ". ", t)
    t = re.sub(r"\s+-\s+", ", ", t)
    pause_sent = _cap_pause_ms(spec, "narratorPauseSentMs", 420)
    pause_comma = _cap_pause_ms(spec, "narratorPauseCommaMs", 180)
    pause_period = _cap_pause_ms(spec, "narratorPausePeriodMs", pause_sent)
    pause_q = _cap_pause_ms(spec, "narratorPauseQuestionMs", pause_sent)
    pause_excl = _cap_pause_ms(spec, "narratorPauseExclamationMs", pause_sent)
    pause_colon = _cap_pause_ms(spec, "narratorPauseColonMs", pause_comma)
    pause_semi = _cap_pause_ms(spec, "narratorPauseSemicolonMs", pause_comma)
    pause_paren = _cap_pause_ms(spec, "narratorPauseParenthesesMs", pause_comma)
    pause_brace = _cap_pause_ms(spec, "narratorPauseBracesMs", pause_comma)
    pause_bracket = _cap_pause_ms(spec, "narratorPauseBracketsMs", pause_comma)
    pause_quote = _cap_pause_ms(spec, "narratorPauseQuotesMs", pause_comma)

    def slnc(ms: int) -> str:
        return f" [[slnc {max(0, ms)}]] "

    t = re.sub(r"\.\s+", "." + slnc(pause_period), t)
    t = re.sub(r"\?\s+", "?" + slnc(pause_q), t)
    t = re.sub(r"!\s+", "!" + slnc(pause_excl), t)
    t = re.sub(r",\s+", "," + slnc(pause_comma), t)
    t = re.sub(r";\s+", ";" + slnc(pause_semi), t)
    t = re.sub(r":\s+", ":" + slnc(pause_colon), t)
    t = re.sub(r"\)\s+", ")" + slnc(pause_paren), t)
    t = re.sub(r"\]\s+", "]" + slnc(pause_bracket), t)
    t = re.sub(r"\}\s+", "}" + slnc(pause_brace), t)
    t = re.sub(r'"\s+', '"' + slnc(pause_quote), t)
    t = re.sub(r"'\s+", "'" + slnc(_cap_pause_ms(spec, "narratorPauseApostropheMs", pause_comma)), t)
    return re.sub(r"\s+", " ", t).strip()


def light_personal_voice_wav(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Barely-there polish — keeps your timbre; tames harsh TTS edge only."""
    if not wav.is_file():
        return
    ffmpeg = find_ffmpeg()
    tmp = wav.with_suffix(".light.wav")
    af = ",".join(
        [
            "highpass=f=80",
            "equalizer=f=3200:width_type=o:width=2:g=-1.0",
            "alimiter=limit=0.98:attack=30:release=200",
        ]
    )
    subprocess.run(
        [ffmpeg, "-y", "-i", str(wav), "-af", af, "-ar", "48000", "-ac", "2", str(tmp)],
        check=True,
        capture_output=True,
    )
    tmp.replace(wav)


def naturalize_personal_voice_wav(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Keep Personal Voice human — no denoise/robotic squash; gentle warmth + de-harsh EQ only."""
    if not wav.is_file():
        return
    spec = spec or {}
    if spec.get("narratorRawPersonalVoice", False):
        return
    ffmpeg = find_ffmpeg()
    tmp = wav.with_suffix(".natural.wav")
    parts = ["highpass=f=65", "lowpass=f=11500"]
    if spec.get("narratorPersonalWarmth", True):
        parts.extend(
            [
                "equalizer=f=130:width_type=o:width=1.2:g=1.0",
                "equalizer=f=2400:width_type=o:width=2:g=-2.0",
                "equalizer=f=4200:width_type=o:width=2:g=-1.2",
            ]
        )
    if spec.get("narratorPersonalLightCompress", True):
        parts.append(
            "acompressor=threshold=-22dB:ratio=1.1:attack=45:release=320:makeup=1.04"
        )
    parts.append("alimiter=limit=0.96:attack=25:release=180")
    af = ",".join(parts)
    subprocess.run(
        [ffmpeg, "-y", "-i", str(wav), "-af", af, "-ar", "48000", "-ac", "2", str(tmp)],
        check=True,
        capture_output=True,
    )
    tmp.replace(wav)


def _load_voice_helpers():
    mod_path = Path(__file__).resolve().parent / "personal-voice-helpers.py"
    if not mod_path.is_file():
        return None
    import importlib.util

    sp = importlib.util.spec_from_file_location("pv_helpers", mod_path)
    mod = importlib.util.module_from_spec(sp)
    assert sp.loader
    sp.loader.exec_module(mod)
    return mod


def apply_personal_voice_helpers(wav: Path, spec: dict[str, Any] | None = None) -> list[str]:
    """Run named voice helpers (humanSmooth, deEss, pitchAutotune, …)."""
    spec = flatten_narrator_personal_settings(spec or {})
    mod = _load_voice_helpers()
    if not mod:
        fluent_personal_voice_wav(wav, spec)
        return ["humanSmooth"]
    applied = mod.apply_voice_helpers(wav, spec)
    if applied:
        ladder = mod.resolve_ladder_phases(spec) if hasattr(mod, "resolve_ladder_phases") else None
        if not ladder:
            print(f"Personal Voice helpers: {' → '.join(applied)}", flush=True)
    return applied


def fluent_personal_voice_wav(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Alias — uses voice helper chain (humanSmooth by default)."""
    apply_personal_voice_helpers(wav, spec)


def autotune_personal_voice_wav(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Legacy path — prefer voiceHelpers.preset in ApprovedCards."""
    spec = flatten_narrator_personal_settings(spec or {})
    vh = spec.get("voiceHelpers")
    if isinstance(vh, dict) and (vh.get("preset") or vh.get("chain")):
        apply_personal_voice_helpers(wav, spec)
        return
    mode = str(spec.get("narratorAutotuneMode", "fluent")).lower()
    if mode in ("fluent", "flow"):
        apply_personal_voice_helpers(wav, spec)
        return
    if not spec.get("narratorAutotune", False) and personal_delivery_preset(spec) != "autotune":
        return
    tools = Path(__file__).resolve().parent
    mod_path = tools / "personal-voice-autotune.py"
    if not mod_path.is_file():
        return
    import importlib.util

    at_spec = importlib.util.spec_from_file_location("pv_autotune", mod_path)
    at_mod = importlib.util.module_from_spec(at_spec)
    assert at_spec.loader
    at_spec.loader.exec_module(at_mod)
    nested = spec.get("narratorPersonalSettings") or {}
    if isinstance(nested, dict):
        if nested.get("autotuneStrength") is not None:
            spec["narratorAutotuneStrength"] = float(nested["autotuneStrength"])
        if nested.get("autotuneSmoothMs") is not None:
            spec["narratorAutotuneSmoothMs"] = float(nested["autotuneSmoothMs"])
        if nested.get("autotuneDryMix") is not None:
            spec["narratorAutotuneDryMix"] = float(nested["autotuneDryMix"])
        if nested.get("autotuneMode") is not None:
            spec["narratorAutotuneMode"] = str(nested["autotuneMode"])
        if nested.get("fluentEnvelope") is not None:
            spec["narratorFluentEnvelope"] = float(nested["fluentEnvelope"])
        if nested.get("fluentGapMs") is not None:
            spec["narratorFluentGapMs"] = float(nested["fluentGapMs"])
    if at_mod.autotune_wav_file(wav, spec):
        at_mod.autotune_then_limiter(wav, spec)
        print(
            f"Personal Voice: autotune applied "
            f"(strength={spec.get('narratorAutotuneStrength', 0.30):.2f}, "
            f"dry={spec.get('narratorAutotuneDryMix', 0.58):.0%}, "
            f"mode={spec.get('narratorAutotuneMode', 'smooth')})",
            flush=True,
        )


def deliver_personal_voice_wav(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Post-process Personal Voice via voiceHelpers chain or legacy presets."""
    spec = flatten_narrator_personal_settings(spec or {})
    capture_text = str(spec.get("narratorCaptureText", "") or "").strip()
    source_text = str(
        spec.get("narratorSourceText") or spec.get("narratorTestPhrase") or capture_text
    ).strip()
    voice_name = resolve_personal_voice_name(spec) or str(
        spec.get("narratorPersonalVoice", "Jacob Adkins")
    )
    say_rate = personal_say_rate(spec)
    helpers_path = _TOOLS_DIR / "personal-voice-helpers.py"
    if source_text and helpers_path.is_file():
        import importlib.util

        sp = importlib.util.spec_from_file_location("pvh_breath", helpers_path)
        pvh = importlib.util.module_from_spec(sp)
        assert sp.loader
        sp.loader.exec_module(pvh)
        if pvh.breath_cue_enabled(spec) and narration_has_breath_cue(source_text):
            pvh.insert_breath_performance_cue(wav, source_text, spec)
            spec = {**spec, "narratorBreathCueInserted": True}

    vh = spec.get("voiceHelpers")
    if isinstance(vh, dict) and (vh.get("chain") or vh.get("preset")):
        apply_personal_voice_helpers(wav, spec)
        if capture_text or source_text:
            finish_narration_tail(
                wav,
                source_text or capture_text,
                voice_name,
                rate=say_rate,
                spec=spec,
            )
        return
    preset = personal_delivery_preset(spec)
    if preset == "raw" and not spec.get("narratorAutotune", False):
        return
    if preset in (
        "fluent",
        "human",
        "humanplus",
        "humancharacter",
        "documentary",
        "ladder",
        "ladderdocumentary",
    ):
        apply_personal_voice_helpers(wav, spec)
        if capture_text or source_text:
            finish_narration_tail(
                wav,
                source_text or capture_text,
                voice_name,
                rate=say_rate,
                spec=spec,
            )
        return
    if preset == "autotune" or spec.get("narratorAutotune", False):
        autotune_personal_voice_wav(wav, spec)
        return
    if spec.get("narratorPolishPersonal", False) and preset == "legacy":
        polish_personal_voice_wav_legacy(wav, spec)
        return
    if preset == "light":
        light_personal_voice_wav(wav, spec)
        return
    if preset in ("natural", "broadcast") or spec.get("narratorNaturalDelivery", False):
        naturalize_personal_voice_wav(wav, spec)


def polish_personal_voice_wav_legacy(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Legacy chain (afftdn) — can sound robotic; kept for explicit opt-in."""
    if not wav.is_file():
        return
    ffmpeg = find_ffmpeg()
    tmp = wav.with_suffix(".personal.wav")
    af = ",".join(
        [
            "highpass=f=75",
            "afftdn=nf=-32",
            "acompressor=threshold=-26dB:ratio=1.35:attack=28:release=220:makeup=1.12",
            "equalizer=f=200:width_type=o:width=2:g=0.35",
            "equalizer=f=2800:width_type=o:width=1.5:g=0.45",
            "alimiter=limit=0.92:attack=10:release=100",
        ]
    )
    subprocess.run(
        [ffmpeg, "-y", "-i", str(wav), "-af", af, "-ar", "48000", "-ac", "2", str(tmp)],
        check=True,
        capture_output=True,
    )
    tmp.replace(wav)


def polish_personal_voice_wav(wav: Path, spec: dict[str, Any] | None = None) -> None:
    deliver_personal_voice_wav(wav, spec)


def polish_voice_wav(wav: Path, spec: dict[str, Any] | None = None) -> None:
    """Smoother documentary delivery — warmth, de-ess; optional gentle pace (default ~0.995)."""
    if not wav.is_file():
        return
    spec = spec or {}
    pace = float(spec.get("narratorSpeechPace", 0.995))
    ffmpeg = find_ffmpeg()
    tmp = wav.with_suffix(".polished.wav")
    parts = [
        "highpass=f=60",
        "afftdn=nf=-28",
        "acompressor=threshold=-22dB:ratio=2.2:attack=12:release=180:makeup=1.5",
        "equalizer=f=180:width_type=o:width=2:g=0.8",
        "equalizer=f=2800:width_type=o:width=2:g=1.0",
        "equalizer=f=9000:width_type=o:width=2:g=-1.2",
    ]
    if abs(pace - 1.0) > 0.004:
        parts.append(_atempo_chain(pace))
    parts.append("alimiter=limit=0.9:attack=5:release=80")
    af = ",".join(parts)
    subprocess.run(
        [ffmpeg, "-y", "-i", str(wav), "-af", af, "-ar", "48000", "-ac", "2", str(tmp)],
        check=True,
        capture_output=True,
    )
    tmp.replace(wav)


def _variant_id(spec: dict[str, Any], key: str, default: str) -> str:
    vid = str(spec.get(key, default)).strip().lower()
    approved = APPROVED_VOICE_DIR / "ApprovedCards.json"
    if approved.is_file():
        try:
            import json

            data = flatten_narrator_personal_settings(
                json.loads(approved.read_text(encoding="utf-8"))
            )
            if key == "introVariant":
                vid = str(data.get("introVariant", vid)).strip().lower()
            elif key == "outroVariant":
                vid = str(data.get("outroVariant", vid)).strip().lower()
        except Exception:
            pass
    return vid or default


def narration_voice_guide_lines() -> list[str]:
    """Shared voice direction for Cursor prompts, local scripts, and proofread exports."""
    return [
        "Sound like a real person with curiosity — warm, smooth, and a little playful.",
        "Highly educational: explain what changed in the world and why a builder made that choice.",
        "Frame this as research-and-development in public — tools like this grow the AI era in positive ways.",
        "Speak to adults learning to ship, young adults picking up real skills, and kids who deserve "
        "a fun visual language for creativity (never preachy, never fear-mongering).",
        "Use vivid transitions; no dead air, no monotone card-reading, no corporate buzzwords.",
        "Make viewers excited for the NEXT build — end with genuine energy, not a robot sign-off.",
        "Never mention signature, autograph, fps, timelapse, Environment Kit, Cave Grader, or editor queue jargon.",
    ]


def narration_target_word_count(spec: dict[str, Any], target_sec: float) -> int:
    """Words needed so Personal Voice roughly fills the graded video runtime."""
    spec = flatten_narrator_personal_settings(spec or {})
    wpm = personal_say_rate(spec)
    sec = max(120.0, float(target_sec or 480))
    return max(200, int((sec / 60.0) * wpm * 0.92))


def build_full_narration_prompt_text(
    *,
    build_mode: str,
    target_sec: float,
    say_rate_wpm: int,
    intro_title: str,
    intro_subtitle: str,
    milestones: list[dict[str, Any]],
) -> str:
    """Mirror demo-recap-pipeline.ts buildFullNarrationPrompt — for proofread exports."""
    sec = max(120, int(target_sec))
    wpm = max(140, int(say_rate_wpm))
    target_words = max(200, int((sec / 60.0) * wpm * 0.92))
    outline_rows: list[str] = []
    for i, m in enumerate(milestones):
        kind = "sub-step" if m.get("beatKind") == "subbeat" else "chapter"
        topic = str(m.get("chapter") or m.get("phase") or "build").strip()
        cap = " ".join(
            str(x).strip()
            for x in (m.get("line1"), m.get("line2"), m.get("line3"))
            if x
        ).strip()
        outline_rows.append(
            f"- {kind} {i + 1} · {topic}\n"
            f"  On-screen captions (teach from these — rephrase in your own spoken words):\n"
            f"  {cap or '(no caption)'}"
        )
    outline = "\n".join(outline_rows)
    intro_line = (
        f"Open with a warm, curious welcome (title: {intro_title}"
        + (f" — {intro_subtitle}" if intro_subtitle else "")
        + "). Hook adults, teens, and curious kids — same story, different entry points."
    )
    voice_rules = "\n".join(f"- {line}" for line in narration_voice_guide_lines())
    return f"""You are writing the COMPLETE voiceover script for a Unity world-build documentary video.

The finished video is already cut to ~{sec} seconds. Your script must fill that runtime when read aloud at ~{wpm} wpm (~{target_words} words). Do not write a short script.

Build mode: {build_mode}

STRUCTURE (required):
1. {intro_line} Set expectations — you are walking through a real Unity world build and learning how worlds are made.
2. Body: move beat-by-beat in order. Use each milestone's on-screen captions as your facts — explain what changed, why it matters, and what the viewer should notice. Tie beats to how R&D-style tooling helps people learn and ship faster in the AI era.
3. Close with a substantive thank-you — invite them to follow for the next build (make them WANT the next drop).

Beat guide (captions are ground truth for content; invent natural spoken phrasing):
{outline}

Voice rules:
{voice_rules}
- One continuous script string (spaces between paragraphs)
- Smooth transitions between beats — never dead air thinking
- NO markdown, NO bullet characters in the script
- NO beat numbers, frame counts, or meta like "on screen you will see"

Return ONLY JSON:
{{"script":"...single string..."}}"""


def write_narration_voice_guide(
    run_dir: Path,
    spec: dict[str, Any],
    milestones: list[dict[str, Any]],
    *,
    target_sec: float | None = None,
    script_preview: str = "",
) -> Path:
    """Write NarrationVoiceGuide.md so the creator can proofread prompting before Terminal capture."""
    spec = flatten_narrator_personal_settings(spec or {})
    dur = float(target_sec or spec.get("fullNarrationTargetSec") or spec.get("targetDurationSec") or 480)
    wpm = personal_say_rate(spec)
    prompt = build_full_narration_prompt_text(
        build_mode=str(spec.get("buildMode") or spec.get("recapBuildMode") or "world-build"),
        target_sec=dur,
        say_rate_wpm=wpm,
        intro_title=str(spec.get("introTitle") or "World Build Recap"),
        intro_subtitle=str(spec.get("introSubtitle") or ""),
        milestones=milestones,
    )
    words = len((script_preview or "").split())
    target_words = narration_target_word_count(spec, dur)
    lines = [
        "# Narration voice guide (proofread before Personal Voice capture)",
        "",
        "Edit **ApprovedCards.json** narrator fields or regenerate the script, then re-run Terminal narration.",
        "",
        "## Targets",
        f"- Video runtime: ~{int(dur)}s",
        f"- Say rate: ~{wpm} wpm",
        f"- Target words: ~{target_words}",
        f"- Current script: {words} words"
        + (" ✓" if words >= int(target_words * 0.85) else " — **too short; regenerate or expand**"),
        "",
        "## Voice direction (what Personal Voice should deliver)",
        "",
    ]
    for rule in narration_voice_guide_lines():
        lines.append(f"- {rule}")
    lines.extend(
        [
            "",
            "## Cursor / AI prompt (script generation)",
            "",
            "```",
            prompt,
            "```",
            "",
        ]
    )
    if script_preview.strip():
        lines.extend(
            [
                "## Current script preview",
                "",
                script_preview.strip()[:8000],
                ("…" if len(script_preview) > 8000 else ""),
                "",
            ]
        )
    path = run_dir / "NarrationVoiceGuide.md"
    path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")
    return path


def intro_narration_script(spec: dict[str, Any]) -> str:
    """Short spoken welcome — never read on-card subtitles (fps / pipeline notes)."""
    if spec.get("introNarration"):
        return humanize_speech_text(sanitize_narration_text(str(spec["introNarration"])), spec)[:260]
    intro_id = _variant_id(spec, "introVariant", "c")
    title = _clean_for_speech(str(spec.get("introTitle", "World Build Recap")))
    scripts = {
        "a": (
            f"Hey — welcome to {title}. "
            "We're walking a real Unity world build together, and I want you to see how research-style "
            "tooling makes creativity approachable whether you're shipping games or just learning how worlds work."
        ),
        "b": (
            f"{title} — glad you're here. "
            "What you see is the actual Scene view, and we'll pause on the beats that teach something "
            "useful for builders of every age."
        ),
        "c": (
            f"{title}. "
            "Settle in — this is a creative build recap, not a dry demo. "
            "We'll move at a steady pace and I'll call out what's changing and why it matters."
        ),
    }
    return humanize_speech_text(sanitize_narration_text(scripts.get(intro_id, scripts["c"])), spec)[:260]


def outro_narration_script(spec: dict[str, Any]) -> str:
    if spec.get("outroNarration"):
        return humanize_speech_text(sanitize_narration_text(str(spec["outroNarration"])), spec)[:480]
    outro_id = _variant_id(spec, "outroVariant", "b")
    scripts = {
        "a": (
            "Thanks for riding this build with me — from first terrain to the final polish. "
            "If this sparked ideas for your own worlds, stick around. The next episode goes deeper, "
            "and I'd love to have you back."
        ),
        "b": (
            "That's the run — terrain, layout, caves, and polish in one arc. "
            "Projects like this are how we learn in public: adults ship faster, young builders pick up "
            "real skills, and kids get a fun lens on creativity in the AI era. "
            "Follow for the next build — you won't want to miss it."
        ),
        "c": (
            "Thank you for watching all the way through. "
            "Subscribe or follow if you want the next drop — we're just getting started."
        ),
    }
    body = scripts.get(outro_id, scripts["b"])
    return humanize_speech_text(sanitize_narration_text(body), spec)[:480]


def milestone_narration_cue_text(m: dict[str, Any], spec: dict[str, Any] | None = None) -> str:
    """Speech for one on-screen hold — captions/narratorScript (used for clip sync)."""
    spec = flatten_narrator_personal_settings(spec or {})
    ns = (m.get("narratorScript") or "").strip()
    if ns:
        return humanize_speech_text(sanitize_narration_text(ns), spec)[:480]
    cap = " ".join(
        str(x).strip() for x in (m.get("line1"), m.get("line2"), m.get("line3")) if x
    ).strip()
    if cap:
        return humanize_speech_text(sanitize_narration_text(cap), spec)[:480]
    ch = _clean_for_speech(str(m.get("chapter") or m.get("phase") or ""))
    if ch and not _is_narration_meta_line(ch):
        return humanize_speech_text(sanitize_narration_text(ch), spec)[:320]
    return ""


def split_script_across_clips(full_script: str, clip_durations: list[float]) -> list[str]:
    """Split continuous script into one chunk per video clip, weighted by clip duration."""
    words = (full_script or "").split()
    n = len(clip_durations)
    if not words or n == 0:
        return [""] * n
    total = sum(clip_durations) or float(n)
    budgets: list[int] = []
    assigned = 0
    for i, dur in enumerate(clip_durations):
        if i == n - 1:
            budgets.append(max(0, len(words) - assigned))
        else:
            b = max(2, int(len(words) * (dur / total)))
            budgets.append(b)
            assigned += b
    chunks: list[str] = []
    pos = 0
    for b in budgets:
        chunks.append(" ".join(words[pos : pos + b]).strip())
        pos += b
    return chunks


def clip_narrator_offset_sec(
    ci: int,
    n_clips: int,
    spec: dict[str, Any],
    *,
    milestone: dict[str, Any] | None = None,
    clip_label: str = "",
) -> float:
    if ci == 0:
        return float(spec.get("introNarratorOffsetSec", 0.6))
    if ci == n_clips - 1:
        return float(spec.get("outroNarratorOffsetSec", 0.6))
    if milestone is not None:
        return float(
            milestone.get("_plannedNarratorOffsetSec") or hold_narrator_offset_sec(spec, milestone)
        )
    label = (clip_label or "").lower()
    if label.startswith("hold"):
        return hold_narrator_offset_sec(spec, None)
    return float(spec.get("timelapseNarratorOffsetSec", 0.25))


def split_full_script_to_hold_texts(
    full_script: str,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any] | None = None,
) -> list[str]:
    """Split one continuous script into per-milestone chunks (word-proportional)."""
    active = [m for m in milestones if not m.get("_skipNarration")]
    if not active:
        return []
    words = (full_script or "").split()
    if not words:
        return [""] * len(active)
    n = len(active)
    out: list[str] = []
    for idx in range(n):
        start = (len(words) * idx) // n
        end = (len(words) * (idx + 1)) // n
        chunk = " ".join(words[start:end]).strip()
        if chunk:
            out.append(humanize_speech_text(sanitize_narration_text(chunk), spec or {}))
        else:
            out.append("")
    return out


def build_full_script_narration_cues(
    clips: list[Path],
    milestones: list[dict[str, Any]],
    hold_clip_indices: list[int],
    full_script: str,
    spec: dict[str, Any],
    clip_labels: list[str] | None = None,
) -> tuple[dict[int, tuple[str, float]], dict[int, str]]:
    """
    Align full-script voice to every video clip (intro, timelapse, holds, tail, outro).
    Previously only intro/holds/outro had cues — bridge/timelapse/tail were silent (often 60s+ gap).
    """
    spec = flatten_narrator_personal_settings(spec)
    ffprobe = find_ffprobe()
    durs = [probe_duration(c, ffprobe=ffprobe) for c in clips]
    labels = clip_labels or [f"clip_{i}" for i in range(len(clips))]
    hold_by_ci: dict[int, dict[str, Any]] = {}
    for hi, ci in enumerate(hold_clip_indices):
        if hi < len(milestones):
            hold_by_ci[ci] = milestones[hi]

    chunks = split_script_across_clips(full_script, durs)
    narration_cues: dict[int, tuple[str, float]] = {}
    cue_labels: dict[int, str] = {}

    for ci, chunk in enumerate(chunks):
        dur = durs[ci] if ci < len(durs) else 0.0
        if dur < 0.05 or not chunk.strip():
            continue
        m = hold_by_ci.get(ci)
        if m and m.get("_skipNarration"):
            continue
        text = humanize_speech_text(sanitize_narration_text(chunk), spec)
        if not text.strip():
            continue
        label = labels[ci] if ci < len(labels) else f"clip_{ci}"
        off = clip_narrator_offset_sec(
            ci, len(clips), spec, milestone=m, clip_label=label
        )
        narration_cues[ci] = (text, off)
        if ci == 0:
            cue_labels[ci] = "intro"
        elif ci == len(clips) - 1:
            cue_labels[ci] = "outro"
        elif m:
            hi = hold_clip_indices.index(ci) if ci in hold_clip_indices else ci
            cap = (m.get("line1") or m.get("chapter") or f"hold {hi + 1}")[:48]
            cue_labels[ci] = f"hold {hi + 1} · {cap}"
        else:
            cue_labels[ci] = label

    covered = sum(durs[i] for i in narration_cues if i < len(durs))
    total = sum(durs)
    print(
        f"Full-script clip cues: {len(narration_cues)}/{len(clips)} clips, "
        f"~{covered:.0f}s/{total:.0f}s video with voice text",
        flush=True,
    )
    return narration_cues, cue_labels


def audit_clip_narration_sync(
    clips: list[Path],
    milestones: list[dict[str, Any]],
    hold_clip_indices: list[int],
    full_script: str,
    spec: dict[str, Any],
    clip_labels: list[str] | None = None,
) -> list[dict[str, Any]]:
    """Report video clip timeline vs narration cue text (for drift diagnosis)."""
    ffprobe = find_ffprobe()
    cues, labels = build_full_script_narration_cues(
        clips, milestones, hold_clip_indices, full_script, spec, clip_labels
    )
    rows: list[dict[str, Any]] = []
    t = 0.0
    for ci, clip in enumerate(clips):
        dur = probe_duration(clip, ffprobe=ffprobe)
        entry = cues.get(ci)
        text = entry[0][:120] if entry else ""
        rows.append(
            {
                "clip": ci,
                "label": labels.get(ci, f"clip_{ci}"),
                "videoStartSec": round(t, 2),
                "videoDurSec": round(dur, 2),
                "narrationPreview": text,
                "hasCue": ci in cues,
            }
        )
        t += dur
    return rows


def uses_full_script_narration(spec: dict[str, Any] | None) -> bool:
    """One Cursor-written script for the whole video — do not read on-screen captions."""
    spec = flatten_narrator_personal_settings(spec or {})
    mode = str(spec.get("narrationMode", "")).lower().replace("-", "_")
    if mode in ("fullscript", "full_script", "cursor_script", "whole_script"):
        return True
    if spec.get("cursorFullNarration") and spec.get("narratorIgnoreCaptions", True):
        return True
    if spec.get("narratorIgnoreCaptions") and (
        spec.get("fullNarrationScript") or spec.get("fullNarrationScriptPath")
    ):
        return True
    return bool(spec.get("useFullNarrationScript", False))


def full_narration_json_path(run_dir: Path | None) -> Path | None:
    if not run_dir:
        return None
    p = run_dir / "DemoRecapFullNarration.json"
    return p if p.is_file() else None


def expand_local_narration_script(
    script: str,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    target_sec: float,
) -> str:
    """Pad local milestone script until it can fill the graded video runtime."""
    spec = flatten_narrator_personal_settings(spec or {})
    target_words = narration_target_word_count(spec, target_sec)
    words = script.split()
    if len(words) >= int(target_words * 0.88):
        return script

    extras: list[str] = [
        "What you are watching is research and development in public — not a slideshow. "
        "Tools like this help grown-ups ship faster, help young adults learn real production skills, "
        "and give kids a playful visual language for creativity in the AI era.",
    ]
    for m in milestones:
        if m.get("_skipNarration"):
            continue
        cap = milestone_narration_cue_text(m, spec)
        if not cap:
            continue
        chapter = str(m.get("chapter") or m.get("phase") or "").strip()
        lead = f"At this beat — {chapter} — " if chapter else "Right here — "
        extras.append(
            f"{lead}{cap} "
            "Notice how each decision stacks: layout, readability, and room for gameplay later."
        )
    extras.extend(
        [
            "That stacking is exactly how professional worlds get made — one honest iteration at a time.",
            "If this helped you see procedural worlds differently, stick around. "
            "The next build goes deeper, and I would love to have you back for it.",
        ]
    )

    out = script
    ei = 0
    while len(out.split()) < target_words and ei < len(extras) * 4:
        out = f"{out} {extras[ei % len(extras)]}"
        ei += 1
    return humanize_speech_text(sanitize_narration_text(out), spec)


def write_local_full_narration_script(
    run_dir: Path,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    *,
    target_sec: float | None = None,
) -> str:
    """
    Build DemoRecapFullNarration.json from milestone beats/captions — no Cursor Agent wait.
    Used for --narration-only / Terminal Personal Voice so say+mysay starts immediately.
    """
    spec = flatten_narrator_personal_settings(spec or {})
    intro_title = str(spec.get("introTitle") or "World Build Recap").strip()
    intro_sub = str(spec.get("introSubtitle") or "").strip()
    parts: list[str] = [
        f"Hey — welcome to {intro_title}."
        + (f" {intro_sub}." if intro_sub else "")
        + " This is a real Unity world build, and we are going to learn from every beat — "
        "whether you ship games, study design, or you are young and curious about how worlds are made."
    ]
    for m in milestones:
        if m.get("_skipNarration"):
            continue
        ns = (m.get("narratorScript") or "").strip()
        if ns:
            parts.append(sanitize_narration_text(ns))
            continue
        cap = " ".join(
            str(x).strip()
            for x in (m.get("line1"), m.get("line2"), m.get("line3"))
            if x
        ).strip()
        if cap:
            parts.append(sanitize_narration_text(cap))
    parts.append(outro_narration_script(spec))
    say_r = personal_say_rate(spec)
    dur = float(target_sec or spec.get("fullNarrationTargetSec") or spec.get("targetDurationSec") or 480)
    script = expand_local_narration_script(
        humanize_speech_text(sanitize_narration_text(" ".join(parts)), spec),
        milestones,
        spec,
        dur,
    )
    write_narration_voice_guide(run_dir, spec, milestones, target_sec=dur, script_preview=script)
    payload = {
        "script": script,
        "wordCount": len(script.split()),
        "targetDurationSec": dur,
        "targetWordCount": narration_target_word_count(spec, dur),
        "sayRateWpm": say_r,
        "source": "local_milestones",
    }
    payload.update(narration_voice_metadata(spec, run_dir))
    path = run_dir / "DemoRecapFullNarration.json"
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    (run_dir / "DemoRecapFullNarration.txt").write_text(script + "\n", encoding="utf-8")
    print(
        f"Full narration script (local, no Cursor): {payload['wordCount']} words "
        f"(target ~{payload['targetWordCount']}) → {path.name}",
        flush=True,
    )
    return script


def resolve_full_narration_script(
    run_dir: Path | None,
    spec: dict[str, Any],
    milestones: list[dict[str, Any]] | None = None,
) -> str:
    """Load continuous voice script — never falls back to caption lines."""
    spec = flatten_narrator_personal_settings(spec or {})
    inline = str(spec.get("fullNarrationScript", "") or "").strip()
    if inline:
        return humanize_speech_text(sanitize_narration_text(inline), spec)

    if run_dir:
        raw_path = spec.get("fullNarrationScriptPath")
        if raw_path:
            p = Path(str(raw_path)).expanduser()
            if p.is_file():
                if p.suffix.lower() == ".json":
                    data = json.loads(p.read_text(encoding="utf-8"))
                    if isinstance(data, dict) and data.get("script"):
                        return humanize_speech_text(
                            sanitize_narration_text(str(data["script"])), spec
                        )
                else:
                    return humanize_speech_text(
                        sanitize_narration_text(p.read_text(encoding="utf-8")), spec
                    )
        jp = full_narration_json_path(run_dir)
        if jp:
            data = json.loads(jp.read_text(encoding="utf-8"))
            script = (data.get("script") or "").strip()
            if script:
                return humanize_speech_text(sanitize_narration_text(script), spec)
        tp = run_dir / "DemoRecapFullNarration.txt"
        if tp.is_file():
            return humanize_speech_text(
                sanitize_narration_text(tp.read_text(encoding="utf-8")), spec
            )

    # Last resort: stitch per-beat narratorScript only (never line1/2/3 captions)
    if milestones and spec.get("narratorAllowBeatScriptFallback", True):
        parts: list[str] = []
        for m in milestones:
            if m.get("_skipNarration"):
                continue
            ns = (m.get("narratorScript") or "").strip()
            if ns:
                parts.append(sanitize_narration_text(ns))
        if parts:
            joined = " ".join(parts)
            return humanize_speech_text(sanitize_narration_text(joined), spec)

    return ""


def beat_narration_script(m: dict[str, Any], spec: dict[str, Any] | None = None) -> str:
    """Per-beat speech — disabled when fullScript mode (captions are screen-only)."""
    spec = flatten_narrator_personal_settings(spec or {})
    if uses_full_script_narration(spec):
        return ""
    if spec.get("narratorIgnoreCaptions"):
        if m.get("narratorScript"):
            raw = sanitize_narration_text(str(m["narratorScript"]))
            return humanize_speech_text(raw, spec)[:480]
        return ""
    if m.get("narratorScript"):
        raw = sanitize_narration_text(str(m["narratorScript"]))
        return humanize_speech_text(raw, spec)[:480]
    lines = _narration_lines_from_milestone(m)
    if m.get("beatKind") == "subbeat":
        lines = lines[:2]
    if not lines:
        ch = _clean_for_speech(str(m.get("chapter", "")))
        if ch and not _is_narration_meta_line(ch):
            return humanize_speech_text(sanitize_narration_text(ch), spec)[:320]
        return ""
    if len(lines) == 1:
        return humanize_speech_text(sanitize_narration_text(lines[0]), spec)[:420]
    joined = lines[0]
    for p in lines[1:]:
        joined = f"{joined}. {p}" if not joined.endswith((".", "!", "?")) else f"{joined} {p}"
    return humanize_speech_text(sanitize_narration_text(joined), spec)[:420]


def _resolve_edge_voice(voice: str) -> str:
    key = (voice or "Andrew").lower().replace(" ", "")
    if key in EDGE_VOICE_MAP:
        return EDGE_VOICE_MAP[key]
    if "-" in voice and voice.startswith("en-"):
        return voice
    return "en-US-AndrewNeural"


def _parse_say_voice_line(line: str) -> str | None:
    line = line.strip()
    if not line:
        return None
    # "Jacob Adkins (English (US)) en_US    # …"
    idx = line.find(" (")
    if idx > 0:
        return line[:idx].strip()
    m = re.match(r"^(\S+)", line)
    return m.group(1) if m else None


def list_macos_say_voices() -> list[str]:
    if platform.system() != "Darwin":
        return []
    try:
        out = subprocess.check_output(["say", "-v", "?"], text=True, errors="replace")
    except (FileNotFoundError, subprocess.CalledProcessError):
        return []
    names: list[str] = []
    seen: set[str] = set()
    for line in out.splitlines():
        name = _parse_say_voice_line(line)
        if name and name not in seen:
            seen.add(name)
            names.append(name)
    return names


def resolve_say_voice(requested: str, voices: list[str]) -> str:
    """Match Personal Voice names like 'Jacob Adkins' (may include spaces)."""
    req = (requested or "Daniel").strip()
    if not voices:
        return req
    if req in voices:
        return req
    low = req.lower()
    for v in voices:
        if v.lower() == low:
            return v
    for v in voices:
        if low in v.lower() or v.lower() in low:
            return v
    return req


def resolve_personal_voice_name(spec: dict[str, Any], run_dir: Path | None = None) -> str | None:
    for key in ("narratorPersonalVoice", "personalVoiceName"):
        raw = spec.get(key)
        if raw:
            return _normalize_personal_voice_name(str(raw))
    for base in (APPROVED_VOICE_DIR, run_dir or Path()):
        p = base / PERSONAL_VOICE_NAME_FILE
        if p.is_file():
            name = p.read_text(encoding="utf-8").strip().splitlines()[0].strip()
            if name:
                return _normalize_personal_voice_name(name)
    return None


def strict_personal_narration(spec: dict[str, Any] | None) -> bool:
    """True when system say/edge fallbacks must not substitute for Personal Voice."""
    spec = flatten_narrator_personal_settings(spec or {})
    eng = str(spec.get("narratorEngine", "auto")).lower()
    return bool(
        spec.get("narratorRequirePersonal")
        or eng in ("personal", "personal-voice")
    )


def personal_voice_visible_to_say(voice_name: str) -> bool:
    """Personal Voice must appear in `say -v ?` for say+mysay capture (Terminal.app)."""
    if platform.system() != "Darwin" or not voice_name.strip():
        return False
    voices = list_macos_say_voices()
    if not voices:
        return False
    resolved = resolve_say_voice(voice_name.strip(), voices)
    return resolved in voices


def write_narration_capture_record(
    run_dir: Path | None,
    *,
    capture_method: str,
    voice_name: str,
    spec: dict[str, Any] | None = None,
) -> None:
    if not run_dir:
        return
    path = run_dir / "RecapNarrationCapture.json"
    payload = {
        "captureMethod": capture_method,
        "voiceName": voice_name,
        "capturedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "personalVerified": capture_method.startswith(("say+mysay", "AVSpeech", "personal")),
    }
    if spec:
        payload["sayRateWpm"] = personal_say_rate(spec)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def read_narration_capture_record(work_dir: Path) -> dict[str, Any]:
    p = work_dir / "RecapNarrationCapture.json"
    if not p.is_file():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {}


def narration_wav_is_verified_personal(work_dir: Path, spec: dict[str, Any] | None = None) -> bool:
    """True when narration.wav was captured with Personal Voice (not edge/system fallback)."""
    spec = flatten_narrator_personal_settings(spec or {})
    if not strict_personal_narration(spec):
        return True
    rec = read_narration_capture_record(work_dir)
    if rec.get("personalVerified"):
        return True
    method = str(rec.get("captureMethod") or "")
    return method.startswith(("say+mysay", "AVSpeech", "personal"))


def personal_voice_capture_ready(
    spec: dict[str, Any] | None, run_dir: Path | None = None
) -> tuple[bool, str]:
    """Report whether say lists Personal Voice; capture is still attempted via mysay/AVSpeech."""
    spec = flatten_narrator_personal_settings(spec or {})
    if not strict_personal_narration(spec):
        return True, "personal not required"
    name = resolve_personal_voice_name(spec, run_dir) or "Jacob Adkins"
    if personal_voice_visible_to_say(name):
        return True, f"say voice visible: {name}"
    return (
        True,
        f"say -v ? hidden for “{name}” in this app — will still try mysay/AVSpeech capture",
    )


def narration_voice_metadata(
    spec: dict[str, Any] | None,
    run_dir: Path | None = None,
    *,
    capture_method: str | None = None,
) -> dict[str, Any]:
    """Voice fields stamped into DemoRecapFullNarration.json for verification."""
    spec = flatten_narrator_personal_settings(spec or {})
    name = resolve_personal_voice_name(spec, run_dir) or str(spec.get("narratorVoice", "Andrew"))
    ready, ready_reason = personal_voice_capture_ready(spec, run_dir)
    meta: dict[str, Any] = {
        "narratorEngine": spec.get("narratorEngine", "personal"),
        "narratorPersonalVoice": name,
        "narratorRequirePersonal": bool(spec.get("narratorRequirePersonal", True)),
        "personalVoiceCaptureReady": ready,
        "personalVoiceCaptureReason": ready_reason,
    }
    if capture_method:
        meta["captureMethod"] = capture_method
    return meta


def stamp_full_narration_voice_metadata(
    run_dir: Path,
    spec: dict[str, Any] | None,
    *,
    capture_method: str | None = None,
) -> None:
    """Merge voice metadata into DemoRecapFullNarration.json when present."""
    jp = full_narration_json_path(run_dir)
    if not jp:
        return
    try:
        data = json.loads(jp.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return
    if not isinstance(data, dict):
        return
    data.update(narration_voice_metadata(spec, run_dir, capture_method=capture_method))
    jp.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def _normalize_personal_voice_name(name: str) -> str:
    n = name.strip()
    if n == n.lower() and " " in n:
        return n.title()
    return n


def resolve_reference_wav(spec: dict[str, Any], run_dir: Path | None = None) -> Path | None:
    raw = spec.get("narratorReferenceWav")
    if raw:
        p = Path(str(raw)).expanduser()
        if p.is_file():
            return p.resolve()
    for base in (APPROVED_VOICE_DIR, run_dir or Path()):
        for name in REFERENCE_WAV_NAMES:
            p = base / name
            if p.is_file():
                return p.resolve()
    return None


def _terminate_subprocess(proc: subprocess.Popen) -> None:
    """Watchdog kill — `say` + Personal Voice can hang on 1-letter tokens."""
    try:
        proc.terminate()
        proc.wait(timeout=2)
    except subprocess.TimeoutExpired:
        try:
            proc.kill()
            proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            pass


def _mysay_once(
    text: str,
    wav: Path,
    voice_name: str,
    rate: int,
    caf: Path,
    *,
    timeout_sec: int = 150,
    use_watchdog: bool = False,
) -> bool:
    env = os.environ.copy()
    env["DYLD_INSERT_LIBRARIES"] = str(_MYSAY_DYLIB.resolve())
    cmd = ["say", "-v", voice_name, "-r", str(rate), "-o", str(caf), text]
    cap = max(8, int(timeout_sec))
    if use_watchdog:
        proc = subprocess.Popen(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
        )
        try:
            _stdout, stderr = proc.communicate(timeout=cap)
            if proc.returncode != 0:
                err = (stderr or b"").decode(errors="replace").strip()
                if err:
                    print(f"Personal Voice (say): {err}", file=sys.stderr)
                return False
        except subprocess.TimeoutExpired:
            _terminate_subprocess(proc)
            raise
    else:
        subprocess.run(
            cmd,
            check=True,
            capture_output=True,
            env=env,
            timeout=cap,
        )
    if not caf.is_file() or caf.stat().st_size < 12000:
        return False
    subprocess.run(
        [
            find_ffmpeg(),
            "-y",
            "-i",
            str(caf),
            "-af",
            "aresample=48000,aformat=channel_layouts=stereo",
            "-c:a",
            "pcm_s16le",
            str(wav),
        ],
        check=True,
        capture_output=True,
        timeout=45,
    )
    return wav_is_audible(wav)


def personal_voice_mysay_to_wav(
    text: str,
    wav: Path,
    voice_name: str,
    *,
    rate: int = 158,
    prefer_plain: bool = True,
    spec: dict[str, Any] | None = None,
) -> bool:
    """say -o with mysay.dylib — captures real Personal Voice to CAF (most reliable on Terminal)."""
    if not text.strip() or platform.system() != "Darwin":
        return False
    if not _MYSAY_DYLIB.is_file():
        return False
    caf = wav.with_suffix(".pv.caf")
    plain = strip_slnc_markup(_clean_for_speech(text))
    attempts: list[tuple[str, str]] = []
    if prefer_plain and plain.strip():
        attempts.append(("plain", plain))
    if text.strip() and text != plain:
        attempts.append(("with pauses", text))
    if not attempts and plain.strip():
        attempts.append(("plain", plain))
    for label, attempt in attempts:
        if not attempt.strip():
            continue
        print(f"Personal Voice: say+mysay/{label} ({len(attempt)} chars)…", flush=True)
        try:
            caf.unlink(missing_ok=True)
        except OSError:
            pass
        if spec and spec.get("narratorWordQueueCapture"):
            timeout_sec = word_queue_mysay_timeout_sec(spec, attempt)
            use_wd = bool(spec.get("narratorMysayWatchdog", word_queue_watchdog_enabled(spec)))
        else:
            timeout_sec = personal_mysay_timeout_sec(spec, attempt)
            use_wd = bool(spec and spec.get("narratorMysayWatchdog"))
        try:
            if _mysay_once(
                attempt,
                wav,
                voice_name,
                rate,
                caf,
                timeout_sec=timeout_sec,
                use_watchdog=use_wd,
            ):
                if label == "plain" and text != plain:
                    print(f"Personal Voice: used plain text (pause markup was silent)", file=sys.stderr)
                return True
        except subprocess.TimeoutExpired:
            print(
                f"Personal Voice (say+mysay/{label}): timed out after {timeout_sec}s "
                f"— will try AVSpeech or next attempt",
                file=sys.stderr,
            )
        except (subprocess.CalledProcessError, FileNotFoundError, OSError) as exc:
            print(f"Personal Voice (say+mysay/{label}): {exc}", file=sys.stderr)
    caf.unlink(missing_ok=True)
    return False


def personal_avspeech_timeout_sec(spec: dict[str, Any] | None) -> int:
    spec = flatten_narrator_personal_settings(spec or {})
    return int(spec.get("narratorPersonalAvSpeechTimeoutSec", 50))


def personal_mysay_timeout_sec(spec: dict[str, Any] | None, text: str = "") -> int:
    """`say` + Personal Voice can block on first use — scale timeout with cue length."""
    spec = flatten_narrator_personal_settings(spec or {})
    if spec.get("narratorWordQueueCapture"):
        return word_queue_mysay_timeout_sec(spec, text)
    base = int(spec.get("narratorMysayTimeoutSec", 150))
    per_char = float(spec.get("narratorMysayTimeoutPerCharSec", 0.35))
    cap = int(spec.get("narratorMysayTimeoutMaxSec", 360))
    floor = int(spec.get("narratorMysayTimeoutMinSec", 60))
    est = base + int(max(0, len((text or "").strip())) * per_char)
    return max(floor, min(cap, est))


def say_capture_phrase_for_word(word: str) -> str:
    """Single-letter tokens often hang in say+mysay — pad so Personal Voice finishes."""
    w = word.strip()
    if len(w) <= 2:
        return f"{w},"
    return w


def chunk_tokens_for_queue(tokens: list[str], chunk_words: int) -> list[str]:
    """Group words into short phrases — much faster than 1 say call per word."""
    n = max(1, int(chunk_words))
    chunks: list[str] = []
    buf: list[str] = []
    for tok in tokens:
        buf.append(tok)
        if len(buf) >= n:
            chunks.append(" ".join(buf))
            buf = []
    if buf:
        chunks.append(" ".join(buf))
    return chunks


def estimate_word_queue_minutes(
    unit_count: int, spec: dict[str, Any] | None, *, sample_chars: int = 12
) -> float:
    """Rough ETA for portfolio / word-queue capture."""
    spec = flatten_narrator_personal_settings(spec or {})
    wq = word_queue_cfg(spec)
    tmo = personal_mysay_timeout_sec(
        {**spec, "narratorWordQueueCapture": True},
        "x" * sample_chars,
    )
    overhead = float(wq.get("etaOverheadSec", 2.5))
    return (unit_count * (tmo + overhead)) / 60.0


def personal_voice_to_wav(
    text: str, wav: Path, voice_name: str, *, spec: dict[str, Any] | None = None
) -> bool:
    """AVSpeechSynthesizer.write — fallback when say+mysay is unavailable."""
    if not text.strip() or platform.system() != "Darwin":
        return False
    if not _PERSONAL_VOICE_SWIFT.is_file():
        print("Missing personal-voice-speak.swift", file=sys.stderr)
        return False
    timeout = personal_avspeech_timeout_sec(spec)
    print(f"Personal Voice: AVSpeech ({len(text)} chars, {timeout}s max)…", flush=True)
    tmp = wav.with_suffix(".pv.wav")
    try:
        proc = subprocess.run(
            [
                "swift",
                str(_PERSONAL_VOICE_SWIFT),
                "-v",
                voice_name,
                "-t",
                text,
                "-o",
                str(tmp),
            ],
            capture_output=True,
            text=True,
            timeout=timeout,
        )
        err_out = (proc.stderr or proc.stdout or "").strip()
        if proc.returncode != 0:
            if err_out:
                print(f"Personal Voice: {err_out}", file=sys.stderr)
            return False
        if not tmp.is_file() or tmp.stat().st_size < 256:
            print("Personal Voice: output empty (check Speech → Personal Voice allow list)", file=sys.stderr)
            return False
        for line in err_out.splitlines():
            if line.startswith("Using:"):
                print(f"Narrator: {line.strip()}")
        if wav.is_file():
            wav.unlink()
        tmp.replace(wav)
        return True
    except (subprocess.TimeoutExpired, FileNotFoundError, OSError) as exc:
        print(f"Personal Voice synth failed: {exc}", file=sys.stderr)
        return False


def is_personal_narration(spec: dict[str, Any] | None) -> bool:
    spec = flatten_narrator_personal_settings(spec or {})
    eng = str(spec.get("narratorEngine", "auto")).lower()
    return bool(
        spec.get("narratorRequirePersonal")
        or eng in ("personal", "personal-voice")
        or pick_speech_engine(spec, None) == "personal"
    )


def pick_speech_engine(spec: dict[str, Any], run_dir: Path | None = None) -> str:
    eng = str(spec.get("narratorEngine", "auto")).lower()
    personal = resolve_personal_voice_name(spec, run_dir)
    if spec.get("narratorRequirePersonal") or eng in ("personal", "personal-voice"):
        return "personal"
    if personal and eng == "auto":
        return "personal"
    if eng in ("say", "macos"):
        return "say"
    if eng in ("edge", "edge-tts"):
        return "edge"
    if eng == "auto" and edge_tts_available():
        return "edge"
    if personal:
        return "personal"
    return "say"


def edge_tts_to_wav(text: str, wav: Path, *, voice: str = "Andrew", rate: str = "+0%") -> bool:
    if not text.strip():
        return False
    if not edge_tts_available():
        return False
    import edge_tts  # type: ignore

    ev = _resolve_edge_voice(voice)
    tmp = wav.with_suffix(".edge.mp3")

    async def _run() -> None:
        comm = edge_tts.Communicate(text, ev, rate=rate)
        await comm.save(str(tmp))

    try:
        asyncio.run(_run())
        if not tmp.is_file() or tmp.stat().st_size < 64:
            return False
        subprocess.run(
            [
                find_ffmpeg(),
                "-y",
                "-i",
                str(tmp),
                "-ar",
                "48000",
                "-ac",
                "2",
                str(wav),
            ],
            check=True,
            capture_output=True,
        )
        tmp.unlink(missing_ok=True)
        return wav.is_file() and wav.stat().st_size > 64
    except Exception as exc:
        print(f"edge-tts failed: {exc}", file=sys.stderr)
        tmp.unlink(missing_ok=True)
        return False


def say_to_wav(text: str, wav: Path, *, voice: str = "Daniel", rate: int = 168) -> bool:
    if not text.strip() or platform.system() != "Darwin":
        return False
    voices = list_macos_say_voices()
    candidates = [voice]
    if " " in voice.strip():
        candidates.append(voice.strip().title())
    v = resolve_say_voice(voice, voices)
    if v not in voices and voices:
        for fallback in (*candidates, "Daniel", "Samantha", "Alex"):
            v2 = resolve_say_voice(fallback, voices)
            if v2 in voices:
                v = v2
                break
    aiff = wav.with_suffix(".aiff")
    env = os.environ.copy()
    if _MYSAY_DYLIB.is_file():
        env["DYLD_INSERT_LIBRARIES"] = str(_MYSAY_DYLIB.resolve())
    try:
        subprocess.run(
            ["say", "-v", v, "-r", str(rate), "-o", str(aiff), text],
            check=True,
            capture_output=True,
            env=env,
        )
        subprocess.run(
            [
                find_ffmpeg(),
                "-y",
                "-i",
                str(aiff),
                "-ar",
                "48000",
                "-ac",
                "2",
                str(wav),
            ],
            check=True,
            capture_output=True,
        )
        aiff.unlink(missing_ok=True)
        return wav.is_file()
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        print(f"say failed ({v}): {exc}", file=sys.stderr)
        return False


def speech_to_wav(
    text: str,
    wav: Path,
    *,
    voice: str = "Andrew",
    rate: int = 198,
    engine: str = "auto",
    spec: dict[str, Any] | None = None,
    run_dir: Path | None = None,
) -> bool:
    spec = spec or {}
    personal = resolve_personal_voice_name(spec, run_dir)
    require_personal = bool(spec.get("narratorRequirePersonal") or personal)
    eng = pick_speech_engine({**spec, "narratorEngine": spec.get("narratorEngine", engine)}, run_dir)

    if eng == "personal":
        name = personal or voice
        say_rate = personal_say_rate(spec)
        spoken = prepare_personal_speech_text(text, spec)
        plain = strip_slnc_markup(prepare_say_capture_text(text, spec))
        prefer_plain = not spec.get("narratorNaturalPauses", False)
        prefer_mysay = bool(spec.get("narratorPreferMysay", True)) and _MYSAY_DYLIB.is_file()
        skip_avspeech = bool(spec.get("narratorSkipAvSpeech", False))

        def _finish_personal(method: str, *, word_queue_capture: bool = False) -> bool:
            if not wav_is_audible(wav):
                try:
                    wav.unlink(missing_ok=True)
                except OSError:
                    pass
                return False
            ladder_spec = {
                **spec,
                "narratorCaptureWordQueue": word_queue_capture,
                "narratorUsedWordQueue": word_queue_capture or spec.get("narratorUsedWordQueue"),
                "narratorCaptureMode": capture_queue_mode(spec) if word_queue_capture else "",
                "narratorCaptureText": plain or spoken,
                "narratorSourceText": expand_breath_performance_markers(text),
            }
            deliver_personal_voice_wav(wav, ladder_spec)  # voiceHelpers chain
            if not wav_is_audible(wav):
                try:
                    wav.unlink(missing_ok=True)
                except OSError:
                    pass
                return False
            print(f"Narrator: Personal Voice via {method} ({name}, {say_rate} wpm)", flush=True)
            spec["_lastCaptureMethod"] = method
            return True

        def _try_mysay() -> bool:
            for spoken_try in (plain, spoken) if plain != spoken else (plain,):
                if not spoken_try.strip():
                    continue
                if personal_voice_mysay_to_wav(
                    spoken_try,
                    wav,
                    name,
                    rate=say_rate,
                    prefer_plain=prefer_plain,
                    spec=spec,
                ) and _finish_personal("say+mysay"):
                    return True
            return False

        def _try_avspeech() -> bool:
            if skip_avspeech:
                return False
            return personal_voice_to_wav(plain or spoken, wav, name, spec=spec) and _finish_personal(
                "AVSpeech"
            )

        print(f"Personal Voice: synthesizing cue ({len(plain)} chars)…", flush=True)
        if portfolio_word_queue_first(spec) and word_queue_should_use(plain or spoken, spec):
            if personal_voice_word_queue_wav(
                plain or spoken,
                wav,
                name,
                rate=personal_word_queue_say_rate(spec),
                spec=spec,
                run_dir=run_dir,
            ) and _finish_personal("say+mysay/word-queue", word_queue_capture=True):
                return True
        if emphasis_capture_enabled(plain or spoken, spec):
            if personal_voice_emphasis_capture_wav(
                plain or spoken,
                wav,
                name,
                rate=say_rate,
                spec=spec,
            ) and _finish_personal("say+mysay/emphasis"):
                return True
        if word_queue_should_use(plain or spoken, spec):
            if personal_voice_word_queue_wav(
                plain or spoken,
                wav,
                name,
                rate=personal_word_queue_say_rate(spec),
                spec=spec,
                run_dir=run_dir,
            ) and _finish_personal("say+mysay/word-queue", word_queue_capture=True):
                return True
        if prefer_mysay:
            if _try_mysay():
                return True
            if _try_avspeech():
                return True
        else:
            if _try_avspeech():
                return True
            if _try_mysay():
                return True
        if spec.get("narratorEdgeFallback", False) and edge_tts_available():
            print("Personal Voice failed — emergency edge-tts fallback (not your voice).", file=sys.stderr)
            if edge_tts_to_wav(plain or text, wav, voice=voice, rate=str(spec.get("narratorEdgeRate", "+8%"))):
                return True
        print(
            "Personal Voice failed — recap has no narration audio.\n"
            "  bash " + str(_TOOLS_DIR / "test-personal-voice.sh") + "\n"
            "  Lower pausePeriodMs (1200ms breaks say); try maxPauseMs 650 in ApprovedCards.\n"
            "  bash " + str(_TOOLS_DIR / "run-recap-terminal.sh"),
            file=sys.stderr,
        )
        return False

    if require_personal:
        print("narratorRequirePersonal is set but engine is not personal — aborting TTS.", file=sys.stderr)
        return False

    text = humanize_speech_text(text, spec)

    if eng == "edge" or (eng == "auto" and edge_tts_available()):
        rate_pct = str(spec.get("narratorEdgeRate", "+8%"))
        if edge_tts_to_wav(text, wav, voice=voice, rate=rate_pct):
            if spec.get("narratorHumanize", True):
                polish_voice_wav(wav, spec)
            return True

    if platform.system() == "Darwin":
        if say_to_wav(text, wav, voice=voice, rate=rate):
            if spec.get("narratorHumanize", True):
                polish_voice_wav(wav, spec)
            return True
    return False


def silence_wav(path: Path, duration: float) -> None:
    duration = max(0.05, duration)
    subprocess.run(
        [
            find_ffmpeg(),
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"anullsrc=r=48000:cl=stereo:d={duration:.3f}",
            "-c:a",
            "pcm_s16le",
            str(path),
        ],
        check=True,
        capture_output=True,
    )


def probe_duration(path: Path, *, ffprobe: str | None = None) -> float:
    probe = ffprobe or find_ffprobe()
    out = subprocess.check_output(
        [
            probe,
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        text=True,
    )
    raw = out.strip()
    if not raw or raw.upper() == "N/A":
        return 0.05
    try:
        return max(0.05, float(raw))
    except ValueError:
        return 0.05


def is_personal_narration(spec: dict[str, Any] | None) -> bool:
    spec = flatten_narrator_personal_settings(spec or {})
    eng = str(spec.get("narratorEngine", "")).lower()
    return eng in ("personal", "personal-voice") or bool(spec.get("narratorRequirePersonal"))


def _atempo_chain(ratio: float) -> str:
    filters: list[str] = []
    r = max(0.25, min(4.0, ratio))
    while r > 2.0:
        filters.append("atempo=2.0")
        r /= 2.0
    while r < 0.5:
        filters.append("atempo=0.5")
        r /= 0.5
    filters.append(f"atempo={r:.5f}")
    return ",".join(filters)


def fit_speech_into_slot(
    speech_wav: Path,
    out: Path,
    slot_sec: float,
    *,
    start_offset_sec: float = 0.0,
    ffprobe: str | None = None,
    spec: dict[str, Any] | None = None,
) -> None:
    """Place speech at start_offset; pad/trim to slot. Personal Voice: no atempo, no word-tail fades."""
    spec = flatten_narrator_personal_settings(spec or {})
    ffmpeg = find_ffmpeg()
    personal = is_personal_narration(spec)
    slot_sec = max(0.1, slot_sec)
    start_offset_sec = max(0.0, min(start_offset_sec, slot_sec - 0.15))
    speech_dur = probe_duration(speech_wav, ffprobe=ffprobe)
    tail_pad = float(spec.get("narrationTailPadSec", 0.35 if personal else 0.2))
    avail = max(0.15, slot_sec - start_offset_sec - tail_pad)
    delay_ms = int(start_offset_sec * 1000)

    fade_in = float(spec.get("narratorSpeechFadeInSec", 0.0 if personal else 0.08))
    fade_out = float(spec.get("narratorSpeechFadeOutSec", 0.0 if personal else 0.14))
    max_atempo = float(spec.get("narratorMaxFitAtempo", 1.0))
    min_atempo = float(spec.get("narratorMinFitAtempo", 1.0 if personal else 0.94))
    if personal:
        max_atempo = min(max_atempo, 1.0)
        min_atempo = 1.0

    if not personal and speech_dur > avail * 1.02:
        fit_bias = float(spec.get("narratorFitPaceBias", 1.0))
        ratio = min(max_atempo, (speech_dur / avail) * fit_bias)
        if ratio > 1.002:
            ratio = max(min_atempo, ratio)
            af = _atempo_chain(ratio)
            fitted = out.with_suffix(".fitted.wav")
            subprocess.run(
                [
                    ffmpeg,
                    "-y",
                    "-i",
                    str(speech_wav),
                    "-af",
                    af,
                    "-ar",
                    "48000",
                    "-ac",
                    "2",
                    str(fitted),
                ],
                check=True,
                capture_output=True,
            )
            speech_wav = fitted
            speech_dur = probe_duration(speech_wav, ffprobe=ffprobe)
    elif speech_dur > avail + 0.05:
        print(
            f"Narration: speech {speech_dur:.1f}s in {avail:.1f}s slot — keeping natural pace "
            f"(no time-stretch; extend hold if clipped)",
            file=sys.stderr,
        )

    af_parts: list[str] = [f"adelay={delay_ms}|{delay_ms}"]
    if fade_in > 0.005:
        af_parts.append(f"afade=t=in:st=0:d={fade_in:.3f}")
    if fade_out > 0.005:
        fade_out_st = max(0.0, speech_dur - fade_out)
        af_parts.append(f"afade=t=out:st={fade_out_st:.3f}:d={fade_out:.3f}")
    post_pad = max(0.0, slot_sec - start_offset_sec - speech_dur)
    if post_pad > 0.01:
        af_parts.append(f"apad=pad_dur={post_pad:.3f}")
    af_parts.append(f"atrim=0:{slot_sec:.3f}")
    af_parts.append("asetpts=PTS-STARTPTS")
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-i",
            str(speech_wav),
            "-af",
            ",".join(af_parts),
            "-ar",
            "48000",
            "-ac",
            "2",
            "-c:a",
            "pcm_s16le",
            str(out),
        ],
        check=True,
        capture_output=True,
    )
    out.with_suffix(".fitted.wav").unlink(missing_ok=True)


def match_audio_duration(audio: Path, target_sec: float, *, ffprobe: str | None = None) -> None:
    """Pad or trim narration bed to exactly match video length."""
    cur = probe_duration(audio, ffprobe=ffprobe)
    if abs(cur - target_sec) < 0.04:
        return
    ffmpeg = find_ffmpeg()
    tmp = audio.with_suffix(".matched.wav")
    if cur < target_sec:
        pad = target_sec - cur
        subprocess.run(
            [
                ffmpeg,
                "-y",
                "-i",
                str(audio),
                "-af",
                f"apad=pad_dur={pad:.3f}",
                "-t",
                f"{target_sec:.3f}",
                str(tmp),
            ],
            check=True,
            capture_output=True,
        )
    else:
        subprocess.run(
            [
                ffmpeg,
                "-y",
                "-i",
                str(audio),
                "-t",
                f"{target_sec:.3f}",
                "-ar",
                "48000",
                "-ac",
                "2",
                "-c:a",
                "pcm_s16le",
                str(tmp),
            ],
            check=True,
            capture_output=True,
        )
    tmp.replace(audio)


def _parse_cue(entry: str | tuple[str, float], default_offset: float) -> tuple[str, float]:
    if isinstance(entry, tuple):
        return entry[0], float(entry[1])
    return str(entry), default_offset


def build_narration_for_full_video(
    video: Path,
    script: str,
    out_wav: Path,
    *,
    voice: str = "Andrew",
    rate: int = 183,
    engine: str = "auto",
    spec: dict[str, Any] | None = None,
    run_dir: Path | None = None,
) -> bool:
    """
    Synthesize the full Cursor script once and fit to entire video duration.
    Captions are not used.
    """
    script = (script or "").strip()
    if not script or not video.is_file():
        return False

    spec = flatten_narrator_personal_settings(spec or {})
    ffprobe = find_ffprobe()
    video_dur = probe_duration(video, ffprobe=ffprobe)
    lead = float(spec.get("fullNarrationLeadSec", 0.6))
    slot = max(1.0, video_dur - lead)

    raw = out_wav.parent / "_full_narr_raw.wav"
    raw.parent.mkdir(parents=True, exist_ok=True)
    used_engine = pick_speech_engine(spec, run_dir)

    print(
        f"Full-script narration: {len(script.split())} words → {video_dur:.1f}s video "
        f"(ignoring caption cards)…",
        flush=True,
    )
    if strict_personal_narration(spec):
        ready, reason = personal_voice_capture_ready(spec, run_dir)
        if not ready:
            print(f"Full-script narration skipped: {reason}", file=sys.stderr)
            return False

    capture_method = ""
    if not speech_to_wav(
        script,
        raw,
        voice=voice,
        rate=rate,
        engine=used_engine,
        spec=spec,
        run_dir=run_dir,
    ):
        return False
    capture_method = str(spec.get("_lastCaptureMethod") or "personal")

    speech_dur = probe_duration(raw, ffprobe=ffprobe)
    personal = is_personal_narration(spec)
    max_atempo = float(spec.get("narratorFullScriptMaxAtempo", 1.06))
    min_atempo = float(spec.get("narratorFullScriptMinAtempo", 1.0))

    if not personal and speech_dur > slot + 0.15:
        ratio = min(max_atempo, speech_dur / slot)
        ratio = max(min_atempo, min(max_atempo, ratio))
        if ratio > 1.01:
            print(
                f"Full script {speech_dur:.1f}s > video {slot:.1f}s — "
                f"gentle atempo {ratio:.3f}",
                flush=True,
            )
            fitted = raw.with_suffix(".fit.wav")
            subprocess.run(
                [
                    find_ffmpeg(),
                    "-y",
                    "-i",
                    str(raw),
                    "-af",
                    _atempo_chain(ratio),
                    "-ar",
                    "48000",
                    "-ac",
                    "2",
                    str(fitted),
                ],
                check=True,
                capture_output=True,
            )
            raw = fitted
            speech_dur = probe_duration(raw, ffprobe=ffprobe)
    elif personal and speech_dur > slot + 0.15:
        print(
            f"Full script {speech_dur:.1f}s > video {slot:.1f}s — "
            f"keeping Personal Voice at natural pace (pad/trim video only)",
            flush=True,
        )

    fit_speech_into_slot(
        raw,
        out_wav,
        video_dur,
        start_offset_sec=lead,
        ffprobe=ffprobe,
        spec=spec,
    )
    match_audio_duration(out_wav, video_dur, ffprobe=ffprobe)

    if not wav_is_audible(out_wav):
        return False
    print(
        f"Full-script narration OK ({speech_dur:.1f}s speech in {video_dur:.1f}s video)",
        flush=True,
    )
    if run_dir:
        stamp_full_narration_voice_metadata(
            run_dir, spec, capture_method=capture_method or "personal"
        )
    return True


def build_narration_for_clips(
    clips: list[Path],
    milestones: list[dict[str, Any]],
    out_wav: Path,
    *,
    voice: str = "Andrew",
    rate: int = 168,
    hold_indices: list[int] | None = None,
    narration_cues: dict[int, str | tuple[str, float]] | None = None,
    engine: str = "auto",
    spec: dict[str, Any] | None = None,
    run_dir: Path | None = None,
) -> bool:
    if not clips:
        return False
    try:
        ffprobe = find_ffprobe()
    except FileNotFoundError:
        return False

    spec = spec or {}
    default_hold_offset = hold_narrator_offset_sec(spec, None)
    cues: dict[int, str | tuple[str, float]] = dict(narration_cues or {})
    if not cues and hold_indices:
        for hi, ci in enumerate(hold_indices):
            if hi < len(milestones):
                cues[ci] = beat_narration_script(milestones[hi], spec)
    parts: list[Path] = []
    tmp = out_wav.parent / "_narr_parts"
    tmp.mkdir(parents=True, exist_ok=True)
    raw_dir = tmp / "_raw"
    raw_dir.mkdir(exist_ok=True)

    used_engine = pick_speech_engine(spec, run_dir)
    personal = resolve_personal_voice_name(spec, run_dir)
    if used_engine == "personal":
        print(f"Narrator engine: macOS Personal Voice only — “{personal or voice}” (no edge-tts fallback)")
    elif used_engine == "edge":
        print(f"Narrator voice: edge-tts ({_resolve_edge_voice(voice)}) — NOT your Personal Voice")
    else:
        print(f"Narrator voice: macOS say ({voice})")

    ref = resolve_reference_wav(spec, run_dir)
    if ref:
        print(f"Reference sample on disk: {ref} (used for Personal Voice setup; clone TTS coming later)")

    cue_labels = (spec or {}).get("_narrationCueLabels") or {}
    narrated_ok = 0

    for ci, clip in enumerate(clips):
        dur = probe_duration(clip, ffprobe=ffprobe)
        part = tmp / f"p_{ci:03d}.wav"
        if ci in cues:
            script, start_offset = _parse_cue(cues[ci], hold_narrator_offset_sec(spec, None))
            label = cue_labels.get(ci, f"clip_{ci}")
            raw = raw_dir / f"raw_{ci:03d}.wav"
            print(f"Narration cue {ci + 1}/{len(clips)}: {label}…", flush=True)
            plan_dir = Path(str(spec.get("_narrPlanDir") or tmp.parent / "_narr_plan"))
            planned = plan_wav_path(plan_dir, script, spec, used_engine)
            got_audio = planned.is_file() and planned.stat().st_size > 256
            if got_audio:
                shutil.copy2(planned, raw)
            elif script and speech_to_wav(
                script,
                raw,
                voice=voice,
                rate=rate,
                engine=used_engine,
                spec=spec,
                run_dir=run_dir,
            ):
                got_audio = True
            if got_audio:
                fit_speech_into_slot(
                    raw, part, dur, start_offset_sec=start_offset, ffprobe=ffprobe, spec=spec
                )
                if used_engine != "personal" and spec.get("narratorHumanize", True):
                    polish_voice_wav(part, spec)
                print(f"Narration OK: {label} ({dur:.1f}s slot, offset {start_offset:.1f}s)")
                narrated_ok += 1
            else:
                print(f"Narration FAILED (silence): {label} — check Personal Voice / edge-tts", file=sys.stderr)
                silence_wav(part, dur)
        else:
            silence_wav(part, dur)
        parts.append(part)

    lst = tmp / "list.txt"
    lst.write_text("".join(f"file '{p.resolve()}'\n" for p in parts), encoding="utf-8")
    if not concat_narration_parts(tmp, out_wav, spec):
        print("ERROR: failed to concat narration parts", file=sys.stderr)
        return False
    if narrated_ok == 0 and cues:
        print("ERROR: No narration audio was generated — video would be silent.", file=sys.stderr)
        return False
    print(f"Narration: {narrated_ok}/{len(cues)} cues with voice audio")
    if narrated_ok > 0 and run_dir and is_personal_narration(spec):
        method = str(spec.get("_lastCaptureMethod") or "say+mysay")
        write_narration_capture_record(
            run_dir,
            capture_method=method,
            voice_name=str(resolve_personal_voice_name(spec, run_dir) or voice),
            spec=spec,
        )
    return out_wav.is_file() and out_wav.stat().st_size > 4096 and narrated_ok > 0


def concat_narration_parts(parts_dir: Path, out_wav: Path, spec: dict[str, Any] | None = None) -> bool:
    """Rebuild narration.wav from p_*.wav (after narration-only part regen)."""
    parts = sorted(parts_dir.glob("p_*.wav"))
    if not parts:
        return False
    lst = parts_dir / "list.txt"
    lst.write_text("".join(f"file '{p.resolve()}'\n" for p in parts), encoding="utf-8")
    ffmpeg = find_ffmpeg()
    concat_af = "aresample=48000,asetpts=PTS-STARTPTS,aformat=channel_layouts=stereo"
    try:
        subprocess.run(
            [
                ffmpeg,
                "-y",
                "-f",
                "concat",
                "-safe",
                "0",
                "-i",
                str(lst),
                "-af",
                concat_af,
                "-c:a",
                "pcm_s16le",
                str(out_wav),
            ],
            check=True,
            capture_output=True,
            text=True,
        )
    except subprocess.CalledProcessError as exc:
        err = (exc.stderr or exc.stdout or str(exc))[:800]
        print(f"Narration concat failed: {err}", file=sys.stderr)
        return False
    return out_wav.is_file() and wav_is_audible(out_wav, min_mean_db=-58.0)


def resolve_natural_personal_narration_wav(
    work_dir: Path,
    spec: dict[str, Any] | None = None,
) -> Path | None:
    """Prefer uncropped Personal Voice capture; never return time-stretched .fit intermediates."""
    spec = flatten_narrator_personal_settings(spec or {})
    if not is_personal_narration(spec):
        return None
    raw = work_dir / "_full_narr_raw.wav"
    if raw.is_file() and wav_is_audible(raw, min_mean_db=-58.0):
        return raw
    narr = work_dir / "narration.wav"
    if narr.is_file() and wav_is_audible(narr, min_mean_db=-58.0):
        return narr
    return None


def remux_preview_with_narration(
    work_dir: Path,
    output_mp4: Path,
    *,
    spec: dict[str, Any] | None = None,
    volume: float | None = None,
    video: Path | None = None,
) -> bool:
    """Mux graded video + narration. Personal Voice: natural capture, pad/trim only."""
    spec = flatten_narrator_personal_settings(spec or {})
    final = video if video is not None else work_dir / "_final_video.mp4"
    narr_wav = work_dir / "narration.wav"
    parts_dir = work_dir / "_narr_parts"
    if parts_dir.is_dir() and sorted(parts_dir.glob("p_*.wav")):
        newest = max(p.stat().st_mtime for p in parts_dir.glob("p_*.wav"))
        if not narr_wav.is_file() or narr_wav.stat().st_mtime < newest - 1:
            print("Rebuilding narration.wav from _narr_parts…", flush=True)
            if not concat_narration_parts(parts_dir, narr_wav, spec):
                print("ERROR: narration parts did not concat to audible wav", file=sys.stderr)
                return False
    if not final.is_file():
        print(f"ERROR: missing graded video {final}", file=sys.stderr)
        return False

    personal_src = resolve_natural_personal_narration_wav(work_dir, spec)
    has_clip_parts = parts_dir.is_dir() and sorted(parts_dir.glob("p_*.wav"))
    if personal_src is not None and not has_clip_parts:
        ffprobe = find_ffprobe()
        video_dur = probe_duration(final, ffprobe=ffprobe)
        lead = float(spec.get("fullNarrationLeadSec", 0.6))
        fit_speech_into_slot(
            personal_src,
            narr_wav,
            video_dur,
            start_offset_sec=lead,
            ffprobe=ffprobe,
            spec=spec,
        )
        match_audio_duration(narr_wav, video_dur, ffprobe=ffprobe)
        print(
            f"Personal Voice remux: {probe_duration(personal_src, ffprobe=ffprobe):.1f}s "
            f"natural → {video_dur:.1f}s video (no atempo)",
            flush=True,
        )
    elif not narr_wav.is_file() or not wav_is_audible(narr_wav, min_mean_db=-58.0):
        print(f"ERROR: missing or silent narration {narr_wav}", file=sys.stderr)
        return False

    vol = volume if volume is not None else float(spec.get("narratorVolume", 1.25))
    output_mp4.parent.mkdir(parents=True, exist_ok=True)
    mux_narration_video_only(
        final, narr_wav, output_mp4, volume=vol, spec=spec
    )
    if not output_mp4.is_file() or output_mp4.stat().st_size < 1024:
        return False
    print(f"Remuxed preview with narration → {output_mp4}")
    return True


def narration_mux_loudnorm(spec: dict[str, Any] | None) -> bool:
    """loudnorm on mux can flatten Personal Voice — off by default for personal engine."""
    spec = flatten_narrator_personal_settings(spec or {})
    if spec.get("narratorPersonalLoudnorm") is not None:
        return bool(spec["narratorPersonalLoudnorm"])
    eng = str(spec.get("narratorEngine", "")).lower()
    if eng in ("personal", "personal-voice") or spec.get("narratorRequirePersonal"):
        return False
    return bool(spec.get("loudnorm", True))


def mp4_has_audio_stream(path: Path) -> bool:
    """True if the MP4 has at least one audio stream (finalize gate)."""
    if not path.is_file() or path.stat().st_size < 1024:
        return False
    try:
        probe = find_ffprobe()
        r = subprocess.run(
            [
                probe,
                "-v",
                "error",
                "-select_streams",
                "a",
                "-show_entries",
                "stream=codec_type",
                "-of",
                "csv=p=0",
                str(path),
            ],
            capture_output=True,
            text=True,
            check=True,
        )
        return "audio" in (r.stdout or "")
    except (subprocess.CalledProcessError, FileNotFoundError):
        return False


def require_narration_before_finalize(spec: dict[str, Any] | None) -> bool:
    spec = flatten_narrator_personal_settings(spec or {})
    if not spec.get("narratorEnabled", True):
        return False
    return bool(spec.get("requireNarrationBeforeFinalize", True))


def mux_narration_video_only(
    video: Path,
    narration: Path,
    output: Path,
    *,
    volume: float = 1.0,
    loudnorm: bool = True,
    spec: dict[str, Any] | None = None,
) -> None:
    if spec is not None:
        loudnorm = narration_mux_loudnorm(spec)
    ffprobe = find_ffprobe()
    video_dur = probe_duration(video, ffprobe=ffprobe)
    match_audio_duration(narration, video_dur, ffprobe=ffprobe)

    vol = max(0.2, min(2.0, volume))
    audio_fx = f"[1:a]aresample=48000,aformat=channel_layouts=stereo,volume={vol}"
    if loudnorm:
        audio_fx += ",loudnorm=I=-16:TP=-1.5:LRA=11"
    audio_fx += "[n]"
    subprocess.run(
        [
            find_ffmpeg(),
            "-y",
            "-i",
            str(video),
            "-i",
            str(narration),
            "-filter_complex",
            audio_fx,
            "-map",
            "0:v",
            "-map",
            "[n]",
            "-c:v",
            "copy",
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            "-t",
            f"{video_dur:.3f}",
            str(output),
        ],
        check=True,
        capture_output=True,
    )
