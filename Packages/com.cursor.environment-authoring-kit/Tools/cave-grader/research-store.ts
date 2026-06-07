/**
 * Local research library: categorized, serialized URL metadata, optional cached images.
 * Agents read disk first — avoids repeat web/API fetches.
 */
import { createHash } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
  readdirSync,
} from "node:fs";
import { dirname, join } from "node:path";
import type { PromptRung } from "./prompt-ladder.js";
import { researchAliasesForRung } from "./research-prompt-budget.js";
import {
  RESEARCH_MIN_YEAR,
  isProvenInProduction,
  papersForMinYear,
  UNITY6_ENGINE_REFS,
  type ResearchEntry,
} from "./research-catalog.js";
import { buildFloridaTerrainSummary } from "./florida-research-paths.js";
import { CAVE_VISUAL_REFERENCES, type VisualReference } from "./research-visual-references.js";
import {
  RESEARCH_CACHE_GENERATED_REL,
  RESEARCH_CACHE_INDEX_REL,
  RESEARCH_CACHE_REL,
} from "./research-cache-paths.js";

export const RESEARCH_CACHE_VERSION = 1;
export { RESEARCH_CACHE_GENERATED_REL, RESEARCH_CACHE_INDEX_REL, RESEARCH_CACHE_REL };

export const RESEARCH_CATEGORIES = [
  "terrain",
  "mesh_shell",
  "adventure",
  "navmesh",
  "materials_lighting",
  "floor_collision",
  "ground_placement",
  "performance",
  "qa_testing",
  "engine_docs",
  "visual_reference",
  "lab_index",
  "pcg_research",
  "3d_gaussian_splat",
  "3d_gaussian_splat_unity",
  "pipeline_editor_responsiveness",
  "mountain_terrain",
  "mountain_lidar",
  "mountain_labyrinth",
  "labyrinth_do_not",
  "terrain_tiling",
  "fullworld_do_not",
  "fullworld_layout_ideal",
  "fullworld_generation_style",
  "surface_props",
  "prop_do_not",
  "unity_editor_ai_guidance",
  "hollow_titan",
  "hollow_titan_do_not",
] as const;

export type ResearchCategory = (typeof RESEARCH_CATEGORIES)[number];

export type SourceType = "paper" | "engine_doc" | "visual_ref" | "lab_index";

export type SerializedResearch = {
  fetchedUtc: string;
  fetchSkipped: boolean;
  summary: string;
  keyPoints: string[];
  implementationNotes?: string;
};

export type LocalImageRef = {
  sourceUrl: string;
  relativePath: string;
  bytes?: number;
  cachedUtc?: string;
};

export type CacheEntry = {
  id: string;
  category: ResearchCategory;
  sourceType: SourceType;
  rungs: PromptRung[];
  title: string;
  lab?: string;
  year: number;
  venue?: string;
  url: string;
  pdfUrl?: string;
  topics: string;
  provenInProduction: boolean;
  imageUrls?: string[];
  localImages?: LocalImageRef[];
  serialized: SerializedResearch;
  /** Relative to hub (ResearchCache/...) */
  metaPath: string;
  contentPath: string;
  imagesManifestPath?: string;
};

export type CategoryIndex = {
  id: ResearchCategory;
  label: string;
  entryIds: string[];
};

export type FloridaTerrainIndexSummary = {
  policy: string;
  counties: string[];
  countyIds: string[];
  aquiferEntryIds: string[];
  panhandleEntryIds: string[];
  hillshadeIndexPath: string;
  hillshades: { county: string; countyId: string; relativePath: string; source?: string }[];
  bulkDemUrl: string;
  syncCommands: string[];
};

