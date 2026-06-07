## Rung: navmesh

**Goal:** NavMesh bakes on walkable floor geometry only.

### Fix in kit code

- `LavaTubeCavePostProcess.BakeNavMeshOnly` (1-arg and 2-arg overloads), `CaveBuildQualityStageFixer.FixNavMesh`, adventure `RouteTerrainFloor` collider.
- Ensure floor mesh has non-trigger collider + enabled renderer before bake.
- Re-bake after visual shell / walkway fixes.

### Do not

- Bake on hidden PathPlatforms or trigger-only volumes.
