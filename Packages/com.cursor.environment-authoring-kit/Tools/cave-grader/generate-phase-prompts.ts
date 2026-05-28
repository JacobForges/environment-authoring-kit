#!/usr/bin/env npx tsx
/**
 * AI-ready active-phase prompt from live Generated JSON + research URLs.
 * Writes a single overwrite file (not one MD per pipeline phase).
 */
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  ACTIVE_PHASE_PROMPT_REL,
  PHASE_DATA_DIGEST_REL,
  PHASE_PROMPTS_INDEX_REL,
  UNIFIED_AGENT_PROMPT_REL,
  JSON_MANIFEST_REL,
  RESEARCH_AGENT_PROMPT_REL,
  phasePromptFrontMatter,
} from "./agent-artifact-paths.js";
import {
  PIPELINE_PHASES,
  phaseForMeatPass,
  phaseForRung,
  phaseForTerrainMeatPass,
} from "./pipeline-phases.js";
import { formatSearchQueriesBlock } from "./research-sources.js";
import { parseRungArg } from "./prompt-ladder.js";
import { parseMeatPassArg } from "./meat-loop-research.js";
import { buildHardcodedCorePrompt } from "./hardcoded-agent-prompts.js";
import { useHardcodedPrompts } from "./prompt-ladder.js";
import {
  buildManifest,
  pathsForPhase,
  readJsonExcerpt,
  discoverAllJson,
} from "./generated-json-catalog.js";
import { resolveHubRoot } from "./hub-root.js";

const hubRoot = resolveHubRoot();
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");

const EXCERPT_CHARS = 1500;
const MAX_PHASE_JSON_EXCERPTS = 3;
const MAX_NON_RESEARCH_EXCERPTS = 5;

function resolveActivePhaseId(): string | undefined {
  const fromEnv = process.env.CAVE_ACTIVE_PHASE?.trim();
  if (fromEnv) return fromEnv;
  const rung = parseRungArg(process.argv);
  if (rung) {
    const p = phaseForRung(rung);
    if (p) return p.id;
  }
  const meatPass = parseMeatPassArg(process.argv);
  if (meatPass !== undefined && meatPass >= 0) {
    const terrainWorkflow = process.env.CAVE_WORKFLOW === "terrain";
    const p = terrainWorkflow ? phaseForTerrainMeatPass(meatPass) : phaseForMeatPass(meatPass);
    if (p) return p.id;
  }
  return undefined;
}

function shouldSkipManifestRebuild(): boolean {
  if (process.env.CAVE_SKIP_MANIFEST_REBUILD === "1") return true;
  const manifestPath = join(hubRoot, JSON_MANIFEST_REL);
  if (!existsSync(manifestPath)) return false;
  try {
    const ageMs = Date.now() - statSync(manifestPath).mtimeMs;
    return ageMs < 30 * 60 * 1000;
  } catch {
    return false;
  }
}

function resolvePhaseJsonPaths(phaseId: string): string[] {
  const phase = PIPELINE_PHASES.find((p) => p.id === phaseId);
  const allJson = discoverAllJson(hubRoot);
  if (phaseId === "research") {
    return [...new Set([...pathsForPhase(phaseId, allJson), ...(phase?.jsonPaths ?? [])])]
      .sort()
      .slice(0, MAX_PHASE_JSON_EXCERPTS);
  }
  return [...new Set([...(phase?.jsonPaths ?? []), ...pathsForPhase(phaseId, allJson)])].sort();
}

