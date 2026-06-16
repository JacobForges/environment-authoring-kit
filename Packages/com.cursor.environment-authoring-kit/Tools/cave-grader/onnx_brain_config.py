#!/usr/bin/env python3
"""ONNX brain config persistence + env application for wizard tabs."""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

from world_tab_brains.model_paths import BRAIN_MODEL_EXT, brain_model_filename

CONFIG_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildOnnxBrainConfig.json")
MODELS_REL = Path("Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/world_tab_brains/models")

TAB_IDS = [
    "terrain",
    "surface-content",
    "caves",
    "mazes",
    "interior-content",
    "atmosphere",
    "music",
    "video",
]

TAB_ENV_SUFFIX = {
    "terrain": "TERRAIN",
    "surface-content": "SURFACE",
    "caves": "CAVES",
    "mazes": "MAZES",
    "interior-content": "INTERIOR",
    "atmosphere": "ATMOSPHERE",
    "music": "MUSIC",
    "video": "VIDEO",
}


def _default_tab(tab_id: str) -> dict[str, Any]:
    return {
        "id": tab_id,
        "enabled": True,
        "threshold": None,
        "modelPath": "",
    }


def default_config() -> dict[str, Any]:
    return {
        "version": 1,
        "global": {"enabled": True, "threshold": 0.70},
        "tabs": {tab_id: _default_tab(tab_id) for tab_id in TAB_IDS},
    }


def _config_path(hub: Path) -> Path:
    return hub / CONFIG_REL


def _models_root(hub: Path) -> Path:
    return hub / MODELS_REL


def _recommended_model_filename(tab_id: str) -> str:
    return brain_model_filename(tab_id)


def _recommended_tab_threshold(tab_id: str) -> float | None:
    # Music model currently has lower held-out accuracy; keep it slightly looser.
    if tab_id == "music":
        return 0.62
    return None


def _apply_recommended_defaults(hub: Path, cfg: dict[str, Any]) -> dict[str, Any]:
    out = normalize_config(cfg)
    root = _models_root(hub)
    tabs = out.get("tabs") or {}
    for tab_id in TAB_IDS:
        t = tabs.get(tab_id) if isinstance(tabs, dict) else None
        if not isinstance(t, dict):
            t = _default_tab(tab_id)
            tabs[tab_id] = t
        model_name = _recommended_model_filename(tab_id)
        if model_name:
            model_file = root / model_name
            # Auto-heal obvious mis-defaults (e.g. all tabs accidentally set to surface model).
            chosen = str(t.get("modelPath") or "").strip()
            if model_file.is_file() and (
                not chosen
                or Path(chosen).name == brain_model_filename("surface-content") and tab_id != "surface-content"
            ):
                t["modelPath"] = model_name
            # If model exists for this tab, enable by default.
            if model_file.is_file() and not bool(t.get("enabled", True)):
                t["enabled"] = True
            if not model_file.is_file():
                t["enabled"] = False
        rec_th = _recommended_tab_threshold(tab_id)
        if rec_th is not None and t.get("threshold") in (None, "", "null"):
            t["threshold"] = rec_th
    out["tabs"] = tabs
    return out


def normalize_config(raw: dict[str, Any] | None) -> dict[str, Any]:
    base = default_config()
    if not isinstance(raw, dict):
        return base
    g = raw.get("global")
    if isinstance(g, dict):
        base["global"]["enabled"] = bool(g.get("enabled", True))
        try:
            base["global"]["threshold"] = float(g.get("threshold", 0.70))
        except Exception:
            base["global"]["threshold"] = 0.70

    tabs_raw = raw.get("tabs")
    if isinstance(tabs_raw, dict):
        for tab_id in TAB_IDS:
            t = tabs_raw.get(tab_id)
            if not isinstance(t, dict):
                continue
            base["tabs"][tab_id]["enabled"] = bool(t.get("enabled", True))
            th = t.get("threshold", None)
            if th in (None, "", "null"):
                base["tabs"][tab_id]["threshold"] = None
            else:
                try:
                    base["tabs"][tab_id]["threshold"] = float(th)
                except Exception:
                    base["tabs"][tab_id]["threshold"] = None
            mp = str(t.get("modelPath") or "").strip()
            base["tabs"][tab_id]["modelPath"] = mp
    return base


def read_config(hub: Path) -> dict[str, Any]:
    p = _config_path(hub)
    if not p.is_file():
        return _apply_recommended_defaults(hub, default_config())
    try:
        raw = json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return _apply_recommended_defaults(hub, default_config())
    return _apply_recommended_defaults(hub, normalize_config(raw))


