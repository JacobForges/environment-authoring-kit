#!/usr/bin/env python3
"""Automated pipeline: tabs 1→6 via AI Responder."""
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from envkit_paths import atomic_write_text

import build_planner as terrain_planner
import wizard_registry as reg
from wizard_registry import PIPELINE_REL, PIPELINE_TAB_IDS
from wizard_tabs import GENERIC_PLANNERS

DEFAULT_PRESETS: dict[str, str] = {
    "terrain": "medium",
    "surface-content": "full_mainscene",
    "caves": "demo_cave",
    "mazes": "surface_maze",
    "interior-content": "cave_population",
    "atmosphere": "florida_dusk",
}


def _utc() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _path(hub: Path) -> Path:
    return hub.expanduser().resolve() / PIPELINE_REL


def read_pipeline(hub: Path) -> dict[str, Any] | None:
    p = _path(hub)
    if not p.is_file():
        return None
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return None


def _write(hub: Path, doc: dict[str, Any]) -> dict[str, Any]:
    p = _path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    doc["updatedUtc"] = _utc()
    atomic_write_text(p, json.dumps(doc, indent=2) + "\n")
    return doc


def public_status(hub: Path) -> dict[str, Any]:
    doc = read_pipeline(hub)
    if not doc:
        return {"active": False, "currentTab": None, "completedTabs": [], "error": None, "focusTab": None}
    return {
        "active": doc.get("status") == "running",
        "currentTab": doc.get("currentTab"),
        "focusTab": doc.get("currentTab"),
        "currentIndex": doc.get("currentIndex", 0),
        "completedTabs": doc.get("completedTabs") or [],
        "error": doc.get("error"),
        "autoApprove": doc.get("autoApprove", True),
        "presets": doc.get("presets") or DEFAULT_PRESETS,
        "status": doc.get("status"),
    }


def start_pipeline(hub: Path, presets: dict[str, str] | None = None, auto_approve: bool = True) -> dict[str, Any]:
    merged = {**DEFAULT_PRESETS, **(presets or {})}
    doc = {
        "version": 1,
        "status": "running",
        "currentIndex": 0,
        "currentTab": PIPELINE_TAB_IDS[0],
        "completedTabs": [],
        "presets": merged,
        "autoApprove": auto_approve,
        "error": None,
        "startedUtc": _utc(),
    }
    _write(hub, doc)
    return public_status(hub)


def cancel_pipeline(hub: Path) -> dict[str, Any]:
    doc = read_pipeline(hub)
    if doc:
        doc["status"] = "cancelled"
        doc["error"] = None
        _write(hub, doc)
    return public_status(hub)


def _advance(doc: dict[str, Any]) -> bool:
    idx = int(doc.get("currentIndex", 0))
    tab = PIPELINE_TAB_IDS[idx]
    completed = doc.setdefault("completedTabs", [])
    if tab not in completed:
        completed.append(tab)
    nxt = idx + 1
    if nxt >= len(PIPELINE_TAB_IDS):
        doc["status"] = "done"
        doc["currentTab"] = None
        doc["currentIndex"] = nxt
        return True
    doc["currentIndex"] = nxt
    doc["currentTab"] = PIPELINE_TAB_IDS[nxt]
    return False


def _kickoff_message(tab_id: str, preset: str) -> str:
    if tab_id == "terrain":
        return terrain_planner._preset_or_raise(preset)["kickoff"]
    if tab_id == "surface-content":
        from content_planner import _preset_or_raise

        return _preset_or_raise(preset)["kickoff"]
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].config.presets[preset]["kickoff"]
    raise RuntimeError(f"No kickoff for tab {tab_id}")


