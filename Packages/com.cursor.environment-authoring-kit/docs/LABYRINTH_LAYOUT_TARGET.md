# Mountain labyrinth — approved layout target

**Single reference (do not use full-ring or close-up mockups):**

`Assets/EnvironmentKit/ResearchCache/images/concepts/phases/03_south_foothill_labyrinth_row/target-foothill-row.png`

(wrong vs target side-by-side — copy your approved comparison here)

## Target (right panel)

- **North play row (y=+1):** normal walkable play disk + primary cave mouth.
- **Middle + south play rows (y=0, y=-1):** **carved labyrinth benches** on all **6 play tiles** (2×3).
- **South of play:** rolling foothills + peak annex — **trail benches only** (no labyrinth carve) leading to **3 south peak cave mouths**.

## Scope

- Labyrinth carve: play disk offsets `(-1,-1)…(1,0)` — **not** south foothill/peak annex tiles.
- Mouth trails: `mountain_trails` — west / center / east exits → each `MountainWildernessCaveMouth` on south peaks.

## Pipeline rules

1. `mountain_foothill_sculpt` — rolling hills on all foothills including south foothill annex.
2. `mountain_peak_sculpt` — peaks on south peak annex row.
3. `mountain_labyrinth_carve` — bench carve on south foothill annex only; **no post-carve denoise on foothill annex** (preserves benches).
