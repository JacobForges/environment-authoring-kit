#!/usr/bin/env python3
"""Wizard manifest index — per-tab status without loading full sessions."""
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from envkit_paths import atomic_write_text
from wizard_registry import MANIFEST_REL, TAB_DEFS, public_session, tab_complete


def _utc() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def manifest_path(hub: Path) -> Path:
    return hub.expanduser().resolve() / MANIFEST_REL


def refresh_manifest(hub: Path) -> dict[str, Any]:
    tabs: list[dict[str, Any]] = []
    for t in TAB_DEFS:
        tid = t["id"]
        try:
            sess = public_session(hub, tid)
            phase = sess.get("phase") or "idle"
            complete = tab_complete(hub, tid)
        except Exception:
            phase = "error"
            complete = False
            sess = {}
        tabs.append(
            {
                "id": tid,
                "label": t["label"],
                "phase": phase,
                "complete": complete,
                "briefPath": t["brief"],
                "sessionPath": t["session"],
                "gradePassed": bool(sess.get("gradePassed")),
            }
        )
    doc = {"version": 1, "updatedUtc": _utc(), "tabs": tabs}
    p = manifest_path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    atomic_write_text(p, json.dumps(doc, indent=2) + "\n")
    return doc


def read_manifest(hub: Path) -> dict[str, Any]:
    p = manifest_path(hub)
    if not p.is_file():
        return refresh_manifest(hub)
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return refresh_manifest(hub)
