import { readFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const generated = "Assets/EnvironmentKit/Generated";

function loadJson<T>(hubRoot: string, rel: string): T | null {
  const path = join(hubRoot.replace(/\/$/, ""), rel);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch {
    return null;
  }
}

export type WorkflowContext = {
  currentPhase?: string;
  nextRequiredPhase?: string;
  hasBlockingCompileErrors?: boolean;
  checklist?: { id: string; label: string; phase: string; checked: boolean }[];
};

export type AgentMemoryFile = {
  entries?: {
    phase: string;
    rung: string;
    fingerprint: string;
    message: string;
    resolved?: boolean;
  }[];
};

export type CompileDiagnostics = {
  hasCompileErrors?: boolean;
  errorCount?: number;
  errors?: { code: string; file: string; line: number; message: string }[];
};

export function readPostBuildWorkflowMd(): string {
  const path = join(dirname(fileURLToPath(import.meta.url)), "prompt-ladder/post-build-workflow.md");
  if (!existsSync(path)) return "";
  return readFileSync(path, "utf8").trim();
}

export function readPreBuildWorkflowMd(): string {
  const path = join(dirname(fileURLToPath(import.meta.url)), "prompt-ladder/pre-build-workflow.md");
  if (!existsSync(path)) return "";
  return readFileSync(path, "utf8").trim();
}

export type PreBuildWorkflowContext = {
  workflow?: string;
  currentPhase?: string;
  nextRequiredPhase?: string;
  preBuildAcceptable?: boolean;
  preBuildScore?: number;
  preBuildGrade?: string;
  hasBlockingCompileErrors?: boolean;
  checklist?: { id: string; label: string; phase: string; checked: boolean }[];
};

export function formatWorkflowAndMemoryBlock(activeRung: string, hubRoot: string): string {
  const wf = loadJson<WorkflowContext>(hubRoot, `${generated}/CaveBuildWorkflowContext.json`);
  const mem = loadJson<AgentMemoryFile>(hubRoot, `${generated}/CaveBuildAgentMemory.json`);
  const compile = loadJson<CompileDiagnostics>(hubRoot, `${generated}/CaveBuildCompileDiagnostics.json`);

  const lines: string[] = [
    readPostBuildWorkflowMd(),
    "",
    "## Workflow state (from disk)",
    "",
  ];

  if (wf) {
    lines.push(`- **currentPhase**: ${wf.currentPhase ?? "?"}`);
    lines.push(`- **nextRequiredPhase**: ${wf.nextRequiredPhase ?? "?"}`);
    lines.push(`- **hasBlockingCompileErrors**: ${wf.hasBlockingCompileErrors ?? false}`);
    if (wf.checklist?.length) {
      lines.push("", "### Checklist (mark [x] in your plan as you complete each)");
      for (const c of wf.checklist) {
        const block =
          activeRung === "compile_gate" || c.phase === "compile_gate"
            ? c.phase === "ladder_fixes" && (compile?.hasCompileErrors ?? false)
            : false;
        lines.push(
          `- [ ] ${c.id}: ${c.label}${block ? " **(BLOCKED until compile clean)**" : ""}`
        );
      }
    }
  } else {
    lines.push("- CaveBuildWorkflowContext.json missing — run Build Complete Cave in Unity first.");
  }

  lines.push("", "## Recorded failures — DO NOT REPEAT");
  const open = (mem?.entries ?? []).filter((e) => !e.resolved).slice(0, 12);
  if (!open.length) {
    lines.push("- (none open)");
  } else {
    for (const e of open) {
      lines.push(`- [ ] \`${e.fingerprint}\` (${e.phase}/${e.rung}): ${e.message}`);
    }
  }

  if (activeRung === "compile_gate" && compile) {
    lines.push("", "## Compile diagnostics (fix ALL before any scene work)");
    lines.push(`- **errorCount**: ${compile.errorCount ?? 0}`);
    for (const err of (compile.errors ?? []).slice(0, 15)) {
      lines.push(`- **${err.code}** ${err.file}:${err.line} — ${err.message}`);
    }
    if ((compile.errorCount ?? 0) > 0) {
      lines.push(
        "",
        "**You may ONLY edit** `Packages/com.cursor.environment-authoring-kit/` C# until errorCount is 0. Tie each fix to your research plan step."
      );
    }
  }

  lines.push(
    "",
    "Paths:",
    `- ${generated}/CaveBuildWorkflowContext.json`,
    `- ${generated}/CaveBuildCompileDiagnostics.json`,
    `- ${generated}/CaveBuildAgentMemory.json`,
    `- ${generated}/CaveBuildResearch.json`,
    ""
  );

  return lines.join("\n");
}

/** Maps Cursor invoke rung → workflow checklist phase (readiness rungs share readiness_ladder). */
function preBuildChecklistPhaseForRung(rung: string): string {
  if (rung === "research" || rung === "plan" || rung === "compile_gate") return rung;
  return "readiness_ladder";
}

export function formatPreBuildWorkflowAndMemoryBlock(activeRung: string, hubRoot: string): string {
  const wf = loadJson<PreBuildWorkflowContext>(
    hubRoot,
    `${generated}/CaveBuildPreBuildWorkflowContext.json`
  );
  const mem = loadJson<AgentMemoryFile>(hubRoot, `${generated}/CaveBuildAgentMemory.json`);
  const compile = loadJson<CompileDiagnostics>(hubRoot, `${generated}/CaveBuildCompileDiagnostics.json`);

  const lines: string[] = [
    readPreBuildWorkflowMd(),
    "",
    "## Pre-build workflow state (from disk)",
    "",
  ];

  const checklistPhase = preBuildChecklistPhaseForRung(activeRung);

  if (wf) {
    lines.push(`- **workflow**: ${wf.workflow ?? "pre_build"}`);
    lines.push(`- **activeCursorRung**: ${activeRung}`);
    lines.push(`- **currentPhase**: ${wf.currentPhase ?? "?"}`);
    lines.push(`- **nextRequiredPhase**: ${wf.nextRequiredPhase ?? "?"}`);
    lines.push(`- **preBuildAcceptable**: ${wf.preBuildAcceptable ?? false}`);
    lines.push(`- **preBuildGrade**: ${wf.preBuildGrade ?? "?"} (${wf.preBuildScore ?? 0}/100)`);
    lines.push(`- **hasBlockingCompileErrors**: ${wf.hasBlockingCompileErrors ?? false}`);
    lines.push(
      "",
      `### Checklist for THIS pass only (\`${checklistPhase}\` / rung \`${activeRung}\`)`,
      "Do not work on other phases in this invoke — Unity runs the next phase automatically."
    );
    if (wf.checklist?.length) {
      for (const c of wf.checklist) {
        if (c.phase !== checklistPhase) continue;
        const block =
          c.phase === "readiness_ladder" && (compile?.hasCompileErrors ?? false);
        lines.push(
          `- [ ] ${c.id}: ${c.label}${block ? " **(BLOCKED until compile clean)**" : ""}`
        );
      }
    }
  } else {
    lines.push("- CaveBuildPreBuildWorkflowContext.json missing — run pre-build gate in Unity first.");
  }

  lines.push("", "## Recorded failures — DO NOT REPEAT");
  const open = (mem?.entries ?? []).filter((e) => !e.resolved).slice(0, 12);
  if (!open.length) {
    lines.push("- (none open)");
  } else {
    for (const e of open) {
      lines.push(`- [ ] \`${e.fingerprint}\` (${e.phase}/${e.rung}): ${e.message}`);
    }
  }

  if (activeRung === "compile_gate" && compile) {
    lines.push("", "## Compile diagnostics");
    lines.push(`- **errorCount**: ${compile.errorCount ?? 0}`);
    if ((compile.errorCount ?? 0) === 0) {
      lines.push(
        "",
        "**COMPILE CLEAN:** No C# fixes needed. Do not edit files or re-run research/plan/ladder in this pass.",
        "Last line must be: `[CaveCursor:phase-complete] workflow=pre_build rung=compile_gate reason=no_errors`"
      );
    } else {
      lines.push("", "Fix every error below (package C# only). Tie each fix to your plan step.");
      for (const err of (compile.errors ?? []).slice(0, 15)) {
        lines.push(`- **${err.code}** ${err.file}:${err.line} — ${err.message}`);
      }
    }
  }

  lines.push(
    "",
    "Paths:",
    `- ${generated}/CaveBuildPreBuildWorkflowContext.json`,
    `- ${generated}/CaveBuildPreBuildLadderReport.json`,
    `- ${generated}/CaveBuildPreBuildLadderContext.json`,
    `- ${generated}/CaveBuildCompileDiagnostics.json`,
    `- ${generated}/CaveBuildAgentMemory.json`,
    `- ${generated}/CaveBuildResearch.json`,
    ""
  );

  return lines.join("\n");
}
