# Concept image approval queue

**Set 01 (painterly):** category **00** approved — **keep 2, 3, 5** → `../../00_fullworld_overview/approved-02.png`, `approved-03.png`, `approved-05.png`.

**Promoted 2026-06-02:** All **55** Set 02 options copied to `../<folder>/approved-01…05.png` + `concept.png` (hero = approved-03). Live prompts use these via `phase-concept-images.ts`.

**Archive:** User rejects gallery #9,12,14,18,21 → `_do-not/user_rejected/`. Review copies remain in `set-02-unity/` for reference.

Nothing is wired into live phase prompts until you approve into production folders.

## How to respond

Per category, e.g. `00 keep 2 redo 4` or `06 keep 1,3`.

Approved copies go to `../../<folder>/approved-0N.png` (or `concept.png` if you want one hero per folder).

## Generation matrix

See `set-02-unity/CONCEPT_IMAGE_GENERATION_MATRIX.md` and `set-02-unity/SCALE.md` (220 m tiles).

## Code anchors in prompts

Each category has a **Kit code anchor** block in `CaveBuildActivePhasePrompt.md` (from `Tools/cave-grader/phase-concept-code-examples.ts`). Regenerate after kit changes:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
HUB_ROOT=/Users/jacob/Hub npx tsx generate-phase-prompts.ts --rung=ground_placement
```

## Categories (5 options each — Set 02)

| # | Folder | Set 02 status | Notes |
|---|--------|---------------|-------|
| 00 | `00_fullworld_overview/` | 5 options | Ideal 49 silhouette @ 220 m |
| 01 | `01_play_disk/` | 5 options | 3×3 play only |
| 02 | `02_foothill_ring_sculpt/` | 5 options | No maze yet |
| 03 | `03_south_foothill_labyrinth_row/` | 5 options | Rolling hills + benches |
| 04 | `04_peak_ring/` | 5 options | South peak annex |
| 05 | `05_wilderness_mountains/` | 5 options | Outer 9×9 ring |
| 06 | `06_mountain_labyrinth_carve/` | 5 options | Spine batch / benches |
| 07 | `07_mountain_trails/` | 5 options | Perimeter trails |
| 08 | `08_wilderness_cave_mouth/` | 5 options | South peak mouths |
| 09 | `09_cave_primary_mouth/` | 5 options | Play-disk mouth |
| 10 | `10_cave_underground/` | 5 options | RouteTerrain tube |

**Production reference (do not overwrite):** `../../03_south_foothill_labyrinth_row/target-foothill-row.png`
