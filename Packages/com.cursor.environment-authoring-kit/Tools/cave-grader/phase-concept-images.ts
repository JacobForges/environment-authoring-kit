/**
 * Per-phase concept images + planning bullets for CaveBuildActivePhasePrompt.md.
 * Drop PNGs under Assets/EnvironmentKit/ResearchCache/images/concepts/phases/<folder>/
 */
import { existsSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { formatConceptCodeBlock } from "./phase-concept-code-examples.js";
import { formatConceptDoNotBlock } from "./phase-concept-image-do-not.js";
import { FULLWORLD_LAYOUT_IDEAL_IMAGE_REL } from "./fullworld-layout-ideal-papers.js";
import { loadActiveConceptImageRel } from "./active-generation-style.js";
import { GRID_SCALE_BLOCK } from "./fullworld-concept-grid-vocabulary.js";

/** Phases that already load mountain_labyrinth research — keep concept block short. */
const PHASE_CONCEPT_COMPACT: Record<string, { maxFolders: number; maxPlanningBullets: number; maxImages: number }> =
  {
    mountain_labyrinth_research: { maxFolders: 2, maxPlanningBullets: 2, maxImages: 1 },
    mountain_labyrinth_carve: { maxFolders: 2, maxPlanningBullets: 2, maxImages: 1 },
    mountain_foothill_sculpt: { maxFolders: 2, maxPlanningBullets: 3, maxImages: 2 },
    mountain_wilderness_tiles: { maxFolders: 2, maxPlanningBullets: 3, maxImages: 2 },
  };

export const PHASE_CONCEPTS_ROOT_REL =
  "Assets/EnvironmentKit/ResearchCache/images/concepts/phases";

export type PhaseConceptSlot = {
  /** Folder under concepts/phases/ */
  folder: string;
  /** Human label for agents */
  label: string;
  /** Art-direction bullets (planning — not C# steps) */
  planning: string[];
  /** Hub-relative fallbacks when folder has no PNG yet */
  fallbacks?: string[];
};

/** Art-direction sequence (how to think about the world). */
export const PHASE_CONCEPT_ART_ORDER: PhaseConceptSlot[] = [
  {
    folder: "00_fullworld_overview",
    label: "FullWorld overview (Ideal 49)",
    planning: [
      "Nine-tile play disk at center; outer foothill ring; peak ring; horizon.",
      "South annex: 3 foothill + 3 peak tiles behind play disk — main labyrinth stage.",
      "Similar silhouette per buildSeed — not a pixel clone of concept.png.",
    ],
    fallbacks: [FULLWORLD_LAYOUT_IDEAL_IMAGE_REL],
    // Active concept guide is injected at prompt time via loadActiveConceptImageRel().
  },
  {
    folder: "01_play_disk",
    label: "Play disk (9 tiles)",
    planning: [
      "Flat walkable bowl; gentle edge blend into approach trails.",
      "Primary cave mouth on disk edge; clear sightlines toward south labyrinth entrance.",
      "No props blocking spawn; no crater bowl in center cells.",
    ],
    fallbacks: [
      "Assets/EnvironmentKit/ResearchCache/images/play-disk-good-01/ref-0.png",
    ],
  },
  {
    folder: "02_foothill_ring_sculpt",
    label: "Foothill ring (rolling hills)",
    planning: [
      "Appalachian rolling green foothills rising from play disk — not a flat beige shelf.",
      "South foothill annex (z=-2) must stay sculptable for labyrinth benches next.",
      "Denoise for rolling relief; do not flatten south annex before labyrinth carve.",
    ],
    fallbacks: [
      "Assets/EnvironmentKit/ResearchCache/images/foothill-good-01/ref-0.png",
    ],
  },
  {
    folder: "03_south_foothill_labyrinth_row",
    label: "South foothill row — labyrinth benches (target)",
    planning: [
      "Second row behind peaks (toward play): wide carved walkway benches on rolling hills.",
      "NOT flat tan slabs; NOT full 16-tile ring maze — south 3×2 annex only.",
      "Use target-foothill-row.png (wrong vs right) as the only layout comparison.",
    ],
    fallbacks: [
      "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/03_south_foothill_labyrinth_row/target-foothill-row.png",
    ],
  },
  {
    folder: "04_peak_ring",
    label: "Peak ring + south peak annex",
    planning: [
      "Tall peaks on peak tiles; south peak row (z=-3) behind foothill labyrinth row.",
      "Summit caps come after labyrinth carve — flat plateaus for hub/spokes later.",
      "Wilderness cave mouths on south peak tiles only (lateral bore, not play-facing pit).",
    ],
    fallbacks: [
      "Assets/EnvironmentKit/ResearchCache/images/appalachian-peak-good-01/ref-0.png",
    ],
  },
  {
    folder: "05_wilderness_mountains",
    label: "Wilderness mountain mass (outer ring)",
    planning: [
      "81-tile grid: stitched neighbors, varied height, horizon backdrop.",
      "Outer cliff band on perimeter only — interiors stay walkable.",
    ],
  },
  {
    folder: "06_mountain_labyrinth_carve",
    label: "Labyrinth carve (spine batch)",
    planning: [
      "ONE batch: 3–5 spine polylines, ~12m half-width benches, raised grass walls on south foothill row.",
      "No per-edge 520-segment queue; no post-carve denoise on south foothill annex (flattens benches).",
      "Read mountain_labyrinth STYLE examples for bench feel — adapt, do not clone.",
    ],
  },
  {
    folder: "07_mountain_trails",
    label: "Perimeter trails",
    planning: [
      "Walkable trail benches on foothill/peak ring — after labyrinth + summit hubs.",
      "Trails connect play disk → labyrinth entrance → wilderness mouth approaches.",
      "Max grade readable in third-person; NavMesh must follow benches.",
    ],
    fallbacks: [
      "Assets/EnvironmentKit/ResearchCache/images/environment-authoring-kit-good-trail-corridor-higher-density/ref-0.png",
    ],
  },
  {
    folder: "08_wilderness_cave_mouth",
    label: "Wilderness surface cave mouth",
    planning: [
      "One shallow mouth on south peak annex tile — approach bench, marker, denoise mouth tile only.",
      "Not a deep bowl; not on play disk; not forward-facing into play disk center.",
    ],
  },
  {
    folder: "09_cave_primary_mouth",
    label: "Primary cave mouth (play disk)",
    planning: [
      "Surface walk-in on play disk; underground route snapped to mouth anchor.",
      "Mouth seal underground only in cave phases — do not re-sculpt FullWorld terrain here.",
    ],
  },
  {
    folder: "10_cave_underground",
    label: "Underground cave (route + shell)",
    planning: [
      "Buried tube except mouth; RouteTerrain floor/ceiling; platforms and navmesh on solution path.",
      "Visual shell readable; no onion stacked ceilings; grade per cave ladder rung.",
    ],
  },
];

/** Unity / kit execution order index (for docs). */
export const PHASE_CONCEPT_BUILD_ORDER = [
  "research",
  "dem_georeference",
  "surface_terrain_extend",
  "surface_lidar_stamp",
  "mountain_research_brief",
  "mountain_wilderness_tiles",
  "mountain_foothill_sculpt",
  "mountain_peak_sculpt",
  "mountain_wilderness_cave_mouth",
  "mountain_labyrinth_research",
  "mountain_labyrinth_carve",
  "mountain_peak_summit_caps",
  "mountain_cliffs",
  "mountain_trails",
  "mountain_play_smooth",
  "visual_shell",
  "ground_placement",
  "cave_mouth_seal",
  "layout_platforms",
  "packaging_ship",
] as const;

/** Maps pipeline phase id → concept slot folder(s). First match wins for primary image. */
export const PHASE_ID_TO_CONCEPT_FOLDERS: Record<string, string[]> = {
  research: ["00_fullworld_overview", "01_play_disk"],
  dem_georeference: ["00_fullworld_overview", "01_play_disk"],
  surface_terrain_extend: ["01_play_disk"],
  surface_lidar_stamp: ["01_play_disk", "02_foothill_ring_sculpt"],
  surface_roads_water_lidar: ["07_mountain_trails"],
  terrain_phase_trails: ["07_mountain_trails"],
  surface_route_bot: ["07_mountain_trails", "08_wilderness_cave_mouth"],
  surface_navmesh: ["01_play_disk", "07_mountain_trails"],
  surface_vegetation_intelligent: ["01_play_disk", "05_wilderness_mountains"],
  mountain_research_brief: ["00_fullworld_overview", "05_wilderness_mountains"],
  mountain_wilderness_tiles: ["05_wilderness_mountains", "02_foothill_ring_sculpt"],
  mountain_foothill_sculpt: ["02_foothill_ring_sculpt", "03_south_foothill_labyrinth_row"],
  mountain_peak_sculpt: ["04_peak_ring"],
  mountain_wilderness_cave_mouth: ["08_wilderness_cave_mouth", "04_peak_ring"],
  mountain_labyrinth_research: [
    "03_south_foothill_labyrinth_row",
    "06_mountain_labyrinth_carve",
  ],
  mountain_labyrinth_carve: [
    "03_south_foothill_labyrinth_row",
    "06_mountain_labyrinth_carve",
  ],
  mountain_peak_summit_caps: ["04_peak_ring"],
  mountain_cliffs: ["05_wilderness_mountains"],
  mountain_trails: ["07_mountain_trails"],
  mountain_play_smooth: ["01_play_disk", "07_mountain_trails"],
  visual_shell: ["10_cave_underground"],
  ground_placement: ["09_cave_primary_mouth"],
  cave_mouth_seal: ["09_cave_primary_mouth"],
  layout_platforms: ["10_cave_underground"],
  moving_platforms: ["10_cave_underground"],
  packaging_ship: ["00_fullworld_overview"],
};

function slotByFolder(folder: string): PhaseConceptSlot | undefined {
  return PHASE_CONCEPT_ART_ORDER.find((s) => s.folder === folder);
}

function listPngsInDir(absDir: string, hub: string): string[] {
  if (!existsSync(absDir)) return [];
  const out: string[] = [];
  const stack = [absDir];
  while (stack.length > 0) {
    const dir = stack.pop()!;
    for (const name of readdirSync(dir)) {
      const full = join(dir, name);
      let isDir = false;
      try {
        isDir = statSync(full).isDirectory();
      } catch {
        continue;
      }
      if (isDir) {
        stack.push(full);
        continue;
      }
      const lower = name.toLowerCase();
      if (
        lower.endsWith(".png") ||
        lower.endsWith(".jpg") ||
        lower.endsWith(".jpeg") ||
        lower.endsWith(".webp")
      ) {
        out.push(full.replace(`${hub}/`, ""));
      }
    }
  }
  out.sort((a, b) => a.localeCompare(b));
  return out;
}

function resolveImagesForFolder(hub: string, folder: string, maxImages = 3): string[] {
  const slot = slotByFolder(folder);
  const dir = join(hub, PHASE_CONCEPTS_ROOT_REL, folder);
  const local = listPngsInDir(dir, hub);
  if (local.length) return local.slice(0, maxImages);
  const fallbacks: string[] = [];
  for (const rel of slot?.fallbacks ?? []) {
    if (existsSync(join(hub, rel))) fallbacks.push(rel);
  }
  return fallbacks.slice(0, maxImages);
}

export function formatPhaseConceptPlanningBlock(phaseId: string, hubRoot: string): string {
  const hub = hubRoot.replace(/\/$/, "");
  const folders = PHASE_ID_TO_CONCEPT_FOLDERS[phaseId];
  if (!folders?.length) return "";

  const activeConceptRel = loadActiveConceptImageRel(hub);

  const compact = PHASE_CONCEPT_COMPACT[phaseId];
  const folderList = compact ? folders.slice(0, compact.maxFolders) : folders;
  const maxPlanning = compact?.maxPlanningBullets ?? 99;
  const maxImages = compact?.maxImages ?? 3;

  const lines: string[] = [
    "## Phase concept images (planning — open before editing)",
    "",
    `**Active FullWorld concept guide:** \`${hub}/${activeConceptRel}\` (similar per seed — not a pixel clone).`,
    "",
    GRID_SCALE_BLOCK,
    "",
  ];
  if (!compact) {
    lines.push(
      "Art-direction order: overview → play disk → foothills → **south labyrinth row** → peaks → wilderness → labyrinth carve → trails → mouths → cave.",
      "Build order differs: peaks sculpt before labyrinth carve; **trails run after** labyrinth + summit caps.",
      ""
    );
  } else {
    lines.push(
      "_Compact block — full art order in `docs/PHASE_CONCEPT_IMAGES.md`. Open images + code anchors below._",
      ""
    );
  }

  const doNotBlock = formatConceptDoNotBlock(folderList, hub);
  if (doNotBlock) lines.push(doNotBlock);

  const seen = new Set<string>();
  for (const folder of folderList) {
    const slot = slotByFolder(folder);
    if (!slot || seen.has(folder)) continue;
    seen.add(folder);

    lines.push(`### ${slot.label}`, "");
    lines.push(`**Drop folder:** \`${hub}/${PHASE_CONCEPTS_ROOT_REL}/${folder}/\``, "");
    for (const p of slot.planning.slice(0, maxPlanning)) lines.push(`- ${p}`);
    lines.push("");
    lines.push(formatConceptCodeBlock(folder, hub));

    const images = resolveImagesForFolder(hub, folder, maxImages);
    if (images.length) {
      lines.push("**Open these images:**");
      for (const rel of images) lines.push(`- \`${hub}/${rel}\``);
      lines.push("");
    } else {
      lines.push(
        `_No PNG in folder yet — add \`target.png\` or use fallbacks listed in docs/PHASE_CONCEPT_IMAGES.md._`,
        ""
      );
    }
  }

  return lines.join("\n");
}

export function formatPhaseConceptIndexMarkdown(hubRoot: string): string {
  const hub = hubRoot.replace(/\/$/, "");
  const lines: string[] = [
    "# Phase concept image index",
    "",
    `Root: \`${hub}/${PHASE_CONCEPTS_ROOT_REL}/\``,
    "",
    "## Art direction order (how to plan)",
    "",
  ];

  for (const slot of PHASE_CONCEPT_ART_ORDER) {
    const images = resolveImagesForFolder(hub, slot.folder);
    lines.push(`### ${slot.label} (\`${slot.folder}/\`)`);
    for (const p of slot.planning) lines.push(`- ${p}`);
    lines.push("");
    lines.push(formatConceptCodeBlock(slot.folder, hub));
    if (images.length) {
      lines.push("", "**On disk:**");
      for (const rel of images) lines.push(`- \`${hub}/${rel}\``);
    } else {
      lines.push("", "_No image yet._");
    }
    lines.push("");
  }

  lines.push("## Unity build order (mountain excerpt)", "");
  for (const id of PHASE_CONCEPT_BUILD_ORDER) {
    if (!id.startsWith("mountain_") && id !== "research") continue;
    const folders = PHASE_ID_TO_CONCEPT_FOLDERS[id];
    lines.push(`- \`${id}\` → ${folders?.join(", ") ?? "—"}`);
  }
  lines.push("");

  return lines.join("\n");
}
