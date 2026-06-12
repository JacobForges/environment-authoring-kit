#!/usr/bin/env python3
"""Clear volatile planner queue state before each Hub world build."""
from __future__ import annotations

import argparse
import os
from pathlib import Path

from build_planner import _read_session, _write_session  # noqa: E402
from envkit_paths import ENV_VAR, resolve_envkit_root  # noqa: E402


def clear_planner_volatile(hub: Path) -> bool:
    doc = _read_session(hub) or {}
    if not doc:
        return False
    changed = False
    if doc.pop("cardPacedQueue", None) is not None:
        changed = True
    if doc.pop("cardPacedActive", None) is not None:
        changed = True
    for card in doc.get("conceptCards") or []:
        if card.pop("jobProgress", None) is not None:
            changed = True
    if changed:
        _write_session(hub, doc)
    return changed


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--hub", required=True, help="Unity Hub project root")
    args = ap.parse_args()
    hub = Path(args.hub).expanduser().resolve()
    os.environ.setdefault("HUB_ROOT", str(hub))
    os.environ.setdefault(ENV_VAR, str(resolve_envkit_root()))
    clear_planner_volatile(hub)


if __name__ == "__main__":
    main()
