# FullWorld generation presets (Hub 1–20)

**Authoritative code:** `Editor/Blockout/FullWorldGenerationStyleCatalog.cs`, `FullWorldGenerationStylePreset.cs`  
**Research entries:** `Tools/cave-grader/fullworld-generation-style-papers.ts` (category `fullworld_generation_style`)  
**UI:** **Window → Environment Kit → Hub → Build → FullWorld preset (20)**

These presets are **not** the ScriptableObjects under `Assets/EnvironmentKit/Presets/` (ForestTerrain, SnowDay, VitureXRPro, etc.). Those assets control **atmosphere, scatter, and XR budgets**. This document is only for the **Hub dropdown** that shapes **FullWorld** surface + cave behavior before **Build Complete Cave (122)** runs.

For terrain grid size, Hub monitoring, and sequential builds, see [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md). For the Ideal layout concept image and agent rules, see [RESEARCH_FULLWORLD_LAYOUT_IDEAL.md](RESEARCH_FULLWORLD_LAYOUT_IDEAL.md).

---

## How presets are applied

1. You pick a preset in **Hub → Build** (saved in EditorPrefs `EnvironmentKit_FullWorldGenerationStyle`).
2. On **Build Complete Cave** (or automated FullWorld bootstrap), the kit calls `FullWorldGenerationStylePreset.ApplyTo(request)` on the active `WorldGenerationRequest`.
3. Order inside `ApplyByIndex`:
   - Set `request.GenerationStyleId` (e.g. `ideal_layout_49`).
   - Call `request.EnsureFullWorldSurfaceContract()` — baseline FullWorld flags (9-tile play disk, outer rings, labyrinth/caps/mouth **on by default**).
   - Run the preset’s **Apply** delegate — your chosen style **overrides** specific flags (water off, no labyrinth, snow biome, etc.).
4. The kit writes **`Assets/EnvironmentKit/Generated/ActiveGenerationStyle.json`** (style id, seed, concept image path when Ideal, research categories for agents).
5. Research gates and mountain/cave phases read `GenerationStyleId` and `PropEmphasis` during the build.

**Default Hub selection:** preset **02 — Ideal … layout** (index 1). Change preset **before** starting a build; mid-build changes do not re-run surface already baked.

**Randomize each build** (EditorPrefs `CaveBuild_RandomizeEachTime`) still rolls layout seed and maze details — presets define **families** of behavior, not identical worlds.

---

## Baseline FullWorld contract (all presets)

Every FullWorld build after `EnsureFullWorldSurfaceContract()` starts from:

| Default | Meaning |
|---------|---------|
| `ForceNineTileSquareGrid` | 3×3 play disk + outer wilderness rings on **81 terrains (9×9)** |
| `UseOuterRingMountains` | Foothill + peak + horizon sculpt |
| `SurfaceIncludeMountains` | Mountain pipeline runs |
| `SurfaceIncludeMountainLabyrinth` | Foothill/peak labyrinth carve (unless preset turns off) |
| `UsePeakSummitCap` | Flat tabletop caps on peak ring (unless preset turns off) |
| `UseMountainWildernessCaveMouth` | Optional large bowl on one wilderness tile (unless preset turns off) |
| `PreserveBlockoutGridLayout` | **false** — open-world terrains, not modular `Environment/Grid` rooms |

Presets **only list differences** from this baseline. If a flag is not mentioned in a preset row, the baseline stands.

---

## Quick picker — best case usage

| Your goal | Preset # | Name |
|-----------|----------|------|
| **Ship-quality showcase / agents / docs** | **02** | Ideal layout (labyrinths + dual mouths) |
| First-time kit smoke test | **01** | Classic FullWorld |
| Florida LiDAR karst, primary mouth, water | **03** | Florida karst faithful |
| Dramatic mountains, Smoky/Appalachian read | **04** | Appalachian ridge heavy |
| Maximum visible surface maze | **05** | Labyrinth annex focus |
| Snow biome, clean peaks, minimal maze | **06** | Alpine snow peaks |
| Moody coast, fog, larger extent | **07** | Coastal fog low |
| Vertical cave + surface labyrinth | **08** | Tomb Raider vertical |
| Platformer cave, trails on surface | **09** | Sparse jumps |
| Organic cave + maze, rugged peaks (no caps) | **10** | Organic wilderness |
| Single obvious entrance on play disk | **11** | Primary mouth only |
| Maze without flat summits | **12** | Labyrinth without summit caps |
| Many small surface cave POIs | **13** | Dense satellite POI caves |
| Hiking loops, trails, no roads/water | **14** | Perimeter trails first |
| Lakes/ponds + foothill maze | **15** | Water + labyrinth |
| Gentle hills, low drama outer ring | **16** | Low outer ring |
| Horror reel, dusk, heavy fog | **17** | Fog horror |
| Long approach cave, interview pacing | **18** | Interview winding |
| Experiment with tile layout variant roll | **19** | Layout variant grid |
| Fastest iteration / CI / low-end Mac | **20** | Speed minimal |

