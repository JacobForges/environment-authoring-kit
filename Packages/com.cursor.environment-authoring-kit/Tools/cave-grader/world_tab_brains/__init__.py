"""Per-tab brains for the Environment Kit build wizard."""

from .brain_types import (  # noqa: F401
    ChecklistAddition,
    ChecklistItemView,
    TabBrainInput,
    TabBrainResult,
    TabBrainValidator,
    Violation,
    ViolationSeverity,
)
from .brain_runtime import evaluate_tab_brain, get_validator, register_validator  # noqa: F401

# Register built-in phase-0 rule validators on import.
try:
    from .rule_validators import TAB_RULE_VALIDATORS  # noqa: F401

    for _tab_id, _fn in TAB_RULE_VALIDATORS.items():
        register_validator(_tab_id, _fn)
except Exception:
    # Do not hard-fail the entire wizard if rule validators fail to import.
    # Missing validators will simply result in default pass behavior.
    pass

