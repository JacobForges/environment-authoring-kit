"""Resolve Environment Kit data root (external volume when mounted, else Hub/Library)."""
from __future__ import annotations

import os
from pathlib import Path

BUNDLE_NAME = "EnvironmentKit-Hub"
ENV_VAR = "ENVIRONMENT_KIT_DATA_ROOT"


def _hub_root() -> Path:
    raw = os.environ.get("HUB_ROOT", "").strip()
    if raw:
        return Path(raw).expanduser()
    return Path.home() / "Hub"


def _internal_root() -> Path:
    return _hub_root() / "Library" / "EnvironmentKit"


def _try_external_root() -> Path | None:
    volumes = Path("/Volumes")
    if not volumes.is_dir():
        return None
    for volume in sorted(volumes.iterdir()):
        if not volume.is_dir():
            continue
        name = volume.name
        if name in {".", "..", "Macintosh HD"} or name.startswith("."):
            continue
        candidate = volume / BUNDLE_NAME
        try:
            candidate.mkdir(parents=True, exist_ok=True)
            probe = candidate / ".write_probe"
            probe.write_text("ok", encoding="utf-8")
            probe.unlink(missing_ok=True)
            return candidate
        except OSError:
            continue
    return None


def resolve_envkit_root() -> Path:
    override = os.environ.get(ENV_VAR, "").strip()
    if override:
        path = Path(override).expanduser()
        path.mkdir(parents=True, exist_ok=True)
        return path
    external = _try_external_root()
    if external is not None:
        return external
    root = _internal_root()
    root.mkdir(parents=True, exist_ok=True)
    return root


def approved_cards_path() -> Path:
    return resolve_envkit_root() / "DemoRecapApproved" / "ApprovedCards.json"


def approved_dir() -> Path:
    return resolve_envkit_root() / "DemoRecapApproved"
