/**
 * Turns ResearchCache on disk into an execution brief agents must use for planning + C# fixes.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { PromptRung } from "./prompt-ladder.js";
import { getMeatPassResearch, type MeatPassResearch } from "./meat-loop-research.js";
import { formatFloridaTerrainBlock, buildFloridaTerrainSummary } from "./florida-research-paths.js";
import {
  RESEARCH_CACHE_GENERATED_REL,
  RESEARCH_CACHE_INDEX_REL,
  RESEARCH_CACHE_REL,
  loadIndex,
  lookupForRung,
  type CacheEntry,
} from "./research-store.js";

export const RESEARCH_EXECUTION_BRIEF_REL =
  "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json";
export const RESEARCH_EXECUTION_BRIEF_MD_REL =
  "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.md";

/** Surface-only: terrain, LiDAR, trails, props, NavMesh. */
export const TERRAIN_RESEARCH_EXECUTION_BRIEF_REL =
  "Assets/EnvironmentKit/Generated/TerrainResearchExecutionBrief.json";
export const TERRAIN_RESEARCH_EXECUTION_BRIEF_MD_REL =
  "Assets/EnvironmentKit/Generated/TerrainResearchExecutionBrief.md";

/** Cave-only: layout, route mesh, shell, combat, validation. */
export const CAVE_RESEARCH_EXECUTION_BRIEF_REL =
  "Assets/EnvironmentKit/Generated/CaveResearchExecutionBrief.json";
export const CAVE_RESEARCH_EXECUTION_BRIEF_MD_REL =
  "Assets/EnvironmentKit/Generated/CaveResearchExecutionBrief.md";

export type ResearchBriefScope = "combined" | "terrain" | "cave";

const TERRAIN_CATEGORIES = new Set([
  "terrain",
  "lidar_terrain",
  "ground_placement",
  "visual_reference",
]);

const CAVE_CATEGORIES = new Set([
  "pcg_shell",
  "cave",
  "combat",
  "performance",
  "materials",
  "navmesh",
]);

function snippetFromContent(hubRoot: string, entry: CacheEntry, maxChars = 420): string {
  const path = join(hubRoot, entry.contentPath);
  if (!existsSync(path)) return entry.serialized.summary.slice(0, maxChars);
  try {
    const text = readFileSync(path, "utf8");
    const summaryIdx = text.indexOf("## Summary");
    const body = summaryIdx >= 0 ? text.slice(summaryIdx) : text;
    return body.replace(/\s+/g, " ").trim().slice(0, maxChars);
  } catch {
    return entry.serialized.summary.slice(0, maxChars);
  }
}

function localImagePaths(hubRoot: string, entry: CacheEntry): string[] {
  const out: string[] = [];
  if (!entry.imageUrls?.length) return out;
  for (let i = 0; i < entry.imageUrls.length; i++) {
    const rel = `${RESEARCH_CACHE_REL}/images/${entry.id}/ref-${i}.png`;
    if (existsSync(join(hubRoot, rel))) out.push(rel);
  }
  return out;
}

/** Entries to surface for execution (rung hits + Florida structure). */
function entryMatchesMeatPass(entry: CacheEntry, mission: MeatPassResearch): boolean {
  if (mission.entryIdPrefixes.some((p) => entry.id.startsWith(p))) return true;
  if (mission.categories.includes(entry.category)) return true;
  return false;
}

function entryMatchesScope(entry: CacheEntry, scope: ResearchBriefScope): boolean {
  if (scope === "combined") return true;
  const cat = entry.category ?? "";
  if (scope === "terrain") {
    return (
      TERRAIN_CATEGORIES.has(cat) ||
      entry.id.includes("terrain") ||
      entry.id.includes("lidar") ||
      entry.id.includes("florida") ||
      entry.id.includes("hillshade")
    );
  }
  return (
    CAVE_CATEGORIES.has(cat) ||
    entry.id.includes("cave") ||
    entry.id.includes("pcg") ||
    entry.id.includes("shell") ||
    entry.id.includes("tunnel")
  );
}

