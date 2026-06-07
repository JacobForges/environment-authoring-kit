# Research — FullWorld DO NOT DO (anti-patterns)

**Status:** Required reading for every FullWorld / mountain pipeline phase.  
**Category:** `fullworld_do_not`  
**Catalog:** `Tools/cave-grader/fullworld-do-not-papers.ts`  
**Generated prompts:** `Assets/EnvironmentKit/Generated/CaveBuildDoNotPrompt.md` (includes these rules)

**Why “DO NOT” docs alone did not fix your build:** Research categories guide **AI agents** reading `ResearchCache` — they do **not** change C# execution. The real bug was `SceneGroundResolver` treating **`Environment/Grid`** as Ground (`"grid"` in name heuristics + stored EditorPrefs). FullWorld now uses `ResolveForFullWorld()`, rejects modular Grid, replaces blockout-anchored `GeneratedSurfaceWorld`, and moves the Ground tag to `SurfaceTerrainMain`.

---

## DO NOT — modular blockout as the world

| Anti-pattern | Why it fails | Correct approach |
|--------------|--------------|------------------|
| **`Ground=Grid`** with `Environment/Grid/EnvironmentRooms` as the visible play space | Logs show modular room floors; Florida DEM stamps flat plates on the kit grid instead of a continuous karst disk with mountain edges. | **Terrain-first:** anchor on `SurfaceTerrainMain` (tag Ground on terrain or let the kit rebind after create). |
| **`PreserveBlockoutGridLayout=true`** | Aligns the 9-tile play disk to the modular room cluster; mountains stitch to blockout bounds, not open-world relief. | Default **false** in `EnsureFullWorldSurfaceContract()`. |
| **`CarveTerrainToBlockoutFootprints=true`** | Benches heightmaps under room footprints; destroys foothill merge and perimeter massifs. | **false** unless explicitly authoring kit layout on top of terrain. |
| **Treating the screenshot of brown grid plates as success** | That is `EnvironmentRooms` + early DEM rows on mis-anchored tiles — not foothills, peaks, horizon, or labyrinth. | Expect **81 Unity terrains**: 9 play + 16 foothill + 24 peak + 32 horizon + labyrinth carve on foothill/peak. |
| **Skipping outer-ring pipeline** | Only 9 flat tiles; no `SurfaceMountainFoothillTile_*` / `SurfaceMountainPeakTile_*`. | Run `SurfaceMountainResearchPipeline` after nine-tile lock (`mountain_wilderness_tiles` → … → `mountain_labyrinth_carve`). |
| **Using `terrain_tiling` research to justify room-grid layout** | `terrain_tiling` is **heightmap stitch / SetNeighbors** for Unity terrain tiles — not `Environment/Grid` modular rooms. | Read `terrain_tiling` for seam merge only; read `fullworld_do_not` before any layout decision. |

---

## DO NOT — pipeline order, DEM, and mountains (10 more)

