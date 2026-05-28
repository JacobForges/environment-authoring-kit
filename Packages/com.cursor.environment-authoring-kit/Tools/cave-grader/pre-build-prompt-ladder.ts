import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { formatResearchSourcesBlock } from "./research-sources.js";
import type { PromptRung } from "./prompt-ladder.js";
import { formatPreBuildWorkflowAndMemoryBlock } from "./workflow-prompt.js";
import { PHASE_COMPLETE_PREFIX } from "./phase-flags.js";

const ladderDir = join(dirname(fileURLToPath(import.meta.url)), "prompt-ladder");

export const PRE_BUILD_RUNG_ORDER = [
  "research",
  "plan",
  "compile_gate",
  "package_tooling",
  "scene_ground",
  "prefab_catalog",
  "cursor_api",
  "research_manifest",
  "scene_portal",
  "prior_cave_state",
] as const;

export type PreBuildPromptRung = (typeof PRE_BUILD_RUNG_ORDER)[number];

export type PreBuildStageRow = {
  id: string;
  name?: string;
  score: number;
  weight?: number;
  critical?: boolean;
  passed?: boolean;
  issues?: string[];
  fixes?: string[];
};

export type PreBuildReport = {
  scene?: string;
  letterGrade: string;
  overallScore: number;
  buildAcceptable: boolean;
  layoutSeed?: number;
  layoutPrototype?: boolean;
  stages: PreBuildStageRow[];
};

export type PreBuildLadderContextDoc = {
  activeRung?: string;
  failingRungs?: string[];
  overallScore?: number;
  letterGrade?: string;
  buildAcceptable?: boolean;
};

const STAGE_PASS = 92;

function readRungMarkdown(rung: string): string {
  const path = join(ladderDir, `rung-${rung}.md`);
  if (!existsSync(path)) return "";
  return readFileSync(path, "utf8").trim();
}

function readCommonRules(): string {
  const path = join(ladderDir, "common-rules.md");
  if (!existsSync(path)) return "";
  return readFileSync(path, "utf8").trim();
}

function readResearchWorkflow(): string {
  const path = join(ladderDir, "research-workflow.md");
  if (!existsSync(path)) return "";
  return readFileSync(path, "utf8").trim();
}

/** Map pre-build readiness rung → post-build research affinity for paper subset. */
function researchRungForPrompt(rung: string): PromptRung {
  const map: Record<string, PromptRung> = {
    research: "research",
    plan: "research",
    compile_gate: "compile_gate",
    package_tooling: "other",
    scene_ground: "ground_placement",
    prefab_catalog: "visual_shell",
    cursor_api: "other",
    research_manifest: "research",
    scene_portal: "ground_placement",
    scene_portal_alt: "ground_placement",
    prior_cave_state: "visual_shell",
  };
  return map[rung] ?? "other";
}

export function isPreBuildRungFailing(rung: string, report: PreBuildReport): boolean {
  if (rung === "research" || rung === "plan") return false;
  const row = report.stages.find((s) => s.id === rung);
  if (!row) return rung !== "research" && rung !== "plan";
  return !row.passed || row.score < STAGE_PASS;
}

export function pickActivePreBuildRung(
  report: PreBuildReport,
  overrideRung?: string | null,
  skipRungs: Set<string> = new Set()
): string {
  if (overrideRung && PRE_BUILD_RUNG_ORDER.includes(overrideRung as PreBuildPromptRung)) {
    return overrideRung;
  }

  let worstCritical: PreBuildStageRow | null = null;
  let worst: PreBuildStageRow | null = null;

  for (const s of report.stages) {
    if (skipRungs.has(s.id)) continue;
    if (s.passed && s.score >= STAGE_PASS) continue;
    if (s.critical) {
      if (
        !worstCritical ||
        s.score < worstCritical.score ||
        (s.score === worstCritical.score && (s.weight ?? 0) > (worstCritical.weight ?? 0))
      ) {
        worstCritical = s;
      }
    } else if (!worst || s.score < worst.score) {
      worst = s;
    }
  }

  if (worstCritical) return worstCritical.id;
  if (worst) return worst.id;
  return "compile_gate";
}