export type ResearchCacheIndex = {
  version: number;
  generatedUtc: string;
  policy: string;
  hubRelativeRoot: string;
  categories: Record<ResearchCategory, CategoryIndex>;
  entries: Record<string, CacheEntry>;
  floridaTerrain?: FloridaTerrainIndexSummary;
  stats: {
    totalEntries: number;
    provenEntries: number;
    withLocalImages: number;
    withFetchedBytes: number;
    floridaAquiferEntries?: number;
    floridaHillshadeCount?: number;
  };
};

const CATEGORY_LABELS: Record<ResearchCategory, string> = {
  terrain: "Terrain carve & surface mouth",
  mesh_shell: "Procedural mesh & cave shell",
  adventure: "Adventure pacing, moving set-pieces",
  navmesh: "NavMesh & walkability",
  materials_lighting: "Materials, lighting, atmosphere",
  floor_collision: "Floor colliders & fall-through",
  ground_placement: "Root depth & ground alignment",
  performance: "Triangles, culling, GPU budget",
  qa_testing: "AAA QA, playtest automation",
  engine_docs: "Unity 6 shipped manuals",
  visual_reference: "Reference images (cave layout)",
  lab_index: "Lab publication indices",
  pcg_research: "Production PCG (proven only)",
  "3d_gaussian_splat": "3D Gaussian Splat — fundamentals, labs, Mac",
  "3d_gaussian_splat_unity": "3D Gaussian Splat — Unity Editor integration",
  pipeline_editor_responsiveness: "Pipeline pacing — editor stalls & live status",
  mountain_terrain: "Mountain massifs — cliffs, trails, perimeter ring",
  mountain_lidar: "Mountain LiDAR / DEM — outer ring & open-world tiles",
  mountain_labyrinth:
    "Mountain labyrinth — walkway, maze annex, stitch to nine-tile play disk",
  labyrinth_do_not:
    "Labyrinth DO NOT — no exit, toy scale, unfair secrets, wrong pipeline order (labyrinth phases only)",
  terrain_tiling: "Terrain tiling — grid index, edge seed, SetNeighbors, heightmap stitch",
  fullworld_do_not:
    "FullWorld DO NOT — no Grid/EnvironmentRooms layout; terrain-first 49 tiles + mountains",
  fullworld_layout_ideal:
    "FullWorld IDEAL layout — 49-tile target concept (play disk, labyrinth foothills, mouths); similar per seed",
  fullworld_generation_style:
    "Hub FullWorld preset (1–20) — wired request flags + agent guidance per style id",
  surface_props: "Surface prop scatter — per-tile vegetation, rocks, trail masks",
  prop_do_not: "Prop DO NOT — cliff faces, world scatter mistakes, research fabrication",
  unity_editor_ai_guidance: "Unity Editor + AI prompt preview — non-blocking pipeline UX",
  hollow_titan:
    "Hollow Titan landmark — vista POI, LP exterior, interior dungeon, spawn scope",
  hollow_titan_do_not:
    "Hollow Titan DO NOT — CC0 primary trunk, world scatter, cave mouth, FullWorld rebuild",
};

const RUNG_TO_CATEGORIES: Partial<Record<PromptRung, ResearchCategory[]>> = {
  visual_shell: ["mesh_shell", "visual_reference", "pcg_research", "ground_placement", "adventure"],
  ground_placement: [
    "hollow_titan",
    "hollow_titan_do_not",
    "fullworld_do_not",
    "fullworld_layout_ideal",
    "terrain",
    "ground_placement",
    "mountain_terrain",
    "mountain_lidar",
  ],
  floor_collision: ["floor_collision", "mesh_shell", "qa_testing"],
  navmesh: ["navmesh", "engine_docs", "adventure"],
  materials: ["materials_lighting", "engine_docs", "adventure"],
  performance: ["performance", "mesh_shell", "pipeline_editor_responsiveness"],
  prop_trees: ["hollow_titan", "hollow_titan_do_not", "surface_props", "prop_do_not", "visual_reference"],
  prop_grass: ["surface_props", "prop_do_not", "visual_reference"],
  prop_bushes: ["surface_props", "prop_do_not", "visual_reference"],
  prop_ground_cover: ["surface_props", "prop_do_not", "visual_reference"],
  compile_gate: ["engine_docs", "adventure"],
  research: ["pcg_research", "lab_index", "qa_testing", "adventure", "mountain_terrain"],
  other: ["qa_testing", "pcg_research", "adventure"],
};

