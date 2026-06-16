#!/usr/bin/env python3
"""Rule-first validators for wizard tabs.

These are the initial Phase-0 brains (no ONNX).

They produce a standardized `TabBrainResult` so planners can decide whether
to keep the checklist update, reopen items, and/or request follow-up rows.
"""

from __future__ import annotations

from typing import Dict, Iterable, List

import os
from pathlib import Path

import numpy as np

import wizard_checklist as wc
from world_tab_brains.model_paths import brain_model_filename, resolve_brain_model_path

from .brain_types import TabBrainInput, TabBrainResult, Violation, ViolationSeverity

_ONNX_SESSIONS: dict[str, object] = {}
_ONNX_MANIFESTS: dict[str, dict] = {}
_TAB_ENV_SUFFIX = {
    "terrain": "TERRAIN",
    "surface-content": "SURFACE",
    "caves": "CAVES",
    "mazes": "MAZES",
    "interior-content": "INTERIOR",
    "atmosphere": "ATMOSPHERE",
    "music": "MUSIC",
    "video": "VIDEO",
}


def _base_result(inp: TabBrainInput) -> TabBrainResult:
    coverage = {item.id: 1.0 if item.done else 0.0 for item in inp.checklist_after_proposed}
    return TabBrainResult(
        passed=True,
        confidence=0.5,
        task_validity_score=0.5,
        outcome_validity_score=0.5,
        coverage_by_checklist_id=coverage,
        violations=[],
        followup_additions=[],
        reopen_checklist_ids=[],
        audit_trace_id=None,
        evidence_refs=[],
        extra={"mode": "rule-only"},
    )


def _keywords_present(decision: str, keywords: Iterable[str]) -> bool:
    low = (decision or "").lower()
    for k in keywords:
        if k.lower() not in low:
            return False
    return True


def _decision_length_ok(decision: str, *, min_len: int = 18) -> bool:
    return len((decision or "").strip()) >= min_len


def _reopen_if_weak(
    *,
    res: TabBrainResult,
    item_id: str,
    item_label: str,
    decision: str,
    required_keywords: List[str] | None = None,
) -> None:
    required_keywords = required_keywords or []
    ok_len = _decision_length_ok(decision)
    ok_kw = True
    if required_keywords:
        ok_kw = _keywords_present(decision, required_keywords)
    if ok_len and ok_kw:
        return

    res.reopen_checklist_ids.append(item_id)
    res.violations.append(
        Violation(
            id=f"weak_{item_id}",
            checklist_id=item_id,
            severity=ViolationSeverity.WARNING,
            message=f"Checklist [{item_id}] “{item_label}” looks missing fields.",
            hint="Include concrete implementable fields (ids/units/arrays) for this checklist item.",
        )
    )


def _onnx_enabled() -> bool:
    return os.environ.get("WORLD_TAB_BRAIN_ONNX", "1").strip().lower() in ("1", "true", "yes", "on")


def _onnx_threshold() -> float:
    raw = os.environ.get("WORLD_TAB_BRAIN_ONNX_THRESHOLD", "0.70").strip()
    try:
        return float(raw)
    except ValueError:
        return 0.70


def _tab_onnx_enabled(tab_id: str) -> bool:
    suffix = _TAB_ENV_SUFFIX.get(tab_id, tab_id.upper().replace("-", "_"))
    key = f"WORLD_TAB_BRAIN_{suffix}_ONNX_ENABLED"
    return os.environ.get(key, "1").strip().lower() in ("1", "true", "yes", "on")


def _tab_onnx_threshold(tab_id: str) -> float:
    suffix = _TAB_ENV_SUFFIX.get(tab_id, tab_id.upper().replace("-", "_"))
    key = f"WORLD_TAB_BRAIN_{suffix}_ONNX_THRESHOLD"
    raw = os.environ.get(key, "").strip()
    if not raw:
        return _onnx_threshold()
    try:
        return float(raw)
    except ValueError:
        return _onnx_threshold()


def _models_dir() -> Path:
    return Path(__file__).resolve().parent / "models"


