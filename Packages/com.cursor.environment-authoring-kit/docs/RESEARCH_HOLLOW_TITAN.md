# Research — Hollow Titan landmark

**Status:** Required reading for every Hollow Titan meat phase (0–11).  
**Categories:** `hollow_titan`, `hollow_titan_do_not`  
**Catalog:** `Tools/cave-grader/hollow-titan-research-papers.ts`, `hollow-titan-do-not-papers.ts`  
**Execution brief:** `Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json`

---

## What Hollow Titan is

One **massive mystical hollow dead tree** per FullWorld seed on a **single surface peak tile**. Interior vertical dungeon (4 floors, spiral stairs, hollow void). Exterior hyper-real **LPMagicalForest** dressing with warm interior lighting vs dark exterior bark.

**Never:** cave mouths, labyrinth bench tiles, trail corridors, or world scatter spawn tables.

---

## Landmark rules (agents)

| Rule | Detail |
|------|--------|
| **Scale** | Trunk ~30 m radius, ~82 m tall, 4 interior floors @ ~6.5 m |
| **Site** | Peak-ring karst tile via `HollowTitanLandmarkSitePicker` |
| **Ground** | `Terrain.SampleHeight` + ~0.35 m clearance; re-snap after sculpt |
| **Exterior** | `HollowTitanExteriorDressing` — LP prefabs primary; CC0 L01–L10 fallback only |
| **Spawns** | Landmark catalog only — enemies, loot, patrol per floor |
| **Silhouette** | Similar per `buildSeed` — not a pixel clone of concept PNG |
| **Vista POI** | Readable from 2+ tiles — Elden Ring Erdtree-style navigation beacon |

---

## Concept images (mandatory)

| Asset | Path |
|-------|------|
| Master | `ResearchCache/images/hollow-titan-concepts/master/concept.png` |
| Phase 0–11 | `ResearchCache/images/hollow-titan-concepts/{00..11}/concept.png` |

Generate missing images:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
python3 generate-hollow-titan-concept-images.py
```

**Do NOT edit C#** until master + active phase concept PNG are open.

---

## Research workflow

1. Open `HollowTitanResearchExecutionBrief.json` (mandatory read order inside).
2. Open `ResearchCache/categories/hollow_titan/index.json` + `hollow_titan_do_not/index.json`.
3. Read ≥2 `ResearchCache/entries/{id}/content.md` files — cite ids in plan table.
4. Open `HollowTitanActivePhasePrompt.md` + `HollowTitanResearchActionPlan.json`.
5. Apply **minimal** fix for active meat phase only — see `HollowTitanLandmarkMeatPhases.cs`.

Full workflow: `Tools/cave-grader/prompt-ladder/hollow-titan-research-workflow.md`

---

## Meat phases (0–11)

| # | Id | Kit focus |
|---|-----|-----------|
| 0 | `hollow_titan_site_pick` | Peak tile + build data |
| 1 | `hollow_titan_base_snap` | SampleHeight snap |
| 2 | `hollow_titan_trunk_shell` | LPMagicalForest trunk |
| 3 | `hollow_titan_hollow_carve` | Interior void marker |
| 4 | `hollow_titan_floor_plates` | Ring platforms |
| 5 | `hollow_titan_stair_spiral` | Spiral ramp |
| 6 | `hollow_titan_dead_branch_scatter` | LP dead branches |
| 7 | `hollow_titan_entrance_framing` | Log_Hollow_01 arch |
| 8 | `hollow_titan_per_floor_spawn_markers` | Enemy capsules |
| 9 | `hollow_titan_lighting_fog_mood` | Warm lights + fog |
| 10 | `hollow_titan_landmark_loot_table` | Landmark loot |
| 11 | `hollow_titan_enemy_patrol_nodes` | Patrol ring |

Phase defs: `Tools/cave-grader/hollow-titan-pipeline-phases.ts`

---

## DO NOT (summary)

See category `hollow_titan_do_not` and `hollow-titan-do-not-papers.ts`.

- CC0 L01–L04 white cylinders as **primary** trunk
- World scatter spawns on landmark floors
- Cave mouth / labyrinth bench placement
- FullWorld rebuild for one phase fix
- C# edits without concept images + execution brief
- Dead branches blocking entrance arch

---

## Sync

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
HUB_ROOT=/path/to/Hub npm run sync-hollow-titan-research
```

Filter cache:

- `Assets/EnvironmentKit/ResearchCache/categories/hollow_titan/`
- `Assets/EnvironmentKit/ResearchCache/categories/hollow_titan_do_not/`

Unity: each meat phase runs `HollowTitanPhaseResearchGate` before execution.
