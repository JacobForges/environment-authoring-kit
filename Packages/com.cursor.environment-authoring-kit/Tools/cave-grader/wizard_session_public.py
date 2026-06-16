#!/usr/bin/env python3
"""Shared public session + pulse for tab planners (streaming, sanitized chat)."""
from __future__ import annotations

from typing import Any

import build_planner as bp
import wizard_checklist as wc


def latest_user_message(doc: dict[str, Any]) -> str:
    for m in reversed(doc.get("messages") or []):
        if m.get("role") == "user":
            return str(m.get("content") or "")
    return ""


def public_tab_session(
    hub_doc: dict[str, Any] | None,
    *,
    tab_id: str,
    help_intro: str,
    default_checklist: list[dict[str, Any]],
    brief_rel: str,
) -> dict[str, Any]:
    if not hub_doc:
        checklist = wc.annotate_indices([dict(c) for c in default_checklist])
        return {
            "tabId": tab_id,
            "phase": "idle",
            "messages": [],
            "checklist": checklist,
            "helpIntro": help_intro,
            "helpSeen": False,
        }

    doc = hub_doc
    if doc.get("cursorWorking") and hasattr(bp, "_clear_stale_cursor_working"):
        if bp._clear_stale_cursor_working(doc):
            pass  # caller should persist if needed

    out = dict(doc)
    stream_text, stream_role = bp._public_streaming(doc)
    if doc.get("cursorWorking"):
        out["streamingText"] = stream_text
        out["streamingRole"] = stream_role
    else:
        out.pop("streamingText", None)
        out.pop("streamingRole", None)

    out["tabId"] = tab_id
    out["helpIntro"] = help_intro
    out.setdefault("helpSeen", bool(out.get("messages")))
    out["messages"] = [
        {**m, "content": bp._sanitize_chat_content(str(m.get("content") or ""))}
        for m in (doc.get("messages") or [])
    ]
    checklist = wc.ensure_checklist_shape(out, default_checklist)
    out["checklist"] = wc.annotate_indices(checklist)
    out["checklistComplete"] = wc.all_done(doc, default_checklist)
    out["briefPath"] = brief_rel
    out["cursorWorking"] = bool(doc.get("cursorWorking"))
    out["awaitingAssistantReply"] = bool(
        doc.get("phase") == "qna"
        and not doc.get("cursorWorking")
        and (doc.get("messages") or [])
        and (doc.get("messages") or [])[-1].get("role") == "user"
    )
    return out


def session_pulse(doc: dict[str, Any] | None, default_checklist: list[dict[str, Any]]) -> dict[str, Any]:
    if not doc:
        return {"phase": None, "messages": [], "checklist": []}
    stream_text, stream_role = bp._public_streaming(doc)
    checklist = wc.ensure_checklist_shape(dict(doc), default_checklist)
    return {
        "phase": doc.get("phase"),
        "messages": [
            {**m, "content": bp._sanitize_chat_content(str(m.get("content") or ""))}
            for m in (doc.get("messages") or [])
        ],
        "checklist": wc.annotate_indices(checklist),
        "checklistComplete": wc.all_done(doc, default_checklist),
        "streamingText": stream_text,
        "streamingRole": stream_role,
        "cursorWorking": bool(doc.get("cursorWorking")),
        "autoRespondActive": bool(doc.get("autoRespondActive")),
    }


def session_busy(doc: dict[str, Any] | None) -> bool:
    if not doc:
        return False
    if doc.get("cursorWorking"):
        return True
    stream_text, _ = bp._public_streaming(doc)
    return bool(stream_text)
