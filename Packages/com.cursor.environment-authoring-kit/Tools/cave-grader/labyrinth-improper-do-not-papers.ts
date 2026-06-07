/**
 * Improper / anti-pattern labyrinths — agents read ONLY during mountain_labyrinth_* phases (via category labyrinth_do_not).
 * Also summarized under fullworld_do_not policy bullets where noted.
 */
import type { ResearchEntry } from "./research-catalog.js";

const C = "labyrinth_do_not";

export const LABYRINTH_IMPROPER_DO_NOT_PAPERS: ResearchEntry[] = [
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: maze with no exit (unwinnable trap)",
    year: 2026,
    venue: "Level design anti-pattern",
    url: "https://en.wikipedia.org/wiki/Labyrinth",
    topics: `${C}, no exit, unwinnable, dead end only, player trap, fullworld_do_not`,
    provenInProduction: true,
    notes:
      "Every surface labyrinth MUST have a solution path from play entrance to maze center and back. Never ship a perfect maze with zero egress.",
    imageUrls: [
      "https://upload.wikimedia.org/wikipedia/commons/thumb/2/2f/Cretan_labyrinth.svg/512px-Cretan_labyrinth.svg.png",
    ],
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: disconnected walkway (entrance island)",
    year: 2026,
    venue: "SurfaceMountainLabyrinthLayout",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, disconnected entrance, orphan corridor, no stitch to play disk`,
    provenInProduction: true,
    notes: "Walkway from nine-tile play disk must be carved and seamed before maze annex — no floating corridor segments.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: grid smaller than play disk (toy maze on wrong scale)",
    year: 2026,
    venue: "SurfaceMountainLabyrinthLayout",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, tiny maze, 17x15 grid, insufficient annex, Tomb Raider scale`,
    provenInProduction: true,
    notes: "Use dramatic grid (31+ cells wide) and 9–12 m cells — foothill band must feel like an explorable annex, not a UI minigame.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: hidden doors without readable affordance",
    year: 2026,
    venue: "Adventure design",
    url: "https://tombraider.fandom.com/wiki/Level_Design",
    topics: `${C}, unfair hidden door, no visual hint, invisible wall`,
    provenInProduction: true,
    notes:
      "Secret passages need terrain bench + marker + prop hint — see mountain_labyrinth proper refs for hidden-door cadence.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: carve labyrinth before foothill ring exists",
    year: 2026,
    venue: "SurfaceMountainResearchPipeline",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, pipeline order, missing foothill, carve on play disk only`,
    provenInProduction: true,
    notes: "Order: 49 tiles placed → ground snap → seams → foothill+peak sculpt → labyrinth research → labyrinth carve → cliffs (edges only).",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: per-edge maze heightmap grid (520+ segment queue)",
    year: 2026,
    venue: "SurfaceMountainLabyrinthLayout.BuildCorridorPolylines",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, BuildCorridorPolylines, edge grid, 520 segments, editor queue, build time, fullworld_do_not`,
    provenInProduction: true,
    notes:
      "Never queue one editor step per maze cell wall. That produced ~520 PeakMassif corridor steps, 90+ minute builds, and a dense pixelated rectangular heightmap stamp. Use BuildCarvePolylines (3–5 spine paths) in ONE ScheduleHeavy batch + one seam pass.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: dense pixelated rectangular labyrinth block (screenshot anti-pattern)",
    year: 2026,
    venue: "FullWorld build audit 2026-05",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, pixel grid, heightmap noise, rectangular stamp, dark block, maze walls as pixels`,
    provenInProduction: true,
    notes:
      "The dark noisy rectangular block on foothill terrain (left of play disk) is a failed carve: every maze edge carved as a short polyline on the heightmap. Walkways must read as wide trail benches (~12m half-width on peak, ~8.5m on foothill), not a checkerboard of wall pixels.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: flat south annex + noisy rectangular labyrinth stamp (user screenshot)",
    year: 2026,
    venue: "FullWorld build audit (local capture)",
    url: "file:Assets/EnvironmentKit/ResearchCache/images/labyrinth-do-not/user-screenshot.png",
    topics: `${C}, flat peaks, no mountain relief, rectangular stamp, noisy block, build audit, screenshot, fullworld_do_not`,
    provenInProduction: true,
    notes:
      "If the south annex reads as a flat plate and the labyrinth carve shows as a dark/noisy rectangular stamp, stop the run. This indicates corridor shaping is too wide/too wall-like, and/or mountain relief is not being expressed on peak tiles. Fix by: narrower peak corridor widening, lower wall raise, and stronger peak corridor mold/shave.",
    imageUrls: [
      "file:Assets/EnvironmentKit/ResearchCache/images/labyrinth-do-not/user-screenshot.png",
    ],
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: double labyrinth pass (peak_sculpt + foothill carve)",
    year: 2026,
    venue: "SurfaceMountainResearchPipeline",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, duplicate carve, peak_sculpt labyrinth, foothill labyrinth, pipeline order`,
    provenInProduction: true,
    notes:
      "Labyrinth runs exactly once in mountain_labyrinth_carve. Peak sculpt phase is denoise + seam lock only. A second pass doubles queue time and amplifies grid artifacts.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: morphological WidenPassages grid dilation on heightmap",
    year: 2026,
    venue: "SurfaceMountainLabyrinthLayout",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, WidenPassages, grid dilation, inflated maze, noise block`,
    provenInProduction: true,
    notes:
      "Do not inflate the logical maze grid before carving — it widens wall density and contributes to pixelated stamps. Widen corridors via half-width on spine polylines, not cell dilation.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: BuildAnnexPassageRunPolylines star/checkerboard grid on south annex",
    year: 2026,
    venue: "SurfaceMountainLabyrinthLayout.BuildAnnexPassageRunPolylines",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, BuildAnnexPassageRunPolylines, star grid, checkerboard, passage raster, fullworld_do_not`,
    provenInProduction: true,
    notes:
      "Rasterizing every passage cell as horizontal+vertical runs + raised wall sculpt reads as a lame star/block maze — NOT movie Labyrinth quality. Carve ONLY BuildCarvePolylines spines (connector + west/east columns + mouth branches + solution path) across the full south 3×2 annex (6 terrains).",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: labyrinth on fewer than six south annex terrains",
    year: 2026,
    venue: "SurfaceMountainSouthAnnex",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, south annex, six terrains, 3x2 rectangle, foothill peak rows`,
    provenInProduction: true,
    notes:
      "Labyrinth must fill the south 3×2 rectangle (foothill y=-2 row + peak y=-3 row). Do not carve only 3 tiles or bleed maze onto play disk — CollectCarveTerrains is annex-only.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: jagged heightmap shard lines in excavation (thin dark splinters)",
    year: 2026,
    venue: "FullWorld build audit 2026-05 crater phase",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, jagged shards, thin splinters, non-walkable lines, heightmap artifact, excavation pit`,
    provenInProduction: true,
    notes:
      "Failed carve: thin dark jagged geometric splinters on flat excavation floor — not walkable benches. Labyrinth paths must be WIDE continuous trail benches (~8–12 m half-width), not hairline heightmap scratches.",
    imageUrls: [
      "https://github.com/cursor/environment-authoring-kit/raw/main/docs/images/labyrinth-do-not-jagged-shards.png",
    ],
  },
];