export function collectExecutionEntries(
  hubRoot: string,
  activeRung: PromptRung,
  limit = 10,
  meatPass?: number,
  scope: ResearchBriefScope = "combined"
): CacheEntry[] {
  const hub = hubRoot.replace(/\/$/, "");
  const index = loadIndex(hub);
  if (!index) return [];

  const lookup = lookupForRung(hub, activeRung, limit);
  const hits = lookup?.hits ?? [];
  const map = new Map<string, CacheEntry>();
  for (const e of hits) map.set(e.id, e);

  const florida = buildFloridaTerrainSummary(hub);
  for (const id of [...florida.aquiferEntryIds, ...florida.panhandleEntryIds].slice(0, 6)) {
    const e = index.entries[id];
    if (e && !map.has(id)) map.set(id, e);
  }

  if (meatPass !== undefined && meatPass >= 0) {
    const mission = getMeatPassResearch(meatPass);
    const meatMap = new Map<string, CacheEntry>();
    for (const id of Object.keys(index.entries)) {
      const e = index.entries[id];
      if (e && entryMatchesMeatPass(e, mission)) meatMap.set(e.id, e);
    }
    for (const e of meatMap.values()) {
      if (!map.has(e.id)) map.set(e.id, e);
    }
  }

  const scoped = [...map.values()].filter((e) => entryMatchesScope(e, scope));
  return scoped.slice(0, limit + 6);
}

export function formatMeatPassMissionBlock(meatPass: number): string {
  const m = getMeatPassResearch(meatPass);
  return [
    "### Meat-loop pass mission",
    "",
    `- **Pass:** ${meatPass}`,
    `- **Title:** ${m.title}`,
    `- **Research focus:** ${m.researchFocus}`,
    `- **Categories:** ${m.categories.join(", ")}`,
    "",
    "Grade and fix **only** what this pass owns; do not repeat prior-pass work unless a stage below still fails.",
    "",
  ].join("\n");
}

export function formatResearchExecutionBlock(
  hubRoot: string,
  activeRung: PromptRung,
  meatPass?: number,
  scope: ResearchBriefScope = "combined"
): string {
  const hub = hubRoot.replace(/\/$/, "");
  const entries = collectExecutionEntries(hub, activeRung, 10, meatPass, scope);
  const florida = buildFloridaTerrainSummary(hub);

  const title =
    scope === "terrain"
      ? "## Terrain research execution brief (MANDATORY — terrain + LiDAR + props)"
      : scope === "cave"
        ? "## Cave research execution brief (MANDATORY — cave geometry + shell + combat)"
        : "## Research execution brief (MANDATORY — plan + code)";

  const lines: string[] = [
    title,
    "",
    "You **must** use pulled data on disk for **planning** and **implementing** kit fixes. Do not invent metrics without reading these files.",
    "",
    "**Before any C# edit:** open at least 2 `content.md` paths below and cite them in your plan table (entry id + file path).",
    "",
  ];

  if (meatPass !== undefined && meatPass >= 0) {
    lines.push(formatMeatPassMissionBlock(meatPass));
  }

  lines.push(
    "### Index & pointer",
    `- \`${hub}/Assets/EnvironmentKit/ResearchCache/index.json\``,
    `- \`${hub}/${RESEARCH_CACHE_GENERATED_REL}\``,
    `- \`${hub}/Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json\` (this brief)`,
    ""
  );

  if (florida.hillshades.length) {
    lines.push("### Terrain images (use for ground_placement / mouth carve, max 5)");
    for (const h of florida.hillshades.slice(0, 5)) {
      lines.push(`- **${h.county}:** \`${hub}/${h.relativePath}\``);
    }
    lines.push("");
  }

  let entryImageLines = 0;
  const maxEntryImages = 5;

  lines.push("### Cache entries for this rung (read → cite → implement)");
  for (const e of entries) {
    lines.push(`#### ${e.id}`);
    lines.push(`- **Read:** \`${hub}/${e.contentPath}\``);
    lines.push(`- **Meta:** \`${hub}/${e.metaPath}\``);
    const imgs = localImagePaths(hub, e);
    for (const img of imgs) {
      if (entryImageLines >= maxEntryImages) break;
      lines.push(`- **Image:** \`${hub}/${img}\``);
      entryImageLines++;
    }
    lines.push(`- **Use for:** ${snippetFromContent(hub, e)}`);
    lines.push("");
  }

  const fl = formatFloridaTerrainBlock(hub, activeRung);
  if (fl) lines.push(fl);

  lines.push(
    "### Execution rules",
    "",
    "1. **Plan table** — each row must name a `ResearchCache/entries/{id}` file or hillshade PNG you opened.",
    "2. **C# changes** — map research to kit files (`CaveGroundPlacementUtility`, `RouteTerrain*`, `CavePerformanceBudget`, etc.).",
    "3. **No water surfaces** — aquifer/LiDAR refs are structure-only (see RESEARCH_DATA_ATTRIBUTION.md).",
    "4. **Do not skip disk** — web search only if a listed `content.md` does not answer the failing metric.",
    ""
  );

  return lines.join("\n");
}