const MOUNTAIN_RUNG_CATEGORIES: ResearchCategory[] = [
  "fullworld_do_not",
  "fullworld_layout_ideal",
  "mountain_terrain",
  "mountain_lidar",
  "mountain_labyrinth",
  "terrain_tiling",
  "terrain",
];

export function slugId(parts: string[]): string {
  const raw = parts
    .join("-")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 80);
  if (raw.length >= 8) return raw;
  return createHash("sha256").update(parts.join("|")).digest("hex").slice(0, 16);
}

function inferCategory(
  sourceType: SourceType,
  topics: string,
  title: string,
  visualCategory?: VisualReference["category"]
): ResearchCategory {
  if (sourceType === "visual_ref" && visualCategory) {
    if (visualCategory === "lidar_terrain") return "terrain";
    if (visualCategory === "mountain_lidar") return "mountain_lidar";
    if (visualCategory === "aquifer_structure") return "ground_placement";
    if (visualCategory === "karst_geomorphology") return "ground_placement";
    if (visualCategory === "terrain") return "terrain";
    if (visualCategory === "navmesh") return "navmesh";
    if (visualCategory === "lighting") return "materials_lighting";
    if (visualCategory === "pcg_shell") return "mesh_shell";
    if (visualCategory === "studio_environment") return "visual_reference";
    if (visualCategory === "performance") return "performance";
    if (visualCategory === "cave_layout") return "visual_reference";
    if (visualCategory === "mesh") return "mesh_shell";
    return "visual_reference";
  }
  if (sourceType === "lab_index") return "lab_index";
  if (sourceType === "engine_doc") return "engine_docs";

  const blob = `${title} ${topics}`.toLowerCase();
  if (blob.includes("pipeline_editor_responsiveness")) return "pipeline_editor_responsiveness";
  if (blob.includes("3d_gaussian_splat_unity")) return "3d_gaussian_splat_unity";
  if (blob.includes("3d_gaussian_splat")) return "3d_gaussian_splat";
  if (blob.includes("pipeline") && blob.includes("pacing")) return "pipeline_editor_responsiveness";
  if (blob.includes("mountain_lidar")) return "mountain_lidar";
  if (blob.includes("hollow_titan_do_not") || (blob.includes("do not") && blob.includes("hollow titan")))
    return "hollow_titan_do_not";
  if (
    blob.includes("hollow_titan") ||
    blob.includes("hollow titan") ||
    blob.includes("hollowtitan") ||
    blob.includes("lpmagicalforest")
  )
    return "hollow_titan";
  if (
    blob.includes("fullworld_do_not") ||
    blob.includes("environmentrooms") ||
    blob.includes("preserveblockoutgridlayout") ||
    blob.includes("ground=grid") ||
    (blob.includes("do not") && blob.includes("modular") && blob.includes("grid"))
  )
    return "fullworld_do_not";
  if (
    blob.includes("fullworld_layout_ideal") ||
    blob.includes("ideal 49") ||
    blob.includes("ideal_layout_49")
  )
    return "fullworld_layout_ideal";
  if (blob.includes("fullworld_generation_style") || blob.includes("preset_"))
    return "fullworld_generation_style";
  if (blob.includes("terrain_tiling") || blob.includes("setneighbors") || blob.includes("heightmap edge"))
    return "terrain_tiling";
  if (blob.includes("labyrinth_do_not") || (blob.includes("do not") && blob.includes("labyrinth")))
    return "labyrinth_do_not";
  if (blob.includes("prop_do_not") || (blob.includes("do not") && blob.includes("prop")))
    return "prop_do_not";
  if (blob.includes("unity_editor_ai_guidance") || blob.includes("prompt preview"))
    return "unity_editor_ai_guidance";
  if (
    blob.includes("surface_props") ||
    blob.includes("prop scatter") ||
    blob.includes("vegetation_intelligent")
  )
    return "surface_props";
  if (
    blob.includes("play_disk_do_not") ||
    blob.includes("appalachian_peak_do_not") ||
    blob.includes("foothill_do_not")
  )
    return "fullworld_do_not";
  if (
    blob.includes("play_disk_structure") ||
    blob.includes("appalachian_peak") ||
    blob.includes("foothill_terrain")
  )
    return "mountain_terrain";
  if (blob.includes("mountain_labyrinth") || blob.includes("labyrinth annex") || blob.includes("walkway labyrinth"))
    return "mountain_labyrinth";
  if (blob.includes("mountain_terrain") || blob.includes("outer_ring_mountains") || blob.includes("cliff"))
    return "mountain_terrain";
  if (blob.includes("open_world_streaming") || blob.includes("world streaming") || blob.includes("chunk"))
    return "terrain";
  if (blob.includes("terrain") || blob.includes("heightmap") || blob.includes("carve"))
    return "terrain";
  if (blob.includes("navmesh") || blob.includes("navigation")) return "navmesh";
  if (blob.includes("collision") || blob.includes("walkable") || blob.includes("fall"))
    return "floor_collision";
  if (blob.includes("placement") || blob.includes("underground")) return "ground_placement";
  if (blob.includes("pbr") || blob.includes("material") || blob.includes("lighting"))
    return "materials_lighting";
  if (blob.includes("performance") || blob.includes("triangle") || blob.includes("gpu"))
    return "performance";
  if (
    blob.includes("adventure") ||
    blob.includes("set-piece") ||
    blob.includes("set piece") ||
    blob.includes("moving hazard") ||
    blob.includes("traversal")
  )
    return "adventure";
  if (blob.includes("testing") || blob.includes("qa") || blob.includes("playtest"))
    return "qa_testing";
  if (blob.includes("pcg") || blob.includes("procedural") || blob.includes("mesh"))
    return "mesh_shell";
  return "pcg_research";
}

