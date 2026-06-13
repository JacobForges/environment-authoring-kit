/**
 * Loop code-bot sessions until HubCodeProgress.codeReady === true.
 */
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { join } from "node:path";
import { loadCaveGraderEnv } from "../../Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/load-env.js";
import { codeReady, loadCodeProgress } from "./code-progress.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../..")).replace(/\/$/, "");
const botDir = join(hubRoot, "Tools/code-bot");
const maxSessions = Math.max(
  1,
  Number.parseInt(process.env.CODE_BOT_MAX ?? "48", 10) || 48
);
const pauseSec = Math.max(0, Number.parseInt(process.env.CODE_BOT_PAUSE_SEC ?? "8", 10) || 8);
const skipUnity = process.env.CODE_BOT_SKIP_UNITY === "1";

function sleepMs(ms: number): void {
  if (ms <= 0) return;
  spawnSync("sleep", [String(Math.ceil(ms / 1000))], { stdio: "ignore" });
}

function runCodeBotPostPass(): number | null {
  const unity = process.env.UNITY_PATH?.trim();
  if (skipUnity || !unity || !existsSync(unity)) return null;

  const logFile = join(hubRoot, "Logs/code-bot-post-pass.log");
  console.log("[CodeBot] Unity post-pass (compile + smoke + wiring audit)…");
  const result = spawnSync(
    unity,
    [
      "-batchmode",
      "-nographics",
      "-projectPath",
      hubRoot,
      "-executeMethod",
      "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunCodeBotPostPass",
      "-quit",
      "-logFile",
      logFile,
    ],
    { stdio: "inherit", timeout: 900_000 }
  );
  return result.status ?? 1;
}

function runCodeSession(extra: string[]): number {
  const script = join(botDir, "run-session.sh");
  const result = spawnSync(script, ["--stream", ...extra], {
    cwd: hubRoot,
    stdio: "inherit",
    env: { ...process.env, HUB_ROOT: hubRoot },
    timeout: Number.parseInt(process.env.CODE_SESSION_TIMEOUT_MIN ?? "90", 10) * 60_000,
  });
  return result.status ?? 1;
}

if (codeReady(loadCodeProgress(hubRoot))) {
  console.log("[CodeBot] codeReady already true.");
  process.exit(0);
}

console.log(`[CodeBot] Loop until codeReady (max ${maxSessions} sessions). Close Unity for batch compile.`);

for (let i = 1; i <= maxSessions; i++) {
  console.log(`\n[CodeBot] === Session ${i}/${maxSessions} ===\n`);

  const postExit = runCodeBotPostPass();
  if (postExit === 2) {
    console.error("[CodeBot] Compile errors — agent should fix on this pass.");
  } else if (postExit === 1) {
    console.log("[CodeBot] Post-pass: compile OK, competition smoke or wiring pending.");
  }

  const agentExit = runCodeSession(process.argv.slice(2));
  if (agentExit === 4) {
    console.error("[CodeBot] Session verify failed (compile). Retrying next iteration.");
  } else if (agentExit !== 0) {
    console.error(`[CodeBot] Agent session failed exit=${agentExit}`);
    process.exit(agentExit);
  }

  if (codeReady(loadCodeProgress(hubRoot))) {
    console.log("\n[CodeBot] codeReady true — all wiring tasks done.");
    process.exit(0);
  }

  if (i < maxSessions) {
    console.log(`[CodeBot] Pausing ${pauseSec}s…`);
    sleepMs(pauseSec * 1000);
  }
}

console.error(`[CodeBot] Max sessions (${maxSessions}) without codeReady. Review HubCodeProgress.json blocked tasks.`);
process.exit(5);
