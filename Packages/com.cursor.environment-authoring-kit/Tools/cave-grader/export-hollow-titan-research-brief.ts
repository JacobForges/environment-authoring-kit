#!/usr/bin/env npx tsx
/**
 * Writes HollowTitanResearchExecutionBrief.json + .md for meat-phase agents.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  HOLLOW_TITAN_MASTER_IMAGE_REL,
  HOLLOW_TITAN_PHASE_PROMPTS,
  getHollowTitanPhasePrompt,
} from "./hollow-titan-concept-prompts.js";
import {
  HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL,
  HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_MD_REL,
} from "./hollow-titan-agent-artifact-paths.js";
import { hollowTitanPhaseForIndex } from "./hollow-titan-pipeline-phases.js";
import { researchCategoriesForPhase } from "./hollow-titan-research-prompt.js";
import { resolveHubRoot } from "./hub-root.js";
import {
  RESEARCH_CACHE_GENERATED_REL,
  RESEARCH_CACHE_INDEX_REL,
  loadIndex,
  lookupForCategories,
  type CacheEntry,
  type ResearchCategory,
} from "./research-store.js";

function parsePhaseIndex(argv: string[]): number | undefined {
  for (const a of argv) {
    if (a.startsWith("--phase=")) {
      const n = parseInt(a.slice("--phase=".length), 10);
      if (!Number.isNaN(n) && n >= 0 && n <= 11) return n;
    }
  }
  const fromEnv = process.env.HOLLOW_TITAN_ACTIVE_PHASE?.trim();
  if (fromEnv) {
    const n = parseInt(fromEnv, 10);
    if (!Number.isNaN(n) && n >= 0 && n <= 11) return n;
  }
  return undefined;
}

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

function collectHollowTitanEntries(
  hubRoot: string,
  phaseIndex?: number,
  limit = 14
): CacheEntry[] {
  const cats = (
    phaseIndex !== undefined
      ? researchCategoriesForPhase(phaseIndex)
      : (["hollow_titan", "hollow_titan_do_not", "fullworld_do_not"] as ResearchCategory[])
  ) as ResearchCategory[];

  const prioritized = ["hollow_titan", "hollow_titan_do_not"] as ResearchCategory[];
  const orderedCats = [
    ...prioritized,
    ...cats.filter((c) => !prioritized.includes(c)),
  ];

  const index = loadIndex(hubRoot);
  if (!index) return [];

  const seen = new Set<string>();
  const hits: CacheEntry[] = [];

  for (const cat of orderedCats) {
    const lookup = lookupForCategories(hubRoot, [cat], limit);
    if (!lookup) continue;
    for (const e of lookup.hits) {
      if (seen.has(e.id)) continue;
      seen.add(e.id);
      hits.push(e);
      if (hits.length >= limit) return hits;
    }
  }

  return hits;
}

function buildMandatoryReadOrder(hub: string, phaseIndex?: number): string[] {
  const order: string[] = [
    `${hub}/${HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL}`,
    `${hub}/${HOLLOW_TITAN_MASTER_IMAGE_REL}`,
    `${hub}/Assets/EnvironmentKit/ResearchCache/index.json`,
    `${hub}/${RESEARCH_CACHE_GENERATED_REL}`,
  ];

  if (phaseIndex !== undefined) {
    const phase = getHollowTitanPhasePrompt(phaseIndex);
    order.splice(2, 0, `${hub}/${phase.conceptImageRel}`);
    order.push(`${hub}/Assets/EnvironmentKit/Generated/HollowTitanActivePhasePrompt.md`);
    order.push(`${hub}/Assets/EnvironmentKit/Generated/HollowTitanResearchActionPlan.json`);
  } else {
    for (const p of HOLLOW_TITAN_PHASE_PROMPTS) {
      order.push(`${hub}/${p.conceptImageRel}`);
    }
  }

  return order;
}

export function buildHollowTitanResearchBriefJson(hubRoot: string, phaseIndex?: number) {
  const hub = hubRoot.replace(/\/$/, "");
  const entries = collectHollowTitanEntries(hub, phaseIndex);
  const phase =
    phaseIndex !== undefined ? getHollowTitanPhasePrompt(phaseIndex) : undefined;
  const pipelinePhase =
    phaseIndex !== undefined ? hollowTitanPhaseForIndex(phaseIndex) : undefined;

  return {
    generatedUtc: new Date().toISOString(),
    landmarkId: "HollowTitanLandmark",
    phaseIndex: phaseIndex ?? -1,
    phaseId: phase?.id ?? "hollow_titan_all_phases",
    phaseLabel: phase?.label ?? "Hollow Titan — all meat phases",
    policy:
      "Mandatory: open concept images + hollow_titan / hollow_titan_do_not cache entries before C# edits. LPMagicalForest primary for exterior phases.",
    indexPath: RESEARCH_CACHE_INDEX_REL,
    cachePointer: RESEARCH_CACHE_GENERATED_REL,
    researchCategories:
      phaseIndex !== undefined ? researchCategoriesForPhase(phaseIndex) : ["hollow_titan", "hollow_titan_do_not"],
    pipelinePhase: pipelinePhase
      ? { id: pipelinePhase.id, focus: pipelinePhase.focus, forbidden: pipelinePhase.forbidden }
      : null,
    conceptImages: {
      master: HOLLOW_TITAN_MASTER_IMAGE_REL,
      phase: phase?.conceptImageRel ?? null,
      allPhaseRels: HOLLOW_TITAN_PHASE_PROMPTS.map((p) => p.conceptImageRel),
    },
    mandatoryReadOrder: buildMandatoryReadOrder(hub, phaseIndex),
    entries: entries.map((e) => ({
      id: e.id,
      title: e.title,
      category: e.category,
      contentPath: e.contentPath,
      metaPath: e.metaPath,
      url: e.url,
      summary: e.serialized.summary,
      snippet: snippetFromContent(hub, e),
    })),
  };
}

export function formatHollowTitanResearchBriefMd(hubRoot: string, phaseIndex?: number): string {
  const hub = hubRoot.replace(/\/$/, "");
  const brief = buildHollowTitanResearchBriefJson(hub, phaseIndex);
  const phase =
    phaseIndex !== undefined ? getHollowTitanPhasePrompt(phaseIndex) : undefined;

  const lines: string[] = [
    "# Hollow Titan — research execution brief",
    "",
    `**Generated:** ${brief.generatedUtc}`,
    phase
      ? `**Active phase:** ${phase.index} — \`${phase.id}\` (${phase.label})`
      : "**Scope:** all 12 meat phases (0–11)",
    "",
    "You **must** use pulled data on disk for **planning** and **implementing** Hollow Titan kit fixes.",
    "",
    "**Before any C# edit:** complete the mandatory read order below and cite ≥2 `content.md` entry ids in your plan.",
    "",
    "## Mandatory read order",
    "",
  ];

  for (let i = 0; i < brief.mandatoryReadOrder.length; i++) {
    lines.push(`${i + 1}. \`${brief.mandatoryReadOrder[i]}\``);
  }
  lines.push("");

  if (brief.pipelinePhase) {
    lines.push("## Phase focus", "", `- **Focus:** ${brief.pipelinePhase.focus}`, "");
    if (brief.pipelinePhase.forbidden?.length) {
      lines.push("### Forbidden", "");
      for (const f of brief.pipelinePhase.forbidden) lines.push(`- ${f}`);
      lines.push("");
    }
  }

  lines.push("## Cache entries (hollow_titan first)", "");
  if (!brief.entries.length) {
    lines.push("_No cache entries — run `npm run sync-hollow-titan-research`._", "");
  } else {
    for (const e of brief.entries) {
      lines.push(`### ${e.id} (\`${e.category}\`)`);
      lines.push(`- **Read:** \`${hub}/${e.contentPath}\``);
      lines.push(`- **URL:** ${e.url}`);
      lines.push(`- **Use for:** ${e.snippet}`);
      lines.push("");
    }
  }

  lines.push(
    "## Execution rules",
    "",
    "1. **Concept images** — open master + active phase PNG before C#.",
    "2. **LPMagicalForest** — primary for phases 2, 6, 7; CC0 L01–L10 fallback only.",
    "3. **Landmark scope** — spawns/loot/patrol on HollowTitanLandmark root only.",
    "4. **No FullWorld rebuild** — fix single failing meat phase from action plan JSON.",
    "5. **Sync** — `HUB_ROOT=<hub> npm run sync-hollow-titan-research` refreshes cache + this brief.",
    ""
  );

  return lines.join("\n");
}

export function writeHollowTitanResearchExecutionBrief(
  hubRoot: string,
  phaseIndex?: number
): { jsonRel: string; mdRel: string } {
  const hub = hubRoot.replace(/\/$/, "");
  const gen = join(hub, "Assets/EnvironmentKit/Generated");
  mkdirSync(gen, { recursive: true });

  const json = buildHollowTitanResearchBriefJson(hub, phaseIndex);
  const md = formatHollowTitanResearchBriefMd(hub, phaseIndex);

  writeFileSync(join(hub, HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL), JSON.stringify(json, null, 2), "utf8");
  writeFileSync(join(hub, HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_MD_REL), md, "utf8");

  return { jsonRel: HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL, mdRel: HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_MD_REL };
}

function main() {
  const hubRoot = resolveHubRoot();
  const phaseIndex = parsePhaseIndex(process.argv);
  const { jsonRel, mdRel } = writeHollowTitanResearchExecutionBrief(hubRoot, phaseIndex);
  const phaseNote = phaseIndex !== undefined ? ` phase=${phaseIndex}` : "";
  console.log(`[HollowTitanResearch] Wrote ${jsonRel} + ${mdRel}${phaseNote}`);
}

main();
