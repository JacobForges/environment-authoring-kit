## Rung: floor_collision (player fall-through)

**Goal:** Player must stand on solid walkable ground at `CaveEntrance_SpawnPoint` after portal teleport — no fall-through void.

### Symptoms

- Teleport logs `CaveEntrance_SpawnPoint @ (x, y, z)` but CharacterController drops through mesh.
- Console: `Failed to create agent because it is not close enough to the NavMesh` (fix floor first, then navmesh rung).
- `player_floor` stage score &lt; 95 or issue: "no walkable ground within raycast range".

### MainScene portal teleport (required)

- Player must land on **maze route start** (`CaveEntrance_SpawnPoint` at first `SolutionPath` cell floor), **not** the above-ground `CaveEntrance_Marker` mouth.
- Call `CaveSpawnTeleportAuthority.ApplyMainAreaTeleportSpawn` and relink `PortalFive` / `MainScene_CavePortal` → spawn transform.
- Read live spawn from `CaveBuildLadderContext.json` → `caveEntranceSpawnWorld`.

### Fix in kit code

- `CaveColliderUtility.IsProtectedPlayCollider` — must protect `RouteTerrainFloor` / `LayoutWalkFloor` colliders from perf/playability stripping.
- `CaveFloorSafetyUtility.EnsureRouteTerrainPlayCollider` — MeshCollider + `CaveWalkableMarker` on walk surface.
- `CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface` — uses `PlayerGroundSnap` raycast after `SpawnGroundPad` box collider.
- `CavePerformanceBudget.Apply` — re-ensure floor collider **after** triangle trim pass.
- `CavePlayabilityValidator.CheckEntranceSpawnGrounded` — editor raycast gate (matches runtime).
- Grade stage: `player_floor` (weight 14, critical).

### Research focus (web)

- Unity CharacterController ground detection + MeshCollider non-convex walk mesh.
- 2026 level-design / PCG papers — spawn points on verified walkable surfaces (raycast + collider audit).
- HN: Unity fall through mesh collider, spawn point snap.

### Do not

- Remove `RouteTerrainFloor` MeshCollider for triangle savings.
- Leave spawn Y from layout grid only without `SnapSpawnToWalkSurface` when route terrain mesh differs.
