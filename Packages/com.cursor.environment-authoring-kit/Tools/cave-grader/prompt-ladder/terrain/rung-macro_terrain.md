## Rung: macro_terrain (world-FBM sculpt + peak normalize)

Precision brief for open-sky height before LiDAR DEM and trails.

### Research first

1. `CaveBuildResearchExecutionBrief.json` + `CaveBuildResearchActionPlan.json`
2. `SurfaceTerrainSculptAgentPrompt.md` (written at sculpt start)
3. Unity Terrain Tools: world-space noise, FBM mountainous shapes — **not** Strata fractal

### Geo + play disk (non-negotiable)

- **Ground center XZ** from `SceneGroundResolver` is the world anchor — cave entrance and trails align to it.
- Florida county hillshade is **research bias only** — map with `WorldToHillshadeUv(world, groundCenter, georef, extent)`.
- **Never** skip stamping/sculpting the inner play disk (`preserve <= 0` skip caused flat quilted cores).
- Prefer counties with **close-up segment** manifests (`PickCountyForSeed`).

### Anti-terrace / anti-stepping (code contract)

| Forbidden | Use instead |
|-----------|-------------|
| `passIndex` in Perlin UV each blend step | Single `SampleWorldFbm(wx, wz, seed)` target |
| Additive `h += noise * (1/N)` per pass | `Lerp(h, targetNorm, passBlend * radial²)` |
| Grid UV `Perlin(nx*4, nz*4)` macro shape | World meters `wx`, `wz` with ~0.0018m⁻¹ base freq |
| `ApplyHeightStyle` on additive FullWorld | Skip overwrite; sculpt from existing Ground disk |
| Full-map `Clone()` soften every pass | Soften once at commit or grader band only |
| Sync `SetHeights` entire map after normalize | Row-band upload via `FlushNormalizeRows` |

### Execute (minimal C#)

- `SurfaceTerrainCenteredAuthor` — blend steps only; `RunPostSculptPolish` off unless debugging
- `SurfaceTerrainGroundLevelNormalizer` — paced scan/apply + incremental upload
- Do **not** add ring wedges, directional passes, or row-flush during sculpt

### Grade

- Ladder rung `heightfield_no_craters` — no bowls, no parallel shelves, peak at Ground Y
- Re-plan with new research URLs if score &lt; 90; one rung per iteration
