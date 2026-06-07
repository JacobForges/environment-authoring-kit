# Surface build responsiveness audit

FullWorld surface finish is paced **one editor frame per heavy step**. Anything that ran synchronously after peak normalize caused apparent freezes.

## Pipeline (after sculpt)

| Step | Was (freeze) | Now |
|------|----------------|-----|
| Peak normalize | `Flush` + callback same frame | Row upload → `FinishNormalizeOnNextFrame` |
| Neighbor tiles | Up to 8 terrains + full-map `GetHeights`/`SetHeights`/`Flush` per tile | `QueueAttachGameplayTiles` — **one tile per slot**, seam seed in **row bands** + edge-only reads (no full main heightmap pull) |
| Trails / roads / water / openings / mountains | Single `SurfaceFeatures` mega-step | **5 separate** finish steps, one per frame |
| NavMesh | Sync `BuildNavMeshData` | One frame (unavoidable; still one step) |
| Finish | Manifest | One frame |

## Terrain ladder pass 1 (phase 0 + heightfield fix)

| Step | Was | Now |
|------|-----|-----|
| Terrain phase 1 — outer smooth | Full-map laplacian + `SetHeights` in one frame | **Paced** row compute + row upload per tile |
| Footprint smooth (9 tiles) | Full 513² laplacian + `SetHeights` per queued “light” step | **Paced** row compute + upload; one tile chain per heavy step |
| Neighbor seam stitch | Up to 4× `SetHeights` + LOD rebuild per tile in one frame | **One shared edge per frame**, `SetHeightsDelayLOD`; connectivity dirty deferred |
| Scene view during build | `SceneView.RepaintAll` every paced step on 9 terrains | **Skipped** while `IsLongBuildActive` (orbit camera without 10–30s hitch) |
| Outer ring mountains | Full-tile CPU sculpt in one frame | **Row-band sculpt** + row-band commit; **per-edge outward relief** (`SurfaceTerrainDirectionalEdgeSculpt`) |
| Neighbor tile seed fill | Bilinear from N/S rows only → E/W flat | **Directional edge accent** on all four sides + corner blend |
| `Resource ID out of range in SetResource` | Hundreds of `SetHeightsDelayLOD` without LOD sync | **Fewer bands** during build + **one `SyncHeightmap` per tile**; console filter when suppression on |
| Mountain polish (after outer ring) | N/A | **Trails on mountains** + **play-band smooth only** — does not replace seed merge or outer-ring sculpt |
| Ladder fix `heightfield_no_craters` | Row-by-row scan queue (500+ steps/tile) | **One heavy batch per tile** + label-aware queue batching (6–8 terrain steps/frame) |
| Queue batching (2026-05) | Sequential mode bypassed queue (`ScheduleNextEditorFrame` per light step) | Long builds **always enqueue** light steps; terrain labels batch ×6, cave ×4, agent ×2 |
| Height upload | 8 row bands × queue step | Sequential long build: **single heavy upload** per tile (`QueueWriteFullHeavy`) |
| Row bands (513²) | 8–16 rows/step | Long build: **64–128** rows/step (sculpt/seam/smooth/DEM) |
| Grader edits | Annulus around Ground anywhere in scene XZ | **Clipped to** `SurfaceTerrainMain` + neighbor tile footprints |
| Props | Radial fallback around Ground in empty space | **Trail slots on terrain only** (no radial scatter in ladder pass) |
| Queue flood (2026-06) | Hundreds of duplicate `radial trail bench` jobs → editor quit | **`CaveBuildEditorQueueSafeguard`** — max 12 pending per label; stall warning when depth ≥ 384 |

See also [FULL_AAA_REBUILD.md](FULL_AAA_REBUILD.md).

## If the editor still hangs

1. **Cave Build → Diagnostics → Emergency: Unfreeze Editor**
2. Check Console for last `[Surface]` line (which step).
3. Neighbor LiDAR continues in background queue after tiles attach — does not block cave startup.

## Agent prompts

See `SurfaceTerrainSculptAgentPrompt.md` and `prompt-ladder/terrain/rung-macro_terrain.md` for anti-terrace height rules.
