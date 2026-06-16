#!/usr/bin/env python3
"""Shared web research + scope guards for Environment Kit wizard Q&A tabs."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import build_planner as bp

TOOLS = Path(__file__).resolve().parent

SENIOR_ENGINEERING_STANDARDS = """## Senior engineering standards (mandatory)
- Write briefs like a staff-level Unity technical designer: concrete ids, coordinates, enums, pipeline flags — no vague placeholders.
- Every checklist `decision` must be specific enough to implement without follow-up (counts, meters, zone ids, feature names).
- Prefer 2025–2026 production patterns: modular ScriptableObject configs, data-driven placement, NavMesh-first walkability, CC0 asset reuse.
- Cite researched sources in `teachingMoment` when they inform a recommendation (title + URL inline).
- JSON brief fields must validate against Unity JsonUtility / existing EnvKit schemas — no invented keys.
- One domain per tab: refuse cross-tab work politely and name the correct wizard tab.
"""

TAB_SCOPE_GUARDS: dict[str, dict[str, Any]] = {
    "terrain": {
        "one_liner": "Procedural world scope ONLY — tiles, terrain passes, cave pipeline flags, planner grid markers.",
        "do_not": [
            "MainScene NPC dialog, hybrid Talk+Shop, quest blockers, agent training gates",
            "cave room graphs (Caves tab), maze topology (Mazes tab), interior loot (Interior tab)",
            "music production, video scripts, atmosphere VFX triggers",
        ],
        "redirect": {
            "surface-content": "MainScene NPCs, dialog, shops, surface enemy patrols",
            "caves": "underground route structure and room graphs",
            "mazes": "terrain labyrinth grids",
            "interior-content": "actors and loot inside caves",
            "atmosphere": "lighting, water, cutscene trigger volumes",
            "music": "pop/trap track production",
            "video": "cinematic scripts and segments",
        },
    },
    "surface-content": {
        "one_liner": "MainScene surface content ONLY — NPCs, enemies, props, dialog, shops, blockers on Polyvania plaza.",
        "do_not": [
            "tileCount, terrain passes, procedural grid scope, cave spline routes",
            "interior cave actors (Interior tab), maze grids (Mazes tab), music/video production",
        ],
        "redirect": {
            "terrain": "world tile footprint and terrain pipeline",
            "caves": "cave/dungeon structure",
            "interior-content": "NPCs and loot inside caves",
            "atmosphere": "sky, fog, water, cutscene triggers",
        },
    },
    "caves": {
        "one_liner": "Underground structure ONLY — entrances, routes, room graphs, depth, nav intent.",
        "do_not": [
            "surface NPC dialog, maze grids, interior loot tables, terrain tileCount",
            "music/video production, atmosphere VFX rendering",
        ],
        "redirect": {
            "terrain": "surface terrain and tile scope",
            "mazes": "terrain labyrinth topology",
            "interior-content": "actors and loot inside caves",
            "atmosphere": "lighting and cutscene triggers",
        },
    },
    "mazes": {
        "one_liner": "Maze/labyrinth topology on terrain ONLY — grid, entrances, loops, landmarks.",
        "do_not": [
            "cave spline routes, surface NPC placement, interior loot, music/video",
        ],
        "redirect": {
            "caves": "procedural cave routes",
            "terrain": "tile footprint and terrain passes",
            "interior-content": "actors inside underground spaces",
        },
    },
    "interior-content": {
        "one_liner": "Interior actors ONLY — NPCs, enemies, props, quests inside caves/dungeons/mazes.",
        "do_not": [
            "cave mesh layout (Caves tab), surface plaza NPCs (Surface tab), terrain tiles",
            "music/video production",
        ],
        "redirect": {
            "caves": "cave structure and room graph",
            "surface-content": "surface MainScene NPCs and quests",
            "terrain": "world tile scope",
        },
    },
    "atmosphere": {
        "one_liner": "Atmosphere hooks ONLY — water, sky, lighting, fog, particles, cutscene trigger volumes.",
        "do_not": [
            "final video scripts (Video tab), NPC dialog, terrain tileCount, music production",
        ],
        "redirect": {
            "video": "cinematic scripts and segment pacing",
            "terrain": "terrain generation scope",
            "surface-content": "NPC dialog and shops",
        },
    },
    "music": {
        "one_liner": "Music production ONLY — genre, BPM, instrumental prompt, vocals, mix, optional video handoff.",
        "do_not": [
            "terrain tiles, cave structure, NPC placement, video terrain content, world build pipeline",
        ],
        "redirect": {
            "terrain": "procedural world scope",
            "video": "non-music cinematic scripts",
            "surface-content": "game NPC dialog",
        },
    },
}

TAB_RESEARCH_FOCUS: dict[str, str] = {
    "terrain": "Unity 6 procedural terrain, tile-grid open worlds, Florida karst surface design, playable demo scope 2025-2026",
    "surface-content": "Unity NPC dialog systems, hybrid Talk+Shop UX, open-world content placement, quest gating 2025-2026",
    "caves": "procedural cave generation Unity, lava tube level design, dungeon room graphs, navmesh underground 2025-2026",
    "mazes": "terrain labyrinth design, grid maze generation, landmark navigation, open-world maze integration 2025-2026",
    "interior-content": "dungeon population design, interior quest items, enemy patrol routes, loot tables Unity 2025-2026",
    "atmosphere": "Unity HDRP/URP lighting, water VFX, time-of-day, cutscene trigger volumes, ambient particles 2025-2026",
    "music": "pop trap production 2025-2026, bedroom artist vocal chain, game OST instrumental prompts, streaming mix loudness",
}

TAB_RESEARCH_CATEGORIES: dict[str, list[str]] = {
    "terrain": ["fullworld_generation_style", "terrain_scope", "navmesh", "playable_demo"],
    "surface-content": ["npc_dialog", "quest_design", "content_placement", "shop_ux"],
    "caves": ["cave_structure", "dungeon_graph", "navmesh", "entrance_design"],
    "mazes": ["maze_topology", "landmark_nav", "terrain_blend"],
    "interior-content": ["dungeon_population", "loot_tables", "patrol_routes"],
    "atmosphere": ["lighting", "water_vfx", "cutscene_triggers", "time_of_day"],
    "music": ["pop_trap_production", "vocal_chain", "instrumental_ai", "streaming_mix"],
}

TAB_RESEARCH_QUERIES: dict[str, list[str]] = {
    "terrain": [
        "Unity 6 procedural terrain tile grid open world best practices 2025 2026",
        "playable demo world scope 9 tile vs 81 tile game design",
        "Florida karst surface level design procedural generation",
        "Unity NavMesh entrance to goal cave world pipeline",
        "social clip pacing open world demo terrain props density",
    ],
    "surface-content": [
        "Unity NPC dialog branching quest giver design 2025 2026",
        "hybrid talk shop merchant UI game design patterns",
        "open world NPC placement town plaza sidewalk design",
        "enemy spawner patrol route wild zone game design",
        "quest blocker prerequisite dialog UX indie games",
    ],
    "caves": [
        "procedural cave generation Unity lava tube level design 2025",
        "dungeon room graph branching cave entrance design",
        "underground navmesh walkable floor procedural caves",
        "cave mouth surface portal linking game design",
        "shallow vs deep cave vertical budget playable demo",
    ],
    "mazes": [
        "terrain labyrinth maze grid design open world 2025 2026",
        "maze dead ends vs loops player navigation intent",
        "landmark rooms vista cells maze level design",
        "surface maze blend terrain tile integration Unity",
        "maze entrance exit portal ties town arena",
    ],
    "interior-content": [
        "dungeon interior NPC enemy placement design 2025",
        "cave quest item key placement side chamber design",
        "interior loot tables shops underground Unity",
        "enemy patrol waypoints interior zones game design",
        "surface quest link interior content quest IDs",
    ],
    "atmosphere": [
        "Unity lighting fog time of day outdoor atmosphere 2025 2026",
        "water river pool VFX game environment design",
        "cutscene trigger volume quest storyline hooks Unity",
        "ambient particles VFX open world mood design",
        "Florida dusk karst atmosphere game lighting reference",
    ],
    "music": [
        "pop trap production bedroom artist 2025 2026 vocal chain",
        "808 slide melodic trap BPM key game OST design",
        "AI instrumental prompt Suno style trap pop 2025",
        "streaming loudness -14 LUFS vocal mix reference trap",
        "music video handoff performance clip karaoke workflow",
    ],
}

TAB_PREFERRED_SOURCES: dict[str, list[str]] = {
    "terrain": ["Unity docs", "GDC talks", "procedural generation blogs", "level design case studies"],
    "surface-content": ["Unity docs", "game UX articles", "quest design GDC", "indie postmortems"],
    "caves": ["procedural generation papers", "Unity cave tutorials", "dungeon design GDC"],
    "mazes": ["maze generation algorithms", "level design blogs", "open world navigation"],
    "interior-content": ["dungeon design", "loot economy", "Unity placement patterns"],
    "atmosphere": ["Unity lighting docs", "VFX artist blogs", "cinematic trigger design"],
    "music": ["music production blogs", "mixing/mastering guides", "trap/pop production tutorials", "game audio GDC"],
}


def _scope_block(tab_id: str) -> str:
    guard = TAB_SCOPE_GUARDS.get(tab_id)
    if not guard:
        return ""
    lines = [
        f"**Scope:** {guard['one_liner']}",
        "",
        "**Do NOT plan here:**",
    ]
    for item in guard.get("do_not") or []:
        lines.append(f"- {item}")
    redirects = guard.get("redirect") or {}
    if redirects:
        lines.append("")
        lines.append("**Redirect off-topic requests:**")
        for target, topic in redirects.items():
            lines.append(f'- "{topic}" → **{target}** tab')
    return "\n".join(lines)


def scope_guard_prompt(tab_id: str) -> str:
    block = _scope_block(tab_id)
    if not block:
        return ""
    return f"## Tab scope guard (strict)\n{block}\n"


def research_queries_for_tab(
    tab_id: str,
    user_message: str = "",
    brief_context: dict[str, Any] | None = None,
) -> list[str]:
    base = list(TAB_RESEARCH_QUERIES.get(tab_id) or TAB_RESEARCH_QUERIES["terrain"])
    msg = (user_message or "").strip()
    if msg and len(msg) > 12:
        base = [f"{tab_id} game design {msg[:120]} 2025 2026", *base]
    brief = brief_context or {}
    extra = brief.get("researchQueries") or []
    if extra:
        merged = list(dict.fromkeys([*extra[:3], *base]))
        return merged[:5]
    return base[:5]


def format_research_bundle(bundle: dict[str, Any] | None, tab_id: str) -> str:
    if not bundle:
        return ""
    items = bundle.get("items") or []
    if not items:
        err = bundle.get("error") or bundle.get("researchError")
        if err:
            return f"## Web research (failed)\nResearch error: {err}\nProceed with checklist using senior defaults.\n"
        return ""
    focus = TAB_RESEARCH_FOCUS.get(tab_id, "")
    lines = [
        "## Web research bundle (2025–2026 — cite when relevant)",
        f"Focus: {focus}" if focus else "",
        f"Queries: {', '.join(bundle.get('queries') or [])}",
        "",
    ]
    for i, item in enumerate(items[:12], start=1):
        title = str(item.get("title") or "Source").strip()
        url = str(item.get("url") or "").strip()
        summary = str(item.get("summary") or "").strip()
        year = item.get("year") or ""
        lines.append(f"{i}. **{title}** ({year}) — {url}")
        if summary:
            lines.append(f"   {summary}")
    lines.append("")
    lines.append("Use insights above in teaching moments and brief fields — do not invent URLs.")
    return "\n".join(line for line in lines if line is not None)


def build_prompt_injection(tab_id: str, doc: dict[str, Any] | None = None) -> str:
    parts = [SENIOR_ENGINEERING_STANDARDS, scope_guard_prompt(tab_id)]
    if doc:
        bundle = doc.get("researchBundle")
        if bundle:
            parts.append(format_research_bundle(bundle, tab_id))
    return "\n".join(p for p in parts if p.strip())


def run_tab_research(
    hub: Path,
    tab_id: str,
    user_message: str = "",
    brief_context: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Run Cursor web research for a wizard tab; returns researchBundle dict."""
    bp.load_dotenv(hub)
    queries = research_queries_for_tab(tab_id, user_message, brief_context)
    brief = dict(brief_context or {})
    brief.setdefault("title", f"EnvKit {tab_id} planner")
    brief.setdefault("summary", (user_message or "")[:300])
    brief["researchQueries"] = queries
    doc: dict[str, Any] = {
        "tabId": tab_id,
        "brief": brief,
        "sessionConfig": {},
    }
    try:
        bundle = bp._cursor_research(hub, doc, tab_id=tab_id)
        bundle["tabId"] = tab_id
        return bundle
    except Exception as exc:
        return {
            "tabId": tab_id,
            "queries": queries,
            "items": [],
            "provider": "cursor",
            "error": str(exc),
        }


def maybe_run_session_research(
    hub: Path,
    tab_id: str,
    doc: dict[str, Any],
    user_message: str = "",
) -> bool:
    """Run research once per session when internetResearch is enabled. Returns True if research ran."""
    if not doc.get("internetResearch"):
        return False
    if doc.get("researchBundle"):
        return False
    brief = doc.get("brief") if isinstance(doc.get("brief"), dict) else {}
    bundle = run_tab_research(hub, tab_id, user_message, brief)
    doc["researchBundle"] = bundle
    doc["researchError"] = bundle.get("error")
    return True