**Rule of thumb:** use **02** for anything you will show stakeholders or train agents on; use **20** when you need a shorter surface phase; use **03** when Florida field authenticity matters more than labyrinth spectacle.

---

## Preset reference (all 20)

### 01 — Classic FullWorld (`classic_fullworld`)

**Best for:** Default kit behavior without forcing Tomb Raider labyrinth cadence; good when you want seed-driven variety on cave maze flavor.

**Overrides:** `UseTombRaiderLabyrinthCadence = false` (surface labyrinth still **on** from baseline unless you change other flags elsewhere).

**Cave:** `MazeGenFlavor` stays **-1** (rolled from seed at generate time).

**When to avoid:** You need guaranteed walkway→annex→cavern cadence or Ideal silhouette — use **02** or **05**.

---

### 02 — Ideal 49-tile layout (`ideal_layout_49`)

**Best for:** **Primary production target** — labyrinth foothills, dual cave mouths, summit caps, research-backed layout. Matches concept image at `ResearchCache/images/fullworld-layout-ideal-49/concept.png` (81-tile grid in current builds).

**Overrides:**

- `UseTombRaiderLabyrinthCadence = true`
- `SurfaceIncludeMountainLabyrinth = true`
- `UsePeakSummitCap = true`
- `UseMountainWildernessCaveMouth = true`
- `UseOuterRingMountains = true`
- `MazeGenFlavor = WalkwayLabyrinthCavern` (5)
- `PropEmphasis` includes `ideal_49_labyrinth_foothills`

**Agents:** Must read concept PNG + [RESEARCH_FULLWORLD_LAYOUT_IDEAL.md](RESEARCH_FULLWORLD_LAYOUT_IDEAL.md) at mountain/cave-mouth phases.

**When to avoid:** Speed-minimal iteration only — use **20** first, then switch to **02** for final pass.

---

### 03 — Florida karst faithful (`florida_karst_faithful`)

**Best for:** Play disk driven by **Florida LiDAR** hillshade/DEM, visible water, aquifer/karst research emphasis; **one** clear primary mouth (no wilderness bowl).

**Overrides:**

- `SurfaceIncludeWater = true`
- `UseMountainWildernessCaveMouth = false`
- `MazeGenFlavor = OrganicCellular` (2)
- `PropEmphasis = florida_karst,aquifer_structure`

**When to avoid:** You want a second dramatic mouth on the mountain ring — use **02**, **05**, or **15**.

---

### 04 — Appalachian ridge heavy (`appalachian_ridge_heavy`)

**Best for:** **Tall outer ring**, strong peak read, `mountain_lidar` research category during mountain phases; summit caps on.

**Overrides:**

- `HeightStyle = Mountains`
- `SurfaceIncludeMountainLabyrinth = true`
- `UsePeakSummitCap = true`
- `PropEmphasis = appalachian_ridge,mountain_lidar`

**When to avoid:** Low, gentle horizon — use **16** or **07**.

---

### 05 — Labyrinth annex focus (`labyrinth_annex_focus`)

**Best for:** **Maximum foothill maze** visibility — Tomb Raider cadence, wilderness mouth, walkway labyrinth cave flavor.

**Overrides:**

- `UseTombRaiderLabyrinthCadence = true`
- `SurfaceIncludeMountainLabyrinth = true`
- `MazeGenFlavor = WalkwayLabyrinthCavern` (5)
- `UseMountainWildernessCaveMouth = true`

