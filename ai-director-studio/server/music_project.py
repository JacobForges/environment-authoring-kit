"""Music project metadata and lifecycle — projectKind: music."""
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from director_paths import atomic_write_text, create_project

MUSIC_META_NAME = "MusicProject.json"
MUSIC_DIR = "music"


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def music_dir(project: Path) -> Path:
    d = project / MUSIC_DIR
    d.mkdir(parents=True, exist_ok=True)
    return d


def read_music_project(project: Path) -> dict[str, Any] | None:
    path = project / MUSIC_META_NAME
    if not path.is_file():
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return None


def write_music_project(project: Path, doc: dict[str, Any]) -> dict[str, Any]:
    doc["updatedAt"] = _utc()
    atomic_write_text(project / MUSIC_META_NAME, json.dumps(doc, indent=2))
    return doc


def default_status() -> dict[str, Any]:
    return {
        "instrumentalReady": False,
        "vocalRecorded": False,
        "produced": False,
        "produceRunning": False,
        "produceProgress": 0,
        "produceMessage": "",
        "sunoConfigured": False,
        "instrumentalSource": None,
    }


def create_music_project(
    name: str | None = None,
    *,
    key: str = "auto",
    scale: str = "minor",
    bpm: int | None = None,
    instrumental_prompt: str = "",
) -> tuple[Path, dict[str, Any]]:
    project = create_project(name or "music")
    doc: dict[str, Any] = {
        "projectKind": "music",
        "name": name or project.name,
        "key": key or "auto",
        "scale": scale or "minor",
        "bpm": bpm,
        "instrumentalPrompt": instrumental_prompt or "",
        "lyrics": "",
        "lyricsFormat": "lines",
        "createdAt": _utc(),
        "status": default_status(),
    }
    music_dir(project)
    write_music_project(project, doc)
    return project, doc


def update_music_project(project: Path, patch: dict[str, Any]) -> dict[str, Any]:
    doc = read_music_project(project) or {"projectKind": "music", "status": default_status()}
    for k, v in patch.items():
        if k == "status" and isinstance(v, dict) and isinstance(doc.get("status"), dict):
            doc["status"] = {**doc["status"], **v}
        else:
            doc[k] = v
    return write_music_project(project, doc)


def music_status(project: Path) -> dict[str, Any]:
    doc = read_music_project(project)
    if not doc:
        return {"ok": False, "error": "not a music project"}
    mdir = music_dir(project)
    st = doc.get("status") or default_status()
    paths = {
        "instrumental": mdir / "instrumental.mp3",
        "vocalRaw": next((mdir / n for n in ("vocal_raw.webm", "vocal_raw.wav") if (mdir / n).is_file()), None),
        "vocalDry": mdir / "vocal_dry.wav",
        "vocalProduced": mdir / "vocal_produced.wav",
        "songMasterWav": mdir / "SongMaster.wav",
        "songMasterMp3": mdir / "SongMaster.mp3",
        "performance": mdir / "performance.mp4",
        "lyrics": mdir / "lyrics.lrc",
    }
    return {
        "ok": True,
        "project": str(project),
        "name": doc.get("name") or project.name,
        "projectKind": "music",
        "key": doc.get("key", "auto"),
        "scale": doc.get("scale", "minor"),
        "bpm": doc.get("bpm"),
        "instrumentalPrompt": doc.get("instrumentalPrompt", ""),
        "lyrics": doc.get("lyrics", ""),
        "lyricsFormat": doc.get("lyricsFormat", "lines"),
        "status": {
            **st,
            "instrumentalReady": paths["instrumental"].is_file(),
            "vocalRecorded": bool(paths["vocalRaw"] and paths["vocalRaw"].is_file()),
            "produced": paths["songMasterWav"].is_file() or paths["songMasterMp3"].is_file(),
        },
        "files": {k: str(v) if v and Path(v).is_file() else None for k, v in paths.items()},
        "hasPerformance": paths["performance"].is_file(),
    }
