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
| Terrain phase 1 — outer smooth | Full `SetHeights` per tile in one frame | **Paced** row upload per tile |
| Ladder fix `heightfield_no_craters` | Full-map crater repair + smooth | **Paced** repair + smooth per surface tile |
| Grader edits | Annulus around Ground anywhere in scene XZ | **Clipped to** `SurfaceTerrainMain` + neighbor tile footprints |
| Props | Radial fallback around Ground in empty space | **Trail slots on terrain only** (no radial scatter in ladder pass) |

## If the editor still hangs

1. **Cave Build → Diagnostics → Emergency: Unfreeze Editor**
2. Check Console for last `[Surface]` line (which step).
3. Neighbor LiDAR continues in background queue after tiles attach — does not block cave startup.

## Agent prompts

See `SurfaceTerrainSculptAgentPrompt.md` and `prompt-ladder/terrain/rung-macro_terrain.md` for anti-terrace height rules.
