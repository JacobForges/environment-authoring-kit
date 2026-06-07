# Research category → Unity tool map

Agents: before editing a tool, load ResearchCache categories below and open the linked `ref-0.png` when present.

| Unity / Kit tool | Research categories | Phase ids |
|------------------|----------------------|-----------|
| `SurfaceIntelligentPropPlacer` | `surface_props`, `prop_do_not` | `surface_vegetation_intelligent` |
| `SurfaceTerrainPropPlacementRegion` | `surface_props` | `surface_vegetation_intelligent` |
| `SurfaceMountainTerrainPhases.QueueCliffAccent` | `mountain_terrain`, `prop_do_not`, `fullworld_do_not` | `mountain_cliffs` |
| `SurfaceMountainPeakDressingAuthor` | `surface_props`, `mountain_terrain` | `mountain_cliffs` |
| `SurfaceTerrainTileExpansion` (seams) | `terrain_tiling`, `fullworld_do_not` | `mountain_wilderness_tiles` |
| `SurfaceTerrainSeamWelds` | `terrain_tiling` | `mountain_labyrinth_carve` |
| `SurfaceMountainLabyrinthLayout` | `mountain_labyrinth`, `labyrinth_do_not` | `mountain_labyrinth_carve` |
| `SurfaceMountainWildernessCaveMouthAuthor` | `mountain_terrain`, `fullworld_do_not` | `mountain_wilderness_cave_mouth` |
| `CaveBuildResearchPhase` / prompt export | `unity_editor_ai_guidance`, `pipeline_editor_responsiveness` | all research phases |

Do **not** BUILD / GENERATE / MAKE research — run npm sync in `Tools/cave-grader`.
