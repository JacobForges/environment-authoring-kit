#!/usr/bin/env python3
"""Tab definitions for the seven-tab AI Build Wizard."""
from __future__ import annotations

from pathlib import Path

from generic_tab_planner import TabPlanner, TabPlannerConfig

GENERATED = Path("Assets/EnvironmentKit/Generated")

_REDIRECT = (
    "\n**Scope guard:** Do ONLY this tab's job. If the user asks for another domain, "
    "name the correct wizard tab and refuse to plan it here."
)

_SENIOR_BRIEF = """
## Brief quality (when qnaComplete true)
- Use stable snake_case ids, concrete coordinates/flags, pipeline booleans — no TBD placeholders.
- Include `tabId`, `version`, `summary`, and tab-specific plan object matching EnvKit JSON schemas.
- Cite web research (title + URL) in teaching moments when a source informed a decision.
"""

_CAVES_CHECKLIST = [
    {"id": "cave_scope", "label": "Cave / dungeon scope", "done": False, "decision": ""},
    {"id": "entrances", "label": "Surface entrances & portal links", "done": False, "decision": ""},
    {"id": "route_style", "label": "Route style (lava tube, rooms, hybrid)", "done": False, "decision": ""},
    {"id": "room_graph", "label": "Room / chamber graph", "done": False, "decision": ""},
    {"id": "depth_budget", "label": "Depth & vertical budget", "done": False, "decision": ""},
    {"id": "navmesh", "label": "Walkable floors & nav intent", "done": False, "decision": ""},
    {"id": "atmosphere_hook", "label": "Lighting / mood hooks (structure only)", "done": False, "decision": ""},
    {"id": "generation_passes", "label": "Pipeline passes to enable", "done": False, "decision": ""},
]

_MAZES_CHECKLIST = [
    {"id": "maze_scope", "label": "Labyrinth / maze scope on terrain", "done": False, "decision": ""},
    {"id": "grid_topology", "label": "Grid topology & size", "done": False, "decision": ""},
    {"id": "entrances_exits", "label": "Entrances, exits, portal ties", "done": False, "decision": ""},
    {"id": "dead_ends", "label": "Dead ends vs loops", "done": False, "decision": ""},
    {"id": "landmarks", "label": "Landmark rooms / vista cells", "done": False, "decision": ""},
    {"id": "surface_blend", "label": "Blend with terrain tiles", "done": False, "decision": ""},
    {"id": "nav_intent", "label": "Player navigation intent", "done": False, "decision": ""},
]

_INTERIOR_CHECKLIST = [
    {"id": "interior_scope", "label": "Interior spaces covered", "done": False, "decision": ""},
    {"id": "interior_npcs", "label": "Cave/dungeon NPCs", "done": False, "decision": ""},
    {"id": "interior_enemies", "label": "Interior enemies & patrols", "done": False, "decision": ""},
    {"id": "interior_props", "label": "Props & interactables", "done": False, "decision": ""},
    {"id": "quest_ties", "label": "Quest links to surface content", "done": False, "decision": ""},
    {"id": "quest_items", "label": "Quest keys & special items", "done": False, "decision": ""},
    {"id": "player_gear", "label": "Equippable player items", "done": False, "decision": ""},
    {"id": "agent_gear", "label": "Agent / companion items", "done": False, "decision": ""},
    {"id": "shops_loot", "label": "Interior shops & loot tables", "done": False, "decision": ""},
    {"id": "placement", "label": "World positions in interior zones", "done": False, "decision": ""},
]

_ATMOSPHERE_CHECKLIST = [
    {"id": "water", "label": "Water bodies & rivers", "done": False, "decision": ""},
    {"id": "sky_time", "label": "Sun, moon, time-of-day", "done": False, "decision": ""},
    {"id": "lighting", "label": "Lighting & fog", "done": False, "decision": ""},
    {"id": "vfx", "label": "Particles & ambient VFX", "done": False, "decision": ""},
    {"id": "audio_hooks", "label": "Audio mood hooks", "done": False, "decision": ""},
    {"id": "cutscene_triggers", "label": "Cutscene trigger volumes & flags", "done": False, "decision": ""},
    {"id": "quest_story_hooks", "label": "Quest storyline trigger ties", "done": False, "decision": ""},
]

