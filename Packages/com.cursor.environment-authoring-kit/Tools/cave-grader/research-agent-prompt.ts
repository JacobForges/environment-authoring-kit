/**
 * ONE consolidated research prompt for terrain + cave agents (no full JSON dumps).
 */
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { PromptRung } from "./prompt-ladder.js";
import {
  RESEARCH_AGENT_PROMPT_JSON_REL,
  RESEARCH_AGENT_PROMPT_REL,
} from "./agent-artifact-paths.js";
import { listLocalHillshadePaths } from "./florida-research-paths.js";
import {
  collectExecutionEntries,
  type ResearchBriefScope,
} from "./research-execution-brief.js";
import {
  LAND_MASS_REFERENCE_RULES_MD,
  REQUIRED_JSON_REPORTS,
} from "./hardcoded-agent-prompts.js";
import type { CacheEntry } from "./research-store.js";

export const MAX_RESEARCH_IMAGES = 5;

export type ResearchImageScope = ResearchBriefScope;

function snippetFromContent(hubRoot: string, entry: CacheEntry, maxChars = 280): string {
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

/** Land-mass hillshade PNGs only — no cache ref thumbnails (legends/watermarks/icons). */
export function pickResearchImagePaths(
  hubRoot: string,
  _scope: ResearchImageScope = "combined"
): string[] {
  const hub = hubRoot.replace(/\/$/, "");
  const out: string[] = [];

  for (const h of listLocalHillshadePaths(hub)) {
    if (out.length >= MAX_RESEARCH_IMAGES) break;
    out.push(h.relativePath);
  }

  return out;
}

export type ResearchSummaryRow = {
  id: string;
  category: string;
  contentPath: string;
  snippet: string;
};

export function buildResearchSummaries(
  hubRoot: string,
  scope: ResearchBriefScope
): ResearchSummaryRow[] {
  const hub = hubRoot.replace(/\/$/, "");
  const entries = collectExecutionEntries(hub, "research", 8, undefined, scope);
  return entries.map((e) => ({
    id: e.id,
    category: e.category ?? "",
    contentPath: e.contentPath,
    snippet: snippetFromContent(hub, e, 280),
  }));
}

/** Compact research block for terrain/cave fix prompts (summaries + max 5 hillshades — no full cache JSON). */
export function formatResearchBlockForFixPrompt(
  hubRoot: string,
  scope: Exclude<ResearchBriefScope, "combined"> = "terrain"
): string {
  const hub = hubRoot.replace(/\/$/, "");
  const images = pickResearchImagePaths(hub, scope);
  const lines: string[] = [
    "## Research (summarized — mandatory before terrain/cave fix)",
    "",
    `Read: \`${hub}/${RESEARCH_AGENT_PROMPT_REL}\` (one consolidated file; regenerate with \`npm run generate-research-agent-prompt\`).`,
    "",
    LAND_MASS_REFERENCE_RULES_MD,
    "",
    ...bulletsForScope(hub, scope, scope === "terrain" ? "Terrain / LiDAR cache" : "Cave / karst cache"),
    `### Hillshade image paths (max ${MAX_RESEARCH_IMAGES}, land-mass only)`,
    "",
  ];
  if (images.length) {
    for (const rel of images) lines.push(`- \`${hub}/${rel}\``);
  } else {
    lines.push("_Run `npm run sync-florida-hillshades` — no county hillshade.png yet._");
  }
  lines.push("");
  return lines.join("\n");
}

function bulletsForScope(hubRoot: string, scope: ResearchBriefScope, title: string): string[] {
  const rows = buildResearchSummaries(hubRoot, scope);
  const lines: string[] = [`### ${title}`, ""];
  if (!rows.length) {
    lines.push("_No cache entries matched this scope — run `npm run sync-research-pull`._", "");
    return lines;
  }
  for (const r of rows) {
    lines.push(
      `- **${r.id}** (\`${r.category}\`) — \`${hubRoot}/${r.contentPath}\``,
      `  - ${r.snippet}`
    );
  }
  lines.push("");
  return lines;
}

export function buildConsolidatedResearchPromptMd(
  hubRoot: string,
  phaseId?: string,
  rung?: PromptRung
): string {
  const hub = hubRoot.replace(/\/$/, "");
  const activeRung = (rung ?? "research") as PromptRung;
  const images = pickResearchImagePaths(hub, "combined");

  const lines: string[] = [
    "# Cave build — consolidated research agent prompt",
    "",
    `**Generated:** ${new Date().toISOString()} | **Phase:** \`${phaseId ?? "research"}\` | **Rung:** \`${activeRung}\``,
    "",
    LAND_MASS_REFERENCE_RULES_MD,
    "",
    "## Policy (Florida structure-only)",
    "",
    "Use bare-earth LiDAR (class 2), Floridan aquifer **structural** surfaces, and karst/subsidence polygons for **cave void layout only**.",
    "Do **not** use water table, TDS, bathymetry, inundation DEMs, or spring discharge for underground geometry.",
    "See `Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md`.",
    "",
    `## Hillshade references (max ${MAX_RESEARCH_IMAGES} — land mass only, no ref-*.png cache images)`,
    "",
  ];

  if (images.length) {
    for (const rel of images) {
      lines.push(`- **Image:** \`${hub}/${rel}\``);
    }
  } else {
    lines.push(
      "_No local hillshade PNGs yet._ Run `npm run sync-florida-hillshades` (county `hillshade.png` under ResearchCache)."
    );
  }
  lines.push("");

  lines.push("## Terrain generation", "");
  lines.push(...bulletsForScope(hub, "terrain", "Top terrain / LiDAR / ground_placement entries"));
  lines.push("## Cave generation", "");
  lines.push(...bulletsForScope(hub, "cave", "Top cave / shell / route / combat entries"));
  lines.push("## Required JSON reports (read on disk — do not paste full files)", "");
  for (const rel of REQUIRED_JSON_REPORTS) {
    lines.push(`- \`${hub}/${rel}\``);
  }
  lines.push(
    "",
    `- **Research cache index:** \`${hub}/Assets/EnvironmentKit/ResearchCache/index.json\``,
    `- **Phase prompts index:** \`${hub}/Assets/EnvironmentKit/Generated/CaveBuildPhasePromptsIndex.json\``,
    "",
    "Active phase work uses `CaveBuildActivePhasePrompt.md` (task focus + small excerpts only).",
    ""
  );

  return lines.join("\n");
}

export function buildResearchAgentPromptJson(
  hubRoot: string,
  phaseId?: string,
  rung?: PromptRung
) {
  const hub = hubRoot.replace(/\/$/, "");
  return {
    generatedUtc: new Date().toISOString(),
    phaseId: phaseId ?? "research",
    rung: rung ?? "research",
    maxImages: MAX_RESEARCH_IMAGES,
    imagePaths: pickResearchImagePaths(hub, "combined"),
    terrainSummaries: buildResearchSummaries(hub, "terrain"),
    caveSummaries: buildResearchSummaries(hub, "cave"),
    markdownPath: RESEARCH_AGENT_PROMPT_REL,
    policy:
      "Land-mass hillshade only (no ref-*.png); Florida structure-only; read JSON reports on disk when executing.",
  };
}

export function writeResearchAgentPrompt(
  hubRoot: string,
  phaseId?: string,
  rung?: PromptRung
): { mdRel: string; jsonRel: string } {
  const hub = hubRoot.replace(/\/$/, "");
  const gen = join(hub, "Assets/EnvironmentKit/Generated");
  mkdirSync(gen, { recursive: true });

  const md = buildConsolidatedResearchPromptMd(hub, phaseId, rung);
  const json = buildResearchAgentPromptJson(hub, phaseId, rung);

  writeFileSync(join(hub, RESEARCH_AGENT_PROMPT_REL), md, "utf8");
  writeFileSync(join(hub, RESEARCH_AGENT_PROMPT_JSON_REL), JSON.stringify(json, null, 2), "utf8");

  return { mdRel: RESEARCH_AGENT_PROMPT_REL, jsonRel: RESEARCH_AGENT_PROMPT_JSON_REL };
}