function inferRungs(category: ResearchCategory, topics: string, title: string): PromptRung[] {
  const rungs = new Set<PromptRung>();
  const blob = `${title} ${topics}`.toLowerCase();

  for (const [rung, cats] of Object.entries(RUNG_TO_CATEGORIES) as [
    PromptRung,
    ResearchCategory[] | undefined,
  ][]) {
    if (cats?.includes(category)) rungs.add(rung);
  }

  if (blob.includes("terrain")) rungs.add("ground_placement");
  if (blob.includes("aquifer") || blob.includes("karst") || blob.includes("floridan"))
    rungs.add("ground_placement");
  if (blob.includes("cave structure") || blob.includes("cavestructureonly"))
    rungs.add("visual_shell");
  if (blob.includes("navmesh")) rungs.add("navmesh");
  if (blob.includes("collision")) rungs.add("floor_collision");
  if (blob.includes("visual") || blob.includes("shell")) rungs.add("visual_shell");
  if (blob.includes("open_world_streaming") || blob.includes("streaming") || blob.includes("chunk"))
    rungs.add("research");
  if (blob.includes("mountain_lidar")) rungs.add("ground_placement");
  if (blob.includes("mountain_terrain") || blob.includes("outer_ring") || blob.includes("cliff"))
    rungs.add("ground_placement");

  if (!rungs.size) rungs.add("other");
  return [...rungs];
}