_CAVES_PRESETS = {
    "demo_cave": {
        "title": "Demo cave",
        "kickoff": "9-tile demo: single lava-tube route from portal to goal chamber, shallow depth, walkable floors throughout.",
        "description": "Small playable underground route.",
    },
    "dungeon_rooms": {
        "title": "Room dungeon",
        "kickoff": "Multi-room dungeon graph with 6 chambers, branching side room, entrance at cave mouth landmark.",
        "description": "Room-based dungeon structure.",
    },
}

_MAZES_PRESETS = {
    "surface_maze": {
        "title": "Surface maze",
        "kickoff": "Terrain labyrinth near east trail: 12x12 grid, one entrance from town, exit at arena gate.",
        "description": "Outdoor maze on terrain tiles.",
    },
}

_INTERIOR_PRESETS = {
    "cave_population": {
        "title": "Cave population",
        "kickoff": "Populate main cave route: gate NPC, 2 patrol enemies, quest item in side chamber, links to surface intro quest.",
        "description": "Interior actors and loot.",
    },
}

_ATMOSPHERE_PRESETS = {
    "florida_dusk": {
        "title": "Florida dusk",
        "kickoff": "Karst surface at dusk: warm sun, long shadows, subtle fog, river pool VFX, cutscene trigger at cave mouth after intro quest.",
        "description": "Outdoor atmosphere + one trigger.",
    },
}


def _mk(
    tab_id: str,
    title: str,
    kind: str,
    session_name: str,
    brief_name: str,
    help_intro: str,
    system_qna: str,
    checklist: list,
    presets: dict,
    topic_hints: dict | None = None,
) -> TabPlanner:
    return TabPlanner(
        TabPlannerConfig(
            tab_id=tab_id,
            kind=kind,
            title=title,
            session_rel=GENERATED / session_name,
            brief_rel=GENERATED / brief_name,
            help_intro=help_intro,
            system_qna=system_qna + _REDIRECT + _SENIOR_BRIEF,
            default_checklist=checklist,
            presets=presets,
            topic_hints=topic_hints or {},
            brief_finalize_note=f"Brief written. Unity can apply **{title}** when the apply hook is wired.",
        )
    )


CAVES_PLANNER = _mk(
    "caves",
    "Caves & dungeons",
    "caves",
    "CaveBuildCaveSession.json",
    "CaveBuildCaveBrief.json",
    """## Caves & dungeons

Plan **underground structure only** — routes, rooms, entrances, depth.

**Does not plan:** surface NPC dialog, maze grids (use Labyrinths tab), interior loot (use Interior content tab).

**Output:** `CaveBuildCaveBrief.json`""",
    """You are the Environment Kit **Caves & Dungeons** planner.

Plan procedural cave/dungeon **structure** — entrances, routes, room graphs, depth, nav intent.
**Does NOT plan:** surface NPC dialog (Surface content), maze grids (Mazes), interior loot (Interior content), terrain tiles (Terrain), music, video.

Redirect surface terrain to **Terrain** tab, mazes to **Labyrinths**, NPCs inside caves to **Interior content**, atmosphere to **Atmosphere**, cinematics to **Video**.

Brief must include `cavePlan`: entrances[], routes[], roomGraph[], depthBudget, navIntent, generationPasses[].

Respond JSON only:
{"assistantMessage":"...","teachingMoment":"optional","qnaComplete":false,"brief":null,"checklist":[]}""",
    _CAVES_CHECKLIST,
    _CAVES_PRESETS,
)