export function parseWorkflowArg(
  argv: string[]
): "pre_build" | "post_build" | "terrain" | null {
  const env = process.env.CAVE_WORKFLOW?.trim();
  if (env === "pre_build" || env === "pre-build") return "pre_build";
  if (env === "post_build" || env === "post-build") return "post_build";
  if (env === "terrain") return "terrain";
  for (const arg of argv) {
    if (arg.startsWith("--workflow=")) {
      const v = arg.slice("--workflow=".length).trim().replace(/-/g, "_");
      if (v === "pre_build") return "pre_build";
      if (v === "post_build") return "post_build";
      if (v === "terrain") return "terrain";
    }
  }
  return null;
}

export function buildPreBuildLadderPrompt(options: {
  hubRoot: string;
  report: PreBuildReport;
  ladderContext: PreBuildLadderContextDoc | null;
  activeRung: string;
  reportPath: string;
}): string {
  const { hubRoot, report, ladderContext, activeRung, reportPath } = options;
  const researchRung = researchRungForPrompt(activeRung);

  const lines: string[] = [
    "You are preparing a Unity cave **pre-build readiness gate** for com.cursor.environment-authoring-kit.",
    `Hub root: ${hubRoot}`,
    `Active rung: **${activeRung}** (no cave geometry generated yet).`,
    `Pre-build grade: ${report.letterGrade} (${report.overallScore}/100), acceptable=${report.buildAcceptable}`,
    "",
    "**THIS INVOKE:** Complete ONLY the active rung above. Unity chains research → plan → compile_gate → readiness rungs in separate agent passes.",
    "Ignore checklist items for other phases. Do not jump ahead to readiness_ladder while rung is compile_gate.",
    "",
    formatPreBuildWorkflowAndMemoryBlock(activeRung, hubRoot),
    "",
    readResearchWorkflow(),
    "",
    formatResearchSourcesBlock(researchRung, 0),
    "",
    readCommonRules(),
    "",
    readRungMarkdown(activeRung),
    "",
  ];

  const failing = report.stages.filter((s) => !s.passed || s.score < STAGE_PASS);
  if (failing.length) {
    lines.push("Failing pre-build stages:");
    for (const s of failing.slice(0, 8)) {
      lines.push(
        `- ${s.id} (${s.score}${s.critical ? ", critical" : ""}): ${(s.issues ?? [])[0] ?? "—"}`
      );
      const fix = (s.fixes ?? [])[0];
      if (fix) lines.push(`  - Fix: ${fix}`);
    }
    lines.push("");
  }

  if (activeRung === "scene_ground" && ladderContext) {
    lines.push("Pre-build ladder context:");
    lines.push(JSON.stringify(ladderContext, null, 2));
    lines.push("");
  }

  lines.push(
    "Full JSON on disk:",
    `- ${reportPath}`,
    `- Assets/EnvironmentKit/Generated/CaveBuildPreBuildLadderContext.json`,
    `- Assets/EnvironmentKit/Generated/CaveBuildPreBuildWorkflowContext.json`,
    `- Assets/EnvironmentKit/Generated/CaveBuildCompileDiagnostics.json`,
    `- Assets/EnvironmentKit/Generated/CaveBuildAgentMemory.json`,
    `- Assets/EnvironmentKit/Generated/CaveBuildResearch.json`,
    "",
    "## Phase complete (REQUIRED — Unity auto-advances)",
    "",
    "When this rung is done (or nothing to change), your **last line of output** must be exactly:",
    `\`${PHASE_COMPLETE_PREFIX} workflow=pre_build rung=${activeRung} reason=done\``,
    "",
    "Do **not** tell the user to re-run pre-build gate or Build Complete Cave in this pass — Unity chains the next phase automatically.",
    "Do **not** work on other workflow phases in this invoke."
  );

  return lines.filter((l) => l.length > 0).join("\n");
}

export function parsePreBuildRungArg(argv: string[]): string | null {
  for (const arg of argv) {
    if (arg.startsWith("--rung=")) return arg.slice("--rung=".length).trim();
  }
  return process.env.CAVE_CURSOR_RUNG?.trim() || null;
}