function buildPhaseMarkdown(phaseId: string, iteration: number): string {
  const phase = PIPELINE_PHASES.find((p) => p.id === phaseId);
  if (!phase) return `# Unknown phase ${phaseId}\n`;

  const rung = phase.rung === "compile_gate" || phase.rung === "pre_build" ? "other" : phase.rung;
  const isResearch = phaseId === "research";

  const discovered = discoverAllJson(hubRoot);
  const phaseJsonPaths = resolvePhaseJsonPaths(phaseId);

  const maxExcerpts = isResearch ? MAX_PHASE_JSON_EXCERPTS : MAX_NON_RESEARCH_EXCERPTS;
  const excerptPaths = phaseJsonPaths.slice(0, maxExcerpts);

  const lines: string[] = [
    phasePromptFrontMatter(phaseId, iteration, phase.rung),
    `# Phase prompt: ${phase.title}`,
    "",
    `**Phase id:** \`${phase.id}\` | **Iteration:** ${iteration} | **Rung:** \`${phase.rung}\``,
    "",
  ];
  if (useHardcodedPrompts()) {
    lines.push(buildHardcodedCorePrompt(hubRoot), "");
  }
  lines.push(
    "## Task focus",
    phase.focus,
    "",
    "## Unified context (read first)",
    `- \`${hubRoot}/${RESEARCH_AGENT_PROMPT_REL}\` — **ONE** consolidated research prompt (terrain + cave summaries, max 5 images)`,
    `- \`${hubRoot}/${UNIFIED_AGENT_PROMPT_REL}\` — JSON catalog (no full paste in this file)`,
    `- \`${hubRoot}/${JSON_MANIFEST_REL}\``,
    `- \`${hubRoot}/${PHASE_DATA_DIGEST_REL}\` — live excerpts for **this** phase (front matter \`phaseId\`)`,
    `- \`${hubRoot}/${ACTIVE_PHASE_PROMPT_REL}\` — **this file** (only active phase prompt on disk)`,
    "",
    "## Required JSON (phase paths — read on disk)"
  );
  for (const p of phaseJsonPaths) lines.push(`- \`${hubRoot}/${p}\``);

  if (!useHardcodedPrompts()) {
    lines.push("", "## Web search (if cache miss — max 2 queries)", "");
    for (const q of phase.webSearchQueries) lines.push(`- ${q}`);
    const qualityPath = join(gen, "CaveBuildQualityReport.json");
    if (existsSync(qualityPath)) {
      try {
        const report = JSON.parse(readFileSync(qualityPath, "utf8")) as import("./prompt-ladder.js").QualityReport;
        const searchBlock = formatSearchQueriesBlock(
          rung as import("./prompt-ladder.js").PromptRung,
          report,
          hubRoot
        );
        if (searchBlock) lines.push("", searchBlock);
      } catch {
        /* quality JSON optional */
      }
    }
  } else {
    lines.push(
      "",
      "## Web search",
      "",
      "_Disabled for hardcoded prompts — use on-disk JSON + hillshade land mass only._",
      ""
    );
  }

  if (!isResearch) {
    lines.push(
      "",
      "## JSON excerpts (active phase only — max " +
        maxExcerpts +
        " files, " +
        EXCERPT_CHARS +
        " chars each)",
      ""
    );
    for (const p of excerptPaths) {
      const entry = discovered.find((j) => j.relativePath === p);
      if (entry) {
        lines.push(`### ${p}`, `*${entry.category} | ${entry.bytes} bytes | ${entry.summary}*`, "");
      }
      lines.push("```json", readJsonExcerpt(hubRoot, p, EXCERPT_CHARS), "```", "");
    }
  } else {
    lines.push(
      "",
      "## Research phase",
      "",
      "Do **not** paste full ResearchCache JSON here. Open `CaveBuildResearchAgentPrompt.md` and cited `content.md` paths on disk.",
      ""
    );
    for (const p of excerptPaths) {
      lines.push("```json", readJsonExcerpt(hubRoot, p, EXCERPT_CHARS), "```", "");
    }
  }

  const actionPlanRel = "Assets/EnvironmentKit/Generated/CaveBuildResearchActionPlan.json";
  const botRel = "Assets/EnvironmentKit/Generated/CaveBuildPhaseBotReport.json";
  if (existsSync(join(hubRoot, actionPlanRel)) && !isResearch) {
    lines.push(
      "",
      "## Research action plan (pointer)",
      `- Read on disk: \`${hubRoot}/${actionPlanRel}\` (do not paste full JSON)`,
      ""
    );
  }
  if (existsSync(join(hubRoot, botRel)) && !isResearch) {
    lines.push(
      "",
      "## Phase bot report (pointer)",
      `- Read on disk: \`${hubRoot}/${botRel}\``,
      ""
    );
  }
  for (const extra of ["CaveBuildNextStepsPrompt.md", "CaveBuildDoNotPrompt.md"]) {
    const p = join(gen, extra);
    if (existsSync(p)) {
      lines.push("", `## ${extra}`, readFileSync(p, "utf8").slice(0, 2000), "");
    }
  }

  lines.push(
    "## Execution (never skip)",
    "",
    "1. Read `CaveBuildResearchAgentPrompt.md` + action plan on disk.",
    "2. Open cited ResearchCache `content.md` paths (max 5 images).",
    "3. Obey `CaveBuildDoNotPrompt.md`.",
    "4. Apply minimal kit C# for this phase only; re-run phase bot.",
    "5. Surface route bot must pass before underground cave route fixes.",
    ""
  );

  return lines.join("\n");
}

function main() {
  const iteration = parseInt(process.env.CAVE_AUTONOMOUS_ITERATION ?? "0", 10) || 0;
  const phaseId = resolveActivePhaseId() ?? "research";
  console.log(`[CaveCursor:info] generate-phase-prompts start phase=${phaseId} iteration=${iteration}`);

  mkdirSync(gen, { recursive: true });

  if (!shouldSkipManifestRebuild()) {
    const manifest = buildManifest(hubRoot, phaseId);
    writeFileSync(
      join(gen, "CaveBuildGeneratedJsonManifest.json"),
      JSON.stringify(manifest, null, 2),
      "utf8"
    );
  } else {
    console.log("[CaveCursor:info] Skipped manifest rebuild (fresh or CAVE_SKIP_MANIFEST_REBUILD=1)");
  }

  const md = buildPhaseMarkdown(phaseId, iteration);
  writeFileSync(join(hubRoot, ACTIVE_PHASE_PROMPT_REL), md, "utf8");

  const index = {
    generatedUtc: new Date().toISOString(),
    iteration,
    activePhaseId: phaseId,
    activePhasePrompt: ACTIVE_PHASE_PROMPT_REL,
    researchAgentPrompt: RESEARCH_AGENT_PROMPT_REL,
    phaseDataDigest: PHASE_DATA_DIGEST_REL,
    unifiedAgentPrompt: UNIFIED_AGENT_PROMPT_REL,
    hubRoot,
    deprecatedPattern: "CaveBuildPhasePrompt_*.md (purged on new build — do not create)",
  };
  writeFileSync(join(gen, "CaveBuildPhasePromptsIndex.json"), JSON.stringify(index, null, 2), "utf8");
  console.log(
    `[CaveCursor:info] Wrote active phase prompt for \`${phaseId}\` → ${ACTIVE_PHASE_PROMPT_REL} (iteration=${iteration})`
  );
}

main();
