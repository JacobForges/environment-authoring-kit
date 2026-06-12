#!/usr/bin/env python3
"""Demo capture session bridge — wizard in-browser frames + chapter manifest."""
from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SESSION_REL = Path("Assets/EnvironmentKit/Generated/DemoCaptureSession.json")
CHAPTERS_NAME = "DemoRecapChapters.json"


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat()


def _session_path(hub: Path) -> Path:
    return hub / SESSION_REL


def read_capture_session(hub: Path) -> dict[str, Any]:
    p = _session_path(hub)
    if not p.is_file():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def _wizard_meta_path(run_folder: Path) -> Path:
    return run_folder / "wizard" / "wizard-capture-meta.json"


def wizard_ui_capture_active(hub: Path) -> bool:
    """True while the browser should capture planner UI frames (not Unity Scene timelapse)."""
    doc = read_capture_session(hub)
    if "wizardUiCapture" in doc:
        return bool(doc.get("wizardUiCapture"))
    if not doc.get("recording"):
        return False
    run_folder = doc.get("runFolder") or ""
    if run_folder and _wizard_meta_path(Path(run_folder)).is_file():
        return False
    label = str(doc.get("buildModeLabel") or "").lower()
    return "planner" in label or "planning" in label


def _write_capture_session(hub: Path, doc: dict[str, Any]) -> None:
    from envkit_paths import atomic_write_text

    p = _session_path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    atomic_write_text(p, json.dumps(doc, indent=2) + "\n")


def stop_wizard_ui_capture(hub: Path) -> bool:
    """Stop browser wizard frame capture; Unity Scene timelapse may continue."""
    doc = read_capture_session(hub)
    if not doc:
        return False
    if doc.get("wizardUiCapture") is False:
        return False
    doc["wizardUiCapture"] = False
    doc["wizardFinalizedUtc"] = _utc()
    _write_capture_session(hub, doc)
    return True


def _chapters_path(run_folder: Path) -> Path:
    return run_folder / CHAPTERS_NAME


def _load_chapters(run_folder: Path) -> dict[str, Any]:
    p = _chapters_path(run_folder)
    if not p.is_file():
        return {"version": 1, "chapters": []}
    try:
        doc = json.loads(p.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        doc = {"version": 1, "chapters": []}
    if "chapters" not in doc:
        doc["chapters"] = []
    return doc


def _save_chapters(run_folder: Path, doc: dict[str, Any]) -> None:
    from envkit_paths import atomic_write_text

    run_folder.mkdir(parents=True, exist_ok=True)
    atomic_write_text(_chapters_path(run_folder), json.dumps(doc, indent=2) + "\n")


def append_chapter(
    run_folder: Path,
    chapter_id: str,
    title: str,
    *,
    phase: str = "",
    source: str = "wizard",
    frame_index: int | None = None,
) -> None:
    doc = _load_chapters(run_folder)
    chapters: list[dict[str, Any]] = list(doc.get("chapters") or [])
    entry = {
        "id": chapter_id,
        "title": title,
        "phase": phase,
        "source": source,
        "utc": _utc(),
    }
    if frame_index is not None:
        entry["wizardFrameIndex"] = frame_index
    if chapters and chapters[-1].get("id") == chapter_id:
        chapters[-1] = entry
    else:
        chapters.append(entry)
    doc["chapters"] = chapters
    _save_chapters(run_folder, doc)


def wizard_frames_dir(run_folder: Path) -> Path:
    d = run_folder / "wizard" / "frames"
    d.mkdir(parents=True, exist_ok=True)
    return d


def next_wizard_frame_index(run_folder: Path) -> int:
    frames = wizard_frames_dir(run_folder)
    existing = sorted(frames.glob("wf_*.png"))
    if not existing:
        return 1
    last = existing[-1].stem
    m = re.search(r"(\d+)$", last)
    return int(m.group(1)) + 1 if m else len(existing) + 1


def save_wizard_frame(run_folder: Path, png_bytes: bytes) -> int:
    idx = next_wizard_frame_index(run_folder)
    path = wizard_frames_dir(run_folder) / f"wf_{idx:06d}.png"
    path.write_bytes(png_bytes)
    return idx


def public_recording_session(hub: Path) -> dict[str, Any]:
    doc = read_capture_session(hub)
    run_folder = doc.get("runFolder") or ""
    run_path = Path(run_folder) if run_folder else None
    chapters = _load_chapters(run_path) if run_path and run_path.is_dir() else {"chapters": []}
    frame_count = 0
    if run_path and run_path.is_dir():
        frame_count = len(list(wizard_frames_dir(run_path).glob("wf_*.png")))
    return {
        "recording": bool(doc.get("recording")),
        "wizardUiCapture": wizard_ui_capture_active(hub),
        "runFolder": run_folder,
        "buildModeLabel": doc.get("buildModeLabel") or "",
        "armedUtc": doc.get("armedUtc") or "",
        "chapters": chapters.get("chapters") or [],
        "wizardFrameCount": frame_count,
    }


def finalize_wizard_capture(hub: Path) -> dict[str, Any]:
    doc = read_capture_session(hub)
    run_folder = doc.get("runFolder")
    if not run_folder:
        return {"ok": False, "message": "No active capture session"}
    run_path = Path(run_folder)
    meta_path = _wizard_meta_path(run_path)
    if meta_path.is_file():
        try:
            existing = json.loads(meta_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            existing = {}
        if existing.get("finalizedUtc"):
            stop_wizard_ui_capture(hub)
            return {
                "ok": True,
                "message": "Wizard capture already finalized",
                "finalizedUtc": existing.get("finalizedUtc"),
                "wizardFrameCount": existing.get("wizardFrameCount")
                or len(list(wizard_frames_dir(run_path).glob("wf_*.png"))),
            }
    append_chapter(run_path, "wizard_complete", "Planning complete", phase="wizard_complete", source="wizard")
    frame_count = len(list(wizard_frames_dir(run_path).glob("wf_*.png")))
    meta = {
        "finalizedUtc": _utc(),
        "wizardFrameCount": frame_count,
    }
    meta_path.parent.mkdir(parents=True, exist_ok=True)
    meta_path.write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    stop_wizard_ui_capture(hub)
    return {"ok": True, "message": "Wizard capture finalized", **meta}