def _override_model_path_for_tab(tab_id: str) -> Path | None:
    suffix = _TAB_ENV_SUFFIX.get(tab_id, tab_id.upper().replace("-", "_"))
    # Preferred per-tab override
    per_tab_key = f"WORLD_TAB_BRAIN_{suffix}_ONNX_PATH"
    raw = os.environ.get(per_tab_key, "").strip()
    if not raw:
        # Back-compat with early surface-only env var
        if tab_id == "surface-content":
            raw = os.environ.get("WORLD_TAB_BRAIN_SURFACE_ONNX_PATH", "").strip()
    if not raw:
        return None
    p = Path(raw).expanduser()
    if not p.is_absolute():
        p = (_models_dir() / p).resolve()
    return p


def _manifest_for_tab(tab_id: str) -> dict | None:
    if tab_id in _ONNX_MANIFESTS:
        return _ONNX_MANIFESTS[tab_id]
    p = _models_dir() / f"{tab_id}_brain.manifest.json"
    if not p.is_file():
        return None
    try:
        import json

        doc = json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return None
    _ONNX_MANIFESTS[tab_id] = doc
    return doc


def _onnx_session_for_tab(tab_id: str):
    if tab_id in _ONNX_SESSIONS:
        return _ONNX_SESSIONS[tab_id]
    model_path = _override_model_path_for_tab(tab_id)
    if model_path is None:
        manifest = _manifest_for_tab(tab_id)
        if manifest:
            model_path = Path(str(manifest.get("modelPath") or ""))
        if model_path is None or not model_path.is_file():
            model_path = resolve_brain_model_path(_models_dir(), tab_id)
    if model_path is None or not model_path.is_file():
        return None
    try:
        import onnxruntime as ort

        sess = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
    except Exception:
        return None
    _ONNX_SESSIONS[tab_id] = sess
    return sess


def _onnx_predict_pass(tab_id: str, item_id: str, item_label: str, decision: str) -> tuple[int | None, float, str | None]:
    sess = _onnx_session_for_tab(tab_id)
    if sess is None:
        return None, 0.0, "no_model"
    prompt = " | ".join(
        [
            f"tab:{tab_id}",
            f"checklist:{item_id}",
            f"q:Answer checklist [{item_id}] — {item_label}",
            f"a:{decision}",
        ]
    )
    try:
        # Model expects tensor(string) [N,1]
        x = np.array([[prompt]], dtype=object)
        out = sess.run(None, {"input_text": x})
        label = int(out[0][0])
        conf = 0.0
        # output_probability: seq(map(label->prob))
        if len(out) > 1 and out[1]:
            prob_map = out[1][0] or {}
            conf = float(prob_map.get(label, 0.0))
        return label, conf, None
    except Exception as exc:
        return None, 0.0, str(exc)


def _apply_onnx_assist(tab_id: str, inp: TabBrainInput, res: TabBrainResult) -> None:
    if not _onnx_enabled():
        return
    if not _tab_onnx_enabled(tab_id):
        return
    threshold = _tab_onnx_threshold(tab_id)
    onnx_seen = 0
    onnx_failures = 0
    for item in inp.checklist_after_proposed:
        if not item.done:
            continue
        label, conf, err = _onnx_predict_pass(tab_id, item.id, item.label, item.decision)
        if err:
            onnx_failures += 1
            continue
        onnx_seen += 1
        # label 0 => reopen, label 1 => pass
        if label == 0 and conf >= threshold:
            _reopen_if_weak(
                res=res,
                item_id=item.id,
                item_label=item.label,
                decision=item.decision,
                required_keywords=[],
            )
    if onnx_seen:
        res.extra["onnxAssist"] = True
        res.extra["onnxThreshold"] = threshold
        res.extra["onnxEvaluatedItems"] = onnx_seen
        override = _override_model_path_for_tab(tab_id)
        if override:
            res.extra["onnxModelSource"] = "env_override"
            res.extra["onnxModelPath"] = str(override)
        else:
            res.extra["onnxModelSource"] = "manifest"
    if onnx_failures:
        res.extra["onnxErrors"] = onnx_failures


