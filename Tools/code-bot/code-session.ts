/**
 * Hub Code Bot — one SDK session per code task (Assets/Scripts wiring only).
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { Agent, CursorAgentError } from "@cursor/sdk";
import type { SDKMessage } from "@cursor/sdk";
import { loadCaveGraderEnv } from "../../Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/load-env.js";
import {
  formatVerifyFailureMessage,
  verifySessionAfterAgent,
} from "../../Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/session-verify.js";
import {
  buildCodeBotPrompt,
  ensureCodeProgressFile,
} from "./code-prompt.js";
import {
  codeReady,
  loadCodeProgress,
  parseCodeTaskArg,
  pickActiveCodeTask,
} from "./code-progress.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../..")).replace(/\/$/, "");
const logDir = join(hubRoot, "Logs/code-bot");

function logStream(event: SDKMessage): void {
  if (event.type === "status") {
    console.error(`[status] ${event.status}${event.message ? `: ${event.message}` : ""}`);
  } else if (event.type === "tool_call" && event.status === "error") {
    console.error(`[tool error] ${event.name}`, event.result ?? "");
  }
}

function formatCodeBotSetup(): string {
  return [
    "## Hub Code Bot setup",
    "",
    "- **Scope:** `Assets/Scripts/` + `Assets/Scripts/Editor/` only.",
    "- **Forbidden:** EnvKit world pipeline (`Packages/.../Editor/Blockout/`), Full AAA Rebuild, terrain/cave queue.",
    "- **Truth:** `Assets/Scripts/HubCodeProgress.json` — update task status when done.",
    "- **World bot:** separate — do not fix cave grades or route probes here.",
  ].join("\n");
}

async function main(): Promise<void> {
  const progressPath = ensureCodeProgressFile(hubRoot);
  const progress = loadCodeProgress(hubRoot);
  if (!progress?.tasks || Object.keys(progress.tasks).length === 0) {
    console.error(`Missing or empty tasks in ${progressPath}`);
    process.exit(1);
  }

  if (codeReady(progress) && !process.argv.includes("--force")) {
    console.log("[CodeBot] codeReady already true — nothing to do (use --force to run anyway).");
    process.exit(0);
  }

  const override = parseCodeTaskArg(process.argv);
  const activeTask = pickActiveCodeTask(progress, override);

  let prompt = buildCodeBotPrompt({ hubRoot, activeTask, progress });
  prompt = `${formatCodeBotSetup()}\n\n---\n\n${prompt}`;

  mkdirSync(logDir, { recursive: true });
  const exportPath = join(logDir, "HubCodeActivePrompt.md");
  writeFileSync(exportPath, prompt, "utf8");

  console.log(`[CodeBot] task=${activeTask} codeReady=${codeReady(progress)}`);
  console.log(`[CodeBot] Prompt: ${exportPath}`);

  if (process.argv.includes("--dry-run")) {
    console.log(prompt);
    process.exit(0);
  }

  const apiKey = process.env.CURSOR_API_KEY?.trim();
  if (!apiKey) {
    console.error("FAIL: CURSOR_API_KEY missing in cave-grader/.env");
    process.exit(1);
  }

  const modelId = process.env.CODE_CURSOR_MODEL ?? process.env.CAVE_CURSOR_MODEL ?? "auto";
  const useStream = process.argv.includes("--stream");
  const cloud = process.argv.includes("--cloud");
  const repoUrl = process.env.CAVE_CURSOR_REPO_URL?.trim();

  const opts = cloud
    ? { apiKey, cloud: { repos: [{ url: repoUrl! }] }, model: { id: modelId } }
    : { apiKey, local: { cwd: hubRoot, settingSources: [] as [] }, model: { id: modelId } };

  if (cloud && !repoUrl) {
    console.error("Cloud needs CAVE_CURSOR_REPO_URL");
    process.exit(1);
  }

  console.log(`[CodeBot] runtime=${cloud ? "cloud" : "local"} model=${modelId} stream=${useStream}`);

  let result;
  try {
    if (useStream) {
      const agent = await Agent.create(opts);
      try {
        const run = await agent.send(prompt);
        console.log(`[CodeBot] run=${run.id}`);
        if (run.supports("stream")) {
          for await (const event of run.stream()) logStream(event);
        }
        result = await run.wait();
      } finally {
        await agent[Symbol.asyncDispose]();
      }
    } else {
      result = await Agent.prompt(prompt, opts);
    }
  } catch (err) {
    if (err instanceof CursorAgentError) {
      console.error(`[CodeBot] Agent error: ${err.message}`);
    } else {
      console.error("[CodeBot] Agent error:", err);
    }
    process.exit(1);
  }

  console.log(`[CodeBot] finished status=${result.status} id=${result.id}`);
  if (result.status !== "finished") {
    process.exit(1);
  }

  console.log("[CodeBot] Verifying compile…");
  const verify = verifySessionAfterAgent(hubRoot);
  for (const note of verify.notes) console.log(`[CodeBot:verify] ${note}`);

  if (!verify.ok) {
    console.error(formatVerifyFailureMessage(verify));
    process.exit(4);
  }

  const after = loadCodeProgress(hubRoot);
  console.log(
    `[CodeBot] Next task hint: ${pickActiveCodeTask(after)} | codeReady=${codeReady(after)}`
  );
  process.exit(0);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
