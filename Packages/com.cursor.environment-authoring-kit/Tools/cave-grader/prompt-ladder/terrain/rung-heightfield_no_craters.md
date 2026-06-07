## Rung: heightfield_no_craters

Fix terrain heightmap: remove crater bowls and spikes in the playable extent.

- Use `SurfaceTerrainHeightAnalyzer` thresholds from ladder report issues.
- Run `SurfaceTerrainCraterRepair` via `SurfaceTerrainLadderFixer` — **one heavy batch per tile** (not row-by-row scan queue).
- No outer-ring smooth here — that is `playable_slopes`.
- Preserve primary cave mouth bowl (`ResolveMouthPreserveRadiusMeters`).

**Stall note:** If Hub shows `scan rows 502/513` for minutes, cancel build and recompile — old paced row-scan path was replaced with sync batch inside `ScheduleHeavy`.
