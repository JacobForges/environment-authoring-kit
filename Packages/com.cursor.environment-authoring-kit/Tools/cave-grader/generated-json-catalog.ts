/**
 * Discovers all Generated + ResearchCache JSON used by the kit and builds compact summaries for AI prompts.
 */
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { resolveHubRoot } from "./hub-root.js";

export type JsonCategory =
  | "terrain"
  | "cave"
  | "surface"
  | "quality"
  | "research"
  | "props"
  | "workflow"
  | "other";

export type DiscoveredJson = {
  relativePath: string;
  category: JsonCategory;
  bytes: number;
  modifiedUtc: string;
  summary: string;
  topKeys: string[];
};

const GENERATED_REL = "Assets/EnvironmentKit/Generated";
const RESEARCH_CACHE_REL = "Assets/EnvironmentKit/ResearchCache";

function categorize(rel: string): JsonCategory {
  const lower = rel.toLowerCase();
  if (
    lower.includes("terrain") ||
    lower.includes("dem") ||
    lower.includes("georef") ||
    lower.includes("hillshade") ||
    lower.includes("elevation-grid") ||
    lower.includes("lidar")
  )
    return "terrain";
  if (lower.includes("surface") && !lower.includes("cave")) return "surface";
  if (lower.includes("prop")) return "props";
  if (
    lower.includes("cave") ||
    lower.includes("route") ||
    lower.includes("combat") ||
    lower.includes("spawn") ||
    lower.includes("navmesh") ||
    lower.includes("lighting")
  )
    return "cave";
  if (
    lower.includes("quality") ||
    lower.includes("ladder") ||
    lower.includes("probe") ||
    lower.includes("playable") ||
    lower.includes("grading") ||
    lower.includes("failing") ||
    lower.includes("compile")
  )
    return "quality";
  if (lower.includes("research") || lower.includes("cache/index")) return "research";
  if (lower.includes("workflow") || lower.includes("agent") || lower.includes("gate"))
    return "workflow";
  return "other";
}

function summarizeObject(obj: unknown, maxLen: number): string {
  if (obj === null || obj === undefined) return "";
  if (typeof obj !== "object") return String(obj).slice(0, maxLen);
  const o = obj as Record<string, unknown>;
  const parts: string[] = [];
  for (const [k, v] of Object.entries(o)) {
    if (parts.length >= 12) break;
    if (v === null || v === undefined) continue;
    if (typeof v === "object" && !Array.isArray(v)) {
      parts.push(`${k}:{…}`);
    } else if (Array.isArray(v)) {
      parts.push(`${k}:[${v.length}]`);
    } else {
      const s = String(v);
      parts.push(`${k}:${s.length > 80 ? s.slice(0, 77) + "…" : s}`);
    }
  }
  return parts.join(" | ").slice(0, maxLen);
}

export function summarizeJsonFile(hubRoot: string, relativePath: string, maxChars = 500): string {
  const full = join(hubRoot, relativePath);
  if (!existsSync(full)) return "(missing)";
  try {
    const raw = readFileSync(full, "utf8");
    if (!raw.trim()) return "(empty)";
    const parsed = JSON.parse(raw) as unknown;
    return summarizeObject(parsed, maxChars);
  } catch {
    return readFileSync(full, "utf8").replace(/\s+/g, " ").trim().slice(0, maxChars);
  }
}

function scanDir(hubRoot: string, relDir: string, pattern: RegExp): string[] {
  const abs = join(hubRoot, relDir);
  if (!existsSync(abs)) return [];
  const out: string[] = [];
  const walk = (dir: string, prefix: string) => {
    for (const name of readdirSync(dir)) {
      const p = join(dir, name);
      const st = statSync(p);
      if (st.isDirectory()) {
        walk(p, join(prefix, name));
        continue;
      }
      if (!pattern.test(name)) continue;
      out.push(join(relDir, prefix, name).replace(/\\/g, "/"));
    }
  };
  walk(abs, "");
  return out;
}

export function discoverAllJson(hubRoot: string): DiscoveredJson[] {
  const hub = hubRoot.replace(/\/$/, "");
  const paths = new Set<string>();

  for (const f of scanDir(hub, GENERATED_REL, /\.json$/i)) paths.add(f);
  paths.add(`${RESEARCH_CACHE_REL}/index.json`);
  paths.add(`${RESEARCH_CACHE_REL}/images/florida-hillshades-index.json`);

  for (const f of scanDir(hub, `${RESEARCH_CACHE_REL}/images`, /elevation-grid\.json$/i))
    paths.add(f);
  for (const f of scanDir(hub, `${RESEARCH_CACHE_REL}/images`, /manifest\.json$/i))
    if (f.includes("-hillshade/")) paths.add(f);

  const list: DiscoveredJson[] = [];
  for (const relativePath of [...paths].sort()) {
    const full = join(hub, relativePath);
    if (!existsSync(full)) continue;
    const st = statSync(full);
    list.push({
      relativePath,
      category: categorize(relativePath),
      bytes: st.size,
      modifiedUtc: st.mtime.toISOString(),
      summary: summarizeJsonFile(hub, relativePath, 480),
      topKeys: extractTopKeys(hub, relativePath),
    });
  }
  return list;
}

