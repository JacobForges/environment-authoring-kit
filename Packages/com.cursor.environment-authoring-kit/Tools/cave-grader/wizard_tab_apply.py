#!/usr/bin/env python3
"""Per-tab Unity apply queue — each wizard tab applies + saves before the next."""
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from envkit_paths import atomic_write_text

APPLY_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildWizardTabApply.json")

TAB_APPLY_STEP: dict[str, str] = {
    "terrain": "terrain_build",
    "surface-content": "surface",
    "caves": "caves",
    "mazes": "mazes",
    "interior-content": "interior",
    "atmosphere": "atmosphere",
}

_BUSY_STATUSES = frozenset({"pending", "running"})


def _utc() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def apply_path(hub: Path) -> Path:
    return hub.expanduser().resolve() / APPLY_REL


def _empty_doc() -> dict[str, Any]:
    return {"version": 2, "current": None, "queue": [], "completed": []}


def read_apply(hub: Path) -> dict[str, Any]:
    p = apply_path(hub)
    if not p.is_file():
        return _empty_doc()
    try:
        doc = json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return _empty_doc()
    doc.setdefault("version", 2)
    doc.setdefault("queue", [])
    doc.setdefault("completed", [])
    return doc


def _write(hub: Path, doc: dict[str, Any]) -> None:
    p = apply_path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    doc["updatedUtc"] = _utc()
    atomic_write_text(p, json.dumps(doc, indent=2) + "\n")


def _item_for_tab(tab_id: str) -> dict[str, Any]:
    step = TAB_APPLY_STEP[tab_id]
    phase = "waiting_terrain" if tab_id == "terrain" else step
    if tab_id == "terrain":
        phase = "terrain_build"
    return {
        "tabId": tab_id,
        "applyStep": step,
        "status": "pending",
        "phase": phase,
        "requestId": _utc(),
        "requestedUtc": _utc(),
        "updatedUtc": _utc(),
        "error": None,
        "completedUtc": None,
    }


def _tab_in_flight(doc: dict[str, Any], tab_id: str) -> bool:
    cur = doc.get("current")
    if isinstance(cur, dict) and cur.get("tabId") == tab_id:
        if cur.get("status") in _BUSY_STATUSES:
            return True
        if cur.get("phase") == "waiting_terrain":
            return True
    for q in doc.get("queue") or []:
        if isinstance(q, dict) and q.get("tabId") == tab_id and q.get("status") in _BUSY_STATUSES:
            return True
    return False


def notify_tab_approved(hub: Path, tab_id: str) -> dict[str, Any] | None:
    """Queue Unity apply when a pipeline tab brief is approved (any path — manual or auto)."""
    if tab_id not in TAB_APPLY_STEP:
        return None

    doc = read_apply(hub)
    if tab_apply_satisfied(hub, tab_id):
        return doc.get("current")

    if _tab_in_flight(doc, tab_id):
        return doc.get("current")

    item = _item_for_tab(tab_id)
    cur = doc.get("current")
    busy = False
    if isinstance(cur, dict):
        st = cur.get("status")
        ph = cur.get("phase")
        busy = st in _BUSY_STATUSES or ph == "waiting_terrain"

    if busy:
        doc.setdefault("queue", []).append(item)
    else:
        doc["current"] = item

    _write(hub, doc)
    try:
        from build_planner import _activate_unity_editor

        _activate_unity_editor()
    except Exception:
        pass
    return item


def is_apply_busy(hub: Path) -> bool:
    doc = read_apply(hub)
    cur = doc.get("current")
    if isinstance(cur, dict):
        if cur.get("status") in _BUSY_STATUSES:
            return True
        if cur.get("phase") == "waiting_terrain":
            return True
    return bool(doc.get("queue"))


def tab_apply_satisfied(hub: Path, tab_id: str) -> bool:
    doc = read_apply(hub)
    for entry in doc.get("completed") or []:
        if not isinstance(entry, dict):
            continue
        if entry.get("tabId") == tab_id and entry.get("status") == "done":
            return True
    cur = doc.get("current")
    if (
        isinstance(cur, dict)
        and cur.get("tabId") == tab_id
        and cur.get("status") == "done"
        and not doc.get("queue")
    ):
        return True
    return False


def public_status(hub: Path) -> dict[str, Any]:
    doc = read_apply(hub)
    cur = doc.get("current") if isinstance(doc.get("current"), dict) else None
    busy = is_apply_busy(hub)
    return {
        "queued": cur is not None or bool(doc.get("queue")),
        "active": busy,
        "status": cur.get("status") if cur else None,
        "phase": cur.get("phase") if cur else None,
        "tabId": cur.get("tabId") if cur else None,
        "queueLength": len(doc.get("queue") or []),
        "completedTabs": [c.get("tabId") for c in (doc.get("completed") or []) if c.get("status") == "done"],
        "error": cur.get("error") if cur else None,
        "completedUtc": cur.get("completedUtc") if cur and cur.get("status") == "done" else None,
        "updatedUtc": doc.get("updatedUtc"),
    }
