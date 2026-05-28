## Rung: ground_placement (underground integration)

**Goal:** `UndergroundCaveSystem` sits **below** the walkable ground surface — not floating at grid origin with a thin shell in void.

### Real-world reference (Florida panhandle — local cache)

Read **before** web search:

1. `Assets/EnvironmentKit/ResearchCache/images/fl-{bay|washington|jackson|calhoun}-hillshade/hillshade.png` — county bare-earth relief (after `npm run sync-florida-hillshades`).
2. `ResearchCache/entries/fl-aquifer-ds926-structural-surfaces/content.md` — Floridan aquifer unit thickness / structural surfaces (**cave void scale only**).
3. `ResearchCache/entries/fl-fgs-subsidence-karst-incidents/content.md` — collapse / subsidence seeds.
4. `CaveBuildResearch.json` → `floridaTerrain` block (paths + policy).

**Do not use** for underground layout: water table, TDS boundary, bathymetry, inundation DEMs, spring discharge volumes. See `docs/RESEARCH_DATA_ATTRIBUTION.md`.

### Fix in kit code (implement research, do not skip)

Use hillshade + aquifer entries you read in `CaveBuildResearchExecutionBrief.json`:

- `CaveGroundPlacementUtility`, `SplineLavaTubeCaveGenerator.GetEntranceWorldPosition`, `CaveAdventureCaveGenerator`, `LavaTubeCaveGenerator.GetEntranceWorldPosition`, `SceneGroundResolver` (exclude cave children from ground bounds).
- Align mouth using **bare-earth** county hillshade relief — cite which `fl-{county}-hillshade/hillshade.png` you used.
- Cave root world position: `ground.SurfaceY - CaveGeometryPaths.UndergroundDepthMeters` on the entrance edge (same as `GetEntranceWorldPosition`).
- Entrance child stays at local `+UndergroundDepthMeters` so the mouth meets surface Y.
- Route terrain meshes use maze **local** floor samples — do not offset ceiling/floor world Y above surface.
- Call `TryAlignUndergroundRoot` from build finalize and `CaveBuildQualityStageFixer` when this rung fails.

### Verify

- `CaveBuildQualityGrader.GradeTerrainIntegration` / placement error < 1.5m.
- No “floating shell” visible above terrain in Scene view.
