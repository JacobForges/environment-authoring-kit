# Set 03 — deployed u3 batch + user rejects

Gallery order = **u2 sorted, then u3 sorted** (same as generation review UI).

## Removed by you (#9, #12, #14, #18, #21)

| # | File | Slot | Reason |
|---|------|------|--------|
| 9 | `u2-01-04.png` | `01_play_disk` option-04 | User reject → `_do-not/user_rejected/` |
| 12 | `u2-02-02.png` | `02_foothill_ring_sculpt` option-02 | User reject |
| 14 | `u2-02-04.png` | `02_foothill_ring_sculpt` option-04 | User reject |
| 18 | `u2-03-03.png` | `03_south_foothill_labyrinth_row` option-03 | User reject |
| 21 | `u2-04-01.png` | `04_peak_ring` option-01 | User reject |

Replacements: **Set 04** deployed → `u4-01-04`, `u4-02-02`, `u4-02-04`, `u4-03-03`, `u4-04-01` into the same five `option-*.png` slots.

## Code examples

Every category folder under `set-02-unity/<folder>/` has **`CODE_ANCHOR.md`** (trimmed C# from the kit). Phase prompts also inject the same via `phase-concept-code-examples.ts` when `generate-phase-prompts` runs.

## Kept — u3 batch (all 20 → set-02-unity)

| # | File | Folder | Option |
|---|------|--------|--------|
| 46 | u3-00-01 | 00_fullworld_overview | 01 |
| 47 | u3-00-02 | 00_fullworld_overview | 02 |
| 48 | u3-00-04 | 00_fullworld_overview | 04 |
| 49 | u3-00-05 | 00_fullworld_overview | 05 |
| 50 | u3-01-01 | 01_play_disk | 01 |
| 51 | u3-01-03 | 01_play_disk | 03 |
| 52 | u3-01-05 | 01_play_disk | 05 |
| 53 | u3-02-01 | 02_foothill_ring_sculpt | 01 |
| 54 | u3-02-04 | 02_foothill_ring_sculpt | 04 |
| 55 | u3-02-05 | 02_foothill_ring_sculpt | 05 |
| 56 | u3-03-01 | 03_south_foothill_labyrinth_row | 01 |
| 57 | u3-03-02 | 03_south_foothill_labyrinth_row | 02 |
| 58 | u3-03-03 | 03_south_foothill_labyrinth_row | 03 |
| 59 | u3-03-04 | 03_south_foothill_labyrinth_row | 04 |
| 60 | u3-04-02 | 04_peak_ring | 02 |
| 61 | u3-04-03 | 04_peak_ring | 03 |
| 62 | u3-04-04 | 04_peak_ring | 04 |
| 63 | u3-04-05 | 04_peak_ring | 05 |
| 64 | u3-05-01 | 05_wilderness_mountains | 01 |
| 65 | u3-05-02 | 05_wilderness_mountains | 02 |

Each folder has **`CODE_ANCHOR.md`** (C# snippet) and prompts pull the same from `phase-concept-code-examples.ts`.

## Still on disk from u2 (kept in set-02)

Categories **06–10** and several u2 **keeps** remain in `set-02-unity/` or `../<folder>/approved-*.png` from prior grading.
