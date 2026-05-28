import { readFileSync, existsSync, writeFileSync, mkdirSync } from "node:fs";
import { join, normalize, relative, resolve, dirname } from "node:path";
import { Agent, CursorAgentError } from "@cursor/sdk";
import type { SDKMessage } from "@cursor/sdk";
import { loadCaveGraderEnv } from "./load-env.js";
import {
  buildLadderPrompt,
  parseRungArg,
  pickActiveRung,
  type FailingStagesDoc,
  type LadderContextDoc,
  type PromptRung,
  type QualityReport,
  type VisualShellAudit,
} from "./prompt-ladder.js";
import {
  buildPreBuildLadderPrompt,
  isPreBuildRungFailing,
  parsePreBuildRungArg,
  parseWorkflowArg,
  pickActivePreBuildRung,
  type PreBuildLadderContextDoc,
  type PreBuildReport,
} from "./pre-build-prompt-ladder.js";
import {
  buildTerrainLadderPrompt,
  isTerrainRungFailing,
  parseTerrainRungArg,
  pickActiveTerrainRung,
  type TerrainLadderContextDoc,
  type TerrainQualityReport,
} from "./terrain-prompt-ladder.js";
import { emitPhaseComplete, type WorkflowMode } from "./phase-flags.js";
import { sleep } from "./pacing.js";
import {
  formatResearchExecutionBlock,
  writeResearchExecutionBrief,
} from "./research-execution-brief.js";
import { buildResearchManifestJson } from "./research-sources.js";
import { phaseForRung } from "./pipeline-phases.js";
import { ACTIVE_PHASE_PROMPT_REL } from "./agent-artifact-paths.js";
import {
  formatPromptHarmonyPrelude,
  promptAlreadyHasHarmony,
} from "./prompt-coherence.js";

function prependAutonomousPromptBlocks(
  prompt: string,
  activeRung: string,
  isTerrain: boolean
): string {
  const parts: string[] = [];
  if (!promptAlreadyHasHarmony(prompt)) {
    parts.push(formatPromptHarmonyPrelude(activeRung, isTerrain ? "terrain" : "cave"));
  }
  for (const name of ["CaveBuildDoNotPrompt.md", "CaveBuildNextStepsPrompt.md"]) {
    const p = join(generated, name);
    if (!existsSync(p)) continue;
    const body = readFileSync(p, "utf8");
    if (prompt.includes(name.replace(".md", ""))) continue;
    parts.push(body);
  }
  const phase = phaseForRung(activeRung as PromptRung);
  if (phase) {
    const phasePath = join(hubRoot, ACTIVE_PHASE_PROMPT_REL);
    if (existsSync(phasePath) && !prompt.includes(ACTIVE_PHASE_PROMPT_REL)) {
      parts.push(readFileSync(phasePath, "utf8"));
    }
  }
  if (parts.length === 0) return prompt;
  return `${parts.join("\n\n---\n\n")}\n\n---\n\n${prompt}`;
}

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../../..")).replace(
  /\/$/,
  ""
);
const generated = join(hubRoot, "Assets/EnvironmentKit/Generated");
const reportPath = join(generated, "CaveBuildQualityReport.json");
const preBuildReportPath = join(generated, "CaveBuildPreBuildLadderReport.json");
const visualShellPath = join(generated, "CaveBuildVisualShellAudit.json");
const failingStagesPath = join(generated, "CaveBuildFailingStages.json");
const ladderContextPath = join(generated, "CaveBuildLadderContext.json");
const preBuildLadderContextPath = join(generated, "CaveBuildPreBuildLadderContext.json");
const terrainReportPath = join(generated, "SurfaceTerrainQualityReport.json");
const terrainLadderContextPath = join(generated, "SurfaceTerrainBuildLadderContext.json");
const terrainPromptExportPath = join(generated, "TerrainBuildTailoredAgentPrompt.md");
const researchJsonPath = join(generated, "CaveBuildResearch.json");
const promptExportPath = join(generated, "CaveBuildTailoredAgentPrompt.md");
const useExportedPrompt =
  !process.argv.includes("--no-exported-prompt") &&
  process.env.CAVE_USE_EXPORTED_PROMPT !== "0" &&
  (process.argv.includes("--use-exported-prompt") ||
    process.env.CAVE_USE_EXPORTED_PROMPT === "1" ||
    process.env.CAVE_HARDCODED_PROMPTS !== "0");