function serializeFromMetadata(
  title: string,
  year: number,
  topics: string,
  notes?: string
): SerializedResearch {
  const keyPoints = topics
    .split(/[,;]/)
    .map((s) => s.trim())
    .filter(Boolean)
    .slice(0, 8);
  return {
    fetchedUtc: new Date().toISOString(),
    fetchSkipped: true,
    summary: `${title} (${year}) — ${topics}`,
    keyPoints,
    implementationNotes: notes,
  };
}

export function cachePaths(hubRoot: string) {
  const root = join(hubRoot.replace(/\/$/, ""), RESEARCH_CACHE_REL);
  return {
    root,
    index: join(root, "index.json"),
    entries: join(root, "entries"),
    images: join(root, "images"),
    categories: join(root, "categories"),
  };
}

export function loadIndex(hubRoot: string): ResearchCacheIndex | null {
  const { index } = cachePaths(hubRoot);
  if (!existsSync(index)) return null;
  try {
    return JSON.parse(readFileSync(index, "utf8")) as ResearchCacheIndex;
  } catch {
    return null;
  }
}

export function entryFromPaper(p: ResearchEntry): CacheEntry {
  const id = slugId([p.lab, p.title]);
  const category = inferCategory("paper", p.topics, p.title);
  const relBase = `${RESEARCH_CACHE_REL}/entries/${id}`;
  return {
    id,
    category,
    sourceType: "paper",
    rungs: inferRungs(category, p.topics, p.title),
    title: p.title,
    lab: p.lab,
    year: p.year,
    venue: p.venue,
    url: p.url,
    pdfUrl: p.pdfUrl,
    topics: p.topics,
    provenInProduction: isProvenInProduction(p),
    imageUrls: p.imageUrls,
    serialized: serializeFromMetadata(p.title, p.year, p.topics, (p as ResearchEntry & { notes?: string }).notes),
    metaPath: `${relBase}/meta.json`,
    contentPath: `${relBase}/content.md`,
    imagesManifestPath: p.imageUrls?.length
      ? `${RESEARCH_CACHE_REL}/images/${id}/manifest.json`
      : undefined,
  };
}

export function entryFromEngineDoc(p: ResearchEntry): CacheEntry {
  const e = entryFromPaper(p);
  e.sourceType = "engine_doc";
  e.category = "engine_docs";
  e.provenInProduction = true;
  return e;
}

export function entryFromVisualRef(v: VisualReference): CacheEntry {
  const id = v.id;
  const category = inferCategory("visual_ref", v.notes, v.title, v.category);
  const relBase = `${RESEARCH_CACHE_REL}/entries/${id}`;
  const lab =
    v.studio ??
    (v.category === "lidar_terrain"
      ? "NOAA / USGS"
      : v.category === "aquifer_structure" || v.category === "karst_geomorphology"
        ? "USGS / FGS / FDEP"
        : "Unity / GPUOpen");
  const topics = [
    v.notes,
    v.caveStructureOnly ? "caveStructureOnly:true" : "",
    v.aquiferSystem ? `aquifer:${v.aquiferSystem}` : "",
    v.region ? `region:${v.region}` : "",
    v.counties?.length ? `counties:${v.counties.join(",")}` : "",
    ...(v.dataUrls ?? []).map((u) => `data:${u}`),
  ]
    .filter(Boolean)
    .join(" | ");
  return {
    id,
    category,
    sourceType: "visual_ref",
    rungs: inferRungs(category, topics, v.title),
    title: v.title,
    lab,
    year: v.year,
    venue: v.category === "lidar_terrain" ? "Public LiDAR DEM" : "Visual reference",
    url: v.docUrl,
    topics,
    provenInProduction: true,
    imageUrls: v.imageUrls,
    serialized: serializeFromMetadata(v.title, v.year, v.notes, v.notes),
    metaPath: `${relBase}/meta.json`,
    contentPath: `${relBase}/content.md`,
    imagesManifestPath:
      v.imageUrls?.length ? `${RESEARCH_CACHE_REL}/images/${id}/manifest.json` : undefined,
  };
}

