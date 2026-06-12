# Phase contracts (ladder rungs)

Machine-readable registry: `CaveBuildPhaseContractRegistry` → `Assets/EnvironmentKit/Generated/CaveBuildPhaseContracts.json`.

**Rule:** A rung may run only when all **input** artifacts exist. When a rung completes, it writes **outputs** and marks downstream rungs dirty if their inputs change.

**Pipeline truth:** [PIPELINE_TRUTH.md](../../../../docs/PIPELINE_TRUTH.md) · **Planner:** [PLANNER_SESSION.md](../../../../docs/PLANNER_SESSION.md)

| Rung ID | Inputs | Outputs | Invalidates | Max runtime (target) |
|---------|--------|---------|-------------|----------------------|
| `research_seed` | Hub ResearchCache index | `CaveBuildResearchExecutionBrief.json`, gate files | all | 30s |
| `macro_terrain` | Ground anchor, seed | Terrain heightmap, `SurfaceDemGeorefStatus.json` | trails, props, cave mouth | 90s |
| `hydrology_masks` | heightmap | Road/water masks (structure) | trails, props | 45s |
| `trails_nav` | heightmap, masks | Trail splines, `SurfaceWorldManifest.json`, surface NavMesh | props, validation | 60s |
| `surface_props` | trails, NavMesh | `GeneratedSurfaceWorld/Vegetation` — per-tile targets; scene ≥42 instances/tile | validation only | 120s |
| `pre_build_gate` | surface artifacts | `CaveBuildPreBuildLadderReport.json` | cave geometry | 120s |
| `cave_layout` | pre-build pass | `CaveMazeLayout` / spline under cave root | route mesh, shell, gameplay | 120s |
| `route_mesh_nav` | layout | `RouteTerrainFloor`, cave NavMesh | shell, materials | 90s |
| `shell_materials` | route mesh | Block rings, PBR materials | gameplay polish | 120s |
| `gameplay_props` | shell | Spawners, portals, mobs | validation | 60s |
| `validation` | all above | `CaveBuildRouteProbe.json`, `CaveBuildSurfaceRouteProbe.json` | polish only | 30s |
| `polish` | grade &lt; ship | `CaveBuildQualityReport.json`, optional prefab | none | 300s |

## Queued pipeline mapping (122 steps)

Source: `CaveBuildQueuedPipelineSchedule` in `Editor/Blockout/CaveBuildQueuedPipelineSchedule.cs`.

| Queued steps | Phase | Global rung |
|--------------|--------|-------------|
| 0 | Validate & prep | `research_seed` / pre-build handoff |
| 1–15 | Geo (15 steps) | `cave_layout` |
| 16–33 | Playability (18 steps) | `route_mesh_nav` |
| 34–39 | Validation (6 steps) | `validation` |
| 40–49 | Ground polish (10 steps) | `shell_materials` (burial under terrain) |
| 50–64 | World stages (15 steps) | `shell_materials` / `gameplay_props` |
| **65** | **Meat loop** | `polish` (entry — not total count) |
| 66–89 | Post-meat (24 steps) | `polish` |
| 90–101 | Research (12 steps) | `research_seed` |
| 102–119 | Finalize polish (18 steps) | `polish` |
| 120 | AAA manifest | commercial manifest |
| 121 | Finish report | finalize report |

**FullWorld** runs surface rungs `macro_terrain` → `surface_props` in startup **before** queued cave step 0. **Planner session** may scope tiles to 13/81/289 via `CaveBuildActiveSessionConfig.json`. Surface-only builds stop after `surface_props`. Cave-only skips surface rungs when artifacts exist.

**Scene check:** `surface_props` is not complete from JSON plans alone — `AreOutputsPresent` requires vegetation instances on every locked terrain (`IsNineTileVegetationSufficient`).

## Invalidation examples

- Edit trail spline → invalidate `trails_nav` through `validation`.
- Change `CaveLayoutRoll` seed → invalidate `cave_layout` through `validation`.
- Bump ResearchCache policy → invalidate `research_seed` only.

Use **Window → Environment Kit → Cave Build → Diagnostics → Invalidate All Ladder Rungs** to force full rebuild.
