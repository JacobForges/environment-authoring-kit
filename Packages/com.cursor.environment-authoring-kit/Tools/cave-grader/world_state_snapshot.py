#!/usr/bin/env python3
"""Shared world-state snapshot loader for wizard tabs.

Goal: every tab can be "world-aware" by reading the latest exported snapshots
and presenting a compact, stable summary for:
- planner prompts (human-readable)
- tab brains (structured metadata)
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping


CONTENT_SNAPSHOT_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildContentLayoutWorldSnapshot.json")
CONTENT_ANCHORS_REL = Path("Assets/EnvironmentKit/ContentLayout/ContentLayoutWorldAnchors.json")


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat()


def _read_json(path: Path) -> dict[str, Any] | None:
    try:
        if not path.is_file():
            return None
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception:
        return None


def _center3(v: Mapping[str, Any] | None) -> tuple[float, float, float] | None:
    if not isinstance(v, Mapping):
        return None
    try:
        return (float(v.get("x", 0.0)), float(v.get("y", 0.0)), float(v.get("z", 0.0)))
    except Exception:
        return None


def _summarize_zone_centers(zones: Mapping[str, Any] | None) -> dict[str, dict[str, Any]]:
    out: dict[str, dict[str, Any]] = {}
    if not isinstance(zones, Mapping):
        return out
    for zid, entry in zones.items():
        if not isinstance(entry, Mapping):
            continue
        center = entry.get("center")
        if isinstance(center, Mapping):
            c = _center3(center)
        else:
            # anchors file uses hubOffsetX/Z only (no y)
            try:
                c = (float(entry.get("hubOffsetX", 0.0)), 0.0, float(entry.get("hubOffsetZ", 0.0)))
            except Exception:
                c = None
        if c is None:
            continue
        out[str(zid)] = {"center": {"x": c[0], "y": c[1], "z": c[2]}, "storyBeat": entry.get("storyBeat", "")}
    return out


def _slot_counts(doc: Mapping[str, Any] | None) -> dict[str, int]:
    if not isinstance(doc, Mapping):
        return {"npc": 0, "enemy": 0, "prop": 0}
    npc = doc.get("npcSlots") or {}
    enemy = doc.get("enemySlots") or {}
    prop = doc.get("propSlots") or {}
    return {
        "npc": len(npc) if isinstance(npc, Mapping) else 0,
        "enemy": len(enemy) if isinstance(enemy, Mapping) else 0,
        "prop": len(prop) if isinstance(prop, Mapping) else 0,
    }


def build_world_state(hub: Path) -> dict[str, Any]:
    """Load the latest known snapshots and return a compact canonical state."""
    hub = hub.expanduser().resolve()
    snap_path = hub / CONTENT_SNAPSHOT_REL
    anchors_path = hub / CONTENT_ANCHORS_REL

    snap = _read_json(snap_path) or {}
    anchors = _read_json(anchors_path) or {}

    zones = _summarize_zone_centers(snap.get("zones") if snap else None)
    if not zones:
        zones = _summarize_zone_centers(anchors.get("zones") if anchors else None)

    return {
        "version": 1,
        "updatedUtc": _utc(),
        "sources": {
            "contentLayoutWorldSnapshot": {
                "path": str(CONTENT_SNAPSHOT_REL),
                "exists": bool(snap_path.is_file()),
                "snapshotVersion": snap.get("version") if isinstance(snap, Mapping) else None,
            },
            "contentLayoutWorldAnchors": {
                "path": str(CONTENT_ANCHORS_REL),
                "exists": bool(anchors_path.is_file()),
                "anchorsVersion": anchors.get("version") if isinstance(anchors, Mapping) else None,
            },
        },
        "scene": {
            "sceneName": snap.get("sceneName") or anchors.get("sceneName") or "",
            "landmark": snap.get("landmark") or anchors.get("landmark") or "",
            "playerSpawn": snap.get("playerSpawn") if isinstance(snap, Mapping) else None,
        },
        "zones": zones,
        "slotCounts": {
            "snapshot": _slot_counts(snap),
            "anchors": _slot_counts(anchors),
        },
    }


def build_prompt_block(world_state: Mapping[str, Any]) -> str:
    """Human-readable world matrix summary for system prompts."""
    scene = world_state.get("scene") if isinstance(world_state, Mapping) else {}
    zones = world_state.get("zones") if isinstance(world_state, Mapping) else {}
    sources = world_state.get("sources") if isinstance(world_state, Mapping) else {}
    counts = world_state.get("slotCounts") if isinstance(world_state, Mapping) else {}
    snap_counts = counts.get("snapshot") if isinstance(counts, Mapping) else {}

    lines: list[str] = []
    lines.append("## World-state matrix (auto-refreshed)")
    lines.append(f"- sceneName: {scene.get('sceneName','')}")
    if scene.get("landmark"):
        lines.append(f"- landmark: {scene.get('landmark')}")
    lines.append(
        f"- snapshot slots: npc={snap_counts.get('npc',0)} enemy={snap_counts.get('enemy',0)} prop={snap_counts.get('prop',0)}"
    )
    # Show only key zones and centers for placement.
    if isinstance(zones, Mapping) and zones:
        for zid in ("town_center", "east_trail", "cave_gate", "battle_arena", "wild_east", "wild_west"):
            z = zones.get(zid)
            if not isinstance(z, Mapping):
                continue
            c = z.get("center") or {}
            lines.append(f"- zone {zid}: center=({c.get('x')},{c.get('y')},{c.get('z')})")
    src_snap = sources.get("contentLayoutWorldSnapshot") if isinstance(sources, Mapping) else {}
    if isinstance(src_snap, Mapping) and not src_snap.get("exists"):
        lines.append("- WARNING: world snapshot missing — ask user to export snapshot in Unity (Game → Export Content Layout World Snapshot).")
    return "\n".join(lines) + "\n"

