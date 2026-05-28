# Research workflow (agents)

## Order of operations

1. Open the scoped brief for your work:
   - **Terrain / LiDAR / props / NavMesh:** `Assets/EnvironmentKit/Generated/TerrainResearchExecutionBrief.json`
   - **Cave geometry / shell / combat:** `Assets/EnvironmentKit/Generated/CaveResearchExecutionBrief.json`
   - **Combined (legacy):** `Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json`
2. Open **`Assets/EnvironmentKit/Generated/CaveBuildResearchCache.json`** — pointer to categorized cache + `floridaTerrain` paths.
3. Open **`Assets/EnvironmentKit/ResearchCache/index.json`** — master catalog (includes `floridaTerrain` summary when synced).
4. Open **`Assets/EnvironmentKit/Generated/CaveBuildResearch.json`** — full prestige-lab catalog + `floridaTerrain` + `dataAttribution`.
5. For **ground_placement** / **visual_shell**: read county hillshades and aquifer entries (below).
6. Web search **only** on cache miss (max queries in prompt budget).

## Local files (no API)

- **`ResearchCache/entries/{id}/content.md`** — serialized summaries per source.
- **`ResearchCache/images/{id}/ref-*.png`** — optional preview images (`npm run sync-research-cache:images`).
- **`ResearchCache/images/fl-{bay|washington|jackson|calhoun}-hillshade/hillshade.png`** — county terrain subsets (`npm run sync-florida-hillshades`).
- **`ResearchCache/images/florida-hillshades-index.json`** — index of generated hillshades.

## Florida panhandle + aquifer (cave structure only)

| County | Hillshade path |
|--------|----------------|
| Bay | `images/fl-bay-hillshade/hillshade.png` |
| Washington | `images/fl-washington-hillshade/hillshade.png` |
| Jackson | `images/fl-jackson-hillshade/hillshade.png` |
| Calhoun | `images/fl-calhoun-hillshade/hillshade.png` |

**Use:** bare-earth LiDAR relief, USGS DS 926 structural thickness, FGS subsidence/karst polygons.  
**Ignore for void layout:** water surfaces, TDS, bathymetry, spring flow.

**Attribution:** `Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md`

## Maintainer refresh

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
HUB_ROOT=/path/to/Hub npm run sync-research-cache
HUB_ROOT=/path/to/Hub npm run sync-florida-hillshades
HUB_ROOT=/path/to/Hub npm run sync-research-catalog
```

Unity: research phase runs `sync-research-cache` automatically. Optional hillshades: `CAVE_SYNC_FL_HILLSHADES=1`.

## Preventing build stalls (Complete Cave / FullWorld)

**Root cause (from catalog URLs + kit audits):** Unity’s editor only stays interactive when work is **time-sliced across `EditorApplication.update` ticks**. Blocking the main thread with `Process.WaitForExit` loops (node/tsx), full-map `TerrainData.GetHeights`/`SetHeights`/`Flush`, or `NavMeshBuilder.BuildNavMeshData` freezes the UI even if a progress bar label updates.

| Source (URL in `CaveBuildResearch.json` / cache) | What it implies for this kit |
|--------------------------------------------------|------------------------------|
| [research-workflow.md](research-workflow.md) + [SURFACE_BUILD_RESPONSIVENESS.md](../../../docs/SURFACE_BUILD_RESPONSIVENESS.md) | One heavy op per editor frame; no sync research pull when `ResearchCache` exists |
| [Ubisoft Far Cry 5 tool chain](https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5) (cache: `ubisoft-farcry5-freshwater-cliff-biome-order`) | **64×64 m minimum bake unit**; editor sessions bake **incrementally per sector**, not whole-world in one frame |
| [Unity terrain heightmaps](https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-Heightmaps.html) | Heightmap edits are full-resolution GPU/CPU work — must be row/tile paced |
| [Unity NavMeshBuilder.BuildNavMeshData](https://docs.unity3d.com/6000.5/Documentation/ScriptReference/AI.NavMeshBuilder.BuildNavMeshData.html) | NavMesh bake is synchronous — isolate to **one** pipeline step, never inside validate/research tsx |
| [EA SEED AAA game testing](https://www.ea.com/seed/news/seed-ml-research-aaa-game-testing) | Automated QA runs as **batch/playtest jobs**, not blocking the content-authoring editor thread |
| [arxiv:2503.05146](https://arxiv.org/abs/2503.05146) (Unity ML-Agents / simulation gates) | Simulation & validation loops must not stall the authoring pipeline |
| [arxiv:2510.15120](https://arxiv.org/abs/2510.15120) (PCG + Unity validation) | Level validation is iterative — grade/reloop must be **fast** and non-blocking |

| Symptom | Likely cause | Fix |
|--------|----------------|-----|
| Only `[Cave] Layout roll` in Console, no cave geometry | Startup blocked on **sync** research or hillshade download (10+ min) | Wait for `[Startup] Pre-placement research` logs, or use on-disk cache; set `CAVE_SYNC_FL_HILLSHADES=1` only when refreshing DEM |
| Validate stuck at **7/13** unified manifest | `generate-unified-agent-prompt.ts` ran **sync** on main thread (up to 120s) | Kit now uses `CaveBuildTsxProcessRunner.BeginRun` + `ValidateAwaitingTsx`; ensure prompt MD/JSON on disk or watch `Skipped … tsx` logs |
| Editor frozen but Console shows heartbeat | `WaitForExit` loop inside an `EditorApplication.update` handler | Never sync-wait in validate ticks — use **Emergency Unfreeze** then rebuild |
| Progress stuck on surface peak normalize | Was: `Flush` + 8 neighbor tiles + all features in one frame | Fixed: paced normalize finish, one neighbor tile/frame, split feature steps — see `docs/SURFACE_BUILD_RESPONSIVENESS.md` |
| Cave never starts after surface | `surfaceBuildActive` or `queuedSculptPasses` handoff flags stale | Re-run build or Emergency Unfreeze; gate now calls `ReleaseStuckHandoffForStartup` |
| Pre-build Cursor running | Expected deferral — cave queues after workflow | Watch for `Pre-build Cursor workflow started`; geometry runs via `CaveBuildPendingGeometryBuild` |
| Forced network research | `CAVE_FORCE_RESEARCH_SYNC=1` or **Cave Build → Force research sync** pref | Turn off unless refreshing cache; pulls 137 catalog URLs via HTTP |

**Order after layout roll:** paced pre-placement research (6 steps) → surface world queue → terrain phases → pre-build gate → **cave geometry pipeline** (63 queued steps).

**Execution brief:** always open `CaveBuildResearchExecutionBrief.json` before terrain/LiDAR fixes — do not re-download hillshades every build unless forced.

## Rung-specific folders

- `ResearchCache/categories/terrain/index.json`
- `ResearchCache/categories/ground_placement/index.json`
- `ResearchCache/categories/visual_reference/index.json`
