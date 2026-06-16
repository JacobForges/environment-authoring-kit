#!/usr/bin/env python3
"""Tab-local checklist merge — never use world-planner CANONICAL_CHECKLIST_IDS."""
from __future__ import annotations

import re
from typing import Any


def checklist_item_ids(default_checklist: list[dict[str, Any]]) -> list[str]:
    return [str(c["id"]) for c in default_checklist]


def extra_items(doc: dict[str, Any]) -> list[dict[str, Any]]:
    raw = doc.get("checklistExtra")
    return list(raw) if isinstance(raw, list) else []


def effective_template(doc: dict[str, Any], default_checklist: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Canonical checklist items plus dynamic follow-ups stored on the session."""
    seen = {str(c["id"]) for c in default_checklist}
    out = [dict(c) for c in default_checklist]
    for item in extra_items(doc):
        if not isinstance(item, dict):
            continue
        cid = str(item.get("id") or "").strip()
        if not cid or cid in seen:
            continue
        out.append(
            {
                "id": cid,
                "label": str(item.get("label") or cid)[:120],
                "category": str(item.get("category") or "Follow-up")[:40],
                "done": False,
                "decision": "",
            }
        )
        seen.add(cid)
    return out


def ensure_checklist_shape(doc: dict[str, Any], default_checklist: list[dict[str, Any]]) -> list[dict[str, Any]]:
    template = effective_template(doc, default_checklist)
    by_id = {c["id"]: dict(c) for c in template}
    for item in doc.get("checklist") or []:
        cid = str(item.get("id") or "")
        if cid not in by_id:
            continue
        prev = by_id[cid]
        prev["label"] = item.get("label") or prev.get("label", cid)
        prev["decision"] = str(item.get("decision") or prev.get("decision") or "")
        prev["done"] = bool(item.get("done"))
        if item.get("category"):
            prev["category"] = item["category"]
    doc["checklist"] = [by_id[c["id"]] for c in template]
    return doc["checklist"]


def next_pending_id(doc: dict[str, Any], default_checklist: list[dict[str, Any]]) -> str | None:
    ensure_checklist_shape(doc, default_checklist)
    for item in doc["checklist"]:
        if not item.get("done"):
            return str(item["id"])
    return None


def all_done(doc: dict[str, Any], default_checklist: list[dict[str, Any]]) -> bool:
    ensure_checklist_shape(doc, default_checklist)
    return all(bool(item.get("done")) for item in doc["checklist"])


def label_for_id(doc: dict[str, Any], item_id: str, default_checklist: list[dict[str, Any]]) -> str:
    ensure_checklist_shape(doc, default_checklist)
    for item in doc["checklist"]:
        if item.get("id") == item_id:
            return f"{item.get('label', item_id)}"
    return item_id


def merge_additions(
    doc: dict[str, Any],
    additions: list[dict[str, Any]] | None,
    default_checklist: list[dict[str, Any]],
) -> int:
    """Append new checklist rows (LLM or gap detector). Returns count added."""
    if not additions:
        return 0
    extra = extra_items(doc)
    known = {str(c["id"]) for c in default_checklist} | {str(e.get("id") or "") for e in extra}
    added = 0
    for item in additions:
        if not isinstance(item, dict):
            continue
        cid = str(item.get("id") or "").strip()
        if not cid or cid in known:
            continue
        extra.append(
            {
                "id": cid,
                "label": str(item.get("label") or cid)[:120],
                "category": str(item.get("category") or "Follow-up")[:40],
            }
        )
        known.add(cid)
        added += 1
    if added:
        doc["checklistExtra"] = extra
        ensure_checklist_shape(doc, default_checklist)
    return added


def reopen_items(
    doc: dict[str, Any],
    item_ids: list[str],
    default_checklist: list[dict[str, Any]],
) -> None:
    ensure_checklist_shape(doc, default_checklist)
    by_id = {c["id"]: c for c in doc["checklist"]}
    for iid in item_ids:
        if iid in by_id:
            by_id[iid]["done"] = False
    doc["checklist"] = [by_id[c["id"]] for c in effective_template(doc, default_checklist)]


def merge_checklist(
    doc: dict[str, Any],
    incoming: list[dict[str, Any]] | None,
    default_checklist: list[dict[str, Any]],
    *,
    latest_user: str = "",
    allow_auto_fallback: bool = True,
    expected_pending_id: str | None = None,
) -> None:
    ensure_checklist_shape(doc, default_checklist)
    by_id = {c["id"]: dict(c) for c in doc["checklist"]}
    marked_any = False
    if incoming:
        for item in incoming:
            cid = str(item.get("id") or "")
            if cid not in by_id:
                continue
            prev = by_id[cid]
            prev["label"] = item.get("label") or prev.get("label", cid)
            if "done" in item:
                prev["done"] = bool(item["done"])
                if prev["done"]:
                    marked_any = True
            if item.get("decision"):
                prev["decision"] = str(item["decision"])[:240]
            by_id[cid] = prev
    template = effective_template(doc, default_checklist)
    doc["checklist"] = [by_id[c["id"]] for c in template]

    user = (latest_user or "").strip()
    if not allow_auto_fallback or not user or user.lower().startswith("{"):
        return
    if "move on" in user.lower():
        return

    nxt = next_pending_id(doc, default_checklist)
    if not nxt:
        return
    if expected_pending_id and nxt != expected_pending_id:
        return
    if marked_any and by_id.get(nxt, {}).get("done"):
        return

    # Only auto-complete when the model omitted checklist updates entirely.
    if marked_any:
        return

    by_id[nxt]["done"] = True
    if not by_id[nxt].get("decision"):
        by_id[nxt]["decision"] = user[:240]
    doc["checklist"] = [by_id[c["id"]] for c in template]


def annotate_indices(checklist: list[dict[str, Any]]) -> list[dict[str, Any]]:
    out = []
    for i, item in enumerate(checklist, 1):
        row = dict(item)
        row["index"] = i
        out.append(row)
    return out


def decision_looks_weak(item_id: str, decision: str) -> bool:
    """Heuristic: marked done but decision does not match topic."""
    d = (decision or "").strip()
    if not d or len(d) < 12:
        return True
    low = d.lower()

    if item_id == "playmode_smoke":
        if re.search(r"required\s*npc|requirednpcids|steps\s*\[|play\s*mode\s*smoke", low):
            return False
        if low.count("→") >= 2 or "walk to" in low:
            return True
        # Placement dump mis-filed as smoke plan
        if "worldanchors" in low.replace(" ", "") and "smoke" not in low:
            return True
        return "smoke" not in low and "step" not in low

    if item_id == "ambient_npcs":
        return "dlg_" not in low and (
            "reserved" in low or "not dialog" in low or "positioned but" in low
        )

    if item_id == "enemy_patrols":
        return "no patrol" in low or "patrol routes authored" in low and "waypoint" not in low

    if item_id == "quest_npcs":
        return "quest_" not in low and "objective" not in low

    return False