const includeLive = process.argv.includes("--live");
const forceCloud = process.argv.includes("--cloud");
const useAuto =
  process.argv.includes("--auto") ||
  (!forceCloud && !process.argv.includes("--local-only"));

const workflowMode = parseWorkflowArg(process.argv) ?? "post_build";
const isPreBuild = workflowMode === "pre_build";
const isTerrain = workflowMode === "terrain";

function loadJson<T>(path: string): T | null {
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    console.error(`Invalid JSON in ${path}: ${msg}`);
    throw err;
  }
}

let thinkingBuffer = "";

function flushThinking(force = false): void {
  const text = thinkingBuffer.trim();
  if (!text) return;
  const endsSentence = /[.!?]$/.test(text);
  if (!force && !endsSentence && text.length < 160) return;
  console.error(`[CaveCursor:thinking] ${text}`);
  thinkingBuffer = "";
}

function logStreamEvent(event: SDKMessage): void {
  if (event.type === "assistant") {
    flushThinking(true);
    for (const block of event.message.content) {
      if (block.type === "text" && block.text?.trim()) {
        const chunk = block.text.trim().replace(/\s+/g, " ");
        console.log(`[CaveCursor:assistant] ${chunk.slice(0, 400)}`);
      }
    }
    return;
  }

  if (event.type === "status") {
    flushThinking(true);
    console.error(
      `[CaveCursor:status] ${event.status}${event.message ? `|${event.message}` : ""}`
    );
    return;
  }

  if (event.type === "tool_call") {
    flushThinking(true);
    const detail =
      typeof event.result === "string"
        ? event.result.slice(0, 200)
        : event.result != null
          ? JSON.stringify(event.result).slice(0, 200)
          : "";
    console.error(`[CaveCursor:tool] ${event.status}|${event.name}|${detail}`);
    return;
  }

  if (event.type === "thinking" && event.text) {
    thinkingBuffer += event.text;
    flushThinking(false);
  }
}

async function dumpLiveRunFailure(
  run: Awaited<ReturnType<Awaited<ReturnType<typeof Agent.create>>["send"]>>,
  result: { id: string; status: string; result?: string }
): Promise<void> {
  console.error("Run status:", result.status, result.id);
  if (result.result) console.error("Run result:\n", result.result);

  if (run.supports("conversation")) {
    try {
      const turns = await run.conversation();
      const tail = turns.slice(-6);
      console.error(`Conversation (${tail.length} turns, tail):`);
      for (const t of tail) {
        console.error(JSON.stringify(t, null, 2).slice(0, 6000));
      }
    } catch (e) {
      console.error("conversation() failed:", e);
    }
  } else {
    console.error("conversation unsupported:", run.unsupportedReason("conversation"));
  }
}

type RunResult = Awaited<ReturnType<typeof Agent.prompt>>;
type ProviderRunResult = {
  status: "finished" | "error" | "cancelled";
  id: string;
  result?: string;
};

function resolveProvider(): string {
  const fromEnv = process.env.CAVE_AI_PROVIDER?.trim();
  return fromEnv && fromEnv.length > 0 ? fromEnv : "Cursor";
}

function lastRunDiagnosticsPath(workflow: WorkflowMode): string {
  if (workflow === "terrain") return join(generated, "TerrainBuildCursorLastRun.json");
  return join(generated, "CaveBuildCursorLastRun.json");
}

function writeLastRunDiagnostics(
  workflow: WorkflowMode,
  payload: Record<string, unknown>
): void {
  try {
    writeFileSync(
      lastRunDiagnosticsPath(workflow),
      JSON.stringify(
        { generatedUtc: new Date().toISOString(), workflow, ...payload },
        null,
        2
      ),
      "utf8"
    );
  } catch {
    /* ignore */
  }
}

type ExternalEdit = {
  path: string;
  op: "replace" | "append" | "write";
  oldText?: string;
  newText: string;
};

type ExternalEditPayload = {
  summary?: string;
  edits?: ExternalEdit[];
};