MAZES_PLANNER = _mk(
    "mazes",
    "Labyrinths & mazes",
    "mazes",
    "CaveBuildMazeSession.json",
    "CaveBuildMazeBrief.json",
    """## Labyrinths & mazes

Plan **maze/labyrinth topology** on terrain — grid, entrances, loops.

**Does not plan:** cave spline routes (Caves tab) or NPC placement (Surface / Interior content).

**Output:** `CaveBuildMazeBrief.json`""",
    """You are the Environment Kit **Labyrinths & Mazes** planner.

Plan maze grids, entrances, dead-ends, and links to terrain landmarks.
**Does NOT plan:** cave spline routes (Caves), surface NPC placement (Surface / Interior), terrain tileCount (Terrain), music, video.

Redirect cave structure to **Caves** tab, actors to **Interior content**, world scope to **Terrain**.

Brief must include `mazePlan`: gridSize, topology, entrances[], exits[], deadEndPolicy, landmarks[], terrainBlendNotes.

Respond JSON only:
{"assistantMessage":"...","teachingMoment":"optional","qnaComplete":false,"brief":null,"checklist":[]}""",
    _MAZES_CHECKLIST,
    _MAZES_PRESETS,
)

INTERIOR_PLANNER = _mk(
    "interior-content",
    "Interior content",
    "interior_content",
    "CaveBuildInteriorContentSession.json",
    "CaveBuildInteriorContentBrief.json",
    """## Interior content

NPCs, enemies, props, quests **inside** caves, dungeons, and mazes.

Link quest IDs to **Surface content** brief when possible.

**Output:** `CaveBuildInteriorContentBrief.json`""",
    """You are the Environment Kit **Interior Content** planner.

Plan actors, loot, quest items, player/agent gear inside underground spaces.
Reference surface quest IDs from `CaveBuildSurfaceContentBrief.json` when relevant — read-only.
**Does NOT plan:** cave mesh layout (Caves), surface plaza NPCs (Surface content), terrain tiles (Terrain), music, video.

Redirect cave mesh layout to **Caves** tab, terrain to **Terrain**, surface quests to **Surface content**.

Brief must include `interiorPlan`: npcs[], enemies[], props[], questItems[], shops[], placements[] with zoneId + worldPosition.

Respond JSON only:
{"assistantMessage":"...","teachingMoment":"optional","qnaComplete":false,"brief":null,"checklist":[]}""",
    _INTERIOR_CHECKLIST,
    _INTERIOR_PRESETS,
)

ATMOSPHERE_PLANNER = _mk(
    "atmosphere",
    "Atmosphere & cinematics",
    "atmosphere",
    "CaveBuildAtmosphereSession.json",
    "CaveBuildAtmosphereBrief.json",
    """## Atmosphere & cinematics

Water, lighting, sky, VFX, and **cutscene trigger** definitions.

**Does not render video** — use **Video** tab (rightmost) for cinematics.

**Output:** `CaveBuildAtmosphereBrief.json`""",
    """You are the Environment Kit **Atmosphere & Cinematics** planner.

Plan water, sun/moon, fog, particles, and cutscene trigger volumes/flags.
**Does NOT plan:** final video scripts (Video tab), NPC dialog (Surface content), terrain tileCount (Terrain), music production.

Redirect final video scripts to **Video** tab, world scope to **Terrain**, actors to **Surface / Interior content**.

Brief must include `atmospherePlan`: water[], skyTime, lighting, fog, vfx[], cutsceneTriggers[], questStoryHooks[].

Respond JSON only:
{"assistantMessage":"...","teachingMoment":"optional","qnaComplete":false,"brief":null,"checklist":[]}""",
    _ATMOSPHERE_CHECKLIST,
    _ATMOSPHERE_PRESETS,
)

GENERIC_PLANNERS: dict[str, TabPlanner] = {
    "caves": CAVES_PLANNER,
    "mazes": MAZES_PLANNER,
    "interior-content": INTERIOR_PLANNER,
    "atmosphere": ATMOSPHERE_PLANNER,
}
