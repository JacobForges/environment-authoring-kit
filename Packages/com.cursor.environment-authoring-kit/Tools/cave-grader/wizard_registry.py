#!/usr/bin/env python3
"""Wizard tab registry — routes API ids to planner backends."""
from __future__ import annotations

from pathlib import Path
from typing import Any

import build_planner as terrain_planner
import content_planner as surface_planner
from wizard_tabs import GENERIC_PLANNERS  # noqa: F401 — re-export for pipeline

MANIFEST_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildWizardManifest.json")
PIPELINE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildWizardPipelineRun.json")

# Ordered pipeline tabs 1–6 (tab 7 video excluded)
PIPELINE_TAB_IDS = [
    "terrain",
    "surface-content",
    "caves",
    "mazes",
    "interior-content",
    "atmosphere",
]

TAB_DEFS: list[dict[str, Any]] = [
    {
        "id": "terrain",
        "label": "Terrain",
        "hash": "terrain",
        "legacy_hashes": ["world"],
        "kind": "terrain",
        "session": "CaveBuildPlannerSession.json",
        "brief": "CaveBuildPlannerBrief.json",
        "backend": "terrain",
        "pipeline": True,
    },
    {
        "id": "surface-content",
        "label": "Surface content",
        "hash": "surface-content",
        "legacy_hashes": ["content"],
        "kind": "surface_content",
        "session": "CaveBuildContentLayoutSession.json",
        "brief": "CaveBuildContentLayoutBrief.json",
        "backend": "surface",
        "pipeline": True,
    },
    {
        "id": "caves",
        "label": "Caves",
        "hash": "caves",
        "legacy_hashes": [],
        "kind": "caves",
        "session": "CaveBuildCaveSession.json",
        "brief": "CaveBuildCaveBrief.json",
        "backend": "generic",
        "pipeline": True,
    },
    {
        "id": "mazes",
        "label": "Mazes",
        "hash": "mazes",
        "legacy_hashes": [],
        "kind": "mazes",
        "session": "CaveBuildMazeSession.json",
        "brief": "CaveBuildMazeBrief.json",
        "backend": "generic",
        "pipeline": True,
    },
    {
        "id": "interior-content",
        "label": "Interior",
        "hash": "interior-content",
        "legacy_hashes": [],
        "kind": "interior_content",
        "session": "CaveBuildInteriorContentSession.json",
        "brief": "CaveBuildInteriorContentBrief.json",
        "backend": "generic",
        "pipeline": True,
    },
    {
        "id": "atmosphere",
        "label": "Atmosphere",
        "hash": "atmosphere",
        "legacy_hashes": [],
        "kind": "atmosphere",
        "session": "CaveBuildAtmosphereSession.json",
        "brief": "CaveBuildAtmosphereBrief.json",
        "backend": "generic",
        "pipeline": True,
    },
    {
        "id": "video",
        "label": "Video",
        "hash": "video",
        "legacy_hashes": [],
        "kind": "video",
        "session": "CaveBuildVideoSession.json",
        "brief": "CaveBuildVideoBrief.json",
        "backend": "video",
        "pipeline": False,
    },
    {
        "id": "music",
        "label": "Music",
        "hash": "music",
        "legacy_hashes": [],
        "kind": "music",
        "session": "CaveBuildMusicSession.json",
        "brief": "CaveBuildMusicBrief.json",
        "backend": "music",
        "pipeline": False,
    },
    {
        "id": "onnx-brains",
        "label": "ONNX Brains",
        "hash": "onnx-brains",
        "legacy_hashes": [],
        "kind": "onnx",
        "session": "CaveBuildOnnxBrainSession.json",
        "brief": "CaveBuildOnnxBrainConfig.json",
        "backend": "onnx",
        "pipeline": False,
    },
]

TAB_BY_ID = {t["id"]: t for t in TAB_DEFS}
TAB_BY_HASH: dict[str, str] = {}
for t in TAB_DEFS:
    TAB_BY_HASH[t["hash"]] = t["id"]
    for lh in t.get("legacy_hashes") or []:
        TAB_BY_HASH[lh] = t["id"]
TAB_BY_HASH["planner"] = "terrain"
TAB_BY_HASH["content"] = "surface-content"


def resolve_tab_id(tab_or_hash: str) -> str | None:
    key = (tab_or_hash or "").strip().lower()
    if key in TAB_BY_ID:
        return key
    return TAB_BY_HASH.get(key)