**Pair with:** Sequential FullWorld terrain **on** (Hub default) so seams finish per tile before labyrinth carve.

**When to avoid:** Alpine open peaks without maze — use **06**.

---

### 06 — Alpine snow peaks (`alpine_snow_peaks`)

**Best for:** **Snow** biome, clear weather, **summit caps**, mountains on, **no surface labyrinth carve** (faster peak read, cleaner silhouettes).

**Overrides:**

- `Biome = Snow`
- `Weather = Clear`
- `UsePeakSummitCap = true`
- `SurfaceIncludeMountainLabyrinth = false`
- `SurfaceIncludeMountains = true`

**When to avoid:** You need play-disk-linked maze benches — use **02** or **05**.

---

### 07 — Coastal fog low mountains (`coastal_fog_low`)

**Best for:** **Foggy**, atmospheric exteriors; **water** on; slightly **larger surface extent** (≥240 m) for misty depth.

**Overrides:**

- `Weather = Foggy`
- `FogDensityMultiplier = 1.35`
- `SurfaceExtentMeters = max(current, 240)`
- `SurfaceIncludeWater = true`

**When to avoid:** Crisp sunny showcase screenshots — use **02** or **03**.

---

### 08 — Tomb Raider vertical (`tomb_raider_vertical`)

**Best for:** **Vertical climb** cave maze plus **surface labyrinth** — climbing routes, annex cadence.

**Overrides:**

- `UseTombRaiderLabyrinthCadence = true`
- `MazeGenFlavor = VerticalClimb` (1)
- `SurfaceIncludeMountainLabyrinth = true`

**When to avoid:** Flat interview-style cave — use **18**.

---

### 09 — Sparse jumps (`sparse_jumps`)

**Best for:** **Jump gaps** in cave (platformer feel); **trails** on surface; more chambers.

**Overrides:**

- `MazeGenFlavor = SparseJumps` (4)
- `SurfaceIncludeTrails = true`
- `CaveChamberCount = max(current, 4)`

**When to avoid:** Walkway/labyrinth narrative cave — use **02** or **05**.

---

### 10 — Organic wilderness (`organic_wilderness`)

**Best for:** **Organic cellular** cave, **labyrinth on**, **no summit caps** — rougher, less “tabletop national park” peak look.

**Overrides:**

- `MazeGenFlavor = OrganicCellular` (2)
- `UsePeakSummitCap = false`
- `SurfaceIncludeMountainLabyrinth = true`

**When to avoid:** You need flat summit plateaus for playability audits — use **02** or **04**.

---

### 11 — Primary mouth only (`primary_mouth_only`)

**Best for:** **Single entrance** on play disk — no `MountainWildernessCaveMouth` bowl; still has labyrinth + caps on ring.

**Overrides:**

- `UseMountainWildernessCaveMouth = false`
- `SurfaceIncludeMountainLabyrinth = true`
- `UsePeakSummitCap = true`

**When to avoid:** Two-entrance fantasy / massif reveal — use **02** or **05**.

---

### 12 — Labyrinth without summit caps (`labyrinth_no_caps`)

**Best for:** **Foothill maze** with Tomb Raider cadence but **no summit cap** stamp — more naturalistic peaks.

**Overrides:**

- `SurfaceIncludeMountainLabyrinth = true`
- `UsePeakSummitCap = false`
- `UseTombRaiderLabyrinthCadence = true`

**When to avoid:** Peak-ring playability caps required — use **02**.

---

### 13 — Dense satellite POI caves (`satellite_poi_dense`)

**Best for:** **Many surface cave markers** (10 satellites), **trails**, random opening sector each build.

**Overrides:**

- `SatelliteCaveCount = 10`
- `SurfaceIncludeTrails = true`
- `PreferredCaveOpeningSector = -1` (random sector)

**When to avoid:** One hero main cave only — use **11** or **03**.

---

### 14 — Perimeter trails first (`perimeter_trails_first`)

**Best for:** **Trail network** on mountains/play edge; **no roads**, **no water**; labyrinth still on.

**Overrides:**

- `SurfaceIncludeTrails = true`
- `SurfaceIncludeRoads = false`
- `SurfaceIncludeMountainLabyrinth = true`
- `SurfaceIncludeWater = false`

