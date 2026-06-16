#!/usr/bin/env python3
"""
Pre-ONNX “extensive” Q&A dataset generator for per-tab world brains.

This does NOT compile ONNX models. Instead it builds a large set of
synthetic checklist turns to train/validate tab-specific validator brains.

Output:
- qa_cases_<tabId>.jsonl: many (assistant-style) user decisions with labels
- qa_ladder_<tabId>.md: rubrics + a few representative examples
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, List, Tuple


@dataclass(frozen=True)
class ChecklistSpec:
    id: str
    label: str


_ROOT = Path(__file__).resolve().parent.parent
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))


def _load_checklists() -> Dict[str, List[ChecklistSpec]]:
    # Import inside function so script can run even if some tabs change.
    import build_planner as terrain_planner
    import content_planner as surface_planner
    import director_session as video_mod
    import wizard_tabs as tabs
    # music_director_session lives under ai-director-studio/server
    # _ROOT = Packages/.../Tools/cave-grader
    # hub_root is the repo root: /Users/jacob/Hub
    hub_root = _ROOT.parents[3]
    music_server = hub_root / "ai-director-studio" / "server"
    if music_server.is_dir() and str(music_server) not in sys.path:
        sys.path.insert(0, str(music_server))
    import music_director_session as music_mod

    terrain = [
        ChecklistSpec(str(c["id"]), str(c["label"]))
        for c in terrain_planner.DEFAULT_CHECKLIST
        if not bool(c.get("locked"))
    ]
    surface = [ChecklistSpec(str(c["id"]), str(c["label"])) for c in surface_planner.DEFAULT_CHECKLIST]
    caves = [ChecklistSpec(str(c["id"]), str(c["label"])) for c in tabs._CAVES_CHECKLIST]  # noqa: SLF001
    mazes = [ChecklistSpec(str(c["id"]), str(c["label"])) for c in tabs._MAZES_CHECKLIST]  # noqa: SLF001
    interior = [  # noqa: SLF001
        ChecklistSpec(str(c["id"]), str(c["label"])) for c in tabs._INTERIOR_CHECKLIST
    ]
    atmosphere = [ChecklistSpec(str(c["id"]), str(c["label"])) for c in tabs._ATMOSPHERE_CHECKLIST]  # noqa: SLF001
    music = [ChecklistSpec(str(c["id"]), str(c["label"])) for c in music_mod.DEFAULT_CHECKLIST]
    video = [ChecklistSpec(str(c["id"]), str(c["label"])) for c in video_mod.DEFAULT_CHECKLIST]

    return {
        "terrain": terrain,
        "surface-content": surface,
        "caves": caves,
        "mazes": mazes,
        "interior-content": interior,
        "atmosphere": atmosphere,
        "music": music,
        "video": video,
    }


def _rubric(tab_id: str, item_id: str) -> Tuple[str, str]:
    """Return (keywords, rubric) strings."""
    # Keep rubric short; the generator below enforces a subset of these checks.
    if tab_id == "terrain":
        mapping = {
            "scope": "Include concrete goals + playable demo intent; mention mountains/water/labyrinth.",
            "props": "Mention prop scatter style + distribution principle (not sparse/only center).",
            "npcs": "Name at least 1 NPC and what they do (brief role + placement at high level).",
            "enemies": "Name enemy type(s) + combat loop intent + approximate challenge tier.",
            "puzzles": "Name 1–2 interactables or traversal blockers and how player uses them.",
            "terrain": "Describe terrain plan elements (mountain/water/labyrinth) with clear scope.",
            "speed": "State speed vs quality trade-off explicitly (fast tier vs high quality tier).",
            "tech_jump": "Include jump gap spacing in meters; mention platform spacing or jump rules.",
            "tech_seams": "Include tile height offsets + seam handling strategy.",
            "tech_navmesh": "Include navmesh intent (walkable vs jump-only routes).",
            "tech_spawn": "Include spawn point + fall respawn / reset behavior.",
            "tech_seed": "State fixed vs random seed and why.",
            "tech_collision": "Include triggers/kill volumes/platform colliders at least by type.",
            "tech_perf": "Include prop budget or perf target (FPS/triangle-ish) and how you reduce load.",
        }
        kw = "terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m"
        return kw, mapping.get(item_id, "Be specific and implementable.")

    if tab_id == "surface-content":
        kw_map = {
            "scene_scope": "MainScene, zones_in_scope, out_of_scope",
            "zones": "zone ids: town_center/battle_arena/east_trail/cave_gate + notes",
            "quest_npcs": "quest_giver, objective chain, quest ids",
            "trainer_npcs": "trainer role + grade gates (minTrainingGrade)",
            "ambient_npcs": "npc_ambient_*, dlg_* flavor nodes",
            "blocker_gates": "gate npc id + gateBlockers list + blocker ids",
            "enemy_patrols": "enemy spawner ids + patrol waypoints",
            "props_layout": "prop ids + interactable vs dressing + blocker collider props",
            "textures": "textureHint per prop cluster (weathered-wood/slate/cyan-banner)",
            "dialog_hybrid": "Talk node ids + Shop inventory + blockers arrays",
            "blockers_training": "minTrainingGrade gates with blocker objects",
            "blockers_quest": "quest_completed blockers with quest ids",
            "placement": "world positions x,y,z + rotation + facing rules",
            "playmode_smoke": "requiredNpcIds + ordered steps[]",
        }
        rubric_map = {
            "scene_scope": "Must define which surface zones are authored vs excluded for now.",
            "zones": "Must list each zone id and what it contains for this slice.",
            "quest_npcs": "Must name the quest giver NPC and give objective chain with ids.",
            "trainer_npcs": "Must name trainer NPC and training gates with thresholds.",
            "ambient_npcs": "Must include 2–3 dlg_* nodes per ambient NPC (not just positions).",
            "blocker_gates": "Must define gate NPC + blockers + physical seal intent.",
            "enemy_patrols": "Must specify spawner ids and 3–4 patrol waypoints each (or justify none).",
            "props_layout": "Must name prop ids with positions and mark interactable behavior vs dressing.",
            "textures": "Must assign textureHint values consistently per prop cluster.",
            "dialog_hybrid": "Must map Talk node chain to Shop and Leave, with blockers where needed.",
            "blockers_training": "Must list blocker objects with minTrainingGrade and hint text.",
            "blockers_quest": "Must list quest_completed blockers with quest ids and where they apply.",
            "placement": "Must give x,z positions and facing (rotationY).",
            "playmode_smoke": "Must give requiredNpcIds and ordered steps with expected outcomes.",
        }
        kw = kw_map.get(item_id, "surface_content")
        return kw, rubric_map.get(item_id, "Be specific and implementable.")

    # Generic tabs (caves/mazes/interior/atmosphere/video): coarse rubrics.
    if tab_id in ("caves", "mazes", "interior-content", "atmosphere", "video"):
        generic = {
            "cave_scope": "Must define underground scope + depth budget + general structure intent.",
            "entrances": "Must link surface entrances/portal ties to cave mouth/portal.",
            "route_style": "Must name route style and how players progress through it.",
            "room_graph": "Must describe room/chamber graph (nodes/edges, branching policy).",
            "depth_budget": "Must include depth/vertical budget in units (meters/layers).",
            "navmesh": "Must state walkable floor and navigation intent.",
            "atmosphere_hook": "Must state lighting/mood hooks at least at structure level.",
            "generation_passes": "Must list which pipeline passes/outputs to enable.",

            "maze_scope": "Must define maze scope on terrain (where and how big).",
            "grid_topology": "Must define grid size and topology policy.",
            "entrances_exits": "Must specify entrances/exits and portal ties.",
            "dead_ends": "Must describe dead-end vs loop policy.",
            "landmarks": "Must define landmark rooms/cells/vistas.",
            "surface_blend": "Must describe how maze blends with terrain tiles.",
            "nav_intent": "Must state navigation intent and player routing behavior.",

            "interior_scope": "Must define which interior spaces/zones are included.",
            "interior_npcs": "Must name NPCs inside interiors and their roles/placement intent.",
            "interior_enemies": "Must name enemy spawners/patrols and pacing intent.",
            "interior_props": "Must name props/interactables and what players do with them.",
            "quest_ties": "Must tie quests to surface content via quest ids.",
            "quest_items": "Must define quest keys/special items and their location/usage.",
            "player_gear": "Must list equippable player items and when they unlock.",
            "agent_gear": "Must list agent/companion items and gating logic.",
            "shops_loot": "Must define interior shops and loot tables with at least one example.",
            "placement": "Must give at least zoneId + worldPosition placements for key items.",

            "water": "Must define water bodies/rivers and placement concept.",
            "sky_time": "Must define sun/moon and time-of-day.",
            "lighting": "Must define lighting + fog intent.",
            "vfx": "Must define particles/VFX and where they trigger.",
            "audio_hooks": "Must define audio mood hooks.",
            "cutscene_triggers": "Must define cutscene trigger volumes/flags.",
            "quest_story_hooks": "Must tie triggers to quest storyline beats.",

            "platform": "Must state primary platform and format constraints.",
            "aspect_ratio": "Must define aspect ratio/framing policy.",
            "length": "Must define target runtime.",
            "audience": "Must define audience persona and language level.",
            "success_metric": "Must define one measurable success metric.",
            "working_title": "Must include working title + logline.",
            "cta": "Must define call to action.",
            "chapters_endscreen": "Must define chapter break/end screen strategy.",
            "hook": "Must define 0-3 second hook strategy.",
            "narrative_arc": "Must define three-act narrative arc.",
            "emotional_peak": "Must identify emotional/high-impact peak.",
            "payoff": "Must define payoff/takeaway.",
            "stakes": "Must define tension/stakes driver.",
            "voice_mode": "Must define voiceover mode/tone.",
            "script_density": "Must define script density/word pacing.",
            "on_screen_text": "Must define on-screen text/annotations policy.",
            "shot_coverage": "Must define shot list coverage strategy.",
            "camera_language": "Must define camera movement/lens grammar.",
            "color_grade_style": "Must define grade style references.",
            "audio_mix_target": "Must define mix/loudness targets.",
            "music_strategy": "Must define score/music plan.",
            "sfx_strategy": "Must define SFX approach.",
            "transitions": "Must define transition language.",
            "thumbnail": "Must define thumbnail concept.",
            "publish_package": "Must define title/description/tags package.",
        }
        kw = f"{tab_id}_keywords"
        return kw, generic.get(item_id, "Be specific and implementable.")

    if tab_id == "music":
        music_generic = {
            "genre": "Include pop/trap genre + subgenre; it should drive BPM and instrumental prompt.",
            "mood": "Include mood adjectives; should influence vocals + mix.",
            "working_title": "Include a concrete working title.",
            "bpm": "Provide target BPM as a number in the right range.",
            "key": "Provide key or 'auto' with expectation of detection.",
            "scale": "Major/minor explicitly.",
            "instrumental_style": "List 3–6 sonic ingredients (808 slides, arps, pads...).",
            "instrumental_prompt": "Provide a Suno-style prompt (genre,BPM,instruments,reference,no vocals).",
            "lyrics_text": "Include hook/lines (line breaks) or full/outline lyrics.",
            "vocal_style": "Provide vocal delivery style (sing/rap-sing/whisper...).",
            "performance_vibe": "Mention performance energy (bedroom vs stadium).",
            "autotune_intent": "State autotune/polish intent (transparent vs obvious).",
            "mix_vibe": "Provide mix goal + reference (e.g. -14 LUFS, crunchy 808...).",
            "music_video_intent": "Yes/no + concept (performance clip, lyric video, handoff).",
        }
        kw = "music_production_checklist"
        return kw, music_generic.get(item_id, "Be specific and implementable.")

    return "keywords", "Be specific and implementable."


def _make_good(tab_id: str, item_id: str, i: int) -> str:
    # Generate implementable decisions with small variations.
    if tab_id == "terrain":
        if item_id == "scope":
            return f"Playable world gate. Ship a fast playable slice: mountains + water edge + labyrinth traversal. Goal: entry-to-goal loop with NPC social beats and a clear challenge tier (tier {i % 3 + 1})."
        if item_id == "props":
            return f"Prop scatter is corridor-biased: trail corridor higher density, horizon soft backdrop, no uniform random. Use tile-based scatter with exclusion for spawn/wizard pad. Variant {i}."
        if item_id == "npcs":
            return f"Include 2 NPCs: quest-giver and ambient flavor. Quest-giver stands near the central 3x3 play disk; ambient points players toward labyrinth route. Keep dialog short and non-blocking (variant {i})."
        if item_id == "enemies":
            return f"Enemies + combat included. Use one wave type around labyrinth turns with readable telegraph. Difficulty tier {i % 3 + 1} with respawn tuned for demo pacing."
        if item_id == "puzzles":
            return f"Puzzles/interactables: one lever/terminal that opens a short traversal gate; one pickup that teaches combat tutorial. Must be reachable within demo loop (variant {i})."
        if item_id == "terrain":
            return f"Terrain plan: mountain silhouettes with water edge and a labyrinth route integrated into tile grid. Ensure slopes are walkable and avoid cliff-ring artifacts (variant {i})."
        if item_id == "speed":
            return f"Speed vs quality: medium tier. Prefer 1-2 passes for acceptable readability; skip expensive high-detail dressing outside critical path. Variant {i}."
        if item_id == "tech_jump":
            return f"Jump gaps: target 2.0m–3.0m gap spacing, platform spacing 1.5m–2.5m so jump arcs are consistent at demo movement speed (variant {i})."
        if item_id == "tech_seams":
            return f"Seams: set tile height offsets so neighboring tiles share a consistent seam band width; eliminate visible step discontinuities using tile-height blending (variant {i})."
        if item_id == "tech_navmesh":
            return f"Navmesh: ensure walkable routes on navmesh for main path; mark only a few jump-only shortcuts and keep them optional. (variant {i})."
        if item_id == "tech_spawn":
            return f"Spawn: place spawn at play disk center; respawn on fall uses a lower kill volume radius and a fall-respawn point just outside hazard so demo reattempts are fast (variant {i})."
        if item_id == "tech_seed":
            return f"Seed: fixed seed for reproducibility during authoring, then switch to random only if quality passes are locked (variant {i})."
        if item_id == "tech_collision":
            return f"Collision: define trigger volumes for tutorial prompt, kill volumes for jump-fail, and platform colliders for bridge edges; ensure no invisible blockers on main route (variant {i})."
        if item_id == "tech_perf":
            return f"Perf budget: keep prop budget under a strict cap; reduce spawn density of high-cost props off-path and preserve FPS. Target demo perf with conservative collision complexity (variant {i})."
        return f"Implementable terrain decision for {item_id} variant {i}."

    if tab_id == "surface-content":
        # Provide content-planner style implementable decisions.
        if item_id == "scene_scope":
            return f"Author full slice across town_center, battle_arena, east_trail, cave_gate. Exclude wild_east/wild_west for now to keep this pass Surface-only (variant {i})."
        if item_id == "zones":
            return f"zones: town_center (plaza hub), battle_arena (west arm), east_trail (east fork), cave_gate (north mouth). Provide brief notes per zone; no wild zones in scope (variant {i})."
        if item_id == "quest_npcs":
            return f"Quest giver: npc_sera_scout at town_center (3,-7) starts quest_sera_east_fork. Objective chain: accept at Sera -> reach prop_sera_fork_sign -> report to npc_lumen_gate; reward: shop unlock + quest_completed unblock."
        if item_id == "trainer_npcs":
            return f"Trainer: npc_ren_coach at town_center (-6,5) rotY90. Provide Talk+Shop hybrid: show training grade gates minTrainingGrade70 for shop_open and minTrainingGrade40/55 for earlier coaching options."
        if item_id == "ambient_npcs":
            return f"Ambient NPCs: npc_ambient_sigrid dlg_sigrid_market, dlg_sigrid_sera_rumor; npc_ambient_jonas dlg_jonas_greetings, dlg_jonas_rumor. Pure flavor (no quests, no shop)."
        if item_id == "blocker_gates":
            return f"Gate NPC: npc_lumen_gate blocker_gate. Talk-only sealed -> hint -> clear on quest_mara_intro_dispatch complete. gateBlockers includes blocker_quest_mara_intro_dispatch. Physical seals blocker_cave_gate_north and blocker_east_trail_fork lift on quest accepts/completes."
        if item_id == "enemy_patrols":
            return f"Enemy spawners: enemy_spawner_east_trail wild_east and enemy_spawner_arena_ring wild_west. Provide 3–4 patrol waypoints per spawner with concrete x,z offsets; grade gate for arena ring is minTrainingGrade70."
        if item_id == "props_layout":
            return f"Props: list prop_depot_sign, prop_mara_manifest_crate, prop_depot_pennant (town_center); prop_sera_fork_sign, prop_sera_fork_pennant, prop_jonas_rumor_board (east_trail); prop_cave_mouth_arch (cave_gate); prop_ren_bleacher_banners + prop_arena_supply_crate (battle_arena). Mark sign as interactable that pings quest objective."
        if item_id == "textures":
            return f"textureHint clusters: weathered-wood on depot sign+crate+fork sign; slate on cave mouth arch + rumor board; cyan-banner on elevated pennants/banners. No grade tint; invisible blockers untextured (variant {i})."
        if item_id == "dialog_hybrid":
            return f"Hybrid dialog: npc_mara_dispatch chain dlg_mara_dispatch->dlg_mara_crate_hint, plus Shop mapping with items and blockers[]. npc_sera_scout offers opt_accept_fork leading to quest_sera_east_fork. Ren has Talk nodes dlg_ren_greet->dlg_ren_bc_intro->shop_open with grade gates."
        if item_id == "blockers_training":
            return "Training blockers: blocker_agent_training_70 {type: agent_training_level, minTrainingGrade:70, hint:'Train to 70% in arena first'}; blocker_agent_training_80 and blocker_agent_training_90 similarly; apply to specific shop rows and optionally advanced brief options."
        if item_id == "blockers_quest":
            return "Quest blockers: blocker_quest_mara_intro_dispatch {type: quest_completed, questId: quest_mara_intro_dispatch, hint:'Complete dispatch to lift cave gate'}; blocker_quest_sera_east_fork {type: quest_completed, questId: quest_sera_east_fork, hint:'Open fork by accepting the quest'}; list where each attaches."
        if item_id == "placement":
            return f"Placement/facing: npc_mara_dispatch (-7,0,-3) rotY90; npc_sera_scout (3,0,-7) rotY180; npc_lumen_gate (-2,0,8) rotY180; npc_ren_coach (-6,0,5) rotY90. Props at zone offsets near each NPC; ensure rotations face plaza."
        if item_id == "playmode_smoke":
            return f"requiredNpcIds: ['npc_mara_dispatch','npc_sera_scout','npc_lumen_gate','npc_ren_coach']. steps: 1) Walk to Mara -> accept quest_mara_intro_dispatch -> verify Shop unlock. 2) Talk Lumen sealed until Mara quest complete. 3) Accept fork at Sera -> verify blocker_east_trail_fork lifts -> interact fork sign. 4) Check Ren shop shows locked hint below 70 at grade 0."
        return f"Surface decision for {item_id} variant {i}."

    if tab_id in ("caves", "mazes", "interior-content", "atmosphere"):
        # Simple structured decisions by item id.
        return f"{tab_id}:{item_id} implementable decision with required structure (variant {i})."

    if tab_id == "music":
        # Match checklist item naming from music_director_session.
        if item_id == "genre":
            return "Dark pop trap — melodic hooks with 808 slide emphasis (variant %d)." % i
        if item_id == "mood":
            return "Moody late-night, confident but vulnerable — not fully sad, not fully hype."
        if item_id == "working_title":
            return f"Midnight Loop (working title) v{i}"
        if item_id == "bpm":
            return f"140 BPM"
        if item_id == "key":
            return "F minor"
        if item_id == "scale":
            return "Minor"
        if item_id == "instrumental_style":
            return "Sliding 808, sparse piano plucks, airy pad, tight hi-hats; add subtle guitar loop."
        if item_id == "instrumental_prompt":
            return "dark pop trap 140 bpm, moody 808s, minor key, catchy hook space, no vocals (variant %d)" % i
        if item_id == "lyrics_text":
            return "Hook:\nlate nights on the loop\nsay you care but never prove\nI'm done with empty promises\n"
        if item_id == "vocal_style":
            return "Melodic rap-sing — half spoken verses, sung hook."
        if item_id == "performance_vibe":
            return "Intimate bedroom take, confident delivery, not shouting."
        if item_id == "autotune_intent":
            return "Obvious but musical autotune on hook; transparent fix on verses."
        if item_id == "mix_vibe":
            return "Wide vocal, punchy 808, streaming-ready (-14 LUFS), reference early Juice WRLD demos."
        if item_id == "music_video_intent":
            return "Yes — performance clip from webcam karaoke session; optional AI Director recap handoff."
        return f"Music decision for {item_id} variant {i}."

    if tab_id == "video":
        return (
            f"Video plan for [{item_id}] variant {i}: define concrete production decision, "
            f"platform-safe export settings, chapter/beat timing, and implementation notes for editor + narration pipeline."
        )

    return f"Decision for {tab_id}:{item_id} variant {i}."


def _make_bad(tab_id: str, item_id: str, i: int) -> str:
    # “Bad” decisions are vague/off-topic, missing required fields, or too short.
    if tab_id == "terrain":
        return f"Make it good. {item_id} variant {i}."
    if tab_id == "surface-content":
        if item_id == "playmode_smoke":
            # Keep it short and missing requiredNpcIds/steps to trigger weak decision heuristics.
            return "TBD QA plan."
        if item_id == "ambient_npcs":
            return "Ambient NPCs positioned but not dialog-authored."
        # For other items, keep it very short so it is clearly incomplete.
        return f"TBD."
    if tab_id in ("caves", "mazes", "interior-content", "atmosphere"):
        return f"Sounds fine. {tab_id}:{item_id} variant {i}."
    if tab_id == "music":
        if item_id in ("instrumental_prompt", "lyrics_text"):
            return "Add good lyrics/instrumental."  # missing structure
        return f"Generic {item_id} idea."
    if tab_id == "video":
        return "Looks good, do it cinematic."
    return f"Bad decision for {tab_id}:{item_id} variant {i}."


def generate_cases(
    tab_id: str,
    items: List[ChecklistSpec],
    n_good: int,
    n_bad: int,
    out_jsonl: Path,
) -> None:
    out_jsonl.parent.mkdir(parents=True, exist_ok=True)
    with out_jsonl.open("w", encoding="utf-8") as f:
        for it in items:
            for i in range(n_good):
                decision = _make_good(tab_id, it.id, i)
                assistant_q = f"Answer checklist [{it.id}] — {it.label}"
                expected_pass = True
                expected_reopen: list[str] = []
                if tab_id == "surface-content":
                    import wizard_checklist as wc

                    weak = wc.decision_looks_weak(it.id, decision)
                    expected_pass = not weak
                    expected_reopen = [it.id] if weak else []

                rec = {
                    "tabId": tab_id,
                    "checklistId": it.id,
                    "variant": "good",
                    "assistantQuestion": assistant_q,
                    "decision": decision,
                    "expectedPass": bool(expected_pass),
                    "expectedReopenIds": expected_reopen,
                    "fixHint": "",
                }
                f.write(json.dumps(rec, ensure_ascii=False) + "\n")
            for i in range(n_bad):
                decision = _make_bad(tab_id, it.id, i)
                assistant_q = f"Answer checklist [{it.id}] — {it.label}"
                expected_pass = False
                expected_reopen = [it.id]
                if tab_id == "surface-content":
                    import wizard_checklist as wc

                    weak = wc.decision_looks_weak(it.id, decision)
                    expected_pass = not weak
                    expected_reopen = [it.id] if weak else []

                rec = {
                    "tabId": tab_id,
                    "checklistId": it.id,
                    "variant": "bad",
                    "assistantQuestion": assistant_q,
                    "decision": decision,
                    "expectedPass": bool(expected_pass),
                    "expectedReopenIds": expected_reopen,
                    "fixHint": "Add concrete implementable fields for this checklist item (ids, units, arrays).",
                }
                f.write(json.dumps(rec, ensure_ascii=False) + "\n")


def generate_ladder(
    tab_id: str,
    items: List[ChecklistSpec],
    n_show: int,
    out_md: Path,
) -> None:
    out_md.parent.mkdir(parents=True, exist_ok=True)
    lines: List[str] = []
    lines.append(f"# Pre-ONNX Q&A Ladder — {tab_id}\n")
    for it in items:
        keywords, rubric = _rubric(tab_id, it.id)
        lines.append(f"## {it.id}: {it.label}\n")
        lines.append(f"- Keywords: `{keywords}`\n")
        lines.append(f"- Rubric: {rubric}\n")
        lines.append(f"- Example GOOD:\n")
        for i in range(min(n_show, 2)):
            lines.append(f"  - { _make_good(tab_id, it.id, i)[:220] }{'...' if len(_make_good(tab_id, it.id, i))>220 else ''}\n")
        lines.append(f"- Example BAD:\n")
        for i in range(min(n_show, 2)):
            lines.append(f"  - { _make_bad(tab_id, it.id, i)[:220] }{'...' if len(_make_bad(tab_id, it.id, i))>220 else ''}\n")
        lines.append("\n")

    out_md.write_text("".join(lines), encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="world_tab_brains/qa_datasets_out", help="Output directory (relative to cave-grader).")
    ap.add_argument("--good", type=int, default=20, help="Good examples per checklist item.")
    ap.add_argument("--bad", type=int, default=20, help="Bad examples per checklist item.")
    ap.add_argument("--show", type=int, default=2, help="How many examples to show per variant in markdown.")
    args = ap.parse_args()

    # Base path: Tools/cave-grader
    here = Path(__file__).resolve().parent.parent  # cave-grader/
    out_root = here / args.out

    checklists = _load_checklists()
    for tab_id, items in checklists.items():
        out_jsonl = out_root / f"qa_cases_{tab_id}.jsonl"
        out_md = out_root / f"qa_ladder_{tab_id}.md"
        generate_cases(tab_id, items, args.good, args.bad, out_jsonl)
        generate_ladder(tab_id, items, args.show, out_md)
        print(f"Wrote {out_jsonl} and {out_md}")


if __name__ == "__main__":
    main()

