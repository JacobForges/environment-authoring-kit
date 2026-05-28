## Rung: heightfield_no_craters

Fix terrain heightmap: remove crater bowls and spikes in the playable extent.

- Use `SurfaceTerrainHeightAnalyzer` thresholds from ladder report issues.
- Run `SurfaceTerrainCraterRepair` via `SurfaceTerrainLadderFixer` (no outer-ring smooth — that is `playable_slopes`).
- Reduce radial water bowl if issues mention `SurfaceTerrainRadialAuthor`.
