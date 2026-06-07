# FullWorld terrain grid, Hub monitoring, and build pacing

> **2026-06:** Grid snap, fast flat seams, and open-world expansion are documented in **[SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md](SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md)**. This file remains the Hub / LiDAR / preset reference.

Canonical reference for **2026-05** pipeline behavior. When menus or step counts drift, trust **`CaveBuildQueuedPipelineSchedule.Total`** (currently **122**) and code under `Editor/Blockout/`.

---

## Environment Kit Hub (primary UI)

**Menu:** **Window → Environment Kit → Hub**

| Tab | Purpose |
|-----|---------|
| **Build** | **Build Complete Cave (122)**, surface/cave-only variants, **FullWorld generation style** (20 presets — see [FULLWORLD_GENERATION_PRESETS.md](FULLWORLD_GENERATION_PRESETS.md)), optional research maintenance buttons, live build monitor |
| **Settings** | AI providers, prefab folders, queue pacing, XR / scene-view options |
| **Data** | Preview `Assets/EnvironmentKit/Generated/` artifacts |

### During a build

- **Pipeline** line — Running / Idle and **queued step** (e.g. `12/122`).
- **Main action** / **Sub-action** — current phase (research gate, terraform, seam stitch, cave geo, …).
- **Activity feed** + **Pipeline log** — scrollable history (newest at bottom).
- **Live status (markdown)** — snapshot of `CaveBuildLiveRunStatus.md`.
- **Pin Hub during active build** — keeps Hub focused; **no second modal progress window** while Hub is open.

### Completion

When Hub was open at finish, the **completion dialog is suppressed**; a **completion banner** in the Build tab links to `Generated/CaveBuildCompletionReadout.md`.
Blocked/early-stop outcomes are also reported as **non-modal Hub banners** during Hub-driven builds.

### Demo recording support

- Build tab AAA foldout includes **Full AAA Rebuild + Recording**.
- Scene view section includes:
  - **Auto-record build recap video**
  - **Verify ffmpeg now**
  - **Install ffmpeg helper**
  - **Reveal last demo output**
- Demo artifacts are written to `Library/EnvironmentKit/DemoCapture/<timestamp>/`.

### Pipeline Console (optional)

**Cave Build → Diagnostics → Pipeline Console** is a **pop-out** for filtered log lines. On a normal Hub build it **does not auto-open** (saves editor RAM). Use **Pop out Pipeline Console (extra RAM)** only if you need a second window.

---

## FullWorld terrain grid — **81 tiles (9×9)**

Chebyshev rings from `SurfaceTerrainMain` (Ground). Counts are fixed in `SurfaceTerrainTileExpansion`:

| Ring (Chebyshev distance) | Count | Tile name prefix | Role |
|---------------------------|-------|------------------|------|
| 0–1 | **9** | `SurfaceTerrainMain` + `SurfaceTerrainTile_*` | Play disk — walkable, trails, primary cave mouth |
| 2 | **16** | `SurfaceMountainFoothillTile_*` | Foothills — labyrinth carve, merge to play |
| 3 | **24** | `SurfaceMountainPeakTile_*` | Peaks — taller massifs, summit caps |
| 4 | **32** | `SurfaceMountainHorizonTile_*` | Horizon — softer distant framing (~62% peak rise) |

**Total:** `9 + 16 + 24 + 32 = 81` terrains.

Historical docs may say **49 (7×7)**; that was play + foothill + peak only. New full builds include the **horizon** ring unless you use an older branch.

### Build order (surface)

1. **Flat grid** — uniform height, place tiles ring-by-ring; **per-tile seam queue skipped** by default (`PreferSkipFlatGridPerTileSeams`); ring-end connectivity + post-grid weld.
2. **Ground snap + grid weld** — align all 81 slots to play-disk ground Y on `SurfaceFullWorldGridAnchor`.
3. **Outer-ring fast pass** — batch inner locks + connectivity (`PreferBatchOuterRingLocks`, optional time budget); not hundreds of paced edge blends on flat tiles.
4. **Directional terraform** — one tile at a time: LiDAR/DEM stamp (where applicable) → wilderness sculpt → seams → inner lock → **next tile** when sequential mode is on.
5. **Mountain research pipeline** — labyrinth, cliffs, trails, wilderness mouth, etc. ([RESEARCH_MOUNTAIN_TERRAIN.md](RESEARCH_MOUNTAIN_TERRAIN.md)).

Hub checkbox: **Sequential FullWorld terrain (finish each tile before next)** — default **on**. Turn off only for faster but interleaved queue behavior (many `seam edge blend` micro-steps).

---

## LiDAR — what actually drives height

| Source | On disk | Layout? | Height? |
|--------|---------|---------|---------|
| **Florida panhandle hillshades** | `ResearchCache/images/fl-*-hillshade/hillshade.png` + `elevation-grid.json` | No | Yes — play disk + per-tile stamps (seed-picked county) |
| **Procedural FBM + mountain sculpt** | Code | Yes (rings, labyrinth, mouths) | Yes — foothill / peak / horizon |
| **`mountain_lidar` research** | `ResearchCache/categories/mountain_lidar/` | Guides agents only | References USGS 3DEP, Appalachian/Smoky *style*; reuses FL segments on outer tiles as weak macro bias (≤28% structural guide) |

Layout silhouette for **Ideal** preset: concept image `fullworld-layout-ideal-49/concept.png` + `fullworld_layout_ideal` research — not a DEM photocopy.

Sync Florida segments:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
HUB_ROOT=/path/to/Hub npm run sync-florida-hillshades
```

Full cache:

```bash
HUB_ROOT=/path/to/Hub npm run sync-research-cache
```

---

## Research — automatic vs Hub buttons

| When | What runs |
|------|-----------|
| **During build** | `CaveBuildPhaseResearchGate` before queued steps; full cache pull on phase boundaries when needed |
| **Mountain phases** | Category briefs (`mountain_terrain`, `mountain_lidar`, …) via `SurfaceMountainResearchPipeline` |
| **Hub → Enrich research entries** | Optional maintenance — rewrites all `content.md` from seed (`sync-research-cache`) |
| **Hub → Grade research for build** | Optional audit/repair for active style + phase |

You do **not** need to click Enrich/Grade before every build if `ResearchCache/` is already populated.

---

## Queued cave pipeline

After surface + startup: **122** queued steps (validate, geo 1–13, playability, meat loop, finalize). UI labels may still say **120** in places; completion target is **`CaveBuildQueuedPipelineSchedule.Total`**.

---

## Related docs

| Doc | Topic |
|-----|--------|
| [FULLWORLD_GENERATION_PRESETS.md](FULLWORLD_GENERATION_PRESETS.md) | All 20 Hub presets — selection guide and flag reference |
| [RESEARCH_FULLWORLD_LAYOUT_IDEAL.md](RESEARCH_FULLWORLD_LAYOUT_IDEAL.md) | Ideal preset silhouette (81-tile grid) |
| [RESEARCH_FULLWORLD_DO_NOT.md](RESEARCH_FULLWORLD_DO_NOT.md) | Do not use modular `Environment/Grid` as FullWorld play space |
| [RESEARCH_MOUNTAIN_TERRAIN.md](RESEARCH_MOUNTAIN_TERRAIN.md) | Mountain phase ladder |
| [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md) | USGS / NOAA / FGS credits |
| [SURFACE-WORLD-BUILD.md](SURFACE-WORLD-BUILD.md) | Surface menus and scopes |
| [WORLD-GENERATION-PIPELINE-LADDER.md](WORLD-GENERATION-PIPELINE-LADDER.md) | Global rung order |