def _dedupe_reopen_ids(res: TabBrainResult) -> None:
    if not res.reopen_checklist_ids:
        return
    seen: set[str] = set()
    out: list[str] = []
    for iid in res.reopen_checklist_ids:
        if iid in seen:
            continue
        seen.add(iid)
        out.append(iid)
    res.reopen_checklist_ids = out


def _generic_brain_with_keyword_map(inp: TabBrainInput, keyword_map: Dict[str, List[str]]) -> TabBrainResult:
    res = _base_result(inp)
    for item in inp.checklist_after_proposed:
        if not item.done:
            continue
        required = keyword_map.get(item.id, [])
        _reopen_if_weak(
            res=res,
            item_id=item.id,
            item_label=item.label,
            decision=item.decision,
            required_keywords=required,
        )
    _apply_onnx_assist(inp.tab_id, inp, res)
    _dedupe_reopen_ids(res)
    res.passed = len(res.reopen_checklist_ids) == 0
    if res.passed:
        res.confidence = 0.8
        res.task_validity_score = 0.85
        res.outcome_validity_score = 0.8
    else:
        res.confidence = 0.25
        res.task_validity_score = 0.25
        res.outcome_validity_score = 0.0
    return res


def surface_content_brain(inp: TabBrainInput) -> TabBrainResult:
    """Rule-first Surface Content validator (no ONNX dependency)."""
    res = _base_result(inp)
    for item in inp.checklist_after_proposed:
        if not item.done:
            continue
        if wc.decision_looks_weak(item.id, item.decision):
            _reopen_if_weak(
                res=res,
                item_id=item.id,
                item_label=item.label,
                decision=item.decision,
                required_keywords=[],
            )

    _apply_onnx_assist("surface-content", inp, res)
    _dedupe_reopen_ids(res)

    res.passed = len(res.reopen_checklist_ids) == 0
    if not res.passed:
        res.confidence = 0.2
        res.task_validity_score = 0.2
        res.outcome_validity_score = 0.0
    return res


def terrain_brain(inp: TabBrainInput) -> TabBrainResult:
    kw = {
        "scope": ["mountains", "water", "labyrinth", "playable"],
        "props": ["scatter", "props", "trail", "budget"],
        "npcs": ["npc", "quest", "dialog"],
        "enemies": ["enemy", "combat", "telegraph"],
        "puzzles": ["lever", "terminal", "interactable", "tutorial"],
        "terrain": ["mountain", "water", "labyrinth"],
        "speed": ["fast", "quality", "tier"],
        "tech_jump": ["jump", "meters", "platform"],
        "tech_seams": ["seam", "tile", "height"],
        "tech_navmesh": ["navmesh", "walkable", "jump-only"],
        "tech_spawn": ["spawn", "respawn", "fall"],
        "tech_seed": ["fixed", "random", "seed"],
        "tech_collision": ["trigger", "kill", "collider"],
        "tech_perf": ["perf", "budget", "fps", "reduce"],
    }
    return _generic_brain_with_keyword_map(inp, kw)


def caves_brain(inp: TabBrainInput) -> TabBrainResult:
    kw = {
        "cave_scope": ["entrances", "routes", "depth", "nav"],
        "entrances": ["surface", "entrance", "portal", "cave"],
        "route_style": ["lava", "tube", "rooms", "hybrid"],
        "room_graph": ["graph", "nodes", "chamber", "rooms"],
        "depth_budget": ["depth", "budget", "meters"],
        "navmesh": ["walkable", "nav", "floors"],
        "atmosphere_hook": ["lighting", "mood", "fog"],
        "generation_passes": ["passes", "navmesh", "collider", "enable"],
    }
    return _generic_brain_with_keyword_map(inp, kw)


