/**
 * Run agent sessions in a loop until HubGameProgress.demoReady === true.
 * Production supervisor: meat-loop post-pass, stall detection, rollback, dashboard.
 */
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { loadBotProductionConfig } from "./bot-production-config.js";
import {
  evaluatePostUnity,
  exportProductionGateStatus,
  postAgentCheckpoint,
  postWebhook,
  resolveIterationCap,
  shouldRunAgent,
  tryGitRollback,
  writeSessionDashboard,
  type SessionSummary,
} from "./bot-supervisor.js";
import { loadCaveGraderEnv } from "./load-env.js";
import {
  formatMissionPhaseLine,
  loadHubGameProgress,
  resolveMissionPhase,
  worldGatePassed,
} from "./mission-phase.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../..")).replace(/\/$/, "");
const graderDir = join(hubRoot, "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader");
const botDir = join(hubRoot, "Tools/cursor-bot");
const cfg = loadBotProductionConfig(hubRoot);
const maxIterations = resolveIterationCap(hubRoot);
const pauseSec = cfg.pauseSec;
const skipUnityPost = process.env.CAVE_UNTIL_DEMO_SKIP_UNITY === "1";

function sleepMs(ms: number): void {
  if (ms <= 0) return;
  spawnSync("sleep", [String(Math.ceil(ms / 1000))], { stdio: "ignore" });
}

function demoReady(): boolean {
  return loadHubGameProgress(hubRoot)?.demoReady === true;
}

function runUnityProductionPostPass(): number | null {
  const unity = process.env.UNITY_PATH?.trim();
  if (skipUnityPost || !unity || !existsSync(unity)) return null;

  const logFile = join(hubRoot, "Logs/agent-post-pass.log");
  console.log("[CaveCursor:until-demo] Unity production post-pass (meat loop + gates)…");
  const result = spawnSync(
    unity,
    [
      "-batchmode",
      "-nographics",
      "-projectPath",
      hubRoot,
      "-executeMethod",
      "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunBotProductionPostPass",
      "-quit",
      "-logFile",
      logFile,
    ],
    { stdio: "inherit", timeout: 900_000 }
  );
  return result.status ?? 1;
}

function runAgentSession(extraArgs: string[]): number {
  if (!shouldRunAgent(cfg)) {
    console.log("[CaveCursor:until-demo] Nightly mode — skipping agent session (Unity-only pass).");
    return 0;
  }
  const script = join(botDir, "run-session.sh");
  const result = spawnSync(script, ["--stream", ...extraArgs], {
    cwd: hubRoot,
    stdio: "inherit",
    env: { ...process.env, HUB_ROOT: hubRoot, CAVE_BOT_SUPERVISOR: "1" },
    timeout: cfg.sessionTimeoutMin * 60_000,
  });
  return result.status ?? 1;
}

function qualityReportMtime(): number {
  const path = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json");
  try {
    return statSync(path).mtimeMs;
  } catch {
    return 0;
  }
}

function readOverallScore(): number {
  const path = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json");
  if (!existsSync(path)) return 0;
  try {
    const doc = JSON.parse(readFileSync(path, "utf8")) as { overallScore?: number };
    return doc.overallScore ?? 0;
  } catch {
    return 0;
  }
}

console.log(formatMissionPhaseLine(hubRoot));
console.log(
  `[CaveCursor:until-demo] Production loop until demoReady (max ${maxIterations} sessions, mode=${cfg.runMode}, pause ${pauseSec}s).`
);
console.log(
  "[CaveCursor:until-demo] Set UNITY_PATH for meat-loop post-pass; keep Unity editor closed during batch."
);

if (demoReady()) {
  console.log("[CaveCursor:until-demo] demoReady already true — nothing to do.");
  process.exit(0);
}

let lastReportMtime = qualityReportMtime();

for (let i = 1; i <= maxIterations; i++) {
  const phase = resolveMissionPhase(hubRoot);
  console.log(`\n[CaveCursor:until-demo] === Session ${i}/${maxIterations} phase=${phase} ===\n`);

  const scoreBefore = readOverallScore();
  let agentExit = 0;
  if (shouldRunAgent(cfg)) {
    agentExit = runAgentSession(process.argv.slice(2));
    postAgentCheckpoint(hubRoot, i, agentExit);
    if (agentExit === 4) {
      console.error(
        "[CaveCursor:until-demo] Compile verify failed (exit 4). Next pass should hit compile_gate."
      );
    } else if (agentExit !== 0) {
      console.error(`[CaveCursor:until-demo] Agent session failed (exit ${agentExit}).`);
      process.exit(agentExit);
    }
  }

  let unityExit: number | null = null;
  let rolledBack = false;
  const notes: string[] = [];

  if (phase === "world" || phase === "gameplay") {
    unityExit = runUnityProductionPostPass();
    exportProductionGateStatus(hubRoot);

    if (unityExit === 2) {
      console.error("[CaveCursor:until-demo] Unity post-pass blocked on compile errors.");
      process.exit(4);
    }

    const evalResult = evaluatePostUnity(hubRoot, cfg, scoreBefore);
    notes.push(...evalResult.notes);

    if (evalResult.rollback) {
      rolledBack = tryGitRollback(hubRoot);
      notes.push(rolledBack ? "Git rollback applied after score regression." : "Rollback failed — manual review.");
    }

    if (evalResult.stalled) {
      notes.push(`Escalate playbook: ${evalResult.playbookId || "MISSING_SPLINE"}`);
    }

    if (unityExit === 3) {
      console.log("[CaveCursor:until-demo] Gate not passed after post-pass — next session.");
    }
  }

  const scoreAfter = readOverallScore();
  const summary: SessionSummary = {
    sessionIndex: i,
    phase,
    agentExit,
    unityExit,
    scoreBefore,
    scoreAfter,
    playbookId: notes.find((n) => n.startsWith("Escalate"))?.split(": ")[1] ?? "",
    stalled: notes.some((n) => n.includes("Stall")),
    rolledBack,
    notes,
  };
  writeSessionDashboard(hubRoot, summary);
  postWebhook(cfg, summary);

  const newMtime = qualityReportMtime();
  if (newMtime <= lastReportMtime && !process.env.UNITY_PATH) {
    console.warn(
      "[CaveCursor:until-demo] Quality JSON unchanged — set UNITY_PATH or re-grade in Unity between passes."
    );
  }
  lastReportMtime = newMtime;

  console.log(formatMissionPhaseLine(hubRoot));

  if (demoReady()) {
    console.log("\n[CaveCursor:until-demo] Playable demo gate passed (demoReady: true). Done.");
    process.exit(0);
  }

  if (worldGatePassed(hubRoot) && phase === "world") {
    console.log("[CaveCursor:until-demo] World gate passed — next session routes to gameplay milestones.");
  }

  if (i < maxIterations) {
    console.log(`[CaveCursor:until-demo] Pausing ${pauseSec}s before next session…`);
    sleepMs(pauseSec * 1000);
  }
}

console.error(
  `[CaveCursor:until-demo] Reached max iterations (${maxIterations}) without demoReady. Increase CAVE_UNTIL_DEMO_MAX or finish G8 manually.`
);
process.exit(5);
