# Set 02 — Unity Scene-view concept generation matrix

Use this when generating **option-01 … option-05** per category. Images are for **approval only** until copied to production folders.

## Global rules (every category)

| Rule | Value |
|------|--------|
| Style | **Unity 6 Editor Scene view screenshot** — dark theme, Hierarchy + Inspector visible, **not** painterly concept art |
| Scale | **220 m × 220 m** per `Terrain` tile (`WorldGenerationRequest.SurfaceExtentMeters`) |
| Camera | Orthographic top-down **or** Scene perspective at human scale (2 m capsule gizmo optional) |
| Content | Real `Terrain` objects, tile seams, sculpt gizmos — **no** UI mockups or miniature dioramas |
| Play disk | 3×3 center = **660 m × 660 m** |
| South annex | Foothill row offsets `(-1,-2),(0,-2),(1,-2)`; peak row `(-1,-3),(0,-3),(1,-3)` |

## Per-category prompt cores (keep under ~120 words each)

| # | Folder | Scene must show |
|---|--------|-----------------|
| 00 | `00_fullworld_overview` | Full 9×9 grid (~1980 m); play disk center; south annex + peaks; **no** labels |
| 01 | `01_play_disk` | **Only** 3×3 play terrains; flat-green bowl; south edge blends toward hills |
| 02 | `02_foothill_ring_sculpt` | Foothill ring **without** maze carve; rolling green; south annex still sculptable |
| 03 | `03_south_foothill_labyrinth_row` | South foothill row: **wide carved benches on rolling hills** — NOT flat tan slabs |
| 04 | `04_peak_ring` | Peak ring + south peak row taller than foothill row |
| 05 | `05_wilderness_mountains` | Outer 9×9 wilderness tiles; horizon; center play smaller |
| 06 | `06_mountain_labyrinth_carve` | Terrain tool + **3–5 spine gizmos**; ~12 m half-width bench; south annex only |
| 07 | `07_mountain_trails` | Perimeter trail benches **after** labyrinth; NavMesh optional blue overlay |
| 08 | `08_wilderness_cave_mouth` | Shallow mouth on **south peak** tile; `MountainWildernessCaveMouth` marker |
| 09 | `09_cave_primary_mouth` | Play-disk shallow walk-in; trail connector; underground route anchor |
| 10 | `10_cave_underground` | `RouteTerrainFloor` / `RouteTerrainCeiling`; buried tube; mouth opening only |

## Anti-patterns (reject / redo)

See also `../../_do-not/` archived rejects and `phase-concept-image-do-not.ts` in cave-grader.

| Tag | Avoid |
|-----|--------|
| `game_view_not_scene` | Play Mode — use Scene tab + Hierarchy |
| `flat_tan_slabs` | Tan/beige flat play or labyrinth shelves |
| `crop_circles` | Concentric rings, geoglyph swirls |
| `spline_chaos_152` | 100+ orange splines / edge-grid carve |
| `circular_hedge_maze` | Chartres circular paver maze |
| `schematic_diagram` | 2D color-block layout without Terrain |
| `miniature_scale` | Diorama under 100 m |
| `wrong_category` | Peak shot in wilderness folder, etc. |

- Painterly aerial “concept art” with no Unity chrome
- Deep vertical pit mouths on play disk (wilderness mouths are **lateral bore** on peaks)

## File naming

`set-02-unity/<folder>/option-01.png` … `option-05.png`

Approved copies → `../<folder>/approved-0N.png` or replace `concept.png`.
