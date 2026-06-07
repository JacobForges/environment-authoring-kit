# Changelog — Environment Authoring Kit

All notable changes to **`com.cursor.environment-authoring-kit`** are documented here.

Format: **[version]** — **date** — summary.

For Hub-wide project notes, see the consuming repo’s `docs/CHANGELOG.md` when present.

---

## [Unreleased]

### FullWorld grid resize (~289 tiles) + immersive detail

- **Grid** — `MaxChebyshevRadius` 17→8 (17×17 = 289 tiles); biome zones recalculated (9 play + 72 mixed + 208 preset).
- **Detail** — wilderness heightmap cap 257²→513²; open-world sculpt uses foothill/peak/horizon tiers by ring.
- **Height limits** — default terrain max height 140→200 m; ridge rise cap 18→32 m; edge/corner peak rises raised.
- **Seam dressing** — new `SurfaceSeamDressingPropAuthor` places 3D rock/berm props along peak↔foothill and tile edges after welds.
- **Step ETC** — extended-grid estimate 32,000→7,500 paced steps.
- Docs/tools updated for 17×17 vocabulary.

---

## [0.3.4] — 2026-06-03

### Full AAA Rebuild safeguards and production scope

- **Editor queue safeguard** — caps duplicate pending jobs per label (e.g. `radial trail bench`), stall warnings when depth stays high.
- **Full AAA Rebuild / + Recording** — forces non-additive surface replace, per-phase `generate-phase-prompts` export (no stale `CaveBuildActivePhasePrompt.md` cache during build).
- **Labyrinth** — seed-random maze spines on south 3×2 annex only; removed hub arcs and fixed west/east column star read.
- **Surface lock** — when underground cave generation starts, blocks further terrain/trail bench/sculpt and surface vegetation meat passes.
- **Hollow Titan** — random above-ground placement with exclusions for cave mouths, labyrinth bounds, and trails.
- Docs: [docs/FULL_AAA_REBUILD.md](docs/FULL_AAA_REBUILD.md), [docs/SURFACE_BUILD_RESPONSIVENESS.md](docs/SURFACE_BUILD_RESPONSIVENESS.md).

---

## [0.3.3] — 2026-06-02

### Surface terrain grid and open-world expansion

- Anchor snap API, layout-audit alignment, skip flat-grid per-tile seams, fast outer-ring locks with time budget.
- `SurfaceOpenWorldGridExpansion` — incremental rings 5–8 toward ~289 tiles (17×17); `OpenWorldGridManifest.json`.
- Menus: **Snap Surface Terrain Grid (81 Tiles)**; **Expand Open World — Add Next Terrain Ring**.
- Docs: [docs/SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md](docs/SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md).

---

## [0.3.2] — 2026-05-25

### FullWorld 81 tiles + Hub-first build UX

- **81-tile grid:** 32 horizon ring tiles (`SurfaceMountainHorizonTile_*`, Chebyshev 4) around existing 49-tile play + foothill + peak core.
- **Environment Kit Hub:** live activity feed, pipeline log, completion banner; modal progress bar suppressed while Hub is open.
- **Pipeline Console:** optional pop-out only; menu redirect to Hub unless forced.
- **Sequential terrain:** `PreferSequentialFullWorldTerrain` (Hub toggle, default on) — sync per-tile seams, idle gate between terraform tiles.
- **Docs:** [docs/FULLWORLD_TERRAIN_AND_HUB.md](docs/FULLWORLD_TERRAIN_AND_HUB.md); README and index refresh.

---

## [0.3.1] — 2026-05-29

### World layout audit (integrated into existing pipeline)

- **Fixed 9-tile 3×3** — `ForceNineTileSquareGrid` (default FullWorld): sequential ring build, outer-ring mountains on whole square edge.
- **Multi-cave** — 1 primary + 7 satellite openings; `CaveBuildSatelliteCaveBuilder` in finalize polish.
- **Terrain ladder rungs** — `nine_tile_grid`, `outer_ring_mountains`, `cave_openings_poi`, `satellite_cave_systems`.
- **`CaveBuildWorldLayoutAudit`** — preflight, surface finish, pre-cave gate, finalize.

### Material upgrader — skip UI/TMP/legacy (build freeze fix)

