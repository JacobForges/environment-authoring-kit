#!/usr/bin/env npx tsx
/**
 * Research → plan → execute: reads phase bot report suggestions, ResearchCache URLs, writes action plan + Next/DO NOT prompts.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { PIPELINE_PHASES } from "./pipeline-phases.js";
import { MANDATORY_BUILD_RULES_MD } from "./mandatory-build-rules.js";
import { buildManifest, pathsForPhase } from "./generated-json-catalog.js";
import { resolveHubRoot } from "./hub-root.js";
import {
  ACTIVE_PHASE_PROMPT_REL,
  PHASE_DATA_DIGEST_REL,
  UNIFIED_AGENT_PROMPT_REL,
  JSON_MANIFEST_REL,
  RESEARCH_AGENT_PROMPT_REL,
} from "./agent-artifact-paths.js";

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../../..")).replace(/\/$/, "");
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");
const cacheIndex = join(hubRoot, "Assets/EnvironmentKit/ResearchCache/index.json");

type CacheEntry = { id: string; title?: string; url?: string; sourceUrl?: string; category?: string; contentPath?: string };
type CacheIndex = { entries?: CacheEntry[] };

function loadJson<T>(path: string): T | null {
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch {
    return null;
  }
}

function researchUrlsForPhase(phaseId: string, limit = 8): { id: string; url: string; localMd: string }[] {
  const phase = PIPELINE_PHASES.find((p) => p.id === phaseId);
  const index = loadJson<CacheIndex>(cacheIndex);
  const out: { id: string; url: string; localMd: string }[] = [];
  if (!index?.entries) return out;

  const entries = Array.isArray(index.entries)
    ? index.entries
    : index.entries
      ? Object.values(index.entries as Record<string, CacheEntry>)
      : [];
  for (const e of entries) {
    if (out.length >= limit) break;
    const cat = (e.category ?? "").toLowerCase();
    const match =
      !phase ||
      phase.researchCategories.some((c) => cat.includes(c) || e.id?.includes(c.replace("_", "")));
    if (!match && phase) continue;
    const url = e.url ?? e.sourceUrl;
    if (!url) continue;
    const localMd = e.contentPath
      ? `${hubRoot}/${e.contentPath.replace(/^\//, "")}`
      : `${hubRoot}/Assets/EnvironmentKit/ResearchCache/entries/${e.id}/content.md`;
    out.push({ id: e.id, url, localMd });
  }
  return out;
}

function suggestionsFromBotReport(): string[] {
  const bot = loadJson<{
    suggestedNextActions?: string[];
    doNot?: string[];
  }>(join(gen, "CaveBuildPhaseBotReport.json"));
  return bot?.suggestedNextActions ?? [];
}

function buildPlan(phaseId: string, queuedStep: number, seed: number) {
  const phase = PIPELINE_PHASES.find((p) => p.id === phaseId);
  const suggestions = suggestionsFromBotReport();
  const urls = researchUrlsForPhase(phaseId);
  const manifest = buildManifest(hubRoot, phaseId);
  const allJsonPaths = pathsForPhase(phaseId, manifest.files);

  const planSteps = [
    {
      order: 1,
      action: "Read ResearchCache entries cited below (content.md) and execution brief.",
      researchEntryIds: urls.map((u) => u.id),
    },
    {
      order: 2,
      action: "Research each suggestedNextAction using sourceUrl — save 1-line note per URL under ResearchCache if new.",
      suggestions,
    },
    {
      order: 3,
      action:
        "Read CaveBuildResearchAgentPrompt.md (ONE consolidated research — summaries only, max 5 images). Use unified manifest + phase digest for active-phase JSON pointers; do not paste full cache JSON.",
      jsonPaths: [
        RESEARCH_AGENT_PROMPT_REL,
        UNIFIED_AGENT_PROMPT_REL,
        JSON_MANIFEST_REL,
        PHASE_DATA_DIGEST_REL,
        ACTIVE_PHASE_PROMPT_REL,
        ...allJsonPaths.slice(0, 8),
      ],
    },
    {
      order: 4,
      action: phase?.focus ?? "Apply minimal kit fix for this phase only.",
      jsonPaths: phase?.jsonPaths ?? [],
    },
    {
      order: 5,
      action: "Execute plan; re-run phase bot; update CaveBuildPhaseBotReport.json.",
      botReport: "Assets/EnvironmentKit/Generated/CaveBuildPhaseBotReport.json",
    },
  ];

  return {
    generatedUtc: new Date().toISOString(),
    phaseId,
    queuedStep,
    seed,
    generatedJsonFileCount: manifest.fileCount,
    generatedJsonPaths: allJsonPaths,
    workflow: ["research_urls", "research_suggestions", "write_plan", "execute_plan", "bot_rereport"],
    suggestedNextActions: suggestions.length
      ? suggestions
      : [
          `Complete phase '${phaseId}' focus: ${phase?.focus ?? "see pipeline-phases.ts"}`,
          "Run surface route bot before cave route if ground_placement/terrain.",
        ],
    doNot: [
      "Do not rebuild entire cave for surface-only bot failures.",
      "Do not skip ResearchCache URLs — plan is invalid without opened content.md files.",
      "Do not radiate-replace main land center (~45% preserve disk).",
      "Do not apply compile_gate fixes for staleErrorCount-only diagnostics.",
    ],
    researchUrlsToOpen: urls,
    planSteps,
    webSearchQueries: phase?.webSearchQueries ?? [],
  };
}

function writeNextStepsMd(phaseId: string, plan: ReturnType<typeof buildPlan>) {
  const lines = [
    `# Next Steps — ${phaseId}`,
    "",
    MANDATORY_BUILD_RULES_MD,
    "",
    "## Suggested next actions (research these first)",
    "",
  ];
  for (const s of plan.suggestedNextActions) lines.push(`- ${s}`);
  lines.push("", "## Research URLs (open before C#)", "");
  for (const u of plan.researchUrlsToOpen) {
    lines.push(`- **${u.id}**: ${u.url}`);
    lines.push(`  - Local: \`${u.localMd}\``);
  }
  lines.push(
    "",
    "## Consolidated research prompt (read before C#)",
    `- \`${hubRoot}/${RESEARCH_AGENT_PROMPT_REL}\``,
    "",
    "## Plan steps",
    ""
  );
  for (const step of plan.planSteps) {
    lines.push(`${step.order}. ${step.action}`);
  }
  writeFileSync(join(gen, "CaveBuildNextStepsPrompt.md"), lines.join("\n"), "utf8");

  const doNot = [
    `# DO NOT — ${phaseId}`,
    "",
    ...plan.doNot.map((d) => `- ${d}`),
    "",
    "## Until plan is executed",
    "- Do not start another meat pass or full rebuild.",
    "- Do not ignore failing surface route bot — fix trails/mouth first.",
  ];
  writeFileSync(join(gen, "CaveBuildDoNotPrompt.md"), doNot.join("\n"), "utf8");
}

function main() {
  const nextOnly = process.argv.includes("--next-steps-only");
  const phaseId = process.env.CAVE_ACTIVE_PHASE?.trim() || "research";
  const queuedStep = parseInt(process.env.CAVE_QUEUED_STEP ?? "-1", 10) || -1;
  const seed = parseInt(process.env.CAVE_BUILD_SEED ?? "0", 10) || 0;

  mkdirSync(gen, { recursive: true });
  const plan = buildPlan(phaseId, queuedStep, seed);
  writeFileSync(
    join(gen, "CaveBuildResearchActionPlan.json"),
    JSON.stringify(plan, null, 2),
    "utf8"
  );
  writeNextStepsMd(phaseId, plan);

  if (nextOnly) {
    console.log(`[CaveCursor:info] Next/DO NOT prompts for ${phaseId}`);
    return;
  }
  console.log(
    `[CaveCursor:info] Research action plan: ${plan.researchUrlsToOpen.length} URLs, ${plan.suggestedNextActions.length} suggestions`
  );
}

main();
