#!/usr/bin/env python3
"""Purge ResearchCache + catalog to bare minimum for planner speed-demo builds."""
from __future__ import annotations

import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
HUB = TOOLS.parents[3] if (TOOLS.parents[3] / "Assets").is_dir() else Path.cwd()

MINIMAL_ENGINE_REFS = [
    {
        "title": "Unity 6 — CharacterController.Move and collision",
        "year": 2026,
        "venue": "Unity 6 Manual",
        "url": "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html",
        "topics": "jump, gravity, platformer movement",
    },
    {
        "title": "Unity 6 — NavMesh and OffMeshLink",
        "year": 2026,
        "venue": "Unity 6 Manual",
        "url": "https://docs.unity3d.com/6000.0/Documentation/Manual/NavMesh-OffMeshLink-Animation.html",
        "topics": "navmesh, platform bridges, AI path",
    },
    {
        "title": "Unity 6 — Triggers and OnTriggerEnter",
        "year": 2026,
        "venue": "Unity 6 Manual",
        "url": "https://docs.unity3d.com/6000.0/Documentation/Manual/CollidersOverview.html",
        "topics": "kill volume, respawn triggers",
    },
]

MINIMAL_SEED = {
    "minResearchYear": 2025,
    "policy": "Minimal — planner speed-demo only. Unity 6 engine refs; no lab papers or Florida LiDAR.",
    "labIndices": {},
    "papers": [],
    "engineReferences": MINIMAL_ENGINE_REFS,
    "visualReferences": [],
    "researchCache": {
        "indexRel": "Assets/EnvironmentKit/ResearchCache/index.json",
        "policy": "Empty — planner session is authoritative; research gate skipped for speed demo.",
    },
    "floridaTerrain": {
        "hillshades": [],
        "hillshadeIndexPath": None,
        "syncCommands": [],
    },
    "dataAttribution": "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md",
    "stats": {
        "labCount": 0,
        "paperCount": 0,
        "visualRefCount": 0,
        "floridaAquiferRefs": 0,
    },
}


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat()


def _count_seed(seed: dict) -> int:
    n = 0
    n += len(seed.get("papers") or [])
    n += len(seed.get("visualReferences") or [])
    n += len(seed.get("engineReferences") or [])
    n += len(seed.get("labIndices") or {})
    ft = seed.get("floridaTerrain") or {}
    n += len(ft.get("hillshades") or [])
    return n


def _count_cache_index(index_path: Path) -> int:
    if not index_path.is_file():
        return 0
    try:
        doc = json.loads(index_path.read_text(encoding="utf-8"))
        return len(doc.get("entries") or [])
    except Exception:
        return 0


def _purge_cache_root(cache_root: Path) -> tuple[int, int]:
    """Returns (entries_removed, folders_removed)."""
    entries_removed = _count_cache_index(cache_root / "index.json")
    folders_removed = 0
    entries_dir = cache_root / "entries"
    if entries_dir.is_dir():
        for child in list(entries_dir.iterdir()):
            if child.is_dir():
                shutil.rmtree(child, ignore_errors=True)
                folders_removed += 1
            elif child.is_file() and child.name != ".gitkeep":
                child.unlink(missing_ok=True)

    # Strip bulky image trees except planner concept
    images = cache_root / "images"
    keep_planner = {
        "concepts/phases/_pending-review/planner-session/concept.png",
        "fullworld-concepts/planner/concept.png",
    }
    if images.is_dir():
        for png in list(images.rglob("*.png")):
            rel = str(png.relative_to(images)).replace("\\", "/")
            if rel not in keep_planner and "planner-session" not in rel:
                png.unlink(missing_ok=True)

    minimal_index = {
        "version": 1,
        "generatedUtc": _utc(),
        "policy": "Minimal — planner build only. No web research entries.",
        "hubRelativeRoot": "Assets/EnvironmentKit/ResearchCache",
        "categories": {
            "planner": {
                "id": "planner",
                "label": "Planner session (authoritative)",
                "entryIds": [],
            }
        },
        "entries": [],
    }
    cache_root.mkdir(parents=True, exist_ok=True)
    (cache_root / "index.json").write_text(json.dumps(minimal_index, indent=2) + "\n", encoding="utf-8")
    return entries_removed, folders_removed


def _purge_generated(hub: Path) -> int:
    gen = hub / "Assets/EnvironmentKit/Generated"
    names = [
        "CaveBuildResearchActionPlan.json",
        "CaveBuildResearchAgentPrompt.json",
        "CaveBuildResearchAgentPrompt.md",
        "CaveBuildResearchCache.json",
        "CaveBuildPrePlacementResearchGate.json",
        "_planner_research_bundle.json",
    ]
    removed = 0
    for name in names:
        for p in [gen / name, gen / f"{name}.meta"]:
            if p.is_file():
                p.unlink()
                removed += 1
    return removed


def _build_active(hub: Path) -> bool:
    state_path = hub / "Assets/EnvironmentKit/Generated/CaveBuildWizardState.json"
    if not state_path.is_file():
        return False
    try:
        state = json.loads(state_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return False
    return state.get("phase") == "started" and not state.get("cancelled", False)


def main() -> None:
    hub = HUB
    if _build_active(hub):
        print(
            json.dumps(
                {
                    "error": "Build pipeline is running — purge aborted.",
                    "hint": "Cancel or wait for the Hub build to finish before purging ResearchCache.",
                },
                indent=2,
            )
        )
        raise SystemExit(2)
    seed_path = TOOLS / "research-catalog.seed.json"
    old_seed = json.loads(seed_path.read_text(encoding="utf-8")) if seed_path.is_file() else {}
    before_seed = _count_seed(old_seed)

    local_cache = hub / "Assets/EnvironmentKit/ResearchCache"
    lex_cache = Path("/Volumes/Lexar/EnvironmentKit-Hub/ResearchCache")

    before_local = _count_cache_index(local_cache / "index.json")
    before_lex = _count_cache_index(lex_cache / "index.json") if lex_cache.is_dir() else 0

    local_entries, local_folders = _purge_cache_root(local_cache)
    lex_entries, lex_folders = (0, 0)
    if lex_cache.is_dir():
        lex_entries, lex_folders = _purge_cache_root(lex_cache)

    seed_path.write_text(json.dumps(MINIMAL_SEED, indent=2) + "\n", encoding="utf-8")
    after_seed = _count_seed(MINIMAL_SEED)
    gen_removed = _purge_generated(hub)

    seed_eliminated = before_seed - after_seed
    cache_eliminated = before_local + before_lex
    total = seed_eliminated + cache_eliminated

    report = {
        "purgedUtc": _utc(),
        "seedEntriesBefore": before_seed,
        "seedEntriesAfter": after_seed,
        "seedEntriesEliminated": seed_eliminated,
        "localCacheEntriesEliminated": before_local,
        "localCacheFoldersRemoved": local_folders,
        "lexarCacheEntriesEliminated": before_lex,
        "lexarCacheFoldersRemoved": lex_folders,
        "generatedFilesRemoved": gen_removed,
        "totalEntriesEliminated": total,
    }
    out = hub / "Assets/EnvironmentKit/Generated/CaveBuildResearchPurgeReport.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
