# Surface prop placement research

Category: `surface_props` (GOOD) and `prop_do_not` (anti-patterns).

## Agent rules

1. Scatter on **every terrain tile** in the FullWorld manifest (play + foothill + peak), not play disk only.
2. **Reject** spawns on vertical walls: `TerrainData.GetSteepness` > 42° or raycast `normal.y` < 0.55.
3. **No** continuous cliff ring around the entire map — cliffs are selective (outer band + peak shoulders).
4. **Never** invent research entries — read `Assets/EnvironmentKit/ResearchCache` only; refresh with `npm run sync-research-catalog` in `Tools/cave-grader`.

## Kit files (edit with research open)

| File | Purpose |
|------|---------|
| `Editor/Blockout/SurfaceIntelligentPropPlacer.cs` | Main per-tile intelligent scatter |
| `Editor/Blockout/SurfaceTerrainPropPlacementRegion.cs` | Tile bounds / region |
| `Editor/Blockout/SurfaceMountainTerrainPhases.cs` | Cliff accent (must not cliff entire map) |
| `Editor/Blockout/SurfaceMountainPeakDressingAuthor.cs` | Rock splat on peaks |

Sync cache: `cd Tools/cave-grader && npm run sync-research-catalog && npm run sync-research-cache`
