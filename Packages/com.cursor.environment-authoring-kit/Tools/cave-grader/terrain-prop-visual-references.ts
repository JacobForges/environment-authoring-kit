/**
 * Prop scatter GOOD/DO NOT reference PNGs — sync to ResearchCache with terrain-generation refs.
 */
import type { ResearchEntry } from "./research-catalog.js";
import type { VisualReference } from "./research-visual-references.js";

const KIT =
  "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_SURFACE_PROPS.md";

function img(filename: string): string {
  return `https://github.com/cursor/environment-authoring-kit/raw/main/docs/images/${filename}`;
}

type PropSpec = {
  id: string;
  title: string;
  filename: string;
  antiPattern?: boolean;
  notes: string;
  topics: string;
  phaseIds: string[];
};

const SPECS: PropSpec[] = [
  {
    id: "prop-good-01",
    title: "GOOD: props on every walkable foothill tile",
    filename: "foothill-good-01-gentle-rise.png",
    notes: "Grass/flowers on all Chebyshev-2 foothill tiles with slope gate — visible cover, not bare brown.",
    topics: "surface_props, every tile, foothill ring, walkable",
    phaseIds: ["surface_vegetation_intelligent", "mountain_foothill_sculpt"],
  },
  {
    id: "prop-good-02",
    title: "GOOD: trail corridor higher density",
    filename: "foothill-good-08-trail-spokes.png",
    notes: "Props follow trail/labyrinth polylines — not uniform random per tile.",
    topics: "surface_props, trail corridor, spline bias",
    phaseIds: ["surface_vegetation_intelligent", "mountain_trails"],
  },
  {
    id: "prop-good-03",
    title: "GOOD: peak rock scatter on slope shoulders only",
    filename: "appalachian-peak-good-06-rock-dressing.png",
    notes: "SurfaceMountainPeakDressingAuthor — rocks on slopes, grass on inner seam band.",
    topics: "surface_props, peak rock, slope paint",
    phaseIds: ["mountain_cliffs", "mountain_peak_sculpt"],
  },
  {
    id: "prop-good-04",
    title: "GOOD: play disk edge props without blocking spawn",
    filename: "play-disk-good-03-south-edge-ready.png",
    notes: "Ground cover on nine tiles — preserve flat spawn/wizard pad (inner preserve disk).",
    topics: "surface_props, play disk, preserve center",
    phaseIds: ["surface_vegetation_intelligent", "mountain_play_smooth"],
  },
  {
    id: "prop-good-05",
    title: "GOOD: horizon soft backdrop — low props not cliff wall",
    filename: "foothill-good-09-horizon-too-tall.png",
    notes: "Horizon ring at reduced height — sparse trees, no vertical cliff prop wall.",
    topics: "surface_props, horizon ring, soft backdrop",
    phaseIds: ["mountain_wilderness_tiles"],
  },
  {
    id: "prop-bad-01",
    title: "DO NOT: props floating on vertical cliff",
    filename: "appalachian-peak-bad-04-blocky-shards.png",
    antiPattern: true,
    notes: "Raycast hit normal nearly horizontal — reject spawn. See prop_do_not.",
    topics: "prop_do_not, vertical wall, floating props",
    phaseIds: ["surface_vegetation_intelligent", "mountain_cliffs"],
  },
  {
    id: "prop-bad-02",
    title: "DO NOT: cliff ring around entire map",
    filename: "appalachian-peak-bad-05-instant-cliff.png",
    antiPattern: true,
    notes: "Uniform vertical wall on all edges — must be Appalachian rolling + selective cliffs.",
    topics: "prop_do_not, cliff ring, fullworld_do_not, mountain_cliffs",
    phaseIds: ["mountain_cliffs", "mountain_wilderness_tiles"],
  },
  {
    id: "prop-bad-03",
    title: "DO NOT: props only on center play tiles",
    filename: "foothill-bad-10-single-tile-maze.png",
    antiPattern: true,
    notes: "Outer 40 tiles bare — fail. Scatter must use grid manifest for all terrains.",
    topics: "prop_do_not, play only, missing outer ring",
    phaseIds: ["surface_vegetation_intelligent"],
  },
  {
    id: "prop-bad-04",
    title: "DO NOT: grass inside labyrinth walkway bench",
    filename: "foothill-bad-02-labyrinth-trench.png",
    antiPattern: true,
    notes: "Walkway must stay readable — exclude props within corridor half-width + wall band.",
    topics: "prop_do_not, labyrinth, walkway, mountain_labyrinth_carve",
    phaseIds: ["surface_vegetation_intelligent", "mountain_labyrinth_carve"],
  },
  {
    id: "prop-bad-05",
    title: "DO NOT: agent-invented research or DO NOT lists",
    filename: "play-disk-bad-08-grid-footprints.png",
    antiPattern: true,
    notes: "Only ResearchCache + npm sync-research-catalog — never BUILD/GENERATE/MAKE database in prompts.",
    topics: "prop_do_not, fullworld_do_not, research policy",
    phaseIds: ["mountain_research_brief", "surface_vegetation_intelligent"],
  },
];

function toVisualRef(spec: PropSpec): VisualReference {
  return {
    id: spec.id,
    title: spec.title,
    year: 2026,
    category: "terrain",
    provenInProduction: true,
    docUrl: KIT,
    imageUrls: [img(spec.filename)],
    notes: spec.notes,
    region: spec.topics.split(",")[0],
  };
}

function toResearchEntry(spec: PropSpec): ResearchEntry {
  return {
    lab: "Environment Authoring Kit",
    title: spec.title,
    year: 2026,
    venue: spec.antiPattern ? "Prop scatter DO NOT" : "Prop scatter reference",
    url: KIT,
    topics: spec.topics,
    provenInProduction: true,
    imageUrls: [img(spec.filename)],
    notes: `${spec.notes} Pipeline phases: ${spec.phaseIds.join(", ")}.`,
  };
}

export const TERRAIN_PROP_VISUAL_REFERENCES: VisualReference[] = SPECS.map(toVisualRef);
export const TERRAIN_PROP_RESEARCH_ENTRIES: ResearchEntry[] = SPECS.map(toResearchEntry);