function parseExecutionPayload(raw: string): ExternalEditPayload | null {
  const block = raw.match(/```json\s*([\s\S]*?)```/i);
  const jsonText = block?.[1]?.trim() || raw.trim();
  if (!jsonText) return null;
  try {
    const parsed = JSON.parse(jsonText) as ExternalEditPayload;
    if (!parsed || !Array.isArray(parsed.edits)) return null;
    return parsed;
  } catch {
    return null;
  }
}

function isPathAllowed(relPath: string): boolean {
  const p = relPath.replace(/\\/g, "/");
  return (
    p.startsWith("Packages/com.cursor.environment-authoring-kit/") ||
    p.startsWith("Assets/EnvironmentKit/") ||
    p === "README.md" ||
    p.startsWith("docs/")
  );
}

function applyExecutionPayloadIfEnabled(
  payload: ExternalEditPayload | null,
  hubRootPath: string
): { applied: number; skipped: number; notes: string[] } {
  const notes: string[] = [];
  if (!payload || !payload.edits || payload.edits.length === 0)
    return { applied: 0, skipped: 0, notes: ["No structured edits found in provider response."] };

  const enabled = process.env.CAVE_EXTERNAL_APPLY_EDITS === "1";
  const dryRun = process.env.CAVE_EXTERNAL_APPLY_DRY_RUN !== "0";
  if (!enabled) {
    return {
      applied: 0,
      skipped: payload.edits.length,
      notes: [
        "Execution layer disabled (CAVE_EXTERNAL_APPLY_EDITS=0).",
        `Received ${payload.edits.length} edits; none applied.`,
      ],
    };
  }

  let applied = 0;
  let skipped = 0;
  for (const edit of payload.edits) {
    if (!edit || !edit.path || !edit.newText || !edit.op) {
      skipped++;
      notes.push("Skipped malformed edit entry.");
      continue;
    }
    if (!isPathAllowed(edit.path)) {
      skipped++;
      notes.push(`Skipped disallowed path: ${edit.path}`);
      continue;
    }

    const targetAbs = resolve(hubRootPath, edit.path);
    const rel = relative(hubRootPath, targetAbs);
    if (rel.startsWith("..")) {
      skipped++;
      notes.push(`Skipped path escaping repo root: ${edit.path}`);
      continue;
    }

    const normalizedAbs = normalize(targetAbs);
    const parent = dirname(normalizedAbs);
    const current = existsSync(normalizedAbs) ? readFileSync(normalizedAbs, "utf8") : "";
    let next = current;
    switch (edit.op) {
      case "replace":
        if (!edit.oldText || !current.includes(edit.oldText)) {
          skipped++;
          notes.push(`Skipped replace; oldText not found: ${edit.path}`);
          continue;
        }
        next = current.replace(edit.oldText, edit.newText);
        break;
      case "append":
        next = current + edit.newText;
        break;
      case "write":
        next = edit.newText;
        break;
      default:
        skipped++;
        notes.push(`Skipped unknown op '${edit.op}' on ${edit.path}`);
        continue;
    }

    if (dryRun) {
      applied++;
      notes.push(`Dry-run apply ${edit.op}: ${edit.path}`);
      continue;
    }

    mkdirSync(parent, { recursive: true });
    writeFileSync(normalizedAbs, next, "utf8");
    applied++;
    notes.push(`Applied ${edit.op}: ${edit.path}`);
  }

  return { applied, skipped, notes };
}

