# Mountain terrain research & pipeline

## Research categories

**Read first:** `fullworld_do_not` — modular `Environment/Grid` is not the FullWorld play space ([RESEARCH_FULLWORLD_DO_NOT.md](RESEARCH_FULLWORLD_DO_NOT.md)).

| Category | Purpose |
|----------|---------|
| `mountain_terrain` | MIT/Harvard/AAA papers — cliffs, trails, perimeter ring, walkable corridors |
| `mountain_lidar` | DEM/LiDAR refs for outer ring only (separate from Florida `lidar_terrain`) |

Sync into Hub:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
HUB_ROOT=/path/to/Hub npm run sync-research-cache
```

Browse: `Assets/EnvironmentKit/ResearchCache/categories/mountain_terrain/` and `mountain_lidar/`.

## Hillshade vs props vs terrain

- **Dark / green canopy pixels** in county hillshade → **tree props** (`SurfaceHillshadeObjectMask` + `AppendHillshadeDetectedTreeSlots`). They are **not** stamped into the heightmap.
- **Macro terrain** uses **elevation grid** (LiDAR) or mid-tone hillshade only (slopes). Outer mountain ring uses procedural sculpt + weak elev bias, not satellite speckle.
- Rebuild surface after changing this split so existing heightmaps are re-stamped.

## FullWorld grid (2026-05)

**81 terrains (9×9):** play 9 + foothill 16 + peak 24 + **horizon 32**. Florida hillshade PNGs stamp height per tile; `mountain_lidar` research guides outer-ring *style* (Appalachian/Smoky massifs), not a separate DEM library. See [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md).

## Paced build phases (Unity)

`SurfaceMountainResearchPipeline` runs after the flat 81-tile grid + directional terraform:

1. **mountain_research** — category-scoped prompts; no sculpt
2. **mountain_lidar** — LiDAR category brief; play disk unchanged
3. **mountain_outer_ring** — `SurfaceOuterRingMountainsAuthor` (world AABB band)
4. **mountain_cliffs** — cliff accent on outer band only
5. **mountain_trails** — perimeter trail benches
6. **mountain_play_smooth** — inner disk + trails smooth (skips cliff band)

## Grading (one task at a time)

- **Mountain:** `SurfaceMountainBuildLadder` + `CaveBuildGradedArtifactGate`
- **Terrain:** `SurfaceTerrainBuildLadder` (nine-tile, heightfield, nav, mouth)
- **Props:** `SurfacePropBuildLadder` (`prop_*` one category at a time)

Failed mountain rungs export `SurfaceMountainActiveRungPrompt.md` and a constrained repair brief.

## Prompt generation (TypeScript)

Phase prompts use `researchCategories` from `mountain-pipeline-phases.ts` via `formatResearchCacheBlockForCategories`. Action plans use `lookupForCategories` first, then fallback heuristics.
