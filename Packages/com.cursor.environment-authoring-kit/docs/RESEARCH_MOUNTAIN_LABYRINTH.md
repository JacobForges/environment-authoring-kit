# Mountain labyrinth (surface)

FullWorld outer-ring **walkway → maze annex** on foothill/peak terrains, stitched to the nine-tile play disk.

**Build-time target:** FullWorld sequential pipeline **under 45 minutes** (`CaveBuildActionPacing`, Sequential FullWorld terrain on Hub).

---

## What IS a mountain labyrinth (kit definition)

A mountain labyrinth is **not** thin scratches on a heightmap and **not** an underground room grid.

| Requirement | Detail |
|-------------|--------|
| **Entrance** | One clear walkway from the nine-tile play disk into the **south** annex (play south edge) |
| **Walkable width** | Wide **trail benches** carved in terrain — ~8.5 m half-width foothill, ~12 m peak |
| **Solution path** | Guaranteed route from entrance → center landmark → back out |
| **Scale** | **3×2 terrain grid south** — 3 foothill tiles (row near play) + 3 peak tiles (deeper south) |
| **Dead ends** | Allowed if readable; optional hidden-door spurs need bench + marker + prop hint |
| **Carve method** | `BuildCarvePolylines` — 3–5 **spine** paths in one heavy batch + one seam pass |

Read: `ResearchCache/entries/environment-authoring-kit-what-is-a-mountain-labyrinth-kit-definition/content.md`

---

## Inspirational style examples (adapt freely — not clones)

Open local PNGs under `ResearchCache/images/` after `npm run sync-research-cache`:

| # | Style | Entry id |
|---|--------|----------|
| 1 | Chartres terrace circular walk | `environment-authoring-kit-style-example-1-chartres-terrace-circular-walk` |
| 2 | Tomb Raider cliff-bench annex | `environment-authoring-kit-style-example-2-tomb-raider-cliff-bench-annex` |
| 3 | Contour switchback hub | `environment-authoring-kit-style-example-3-contour-switchback-hub-maze` |
| 4 | Hub-and-spoke vista junction | `environment-authoring-kit-style-example-4-hub-and-spoke-vista-junction` |
| 5 | Hidden-door spur with hint | `environment-authoring-kit-style-example-5-hidden-door-spur-with-terrain-hint` |

Agents may mix ideas (e.g. switchback approach + hub center + one secret spur). **Creative variation is encouraged** as long as benches stay wide and walkable.

---

## DO NOT produce (labyrinth_do_not)

Includes the **jagged heightmap shard** anti-pattern from failed builds:

- Per-edge maze grid (`BuildCorridorPolylines` / 520-segment queue)
- Dense pixelated rectangular block
- **Thin dark splinters** on flat excavation floor (non-walkable hairlines)
- Double labyrinth pass, `WidenPassages` dilation

Image: `ResearchCache/images/environment-authoring-kit-do-not-jagged-heightmap-shard-lines-in-excavation-thin/ref-0.png`

---

## Pipeline phases (Unity)

| Phase | Work |
|-------|------|
| `mountain_labyrinth_research` | Read definition + 5 style examples + `labyrinth_do_not` |
| `mountain_labyrinth_carve` | Spine carve only — one batch, one seam |

Runs after `mountain_peak_sculpt` (denoise + lock only), before `mountain_cliffs`.

Sync:

```bash
cd Tools/cave-grader && npm run sync-research-cache
```

## Scene outputs

- `GeneratedSurfaceWorld/MountainLabyrinth/` — `MountainLabyrinth_Entrance`, `_Center`, `_Waypoint_*`
- Heightmap benches on foothill/peak along **spine** polylines

## Request flags

- `SurfaceIncludeMountainLabyrinth` (default on for FullWorld)
- `UseTombRaiderLabyrinthCadence` forces surface labyrinth on FullWorld