async function main(): Promise<void> {
  const provider = resolveProvider();
  const isCursorProvider = provider.toLowerCase() === "cursor";
  const apiKey = isCursorProvider
    ? process.env.CURSOR_API_KEY?.trim()
    : process.env.CAVE_ACTIVE_API_KEY?.trim() ||
      process.env.OPENAI_API_KEY?.trim() ||
      process.env.ANTHROPIC_API_KEY?.trim() ||
      process.env.GOOGLE_API_KEY?.trim() ||
      process.env.OPENROUTER_API_KEY?.trim() ||
      process.env.CUSTOM_API_KEY?.trim();
  if (!apiKey) {
    const keyHint = isCursorProvider
      ? "CURSOR_API_KEY is required (.env or environment)."
      : `API key required for provider '${provider}' (CAVE_ACTIVE_API_KEY or provider-specific key in .env; set in Hub → Settings).`;
    console.error(keyHint);
    if (isCursorProvider)
      console.error("Unity: Window → Environment Kit → Cave Build → Sync API Key from .env");
    writeLastRunDiagnostics(workflowMode, {
      exitCode: 1,
      error: "missing_api_key",
      provider,
    });
    process.exit(1);
  }

  let prompt: string;
  let activeRung: string;
  let researchJson: string;
  let tailoredExportPath = promptExportPath;

  if (isTerrain) {
    const terrainReport =
      loadJson<TerrainQualityReport>(terrainReportPath) ??
      loadJson<TerrainQualityReport>(join(generated, "SurfaceTerrainBuildLadderReport.json"));
    if (!terrainReport) {
      console.error(
        `Missing ${terrainReportPath}. Run Window → Environment Kit → Terrain Build Grader → Re-grade in Unity.`
      );
      writeLastRunDiagnostics(workflowMode, {
        exitCode: 1,
        error: "missing_terrain_report",
        path: terrainReportPath,
      });
      process.exit(1);
    }

    const terrainContext = loadJson<TerrainLadderContextDoc>(terrainLadderContextPath);
    const overrideRung = parseTerrainRungArg(process.argv);
    activeRung = pickActiveTerrainRung(terrainReport, overrideRung);
    tailoredExportPath = terrainPromptExportPath;

    researchJson = buildResearchManifestJson(
      activeRung,
      terrainReport.scene ?? "unknown",
      terrainReport.seed ?? 0,
      hubRoot
    );
    writeFileSync(researchJsonPath, researchJson, "utf8");

    const tailoredOnDisk = terrainPromptExportPath;
    if (useExportedPrompt && existsSync(tailoredOnDisk)) {
      prompt = readFileSync(tailoredOnDisk, "utf8");
      console.log(
        `[CaveCursor:info] Using pre-exported terrain prompt (${tailoredOnDisk}).`
      );
    } else {
      prompt = buildTerrainLadderPrompt({
        hubRoot,
        report: terrainReport,
        ladderContext: terrainContext,
        activeRung,
        reportPath: terrainReportPath,
      });
    }

    const failing = terrainReport.stages
      .filter((s) => !s.passed || s.score < 90)
      .map((s) => `${s.id}:${s.score}`);
    console.log(
      `[CaveCursor:info] Terrain workflow rung=${activeRung} grade=${terrainReport.letterGrade} (${terrainReport.overallScore}/100)`
    );
    console.log(`[CaveCursor:info] Failing terrain: ${failing.join(", ") || "none"}`);

    if (!isTerrainRungFailing(activeRung, terrainReport)) {
      emitPhaseComplete("terrain", activeRung, "already_passing");
      writeLastRunDiagnostics(workflowMode, { exitCode: 0, rung: activeRung, skipped: true });
      process.exit(0);
    }
  } else if (isPreBuild) {
    const preReport = loadJson<PreBuildReport>(preBuildReportPath);
    if (!preReport) {
      console.error(
        `Missing ${preBuildReportPath}. Run Build Complete Cave (blocked) or Window → Cave Build → Run Pre-Build Gate Only in Unity.`
      );
      process.exit(1);
    }

    const preContext = loadJson<PreBuildLadderContextDoc>(preBuildLadderContextPath);
    const overrideRung = parsePreBuildRungArg(process.argv);
    activeRung = pickActivePreBuildRung(preReport, overrideRung);

    researchJson = buildResearchManifestJson(
      activeRung,
      preReport.scene ?? "unknown",
      0,
      hubRoot
    );
    writeFileSync(researchJsonPath, researchJson, "utf8");

    prompt = buildPreBuildLadderPrompt({
      hubRoot,
      report: preReport,
      ladderContext: preContext,
      activeRung,
      reportPath: preBuildReportPath,
    });

    const failing = preReport.stages
      .filter((s) => !s.passed || s.score < 92)
      .map((s) => `${s.id}:${s.score}`);
    console.log(
      `[CaveCursor:info] Pre-build workflow rung=${activeRung} grade=${preReport.letterGrade} (${preReport.overallScore}/100)`
    );
    console.log(
      `[CaveCursor:info] Failing readiness: ${failing.join(", ") || "none"}`
    );

    if (!isPreBuildRungFailing(activeRung, preReport)) {
      emitPhaseComplete("pre_build", activeRung, "already_passing");
      process.exit(0);
    }
  } else {
    const report = loadJson<QualityReport>(reportPath);
    if (!report) {
      console.error(`Missing ${reportPath}. Build a cave in Unity or Re-grade first.`);
      writeLastRunDiagnostics(workflowMode, {
        exitCode: 1,
        error: "missing_quality_report",
        path: reportPath,
      });
      process.exit(1);
    }

    const visualShell = loadJson<VisualShellAudit>(visualShellPath);
    const failingStages = loadJson<FailingStagesDoc>(failingStagesPath);
    const ladderContext = loadJson<LadderContextDoc>(ladderContextPath);
    const overrideRung = parseRungArg(process.argv);
    activeRung = pickActiveRung(report, visualShell, ladderContext, overrideRung);

    researchJson = buildResearchManifestJson(
      activeRung,
      report.scene ?? ladderContext?.scene ?? "unknown",
      ladderContext?.meatLoopPass ?? -1,
      hubRoot
    );
    writeFileSync(researchJsonPath, researchJson, "utf8");

    const tailoredOnDisk = join(generated, "CaveBuildTailoredAgentPrompt.md");
    if (useExportedPrompt && existsSync(tailoredOnDisk)) {
      writeResearchExecutionBrief(hubRoot, activeRung as PromptRung);
      prompt = readFileSync(tailoredOnDisk, "utf8");
      const execBlock = formatResearchExecutionBlock(hubRoot, activeRung as PromptRung);
      if (execBlock && !prompt.includes("Research execution brief")) {
        prompt = `${execBlock}\n\n${prompt}`;
      }
      console.log(
        `[CaveCursor:info] Using pre-exported tailored prompt (${tailoredOnDisk}) — not rebuilding generic template.`
      );
    } else {
      prompt = buildLadderPrompt({
        hubRoot,
        report,
        visualShell,
        failingStages,
        ladderContext,
        activeRung: activeRung as PromptRung,
        reportPath,
        visualShellPath,
      });
    }

    const failing = report.stages.filter((s) => s.score < 95).map((s) => `${s.id}:${s.score}`);
    console.log(
      `[CaveCursor:info] Prompt ladder rung=${activeRung} (web research + plan required before edits)`
    );
    console.log(
      `[CaveCursor:info] Grade ${report.letterGrade} (${report.overallScore}/100) dud=${report.isDud ?? false} failing=${failing.join(", ") || "none"}`
    );
  }

  prompt = prependAutonomousPromptBlocks(prompt, activeRung, isTerrain);
  if (!isCursorProvider) {
    prompt +=
      "\n\n## REQUIRED OUTPUT FORMAT (execution layer)\n" +
      "Return one JSON object in a ```json fenced block with this shape:\n" +
      "{ \"summary\": \"...\", \"edits\": [ { \"path\": \"relative/path\", \"op\": \"replace|append|write\", \"oldText\": \"...\", \"newText\": \"...\" } ] }\n" +
      "Rules: use repo-relative paths, no absolute paths, no shell commands, no prose outside the JSON block.";
  }
  writeFileSync(tailoredExportPath, prompt, "utf8");
  if (isTerrain) {
    writeFileSync(join(generated, "SurfaceTerrainActiveRungPrompt.md"), prompt, "utf8");
  } else {
    writeFileSync(promptExportPath, prompt, "utf8");
    writeFileSync(join(generated, "CaveBuildActiveRungPrompt.md"), prompt, "utf8");
  }
  console.log(`[CaveCursor:info] Workflow=${workflowMode}`);
  console.log(`[CaveCursor:info] Research manifest: ${researchJsonPath}`);
  console.log(`[CaveCursor:info] Tailored prompt saved: ${promptExportPath}`);
  await sleep();
  console.log("[CaveCursor:info] Pause before agent prompt (0.3s)");

  if (process.argv.includes("--dry-run")) {
    console.log(prompt);
    writeLastRunDiagnostics(workflowMode, { exitCode: 0, dryRun: true, rung: activeRung });
    process.exit(0);
  }

  const modelId = process.env.CAVE_ACTIVE_MODEL ?? process.env.CAVE_CURSOR_MODEL ?? "auto";
  const activeBaseUrl = process.env.CAVE_ACTIVE_BASE_URL?.trim() || "";
  const useStream = process.argv.includes("--stream");
  const repoUrl = process.env.CAVE_CURSOR_REPO_URL?.trim();
  const startupRetryCount = Math.max(
    1,
    Number.parseInt(process.env.CAVE_AGENT_STARTUP_RETRIES ?? "8", 10) || 8
  );

  async function invokeOnce(cloud: boolean): Promise<RunResult> {
    if (!isCursorProvider) {
      const external = await invokeExternalProvider({
        provider,
        prompt,
        modelId,
        apiKey: apiKey ?? "",
        baseUrl: activeBaseUrl,
      });
      return {
        status: external.status,
        id: external.id,
        result: external.result,
      } as RunResult;
    }

    const opts = cloud
      ? {
          apiKey,
          cloud: { repos: [{ url: repoUrl! }] },
          model: { id: modelId },
        }
      : {
          apiKey,
          local: { cwd: hubRoot, settingSources: [] as [] },
          model: { id: modelId },
        };

    if (cloud && !repoUrl) {
      console.error(
        "Cloud runtime needs CAVE_CURSOR_REPO_URL (Hub git remote, e.g. https://github.com/you/Hub.git)."
      );
      process.exit(1);
    }

    const runtime = cloud ? "cloud" : "local";
    flushThinking(true);
    console.log(
      `[CaveCursor:info] Invoking agent rung=${activeRung} workflow=${workflowMode} runtime=${runtime} model=${modelId} stream=${useStream}`
    );

    if (useStream) {
      const agent = await Agent.create(opts);
      try {
        const run = await agent.send(prompt);
        console.log(`[CaveCursor:info] Run started id=${run.id} agent=${run.agentId}`);
        if (run.supports("stream")) {
          try {
            for await (const event of run.stream()) logStreamEvent(event);
            flushThinking(true);
            console.log("[CaveCursor:info] Stream ended");
          } catch (streamErr) {
            console.error("Stream ended with error:", streamErr);
          }
        } else {
          console.error("stream unsupported:", run.unsupportedReason("stream"));
        }
        const result = await run.wait();
        if (result.status === "error" || result.status === "cancelled") {
          await dumpLiveRunFailure(run, result);
        }
        return result;
      } finally {
        await agent[Symbol.asyncDispose]();
      }
    }

    return Agent.prompt(prompt, opts);
  }

  function isSqliteStartupFailure(err: unknown): boolean {
    return (
      err instanceof CursorAgentError &&
      typeof err.message === "string" &&
      err.message.toUpperCase().includes("SQLITE_CANTOPEN")
    );
  }

  function startupRetryDelayMs(attempt: number): number {
    const base = 1200;
    return base * attempt;
  }

  async function invokeLocalWithStartupRetries(): Promise<RunResult> {
    let lastErr: unknown = null;
    for (let attempt = 1; attempt <= startupRetryCount; attempt++) {
      try {
        if (attempt > 1) {
          console.log(
            `[CaveCursor:info] Retry ${attempt}/${startupRetryCount} — waiting for Cursor local runtime…`
          );
        }
        return await invokeOnce(false);
      } catch (err) {
        lastErr = err;
        if (!isSqliteStartupFailure(err) || attempt >= startupRetryCount) throw err;
        const wait = startupRetryDelayMs(attempt);
        console.error(
          `[CaveCursor:warn] Local runtime not ready (${attempt}/${startupRetryCount}): ${
            (err as CursorAgentError).message
          }`
        );
        console.error(`[CaveCursor:info] Waiting ${wait}ms before retry…`);
        await new Promise((resolve) => setTimeout(resolve, wait));
      }
    }
    throw lastErr instanceof Error ? lastErr : new Error(String(lastErr));
  }

  try {
    let cloud = forceCloud;
    if (useAuto && !forceCloud) cloud = false;

    let result = isCursorProvider
      ? cloud
        ? await invokeOnce(true)
        : await invokeLocalWithStartupRetries()
      : await invokeOnce(false);

    if (
      useAuto &&
      isCursorProvider &&
      !cloud &&
      (result.status === "error" || result.status === "cancelled") &&
      repoUrl
    ) {
      console.error("\nLocal agent failed — retrying on cloud (CAVE_CURSOR_REPO_URL)…\n");
      result = await invokeOnce(true);
    }

    if (result.status === "error") {
      console.error("\nAgent run failed:", result.id);
      console.error("Run: npm run doctor  (checks API key + local executor)");
      console.error(
        "\nManual fallback: open Cursor IDE chat in",
        hubRoot,
        "and paste",
        promptExportPath
      );
      writeLastRunDiagnostics(workflowMode, {
        exitCode: 2,
        error: "agent_error",
        runId: result.id,
        status: result.status,
        rung: activeRung,
        provider,
      });
      process.exit(2);
    }

    if (result.status === "cancelled") {
      console.error("Agent run cancelled:", result.id);
      writeLastRunDiagnostics(workflowMode, {
        exitCode: 3,
        error: "agent_cancelled",
        runId: result.id,
        provider,
      });
      process.exit(3);
    }

    console.log("Agent finished:", result.status, result.id);
    let externalExecution: { applied: number; skipped: number; notes: string[] } | null = null;
    if (result.result) {
      if (!isCursorProvider) {
        const payload = parseExecutionPayload(result.result);
        externalExecution = applyExecutionPayloadIfEnabled(payload, hubRoot);
        for (const note of externalExecution.notes) console.log(`[CaveCursor:exec] ${note}`);
      }
      console.log(result.result);
    }

    await sleep();
    console.log("[CaveCursor:info] Pause before phase-complete flag (0.3s)");
    const wf: WorkflowMode = isPreBuild ? "pre_build" : isTerrain ? "terrain" : "post_build";
    emitPhaseComplete(wf, activeRung, "done");
    writeLastRunDiagnostics(workflowMode, {
      exitCode: 0,
      rung: activeRung,
      runId: result.id,
      status: result.status,
      provider,
      model: modelId,
      baseUrl: activeBaseUrl,
      externalExecutionApplied: externalExecution?.applied ?? 0,
      externalExecutionSkipped: externalExecution?.skipped ?? 0,
      externalExecutionNotes: externalExecution?.notes ?? [],
    });
  } catch (err) {
    if (isSqliteStartupFailure(err) && repoUrl && useAuto && !forceCloud) {
      console.error(
        "\nLocal SQLITE startup failed — retrying on cloud (CAVE_CURSOR_REPO_URL)…\n"
      );
      try {
        const cloudResult = await invokeOnce(true);
        if (cloudResult.status === "error") {
          writeLastRunDiagnostics(workflowMode, {
            exitCode: 2,
            error: "agent_error",
            runId: cloudResult.id,
            status: cloudResult.status,
            rung: activeRung,
            runtime: "cloud_after_sqlite",
            provider,
          });
          process.exit(2);
        }

        if (cloudResult.status === "cancelled") {
          writeLastRunDiagnostics(workflowMode, {
            exitCode: 3,
            error: "agent_cancelled",
            runId: cloudResult.id,
            runtime: "cloud_after_sqlite",
            provider,
          });
          process.exit(3);
        }

        const wf: WorkflowMode = isPreBuild ? "pre_build" : isTerrain ? "terrain" : "post_build";
        emitPhaseComplete(wf, activeRung, "done");
        writeLastRunDiagnostics(workflowMode, {
          exitCode: 0,
          rung: activeRung,
          runId: cloudResult.id,
          status: cloudResult.status,
          runtime: "cloud_after_sqlite",
          provider,
        });
        process.exit(0);
      } catch (cloudErr) {
        console.error("Cloud fallback after SQLITE also failed:", cloudErr);
      }
    }

    if (err instanceof CursorAgentError || isSqliteStartupFailure(err)) {
      const message =
        err instanceof CursorAgentError ? err.message : err instanceof Error ? err.message : String(err);
      console.error("Startup failed:", message);
      console.error("Run: npm run doctor");
      console.error(
        "Try: keep Cursor app open + signed in; set CAVE_AGENT_STARTUP_RETRIES=8 if startup is slow."
      );
      writeLastRunDiagnostics(workflowMode, {
        exitCode: 1,
        error: "cursor_agent_startup",
        message,
        retryable: isSqliteStartupFailure(err) || (err instanceof CursorAgentError && err.isRetryable),
        provider,
      });
      process.exit(1);
    }
    writeLastRunDiagnostics(workflowMode, {
      exitCode: 1,
      error: "unhandled",
      message: err instanceof Error ? err.message : String(err),
      provider,
    });
    throw err;
  }
}

