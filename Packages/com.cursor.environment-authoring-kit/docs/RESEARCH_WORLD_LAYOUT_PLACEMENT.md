# Research — world layout & placement (30 sources)

**Status:** Curated 2026-05-29 for near-perfect **9-tile FullWorld** layout before Option A streaming.  
**Catalog:** `Tools/cave-grader/research-catalog.seed.json` (ids `wlp-01` … `wlp-42`, topic `world_layout_placement`).  
**Related:** [RESEARCH_SURFACE_PROPS_UNDERGROUND.md](RESEARCH_SURFACE_PROPS_UNDERGROUND.md), [RESEARCH_OPEN_WORLD_STREAMING.md](RESEARCH_OPEN_WORLD_STREAMING.md), [WORLD-GENERATION-PIPELINE-LADDER.md](WORLD-GENERATION-PIPELINE-LADDER.md).

**Symptoms this addresses:** props/trees on steep slopes or floating planes, cave shell intersecting surface tiles, uneven scatter, mouth misalignment, “busy” overlap in Scene view.

---

## A. Pipeline order & masks (plan before place)

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-01 | [Far Cry 5 — GDC tool chain](https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5) | 64×64 m sectors; **masks passed between tools** (water → cliffs → biomes). Order is contractual. | Match `SurfaceTerrainAiPhases` → props → pre-build → cave queue. |
| wlp-02 | [Far Cry 5 — Game Developer video](https://www.gamedeveloper.com/design/video-the-world-generation-tech-behind-i-far-cry-5-i-) | Freshwater splines drive water surface, shoreline assets, and **exclusion masks** for later steps. | Trails/hydrology before prop scatter; trail cap ~4%. |
| wlp-03 | [Christian Mills — FC5 notes](https://christianjmills.com/posts/procedural-tools-far-cry-5-notes/) | Houdini Engine chain; incremental sector bake vs nightly full regen. | One neighbor tile per editor frame; don’t rebake all 9 tiles per prop. |
| wlp-04 | [Epic — UE5 PCG overview](https://dev.epicgames.com/documentation/en-us/unreal-engine/procedural-content-generation-overview) | Graph **topological order**; bake-time default; pin seeds while debugging. | `CaveBuildPhaseContractRegistry` invalidation = PCG-style upstream dirty. |
| wlp-05 | [StraySpark — PCG production UE5](https://www.strayspark.studio/blog/procedural-content-generation-pcg-framework-production-ue5-7) | Separate **structural PCG** from organic scatter; don’t mix in one undifferentiated pass. | Surface props after terrain+trails; cave geo after pre-build gate. |

---

## B. Terrain tiles & seams (one continuous ground)

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-06 | [Unity — Terrain.SetNeighbors](https://docs.unity3d.com/ScriptReference/Terrain.SetNeighbors.html) | LOD must match on **all four sides**; call on every tile then flush. | `SurfaceTerrainTileExpansion.RefreshTerrainConnectivity`. |
| wlp-07 | [Unity — Create Neighbor Terrains](https://docs.unity3d.com/Manual/terrain-CreateNeighborTerrains.html) | Fill heightmap from neighbors; **Grouping ID** + Auto connect. | Lock play center before scatter; same resolution all tiles. |
| wlp-08 | [Bugnet — terrain chunk holes](https://bugnet.io/blog/fix-unity-terrain-holes-appearing-at-chunk-boundaries) | Holes = missing neighbors, **edge height mismatch**, or mismatched LOD settings. | Post-expand stitch pass if seams visible in Scene view. |
| wlp-09 | [Unity — Heightmaps](https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-Heightmaps.html) | Height data is authoritative; paint and scatter read same heightfield. | Prop slots use `SampleHeight`; no Y from play center guess. |
| wlp-10 | [Guerrilla — Streaming HZD world](https://www.guerrilla-games.com/read/Streaming-the-World-of-Horizon-Zero-Dawn) | Streaming cells share **edge continuity** rules; not isolated terrain islands. | Option A prep: 128 m cells must inherit same seam discipline. |

---

## C. Surface vegetation & props (rules, not random)

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-11 | [Guerrilla — HZD procedural vegetation](https://www.guerrilla-games.com/read/horizon-zero-dawn-procedural-vegetation-placement) | Placement graph after terrain; density rules per biome. | Unified 2× spread + `OrderSlotsForFullAreaSpread`. |
| wlp-12 | [Guerrilla — GPU runtime placement](https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn) | Runtime fill around player; **terrain theme + roads are inputs**, not outputs of scatter. | Don’t `RequestScriptCompilation` during prop pass. |
| wlp-13 | [GDC Vault — GPU placement HZD](https://www.gdcvault.com/play/1024120/GPU-Based-Run-Time-Procedural) | Interstitial fill between primary points; separate passes for asset classes. | One spread pass per category + polish top-up. |
| wlp-14 | [StraySpark — believable biomes](https://www.strayspark.studio/blog/building-believable-biomes-procedural-scatter) | Slope, altitude, moisture, **exclusion zones** (paths, structures). | `IsWalkableSlope`; trees avoid cave mouth 32 m. |
| wlp-15 | [StraySpark — 16 km² afternoon populate](https://www.strayspark.studio/blog/how-we-populated-16km-open-world) | Biome layers + min spacing + cluster rules; review broad pass before polish. | `SurfacePropPlacementPlan_*.json` + coverage audit. |

---

## D. Cave ↔ surface alignment (no intersecting shells)

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-16 | [USGS DS 926 — Floridan aquifer](https://pubs.usgs.gov/ds/0926/) | Structural surfaces & thickness; void **below** land surface. | `UndergroundDepthMeters`; route floor below mouth. |
| wlp-17 | [FGS/FDEP — karst GIS](https://geodata.dep.state.fl.us/search?layout=grid&tags=karst) | Surface entrance + subsidence features; not random pit on flat mesh. | `CarveTerrainBowlAtMouth`; mouth connector trail. |
| wlp-18 | [Unity — Terrain Paint Texture](https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-PaintTexture.html) | Rock/dirt layers at entrance after height carve. | Post-mouth alphamap in surface ladder. |
| wlp-19 | [Epic — Landscape outdoor terrain](https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-outdoor-terrain-in-unreal-engine) | Terrain **openings** for caves; height blend at hole edge. | `RouteTerrainFloor` before mouth-only fixes. |
| wlp-20 | [ezEngine — procedural placement](https://ezengine.net/pages/docs/terrain/procedural/procedural-object-placement.html) | HZD-inspired; placement only where **terrain + material rules** pass. | `SurfaceTerrainPropPlacementRegion` **UV/slot grid on terrain** — **not** `Environment/Grid` modular rooms (see `RESEARCH_FULLWORLD_DO_NOT.md`). |

---

## E. Validation, grading & automated repair

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-21 | [NVIDIA — Fly, Fail, Fix](https://arxiv.org/abs/2507.12666) | Playtest traces → detect fail → patch level → retry. | Meat loop + `CaveBuildAutomatedValidation` (not every log line). |
| wlp-22 | [EA SEED — AAA ML testing](https://www.ea.com/seed/news/seed-ml-research-aaa-game-testing) | Scale regression on builds; route probes as gates. | `CaveBuildRouteProbe`, NavMesh bot steps. |
| wlp-23 | [Sony — automated PS5 gameplay](https://sonyinteractive.com/en/innovation/research-academia/research/ai-technology-for-automating-gameplay-on-playstation-5-under-human-equivalent-conditions/) | Human-equivalent traversal tests for open areas. | Surface route probe + cave route probe separation. |
| wlp-24 | [Activision Research — QA publications](https://research.activision.com/publications) | Production QA tiers; block ship on critical layout fails. | `CaveBuildPreBuildLadder` + letter grades. |
| wlp-25 | [COMMERCIAL-PRODUCTION-GRADING.md](COMMERCIAL-PRODUCTION-GRADING.md) | Ship/Beta/Alpha thresholds per category. | Target **Beta+** on layout before Option A. |

---

## F. POI, trails & readability (player understands space)

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-26 | [Theseus — Elden Ring wayfinding thesis](https://www.theseus.fi/bitstream/10024/904850/2/Grau_Vesna.pdf) | Terrain cues + landmark spacing without UI spam. | Twin peaks, trail forks, sinkhole POIs. |
| wlp-27 | [GDC — HZD tools pipeline](https://www.gdcvault.com/play/1024124/Creating-a-Tools-Pipeline-for) | Artist-authored rules drive runtime systems. | Hub presets + `WorldGenerationRequest` seed lock. |
| wlp-28 | [SideFX — heightfield scatter](https://www.sidefx.com/docs/houdini/heightfields/scattersop.html) | Scatter points **separate** from heightfield mesh; mask-driven density. | Props never write heightmap in scatter pass. |
| wlp-29 | [SideFX — heightfield creation](https://www.sidefx.com/docs/houdini/heightfields/creation.html) | LOD upsample chain before detail scatter. | Terrain AI phases before vegetation lock. |
| wlp-30 | [Springer — max vegetation coverage (Poisson-style)](https://doi.org/10.1007/s11042-022-12107-8) | Optimization under spacing constraints; no overlap disks. | `DedupeSlotsPreferGrid` + min separation per category. |

---

## G. Mountain ring scale & vertical readability (Lynch edges)

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-31 | [Kevin Lynch — *The Image of the City* (1960)](https://en.wikipedia.org/wiki/The_Image_of_the_City) | **Edges** (mountain ranges, coastlines) break continuity and frame districts; must read from play space. | 9-tile square perimeter = Lynch edge; center tile stays playable karst. |
| wlp-32 | [Alexander Bakharev — MMO world design (2026)](https://medium.com/@alexander.bakharev_16063/so-you-want-to-build-an-mmo-8-18-world-design-level-architecture-c07798d17f1c) | Mountain ranges as linear boundaries; landmarks visible from distance without UI. | Outer ring peaks at corners + edge mids; 12 peaks on 3×3 perimeter. |
| wlp-33 | [SideFX — heightfield creation](https://www.sidefx.com/docs/houdini/heightfields/creation.html) | Macro heightfield first (ridges), detail scatter second; peaks are **world-unit** targets not normalized noise. | `SurfaceOuterRingMountainsAuthor`: corner **+52 m**, edge **+36 m**, band **+22 m** (scaled to `terrainData.size.y`). |
| wlp-34 | [Unreal — Landscape outdoor terrain](https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-outdoor-terrain-in-unreal-engine) | Rock/snow layers driven by **slope + height** (e.g. snow above 800 m); silhouette matters more than micro-noise. | Grade outer edge ≥ **12 m** above play center (Beta); target **18 m+** for Alpha ring read. |
| wlp-35 | [Chris Kempke — game world size & horizon](https://terrain.chriskempke.com/size_of_game_worlds/) | Vertical scale vs draw distance; 3000 m real peaks visible ~200 km — games use **stylized** shorter silhouettes. | MacBook preset `TerrainMaxHeightMeters` **96 m**; Default **140 m** — peaks use ~55–65% of range at corners. |
| wlp-36 | [ResetEra — vertical scale in open worlds](https://www.resetera.com/threads/wish-more-open-worlds-would-have-a-better-sense-of-vertical-scale.163308/) | Many OW games flatten mountains for climbability; **non-climbable edge mass** still sells scale (Witcher 3 Kaer Morhen). | Outer ring is boundary mass; primary trail + mouth stay on inner 9-tile play square. |

**Kit targets (2026-05-29):**

| Artifact | Role |
|----------|------|
| `SurfaceWorldLayoutPrePlan.json` | Research-backed plan **before** LiDAR/sculpt — skip post-DEM sculpt when authoritative, pipeline order, mountain targets, agent hint. |
| `CaveBuildPrePlacementResearchGate.json` | Existing research gate before placement (tsx sync + brief). |

**Kit targets (mountains + perf):**

| Metric | Before | After |
|--------|--------|-------|
| Corner peak rise | ~16 m (0.20 norm on 80 m) | **52 m** world meters |
| Edge peak rise | ~10 m | **36 m** |
| Peak count | 8 | **12** (corners + mids + quarter-edge) |
| Outer band width | 48 m | **88 m** |
| Grader pass (edge Δ) | ≥ 4 m | ≥ **12 m** (85 score) |

---

## H. Terrain sculpt performance (editor freeze audit)

| ID | Source | Summary | Kit use |
|----|--------|---------|---------|
| wlp-37 | [Unity — TerrainData.SetHeights](https://docs.unity3d.com/ScriptReference/TerrainData.SetHeights.html) | Each `SetHeights` **recomputes LOD + vegetation** — avoid per-row calls in automated builds. | Hub builds: in-memory sculpt → **one** commit band upload at pass end. |
| wlp-38 | [Unity Support — SetHeights slow](https://support.unity.com/hc/en-us/articles/205486596-Why-is-the-TerrainData-SetHeight-so-slow) | Use `SetHeightsDelayLOD` during edits; `SyncHeightmap` once when done. | `SurfaceTerrainCenteredAuthor.FlushCommitRows(delayLod: true)` + final sync. |
| wlp-39 | [Unity Discussions — SyncHeightmap after DelayLOD](https://discussions.unity.com/t/getheights-setheights-setheightsdelaylod-and-terrainapi/751634) | `SyncHeightmap` is correct finalize after delayed LOD writes. | Called once after sculpt commit rows complete. |
| wlp-40 | [Guerrilla — HZD streaming](https://www.guerrilla-games.com/read/Streaming-the-World-of-Horizon-Zero-Dawn) | Incremental cell bake vs full-world regen every frame. | Disable `LivePreviewEachRowChunk` when `CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive`. |
| wlp-41 | [Far Cry 5 notes — incremental sector bake](https://christianjmills.com/posts/procedural-tools-far-cry-5-notes/) | Nightly full regen vs per-sector incremental. | Reduce sculpt passes **12→8** (no DEM), **5→4** (post-LiDAR); ladder **8→4** rounds FullWorld. |
| wlp-42 | [Unity Discussions — NativeArray terrain bottleneck](https://discussions.unity.com/t/terraindata-api-nativearray/739763) | CPU `GetHeights`/`SetHeights` dominate after fast Burst jobs; batch writes. | Skip center tile in outer-ring sculpt; Gaussian peak bbox culling. |

**Pipeline changes (2026-05-29 freeze at pass 12/12 rows 512/513):**

| Step | Action | Why |
|------|--------|-----|
| Remove | Per-row `SetHeights` during Hub sculpt (`LivePreviewEachRowChunk`) | ~130× passes × 12 ≈ **1500+ LOD rebuilds** per main tile |
| Remove | Per-pass full heightmap preview in automated builds | Duplicate upload before final commit |
| Reduce | `DefaultPassCount` 12 → **8**; post-DEM 5 → **4** | Same FBM target field; fewer blend iterations |
| Reduce | Terrain meat loop **8 → 4** rounds; ladder fix cap **8 → 4** | Grading still runs; fewer re-carve cycles before props |
| Keep | Paced editor queue (`CaveBuildActionPacing`) | Prevents single-frame main-thread stall |
| Keep | Mouth carve before props + 12 peak outer ring | User scale requirement unchanged |

---

## Kit helpers already in repo (use these first)

| Tool | Role |
|------|------|
| `CaveBuildFullRunPreflight` | Blocks bad builds (ground, catalog, research). |
| `CaveBuildPreBuildLadder` | Score readiness before cave geo. |
| `CaveBuildWorkflowGuardrails.AuditSurfacePropCoverage` | Per-tile vegetation contract. |
| `CaveBuildAutomatedValidation` | Route, shell, geometry, entrance checks. |
| `CaveSceneMaterialRepair` | Pink materials / wrong shader (not layout). |
| `SurfaceTrailCaveMouthConnector` | Trail ↔ mouth alignment. |
| `CaveBuildSurfaceProgress` | Paced surface pipeline (reduces freeze). |
| `grade-and-fix.ts` + Cursor API | **After** JSON reports exist — targeted rung fixes. |

---

## Gaps to build next (fastest impact)

1. **WorldLayoutAudit.json** — single report: seam gap mm, props below min/tile, cave AABB vs terrain overlap, mouth delta Y.  
2. **Hard gate** — block cave queue if `layoutOverlapMeters > threshold`.  
3. **Prop snap pass** — raycast down to terrain after scatter; reject floaters.  
4. **Shell clip mask** — hide cave mesh above surface heightfield + 2 m.  
5. **Cursor agent** — only on failing layout rungs from JSON (not console spam).

---

## 20-step plan — near-perfect layout (fastest path)

**Goal:** One FullWorld build that reads as a single Florida karst surface + buried cave, full 9-tile coverage, grade **Beta+** on layout metrics — **before** Option A streaming.

| Step | Action | Time |
|------|--------|------|
| 1 | Pull research: `cd Tools/cave-grader && npm run sync-research-pull` (indexes this doc + cache). | 5 min |
| 2 | Hub → **Pre-Build Gate**; fix all **BLOCK** rows in `CaveBuildPreflightReport.md`. | 15 min |
| 3 | Confirm **Ground** + **PortalFive** tagged; one starter scene only. | 5 min |
| 4 | Import modular **mesh** cave pack (not texture-only); verify catalog counts in preflight. | 10 min |
| 5 | **Surface Only** build once; verify 9 tiles + `SetNeighbors` (no holes at edges). | 30–60 min |
| 6 | Check `AuditSurfacePropCoverage` in log — every tile ≥ minimum instances. | 5 min |
| 7 | Walk Scene view on **each tile corner** — note seam gaps & floating props list. | 15 min |
| 8 | If props thin: re-run surface half with latest unified spread (already on `main` locally). | 30 min |
| 9 | Run **Terrain Build Grader**; fix failing surface ladder rungs only. | 20 min |
| 10 | Lock **seed** in Hub; export `CaveBuildLadderContext.json`. | 2 min |
| 11 | **Pre-Build Ladder** until ≥88 / build acceptable. | 10 min |
| 12 | **FullWorld 122** — do not interrupt; runner Mac only if CodeQL Sunday. | ~60 min |
| 13 | After surface props: confirm post-prop crater stabilization ran (log line). | 2 min |
| 14 | After cave geo: `CaveSceneMaterialRepair` if pink; ignore UI/TMP shader warnings. | 5 min |
| 15 | Run **Cave Build Grader**; open `CaveBuildQualityReport.json`. | 10 min |
| 16 | If layout fails: read `CaveBuildRouteProbe.json` + visual shell audit — **no full rebuild** unless geo rungs invalid. | 15 min |
| 17 | Optional Cursor: `npm run run` with `CAVE_CURSOR_RUNG` = failing rung only. | 30 min |
| 18 | **Play Mode** walk: mouth → 200 m surface → back; note overlap/floaters. | 20 min |
| 19 | Fix only listed issues (burial, mouth, prop polish); **invalidate** minimal rungs per `PHASE_CONTRACTS.md`. | 1–2 hr |
| 20 | When layout stable on 9 tiles → approve **Option A** checklist; Phase 0 spike (streaming), not wider props yet. | plan |

**Fastest mistake to avoid:** Running FullWorld before surface-only passes and pre-build PASS — wastes an hour and leaves intersecting shells like your screenshot.

---

## Sync commands

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
npm run sync-research-catalog
npm run sync-research-pull   # optional: cache images + Florida hillshades
```

Agent pointer after sync: `Assets/EnvironmentKit/Generated/CaveBuildResearchCache.json` (filter topic `world_layout_placement`).
