#!/usr/bin/env python3
"""Rule-first validators for wizard tabs.

These brains do not depend on ONNX; they wrap existing checklist helpers and
tab-specific invariants to provide a `TabBrainResult` that expresses whether
the current step looks complete.

They are the initial implementation for the \"rule-only baseline\" phase of
the tab-brain rollout.
"""
from __future__ import annotations

from typing import Dict

import wizard_checklist as wc

from .brain_types import (
    ChecklistItemView,
    TabBrainInput,
    TabBrainResult,
    Violation,
    ViolationSeverity,
)


def _base_result(inp: TabBrainInput) -> TabBrainResult:
    coverage = {item.id: 1.0 if item.done else 0.0 for item in inp.checklist_after_proposed}
    return TabBrainResult(
        passed=True,
        confidence=0.5,
        task_validity_score=0.5,
        outcome_validity_score=0.5,
        coverage_by_checklist_id=coverage,
    )


def _surface_content_brain(inp: TabBrainInput) -> TabBrainResult:
    """Surface-content specific rule validator.

    Uses the same heuristics as `wizard_checklist.decision_looks_weak` for
    several key items, and blocks completion when obviously thin.
    """

    res = _base_result(inp)
    pending: Dict[str, ChecklistItemView] = {}

    for item in inp.checklist_after_proposed:
        if not item.done:
            pending[item.id] = item
        else:
            if wc.decision_looks_weak(item.id, item.decision):
                res.violations.append(
                    Violation(
                        id=f"weak_{item.id}",
                        checklist_id=item.id,
                        severity=ViolationSeverity.WARNING,
                        message=f"Checklist [{item.id}] “{item.label}” looks thin or off-topic.",
                        hint="Ask one focused follow-up before treating this topic as complete.",
                    )
                )

    if pending:
        res.passed = False
        res.confidence = 0.2
        res.task_validity_score = 0.2
        res.outcome_validity_score = 0.0
        res.reopen_checklist_ids.extend(sorted(pending.keys()))
    else:
        # Only declare success when no violation is worse than WARNING.
        if any(v.severity is ViolationSeverity.ERROR for v in res.violations):
            res.passed = False
            res.confidence = 0.1
            res.task_validity_score = 0.0
            res.outcome_validity_score = 0.0
        else:
            res.passed = True
            res.confidence = 0.8
            res.task_validity_score = 0.9
            res.outcome_validity_score = 0.8

    return res


TAB_RULE_VALIDATORS = {
    "surface-content": _surface_content_brain,
    # Other tabs (terrain, caves, mazes, etc.) can be added incrementally.
}