type ExternalInvokeArgs = {
  provider: string;
  prompt: string;
  modelId: string;
  apiKey: string;
  baseUrl: string;
};

async function invokeExternalProvider(args: ExternalInvokeArgs): Promise<ProviderRunResult> {
  const provider = args.provider.toLowerCase();
  if (provider.includes("anthropic")) return invokeAnthropic(args);
  if (provider.includes("google") || provider.includes("gemini")) return invokeGemini(args);
  return invokeOpenAiCompatible(args);
}

async function invokeOpenAiCompatible(args: ExternalInvokeArgs): Promise<ProviderRunResult> {
  const base =
    args.baseUrl ||
    (args.provider.toLowerCase().includes("openrouter")
      ? "https://openrouter.ai/api/v1"
      : "https://api.openai.com/v1");
  const url = `${base.replace(/\/$/, "")}/chat/completions`;
  const runId = `ext-openai-${Date.now()}`;
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (args.apiKey) headers.Authorization = `Bearer ${args.apiKey}`;
  if (args.provider.toLowerCase().includes("openrouter")) {
    headers["HTTP-Referer"] = "https://github.com/cursor/environment-authoring-kit";
    headers["X-Title"] = "Environment Kit Cave Grader";
  }

  const res = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify({
      model: args.modelId,
      messages: [{ role: "user", content: args.prompt }],
      temperature: 0.2,
      stream: false,
    }),
  });
  const text = await res.text();
  if (!res.ok) return { status: "error", id: runId, result: `[${res.status}] ${text.slice(0, 8000)}` };
  try {
    const json = JSON.parse(text) as { choices?: Array<{ message?: { content?: string } }> };
    return {
      status: "finished",
      id: runId,
      result: json.choices?.[0]?.message?.content ?? text,
    };
  } catch {
    return { status: "finished", id: runId, result: text };
  }
}

