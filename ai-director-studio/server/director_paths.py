"""Standalone AI Director data paths — no Unity Hub coupling."""
from __future__ import annotations

import os
import shutil
from pathlib import Path

ENV_VAR = "DIRECTOR_DATA_ROOT"
# Legacy alias used by vendored recap compose scripts
ENVIRONMENT_KIT_DATA_ROOT = ENV_VAR
STUDIO_ROOT = Path(__file__).resolve().parent.parent


def is_standalone() -> bool:
    return os.environ.get("STANDALONE", "1").strip().lower() in ("1", "true", "yes")


def resolve_data_root() -> Path:
    override = os.environ.get(ENV_VAR, "").strip()
    if override:
        path = Path(override).expanduser()
    else:
        path = Path.home() / "Library" / "AIDirectorStudio"
    path.mkdir(parents=True, exist_ok=True)
    return path


# Vendored recap code expects this name
resolve_envkit_root = resolve_data_root


def projects_dir() -> Path:
    path = resolve_data_root() / "projects"
    path.mkdir(parents=True, exist_ok=True)
    return path


def approved_dir() -> Path:
    path = resolve_data_root() / "approved"
    path.mkdir(parents=True, exist_ok=True)
    return path


def approved_cards_path() -> Path:
    return approved_dir() / "ApprovedCards.json"


def server_runtime_dir() -> Path:
    path = resolve_data_root() / ".server-runtime"
    path.mkdir(parents=True, exist_ok=True)
    return path


def recap_temp_dir() -> Path:
    path = resolve_data_root() / ".recap-tmp"
    path.mkdir(parents=True, exist_ok=True)
    return path


def _is_local_volume(path: Path) -> bool:
    try:
        resolved = path.resolve()
    except OSError:
        return False
    parts = resolved.parts
    return not (len(parts) >= 2 and parts[1] == "Volumes")


def planner_ipc_temp_dir() -> Path:
    candidates = (
        Path.home() / "Library" / "AIDirectorStudio" / ".planner-tmp",
        Path("/tmp") / "ai-director-studio",
    )
    for candidate in candidates:
        try:
            candidate.mkdir(parents=True, exist_ok=True)
            resolved = candidate.resolve()
            if not _is_local_volume(resolved):
                continue
            probe = resolved / ".write_probe"
            probe.write_text("ok", encoding="utf-8")
            probe.unlink(missing_ok=True)
            return resolved
        except OSError:
            continue
    fallback = Path("/tmp/ai-director-studio")
    fallback.mkdir(parents=True, exist_ok=True)
    return fallback


def ensure_planner_process_env() -> Path:
    tmp = planner_ipc_temp_dir()
    os.environ["TMPDIR"] = str(tmp)
    os.environ["TEMP"] = str(tmp)
    os.environ["TMP"] = str(tmp)
    return tmp


def ensure_recap_process_env() -> Path:
    root = resolve_data_root()
    tmp = recap_temp_dir()
    os.environ[ENV_VAR] = str(root)
    os.environ["TMPDIR"] = str(tmp)
    os.environ["TEMP"] = str(tmp)
    os.environ["TMP"] = str(tmp)
    return root


def atomic_write_text(path: Path, text: str) -> None:
    import tempfile

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp_name = tempfile.mkstemp(
        prefix=f".{path.name}.",
        suffix=".tmp",
        dir=str(path.parent),
    )
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write(text)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp_name, path)
    except Exception:
        try:
            os.unlink(tmp_name)
        except OSError:
            pass
        raise


def studio_repo_root() -> Path:
    return STUDIO_ROOT


def list_projects() -> list[Path]:
    root = projects_dir()
    runs = [
        p
        for p in root.iterdir()
        if p.is_dir() and not p.name.startswith(".")
    ]
    return sorted(runs, key=lambda p: p.stat().st_mtime, reverse=True)


def latest_project() -> Path | None:
    for project in list_projects():
        if (project / "timelapse").is_dir() and any((project / "timelapse").glob("tl_*.png")):
            return project
    return list_projects()[0] if list_projects() else None


def resolve_project(path: str | None) -> Path | None:
    if not path:
        return latest_project()
    p = Path(path).expanduser().resolve()
    return p if p.is_dir() else None


def create_project(name: str | None = None) -> Path:
    from datetime import datetime

    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    slug = "".join(c if c.isalnum() or c in "-_" else "-" for c in (name or "project").strip().lower())
    slug = slug.strip("-") or "project"
    project = projects_dir() / f"{stamp}-{slug}"
    project.mkdir(parents=True, exist_ok=True)
    (project / "timelapse").mkdir(exist_ok=True)
    (project / "uploads").mkdir(exist_ok=True)
    (project / "uploads" / "playthroughs").mkdir(parents=True, exist_ok=True)
    return project


def mac_data_volume_free_gb() -> float | None:
    try:
        usage = shutil.disk_usage("/System/Volumes/Data")
        return usage.free / (1024**3)
    except OSError:
        return None