**When to avoid:** Karst pools and Florida water features — use **03** or **15**.

---

### 15 — Water + labyrinth (`water_labyrinth`)

**Best for:** **Surface water** plus **foothill labyrinth** and **wilderness mouth** — exploration fantasy with basins and maze annex.

**Overrides:**

- `SurfaceIncludeWater = true`
- `SurfaceIncludeMountainLabyrinth = true`
- `UseMountainWildernessCaveMouth = true`

**When to avoid:** Dry desert mountain — use **04** or **16**.

---

### 16 — Low outer ring (`low_outer_ring`)

**Best for:** **Gentle hilly** outer ring (`HeightStyle = Hilly`), mountains on, **no labyrinth carve** — faster, softer horizon.

**Overrides:**

- `HeightStyle = Hilly`
- `SurfaceIncludeMountains = true`
- `UseOuterRingMountains = true`
- `SurfaceIncludeMountainLabyrinth = false`

**When to avoid:** Signature Ideal labyrinth silhouette — use **02** or **05**.

---

### 17 — Fog horror (`fog_horror`)

**Best for:** **Dusk**, **heavy fog**, dark mood; **InterviewWinding** cave flavor.

**Overrides:**

- `Time = Dusk`
- `Weather = Foggy`
- `FogDensityMultiplier = 1.6`
- `ColorMood = 0.25`
- `MazeGenFlavor = InterviewWinding` (3)

**When to avoid:** Bright Florida karst reference — use **03**.

---

### 18 — Interview winding (`interview_winding`)

**Best for:** **Longer cave** approach (`CaveTunnelSegments ≥ 14`), **winding** maze — good for vertical slices and demo paths.

**Overrides:**

- `MazeGenFlavor = InterviewWinding` (3)
- `CaveTunnelSegments = max(current, 14)`

**When to avoid:** Vertical climb emphasis — use **08**.

---

### 19 — Layout variant grid (`layout_variant_grid`)

**Best for:** Experimenting with **`SurfaceTileLayoutVariant`** rolled from seed (0–47 asymmetric neighbor pattern); forces 9-tile grid + labyrinth.

**Overrides:**

- `SurfaceTileLayoutVariant = -1` (roll from seed)
- `SurfaceIncludeMountainLabyrinth = true`
- `ForceNineTileSquareGrid = true`

**When to avoid:** You need deterministic layout for A/B screenshots — fix seed **and** consider documenting variant index from logs.

---

### 20 — Speed minimal (`speed_minimal`)

**Best for:** **Fastest FullWorld surface phase** — fewer enhancement hooks, lighter terrain passes, fewer satellites.

**Overrides:**

- `RunEnhancementPhases = false`
- `SurfaceDirectionCount = 4`
- `SurfaceTerrainBuildPasses = 8`
- `SurfaceIncludeRoads = false`
- `SatelliteCaveCount = 3`

**Does not disable:** 81-tile grid, LiDAR terraform, or 122-step cave queue — only reduces **extra** surface work.

**When to avoid:** Final quality pass, grading, or Ideal layout audit — always finish with **02** (or your ship preset) on a clean cache or **Full AAA Rebuild**.

---

## Cave maze flavors (`MazeGenFlavor`)

| Index | Name | Typical presets |
|-------|------|-----------------|
| -1 | Roll from seed | **01** Classic |
| 0 | WindingCompact | (seed roll) |
| 1 | VerticalClimb | **08** |
| 2 | OrganicCellular | **03**, **10** |
| 3 | InterviewWinding | **17**, **18** |
| 4 | SparseJumps | **09** |
| 5 | WalkwayLabyrinthCavern | **02**, **05** |

---

## Key `WorldGenerationRequest` flags (glossary)

