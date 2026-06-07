/**
 * Per-preset biome/prop layout prompts for FullWorld surface prop pass.
 * Master concept: Assets/EnvironmentKit/ResearchCache/images/biome-layout-concepts/master/concept.png
 */

export const BIOME_LAYOUT_CONCEPTS_ROOT_REL =
  "Assets/EnvironmentKit/ResearchCache/images/biome-layout-concepts";

export const BIOME_LAYOUT_MASTER_IMAGE_REL = `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/master/concept.png`;

export const BIOME_LAYOUT_SCALE_BLOCK = [
  "FullWorld biome layout: 289 tiles (17×17 Chebyshev), play disk center (9 tiles),",
  "72 mixed transition ring (cheb 2–4), 208 preset wedges (cheb 5–8).",
  "Prop transitions use feather bands (~1.75 tiles) — mix adjacent biome prop pools by distance,",
  "not terrain alphamap fade. Mystical Florida karst aesthetic.",
].join("\n");

export const BIOME_LAYOUT_FEATHER_RULES = [
  "Feather rules: center of biome = 100% that biome's prop catalog.",
  "Within ~1.75 Chebyshev tiles of a zone boundary, weighted mix of both adjacent biomes' trees/grass/rocks.",
  "Play karst meadow → foothill green → peak stone → horizon mist → preset wedge.",
  "Water/coastal presets (4, 7) favor blue plants + sparse palms.",
  "Paced queue placement respects hardware budget (DefaultPropsPerEditorChunk).",
].join("\n");

export type BiomeLayoutPresetPrompt = {
  index: number;
  id: string;
  label: string;
  dominantBiomes: string;
  propSetSummary: string;
  promptCore: string;
  ringLayout: string[];
  builderNotes: string[];
  conceptImageRel: string;
  fullWorldConceptImageRel: string;
};

