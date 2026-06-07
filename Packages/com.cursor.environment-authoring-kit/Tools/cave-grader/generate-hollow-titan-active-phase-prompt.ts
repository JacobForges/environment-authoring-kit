#!/usr/bin/env npx tsx
/**
 * Writes HollowTitanActivePhasePrompt.md for one meat phase (index 0–11).
 */
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  HOLLOW_TITAN_ACTIVE_PHASE_PROMPT_REL,
  HOLLOW_TITAN_PHASE_PROMPT_MANIFEST_REL,
  HOLLOW_TITAN_RESEARCH_ACTION_PLAN_REL,
  HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL,
  hollowTitanPhasePromptFrontMatter,
} from "./hollow-titan-agent-artifact-paths.js";
import {
  formatHollowTitanPhasePromptBlock,
  getHollowTitanPhasePrompt,
} from "./hollow-titan-concept-prompts.js";
import {
  buildHollowTitanResearchSummaries,
  formatHollowTitanConceptImagesBlock,
  HOLLOW_TITAN_FEATHER_RULES,
  researchCategoriesForPhase,
} from "./hollow-titan-research-prompt.js";
import { formatResearchCacheBlockForCategories } from "./research-cache-prompt.js";
import { resolveHubRoot } from "./hub-root.js";

function parsePhaseIndex(argv: string[]): number {
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
  return 0;
}

function parseSeed(): number {
  const fromEnv = process.env.HOLLOW_TITAN_BUILD_SEED?.trim();
  if (fromEnv) {
    const n = parseInt(fromEnv, 10);
    if (!Number.isNaN(n)) return n;
  }
  return 0;
}

function mandatoryReadUrls(hub: string, phaseIndex: number): string[] {
  const summaries = buildHollowTitanResearchSummaries(hub, phaseIndex, 6);
  return summaries.map((s) => `${hub}/${s.contentPath}`);
}

function buildActivePhaseMarkdown(hubRoot: string, phaseIndex: number, seed: number): string {
  const hub = hubRoot.replace(/\/$/, "");
  const phase = getHollowTitanPhasePrompt(phaseIndex);
  const cats = researchCategoriesForPhase(phaseIndex);
  const urls = mandatoryReadUrls(hub, phaseIndex);

  const lines: string[] = [
    hollowTitanPhasePromptFrontMatter(phaseIndex, phase.id, seed),
    `# Hollow Titan active phase: ${phase.label}`,
    "",
    `**Phase index:** ${phase.index} | **Phase id:** \`${phase.id}\` | **Build seed:** ${seed}`,
    "",
    formatHollowTitanConceptImagesBlock(hub, phaseIndex),
    formatHollowTitanPhasePromptBlock(phaseIndex, hub),
    "## Feather + landmark rules",
    "",
    HOLLOW_TITAN_FEATHER_RULES,
    "",
    formatResearchCacheBlockForCategories(cats, hub, 8),
    "",
    "## Research snippets (mandatory read — open content.md)",
    "",
  ];

  const summaries = buildHollowTitanResearchSummaries(hub, phaseIndex, 8);
  if (!summaries.length) {
    lines.push("_No cache entries — run `npm run sync-research-pull`._", "");
  } else {
    for (const r of summaries) {
      lines.push(`- **${r.id}** (\`${r.category}\`)`);
      lines.push(`  - \`${hub}/${r.contentPath}\``);
      lines.push(`  - ${r.snippet}`);
    }
    lines.push("");
  }

  lines.push("## Mandatory read URLs", "");
  for (const u of urls) lines.push(`- \`${u}\``);
  lines.push(
    "",
    "## Companion generated files",
    "",
    `- \`${hub}/${HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL}\``,
    `- \`${hub}/${HOLLOW_TITAN_RESEARCH_ACTION_PLAN_REL}\``,
    `- \`${hub}/${HOLLOW_TITAN_PHASE_PROMPT_MANIFEST_REL}\``,
    `- \`${hub}/${HOLLOW_TITAN_ACTIVE_PHASE_PROMPT_REL}\` — **this file**`,
    ""
  );

  return lines.join("\n");
}

function main() {
  const hubRoot = resolveHubRoot();
  const phaseIndex = parsePhaseIndex(process.argv);
  const seed = parseSeed();
  const hub = hubRoot.replace(/\/$/, "");
  const gen = join(hub, "Assets/EnvironmentKit/Generated");
  mkdirSync(gen, { recursive: true });

  const md = buildActivePhaseMarkdown(hub, phaseIndex, seed);
  const outPath = join(hub, HOLLOW_TITAN_ACTIVE_PHASE_PROMPT_REL);
  writeFileSync(outPath, md, "utf8");

  const phase = getHollowTitanPhasePrompt(phaseIndex);
  console.log(
    `[CaveCursor:info] Hollow Titan active phase prompt → ${HOLLOW_TITAN_ACTIVE_PHASE_PROMPT_REL} (${phase.id})`
  );
}

main();
