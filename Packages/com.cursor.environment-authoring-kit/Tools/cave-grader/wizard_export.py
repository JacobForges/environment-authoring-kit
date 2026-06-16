#!/usr/bin/env python3
"""Export finalized tab plans to Desktop."""
from __future__ import annotations

import json
import shutil
from datetime import datetime
from pathlib import Path
from typing import Any

import build_planner as bp
import content_planner as cp
from generic_tab_planner import TabPlannerConfig


def _stamp() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S")


def _write_bundle(out_dir: Path, tab_label: str, brief: dict, session_doc: dict, system_text: str) -> dict[str, Any]:
    out_dir.mkdir(parents=True, exist_ok=True)
    brief_path = out_dir / f"{tab_label}-brief.json"
    transcript_path = out_dir / f"{tab_label}-transcript.md"
    prompt_path = out_dir / f"{tab_label}-prompt-snapshot.txt"
    brief_path.write_text(json.dumps(brief, indent=2) + "\n", encoding="utf-8")
    lines = [f"# {tab_label} session transcript\n"]
    for m in session_doc.get("messages") or []:
        lines.append(f"## {m.get('role', '?')}\n\n{m.get('content', '')}\n")
    if session_doc.get("checklist"):
        lines.append("\n## Checklist\n")
        for c in session_doc["checklist"]:
            lines.append(f"- [{('x' if c.get('done') else ' ')}] {c.get('label')}: {c.get('decision', '')}\n")
    transcript_path.write_text("".join(lines), encoding="utf-8")
    prompt_path.write_text(system_text or "(no system prompt snapshot)\n", encoding="utf-8")
    return {
        "ok": True,
        "folder": str(out_dir),
        "files": [str(brief_path), str(transcript_path), str(prompt_path)],
    }


def write_tab_export(hub: Path, config: TabPlannerConfig, session_doc: dict[str, Any]) -> dict[str, Any]:
    brief_path = hub / config.brief_rel
    brief = json.loads(brief_path.read_text(encoding="utf-8")) if brief_path.is_file() else session_doc.get("brief") or {}
    desktop = Path.home() / "Desktop" / f"CaveBuild-{config.tab_id}-{_stamp()}"
    return _write_bundle(desktop, config.tab_id, brief, session_doc, config.system_qna)


def write_surface_export(hub: Path) -> dict[str, Any]:
    p = hub / cp.SESSION_REL
    if not p.is_file():
        raise RuntimeError("No surface content session")
    doc = json.loads(p.read_text(encoding="utf-8"))
    if doc.get("phase") != "finalized":
        raise RuntimeError("Surface content not finalized")
    brief_path = hub / cp.BRIEF_REL
    brief = json.loads(brief_path.read_text(encoding="utf-8")) if brief_path.is_file() else {}
    desktop = Path.home() / "Desktop" / f"CaveBuild-surface-content-{_stamp()}"
    return _write_bundle(desktop, "surface-content", brief, doc, cp.SYSTEM_QNA)


def write_terrain_export(hub: Path) -> dict[str, Any]:
    doc = bp._read_session(hub) if hasattr(bp, "_read_session") else None
    if not doc:
        from envkit_paths import planner_session_read_path

        sp = planner_session_read_path(hub)
        if sp and sp.is_file():
            doc = json.loads(sp.read_text(encoding="utf-8"))
    if not doc:
        raise RuntimeError("No terrain session")
    brief_path = hub / bp.BRIEF_REL
    brief = json.loads(brief_path.read_text(encoding="utf-8")) if brief_path.is_file() else {}
    desktop = Path.home() / "Desktop" / f"CaveBuild-terrain-{_stamp()}"
    system = getattr(bp, "SYSTEM_QNA", "terrain planner")
    return _write_bundle(desktop, "terrain", brief, doc, system)
