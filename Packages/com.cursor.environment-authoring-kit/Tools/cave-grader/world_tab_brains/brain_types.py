#!/usr/bin/env python3
"""Shared types and contracts for per-tab brains.

This module defines the validator schema used by the world-generation wizard
tabs. Each tab-specific brain (rule-based, ONNX-backed, or hybrid) receives
the same structured input and returns a `TabBrainResult` describing whether
the proposed checklist update and brief edits are acceptable.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Callable, Dict, List, Mapping, MutableMapping, Optional, Sequence


class ViolationSeverity(str, Enum):
    """Severity levels for tab-brain validation results."""

    INFO = "info"
    WARNING = "warning"
    ERROR = "error"


@dataclass
class ChecklistItemView:
    """Lightweight view of a single checklist row."""

    id: str
    label: str
    done: bool
    decision: str
    category: str = ""
    index: int | None = None


@dataclass
class TabBrainInput:
    """Tab-brain input payload.

    This is intentionally minimal and text-centric so that small ONNX models
    and rule engines can operate without depending on the full session shape.
    """

    tab_id: str
    checklist_before: Sequence[ChecklistItemView]
    checklist_after_proposed: Sequence[ChecklistItemView]
    latest_user_message: str
    latest_assistant_message: str
    brief_fragment: Mapping[str, Any] | None = None
    # Optional per-tab metadata (zone ids, phase, presets, etc.).
    metadata: Mapping[str, Any] | None = None


@dataclass
class Violation:
    """Represents a single validation issue detected by a tab brain."""

    id: str
    message: str
    severity: ViolationSeverity = ViolationSeverity.ERROR
    checklist_id: str | None = None
    hint: str | None = None


@dataclass
class ChecklistAddition:
    """Additional checklist row to add dynamically."""

    id: str
    label: str
    category: str = "Follow-up"


@dataclass
class TabBrainResult:
    """Standardized output for all tab brains.

    The planner will combine this with deterministic rules before deciding
    whether to accept a checklist update, reopen items, or add follow-ups.
    """

    passed: bool
    confidence: float = 0.0
    task_validity_score: float = 0.0
    outcome_validity_score: float = 0.0
    coverage_by_checklist_id: Dict[str, float] = field(default_factory=dict)
    violations: List[Violation] = field(default_factory=list)
    followup_additions: List[ChecklistAddition] = field(default_factory=list)
    reopen_checklist_ids: List[str] = field(default_factory=list)
    audit_trace_id: str | None = None
    evidence_refs: List[str] = field(default_factory=list)
    # Optional extra fields for future extensions or debugging.
    extra: MutableMapping[str, Any] = field(default_factory=dict)

    def to_public_dict(self) -> Dict[str, Any]:
        """Return a JSON-serializable view suitable for session metadata."""

        return {
            "passed": bool(self.passed),
            "confidence": float(self.confidence),
            "taskValidityScore": float(self.task_validity_score),
            "outcomeValidityScore": float(self.outcome_validity_score),
            "coverageByChecklistId": dict(self.coverage_by_checklist_id),
            "violations": [
                {
                    "id": v.id,
                    "message": v.message,
                    "severity": v.severity.value,
                    "checklistId": v.checklist_id,
                    "hint": v.hint,
                }
                for v in self.violations
            ],
            "followupChecklistAdditions": [
                {"id": a.id, "label": a.label, "category": a.category}
                for a in self.followup_additions
            ],
            "reopenChecklistIds": list(self.reopen_checklist_ids),
            "auditTraceId": self.audit_trace_id,
            "evidenceRefs": list(self.evidence_refs),
            "extra": dict(self.extra),
        }


TabBrainValidator = Callable[[TabBrainInput], TabBrainResult]

