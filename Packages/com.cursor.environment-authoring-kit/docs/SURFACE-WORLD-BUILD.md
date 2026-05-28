# Surface world build (open sky)

Radial and multi-tile landscape generation from your **Ground**-tagged anchor. FullWorld runs **surface + terrain ladder before** the 120-step cave queue (`CaveBuildStartupCoordinator`).

## Menus

| Menu | Scope | Behavior |
|------|--------|----------|
| **Build Complete Cave Level** | `FullWorld` | Surface world → terrain AI phases → terrain ladder → **then** cave validate + geo + polish … 120/120 |
| **Build Surface World Only** | `SurfaceOnly` | Trails, roads, water, mountains, openings, vegetation — **no** underground geometry |
| **Build Cave Only — Align to Surface** | `CaveOnly` | Underground only; mouth aligns to `GeneratedSurfaceWorld/CaveOpenings` |

## Generated hierarchy

Under `EnvironmentRoot/GeneratedSurfaceWorld/`:

| Child | Purpose |
|-------|---------|
| **Trails** | Walkable paths (terrain bench + waypoints) |
| **Roads** | Low-relief corridors |
| **Water** | Basins + placeholder planes (no visible debug disc in Play Mode) |
| **Mountains** | Height stamps + peak markers |
| **CaveOpenings** | `SurfaceCaveOpeningMarker` for cave alignment |
| **Vegetation** | Prefab scatter — **per-tile contract** (see below) |

Manifest: `Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json`  
Terrain lock: `SurfacePropTerrainLock.json` (9-tile bounds for scatter)

## Nine-tile vegetation contract

Placement is **per terrain tile**, not only around the play-center disk.

| Category | Target / tile | Minimum / tile |
|----------|---------------|----------------|
| Trees | 35 | 28 |
| Grass | 150 | 110 |
| Bushes | 95 | 75 |
| Ground cover | 110 | 85 |

**Acceptance:** each locked terrain has ≥42 total vegetation instances; world total ≥ `55 × tileCount`. Plans must reach ≥88% of category targets (`CaveBuildWorkflowGuardrails.AuditSurfacePropCoverage`).

Implementation: `SurfaceTerrainPropPlacementRegion`, `SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPass`.

## Terrain ladder rungs (surface)

Includes `prop_trees`, `prop_grass`, `prop_bushes`, `prop_ground_cover`, `heightfield_no_craters`, `trail_walkability`, `cave_mouth_grounding`, etc. Report: `SurfaceTerrainBuildLadderReport.json`.

**Note:** `cave_mouth_grounding` aligns the mouth and may rebuild **descent** only when `RouteTerrainFloor` already exists from cave geo — it does not generate a full cave.

## AI prompts (Generated JSON)

Each phase runs `CaveBuildUnifiedPromptBridge.RefreshForPhase`:

1. Scans `Assets/EnvironmentKit/Generated/*.json` + ResearchCache  
2. Writes `CaveBuildGeneratedJsonManifest.json`, `CaveBuildUnifiedAgentPrompt.md`  
3. Updates `CaveBuildActivePhasePrompt.md`, action plan JSON  

Manual refresh:

```bash
cd Tools/cave-grader
npm run generate-unified-agent-prompt -- --phase=terrain_phase_dem
```

## Request flags (`WorldGenerationRequest`)

- `SurfaceScope` — `FullWorld` | `SurfaceOnly` | `CaveOnly`  
- `SurfaceExtentMeters` (default 220)  
- `SurfaceDirectionCount`, `SurfaceTerrainBuildPasses`  
- `SurfaceIncludeMountains`, `SurfaceIncludeWater`, `SurfaceIncludeRoads`, `SurfaceIncludeTrails`

## Open sky

Surface passes set clear daytime ambient/sun and disable global fog. **Restore Sunny Surface Lighting** if a later cave pass changes mood.

## Related docs

- [REQUIREMENTS.md](REQUIREMENTS.md) — §2.2 vegetation contract  
- [WORLD-GENERATION-PIPELINE-LADDER.md](WORLD-GENERATION-PIPELINE-LADDER.md) — rungs 1–4  
- [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) — `surface_props` rung
