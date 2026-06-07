/**
 * Proven visual references: Unity docs, major studios, NW Florida LiDAR (2025–2026).
 */
import { formatFloridaTerrainBlock, listLocalHillshadePaths } from "./florida-research-paths.js";
import { GAME_STUDIO_VISUAL_REFERENCES } from "./research-visual-references-game-studios.js";
import { WORLD_GEN_LADDER_REFERENCES } from "./research-world-gen-ladder.js";
import { FLORIDA_AQUIFER_VISUAL_REFERENCES } from "./research-visual-references-florida-aquifer.js";
import { FLORIDA_LIDAR_VISUAL_REFERENCES } from "./research-visual-references-florida-lidar.js";
import { MOUNTAIN_LIDAR_VISUAL_REFERENCES } from "./research-visual-references-mountain-lidar.js";
import { TERRAIN_GENERATION_VISUAL_REFERENCES } from "./terrain-generation-visual-references.js";
import { WORLD_LAYOUT_PLACEMENT_REFERENCES } from "./research-visual-references-world-layout.js";
import {
  FULLWORLD_LAYOUT_IDEAL_IMAGE_REL,
  FULLWORLD_LAYOUT_IDEAL_VISUAL,
} from "./fullworld-layout-ideal-papers.js";
import { isIdealLayoutStyle } from "./active-generation-style.js";
import { existsSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

export type VisualReferenceCategory =
  | "terrain"
  | "mesh"
  | "lighting"
  | "navmesh"
  | "pcg_shell"
  | "cave_layout"
  | "lidar_terrain"
  | "mountain_lidar"
  | "aquifer_structure"
  | "karst_geomorphology"
  | "studio_environment"
  | "performance";

export type VisualReference = {
  id: string;
  title: string;
  year: number;
  category: VisualReferenceCategory;
  provenInProduction: true;
  docUrl: string;
  imageUrls: string[];
  notes: string;
  /** Game studio or agency (studio refs). */
  studio?: string;
  /** Geographic region tag (LiDAR refs). */
  region?: string;
  /** Florida counties covered (LiDAR refs). */
  counties?: string[];
  /** Bulk download / GIS portals (not preview images). */
  dataUrls?: string[];
  /** When true, use bedrock/void structure only — no water table, bathy, or spring flow. */
  caveStructureOnly?: boolean;
  /** Hydrogeologic system tag (aquifer refs). */
  aquiferSystem?: string;
};

const UNITY_ENGINE_REFS: VisualReference[] = [
  {
    id: "unity6-terrain-heightmap",
    title: "Unity 6 Terrain — Heightmaps (carved mouth / surface walk-in)",
    year: 2026,
    category: "terrain",
    provenInProduction: true,
    studio: "Unity Technologies",
    docUrl: "https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-Heightmaps.html",
    imageUrls: [
      "https://docs.unity3d.com/6000.5/Documentation/uploads/Main/terrain-Heightmap-Tools.png",
      "https://docs.unity3d.com/6000.5/Documentation/uploads/Main/terrain-Sculpt-Tool.png",
    ],
    notes: "Surface entrance carve + alphamap paint; underground tube uses RouteTerrain meshes.",
  },
  {
    id: "unity6-terrain-paint-texture",
    title: "Unity 6 Terrain — Paint Texture (rock / dirt layers)",
    year: 2026,
    category: "terrain",
    provenInProduction: true,
    studio: "Unity Technologies",
    docUrl: "https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-PaintTexture.html",
    imageUrls: [
      "https://docs.unity3d.com/6000.5/Documentation/uploads/Main/terrain-PaintTexture-Layer.png",
    ],
    notes: "Entrance path rock paint after height carve.",
  },
  {
    id: "unity6-mesh-data-procedural",
    title: "Unity 6 MeshData — procedural floor/ceiling strips",
    year: 2026,
    category: "mesh",
    provenInProduction: true,
    studio: "Unity Technologies",
    docUrl: "https://docs.unity3d.com/6000.5/Documentation/Manual/MeshData.html",
    imageUrls: [
      "https://docs.unity3d.com/6000.5/Documentation/uploads/Main/class-Mesh-Inspector.png",
    ],
    notes: "RouteTerrainFloor / RouteTerrainCeiling mesh builders in kit.",
  },
  {
    id: "unity6-navmesh-bake",
    title: "Unity 6 NavMesh — baked walkable cave floor",
    year: 2026,
    category: "navmesh",
    provenInProduction: true,
    studio: "Unity Technologies",
    docUrl: "https://docs.unity3d.com/6000.5/Documentation/Manual/Navigation.html",
    imageUrls: [
      "https://docs.unity3d.com/6000.5/Documentation/uploads/Main/NavMeshOverview.png",
    ],
    notes: "NavMesh on RouteTerrainFloor collider after shell pass.",
  },
  {
    id: "unity6-lighting-baked",
    title: "Unity 6 Lighting — baked interior readability",
    year: 2026,
    category: "lighting",
    provenInProduction: true,
    studio: "Unity Technologies",
    docUrl: "https://docs.unity3d.com/6000.5/Documentation/Manual/LightingOverview.html",
    imageUrls: [
      "https://docs.unity3d.com/6000.5/Documentation/uploads/Main/LightingWindow.png",
    ],
    notes: "Materials + light probes for cave readability (materials rung).",
  },
];

export const CAVE_VISUAL_REFERENCES: VisualReference[] = [
  FULLWORLD_LAYOUT_IDEAL_VISUAL,
  ...UNITY_ENGINE_REFS,
  ...GAME_STUDIO_VISUAL_REFERENCES,
  ...WORLD_GEN_LADDER_REFERENCES,
  ...WORLD_LAYOUT_PLACEMENT_REFERENCES,
  ...FLORIDA_LIDAR_VISUAL_REFERENCES,
  ...FLORIDA_AQUIFER_VISUAL_REFERENCES,
  ...MOUNTAIN_LIDAR_VISUAL_REFERENCES,
  ...TERRAIN_GENERATION_VISUAL_REFERENCES,
];

const RUNG_CATEGORY_MAP: Record<string, VisualReferenceCategory[]> = {
  visual_shell: ["mesh", "pcg_shell", "cave_layout", "studio_environment"],
  ground_placement: [
    "terrain",
    "lidar_terrain",
    "mountain_lidar",
    "aquifer_structure",
    "karst_geomorphology",
    "studio_environment",
  ],
  outer_ring_mountains: ["terrain", "mountain_lidar"],
  mountain_research: ["terrain", "mountain_lidar"],
  mountain_cliffs: ["terrain", "mountain_lidar"],
  mountain_trails: ["terrain", "navmesh"],
  mountain_lidar: ["mountain_lidar", "terrain"],
  floor_collision: ["mesh", "terrain", "lidar_terrain", "aquifer_structure"],
  navmesh: ["navmesh", "mesh"],
  materials: ["lighting", "mesh", "studio_environment"],
  performance: ["performance", "mesh", "pcg_shell"],
  other: [
    "cave_layout",
    "terrain",
    "mesh",
    "studio_environment",
    "lidar_terrain",
    "aquifer_structure",
    "karst_geomorphology",
  ],
  compile_gate: [],
  research: ["terrain", "pcg_shell", "lidar_terrain", "studio_environment"],
};

export function visualRefsForRung(rung: string): VisualReference[] {
  const cats = RUNG_CATEGORY_MAP[rung] ?? ["cave_layout"];
  return CAVE_VISUAL_REFERENCES.filter((v) => cats.includes(v.category));
}

const NW_FL_COUNTIES = ["Bay", "Washington", "Jackson", "Calhoun"];

export function floridaPanhandleRefs(): VisualReference[] {
  return CAVE_VISUAL_REFERENCES.filter(
    (v) =>
      (v.category === "lidar_terrain" ||
        v.category === "aquifer_structure" ||
        v.category === "karst_geomorphology") &&
      (v.counties?.some((c) => NW_FL_COUNTIES.includes(c)) ?? false)
  );
}

export function floridaAquiferStructureRefs(): VisualReference[] {
  return CAVE_VISUAL_REFERENCES.filter(
    (v) =>
      (v.category === "aquifer_structure" || v.category === "karst_geomorphology") &&
      v.caveStructureOnly === true
  );
}

const MAX_VISUAL_IMAGE_LINES = 5;
const CONCEPT_IMAGES_ROOT_REL = "Assets/EnvironmentKit/ResearchCache/images/concepts";

function conceptFoldersForRung(rung: string): string[] {
  switch (rung) {
    case "ground_placement":
      return ["ground_placement", "terrain", "global"];
    case "outer_ring_mountains":
    case "mountain_research":
    case "mountain_cliffs":
    case "mountain_lidar":
      return ["outer_ring_mountains", "mountain_lidar", "terrain", "global"];
    case "mountain_trails":
      return ["mountain_trails", "terrain", "global"];
    case "visual_shell":
      return ["visual_shell", "global"];
    case "research":
      return ["research", "terrain", "global"];
    default:
      return ["global"];
  }
}

function listImageFilesRecursive(root: string): string[] {
  if (!existsSync(root)) return [];
  const out: string[] = [];
  const stack = [root];
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
        out.push(full);
      }
    }
  }

  out.sort((a, b) => a.localeCompare(b));
  return out;
}

