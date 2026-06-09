# TERRAIN_SEAM_NINETILE

1. Confirm play disk lock — nine `SurfaceTerrainTile_*` tiles in paced seam queue.
2. Let paced seam steps finish; avoid sync `StitchAll` on broken grid.
3. If gaps > 0.5m after queue: purge terrain via layout audit path, then rebuild disk only.
4. Do not expand to full 289-tile grid unless planner session requests it.

Tag: `// [bot:rung:terrain_seam:DATE]`