def write_config(hub: Path, cfg: dict[str, Any]) -> dict[str, Any]:
    normalized = _apply_recommended_defaults(hub, cfg)
    p = _config_path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(normalized, indent=2) + "\n", encoding="utf-8")
    return normalized


def _resolve_model_path(raw: str, hub: Path) -> str:
    txt = (raw or "").strip()
    if not txt:
        return ""
    p = Path(txt).expanduser()
    if p.is_absolute():
        return str(p)
    # Relative paths resolve from models dir by default.
    return str((hub / MODELS_REL / txt).resolve())


def apply_config_to_env(cfg: dict[str, Any], hub: Path) -> None:
    global_cfg = cfg.get("global") or {}
    os.environ["WORLD_TAB_BRAIN_ONNX"] = "1" if bool(global_cfg.get("enabled", True)) else "0"
    os.environ["WORLD_TAB_BRAIN_ONNX_THRESHOLD"] = str(float(global_cfg.get("threshold", 0.70)))

    tabs = cfg.get("tabs") or {}
    for tab_id in TAB_IDS:
        suffix = TAB_ENV_SUFFIX.get(tab_id, tab_id.upper().replace("-", "_"))
        tcfg = tabs.get(tab_id) if isinstance(tabs, dict) else None
        if not isinstance(tcfg, dict):
            tcfg = _default_tab(tab_id)
        os.environ[f"WORLD_TAB_BRAIN_{suffix}_ONNX_ENABLED"] = "1" if bool(tcfg.get("enabled", True)) else "0"

        tab_threshold = tcfg.get("threshold", None)
        env_threshold = f"WORLD_TAB_BRAIN_{suffix}_ONNX_THRESHOLD"
        if tab_threshold in (None, "", "null"):
            os.environ.pop(env_threshold, None)
        else:
            try:
                os.environ[env_threshold] = str(float(tab_threshold))
            except Exception:
                os.environ.pop(env_threshold, None)

        env_path = f"WORLD_TAB_BRAIN_{suffix}_ONNX_PATH"
        model_path = _resolve_model_path(str(tcfg.get("modelPath") or ""), hub)
        if model_path:
            os.environ[env_path] = model_path
        else:
            os.environ.pop(env_path, None)

        # Legacy surface env key used in earlier patches.
        if tab_id == "surface-content":
            if model_path:
                os.environ["WORLD_TAB_BRAIN_SURFACE_ONNX_PATH"] = model_path
            else:
                os.environ.pop("WORLD_TAB_BRAIN_SURFACE_ONNX_PATH", None)


def available_models(hub: Path) -> list[str]:
    root = _models_root(hub)
    if not root.is_dir():
        return []
    out: list[str] = []
    for p in sorted(root.glob(f"*_brain{BRAIN_MODEL_EXT}")):
        out.append(p.name)
    for p in sorted(root.glob("*_brain.onnx")):
        if p.name not in out:
            out.append(p.name)
    return out


def diagnostics(hub: Path, cfg: dict[str, Any]) -> dict[str, Any]:
    tabs_cfg = cfg.get("tabs") or {}
    root = _models_root(hub)
    rows: list[dict[str, Any]] = []
    enabled_tabs = 0
    model_tabs = 0
    for tab_id in TAB_IDS:
        t = tabs_cfg.get(tab_id) if isinstance(tabs_cfg, dict) else None
        if not isinstance(t, dict):
            t = _default_tab(tab_id)
        enabled = bool(t.get("enabled", True))
        model_raw = str(t.get("modelPath") or "")
        resolved = _resolve_model_path(model_raw, hub) if model_raw else ""
        exists = bool(resolved and Path(resolved).is_file())
        manifest = root / f"{tab_id}_brain.manifest.json"
        test_acc = None
        records = None
        if manifest.is_file():
            try:
                doc = json.loads(manifest.read_text(encoding="utf-8"))
                m = doc.get("metrics") or {}
                if isinstance(m, dict):
                    test_acc = m.get("testAccuracy")
                    records = m.get("records")
            except Exception:
                pass
        if enabled:
            enabled_tabs += 1
        if exists:
            model_tabs += 1
        rows.append(
            {
                "tabId": tab_id,
                "enabled": enabled,
                "modelPath": model_raw,
                "resolvedModelPath": resolved,
                "modelExists": exists,
                "testAccuracy": test_acc,
                "records": records,
            }
        )
    return {
        "tabs": rows,
        "enabledTabs": enabled_tabs,
        "tabsWithModelFile": model_tabs,
        "totalTabs": len(TAB_IDS),
    }

