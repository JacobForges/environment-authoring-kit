/**
 * Shared FullWorld concept grid vocabulary for agent prompts and image generation.
 * Authoritative code: SurfaceOpenWorldGridExpansion.cs, FullWorldBiomeZoneLayout.cs
 */

export const TILE_SIZE_M = 220;
export const MAX_CHEBYSHEV_RADIUS = 8;
export const GRID_SIDE = 2 * MAX_CHEBYSHEV_RADIUS + 1; // 17
export const TOTAL_TILE_TARGET = 289; // 17×17

/** 81-tile core (Chebyshev 0–4) */
export const CORE_PLAY_TILES = 9;
export const CORE_FOOTHILL_TILES = 16;
export const CORE_PEAK_TILES = 24;
export const CORE_HORIZON_TILES = 32;
export const CORE_TOTAL_TILES = 81;

/** Open-world expansion (Chebyshev 5–8) */
export const MIXED_RING_MIN = 2;
export const MIXED_RING_MAX = 4;
export const MIXED_RING_TILES = 72;
export const PRESET_ZONE_MIN = 5;
export const PRESET_ZONE_MAX = MAX_CHEBYSHEV_RADIUS;
export const PRESET_ZONE_TILES = 208;

/** Play-disk labyrinth carve: south 2 rows of 3×3 = 6 tiles */
export const PLAY_LABYRINTH_TILES = 6;

export const GRID_SCALE_BLOCK = [
  `FullWorld grid: ${GRID_SIDE}×${GRID_SIDE} Chebyshev (radius ${MAX_CHEBYSHEV_RADIUS}) ≈ ${TOTAL_TILE_TARGET} terrain tiles @ ${TILE_SIZE_M} m each (~${GRID_SIDE * TILE_SIZE_M} m extent).`,
  `Core 81 tiles: 9 play (Cheb 0–1) + 16 foothill (2) + 24 peak (3) + 32 horizon (4).`,
  `Open world: rings 2–4 mixed transition (${MIXED_RING_TILES} tiles) + rings 5–8 preset zones (${PRESET_ZONE_TILES} tiles).`,
  `Play disk = center 3×3 (660 m). Ideal preset: labyrinth carve on south 2 rows only (${PLAY_LABYRINTH_TILES} tiles).`,
].join("\n");

export type ConceptPresetPrompt = {
  index: number;
  id: string;
  title: string;
  layoutFocus: string;
  promptCore: string;
};

export const FULLWORLD_CONCEPT_PRESET_PROMPTS: ConceptPresetPrompt[] = [
  {
    index: 0,
    id: "concept_00_ideal_labyrinth",
    title: "0 Ideal labyrinth",
    layoutFocus: "6-tile play-disk labyrinth + peaks + trails",
    promptCore:
      "3×3 play disk center; north row walkable + primary cave; middle+south rows = 6 labyrinth tiles; foothill/peak/horizon rings; 3 south peak mouths with trails; full 17×17 grid at scale.",
  },
  {
    index: 1,
    id: "concept_01_classic_rings",
    title: "1 Classic rings",
    layoutFocus: "Open concentric rings, minimal maze",
    promptCore:
      "Full 17×17 grid; smooth concentric terrain bands from center play disk outward; no labyrinth carve; organic cellular cave flavor; open exploration rings.",
  },
  {
    index: 2,
    id: "concept_02_florida_karst",
    title: "2 Florida karst",
    layoutFocus: "Flat play + surface water",
    promptCore:
      "Full grid; flat green play disk; visible water basins on mixed rings; karst limestone; no wilderness mouth; Florida LiDAR hillshade read.",
  },
  {
    index: 3,
    id: "concept_03_appalachian_peaks",
    title: "3 Appalachian peaks",
    layoutFocus: "Tall peak ring + summit caps",
    promptCore:
      "Full grid; dramatic tall peak ring (Cheb 3) and outer massifs; summit caps on peaks; foothill labyrinth on; Appalachian ridge silhouette.",
  },
  {
    index: 4,
    id: "concept_04_coastal_fog",
    title: "4 Coastal fog",
    layoutFocus: "Foggy low mountains + water edge",
    promptCore:
      "Full grid; foggy atmospheric outer rings; low gentle mountains; water at perimeter; muted coastal palette; play disk center visible.",
  },
  {
    index: 5,
    id: "concept_05_tomb_raider_vertical",
    title: "5 Tomb Raider vertical",
    layoutFocus: "Vertical climb cave + surface labyrinth",
    promptCore:
      "Full grid; vertical cliff faces on peak ring; surface labyrinth on play south rows; climbing routes; annex cadence; dramatic elevation contrast.",
  },
  {
    index: 6,
    id: "concept_06_sparse_trails",
    title: "6 Sparse trails",
    layoutFocus: "Jump-gap cave + scattered trail network",
    promptCore:
      "Full grid; sparse trail nodes across mixed rings; platformer jump gaps suggested; play disk center; scattered path network not dense maze.",
  },
  {
    index: 7,
    id: "concept_07_water_labyrinth",
    title: "7 Water labyrinth",
    layoutFocus: "Water basins + foothill maze + wilderness mouth",
    promptCore:
      "Full grid; water basins on foothill/mixed rings; foothill labyrinth carve; wilderness mouth on south peak; exploration fantasy with ponds.",
  },
  {
    index: 8,
    id: "concept_08_perimeter_trails",
    title: "8 Perimeter trails",
    layoutFocus: "Trail ring on foothills, roads off",
    promptCore:
      "Full grid; perimeter trail benches on foothill/peak ring edge; labyrinth on play disk; no roads; hiking loop silhouette around core 81 tiles.",
  },
  {
    index: 9,
    id: "concept_09_speed_minimal",
    title: "9 Speed minimal",
    layoutFocus: "Small play core, fast iteration",
    promptCore:
      "Full grid shown but outer rings muted/simplified; small play disk emphasis; minimal detail on rings 5–8; fast-iteration build target.",
  },
];

export function formatConceptGuidePromptBlock(presetIndex: number): string {
  const p = FULLWORLD_CONCEPT_PRESET_PROMPTS[presetIndex] ?? FULLWORLD_CONCEPT_PRESET_PROMPTS[0];
  return [
    "## FullWorld concept guide (layout scale)",
    "",
    GRID_SCALE_BLOCK,
    "",
    `**Active preset ${p.index}:** ${p.title} — ${p.layoutFocus}`,
    p.promptCore,
  ].join("\n");
}
