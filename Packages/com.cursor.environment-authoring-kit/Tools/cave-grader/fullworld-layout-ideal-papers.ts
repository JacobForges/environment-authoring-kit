/**
 * Ideal FullWorld 49-tile layout — concept reference for agents (category: fullworld_layout_ideal).
 */
import type { ResearchEntry } from "./research-catalog.js";
import type { VisualReference } from "./research-visual-references.js";

const C = "fullworld_layout_ideal";

export const FULLWORLD_LAYOUT_IDEAL_IMAGE_REL =
  "Assets/EnvironmentKit/ResearchCache/images/fullworld-layout-ideal-49/concept.png";

export const FULLWORLD_LAYOUT_IDEAL_DOC_REL =
  "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_FULLWORLD_LAYOUT_IDEAL.md";

export const FULLWORLD_LAYOUT_IDEAL_PAPERS: ResearchEntry[] = [
  {
    lab: "Environment Authoring Kit",
    title: "TARGET: Ideal FullWorld 81-tile layout (concept reference image)",
    year: 2026,
    venue: "Hub generation style",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_LAYOUT_IDEAL.md",
    topics: `${C}, 49 tiles, nine-tile play disk, foothill ring, peak ring, mountain labyrinth, cave mouth, buried cave`,
    provenInProduction: true,
    notes:
      "Open ResearchCache/images/fullworld-layout-ideal-49/concept.png — similar silhouette each run, not identical. Labyrinth on play-disk south 2 rows; trails to three south peak mouths (W,C,E).",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO: mountain labyrinth after wilderness mouth, before summit caps",
    year: 2026,
    venue: "SurfaceMountainResearchPipeline",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_LAYOUT_IDEAL.md#mountain-pipeline-order-do-not-reorder",
    topics: `${C}, mountain_labyrinth_carve, mountain_wilderness_cave_mouth, mountain_peak_summit_caps`,
    provenInProduction: true,
    notes:
      "Massif mouth on one random foothill/peak; labyrinth carve + stitch second; summit caps on flat peak plateaus after maze.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO: snap underground walkway start to surface cave mouth",
    year: 2026,
    venue: "CaveRouteMouthSnapUtility",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_LAYOUT_IDEAL.md",
    topics: `${C}, CaveOpening_Primary, mouth anchor, buried cave, SurfaceWalkIn`,
    provenInProduction: true,
    notes:
      "Route geometry XZ under mouth; roof audit strips above-ground shell except entry exempt radius.",
  },
];

export const FULLWORLD_LAYOUT_IDEAL_VISUAL: VisualReference = {
  id: "fullworld-layout-ideal-49-concept",
  title: "Ideal FullWorld 81-tile — play-disk labyrinth (2 rows), foothill trails, 3 peak mouths",
  year: 2026,
  category: "studio_environment",
  provenInProduction: true,
  studio: "Environment Authoring Kit",
  docUrl:
    "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_LAYOUT_IDEAL.md",
  imageUrls: [],
  notes:
    `${C}, TARGET layout — 81 terrains, maze benches on south 2 play rows, trails to Cave W/C/E, primary mouth on north play row. Similar per seed, not a clone.`,
};

export const FULLWORLD_LAYOUT_IDEAL_PROMPT_BULLETS: string[] = [
  "Read fullworld_layout_ideal + concept.png — 9 play + foothill/peak rings; Full AAA adds open world to ~289 tiles (17×17).",
  "Similar layout each run — honor buildSeed for labyrinth grid, maze flavor, prop scatter, and Hollow Titan tile pick.",
  "Labyrinth carve on south TWO rows of the 3×3 play disk only — foothill maze trails, not radial star cave openings.",
  "mountain_trails: bench paths from play maze south exits to Cave W, Cave C, Cave E on south peak row.",
  "Primary cave mouth on north play row; three south peak mouths only (W/C/E) — no star pattern on play disk.",
  "One massive Hollow Titan dead tree — random surface terrain tile each seed (always above ground).",
  "LiDAR guides carve/rise sculpt on all tiles — never authoritative heightmap stamp replacement.",
  "Underground cave buried except mouth — walkway start snapped to surface opening marker.",
];
