# Terrain tiling & grid index

Multi-tile FullWorld uses **serialized grid indices** plus a JSON manifest so merge, stitch, and AI agents know which terrain owns each slot.

## Scene components

Each terrain tile has `SurfaceTerrainGridIndex` (runtime):

- `gridX`, `gridZ` — offset from `SurfaceTerrainMain` at (0,0)
- `ring` — `play_main` | `play_neighbor` | `foothill` | `peak`
- `tileId` — e.g. `play_1_0`, `foothill_2_-1`
- `mergeTargetTileId` — inner-ring terrain this tile was **seeded/stitched from**

## Manifest (agents read this first)

`Assets/EnvironmentKit/Generated/SurfaceTerrainGridManifest.json`

Written after neighbor spawn, outer-ring spawn, and nine-tile lock. Includes neighbor ids, heightmap resolution, position mismatch vs expected grid origin, and `playDiskLocked`.

## Research

**Not the same as `Environment/Grid` modular rooms.** `terrain_tiling` is Unity Terrain tile stitch (`SetNeighbors`, heightmap edges). For anti-patterns see `RESEARCH_FULLWORLD_DO_NOT.md` (`fullworld_do_not`).

Category: `terrain_tiling` — sync with:

```bash
cd Tools/cave-grader && npm run sync-research-cache
```

Sources: Unity `Terrain.SetNeighbors`, manual heightmap edge stitch, USGS DEM tiling, community seam-blend notes.

## Build order (merge targets)

1. `SurfaceTerrainMain` (play_0_0)
2. Eight `SurfaceTerrainTile_*` — seed from main + spawned cardinals; `mergeTarget` = inner tile toward origin
3. Nine-tile LiDAR per offset (`TileDemSeed`) + seam lock
4. Foothill/peak tiles — seed from `mergeTarget` on Chebyshev-1 inner slot; stitch play→foothill at 0.88 blend