def _generic(tab_id: str):
    return GENERIC_PLANNERS[tab_id]


def public_session(hub: Path, tab_id: str) -> dict[str, Any]:
    tab = TAB_BY_ID[tab_id]
    backend = tab["backend"]
    if backend == "terrain":
        sess = terrain_planner.public_session(hub, side_effects=False)
        sess["tabId"] = "terrain"
        sess["helpIntro"] = _help_intro("terrain")
        return sess
    if backend == "surface":
        sess = surface_planner.public_session(hub)
        sess["tabId"] = "surface-content"
        sess["helpIntro"] = _help_intro("surface-content")
        return sess
    if backend == "generic":
        return _generic(tab_id).public_session(hub)
    if backend == "video":
        return {
            "tabId": "video",
            "phase": "idle",
            "messages": [],
            "checklist": [],
            "helpIntro": _help_intro("video"),
            "embedUrl": "http://127.0.0.1:8767/",
        }
    if backend == "music":
        return {
            "tabId": "music",
            "phase": "idle",
            "messages": [],
            "checklist": [],
            "helpIntro": _help_intro("music"),
            "embedUrl": "http://127.0.0.1:8767/?music=1",
        }
    if backend == "onnx":
        return {
            "tabId": "onnx-brains",
            "phase": "idle",
            "messages": [],
            "checklist": [],
            "helpIntro": _help_intro("onnx-brains"),
        }
    raise RuntimeError(f"Unknown backend {backend}")


def start_session(hub: Path, tab_id: str, message: str, **kwargs) -> dict[str, Any]:
    if tab_id == "terrain":
        return terrain_planner.start_session(hub, message, bool(kwargs.get("internetResearch")))
    if tab_id == "surface-content":
        return surface_planner.start_session(
            hub, message, internet_research=bool(kwargs.get("internetResearch", True))
        )
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].start_session(
            hub, message, internet_research=bool(kwargs.get("internetResearch", True))
        )
    raise RuntimeError(f"Tab {tab_id} cannot start via generic start")


def chat_turn(hub: Path, tab_id: str, message: str) -> dict[str, Any]:
    if tab_id == "terrain":
        return terrain_planner.chat_turn(hub, message)
    if tab_id == "surface-content":
        return surface_planner.chat_turn(hub, message)
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].chat_turn(hub, message)
    raise RuntimeError(f"Tab {tab_id} has no chat")


def resume_qna(hub: Path, tab_id: str) -> dict[str, Any]:
    if tab_id == "terrain":
        return terrain_planner.resume_qna(hub)
    if tab_id == "surface-content":
        return surface_planner.resume_qna(hub)
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].resume_qna(hub)
    raise RuntimeError(f"Tab {tab_id} has no resume")


def approve(hub: Path, tab_id: str, approved: bool, feedback: str = "") -> dict[str, Any]:
    if tab_id == "terrain":
        # Terrain uses plan approval as finalize for pipeline
        if approved:
            return terrain_planner.approve_plan(hub, True)
        return terrain_planner.reset_session(hub, "plan")
    if tab_id == "surface-content":
        return surface_planner.approve_layout(hub, approved, feedback)
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].approve_brief(hub, approved, feedback)
    raise RuntimeError(f"Tab {tab_id} has no approve")


def reset_session(hub: Path, tab_id: str) -> dict[str, Any]:
    if tab_id == "terrain":
        terrain_planner.reset_session(hub, "full")
        from wizard_manifest import refresh_manifest

        refresh_manifest(hub)
        return public_session(hub, tab_id)
    if tab_id == "surface-content":
        return surface_planner.reset_session(hub)
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].reset_session(hub)
    return public_session(hub, tab_id)


def auto_respond_step(hub: Path, tab_id: str, preset: str, **kwargs) -> dict[str, Any]:
    if tab_id == "terrain":
        return terrain_planner.auto_respond_step(
            hub, preset, internet_research=bool(kwargs.get("internetResearch"))
        )
    if tab_id == "surface-content":
        return surface_planner.auto_respond_step(
            hub, preset, internet_research=bool(kwargs.get("internetResearch", True))
        )
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].auto_respond_step(
            hub, preset, internet_research=bool(kwargs.get("internetResearch", True))
        )
    raise RuntimeError(f"Tab {tab_id} has no auto-respond")