def _advance_when_applied(hub: Path, doc: dict[str, Any], tab_id: str, sess: dict[str, Any]) -> dict[str, Any]:
    """Advance pipeline only after Unity applied + saved the tab just approved."""
    from wizard_tab_apply import is_apply_busy, notify_tab_approved, public_status as apply_status
    from wizard_tab_apply import tab_apply_satisfied

    if not tab_apply_satisfied(hub, tab_id):
        if not is_apply_busy(hub):
            notify_tab_approved(hub, tab_id)
        from wizard_manifest import refresh_manifest

        refresh_manifest(hub)
        return {
            "ok": True,
            "waiting": True,
            "waitingApply": True,
            "tabId": tab_id,
            "focusTab": tab_id,
            "status": public_status(hub),
            "session": sess,
            "apply": apply_status(hub),
        }

    done_all = _advance(doc)
    _write(hub, doc)
    from wizard_manifest import refresh_manifest

    refresh_manifest(hub)
    return {
        "ok": True,
        "tabComplete": tab_id,
        "pipelineDone": done_all,
        "focusTab": doc.get("currentTab"),
        "status": public_status(hub),
        "session": sess,
        "apply": apply_status(hub),
    }


def _terrain_auto_gates(hub: Path, doc: dict[str, Any], sess: dict[str, Any]) -> dict[str, Any]:
    if not doc.get("autoApprove"):
        return sess
    phase = sess.get("phase")
    if phase == "awaiting_concept_approval":
        sess = terrain_planner.approve_concept(hub, True, "")
    elif phase == "awaiting_research_approval":
        sess = terrain_planner.approve_research(hub, True, None)
    elif phase == "awaiting_plan_approval":
        sess = terrain_planner.approve_plan(hub, True)
    return sess


def pipeline_step(hub: Path) -> dict[str, Any]:
    doc = read_pipeline(hub)
    if not doc or doc.get("status") != "running":
        return {"ok": False, "message": "No active pipeline", "status": public_status(hub)}

    tab_id = doc.get("currentTab") or PIPELINE_TAB_IDS[0]
    preset = (doc.get("presets") or DEFAULT_PRESETS).get(tab_id, "")

    try:
        if reg.session_busy(hub, tab_id):
            return {
                "ok": True,
                "waiting": True,
                "tabId": tab_id,
                "focusTab": tab_id,
                "status": public_status(hub),
                "session": reg.public_session(hub, tab_id),
            }

        sess = reg.public_session(hub, tab_id)
        phase = sess.get("phase") or "idle"

        if phase in ("idle", "cancelled", None):
            reg.start_session(hub, tab_id, _kickoff_message(tab_id, preset))
            sess = reg.public_session(hub, tab_id)

        if tab_id == "terrain":
            sess = _terrain_auto_gates(hub, doc, sess)
            phase = sess.get("phase")
            if phase in ("awaiting_concept_approval", "awaiting_research_approval", "awaiting_plan_approval"):
                sess = reg.public_session(hub, tab_id)
                phase = sess.get("phase")

        if phase == "awaiting_approval" and doc.get("autoApprove") and tab_id != "terrain":
            sess = reg.approve(hub, tab_id, True, "")
            phase = sess.get("phase")

        if reg.tab_complete(hub, tab_id):
            return _advance_when_applied(hub, doc, tab_id, sess)

        if phase == "finalized" and tab_id == "terrain":
            return _advance_when_applied(hub, doc, tab_id, sess)

        result = reg.auto_respond_step(hub, tab_id, preset)
        sess = result.get("session") or reg.public_session(hub, tab_id)

        if result.get("waiting") or reg.session_busy(hub, tab_id):
            return {
                "ok": True,
                "waiting": True,
                "tabId": tab_id,
                "focusTab": tab_id,
                "status": public_status(hub),
                "session": sess,
            }

        if tab_id == "terrain":
            sess = _terrain_auto_gates(hub, doc, sess)

        phase = sess.get("phase")
        if phase == "awaiting_approval" and doc.get("autoApprove") and tab_id != "terrain":
            sess = reg.approve(hub, tab_id, True, "")

        if reg.tab_complete(hub, tab_id):
            return _advance_when_applied(hub, doc, tab_id, sess)

        return {
            "ok": True,
            "tabId": tab_id,
            "focusTab": tab_id,
            "status": public_status(hub),
            "session": sess,
        }
    except Exception as exc:
        doc["status"] = "error"
        doc["error"] = str(exc)
        _write(hub, doc)
        return {"ok": False, "error": str(exc), "focusTab": tab_id, "status": public_status(hub)}
