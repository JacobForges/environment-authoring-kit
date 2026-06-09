# NAVMESH_PARTIAL

1. Quality report shows NavMesh partial or `terrain_integration` fail.
2. Run surface NavMesh ladder rung (`SurfaceTerrainBuildLadder` → surface_navmesh).
3. Rebake after props locked to ground; verify entrance→goal path in report.
4. Surface agents only — do not move cave mouth unless ground_placement fails tolerance.

Tag: `// [bot:rung:navmesh:DATE]`