- `LavaTubeMaterialUpgrader` skips GUI, TextMeshPro, legacy particles, and non-cave asset paths; only reads `_BaseMap` when `HasProperty`.
- Stops thousands of `_BaseMap` console errors during cave geometry that stalled the editor.

### Outer ring mountains + sculpt performance (2026-05-29 PM)

- **Mountain scale** — `SurfaceOuterRingMountainsAuthor`: 12 peaks, world-meter targets (corner +52 m, edge +36 m, 88 m band); skips center tile; bbox-culled Gaussian sculpt.
- **Terrain height budget** — MacBook preset 96 m, Default 140 m vertical range for taller silhouettes.
- **Sculpt freeze fix** — Hub builds disable per-row/per-pass `SetHeights` preview; use `SetHeightsDelayLOD` + one `SyncHeightmap`; passes 12→8 (4 post-DEM); terrain meat loop 8→4 rounds.
- **Research** — [RESEARCH_WORLD_LAYOUT_PLACEMENT.md](docs/RESEARCH_WORLD_LAYOUT_PLACEMENT.md) sections G–H (`wlp-31`…`wlp-42`): mountain scale + terrain perf audit URLs.

### Research — world layout & placement (42 sources)

- [RESEARCH_WORLD_LAYOUT_PLACEMENT.md](docs/RESEARCH_WORLD_LAYOUT_PLACEMENT.md) — eight categories (incl. mountain scale + sculpt perf), kit mapping, 20-step fastest plan.
- `research-visual-references-world-layout.ts` + catalog (`wlp-01`…`wlp-42`).

### Surface props — unified full-map spread

- One spread pass per category: **2×** grid spacing + interstitial offset on all locked terrain tiles (replaces separate primary + wide-spread passes).
- `CollectUnifiedSpreadPlacementSlotsForCategory`; polish top-up retained for contract density.
- `REQUIREMENTS.md` targets aligned with `TargetPerTile` / `MinPlacementsPerTile` in code.

### CodeQL (GitHub code scanning)

- **Verified green** on self-hosted Mac: workflow **CodeQL**, job **Analyze (csharp — Unity)** (~16 min first run including CodeQL bundle download; Unity prep ~2½ min).
- Single workflow [`.github/workflows/codeql.yml`](../../.github/workflows/codeql.yml): C# on `self-hosted` + Unity/`UNITY_PATH`; TypeScript (`cave-grader`) and Actions on GitHub cloud.
- `CodeQlUnityBootstrap`: no second `RequestScriptCompilation` after batchmode import (fixes ~45 min CI hang); accepts `EnvironmentAuthoringKit.*.csproj` without `.sln`.
- `run-codeql-unity-prep.sh` / `codeql-build-csharp.sh` for Unity prep and `dotnet build` tracing.
- Docs: Hub [docs/CODEQL_SETUP_AND_USE.md](../../docs/CODEQL_SETUP_AND_USE.md), package [docs/CODEQL_SELFHOSTED_INSTALL.md](docs/CODEQL_SELFHOSTED_INSTALL.md).
- Batchmode: skip **Assets → Open C# Project** fallback so CI does not launch Cursor on the runner work tree.

---

## [0.3.0] — 2026-05-27

### Documentation (2026-05-28 follow-up)

- Hub [PUBLIC_REPO_SCOPE.md](../../docs/PUBLIC_REPO_SCOPE.md) and aligned README / REQUIREMENTS / package docs for GitHub accuracy (no sample scenes, no store art, XR honesty, ResearchCache local-only).

### Pipeline (120 steps)

- Queued FullWorld cave build expanded from **63 → 120** paced macro steps (`CaveBuildQueuedPipelineSchedule`).
- New **ground polish** block (10 steps): iterative heightmap burial, roof strip, depth-only mouth snap, entrance carve refresh.
- New **finalize polish** block (18 steps): prop coverage, nine-tile vegetation check, burial/roof pass, completion contract before manifest.
- World stages expanded to **15** (burial touch-up, route props, shell gap pass, grounding lock).
- Post-meat expanded to **24** and research to **12** substeps for finer progress and resume points.

### Polish (from prior session)

