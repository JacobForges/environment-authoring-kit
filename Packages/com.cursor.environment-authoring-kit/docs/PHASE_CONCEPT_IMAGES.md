# Phase concept images — planning map

Agents get a **Phase concept images** section in `CaveBuildActivePhasePrompt.md` whenever prompts refresh for that pipeline phase.

**Drop PNGs here:** `Assets/EnvironmentKit/ResearchCache/images/concepts/phases/<folder>/`

Regenerate prompts after adding images: `npm run generate-phase-prompts` in `Tools/cave-grader` (or let the build gate run tsx).

Each category also injects a **Kit code anchor** (trimmed C# excerpt) beside its images in `CaveBuildActivePhasePrompt.md` — see `Tools/cave-grader/phase-concept-code-examples.ts`.

**Pending approval (Set 02 Unity screenshots):** `Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/set-02-unity/` — matrix in `CONCEPT_IMAGE_GENERATION_MATRIX.md`, queue in `APPROVAL_MANIFEST.md`.

**Prompt size:** `mountain_labyrinth_*` phases use a compact concept block (fewer bullets/images) because they already load `mountain_labyrinth` research entries.

---

## Your art-direction order (good for planning)

This is how to **think** about the world — storyboard order:

| # | What | Concept folder |
|---|------|----------------|
| 1 | FullWorld overview | `00_fullworld_overview/` (fallback: `fullworld-layout-ideal-49/concept.png`) |
| 2 | Play disk (9 tiles) | `01_play_disk/` |
| 3 | Foothill ring — rolling hills | `02_foothill_ring_sculpt/` |
| 4 | **South foothill labyrinth row** (benches behind peaks) | `03_south_foothill_labyrinth_row/` — **approved:** `target-foothill-row.png` |
| 5 | Peak ring + south peak annex | `04_peak_ring/` |
| 6 | Wilderness mountains (outer ring) | `05_wilderness_mountains/` |
| 7 | Labyrinth carve (implementation) | `06_mountain_labyrinth_carve/` |
| 8 | Perimeter trails | `07_mountain_trails/` |
| 9 | Wilderness cave mouth | `08_wilderness_cave_mouth/` |
| 10 | Primary cave mouth (play) | `09_cave_primary_mouth/` |
| 11 | Underground cave | `10_cave_underground/` |

Yes — **play → foothills (with labyrinth row concept) → peaks → wilderness → labyrinth carve → trails → mouths → cave** is the right mental model.

---

## Unity build order (execution — trails are later)

Mountain pipeline in the editor (`SurfaceMountainResearchPipeline`):

1. `mountain_research_brief`
2. `mountain_wilderness_tiles` — spawn outer ring
3. `mountain_foothill_sculpt` — rolling foothills (+ south annex)
4. `mountain_peak_sculpt` — peaks (labyrinth **not** here)
5. `mountain_wilderness_cave_mouth` — south peak mouths
6. `mountain_labyrinth_research` — plan spines; open `03_` + `06_` concepts
7. `mountain_labyrinth_carve` — bench carve on south foothill row
8. `mountain_peak_summit_caps`
9. `mountain_cliffs`
10. `mountain_trails` — **after** labyrinth
11. `mountain_play_smooth`

Cave (underground) runs on its own ladder (`visual_shell` → mouth seal → platforms → …) while surface work continues in other steps.

---

## Phase id → concept folders

See `Tools/cave-grader/phase-concept-images.ts` (`PHASE_ID_TO_CONCEPT_FOLDERS`) for the full map.

Examples:

| Phase id | Concept folders injected into prompt |
|----------|--------------------------------------|
| `mountain_foothill_sculpt` | foothill sculpt + south labyrinth row |
| `mountain_labyrinth_research` | south row + labyrinth carve |
| `mountain_labyrinth_carve` | south row + labyrinth carve |
| `mountain_trails` | trails |
| `mountain_peak_sculpt` | peak ring |
| `visual_shell` / `layout_platforms` | underground cave |

---

## Approved south-row comparison only

Do **not** use deleted full-ring mockups. Keep only:

`concepts/phases/03_south_foothill_labyrinth_row/target-foothill-row.png`

(See also `docs/LABYRINTH_LAYOUT_TARGET.md`.)