export function buildAllCatalogEntries(): CacheEntry[] {
  const map = new Map<string, CacheEntry>();
  for (const p of papersForMinYear()) {
    const e = entryFromPaper(p);
    map.set(e.id, e);
  }
  for (const p of UNITY6_ENGINE_REFS) {
    const e = entryFromEngineDoc(p);
    map.set(e.id, e);
  }
  for (const v of CAVE_VISUAL_REFERENCES) {
    const e = entryFromVisualRef(v);
    map.set(e.id, e);
  }
  return [...map.values()];
}

export function lookupForRung(
  hubRoot: string,
  rung: string,
  limit = 12
): { index: ResearchCacheIndex; hits: CacheEntry[] } | null {
  const index = loadIndex(hubRoot);
  if (!index) return null;

  const aliases = new Set(researchAliasesForRung(rung));
  const cats = new Set<ResearchCategory>();
  for (const alias of aliases) {
    for (const c of RUNG_TO_CATEGORIES[alias] ?? []) cats.add(c);
  }
  if (rung.includes("height") || rung.includes("slope") || rung.includes("crater"))
    cats.add("terrain");
  if (rung.startsWith("prop_")) {
    cats.add("surface_props");
    cats.add("prop_do_not");
    cats.add("visual_reference");
  }
  if (
    rung.includes("mountain") ||
    rung === "outer_ring_mountains" ||
    rung === "nine_tile_grid"
  ) {
    for (const c of MOUNTAIN_RUNG_CATEGORIES) cats.add(c);
  }

  const hits = Object.values(index.entries)
    .filter(
      (e) =>
        e.provenInProduction &&
        e.rungs.some((r) => aliases.has(r as PromptRung))
    )
    .sort((a, b) => {
      const aCat = cats.has(a.category) ? 0 : 1;
      const bCat = cats.has(b.category) ? 0 : 1;
      return aCat - bCat || b.year - a.year;
    })
    .slice(0, limit);

  return { index, hits };
}

/** Category-first lookup for mountain / terrain research phases. */
export function lookupForCategories(
  hubRoot: string,
  categories: ResearchCategory[],
  limit = 12
): { index: ResearchCacheIndex; hits: CacheEntry[] } | null {
  const index = loadIndex(hubRoot);
  if (!index) return null;
  const catSet = new Set(categories);
  const hits = Object.values(index.entries)
    .filter((e) => e.provenInProduction && catSet.has(e.category))
    .sort((a, b) => b.year - a.year)
    .slice(0, limit);
  return { index, hits };
}

export function cachedUrls(index: ResearchCacheIndex): Set<string> {
  const urls = new Set<string>();
  for (const e of Object.values(index.entries)) {
    urls.add(e.url);
    if (e.pdfUrl) urls.add(e.pdfUrl);
    for (const img of e.imageUrls ?? []) urls.add(img);
  }
  return urls;
}

