#!/usr/bin/env python3
"""Runtime helpers for tab brains.

This module is intentionally light-weight: it provides a registry for
per-tab validator callables and a small helper to build `TabBrainInput`
structures from the existing wizard session docs.

Rule-based validators live alongside optional ONNX-backed validators so
that we can start in a deterministic mode and later upgrade to hybrid
brains without changing the tab planners.
"""
from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, MutableMapping, Optional

import wizard_checklist as wc

from .brain_types import (
    ChecklistItemView,
    TabBrainInput,
    TabBrainResult,
    TabBrainValidator,
    Violation,
    ViolationSeverity,
)


_VALIDATORS: Dict[str, TabBrainValidator] = {}


def register_validator(tab_id: str, fn: TabBrainValidator) -> None:
    """Register a validator for a given tab id."""

    _VALIDATORS[tab_id] = fn


def get_validator(tab_id: str) -> TabBrainValidator | None:
    """Return the registered validator for `tab_id`, if any."""

    return _VALIDATORS.get(tab_id)


def _view_checklist(items: Iterable[Mapping[str, Any]]) -> List[ChecklistItemView]:
    out: List[ChecklistItemView] = []
    for raw in items:
        cid = str(raw.get("id") or "")
        if not cid:
            continue
        out.append(
            ChecklistItemView(
                id=cid,
                label=str(raw.get("label") or cid),
                done=bool(raw.get("done")),
                decision=str(raw.get("decision") or ""),
                category=str(raw.get("category") or ""),
                index=int(raw.get("index")) if raw.get("index") is not None else None,
            )
        )
    return out


def build_input_for_tab(
    tab_id: str,
    doc: Mapping[str, Any],
    *,
    proposed_checklist: Optional[Iterable[Mapping[str, Any]]] = None,
) -> TabBrainInput:
    """Create a TabBrainInput from a wizard session doc.

    The planner passes `proposed_checklist` only when it differs from the
    stored checklist; otherwise this falls back to the current checklist.
    """

    before = list(doc.get("checklist") or [])
    after = list(proposed_checklist or before)
    latest_user = ""
    latest_assistant = ""
    for msg in reversed(doc.get("messages") or []):
        role = str(msg.get("role") or "")
        if role == "user" and not latest_user:
            latest_user = str(msg.get("content") or "")
        elif role == "assistant" and not latest_assistant:
            latest_assistant = str(msg.get("content") or "")
        if latest_user and latest_assistant:
            break

    meta = {"phase": doc.get("phase"), "kind": doc.get("kind"), "sceneName": doc.get("sceneName")}
    ws = doc.get("worldState")
    if isinstance(ws, Mapping) and ws:
        # Keep metadata compact: include only zone centers and slot counts.
        meta["worldState"] = {
            "scene": ws.get("scene"),
            "zones": ws.get("zones"),
            "slotCounts": ws.get("slotCounts"),
            "sources": ws.get("sources"),
        }

    return TabBrainInput(
        tab_id=tab_id,
        checklist_before=_view_checklist(before),
        checklist_after_proposed=_view_checklist(after),
        latest_user_message=latest_user,
        latest_assistant_message=latest_assistant,
        brief_fragment=(doc.get("brief") or {}),
        metadata=meta,
    )


def _default_pass_result(tab_id: str, inp: TabBrainInput) -> TabBrainResult:
    """Return a neutral pass result when no validator is registered.

    This keeps the plumbing simple for tabs that have not yet opted into
    specialized brains. Hard constraints are still enforced elsewhere.
    """

    coverage = {item.id: 1.0 if item.done else 0.0 for item in inp.checklist_after_proposed}
    return TabBrainResult(
        passed=True,
        confidence=0.0,
        task_validity_score=0.0,
        outcome_validity_score=0.0,
        coverage_by_checklist_id=coverage,
        violations=[],
        followup_additions=[],
        reopen_checklist_ids=[],
        audit_trace_id=None,
        evidence_refs=[],
        extra={"tabId": tab_id, "mode": "no_validator"},
    )


def evaluate_tab_brain(
    tab_id: str,
    doc: MutableMapping[str, Any],
    *,
    proposed_checklist: Optional[Iterable[Mapping[str, Any]]] = None,
) -> TabBrainResult:
    """Run the tab brain (if any) and attach a public view to the session doc.

    This is the primary entry point that tab planners should call after
    merging checklist updates but before deciding whether to finalize an
    item or move on.
    """

    validator = get_validator(tab_id)
    inp = build_input_for_tab(tab_id, doc, proposed_checklist=proposed_checklist)
    if validator is None:
        result = _default_pass_result(tab_id, inp)
    else:
        try:
            result = validator(inp)
        except Exception as exc:  # pragma: no cover - defensive
            result = TabBrainResult(
                passed=False,
                confidence=0.0,
                task_validity_score=0.0,
                outcome_validity_score=0.0,
                coverage_by_checklist_id={item.id: 0.0 for item in inp.checklist_after_proposed},
                violations=[
                    Violation(
                        id="brain_exception",
                        message=f"Tab brain for {tab_id} raised: {exc}",
                        severity=ViolationSeverity.ERROR,
                    )
                ],
                extra={"tabId": tab_id, "mode": "exception"},
            )

    # Attach a public snapshot for UI / telemetry purposes.
    brain_meta = result.to_public_dict()
    doc.setdefault("tabBrains", {})
    try:
        # type: ignore[assignment]
        doc["tabBrains"][tab_id] = brain_meta
    except Exception:
        # If doc["tabBrains"] had an unexpected shape, overwrite defensively.
        doc["tabBrains"] = {tab_id: brain_meta}

    # Lightweight telemetry: store per-turn brain outcomes for later retraining.
    # (Keeps data local to the session so we don't need a separate logging backend yet.)
    try:
        doc.setdefault("tabBrainTelemetry", [])
        doc["tabBrainTelemetry"].append(
            {
                "utc": datetime.now(timezone.utc).isoformat(),
                "tabId": tab_id,
                "passed": bool(result.passed),
                "confidence": float(result.confidence),
                "reopenChecklistIds": list(result.reopen_checklist_ids),
                "violations": [
                    {"id": v.id, "severity": v.severity.value, "checklistId": v.checklist_id}
                    for v in result.violations
                ],
            }
        )
        if len(doc["tabBrainTelemetry"]) > 200:
            doc["tabBrainTelemetry"] = doc["tabBrainTelemetry"][-200:]
    except Exception:
        pass
    return result