async function invokeAnthropic(args: ExternalInvokeArgs): Promise<ProviderRunResult> {
  const url = `${(args.baseUrl || "https://api.anthropic.com/v1").replace(/\/$/, "")}/messages`;
  const runId = `ext-anthropic-${Date.now()}`;
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "anthropic-version": "2023-06-01",
  };
  if (args.apiKey) headers["x-api-key"] = args.apiKey;
  const res = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify({
      model: args.modelId,
      max_tokens: 4096,
      temperature: 0.2,
      messages: [{ role: "user", content: args.prompt }],
    }),
  });
  const text = await res.text();
  if (!res.ok) return { status: "error", id: runId, result: `[${res.status}] ${text.slice(0, 8000)}` };
  try {
    const json = JSON.parse(text) as { content?: Array<{ text?: string }> };
    const joined = json.content?.map((c) => c.text ?? "").join("\n").trim();
    return { status: "finished", id: runId, result: joined || text };
  } catch {
    return { status: "finished", id: runId, result: text };
  }
}

async function invokeGemini(args: ExternalInvokeArgs): Promise<ProviderRunResult> {
  const base = (args.baseUrl || "https://generativelanguage.googleapis.com/v1beta").replace(/\/$/, "");
  const model = args.modelId || "gemini-2.5-flash";
  const runId = `ext-gemini-${Date.now()}`;
  const keyParam = args.apiKey ? `?key=${encodeURIComponent(args.apiKey)}` : "";
  const url = `${base}/models/${encodeURIComponent(model)}:generateContent${keyParam}`;
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      contents: [{ role: "user", parts: [{ text: args.prompt }] }],
      generationConfig: { temperature: 0.2, maxOutputTokens: 4096 },
    }),
  });
  const text = await res.text();
  if (!res.ok) return { status: "error", id: runId, result: `[${res.status}] ${text.slice(0, 8000)}` };
  try {
    const json = JSON.parse(text) as {
      candidates?: Array<{ content?: { parts?: Array<{ text?: string }> } }>;
    };
    const joined = json.candidates?.[0]?.content?.parts?.map((p) => p.text ?? "").join("\n").trim();
    return { status: "finished", id: runId, result: joined || text };
  } catch {
    return { status: "finished", id: runId, result: text };
  }
}

main();
