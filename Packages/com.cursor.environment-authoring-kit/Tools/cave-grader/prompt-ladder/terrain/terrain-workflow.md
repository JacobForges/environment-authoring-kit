## Terrain workflow (mirror of cave post-build)

1. Read `SurfaceTerrainQualityReport.json` and `SurfaceTerrainBuildLadderContext.json`.
2. Web research (USGS DEM, trail grade %, vegetation density refs) — brief notes in agent reply only.
3. Plan: list files you will touch for the active rung only.
4. Implement in C# editor scripts or terrain data APIs (no manual Terrain Inspector steps unless unavoidable).
5. Stop when the rung would score ≥ 90; Unity advances the ladder automatically.

### LiDAR / imagery phase (`terrain_phase_dem`)

Before the authoritative DEM stamp, Unity exports:

- `CaveBuildResearchActionPlan` (imagery + EPQS elevation constraints)
- `SurfaceTerrainActiveRungPrompt.md` (grade → plan for the worst ladder rung)
- Phase prompt at `macro_terrain`

After stamp: de-checkerboard + grader-band polish, then continue the terrain ladder. Re-sync cache when blocks persist: `HUB_ROOT=… npm run sync-florida-hillshades -- --elev-grid=128 --pixels=1024`.

### Sculpt agent prompt (before phase 4)

Unity writes `Assets/EnvironmentKit/Generated/SurfaceTerrainSculptAgentPrompt.md` at sculpt start (`SurfaceTerrainSculptPromptBridge`). Agents must read it + `rung-macro_terrain.md` before changing height code.

### Sculpt terracing (stepped ridges)

Do **not** stack many additive Perlin passes with a different `passIndex` UV offset each time — that reproduces Unity Terrain Tools **Strata** banding. Use **world-space FBM** and **lerp** toward one target height field over ~12 blend steps. Post-sculpt polish is **off by default** (`RunPostSculptPolish = false`); commit-time grader smooth is enough. If enabling polish, never blur `y = res-1` (needs `y±1` neighbors). See `SurfaceTerrainCenteredAuthor`.

**Do not** run full cave geometry builds unless the rung is `cave_mouth_grounding` and mouth alignment requires cave root adjustment only.