def auto_respond_until_done(hub: Path, tab_id: str, preset: str, **kwargs) -> dict[str, Any]:
    if tab_id == "terrain":
        return terrain_planner.auto_respond_until_concept(
            hub, preset, internet_research=bool(kwargs.get("internetResearch"))
        )
    if tab_id == "surface-content":
        return surface_planner.auto_respond_until_approval(
            hub, preset, internet_research=bool(kwargs.get("internetResearch", True))
        )
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].auto_respond_until_approval(
            hub, preset, internet_research=bool(kwargs.get("internetResearch", True))
        )
    raise RuntimeError(f"Tab {tab_id} has no auto-respond")


def session_pulse(hub: Path, tab_id: str) -> dict[str, Any]:
    tab = TAB_BY_ID[tab_id]
    backend = tab["backend"]
    if backend == "terrain":
        return terrain_planner.session_pulse(hub)
    if backend == "surface":
        return surface_planner.session_pulse(hub)
    if backend == "generic":
        return GENERIC_PLANNERS[tab_id].session_pulse(hub)
    return {"phase": None, "messages": []}


def session_busy(hub: Path, tab_id: str) -> bool:
    import wizard_session_public as wsp

    if tab_id == "terrain":
        doc = terrain_planner._read_session(hub) if hasattr(terrain_planner, "_read_session") else None
        if not doc:
            from envkit_paths import planner_session_read_path

            p = planner_session_read_path(hub)
            if p and p.is_file():
                import json

                doc = json.loads(p.read_text(encoding="utf-8"))
        return wsp.session_busy(doc)
    if tab_id == "surface-content":
        from content_planner import _read_session

        return wsp.session_busy(_read_session(hub))
    if tab_id in GENERIC_PLANNERS:
        return wsp.session_busy(GENERIC_PLANNERS[tab_id]._read_session(hub))
    return False


def export_tab(hub: Path, tab_id: str) -> dict[str, Any]:
    if tab_id == "terrain":
        from wizard_export import write_terrain_export

        return write_terrain_export(hub)
    if tab_id == "surface-content":
        from wizard_export import write_surface_export

        return write_surface_export(hub)
    if tab_id in GENERIC_PLANNERS:
        doc = GENERIC_PLANNERS[tab_id]._read_session(hub)
        if not doc:
            raise RuntimeError("No session")
        return GENERIC_PLANNERS[tab_id].export_bundle(hub)
    raise RuntimeError(f"Tab {tab_id} cannot export")


def tab_complete(hub: Path, tab_id: str) -> bool:
    sess = public_session(hub, tab_id)
    phase = sess.get("phase")
    if tab_id == "terrain":
        return phase == "finalized"
    return phase == "finalized" and bool(sess.get("gradePassed", True))


def _help_intro(tab_id: str) -> str:
    intros = {
        "terrain": """## Terrain

Plan procedural **world scope** — tile footprint, surface trails, caves pipeline flags, nav.

**Not here:** MainScene NPC dialog (Surface content), maze layout (Mazes), video scripts (Video).

Click **Start planning** or use **AI Responder** presets.""",
        "surface-content": """## Surface content

Plan **MainScene / surface** NPCs, monsters, items, quests, hybrid Talk+Shop dialog.

**Not here:** terrain tiles (Terrain), cave room graphs (Caves).

**Output:** `CaveBuildContentLayoutBrief.json`""",
        "video": """## Video & cutscenes

AI Director studio — cinematic scripts, segments, pacing, export.

**Requires** AI Director on port **8767**. Tie into Atmosphere cutscene triggers and quest storylines.

**Not included** in the automated 1→6 pipeline.""",
        "music": """## Music production

AI Director Studio **music mode** — briefing checklist, instrumental (generate or upload), vocal record, produce master, optional handoff to music video.

**Requires** `ai-director-studio` at repo root and AI Director on port **8767**.

**Not included** in the automated 1→6 pipeline.""",
        "onnx-brains": """## ONNX brains

Tune per-tab ONNX validator settings (enabled, threshold, model override path).

This tab updates ONNX runtime configuration only; it does not change world/content briefs directly.""",
    }
    if tab_id in intros:
        return intros[tab_id]
    if tab_id in GENERIC_PLANNERS:
        return GENERIC_PLANNERS[tab_id].config.help_intro
    return ""