export const BIOME_LAYOUT_PRESET_PROMPTS: BiomeLayoutPresetPrompt[] = [
  {
    index: 0,
    id: "biome_layout_00_ideal_labyrinth",
    label: "Ideal labyrinth — karst play + foothill/peak rings",
    dominantBiomes:
      "PlayKarst (center), FoothillGreen (cheb 2–4), PeakStone (5–7), MixedTransition (8–9), ConceptPreset00 wedge",
    propSetSummary:
      "Karst meadow poplar/beech + fern ground cover; foothill green pine/oak; peak blue spruce + granite rocks",
    promptCore:
      "Center play disk reads as open karst meadow with scattered poplar and beech. South labyrinth annex gets fern/mushroom understory only. Mixed ring feathers play→foothill→peak toward preset wedge.",
    ringLayout: [
      "Cheb 0–1: PlayKarst — open karst meadow",
      "Cheb 2–4: FoothillGreen feather from play",
      "Cheb 5–7: PeakStone — spruce + granite boulders",
      "Cheb 8–9: MixedTransition feather toward preset wedge",
      "Cheb 10+: ConceptPreset00 wedge",
    ],
    builderNotes: [
      "Reference master + fullworld-concepts/00/concept.png.",
      "BiomePropCatalog + BiomePropFeatherResolver at each slot.",
    ],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/00/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/00/concept.png",
  },
  {
    index: 1,
    id: "biome_layout_01_classic_rings",
    label: "Classic open rings — minimal maze",
    dominantBiomes: "PlayKarst, FoothillGreen, PeakStone, HorizonMist, ConceptPreset01 wedge",
    propSetSummary: "Even green rings; classic concentric prop density falloff",
    promptCore:
      "Minimal labyrinth — play disk stays open meadow. Concentric rings read as foothill→peak→horizon with smooth prop feather bands.",
    ringLayout: [
      "Cheb 0–1: PlayKarst meadow",
      "Cheb 2–5: FoothillGreen ring",
      "Cheb 6–8: PeakStone + HorizonMist feather",
      "Cheb 10+: ConceptPreset01 wilderness",
    ],
    builderNotes: ["No south labyrinth prop override.", "Wider feather at cheb 4–6."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/01/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/01/concept.png",
  },
  {
    index: 2,
    id: "biome_layout_02_florida_karst",
    label: "Florida karst — flat play + water",
    dominantBiomes: "PlayKarst, coastal water band, ConceptPreset02 wedge",
    propSetSummary: "Flat meadow + lotus/reed/alocasia near water; minimal peak props",
    promptCore:
      "Flat karst play disk with meadow grass and low beech. Water tiles get blue coastal plants — lotus, lily, sparse palms.",
    ringLayout: [
      "Cheb 0–1: PlayKarst flat meadow",
      "Cheb 2–9: MixedTransition with water-plant bias",
      "Cheb 10+: ConceptPreset02 Florida karst wedge",
    ],
    builderNotes: ["PropEmphasis: florida_karst.", "SurfaceIncludeWater enables coastal pools."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/02/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/02/concept.png",
  },
  {
    index: 3,
    id: "biome_layout_03_appalachian_peaks",
    label: "Appalachian tall peaks",
    dominantBiomes: "PeakStone dominant, FoothillGreen base, PlayKarst meadow center",
    propSetSummary: "Heavy peak spruce/pine + granite rocks; thin play meadow",
    promptCore:
      "Tall peak biome dominates mixed ring — dense blue spruce, exposed boulder scatter, sparse grass above cheb 4.",
    ringLayout: [
      "Cheb 0–1: PlayKarst thin meadow",
      "Cheb 2–4: FoothillGreen base",
      "Cheb 5–9: PeakStone dominant",
      "Cheb 10+: ConceptPreset03 wedge",
    ],
    builderNotes: ["PropEmphasis: appalachian_ridge.", "Rocks prioritized on cheb 5+."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/03/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/03/concept.png",
  },
  {
    index: 4,
    id: "biome_layout_04_coastal_fog",
    label: "Coastal fog low mountains",
    dominantBiomes: "HorizonMist, coastal water, ConceptPreset04 wedge",
    propSetSummary: "Blue mist plants, sparse trees, reed/lotus near water",
    promptCore:
      "Foggy coastal read — blue-toned trees and bushes, sparse tall silhouettes, water-edge reeds.",
    ringLayout: [
      "Cheb 0–1: PlayKarst open",
      "Cheb 2–9: HorizonMist + coastal feather",
      "Cheb 10+: ConceptPreset04 coastal fog wedge",
    ],
    builderNotes: ["Weather Foggy.", "SurfaceIncludeWater for coastal plants."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/04/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/04/concept.png",
  },
  {
    index: 5,
    id: "biome_layout_05_tomb_raider_vertical",
    label: "Tomb Raider vertical cave + maze",
    dominantBiomes: "PlayKarst, AnnexLabyrinth south, PeakStone approach",
    propSetSummary: "Labyrinth fern/mushroom; peak pines at cave mouths",
    promptCore:
      "South labyrinth annex uses fern/mushroom ground cover only. Peak stone pines frame vertical cave approaches.",
    ringLayout: [
      "Cheb 0–1: PlayKarst with trail clearings",
      "South annex: AnnexLabyrinth",
      "Cheb 5–9: PeakStone at mouth approaches",
      "Cheb 10+: ConceptPreset05 wedge",
    ],
    builderNotes: ["Exclude props near cave mouths.", "UseTombRaiderLabyrinthCadence."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/05/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/05/concept.png",
  },
  {
    index: 6,
    id: "biome_layout_06_sparse_trails",
    label: "Sparse jumps + trail network",
    dominantBiomes: "PlayKarst, sparse FoothillGreen, ConceptPreset06 wedge",
    propSetSummary: "Sparse trees — trail-corridor readable",
    promptCore: "Intentionally sparse prop density — trees clustered off-trail. Jump nodes stay bare.",
    ringLayout: [
      "Cheb 0–1: PlayKarst sparse meadow",
      "Cheb 2–9: Light FoothillGreen scatter",
      "Cheb 10+: ConceptPreset06 sparse wilderness",
    ],
    builderNotes: ["TrimToSparse on preset 06.", "Trail corridor exclusion."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/06/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/06/concept.png",
  },
  {
    index: 7,
    id: "biome_layout_07_water_labyrinth",
    label: "Water basins + foothill maze",
    dominantBiomes: "PlayKarst, water/coastal, FoothillGreen, ConceptPreset07 wedge",
    propSetSummary: "Water basin lotus/reeds; foothill maze green pine",
    promptCore:
      "Water basins with blue coastal plants. Foothill maze green pine/oak on mixed ring. Feather water↔foothill at basin edges.",
    ringLayout: [
      "Cheb 0–1: PlayKarst",
      "Water basins: coastal plant pools",
      "Cheb 2–9: FoothillGreen + water feather",
      "Cheb 10+: ConceptPreset07 wedge",
    ],
    builderNotes: ["SurfaceIncludeWater required.", "Feather water/coastal ↔ foothill."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/07/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/07/concept.png",
  },
  {
    index: 8,
    id: "biome_layout_08_perimeter_trails",
    label: "Perimeter trail ring",
    dominantBiomes: "PlayKarst center, FoothillGreen perimeter band, ConceptPreset08 wedge",
    propSetSummary: "Perimeter trail ring: bush/ground cover along ring; open play center",
    promptCore:
      "Open play center with meadow grass. Perimeter trail ring (cheb 2–3) gets bush accent — visual ring guide.",
    ringLayout: [
      "Cheb 0–1: PlayKarst open center",
      "Cheb 2–3: Perimeter trail bush band",
      "Cheb 4–9: Standard mixed ring feather",
      "Cheb 10+: ConceptPreset08 wedge",
    ],
    builderNotes: ["SurfaceIncludeTrails.", "Do not block trail readability."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/08/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/08/concept.png",
  },
  {
    index: 9,
    id: "biome_layout_09_speed_minimal",
    label: "Speed minimal — 81-tile core",
    dominantBiomes: "PlayKarst only (81-tile core)",
    propSetSummary: "Minimal props — meadow grass only on play disk",
    promptCore: "Speed build — props limited to play disk 3×3 only. Meadow grass and minimal ground cover.",
    ringLayout: [
      "Cheb 0–1 only: PlayKarst minimal meadow",
      "No props beyond play disk when UseExtendedOpenWorldGrid=false",
    ],
    builderNotes: ["UseExtendedOpenWorldGrid=false.", "Lowest hardware budget."],
    conceptImageRel: `${BIOME_LAYOUT_CONCEPTS_ROOT_REL}/09/concept.png`,
    fullWorldConceptImageRel: "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/09/concept.png",
  },
];

export function getBiomeLayoutPresetPrompt(index: number): BiomeLayoutPresetPrompt {
  return BIOME_LAYOUT_PRESET_PROMPTS[Math.max(0, Math.min(index, BIOME_LAYOUT_PRESET_PROMPTS.length - 1))];
}

export function formatBiomeLayoutPresetPromptBlock(
  presetIndex: number,
  hubRoot: string
): string {
  const p = getBiomeLayoutPresetPrompt(presetIndex);
  return [
    "## Biome layout guide",
    "",
    `Master: ${hubRoot}/${BIOME_LAYOUT_MASTER_IMAGE_REL}`,
    `Preset biome guide: ${hubRoot}/${p.conceptImageRel}`,
    `FullWorld concept: ${hubRoot}/${p.fullWorldConceptImageRel}`,
    "",
    BIOME_LAYOUT_SCALE_BLOCK,
    "",
    BIOME_LAYOUT_FEATHER_RULES,
    "",
    `### Preset ${p.index} — ${p.label}`,
    "",
    `Dominant biomes: ${p.dominantBiomes}`,
    `Prop sets: ${p.propSetSummary}`,
    "",
    p.promptCore,
    "",
    "Ring layout:",
    ...p.ringLayout.map((r) => `- ${r}`),
    "",
    "Builder notes:",
    ...p.builderNotes.map((n) => `- ${n}`),
  ].join("\n");
}
