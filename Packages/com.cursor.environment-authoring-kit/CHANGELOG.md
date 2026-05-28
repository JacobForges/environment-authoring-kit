# Changelog — Environment Authoring Kit

All notable changes to **`com.cursor.environment-authoring-kit`** are documented here.

Format: **[version]** — **date** — summary.

For Hub-wide project notes, see the consuming repo’s `docs/CHANGELOG.md` when present.

---

## [0.3.0] — 2026-05-27

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
- **63-step queued pipeline** documented and aligned: validate → geo 1–13 → playability → validation → world → meat → post-meat → research → manifest → finalize.
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
