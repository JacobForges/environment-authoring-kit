import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { randomBytes } from "node:crypto";
import { formatResearchExecutionBlock } from "./research-execution-brief.js";
import type {
  FailingStagesDoc,
  LadderContextDoc,
  PromptRung,
  QualityReport,
  VisualShellAudit,
} from "./prompt-ladder.js";

const generated = "Assets/EnvironmentKit/Generated";

export type AgentSessionDoc = {
  sessionId?: string;
  generatedUtc?: string;
  clearedFiles?: string[];
  requiredJson?: string[];
};

export type RouteProbeDoc = {
  passed?: boolean;
  pathSteps?: number;
  issues?: {
    code?: string;
    message?: string;
    suggestedStageId?: string;
    pathIndex?: number;
    worldPosition?: number[];
  }[];
};

export type CombatProbeDoc = {
  passed?: boolean;
  issues?: { code?: string; message?: string; suggestedStageId?: string }[];
};

export type CompileDiagnostics = {
  hasCompileErrors?: boolean;
  errorCount?: number;
  errors?: { code: string; file: string; line: number; message: string }[];
};

function loadJson<T>(hubRoot: string, rel: string): T | null {
  const path = join(hubRoot.replace(/\/$/, ""), rel);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch {
    return null;
  }
}

function newSessionId(): string {
  return randomBytes(6).toString("hex");
}

/** Unique, issue-specific instructions for this invoke (not a generic repeat). */
export function buildTailoredFixBrief(options: {
  hubRoot: string;
  activeRung: PromptRung;
  report: QualityReport;
  visualShell: VisualShellAudit | null;
  failingStages: FailingStagesDoc | null;
  ladderContext: LadderContextDoc | null;
  meatLoopPass?: number;
}): string {
  const {
    hubRoot,
    activeRung,
    report,
    visualShell,
    failingStages,
    ladderContext,
    meatLoopPass = ladderContext?.meatLoopPass ?? -1,
  } = options;

  const session =
    loadJson<AgentSessionDoc>(hubRoot, `${generated}/CaveBuildAgentSession.json`) ?? {};
  const sessionId = session.sessionId ?? newSessionId();
  const routeProbe = loadJson<RouteProbeDoc>(hubRoot, `${generated}/CaveBuildRouteProbe.json`);
  const combatProbe = loadJson<CombatProbeDoc>(hubRoot, `${generated}/CaveBuildCombatProbe.json`);
  const compile = loadJson<CompileDiagnostics>(
    hubRoot,
    `${generated}/CaveBuildCompileDiagnostics.json`
  );

  const execBlock = formatResearchExecutionBlock(hubRoot, activeRung);

  const lines: string[] = [
    "## THIS PASS ONLY — tailored fix brief",
    "",
    `**Session:** \`${sessionId}\` | **UTC:** ${new Date().toISOString()} | **Rung:** \`${activeRung}\` | **Meat pass:** ${meatLoopPass}`,
    "",
    "**Do not** reuse fixes from a prior session. Stale prompts were deleted; only read JSON listed in `CaveBuildAgentSession.json` → `requiredJson`.",
    "",
    "**MANDATORY:** Use `ResearchCache/` data and images in your **plan** and **C# fixes** — cite entry paths you opened.",
    "",
  ];

  if (execBlock) {
    lines.push(execBlock);
    lines.push("");
  }

  if (session.clearedFiles?.length) {
    lines.push("Cleared before this invoke (ignore old content):");
    for (const f of session.clearedFiles.slice(0, 12)) lines.push(`- \`${f}\``);
    lines.push("");
  }

  const tasks: string[] = [];

  if (compile?.hasCompileErrors && (compile.errorCount ?? 0) > 0) {
    for (const err of (compile.errors ?? []).slice(0, 8)) {
      tasks.push(
        `[compile_gate] Fix **${err.code}** in \`${err.file}:${err.line}\` — ${err.message}`
      );
    }
  }

  const failing = report.stages.filter((s) => s.score < 95);
  for (const s of failing) {
    const issue = (s.issues ?? [])[0] ?? "below threshold";
    const fix = (s.fixes ?? [])[0];
    tasks.push(
      `[${s.id}] score ${s.score}/100 — ${issue}${fix ? ` → **Fix:** ${fix}` : ""}`
    );
  }

  for (const issue of routeProbe?.issues ?? []) {
    tasks.push(
      `[route_probe/${issue.suggestedStageId ?? "path"}] ${issue.message ?? issue.code ?? "route issue"}`
    );
  }

  for (const issue of combatProbe?.issues ?? []) {
    tasks.push(
      `[combat_probe/${issue.suggestedStageId ?? "combat"}] ${issue.message ?? issue.code ?? "combat issue"}`
    );
  }

  if (report.dudReasons?.length) {
    for (const d of report.dudReasons.slice(0, 4)) tasks.push(`[dud] ${d}`);
  }

  if (visualShell?.issues?.length && activeRung === "visual_shell") {
    for (const v of visualShell.issues.slice(0, 6)) tasks.push(`[visual_shell] ${v}`);
  }

  if (failingStages?.stages?.length) {
    for (const s of failingStages.stages.slice(0, 5)) {
      const top = (s.issues ?? [])[0];
      if (top) tasks.push(`[failing_stages/${s.id}] ${top}`);
    }
  }

  const deduped = [...new Set(tasks)].slice(0, 24);
  if (!deduped.length) {
    lines.push("- No open issues in live JSON — verify grade report; if Ship target met, exit without edits.");
  } else {
    lines.push("### Action list (complete in order; stop when rung passes)");
    deduped.forEach((t, i) => lines.push(`${i + 1}. ${t}`));
  }

  lines.push("");
  lines.push("### Required JSON (read before editing)");
  const required = session.requiredJson?.length
    ? session.requiredJson
    : [
        `${generated}/CaveBuildQualityReport.json`,
        `${generated}/CaveBuildResearchCache.json`,
        `${generated}/CaveBuildResearchExecutionBrief.json`,
        `Assets/EnvironmentKit/ResearchCache/index.json`,
        `${generated}/CaveBuildAgentSession.json`,
        `${generated}/CaveBuildRouteProbe.json`,
        `${generated}/CaveBuildCombatProbe.json`,
      ];
  for (const p of required) {
    const full = `${hubRoot}/${p}`.replace(/\\/g, "/");
    lines.push(`- \`${full}\``);
  }

  return lines.join("\n");
}