function listLocalConceptPaths(hub: string, rung: string): string[] {
  const root = join(hub, CONCEPT_IMAGES_ROOT_REL);
  if (!existsSync(root)) return [];

  const rels: string[] = [];
  for (const folder of conceptFoldersForRung(rung)) {
    const dir = join(root, folder);
    if (!existsSync(dir)) continue;
    for (const full of listImageFilesRecursive(dir)) {
      rels.push(full.replace(`${hub}/`, ""));
    }
  }
  return rels;
}

export function formatVisualReferencesBlock(rung: string, hubRoot?: string): string {
  const refs = visualRefsForRung(rung);
  if (!refs.length) return "";

  const lines: string[] = [
    "## Visual references (Unity + AAA studios + FL panhandle LiDAR + aquifer structure)",
    "",
    `**Image cap:** at most ${MAX_VISUAL_IMAGE_LINES} **Image:** lines total (prefer local hillshades when hub root is set).`,
    "Read **local** `ResearchCache/images/` (build pulls missing images automatically; reuse existing PNGs). County hillshades: `images/fl-*-hillshade/hillshade.png`.",
    "Aquifer/karst entries are **cave-structure only** — use DS 926 thickness, bare-earth LiDAR, and subsidence polygons; ignore water table, TDS, bathymetry, and spring discharge.",
    "Use LiDAR **dataUrls** for full DEM/LAZ tiles (GeoTIFF), not random web image search.",
    "",
  ];

  const hub = (hubRoot ?? process.env.HUB_ROOT)?.replace(/\/$/, "");
  let imageLineCount = 0;

  if (hub) {
    for (const rel of listLocalConceptPaths(hub, rung)) {
      if (imageLineCount >= MAX_VISUAL_IMAGE_LINES) break;
      lines.push(`- **Image:** \`${hub}/${rel}\` (local concept image for \`${rung}\`)`);
      imageLineCount++;
    }

    const idealImg = join(hub, FULLWORLD_LAYOUT_IDEAL_IMAGE_REL);
    if (
      existsSync(idealImg) &&
      isIdealLayoutStyle(hub) &&
      (rung === "ground_placement" || rung === "visual_shell" || rung === "research")
    ) {
      lines.push(
        `- **Image:** \`${idealImg}\` (IDEAL FullWorld 49-tile target — labyrinths on foothills, similar per seed)`
      );
      imageLineCount++;
    }

    for (const h of listLocalHillshadePaths(hub)) {
      if (imageLineCount >= MAX_VISUAL_IMAGE_LINES) break;
      lines.push(`- **Image:** \`${hub}/${h.relativePath}\` (${h.county} hillshade)`);
      imageLineCount++;
    }
  }

  for (const r of refs) {
    if (imageLineCount >= MAX_VISUAL_IMAGE_LINES) break;
    lines.push(`### ${r.title} (${r.year})`);
    if (r.studio) lines.push(`- Studio/Agency: ${r.studio}`);
    if (r.region) lines.push(`- Region: ${r.region}`);
    if (r.counties?.length) lines.push(`- Counties: ${r.counties.join(", ")}`);
    lines.push(`- Doc: ${r.docUrl}`);
    lines.push(`- Notes: ${r.notes}`);
    for (const u of r.dataUrls ?? []) lines.push(`- Data: ${u}`);
    if (!hub) {
      for (const img of r.imageUrls) {
        if (imageLineCount >= MAX_VISUAL_IMAGE_LINES) break;
        lines.push(`- Image: ${img}`);
        imageLineCount++;
      }
    }
    lines.push("");
  }

  if (hub) {
    const terrainBlock = formatFloridaTerrainBlock(hub, rung);
    if (terrainBlock) lines.push(terrainBlock);
  } else {
    const fl = floridaPanhandleRefs();
    if (fl.length && (rung === "ground_placement" || rung === "research")) {
      lines.push("### Florida panhandle priority (Bay, Washington, Jackson, Calhoun)");
      for (const r of fl) {
        lines.push(`- ${r.id}: ${r.title} — ${r.dataUrls?.[0] ?? r.docUrl}`);
      }
      lines.push("");
    }
  }

  return lines.join("\n");
}

export { formatFloridaTerrainBlock, listLocalHillshadePaths };
