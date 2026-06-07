# First Unity run after surface-grid updates

Use this checklist once on the current commit, then run longer polish if results look good.

## Before you start (~5 minutes)

1. **Stop any stuck build** — if `Library/EnvironmentKit/CaveBuildLiveRunStatus.md` shows an old seam loop at step `1/122`, cancel in the Hub.
2. **Hub → Diagnostics → Apply Reliable FullWorld Preset**
3. **Cave Build → Advanced → Full AAA Rebuild** with **incremental ladder OFF** (first validation on this commit).
4. Active scene should be **MainScene** (needs Ground / portal — not `BossStage_01` alone).

## Recovery menus (new names)

| Menu | When |
|------|------|
| **Cave Build → Advanced → Snap Surface Terrain Grid (81 Tiles, Active Scene)** | ~60 m gaps, misaligned wilderness, before re-auditing |
| **Cave Build → Advanced → Expand Open World — Add Next Terrain Ring** | After 81-tile core is good; adds one ring toward ~289 tiles |

See [SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md](SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md).

## What to watch (first ~30–60 min)

| Milestone | Pass signal |
|-----------|-------------|
| Compile | Console clean |
| Grid / seams | `WorldLayoutAudit.json` → `layoutAcceptable: true`, gaps **&lt; 2 m** |
| Terrain ladder | `SurfaceTerrainBuildLadderReport.json` → `buildAcceptable: true` |
| Mountains / labyrinth | South annex on hills — compare phase `concept.png` |
| Cave | `CaveBuildQualityReport.json` not `Blocked` / `isDud` |

## Concept images (in git)

- Production: `Assets/EnvironmentKit/ResearchCache/images/concepts/phases/<folder>/approved-01…05.png` + `concept.png`
- Regenerate prompts: `npm run generate-phase-prompts` in `Tools/cave-grader` (`research-cache-paths.ts` fixes circular import)

## If it fails early

- **Position gaps ~60–66 m** → **Snap Surface Terrain Grid**, then Full AAA Rebuild; do not run the full cave queue on a broken surface.
- **Blocked cave dud** → fix surface + layout first.
- **Slow flat grid** → confirm `PreferSkipFlatGridPerTileSeams` is on (default); outer ring should use fast batch locks.

## After one acceptable pass

Run recording / long polish only after terrain + layout pass.
