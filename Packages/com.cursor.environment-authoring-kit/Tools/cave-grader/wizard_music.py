#!/usr/bin/env python3
"""Bridge Hub wizard Music tab → AI Director Studio (music production UI)."""
from __future__ import annotations

import importlib.util
from pathlib import Path
from typing import Any

_TOOLS = Path(__file__).resolve().parent


def _studio_root(hub: Path) -> Path:
    hub = hub.expanduser().resolve()
    candidates = [
        hub / "ai-director-studio",
        hub.parent / "ai-director-studio",
        _TOOLS.parent.parent.parent.parent / "ai-director-studio",
    ]
    for c in candidates:
        if (c / "server" / "ensure-ai-director.py").is_file():
            return c.resolve()
    return (hub / "ai-director-studio").resolve()


def _ensure_mod(hub: Path | None):
    root = _studio_root(hub) if hub else _TOOLS.parent.parent.parent.parent / "ai-director-studio"
    script = root / "server" / "ensure-ai-director.py"
    if not script.is_file():
        raise RuntimeError(f"ai-director-studio not found (expected {script})")
    spec = importlib.util.spec_from_file_location("studio_ensure_ai_director", script)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load {script}")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def public_status(hub: Path | None) -> dict[str, Any]:
    try:
        mod = _ensure_mod(hub)
    except RuntimeError as ex:
        return {"ready": False, "error": str(ex), "musicUrl": None, "port": None}
    port = mod._port()
    healthy = mod.api_healthy(port) and mod.api_has_routes(port)
    has_music = healthy and mod.api_has_music_routes(port)
    url = f"http://127.0.0.1:{port}/?music=1" if has_music else None
    return {
        "ready": has_music,
        "port": port,
        "musicUrl": url,
        "studioRoot": str(_studio_root(hub)) if hub else None,
    }


def ensure_for_hub(hub: Path) -> dict[str, Any]:
    hub = hub.expanduser().resolve()
    try:
        mod = _ensure_mod(hub)
    except RuntimeError as ex:
        return {"ok": False, "ready": False, "error": str(ex), "musicUrl": None}

    rc = mod.ensure(None, open_ui=False)
    port = mod._port()
    if rc != 0:
        return {
            "ok": False,
            "ready": False,
            "error": f"AI Director Studio failed to start on port {port}. See ai-director-server.log.",
            "musicUrl": None,
            "port": port,
        }

    if not mod.api_has_music_routes(port):
        return {
            "ok": False,
            "ready": False,
            "error": "Director API running but music routes missing — rebuild ai-director-studio.",
            "musicUrl": None,
            "port": port,
        }

    url = f"http://127.0.0.1:{port}/?music=1"
    return {"ok": True, "ready": True, "musicUrl": url, "port": port}
