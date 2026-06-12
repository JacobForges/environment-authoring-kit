/**
 * Hub cursor-bot pipeline audit — read-only JSON truth aggregation + optional Unity validation batch.
 * Writes Assets/EnvironmentKit/Generated/HubBotPipelineAudit.json
 */
import { execFileSync, spawnSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, statfsSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { formatGameplaySmokeLine, loadPlaySmokeReport, playSmokePassed } from "./gameplay-smoke-gate.js";
import {
  formatMissionPhaseLine,
  gameplayMilestonesReady,
  loadHubGameProgress,
  loadPrimaryQualityGate,
  resolveMissionPhase,
  worldGatePassed,
} from "./mission-phase.js";
import { loadCaveGraderEnv } from "./load-env.js";

loadCaveGraderEnv();

export type AuditCheck = {
  id: string;
  tier: number;
  pass: boolean;
  severity: "ok" | "warn" | "fail" | "skip";
  detail: string;
  playbookId?: string;
};

export type PipelineAuditResult = {
  capturedUtc: string;
  hubRoot: string;
  missionPhase: string;
  pass: boolean;
  failCount: number;
  warnCount: number;
  checks: AuditCheck[];
};

const GENERATED = "Assets/EnvironmentKit/Generated";
const AUDIT_OUT = `${GENERATED}/HubBotPipelineAudit.json`;
const VALIDATION_REPORT = `${GENERATED}/CaveBuildBotValidationReport.json`;

function readJson<T>(path: string): T | null {
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch {
    return null;
  }
}

function diskFreeGb(hubRoot: string): { freeGb: number; ok: boolean } {
  try {
    const st = statfsSync(hubRoot);
    const freeBytes = Number(st.bfree) * Number(st.bsize);
    const freeGb = freeBytes / (1024 ** 3);
    return { freeGb, ok: freeGb >= 2 };
  } catch {
    try {
      const out = execFileSync("df", ["-k", hubRoot], { encoding: "utf8" }).trim().split("\n").pop() ?? "";
      const parts = out.split(/\s+/);
      const availK = Number.parseInt(parts[3] ?? "0", 10);
      const freeGb = availK / (1024 ** 2);
      return { freeGb, ok: freeGb >= 2 };
    } catch {
      return { freeGb: -1, ok: true };
    }
  }
}

function mergeUnityValidation(checks: AuditCheck[]): void {
  const hubRoot = process.env.HUB_ROOT ?? "";
  const doc = readJson<{
    checks?: { id?: string; pass?: boolean; detail?: string; blocking?: boolean }[];
    exitCode?: number;
  }>(join(hubRoot, VALIDATION_REPORT));
  if (!doc?.checks?.length) return;

  for (const row of doc.checks) {
    if (!row.id) continue;
    checks.push({
      id: `unity.${row.id}`,
      tier: 2,
      pass: row.pass === true,
      severity: row.pass ? "ok" : row.blocking ? "fail" : "warn",
      detail: row.detail ?? "",
      playbookId: playbookForCheck(row.id, row.pass === true),
    });
  }
}

function playbookForCheck(id: string, pass: boolean): string | undefined {
  if (pass) return undefined;
  const map: Record<string, string> = {
    "compile.clean": "COMPILE_GATE",
    "probe.surface_route": "SURFACE_ROUTE_FAIL",
    "probe.cave_route": "ROUTE_PROBE_FAIL",
    "probe.surface_walkin": "ROUTE_PROBE_FAIL",
    "audit.world_layout": "LAYOUT_AUDIT_SEAMS",
    "audit.visual_shell": "GEOMETRY_VOID",
    "audit.geometry_integrity": "GEOMETRY_VOID",
    "probe.combat": "GAMEPLAY_MILESTONE",
    "gate.performance_tri": "PERF_TRI_BUDGET",
    "gameplay.smoke_checklist": "GAMEPLAY_SMOKE_FAIL",
    "competition.smoke": "COMPETITION_SMOKE_FAIL",
    "scene.cave_root": "GAMEPLAY_MILESTONE",
  };
  return map[id];
}

export function runPipelineAudit(hubRoot: string, opts?: { mergeUnity?: boolean }): PipelineAuditResult {
  const checks: AuditCheck[] = [];
  const gen = join(hubRoot, GENERATED);

  const disk = diskFreeGb(hubRoot);
  checks.push({
    id: "host.disk_free",
    tier: 0,
    pass: disk.ok,
    severity: disk.ok ? "ok" : "fail",
    detail: disk.freeGb < 0 ? "unknown" : `${disk.freeGb.toFixed(1)} GB free`,
    playbookId: disk.ok ? undefined : "DISK_FULL_PACED",
  });

  checks.push({
    id: "host.generated_writable",
    tier: 0,
    pass: existsSync(gen),
    severity: existsSync(gen) ? "ok" : "fail",
    detail: existsSync(gen) ? GENERATED : `${GENERATED} missing — create or symlink`,
    playbookId: existsSync(gen) ? undefined : "EXTERNAL_STORAGE",
  });

  const phase = resolveMissionPhase(hubRoot);
  checks.push({
    id: "mission.phase",
    tier: 0,
    pass: true,
    severity: "ok",
    detail: phase,
  });

  const quality = loadPrimaryQualityGate(hubRoot);
  checks.push({
    id: "world.quality_report",
    tier: 0,
    pass: quality != null,
    severity: quality ? "ok" : "warn",
    detail: quality
      ? `score=${quality.overallScore ?? "?"} acceptable=${quality.buildAcceptable === true}`
      : "CaveBuildQualityReport.json missing — re-grade in Unity",
  });

  checks.push({
    id: "world.gate_85",
    tier: 0,
    pass: worldGatePassed(hubRoot),
    severity: worldGatePassed(hubRoot) ? "ok" : "warn",
    detail: worldGatePassed(hubRoot) ? "ship gate passed" : "buildAcceptable + score ≥ 85 not met",
    playbookId: worldGatePassed(hubRoot) ? undefined : "MISSING_SPLINE",
  });

  const routeProbe = readJson<{ passed?: boolean }>(join(gen, "CaveBuildRouteProbe.json"));
  checks.push({
    id: "world.route_probe",
    tier: 0,
    pass: routeProbe?.passed !== false,
    severity: routeProbe?.passed === false ? "fail" : routeProbe ? "ok" : "warn",
    detail: routeProbe ? `passed=${routeProbe.passed !== false}` : "no route probe JSON",
    playbookId: routeProbe?.passed === false ? "ROUTE_PROBE_FAIL" : undefined,
  });

  const surfaceRoute = readJson<{ passed?: boolean; reachedCaveMouth?: boolean }>(
    join(gen, "CaveBuildSurfaceRouteProbe.json")
  );
  checks.push({
    id: "world.surface_route",
    tier: 0,
    pass: surfaceRoute?.passed === true,
    severity: surfaceRoute?.passed === false ? "fail" : surfaceRoute ? "ok" : "warn",
    detail: surfaceRoute
      ? `passed=${surfaceRoute.passed === true} mouth=${surfaceRoute.reachedCaveMouth === true}`
      : "no surface route probe JSON",
    playbookId: surfaceRoute?.passed === false ? "SURFACE_ROUTE_FAIL" : undefined,
  });

  const layout = readJson<{
    blocksSurfaceContinue?: boolean;
    blocksCaveQueue?: boolean;
    maxSeamGapMeters?: number;
  }>(join(gen, "WorldLayoutAudit.json"));
  if (layout) {
    const ok = layout.blocksSurfaceContinue !== true && layout.blocksCaveQueue !== true;
    checks.push({
      id: "world.layout_audit",
      tier: 0,
      pass: ok,
      severity: ok ? "ok" : "fail",
      detail: `seam=${layout.maxSeamGapMeters?.toFixed(2) ?? "?"}m blocks=${layout.blocksCaveQueue === true}`,
      playbookId: ok ? undefined : "LAYOUT_AUDIT_SEAMS",
    });
  }

  const compile = readJson<{
    hasCompileErrors?: boolean;
    verifiedErrorCount?: number;
  }>(join(gen, "CaveBuildCompileDiagnostics.json"));
  const compileOk =
    compile != null &&
    compile.hasCompileErrors !== true &&
    (compile.verifiedErrorCount ?? 0) === 0;
  checks.push({
    id: "compile.diagnostics",
    tier: 0,
    pass: compileOk,
    severity: compile ? (compileOk ? "ok" : "fail") : "warn",
    detail: compile
      ? `verifiedErrors=${compile.verifiedErrorCount ?? "?"}`
      : "no compile diagnostics — set UNITY_PATH for export",
    playbookId: compileOk ? undefined : "COMPILE_GATE",
  });

  const progress = loadHubGameProgress(hubRoot);
  checks.push({
    id: "gameplay.progress_file",
    tier: 0,
    pass: progress != null,
    severity: progress ? "ok" : "warn",
    detail: progress ? `demoReady=${progress.demoReady === true}` : "HubGameProgress.json missing",
    playbookId: progress ? undefined : "GAMEPLAY_MILESTONE",
  });

  checks.push({
    id: "gameplay.milestones_g1_g7",
    tier: 0,
    pass: gameplayMilestonesReady(hubRoot),
    severity: gameplayMilestonesReady(hubRoot) ? "ok" : "warn",
    detail: gameplayMilestonesReady(hubRoot) ? "G1–G7 done" : "gameplay milestones pending",
    playbookId: gameplayMilestonesReady(hubRoot) ? undefined : "GAMEPLAY_MILESTONE",
  });

  checks.push({
    id: "gameplay.smoke_play",
    tier: 0,
    pass: playSmokePassed(hubRoot),
    severity: playSmokePassed(hubRoot) ? "ok" : "warn",
    detail: formatGameplaySmokeLine(hubRoot),
    playbookId: playSmokePassed(hubRoot) ? undefined : "GAMEPLAY_SMOKE_FAIL",
  });

  const researchIndex = join(hubRoot, "Assets/EnvironmentKit/ResearchCache/index.json");
  checks.push({
    id: "research.cache_index",
    tier: 0,
    pass: existsSync(researchIndex),
    severity: existsSync(researchIndex) ? "ok" : "warn",
    detail: existsSync(researchIndex) ? "ResearchCache present" : "run npm run sync-research-pull",
  });

  const sessionCfg = readJson<{ label?: string; tileCount?: number; agentInvokes?: boolean }>(
    join(gen, "CaveBuildActiveSessionConfig.json")
  );
  if (sessionCfg) {
    checks.push({
      id: "planner.session",
      tier: 0,
      pass: true,
      severity: "ok",
      detail: `${sessionCfg.label ?? "session"} tiles=${sessionCfg.tileCount ?? "?"} agentInvokes=${sessionCfg.agentInvokes !== false}`,
    });
  }

  if (opts?.mergeUnity) mergeUnityValidation(checks);

  const failCount = checks.filter((c) => c.severity === "fail").length;
  const warnCount = checks.filter((c) => c.severity === "warn").length;
  const result: PipelineAuditResult = {
    capturedUtc: new Date().toISOString(),
    hubRoot,
    missionPhase: phase,
    pass: failCount === 0,
    failCount,
    warnCount,
    checks,
  };

  mkdirSync(gen, { recursive: true });
  writeFileSync(join(hubRoot, AUDIT_OUT), `${JSON.stringify(result, null, 2)}\n`, "utf8");
  return result;
}

export function runUnityPipelineAudit(hubRoot: string): number | null {
  const unity = process.env.UNITY_PATH?.trim();
  if (!unity || !existsSync(unity)) return null;
  if (process.env.CAVE_SKIP_PIPELINE_AUDIT_UNITY === "1") return null;

  const logFile = join(hubRoot, "Logs/bot-pipeline-audit-unity.log");
  console.log("[CaveCursor:audit] Unity read-only validation batch (close editor first)…");
  const result = spawnSync(
    unity,
    [
      "-batchmode",
      "-nographics",
      "-projectPath",
      hubRoot,
      "-executeMethod",
      "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunBotPipelineAudit",
      "-quit",
      "-logFile",
      logFile,
    ],
    { stdio: "inherit", timeout: 600_000 }
  );
  return result.status ?? 1;
}

export function formatPipelineAuditLine(result: PipelineAuditResult): string {
  return (
    `[CaveCursor:pipeline-audit] phase=${result.missionPhase} ` +
    `pass=${result.pass} fail=${result.failCount} warn=${result.warnCount}`
  );
}

export function formatPipelineAuditBlock(result: PipelineAuditResult): string {
  const failing = result.checks.filter((c) => !c.pass && c.severity !== "skip");
  if (failing.length === 0) {
    return "## Pipeline audit\nAll tracked checks passed or are informational.";
  }

  const lines = failing.slice(0, 12).map((c) => {
    const pb = c.playbookId ? ` → playbook **${c.playbookId}**` : "";
    return `- **${c.id}** (${c.severity}): ${c.detail}${pb}`;
  });
  return ["## Pipeline audit — fix these first", ...lines].join("\n");
}

export function topPlaybookFromAudit(hubRoot: string): string | null {
  const doc = readJson<PipelineAuditResult>(join(hubRoot, AUDIT_OUT));
  if (!doc?.checks?.length) return null;
  const fail = doc.checks.find((c) => c.severity === "fail" && c.playbookId);
  if (fail?.playbookId) return fail.playbookId;
  const warn = doc.checks.find((c) => c.severity === "warn" && c.playbookId);
  return warn?.playbookId ?? null;
}

function main(): void {
  const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../../..")).replace(/\/$/, "");
  const withUnity = process.argv.includes("--unity");

  let unityExit: number | null = null;
  if (withUnity) unityExit = runUnityPipelineAudit(hubRoot);

  const result = runPipelineAudit(hubRoot, { mergeUnity: withUnity || unityExit != null });
  console.log(formatPipelineAuditLine(result));
  console.log(formatMissionPhaseLine(hubRoot));

  if (unityExit != null && unityExit === 2) process.exit(4);
  if (!result.pass) process.exit(unityExit != null && unityExit > 0 ? unityExit : 1);
  process.exit(unityExit ?? 0);
}

const isMain =
  process.argv[1] != null && fileURLToPath(import.meta.url) === process.argv[1];
if (isMain) main();