function buildAgentGuidance(entry: CacheEntry): string {
  const lines: string[] = [];
  if (entry.category === "fullworld_do_not") {
    lines.push(
      "Read before any FullWorld terrain/mountain edit. These are hard anti-patterns — do not substitute modular Grid layout."
    );
  } else if (entry.category === "fullworld_layout_ideal") {
    lines.push(
      "Target silhouette reference — open the concept PNG when Ideal preset is active. Similar per buildSeed, not a pixel-perfect clone."
    );
  } else if (entry.category === "fullworld_generation_style") {
    lines.push(
      "Apply only when ActiveGenerationStyle.json styleId matches. Merge with WorldGenerationRequest flags from FullWorldGenerationStyleCatalog."
    );
  } else if (entry.category === "mountain_labyrinth") {
    lines.push(
      "Use during mountain_labyrinth_research/carve only. Read WHAT IS a mountain labyrinth (definition entry) + up to 5 STYLE Example PNGs — adapt creatively, do not clone. Carve spine polylines (BuildCarvePolylines), NOT per-edge grid. Stitch to nine-tile play disk; check labyrinth_do_not."
    );
  } else if (entry.category === "labyrinth_do_not") {
    lines.push(
      "Anti-patterns for labyrinth phases only — never use as primary maze reference. Includes: jagged heightmap shard splinters, per-edge 520-segment queue, pixelated rectangular block, double pass, WidenPassages dilation. Open ref-0.png for jagged-shard example."
    );
  } else if (entry.category === "terrain_tiling") {
    lines.push("Unity Terrain neighbor stitch / heightmap edges — not Environment/Grid modular rooms.");
  } else if (entry.category === "surface_props") {
    lines.push(
      "Use when placing props on terrain tiles (surface_vegetation_intelligent, prop_*). SampleHeight + slope/normal gate on EVERY tile. Open ref PNG in ResearchCache/images/{id}/."
    );
  } else if (entry.category === "prop_do_not") {
    lines.push(
      "HARD STOP before scatter/cliff: no props on vertical walls, no cliff ring around whole map, no inventing research. Do NOT BUILD/GENERATE/MAKE database."
    );
  } else if (entry.category === "unity_editor_ai_guidance") {
    lines.push(
      "Editor UX only: show large Scene notification + Hub line when agent prompt is sent — pipeline must NOT block on DisplayProgressBar."
    );
  } else {
    lines.push(
      `Use when pipeline rung matches (${entry.rungs.join(", ")}). Prefer local ResearchCache content over re-searching the web.`
    );
  }
  if (entry.serialized.implementationNotes) {
    lines.push("");
    lines.push(entry.serialized.implementationNotes);
  }
  return lines.join("\n");
}

export function writeEntryFiles(hubRoot: string, entry: CacheEntry): void {
  const { root, entries, images } = cachePaths(hubRoot);
  const entryDir = join(entries, entry.id);
  mkdirSync(entryDir, { recursive: true });

  writeFileSync(join(entryDir, "meta.json"), JSON.stringify(entry, null, 2), "utf8");

  const agentGuidance = buildAgentGuidance(entry);
  const md = [
    `# ${entry.title}`,
    "",
    "## Source",
    `- **Primary URL:** ${entry.url}`,
    entry.pdfUrl ? `- **PDF / secondary:** ${entry.pdfUrl}` : "",
    `- **Lab / author:** ${entry.lab ?? "—"}`,
    `- **Venue:** ${entry.venue ?? "—"}`,
    `- **Year:** ${entry.year}`,
    `- **Category:** ${entry.category}`,
    `- **Proven in production:** ${entry.provenInProduction ? "yes" : "no"}`,
    `- **Rungs:** ${entry.rungs.join(", ")}`,
    "",
    "## Summary",
    entry.serialized.summary,
    "",
    "## Agent guidance",
    agentGuidance,
    "",
    "## Key points",
    ...entry.serialized.keyPoints.map((k) => `- ${k}`),
    entry.serialized.implementationNotes
      ? ["", "## Implementation notes", entry.serialized.implementationNotes]
      : [],
    "",
    "## Data portals (LiDAR / DEM download — not preview images)",
    ...(entry.topics.includes("data:")
      ? entry.topics
          .split("|")
          .map((t) => t.trim())
          .filter((t) => t.startsWith("data:"))
          .map((t) => `- ${t.replace(/^data:/, "")}`)
      : ["- (none)"]),
    "",
    "## Images",
    ...(entry.localImages?.length
      ? entry.localImages.map(
          (i) => `- Local: \`${i.relativePath}\` (from ${i.sourceUrl})`
        )
      : (entry.imageUrls ?? []).map((u) => `- Remote: ${u}`)),
    "",
  ]
    .flat()
    .filter((l) => l !== "")
    .join("\n");

  writeFileSync(join(entryDir, "content.md"), md, "utf8");

  if (entry.imageUrls?.length) {
    const imgDir = join(images, entry.id);
    mkdirSync(imgDir, { recursive: true });
    const manifest = {
      entryId: entry.id,
      category: entry.category,
      images: entry.imageUrls.map((sourceUrl, i) => ({
        sourceUrl,
        fileName: `ref-${i}.png`,
        relativePath: `${RESEARCH_CACHE_REL}/images/${entry.id}/ref-${i}.png`,
        cached: !!(entry.localImages && entry.localImages[i]?.bytes),
      })),
    };
    writeFileSync(join(imgDir, "manifest.json"), JSON.stringify(manifest, null, 2), "utf8");
  }
}

