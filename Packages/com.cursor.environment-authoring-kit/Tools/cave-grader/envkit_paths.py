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


def server_runtime_dir() -> Path:
    """PID/log files for long-running EnvKit servers — on external drive when mounted."""
    path = resolve_envkit_root() / ".server-runtime"
    path.mkdir(parents=True, exist_ok=True)
    return path


def recap_temp_dir() -> Path:
    """Heavy ffmpeg/Pillow temp — always on EnvKit data root (Lexar when mounted)."""
    path = resolve_envkit_root() / ".recap-tmp"
    path.mkdir(parents=True, exist_ok=True)
    return path


def _is_local_volume(path: Path) -> bool:
    """True when path is on internal disk (tsx IPC pipes fail with ENOTSUP on many externals)."""
    try:
        resolved = path.resolve()
    except OSError:
        return False
    parts = resolved.parts
    return not (len(parts) >= 2 and parts[1] == "Volumes")


def planner_ipc_temp_dir() -> Path:
    """tsx IPC pipes (Unix domain sockets) — local disk only.

    Hub/Library/EnvironmentKit often symlinks to Lexar; resolve_envkit_root() is external too.
    Keep planner/tsx temp under ~/Library/EnvironmentKit on the internal SSD.
    """
    candidates = (
        Path.home() / "Library" / "EnvironmentKit" / ".planner-tmp",
        Path("/tmp") / "environmentkit-planner",
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
    fallback = Path("/tmp/environmentkit-planner")
    fallback.mkdir(parents=True, exist_ok=True)
    return fallback


def ensure_planner_process_env() -> Path:
    """Pin planner/tsx temp to local disk — override Lexar recap TMPDIR in long-lived servers."""
    tmp = planner_ipc_temp_dir()
    os.environ["TMPDIR"] = str(tmp)
    os.environ["TEMP"] = str(tmp)
    os.environ["TMP"] = str(tmp)
    return tmp


def planner_tmpdir_ok() -> bool:
    """Health check: current TMPDIR must be local (not /Volumes/Lexar/.../.planner-tmp)."""
    raw = os.environ.get("TMPDIR", "").strip()
    if not raw:
        return False
    try:
        return _is_local_volume(Path(raw))
    except OSError:
        return False


def preview_mirror_dir() -> Path:
    """Preview MP4 shortcut folder — on external drive when available."""
    path = resolve_envkit_root() / "DesktopMirror"
    path.mkdir(parents=True, exist_ok=True)
    return path


def ensure_recap_process_env() -> Path:
    """Pin recap work to EnvKit data root (Lexar when mounted) — always override TMPDIR."""
    root = resolve_envkit_root()
    tmp = recap_temp_dir()
    os.environ[ENV_VAR] = str(root)
    # Never inherit Cursor/macOS /var/folders — that fills the internal SSD.
    os.environ["TMPDIR"] = str(tmp)
    os.environ["TEMP"] = str(tmp)
    os.environ["TMP"] = str(tmp)
    return root


def unity_recap_scratch_dir() -> Path:
    """Unity batchmode logs/scratch — on external EnvKit root, not Hub project disk."""
    path = resolve_envkit_root() / ".recap-unity"
    path.mkdir(parents=True, exist_ok=True)
    return path


PLANNER_SESSION_LEGACY_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildPlannerSession.json")
PLANNER_SESSION_REL = Path("Library/EnvironmentKit/CaveBuildPlannerSession.json")


def planner_session_path(hub: Path) -> Path:
    """Live planner Q&A JSON — prefer Library/ (Unity does not import) over legacy Assets/ path."""
    hub = Path(hub).expanduser().resolve()
    primary = hub / PLANNER_SESSION_REL
    legacy = hub / PLANNER_SESSION_LEGACY_REL
    if primary.is_file():
        return primary
    if legacy.is_file():
        return legacy
    primary.parent.mkdir(parents=True, exist_ok=True)
    return primary


def planner_session_write_path(hub: Path) -> Path:
    """Target path for all planner session writes (outside Assets/)."""
    hub = Path(hub).expanduser().resolve()
    path = hub / PLANNER_SESSION_REL
    path.parent.mkdir(parents=True, exist_ok=True)
    return path


def atomic_write_text(path: Path, text: str) -> None:
    """Write UTF-8 text atomically so Unity/file watchers never see a partial file."""
    import os
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


def mac_data_volume_free_gb() -> float | None:
    """Free space on macOS Data volume (GB), or None if unknown."""
    try:
        import shutil

        usage = shutil.disk_usage("/System/Volumes/Data")
        return usage.free / (1024**3)
    except OSError:
        return None
