# Phase contracts (ladder rungs)

Machine-readable registry: `CaveBuildPhaseContractRegistry` → `Assets/EnvironmentKit/Generated/CaveBuildPhaseContracts.json`.

**Rule:** A rung may run only when all **input** artifacts exist. When a rung completes, it writes **outputs** and marks downstream rungs dirty if their inputs change.

| Rung ID | Inputs | Outputs | Invalidates | Max runtime (target) |
|---------|--------|---------|-------------|----------------------|
| `research_seed` | Hub ResearchCache index | `CaveBuildResearchExecutionBrief.json`, gate files | all | 30s |
| `macro_terrain` | Ground anchor, seed | Terrain heightmap, `SurfaceDemGeorefStatus.json` | trails, props, cave mouth | 90s |
| `hydrology_masks` | heightmap | Road/water masks (structure) | trails, props | 45s |
| `trails_nav` | heightmap, masks | Trail splines, `SurfaceWorldManifest.json`, surface NavMesh | props, validation | 60s |
| `surface_props` | trails, NavMesh | `GeneratedSurfaceWorld/Vegetation` — per-tile targets (e.g. trees 35×N, grass 150×N); scene ≥42 instances/tile | validation only | 120s |
| `pre_build_gate` | surface artifacts | `CaveBuildPreBuildLadderReport.json` | cave geometry | 120s |
| `cave_layout` | pre-build pass | `CaveMazeLayout` / spline under cave root | route mesh, shell, gameplay | 120s |
| `route_mesh_nav` | layout | `RouteTerrainFloor`, cave NavMesh | shell, materials | 90s |
| `shell_materials` | route mesh | Block rings, PBR materials | gameplay polish | 120s |
| `gameplay_props` | shell | Spawners, portals, mobs | validation | 60s |
| `validation` | all above | `CaveBuildRouteProbe.json`, `CaveBuildSurfaceRouteProbe.json` | polish only | 30s |
| `polish` | grade &lt; ship | `CaveBuildQualityReport.json`, optional prefab | none | 300s |

## Queued pipeline mapping (120 steps)

| Queued steps | Global rung |
|--------------|-------------|
| 0 | `research_seed` |
| 1–13 | `cave_layout` (+ geo artifacts) |
| 14–31 | `route_mesh_nav` / playability |
| 32–37 | `validation` |
| 38–47 | `shell_materials` (ground polish / burial under terrain) |
| 48–62 | `shell_materials` / `gameplay_props` (world stages) |
| 63 | `polish` (meat loop) |
| 64–87 | `polish` (post-meat) |
| 88–99 | `research_seed` (post-build research) |
| 100–117 | `polish` (finalize: props, burial, contract) |
| 118 | commercial manifest |
| 119 | finalize report |

Constants: `CaveBuildQueuedPipelineSchedule` in `Editor/Blockout/CaveBuildQueuedPipelineSchedule.cs`.

**FullWorld** runs surface rungs `macro_terrain` → `surface_props` in startup **before** queued cave step 0. Surface-only builds stop after `surface_props`. Cave-only skips surface rungs when artifacts exist.

**Scene check:** `surface_props` is not complete from JSON plans alone — `AreOutputsPresent` requires vegetation instances on every locked terrain (`IsNineTileVegetationSufficient`).

## Invalidation examples

- Edit trail spline → invalidate `trails_nav` through `validation`.
- Change `CaveLayoutRoll` seed → invalidate `cave_layout` through `validation`.
- Bump ResearchCache policy → invalidate `research_seed` only.

Use **Window → Environment Kit → Cave Build → Diagnostics → Invalidate All Ladder Rungs** to force full rebuild.
