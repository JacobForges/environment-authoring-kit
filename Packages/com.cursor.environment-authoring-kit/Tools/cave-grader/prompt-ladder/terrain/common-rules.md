## Terrain grader — common rules

- **Terrain footprint only:** height fixes and props must stay on `SurfaceTerrainMain` + `SurfaceTerrainTile_*` tiles. Do not edit other scene terrains or scatter props in empty space around the play region.

### Sculpt & normalize (precision)

- Read `SurfaceTerrainSculptAgentPrompt.md` before editing `SurfaceTerrainCenteredAuthor` or peak normalize.
- **World FBM lerp** — one target height field; no `passIndex` in noise UV (no Strata terraces).
- **Additive FullWorld** — do not call grid `ApplyHeightStyle` before sculpt (causes visible horizontal ribs in Scene view).
- **Peak normalize** — row-band `SetHeights` only; never `Clone()` + full-map smooth in a single frame.

- Hub root: use `HUB_ROOT` / local cwd for all paths.
- Generated JSON lives under `Assets/EnvironmentKit/Generated/`.
- Surface root name: `GeneratedSurfaceWorld` (`SurfaceWorldPaths.RootName`).
- Re-grade in Unity: **Window → Environment Kit → Terrain Build Grader → Re-grade**.
- Florida DEM / hillshade references: `Assets/EnvironmentKit/ResearchCache/` when sculpting height.
- Props: place **one prefab at a time** per category rung; respect `SurfaceIntelligentPropPlacer` catalog tags.
- Craters: never leave bowl pits in the playable ring — use `SurfaceTerrainCraterRepair` + outer smooth.
- Cave mouth: keep entrance descent walk-in aligned with terrain opening (`SurfaceCaveOpeningAligner`, `CaveSurfaceEntranceBuilder`).