function extractTopKeys(hubRoot: string, relativePath: string): string[] {
  const full = join(hubRoot, relativePath);
  if (!existsSync(full)) return [];
  try {
    const parsed = JSON.parse(readFileSync(full, "utf8")) as Record<string, unknown>;
    return Object.keys(parsed).slice(0, 16);
  } catch {
    return [];
  }
}

const RESEARCH_PHASE_KEY_SUFFIXES = [
  "CaveBuildResearchAgentPrompt.json",
  "CaveBuildResearchActionPlan.json",
  "CaveBuildGeneratedJsonManifest.json",
  "CaveBuildPhasePromptsIndex.json",
  "CaveBuildResearchExecutionBrief.json",
  "TerrainResearchExecutionBrief.json",
  "CaveResearchExecutionBrief.json",
  "CaveBuildResearchCache.json",
  "CaveBuildResearch.json",
  "SurfaceDemGeorefStatus.json",
  "CaveBuildPhaseBotReport.json",
  "florida-hillshades-index.json",
];

export function pathsForPhase(phaseId: string, all: DiscoveredJson[]): string[] {
  const phase = phaseId.toLowerCase();
  const priority = new Set<string>();

  if (phase === "research") {
    for (const suffix of RESEARCH_PHASE_KEY_SUFFIXES) {
      const hit = all.find((j) => j.relativePath.endsWith(suffix));
      if (hit) priority.add(hit.relativePath);
    }
    for (const j of all) {
      if (j.category === "terrain" && j.relativePath.includes("elevation-grid"))
        priority.add(j.relativePath);
    }
    return [...priority].slice(0, 12);
  }

  const always = [
    "CaveBuildResearchActionPlan.json",
    "CaveBuildResearchAgentPrompt.json",
    "CaveBuildPhaseBotReport.json",
    "TerrainResearchExecutionBrief.json",
    "CaveResearchExecutionBrief.json",
    "CaveBuildResearchExecutionBrief.json",
    "SurfaceDemGeorefStatus.json",
    "CaveBuildGeneratedJsonManifest.json",
  ];
  for (const a of always) {
    const hit = all.find((j) => j.relativePath.endsWith(a));
    if (hit) priority.add(hit.relativePath);
  }

  for (const j of all) {
    const name = j.relativePath.toLowerCase();
    if (phase.includes("terrain") || phase.includes("dem") || phase.includes("lidar")) {
      if (j.category === "terrain" || j.category === "surface") priority.add(j.relativePath);
    } else if (phase.includes("surface")) {
      if (j.category === "surface" || j.category === "terrain" || j.category === "props")
        priority.add(j.relativePath);
    } else if (phase.includes("cave") || phase.includes("visual") || phase.includes("layout")) {
      if (j.category === "cave" || j.category === "quality") priority.add(j.relativePath);
    } else if (phase.includes("prop") || phase.includes("vegetation")) {
      if (j.category === "props" || j.category === "surface") priority.add(j.relativePath);
    } else {
      priority.add(j.relativePath);
    }
    if (name.includes(phase.replace(/_/g, ""))) priority.add(j.relativePath);
  }

  return [...priority];
}

export function readJsonExcerpt(hubRoot: string, relativePath: string, max = 6000): string {
  const full = join(hubRoot, relativePath);
  if (!existsSync(full)) return `(missing: ${relativePath})`;
  try {
    return readFileSync(full, "utf8").slice(0, max);
  } catch {
    return `(unreadable: ${relativePath})`;
  }
}

export function buildManifest(hubRoot: string, activePhase?: string) {
  const files = discoverAllJson(hubRoot);
  return {
    generatedUtc: new Date().toISOString(),
    hubRoot: hubRoot.replace(/\/$/, ""),
    activePhase: activePhase ?? "",
    fileCount: files.length,
    files,
    phasePaths: activePhase ? pathsForPhase(activePhase, files) : [],
  };
}

export function formatManifestMarkdown(manifest: ReturnType<typeof buildManifest>): string {
  const lines: string[] = [
    "# Unified agent context — all Generated JSON",
    "",
    `**Generated:** ${manifest.generatedUtc} | **Files:** ${manifest.fileCount} | **Active phase:** \`${manifest.activePhase || "(none)"}\``,
    "",
    "## Mandatory read order",
    "1. `CaveBuildResearchActionPlan.json` — research URLs + plan steps",
    "2. `TerrainResearchExecutionBrief.md` + `CaveResearchExecutionBrief.md`",
    "3. Every JSON row below for the active phase (full excerpts in CaveBuildPhaseDataDigest.md + CaveBuildActivePhasePrompt.md)",
    "4. `CaveBuildDoNotPrompt.md` + `CaveBuildNextStepsPrompt.md`",
    "",
    "## Catalog (every JSON on disk)",
    "| Path | Category | Bytes | Summary |",
    "|------|----------|-------|---------|",
  ];
  for (const f of manifest.files) {
    const safe = f.summary.replace(/\|/g, "/").replace(/\n/g, " ");
    lines.push(`| \`${f.relativePath}\` | ${f.category} | ${f.bytes} | ${safe} |`);
  }
  if (manifest.phasePaths.length) {
    lines.push("", "## Active phase JSON (read these first)", "");
    for (const p of manifest.phasePaths) lines.push(`- \`${manifest.hubRoot}/${p}\``);
  }
  return lines.join("\n");
}