- `EnsureFullyBuriedUnderSurface` — multi-tile heightmap protrusion loop; mouth snap with `allowRaise: false` after burial.
- Denser score-sorted vegetation + per-category polish top-up pass on surface ladder.
- `SurfaceCaveRoofAuditor` scans full cave root against heightmap Y.

### License

- Replaced **CC0** with **Educational Use free / Commercial requires separate license** — see [LICENSE.md](LICENSE.md).

### Hub + provider routing + flow audit

- Added **Environment Kit Hub** window (`Window → Environment Kit → Hub`) with Build / Settings / Data tabs.
- Added multi-provider settings storage and toggles in Hub:
  Cursor, Google Gemini, Anthropic Claude, OpenAI-compatible, OpenRouter, Local Ollama, Local LM Studio, Custom endpoint.
- Added provider/model/base URL environment export to grader process bootstrap (`CAVE_AI_PROVIDER`, `CAVE_ACTIVE_MODEL`, `CAVE_ACTIVE_BASE_URL`, provider key vars).
- Added in-Hub **flow audit** warnings to flag confusing mismatches (e.g., non-Cursor provider selected while Cursor SDK automation is enabled).
- Updated docs and `.env.example` to include optional provider key fields and current runtime limitation notes.

---

## [0.2.0] — 2026-05-27

### FullWorld pipeline integrity

- **Terrain-first startup:** `CaveBuildStartupCoordinator` finishes surface world + terrain AI phases + terrain ladder before queued cave work (`CaveBuildSurfaceCompletionGate`).
- **Queued pipeline (historical 63 macro steps)** — superseded by **120** steps in v0.3.0; validate → geo 1–13 → playability → … → finalize.
- **Strict playable cave detection:** `HasPlayableCaveLayoutInScene` requires block tunnel rings, full shell (floor + ceiling meshes), or `MainCaveTube` — not `childCount > 2` or ramp-only patches.
- **Incremental ladder** cannot skip geo when scene lacks full cave; FullWorld bootstrap invalidates `cave_layout` when geometry is partial.
- **Removed floor-only mouth shortcut:** terrain ladder no longer calls `BuildFloorOnly` to fake underground layout; mouth fixes require existing `RouteTerrainFloor` from geo steps 1–13.
- **Research gate** no longer blocks validation (32–36) or geo (1–13) steps on prompt export waits.

### Surface vegetation (9-tile contract)

- **Per-tile targets** (× terrain count): trees 35, grass 150, bushes 95, ground cover 110; enforced minimums per tile before fill pass.
- **Grid placement** fills each terrain tile footprint (inset bounds), not only a center play annulus — fixes empty corner tiles on 3×3 grids.
- **Scene contract for `surface_props` rung:** `IsNineTileVegetationSufficient` — ≥42 instances per tile and ≥55×tile total under `GeneratedSurfaceWorld/Vegetation`.
- **Coverage audit** raised to 88% of plan targets (was 55%).

### Fixes

- `UnityEngine.Object` disambiguation in `CaveBuildPhaseContractRegistry`.
- `LogCaveWarning` overload — removed invalid `forceUnityConsole` argument.

### Documentation

- Rewrote package `README.md`, added `docs/REQUIREMENTS.md`, `docs/README.md`, and this changelog for public distribution.

---

## [0.1.9] — 2026-05-21

### Research & Florida terrain

- Mandatory research pull on full builds; `floridaTerrain` in exported JSON.
- [RESEARCH_DATA_ATTRIBUTION.md](docs/RESEARCH_DATA_ATTRIBUTION.md) for USGS / NOAA / FGS credits.

### Editor stability

- Queued meat loop, world stages, and block rings split across editor ticks (`CaveBuildActionPacing`).
- Pre-build / post-build Cursor workflows; no duplicate full rebuild on stack overflow paths.

### Grading & Cursor

- Commercial production tiers; per-rung Cursor prompts.
- Spawn on maze route start; portal / spawn grading weights adjusted.

---

## [0.1.0] — 2026-05 (initial)

- Procedural cave: maze layout, adventure block tunnel, spline hybrid, quality meat loop.
- Surface world generator (trails, roads, water, openings).
- Node `cave-grader` with `@cursor/sdk` grade-and-fix.
