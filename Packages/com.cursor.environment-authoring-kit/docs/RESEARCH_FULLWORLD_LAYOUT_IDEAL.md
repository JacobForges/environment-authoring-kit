# FullWorld ideal layout (81 tiles) — target reference

**Category:** `fullworld_layout_ideal`  
**Concept image:** `Assets/EnvironmentKit/ResearchCache/images/fullworld-layout-ideal-49/concept.png`  
*(Folder name is historical; grid is now **9×9 = 81** terrains.)*

Agents must open this image at the start of every FullWorld surface/mountain/cave-mouth phase when **Generation style → Ideal … layout** is selected in Environment Kit Hub. Runs do **not** need to be pixel-identical — they should be **similar in silhouette and pipeline order**.

## Target silhouette (what “similar” means)

| Ring | Tiles | Target |
|------|-------|--------|
| Center | 9 | Florida LiDAR play disk — **labyrinth carved on south 2 rows (6 tiles)**; north row walkable + primary cave |
| Foothill | 16 | Rolling relief only — **no maze carve**; **trail benches** from play to south peaks |
| Peak | 24 | Taller massifs; **3 south peak cave mouths (W, C, E)**; summit caps; cliff outer band |
| Horizon | 32 | Softer distant massifs framing the core (Chebyshev 4) |
| Mouths | 2 types | Primary `CaveOpening_Primary` on north play row + **three** `MountainWildernessCaveMouth` on south peak annex |
| Underground | — | Cave buried except mouth; walkway start snapped to surface opening |

## Mountain pipeline order (do not reorder)

1. `mountain_wilderness_tiles` — 81 tiles flat place+seam, then directional terraform (sequential default)  
2. `mountain_foothill_sculpt` / `mountain_peak_sculpt` — per-tile locks  
3. `mountain_wilderness_cave_mouth` — one large bowl (massif only)  
4. `mountain_labyrinth_research` → `mountain_labyrinth_carve` — **play-disk south 2 rows only** (6 tiles)  
5. `mountain_peak_summit_caps` — after labyrinth  
6. `mountain_cliffs` → `mountain_trails` (play → **Cave W / C / E**) → `mountain_play_smooth`  

## Similarity plan (per seed, not clone)

- **Grid:** **9×9** Chebyshev rings (81 terrains); never modular `Environment/Grid` rooms ([RESEARCH_FULLWORLD_DO_NOT.md](RESEARCH_FULLWORLD_DO_NOT.md)).  
- **Labyrinth:** `SurfaceMountainLabyrinthLayout.Generate(seed)` — grid size and entrance vary.  
- **Cave maze:** Prefer `WalkwayLabyrinthCavern` when ideal style is active.  
- **Monitor:** Environment Kit Hub activity feed during build ([FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md)).  
- **Acceptance:** Layout audit ~81 manifest tiles; labyrinth touches play disk; primary mouth on walkable lip.

## Hub preset

**Environment Kit Hub → Generation style → Ideal … layout (labyrinths + mouths)** writes `ActiveGenerationStyle.json` and `WorldGenerationRequest` flags before **Build Complete Cave (122)** runs.

Full catalog (all 20 presets, when to use each, troubleshooting): [FULLWORLD_GENERATION_PRESETS.md](FULLWORLD_GENERATION_PRESETS.md).
