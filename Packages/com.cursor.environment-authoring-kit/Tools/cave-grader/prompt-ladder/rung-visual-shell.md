## Rung: visual_shell (onion layers)

**Goal:** One walkable route surface — no horizontal onion stacks, no invisible solid colliders, tall natural ceiling.

### Fix in kit code

- `CaveEnclosureShellBuilder`, `CaveCompactLayerPurge`, `CaveBuildVisualShellAuditor`, `CaveBuildQualityStageFixer.FixVisualShell`, `LavaTubeCaveBuildPipeline` (purge before grade).
- Ensure full build produces exactly **one** `RouteTerrainFloor` + **one** `RouteTerrainCeiling` under `CaveGeometry`.
- **Disable** flat `PathPlatforms` renderers/colliders when `RouteTerrainFloor` exists.
- **Purge:** AdventureShell, PathCeiling stacks, per-cell Floor_/Ceiling_ slabs, legacy spline tubes (MainCaveTube, SkySeal).
- Block tunnel: **one ring per path cell** — remove `BlockRingMid_*` mid-rings that stack as onion walls.
- **Stray blocks:** All `CaveBlock_*` must live under `CaveGeometry/BlockTunnel` only. Extend `CaveCompactLayerPurge.PurgeStrayBlockShells` and call it from `Purge()` + before grade in `CaveBuildQualitySystem.Grade`. Delete orphans under `SplineMesh`, root, or legacy hybrid roots.
- **Solid walls:** `CaveBlock_Shell` + `CaveBlock_Minable` keep **renderers on** (rock material). Shell blocks have **no** collider (visual only). Run `CaveInvisibleColliderUtility.StripForAdventure` after block build. Minable outer ring keeps colliders for mining.
- **Ceiling headroom:** `CeilingClearanceAboveFloor` ≥ 2.75× corridor height (~20m default); single `RouteTerrainCeiling` mesh only.
- **Terrain:** Scene `Terrain` = carved mouth + rock paint + entrance walk alignment; underground route = `RouteTerrainFloor` + `RouteTerrainCeiling` meshes (Unity 6 heightmap docs).
- **Ceiling height:** `CaveMazeLayout.CeilingClearanceAboveFloor` ≥ `2× CorridorHeight`; ceiling mesh uses `GetCeilingClearanceAt` — **do not** move walk floor when raising ceiling.
- **Jump pits:** No block rings on `JumpGapCells`. `CavePitFallRecovery` + deep trigger → `CaveMainAreaRespawn` (surface `PlayerSpawn` / `PlayerSpawnPoint`), not invisible pit floors.
- **Terrain:** Prefer `RouteTerrainFloor` / `RouteTerrainCeiling` molded meshes + `UseTerrainCarve` — not hidden box walls.

### Do not

- Add AdventureShell or per-step slab ceilings.
- Leave visible PathPlatform slabs when route terrain floor is present.
