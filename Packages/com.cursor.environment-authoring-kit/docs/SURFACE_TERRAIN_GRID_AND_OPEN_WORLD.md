# Surface terrain grid and open-world expansion

Canonical reference for **surface placement**, **grid snap**, **fast seams**, and the **extended open-world grid** (2026-06).  
**FullWorld default:** `UseExtendedOpenWorldGrid=true` places **~289 tiles** (17×17, Chebyshev radius **8**) in one build.  
The **81-tile core** (rings 0–4, `FullWorldChebyshevRadius = 4`) remains the mountain/play reference inside the larger grid.

**Grid math:** tile count = (2r+1)². At r=8 → 17×17 = **289** tiles (user target ~284; 289 is the exact Chebyshev square).

Older name: [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md) (Hub monitoring, LiDAR, presets — still valid).

---

## Surface terrain grid — 81-tile mountain core (rings 0–4)

| Ring | Chebyshev distance | Tiles | Prefix |
|------|-------------------|-------|--------|
| Play disk | 0–1 | 9 | `SurfaceTerrainMain`, `SurfaceTerrainTile_*` |
| Foothills | 2 | 16 | `SurfaceMountainFoothillTile_*` |
| Peaks | 3 | 24 | `SurfaceMountainPeakTile_*` |
| Horizon | 4 | 32 | `SurfaceMountainHorizonTile_*` |

**Anchor:** `SurfaceFullWorldGridAnchor` — every slot is `anchor.position + offset × tileSize` (edge-to-edge, no ~60 m drift).

### Editor menus (recovery)

| Menu | Purpose |
|------|---------|
| **Cave Build → Advanced → Snap Surface Terrain Grid (Active Scene)** | One-shot snap + consolidate duplicates + re-index manifest |
| **Cave Build → Advanced → Expand Open World — Add Next Terrain Ring** | Incremental +1 Chebyshev ring (recovery when not using full ~289 build) |

### Build pipeline (flat grid phase)

1. **Place + flat height** — per tile; **no per-tile seam queue** when `PreferSkipFlatGridPerTileSeams` is on (default).
2. **Ring-end connectivity** — refresh neighbors at end of each Chebyshev ring.
3. **Grid weld** — `WeldFullWorldGridEdgesSync` after all planned slots (81 core or ~289 extended).
4. **Outer-ring fast pass** — connectivity + inner locks (`PreferFastFlatGridOuterRingSeams`, batch locks, 120 s budget).
5. **Seam dressing** — `SurfaceSeamDressingPropAuthor` places 3D rock/berm props along peak↔foothill and open-world tile edges after welds.

### Layout audit

- `Assets/EnvironmentKit/Generated/WorldLayoutAudit.json` — `layoutAcceptable`, `maxSeamGapMeters`.
- Wilderness alignment uses the same anchor math as placement (`ExpectedOrigin` + `SurfaceFullWorldGridAnchor`).
- Seam pairing uses **tight** edge adjacency (no diagonal false ~66 m gaps).

---

## Extended open-world grid (~289 tiles) — **FullWorld default**

| Setting | Value |
|---------|--------|
| `UseExtendedOpenWorldGrid` | **`true`** (FullWorld baseline via `EnsureFullWorldSurfaceContract`) |
| Target tile count | **289** (17×17 at radius 8) |
| Max Chebyshev radius | **8** (17×17 = **289** slots) |
| Manifest | `Assets/EnvironmentKit/Generated/OpenWorldGridManifest.json` |
| New tile prefix (rings 5+) | `SurfaceOpenWorldTile_{x}_{y}` |

**Biome zones at radius 8:** 9 play + 72 mixed (rings 2–4) + 208 preset wedges (rings 5–8).

**Default FullWorld / Full AAA Rebuild:** places **all rings 0–8** in the flat-grid phase (`BuildAaaExtendedPlaceOrder`).

**Incremental recovery:** each menu run adds **one** ring only — place flat tiles, weld **boundary** to the previous ring, update manifest.

### Detail per tile (289-scale affordances)

| Setting | Old (1225) | New (289) |
|---------|-----------|-----------|
| Wilderness heightmap cap | 257² | **513²** (matches play disk) |
| Open-world sculpt tier | Horizon-only (soft) | Foothill/Peak/Horizon by ring |
| Terrain max height (default) | 140 m | **200 m** |
| Max ridge rise (LiDAR sculpt) | 18 m cap | **32 m** cap |

---

## Code entry points

| Area | File |
|------|------|
| Grid snap / flat build | `Editor/Blockout/SurfaceTerrainTileExpansion.cs` |
| Open-world rings | `Editor/Blockout/SurfaceOpenWorldGridExpansion.cs` |
| Expected origins | `Editor/Blockout/SurfaceTerrainGridRegistry.cs` |
| Seam 3D dressing | `Editor/Blockout/SurfaceSeamDressingPropAuthor.cs` |
| Layout audit | `Editor/Blockout/CaveBuildWorldLayoutAudit.cs` |

### Toggles (defaults)

| Property | Default | Effect |
|----------|---------|--------|
| `UseExtendedOpenWorldGrid` | `true` | FullWorld places ~289 tiles (set `false` for 81-tile core only — concept 9 Speed minimal) |
| `PreferSkipFlatGridPerTileSeams` | `true` | Skip useless per-tile seam queue on flat grid |
| `PreferFastFlatGridOuterRingSeams` | `true` | Fast outer-ring pass (no paced edge blend) |
| `PreferBatchOuterRingLocks` | `true` | Lock foothill/peak/horizon in one heavy step |
| `OuterRingSeamBudgetSeconds` | `120` | Cap paced lock time if batch is off |

---

## Related docs

| Doc | Topic |
|-----|--------|
| [FIRST_RUN_AFTER_SESSION.md](FIRST_RUN_AFTER_SESSION.md) | Pre-flight checklist after large pipeline changes |
| [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md) | Hub UI, LiDAR, sequential terraform |
| [PHASE_CONCEPT_IMAGES.md](PHASE_CONCEPT_IMAGES.md) | Mountain phase concept images + prompts |
| [PROJECT_LAYOUT_PLAN_20_STEPS.md](PROJECT_LAYOUT_PLAN_20_STEPS.md) | Surface → audit → cave order |
