/**
 * Inspirational mountain labyrinth styles — agents may adapt creatively; not pixel-perfect clones.
 * Images live under Assets/EnvironmentKit/ResearchCache/images/{slugId}/ref-0.png
 */
import type { ResearchEntry } from "./research-catalog.js";

const T = "mountain_labyrinth";
const KIT = "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md";

export const MOUNTAIN_LABYRINTH_DEFINITION: ResearchEntry = {
  lab: "Environment Authoring Kit",
  title: "WHAT IS a mountain labyrinth — kit definition",
  year: 2026,
  venue: "RESEARCH_MOUNTAIN_LABYRINTH",
  url: KIT,
  topics: `${T}, definition, walkway, maze annex, solution path, play disk gate, terrain bench`,
  provenInProduction: true,
  notes:
      "A mountain labyrinth is a WALKABLE outdoor maze annex on foothill/peak terrain SOUTH of the play disk: (1) one clear entrance from the nine-tile play disk south edge, (2) wide trail benches (~8–12 m half-width), (3) guaranteed solution path, (4) optional dead-ends, (5) spans a 3×2 south annex (6 terrains). Carve spine polylines only.",
};

export const MOUNTAIN_LABYRINTH_STYLE_EXAMPLES: ResearchEntry[] = [
  {
    lab: "Environment Authoring Kit",
    title: "STYLE Example 1 — Chartres terrace circular walk",
    year: 2026,
    venue: "Inspirational reference (adapt freely)",
    url: KIT,
    topics: `${T}, style example, circular path, terrace bench, cathedral labyrinth, vista center`,
    provenInProduction: true,
    notes:
      "Inspirational only: concentric walk benches on a flat mountain terrace with a clear center hub. You may use partial arcs, not a full circle.",
    imageUrls: [
      "https://github.com/cursor/environment-authoring-kit/raw/main/docs/images/labyrinth-style-01-chartres-terrace.png",
    ],
  },
  {
    lab: "Environment Authoring Kit",
    title: "STYLE Example 2 — Tomb Raider cliff-bench annex",
    year: 2026,
    venue: "Inspirational reference (adapt freely)",
    url: KIT,
    topics: `${T}, style example, cliff ring, wide bench, third-person walk, approach maze`,
    provenInProduction: true,
    notes:
      "Inspirational only: wide carved benches hugging cliff walls with generous turning radius — player always sees sky and wall affordance.",
    imageUrls: [
      "https://github.com/cursor/environment-authoring-kit/raw/main/docs/images/labyrinth-style-02-cliff-bench.png",
    ],
  },
  {
    lab: "Environment Authoring Kit",
    title: "STYLE Example 3 — Contour switchback hub maze",
    year: 2026,
    venue: "Inspirational reference (adapt freely)",
    url: KIT,
    topics: `${T}, style example, switchback, contour following, hub junction, foothill slope`,
    provenInProduction: true,
    notes:
      "Inspirational only: paths follow elevation contours with switchbacks; junction hubs at each turn — good for foothill ring.",
    imageUrls: [
      "https://github.com/cursor/environment-authoring-kit/raw/main/docs/images/labyrinth-style-03-switchback-hub.png",
    ],
  },
  {
    lab: "Environment Authoring Kit",
    title: "STYLE Example 4 — Hub-and-spoke vista junction",
    year: 2026,
    venue: "Inspirational reference (adapt freely)",
    url: KIT,
    topics: `${T}, style example, hub spoke, vista node, dead end spur, landmark`,
    provenInProduction: true,
    notes:
      "Inspirational only: central plaza with 4–6 wide spokes; some spokes end at vista knolls (dead-ends OK if readable).",
    imageUrls: [
      "https://github.com/cursor/environment-authoring-kit/raw/main/docs/images/labyrinth-style-04-hub-spoke.png",
    ],
  },
  {
    lab: "Environment Authoring Kit",
    title: "STYLE Example 5 — Hidden-door spur with terrain hint",
    year: 2026,
    venue: "Inspirational reference (adapt freely)",
    url: KIT,
    topics: `${T}, style example, hidden door, secret spur, bench hint, marker prop`,
    provenInProduction: true,
    notes:
      "Inspirational only: main spine plus optional narrow spur behind a rock bench / debris hint — secret must still be walkable when found.",
    imageUrls: [
      "https://github.com/cursor/environment-authoring-kit/raw/main/docs/images/labyrinth-style-05-hidden-spur.png",
    ],
  },
];
