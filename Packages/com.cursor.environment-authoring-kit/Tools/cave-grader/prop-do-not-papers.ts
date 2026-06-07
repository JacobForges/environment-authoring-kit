/**
 * Prop / cliff anti-patterns — category prop_do_not. Agents MUST read before scatter and cliff phases.
 */
import type { ResearchEntry } from "./research-catalog.js";

const C = "prop_do_not";

export const PROP_DO_NOT_PAPERS: ResearchEntry[] = [
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: props on vertical cliff faces or wall normals",
    year: 2026,
    venue: "surface_props / prop_do_not",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_SURFACE_PROPS.md",
    topics: `${C}, vertical wall, cliff face, normal.y, slope filter, floating props`,
    provenInProduction: true,
    notes:
      "NEVER Instantiate on surfaces where Physics.Raycast normal.y < 0.55 or TerrainData.GetSteepness > 42°. Vertical walls get rock splat only (peak dressing), not grass/trees.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: continuous cliff ring around entire map perimeter",
    year: 2026,
    venue: "mountain_cliffs / fullworld_do_not",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md",
    topics: `${C}, cliff ring, entire map, horizon band, appalachian rolling, fullworld_do_not`,
    provenInProduction: true,
    notes:
      "Cliffs belong on outer perimeter bands and select peak shoulders — NOT a uniform vertical wall on every tile edge. Foothills must stay walkable rolling hills into peaks.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: skip terrain tiles during prop scatter",
    year: 2026,
    venue: "SurfaceIntelligentPropPlacer",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_SURFACE_PROPS.md",
    topics: `${C}, every tile, 81 terrain, manifest, missing tile props`,
    provenInProduction: true,
    notes:
      "Iterate SurfaceTerrainGridManifest / all play + foothill + peak tiles. Empty tile = pipeline failure, not success.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: BUILD / GENERATE / MAKE research database from agent",
    year: 2026,
    venue: "ResearchCache policy",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md",
    topics: `${C}, prop_do_not, fullworld_do_not, no invent research, ResearchCache only`,
    provenInProduction: true,
    notes:
      "Agents READ ResearchCache + run npm sync-research-catalog in Tools/cave-grader. Do NOT fabricate papers, URLs, or DO NOT lists in chat or C# comments as authoritative.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: dense grass props on south labyrinth walkway benches",
    year: 2026,
    venue: "mountain_labyrinth_carve",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, labyrinth, walkway, bench, prop clearance, mountain_labyrinth`,
    provenInProduction: true,
    notes: "Keep corridor polylines clear — props outside wall band only, lower density within 8m of spine.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: props only on play disk ignoring outer rings",
    year: 2026,
    venue: "surface_vegetation_intelligent",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_SURFACE_PROPS.md",
    topics: `${C}, play disk only, foothill, peak, outer ring, incomplete scatter`,
    provenInProduction: true,
    notes: "User target: props on every walkable terrain tile in 81-tile world — not nine-tile island.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: cliff accent on play disk or labyrinth annex floor",
    year: 2026,
    venue: "SurfaceMountainTerrainPhases",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_TERRAIN.md",
    topics: `${C}, cliff accent, play disk, south annex, mountain_cliffs, fullworld_do_not`,
    provenInProduction: true,
    notes: "QueueCliffAccent must filter to outer horizon + peak outer band only — never play or annex walkway tiles.",
  },
];