def mazes_brain(inp: TabBrainInput) -> TabBrainResult:
    kw = {
        "maze_scope": ["maze", "grid", "terrain", "size"],
        "grid_topology": ["grid", "topology", "cells", "policy"],
        "entrances_exits": ["entrances", "exits", "portal", "ties"],
        "dead_ends": ["dead-end", "loop", "policy"],
        "landmarks": ["landmark", "vista", "rooms"],
        "surface_blend": ["blend", "terrain", "tiles"],
        "nav_intent": ["navigation", "route", "player"],
    }
    return _generic_brain_with_keyword_map(inp, kw)


def interior_content_brain(inp: TabBrainInput) -> TabBrainResult:
    kw = {
        "interior_scope": ["interior", "caves", "dungeon", "maze"],
        "interior_npcs": ["npc", "dialog", "placement"],
        "interior_enemies": ["enemy", "patrol", "spawner", "route"],
        "interior_props": ["props", "interactable", "loot"],
        "quest_ties": ["quest", "surface", "id"],
        "quest_items": ["quest", "key", "item", "location"],
        "player_gear": ["player", "gear", "item"],
        "agent_gear": ["agent", "companion", "gear"],
        "shops_loot": ["shop", "loot", "table", "inventory"],
        "placement": ["zoneId", "worldPosition", "x", "y", "z"],
    }
    return _generic_brain_with_keyword_map(inp, kw)


def atmosphere_brain(inp: TabBrainInput) -> TabBrainResult:
    kw = {
        "water": ["water", "river", "pool"],
        "sky_time": ["sun", "moon", "time"],
        "lighting": ["lighting", "fog"],
        "vfx": ["vfx", "particles"],
        "audio_hooks": ["audio", "mood"],
        "cutscene_triggers": ["cutscene", "trigger", "volume"],
        "quest_story_hooks": ["quest", "story", "hooks", "beats"],
    }
    return _generic_brain_with_keyword_map(inp, kw)


def music_brain(inp: TabBrainInput) -> TabBrainResult:
    kw = {
        "genre": ["pop", "trap", "bpm"],
        "mood": ["moody", "confident", "vulnerable"],
        "working_title": ["loop", "midnight", "working"],
        "bpm": ["bpm"],
        "key": ["minor", "auto"],
        "scale": ["major", "minor"],
        "instrumental_style": ["808", "pad", "hi-hats"],
        "instrumental_prompt": ["no vocals", "suno", "genre"],
        "lyrics_text": ["hook", "\n"],
        "vocal_style": ["rap", "sing", "melodic"],
        "performance_vibe": ["bedroom", "stadium"],
        "autotune_intent": ["autotune", "polish"],
        "mix_vibe": ["lu", "reference", "808"],
        "music_video_intent": ["yes", "no", "handoff", "clip"],
    }
    return _generic_brain_with_keyword_map(inp, kw)


def video_brain(inp: TabBrainInput) -> TabBrainResult:
    # Video uses AI Director flows, but ONNX assist can still validate checklist-style answers.
    kw = {
        "platform": ["youtube", "shorts", "tiktok", "reel", "platform"],
        "aspect_ratio": ["16:9", "9:16", "aspect", "framing"],
        "length": ["seconds", "minutes", "runtime", "target"],
        "audience": ["audience", "recruiter", "studio", "viewer"],
        "success_metric": ["metric", "retention", "watch", "conversion"],
        "working_title": ["title", "logline"],
        "cta": ["call", "action", "subscribe", "follow"],
        "chapters_endscreen": ["chapters", "end-screen", "act"],
        "hook": ["hook", "0-3", "opening"],
        "narrative_arc": ["act", "arc", "story"],
        "emotional_peak": ["peak", "hero", "moment"],
        "payoff": ["payoff", "takeaway"],
        "stakes": ["stakes", "tension"],
    }
    return _generic_brain_with_keyword_map(inp, kw)


TAB_RULE_VALIDATORS: Dict[str, object] = {
    "terrain": terrain_brain,
    "surface-content": surface_content_brain,
    "caves": caves_brain,
    "mazes": mazes_brain,
    "interior-content": interior_content_brain,
    "atmosphere": atmosphere_brain,
    "music": music_brain,
    "video": video_brain,
}

