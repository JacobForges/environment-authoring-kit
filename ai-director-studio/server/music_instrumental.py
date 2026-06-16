"""AI instrumental generation (Suno API) or upload fallback."""
from __future__ import annotations

import json
import os
import shutil
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

from music_project import music_dir, read_music_project, update_music_project

SUNO_API_URL = os.environ.get("SUNO_API_URL", "https://api.sunoapi.org/api/v1/generate")


def suno_configured() -> bool:
    return bool(os.environ.get("SUNO_API_KEY", "").strip())


def save_instrumental_upload(project: Path, src: Path) -> dict[str, Any]:
    if not read_music_project(project):
        raise ValueError("not a music project")
    mdir = music_dir(project)
    dest = mdir / "instrumental.mp3"
    if src.suffix.lower() == ".mp3":
        shutil.copy2(src, dest)
    else:
        import subprocess

        ffmpeg = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
        subprocess.run(
            [ffmpeg, "-y", "-i", str(src), "-codec:a", "libmp3lame", "-q:a", "2", str(dest)],
            check=True,
            capture_output=True,
            timeout=120,
        )
    update_music_project(
        project,
        {"status": {"instrumentalReady": True, "instrumentalSource": "upload"}},
    )
    return {"ok": True, "instrumental": str(dest), "source": "upload"}


def generate_instrumental(project: Path, prompt: str | None = None) -> dict[str, Any]:
    if not read_music_project(project):
        raise ValueError("not a music project")
    doc = read_music_project(project) or {}
    api_key = os.environ.get("SUNO_API_KEY", "").strip()
    if not api_key:
        return {
            "ok": False,
            "error": "SUNO_API_KEY not configured — upload an instrumental MP3 instead.",
            "sunoConfigured": False,
            "needsUpload": True,
        }
    text = (prompt or doc.get("instrumentalPrompt") or "pop trap instrumental dark 140 bpm").strip()
    payload = json.dumps(
        {
            "prompt": text,
            "make_instrumental": True,
            "wait_audio": True,
        }
    ).encode("utf-8")
    req = urllib.request.Request(
        SUNO_API_URL,
        data=payload,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {api_key}",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=180) as resp:
            data = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        return {"ok": False, "error": f"Suno API error: {exc.code} {body[:200]}", "sunoConfigured": True}
    except Exception as exc:
        return {"ok": False, "error": str(exc), "sunoConfigured": True}

    audio_url = None
    if isinstance(data, dict):
        for key in ("audio_url", "audioUrl", "url"):
            if data.get(key):
                audio_url = data[key]
                break
        clips = data.get("clips") or data.get("data") or []
        if not audio_url and clips and isinstance(clips, list):
            first = clips[0] if clips else {}
            if isinstance(first, dict):
                audio_url = first.get("audio_url") or first.get("audioUrl") or first.get("url")

    if not audio_url:
        return {
            "ok": False,
            "error": "Suno returned no audio URL — upload instrumental instead.",
            "sunoConfigured": True,
            "needsUpload": True,
            "raw": data,
        }

    mdir = music_dir(project)
    dest = mdir / "instrumental.mp3"
    try:
        with urllib.request.urlopen(audio_url, timeout=120) as audio_resp:
            dest.write_bytes(audio_resp.read())
    except Exception as exc:
        return {"ok": False, "error": f"Failed to download instrumental: {exc}", "sunoConfigured": True}

    update_music_project(
        project,
        {
            "instrumentalPrompt": text,
            "status": {"instrumentalReady": True, "instrumentalSource": "suno", "sunoConfigured": True},
        },
    )
    return {"ok": True, "instrumental": str(dest), "source": "suno", "prompt": text}
