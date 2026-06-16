#!/usr/bin/env python3
"""Bridge Hub wizard Video tab → AI Director (port 8767)."""
from __future__ import annotations

import importlib.util
from pathlib import Path
from typing import Any

_TOOLS = Path(__file__).resolve().parent


def _ensure_mod():
    spec = importlib.util.spec_from_file_location("ensure_ai_director", _TOOLS / "ensure-ai-director.py")
    if spec is None or spec.loader is None:
        raise RuntimeError("ensure-ai-director.py not found")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def resolve_capture_for_hub(hub: Path) -> Path | None:
    """Latest timelapse capture for this Unity project."""
    hub = hub.expanduser().resolve()
    try:
        import planner_recording as rec

        doc = rec.read_capture_session(hub)
        for key in ("runFolder", "capturePath", "capture"):
            raw = doc.get(key)
            if not raw:
                continue
            p = Path(str(raw)).expanduser().resolve()
            if p.is_dir() and (p / "timelapse").is_dir():
                return p
            if p.is_dir():
                return p
    except Exception:
        pass

    cap_root = hub / "Library" / "EnvironmentKit" / "DemoCapture"
    if cap_root.is_dir():
        runs = sorted(
            (p for p in cap_root.iterdir() if p.is_dir() and (p / "timelapse").is_dir()),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
        if runs:
            return runs[0]

    mod = _ensure_mod()
    fallback = mod.latest_capture()
    return fallback


def public_status(hub: Path | None) -> dict[str, Any]:
    mod = _ensure_mod()
    port = mod._port()
    healthy = mod.api_healthy(port) and mod.api_has_routes(port)
    capture = resolve_capture_for_hub(hub) if hub else None
    url = mod.director_url(capture, port) if healthy and capture else None
    return {
        "ready": healthy,
        "port": port,
        "directorUrl": url,
        "capture": str(capture) if capture else None,
        "hasCapture": capture is not None,
    }


def ensure_for_hub(hub: Path) -> dict[str, Any]:
    hub = hub.expanduser().resolve()
    capture = resolve_capture_for_hub(hub)
    if capture is None:
        return {
            "ok": False,
            "ready": False,
            "error": (
                "No DemoCapture run found. Record a demo in Unity (timelapse under "
                "Library/EnvironmentKit/DemoCapture) or finalize a wizard capture session."
            ),
            "capture": None,
            "directorUrl": None,
        }

    mod = _ensure_mod()
    rc = mod.ensure(capture, open_ui=False)
    port = mod._port()
    if rc != 0:
        return {
            "ok": False,
            "ready": False,
            "error": f"AI Director failed to start on port {port}. See ai-director-server.log.",
            "capture": str(capture),
            "directorUrl": None,
            "port": port,
        }

    url = mod.director_url(capture, port)
    return {
        "ok": True,
        "ready": True,
        "directorUrl": url,
        "capture": str(capture),
        "port": port,
    }