export function buildResearchExecutionBriefJson(
  hubRoot: string,
  activeRung: PromptRung,
  meatPass?: number,
  scope: ResearchBriefScope = "combined"
) {
  const hub = hubRoot.replace(/\/$/, "");
  const entries = collectExecutionEntries(hub, activeRung, 10, meatPass, scope);
  const florida = buildFloridaTerrainSummary(hub);
  const meatMission = meatPass !== undefined && meatPass >= 0 ? getMeatPassResearch(meatPass) : null;

  return {
    generatedUtc: new Date().toISOString(),
    scope,
    activeRung,
    meatLoopPass: meatPass ?? -1,
    meatPassMission: meatMission,
    policy:
      "Mandatory: use ResearchCache + images for planning and executing fixes. Cite entry ids in plan; implement in kit C#.",
    indexPath: RESEARCH_CACHE_INDEX_REL,
    cachePointer: RESEARCH_CACHE_GENERATED_REL,
    floridaTerrain: florida,
    entries: entries.map((e) => ({
      id: e.id,
      title: e.title,
      category: e.category,
      contentPath: e.contentPath,
      metaPath: e.metaPath,
      imagePaths: localImagePaths(hub, e),
      summary: e.serialized.summary,
    })),
    hillshades: florida.hillshades,
  };
}

function pathsForScope(scope: ResearchBriefScope): { jsonRel: string; mdRel: string } {
  if (scope === "terrain") {
    return { jsonRel: TERRAIN_RESEARCH_EXECUTION_BRIEF_REL, mdRel: TERRAIN_RESEARCH_EXECUTION_BRIEF_MD_REL };
  }
  if (scope === "cave") {
    return { jsonRel: CAVE_RESEARCH_EXECUTION_BRIEF_REL, mdRel: CAVE_RESEARCH_EXECUTION_BRIEF_MD_REL };
  }
  return { jsonRel: RESEARCH_EXECUTION_BRIEF_REL, mdRel: RESEARCH_EXECUTION_BRIEF_MD_REL };
}

export function writeResearchExecutionBrief(
  hubRoot: string,
  activeRung: PromptRung,
  meatPass?: number,
  scope: ResearchBriefScope = "combined"
): string {
  const hub = hubRoot.replace(/\/$/, "");
  const gen = join(hub, "Assets/EnvironmentKit/Generated");
  const json = buildResearchExecutionBriefJson(hub, activeRung, meatPass, scope);
  const md = formatResearchExecutionBlock(hub, activeRung, meatPass, scope);
  const { jsonRel, mdRel } = pathsForScope(scope);

  mkdirSync(gen, { recursive: true });
  writeFileSync(join(hub, jsonRel), JSON.stringify(json, null, 2), "utf8");
  writeFileSync(join(hub, mdRel), md, "utf8");

  if (scope === "combined") {
    writeResearchExecutionBrief(hubRoot, activeRung, meatPass, "terrain");
    writeResearchExecutionBrief(hubRoot, activeRung, meatPass, "cave");
  }

  return jsonRel;
}