| # | Anti-pattern | Why it fails | Correct approach |
|---|--------------|--------------|------------------|
| 7 | **FullWorld 120 before a clean surface-only pass** | Intersecting shells, bad ground, and wasted GPU time; you get modular plates + half-finished DEM instead of a locked play disk. | Surface-only once → fix 9-tile seams → then FullWorld with outer ring. |
| 8 | **Missing Florida hillshades / georef for seed** | Flat, repetitive height; “Florida LiDAR rows” in the log but no real karst relief on play tiles. | `npm run sync-florida-hillshades` + verify `SurfaceDemGeorefStatus.json` before trusting DEM. |
| 9 | **Deleting or replacing all of `GeneratedSurfaceWorld` mid-build** | Wipes foothill/peak tiles and manifest progress; next pass restarts on Grid anchor again. | Targeted ladder fixes per rung; never purge surface root during mountain pipeline. |
| 10 | **Re-sculpting or procedural height pass on play disk after authoritative DEM + nine-tile lock** | Flattens LiDAR, breaks inner merge, mountains stitch to wrong baseline. | Outer ring sculpt only; inner 9 tiles = DEM + seam stitch + trails/mouth, not new macro noise. |
| 11 | **Peak / Gaussian / outer-ring sculpt on the inner 9 tiles** | Center looks like lumpy plates; perimeter never reads as massif. | Chebyshev-2 = foothill, Chebyshev-3 = peak; play disk stays Florida play height. |
| 12 | **Attaching 40 outer tiles before nine-tile lock validates** | Foothills merge to wrong edges; visible cliffs at play boundary instead of smooth merge. | `QueueAttachGameplayTiles` → unified polish → lock → then `mountain_wilderness_tiles`. |
| 13 | **Judging layout from 9 play tiles only (ignoring manifest ~81)** | Audit passes while foothill/peak/horizon missing or unstitched; Scene view still looks like a room grid. | Open `SurfaceTerrainGridManifest.json`; count outer-ring `mergeTargetTileId` chains. |
| 14 | **Leaving `EnvironmentRooms` meshes as the readable “ground”** | Screenshot success = brown modular floors; terrains exist underneath but invisible as world. | Frame Scene on terrains; kit overlay is optional; terrain is walkable surface. |
| 15 | **Full cave rebuild for surface / mountain bot failures** | Hours lost; cave geo unrelated to missing `SurfaceMountainPeakTile_*`. | Fix failing surface/mountain rung from JSON only (`CaveBuildDoNotPrompt.md`). |
| 16 | **Heavy smooth / radiate-replace on main-land preserve disk (~45% center)** | Destroys mouth bowl, karst dips, and trail readability after LiDAR. | `mountain_play_smooth` on inner disk + trail benches only; skip outer cliff band. |
| 17 | **Per-edge maze heightmap grid (`BuildCorridorPolylines` queue)** | ~520 editor steps, 90+ min builds, dense pixelated rectangular block on foothills (see screenshot in `labyrinth_do_not`). | **Spine carve only:** `BuildCarvePolylines` (3–5 paths), one `ScheduleHeavy` batch, one seam — see [RESEARCH_MOUNTAIN_LABYRINTH.md](RESEARCH_MOUNTAIN_LABYRINTH.md). |
| 18 | **Double labyrinth pass (peak_sculpt + carve)** | Doubles queue time and amplifies grid artifacts on peak tiles. | Labyrinth runs **once** in `mountain_labyrinth_carve`; peak sculpt = denoise + lock only. |

**Build-time target:** FullWorld sequential pipeline **under 45 minutes** with pace batching (`CaveBuildActionPacing`, Sequential FullWorld terrain on Hub).

---

## DO — terrain-first FullWorld (user target)

1. **`SurfaceTerrainMain`** + 8 `SurfaceTerrainTile_*` — seamless Florida play disk (LiDAR authoritative).
2. **16** `SurfaceMountainFoothillTile_*` merged to the play disk inner edges.
3. **24** `SurfaceMountainPeakTile_*` woven into foothills.
4. **32** `SurfaceMountainHorizonTile_*` on Chebyshev 4 (distant framing).
5. Wilderness stitch on the **9×9** grid; monitor in **Environment Kit Hub** ([FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md)).
6. **`SurfaceIncludeMountainLabyrinth`** — **spine** walkway benches on foothill/peak (~2 tiles north), stitched to play AABB — not per-cell edge grid.
7. **`SurfaceTerrainGridManifest.json`** — ~81 entries with `mergeTargetTileId` chains.

---

## Confused terms (keep separate)

| Term | Means |
|------|--------|
| **9-tile play disk / 81-tile FullWorld grid** | Unity `Terrain` tiles + `SurfaceTerrainGridIndex` (9 play + 16 foothill + 24 peak + 32 horizon). |
| **Environment/Grid** | Modular mesh blockout for room kit — **not** the surface world. |
| **Prop placement grid** | UV/slot scatter on terrain (`SurfaceTerrainPropPlacementRegion`) — not modular rooms. |

---

## Sync

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
npm run sync-research-catalog
```

Filter cache: `Assets/EnvironmentKit/ResearchCache/categories/fullworld_do_not/`