| Flag | Effect |
|------|--------|
| `SurfaceIncludeWater` | Basins / water features on surface |
| `SurfaceIncludeTrails` | Trail benches and walk routes |
| `SurfaceIncludeRoads` | Road network (off in **14**, **20**) |
| `SurfaceIncludeMountainLabyrinth` | Carved maze corridors on foothill/peak ring |
| `UseTombRaiderLabyrinthCadence` | Walkway → annex → cavern research pacing |
| `UsePeakSummitCap` | Flat caps on peak-ring plateaus |
| `UseMountainWildernessCaveMouth` | Large mouth on one wilderness tile |
| `UseOuterRingMountains` | 81-tile outer sculpt (always on FullWorld baseline) |
| `SatelliteCaveCount` | Extra surface cave opening markers |
| `RunEnhancementPhases` | Enhancement catalog hooks (**20** turns off) |
| `SurfaceTerrainBuildPasses` | Procedural blend passes after DEM (**20** → 8) |
| `SurfaceDirectionCount` | Directional complement axes (**20** → 4) |
| `PropEmphasis` | Comma tags for research lookup (`florida_karst`, `mountain_lidar`, …) |
| `HeightStyle` | Flat / Hilly / Mountains (**04**, **16**) |
| `Biome` / `Time` / `Weather` | Atmosphere and scatter families (**06**, **07**, **17**) |

Full field list: `Editor/Generation/WorldGenerationRequest.cs`.

---

## Recipes vs Hub presets

| Mechanism | Location | Role |
|----------|----------|------|
| **Hub preset (1–20)** | Hub dropdown | Applied on every **Build Complete Cave** via `FullWorldGenerationStylePreset.ApplyTo` |
| **JSON recipes** | `Assets/EnvironmentKit/Recipes/*.json` | Gates, meat loop, research intensity (`aaa-full-cave-production.json` default) |

Recipes and presets **compose**: recipe controls pipeline gates; preset controls layout/cave/surface flavor. For recipe details see [Assets/EnvironmentKit/Recipes/README.md](../../../Assets/EnvironmentKit/Recipes/README.md).

---

## Research and maintenance

| Task | Action |
|------|--------|
| Refresh research papers for all 20 | `cd Tools/cave-grader && HUB_ROOT=/path/to/Hub npm run sync-research-cache` |
| Audit preset vs cache | Hub → **Grade research for build** (optional) |
| Confirm active preset on disk | Read `Generated/ActiveGenerationStyle.json` after Apply |
| Ideal concept missing | Hub → **Reveal ideal layout concept image** or sync ResearchCache |

Category **`fullworld_generation_style`** holds one paper per preset id for agent prompts.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Labyrinth missing | Preset **06**, **16**, or **20**-like flags; audit failed | Use **02** or **05**; check `SurfaceIncludeMountainLabyrinth` in logs |
| Two mouths when you wanted one | Preset **02/05/15** | Use **11** or **03** |
| Build feels like brown room grid | Wrong ground (`Environment/Grid`) | FullWorld needs **SurfaceTerrainMain** as Ground — [RESEARCH_FULLWORLD_DO_NOT.md](RESEARCH_FULLWORLD_DO_NOT.md) |
| Preset seems ignored | Build started before selection saved | Select preset, then click build; verify `ActiveGenerationStyle.json` |
| Same preset, different maze | Expected — seed drives maze/cell layout | Fix seed in Hub settings or use fixed seed pref |
| Surface fast, cave slow | **20** only shortens surface extras | Normal; cave queue still 122 steps |

---

## Related documentation

| Doc | Topic |
|-----|--------|
| [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md) | 81 tiles, Hub monitor, sequential terrain |
| [RESEARCH_FULLWORLD_LAYOUT_IDEAL.md](RESEARCH_FULLWORLD_LAYOUT_IDEAL.md) | Preset **02** agent rules + concept image |
| [RESEARCH_FULLWORLD_DO_NOT.md](RESEARCH_FULLWORLD_DO_NOT.md) | Common mistakes |
| [RESEARCH_MOUNTAIN_TERRAIN.md](RESEARCH_MOUNTAIN_TERRAIN.md) | Mountain phase ladder |
| [PROJECT_LAYOUT_PLAN_20_STEPS.md](PROJECT_LAYOUT_PLAN_20_STEPS.md) | Surface → cave checklist |
| [../README.md](../README.md) | Package install and menus |

---

## For maintainers

When adding preset **21+**:

1. Add `StyleDefinition` in `FullWorldGenerationStyleCatalog.cs`.
2. Extend `StyleCount` and `FullWorldGenerationStyleId` enum.
3. Add entry in `fullworld-generation-style-papers.ts`.
4. Run `npm run sync-research-cache`.
5. Update this document and Hub `DisplayNames` array (auto from catalog).
