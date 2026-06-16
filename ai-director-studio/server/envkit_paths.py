"""Compatibility shim — vendored EnvKit scripts import envkit_paths."""
from __future__ import annotations

from director_paths import (  # noqa: F401
    ENV_VAR,
    atomic_write_text,
    approved_cards_path,
    approved_dir,
    ensure_planner_process_env,
    ensure_recap_process_env,
    is_standalone,
    mac_data_volume_free_gb,
    planner_ipc_temp_dir,
    recap_temp_dir,
    resolve_data_root,
    resolve_envkit_root,
    resolve_project,
    server_runtime_dir,
    studio_repo_root,
)

# Aliases for capture-centric naming in vendored code
resolve_capture = resolve_project
latest_capture = __import__("director_paths", fromlist=["latest_project"]).latest_project