export function writeCategoryIndexes(
  hubRoot: string,
  index: ResearchCacheIndex
): void {
  const { categories: catDir } = cachePaths(hubRoot);
  mkdirSync(catDir, { recursive: true });

  for (const cat of RESEARCH_CATEGORIES) {
    const block = index.categories[cat];
    if (!block?.entryIds.length) continue;

    const slim = block.entryIds.map((id) => {
      const e = index.entries[id];
      return {
        id,
        title: e.title,
        url: e.url,
        metaPath: e.metaPath,
        contentPath: e.contentPath,
        imagesManifestPath: e.imagesManifestPath,
      };
    });

    const catPath = join(catDir, cat);
    mkdirSync(catPath, { recursive: true });
    writeFileSync(
      join(catPath, "index.json"),
      JSON.stringify(
        {
          id: cat,
          label: block.label,
          entryCount: slim.length,
          entries: slim,
        },
        null,
        2
      ),
      "utf8"
    );
  }
}

export function buildIndexFromEntries(
  entries: CacheEntry[],
  hubRoot?: string
): ResearchCacheIndex {
  const categories = {} as Record<ResearchCategory, CategoryIndex>;
  for (const cat of RESEARCH_CATEGORIES) {
    categories[cat] = { id: cat, label: CATEGORY_LABELS[cat], entryIds: [] };
  }

  const entriesMap: Record<string, CacheEntry> = {};
  for (const e of entries) {
    entriesMap[e.id] = e;
    categories[e.category].entryIds.push(e.id);
  }

  for (const cat of RESEARCH_CATEGORIES) {
    categories[cat].entryIds.sort();
  }

  let withLocalImages = 0;
  let withFetchedBytes = 0;
  let floridaAquiferEntries = 0;
  for (const e of entries) {
    if (e.localImages?.some((i) => i.bytes && i.bytes > 0)) withLocalImages++;
    if (e.localImages?.length) withFetchedBytes++;
    if (
      e.topics.includes("caveStructureOnly") ||
      e.id.startsWith("fl-aquifer-") ||
      e.id.startsWith("fl-fgs-") ||
      e.id.startsWith("fl-fgdl-")
    ) {
      floridaAquiferEntries++;
    }
  }

  let floridaTerrain: FloridaTerrainIndexSummary | undefined;
  let floridaHillshadeCount = 0;
  if (hubRoot) {
    floridaTerrain = buildFloridaTerrainSummary(hubRoot);
    floridaHillshadeCount = floridaTerrain.hillshades.length;
  }

  return {
    version: RESEARCH_CACHE_VERSION,
    generatedUtc: new Date().toISOString(),
    policy: `${RESEARCH_MIN_YEAR}–2026 proven sources only. Read ResearchCache before any web search. Florida aquifer/LiDAR: cave structure only — see RESEARCH_DATA_ATTRIBUTION.md.`,
    hubRelativeRoot: RESEARCH_CACHE_REL,
    categories,
    entries: entriesMap,
    floridaTerrain,
    stats: {
      totalEntries: entries.length,
      provenEntries: entries.filter((e) => e.provenInProduction).length,
      withLocalImages,
      withFetchedBytes,
      floridaAquiferEntries,
      floridaHillshadeCount,
    },
  };
}
