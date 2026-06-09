#!/usr/bin/env python3
"""
Personal Voice Ladder — phased post-capture pipeline.

  algorithm   → antiStutter, lowClean, speakerSafe
  character   → character, casualTone, warmth
  tone        → naturalPitch, phraseFall, ttsSmooth (skipped on dry master pass)
  polish      → deClick, autoEq25, midTreble, deEss, segmentVolume (spatialHarmony skipped on dry master)
  auditor     → measure + auto-fix (levels, tails, clipping)
  static      → gapDehiss, staticClean, gapDehiss (silence-only hiss tame)
  finalizer   → loudnorm + limiter

Docs: PERSONAL_VOICE_NARRATION.md, VOICE_HELPERS.md
Configure: ~/Hub/Library/EnvironmentKit/DemoRecapApproved/ApprovedCards.json

  "voiceLadder": { "enabled": true, "phases": { ... } },
  "voiceHelpers": { "preset": "ladderDocumentary", "sayRate": 186, ... }
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any

_TOOLS = Path(__file__).resolve().parent

# Phase name → list of helper ids (auditor/finalizer are special)
DEFAULT_LADDER_PHASES: dict[str, Any] = {
    "algorithm": ["antiStutter", "lowClean", "speakerSafe"],
    "character": ["character", "casualTone", "warmth"],
    "tone": ["naturalPitch", "phraseFall", "ttsSmooth"],
    "polish": ["deClick", "autoEq25", "midTreble", "spatialHarmony", "deEss", "segmentVolume"],
    "auditor": True,
    "static": ["gapDehiss", "staticClean", "gapDehiss"],
    "finalizer": ["finalizer"],
}


def _load_helpers():
    path = _TOOLS / "personal-voice-helpers.py"
    spec = importlib.util.spec_from_file_location("pvh", path)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def _normalize_ladder_phases(phases: dict[str, Any]) -> dict[str, Any]:
    """Ensure static helpers run in dedicated phase before finalizer."""
    out = dict(phases)
    polish = out.get("polish")
    if isinstance(polish, list):
        out["polish"] = [h for h in polish if h not in ("staticClean", "hissGate", "grainPull", "synthHissCut", "noiseFloor", "staticSeal")]
    fin = out.get("finalizer")
    if isinstance(fin, list):
        static_helpers = {"staticClean", "hissGate", "grainPull", "synthHissCut", "noiseFloor", "staticSeal"}
        out["finalizer"] = [h for h in fin if h not in static_helpers]
        if not out["finalizer"]:
            out["finalizer"] = ["finalizer"]
    if "static" not in out:
        out["static"] = list(DEFAULT_LADDER_PHASES["static"])
    elif isinstance(out.get("static"), list) and "gapDehiss" not in out["static"]:
        out["static"] = ["gapDehiss", *out["static"], "gapDehiss"]
    return out


def _ladder_cfg(spec: dict[str, Any]) -> dict[str, Any]:
    vh = spec.get("voiceHelpers") or {}
    if not isinstance(vh, dict):
        vh = {}
    ladder = spec.get("voiceLadder") or vh.get("voiceLadder") or {}
    if not isinstance(ladder, dict):
        ladder = {}
    return ladder


def resolve_ladder_phases(spec: dict[str, Any]) -> dict[str, Any] | None:
    """Return phase map if ladder mode is on, else None."""
    ladder = _ladder_cfg(spec)
    vh = spec.get("voiceHelpers") or {}
    preset = ""
    if isinstance(vh, dict):
        preset = str(vh.get("preset", "")).lower()
    if ladder.get("enabled") is True or preset in (
        "ladder",
        "ladderdocumentary",
        "ladderdocumentary",
        "ladderDocumentary",
    ):
        phases = ladder.get("phases")
        if isinstance(phases, dict) and phases:
            return _normalize_ladder_phases(phases)
        return dict(DEFAULT_LADDER_PHASES)
    return None


def apply_voice_ladder(wav: Path, spec: dict[str, Any] | None = None) -> dict[str, Any]:
    """
    Run ladder phases in order. Returns summary {phases, helpers, auditor}.
    """
    spec = dict(spec or {})
    phases = resolve_ladder_phases(spec)
    if not phases:
        h = _load_helpers()
        applied = h.apply_voice_helpers(wav, spec)
        return {"mode": "chain", "helpers": applied}

    h = _load_helpers()
    summary: dict[str, Any] = {"mode": "ladder", "phases": [], "helpers": [], "auditor": {}}
    order = ladder_phase_order(phases)

    for phase_name in order:
        content = phases.get(phase_name)
        if content is None:
            continue
        print(f"Voice ladder · {phase_name}…", flush=True)
        phase_helpers: list[str] = []

        if phase_name == "auditor":
            report = h.run_voice_auditor(wav, spec)
            summary["auditor"] = report
            if report.get("fixes_applied"):
                phase_helpers.append("auditor-fix")
            summary["phases"].append(
                {"name": phase_name, "helpers": phase_helpers, "report": report.get("status")}
            )
            continue

        if phase_name == "finalizer":
            chain = content if isinstance(content, list) else ["finalizer"]
        elif isinstance(content, list):
            chain = content
        else:
            continue

        for name in chain:
            fn = h.HELPER_APPLY.get(name)
            if not fn:
                print(f"  skip unknown helper: {name}", file=sys.stderr)
                continue
            try:
                fn(wav, spec)
                phase_helpers.append(name)
                summary["helpers"].append(name)
            except Exception as exc:
                print(f"  {name} failed: {exc}", file=sys.stderr)

        summary["phases"].append({"name": phase_name, "helpers": phase_helpers})

    print(
        "Voice ladder done: "
        + " → ".join(f"{p['name']}({','.join(p['helpers']) or 'qc'})" for p in summary["phases"]),
        flush=True,
    )
    return summary


def ladder_phase_order(phases: dict[str, Any]) -> list[str]:
    preferred = ("algorithm", "character", "tone", "polish", "auditor", "static", "finalizer")
    out = [k for k in preferred if k in phases]
    for k in phases:
        if k not in out:
            out.append(k)
    return out


def print_ladder_help() -> None:
    print("Personal Voice Ladder (phased pipeline)\n")
    for name, helpers in DEFAULT_LADDER_PHASES.items():
        if name == "auditor":
            print(f"  {name}: QC + auto-fix")
        else:
            print(f"  {name}: {', '.join(helpers) if isinstance(helpers, list) else helpers}")
    print("\nSet voiceHelpers.preset to ladderDocumentary or voiceLadder.enabled: true")


if __name__ == "__main__":
    print_ladder_help()
