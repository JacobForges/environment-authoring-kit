/**
 * Verifies Cursor API + SDK setup before grade-and-fix automation.
 * Run: npm run doctor
 */
import { existsSync } from "node:fs";
import { join } from "node:path";
import { Agent, Cursor, CursorAgentError } from "@cursor/sdk";
import type { SDKMessage } from "@cursor/sdk";
import { loadCaveGraderEnv } from "./load-env.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../..")).replace(/\/$/, "");

function logStream(event: SDKMessage): void {
  if (event.type === "status") {
    console.error(`[status] ${event.status}${event.message ? `: ${event.message}` : ""}`);
  } else if (event.type === "tool_call" && event.status === "error") {
    console.error(`[tool error] ${event.name}`, event.result ?? "");
  }
}

async function smokeTestLocal(apiKey: string, modelId: string): Promise<{
  ok: boolean;
  status: string;
  id: string;
  detail?: string;
}> {
  const agent = await Agent.create({
    apiKey,
    local: { cwd: hubRoot, settingSources: [] },
    model: { id: modelId },
  });

  try {
    const run = await agent.send("Reply with exactly: CAVE_AGENT_OK");
    console.log(`  model=${modelId} run=${run.id}`);
    if (run.supports("stream")) {
      try {
        for await (const event of run.stream()) logStream(event);
      } catch (e) {
        console.error("  stream error:", e);
      }
    }
    const result = await run.wait();
    if (result.status === "finished") {
      return { ok: true, status: result.status, id: result.id, detail: result.result };
    }

    let detail = result.result ?? "";
    if (run.supports("conversation")) {
      try {
        const turns = await run.conversation();
        if (turns.length) detail += "\n" + JSON.stringify(turns.slice(-2), null, 2).slice(0, 4000);
      } catch {
        /* ignore */
      }
    }
    return { ok: false, status: result.status, id: result.id, detail };
  } finally {
    await agent[Symbol.asyncDispose]();
  }
}

async function main(): Promise<void> {
  console.log("=== Cave grader — Cursor automation doctor ===\n");

  const apiKey = process.env.CURSOR_API_KEY?.trim();
  if (!apiKey) {
    console.error("FAIL: CURSOR_API_KEY missing.");
    console.error("  Set in Tools/cave-grader/.env");
    console.error("  Key: https://cursor.com/dashboard/cloud-agents");
    process.exit(1);
  }
  console.log("OK: CURSOR_API_KEY present (" + apiKey.slice(0, 12) + "…)");
  console.log("OK: @cursor/sdk installed (npm install in this folder)\n");

  if (existsSync("/Applications/Cursor.app")) {
    console.log("OK: Cursor.app at /Applications/Cursor.app");
  } else {
    console.warn("WARN: Install Cursor from https://cursor.com");
  }

  console.log("\n--- API: list models ---");
  let modelIds: string[] = [];
  try {
    const models = await Cursor.models.list({ apiKey });
    modelIds = models.map((m) => m.id).filter(Boolean);
    console.log(
      "OK: API reachable. Models:",
      modelIds.slice(0, 12).join(", ") || "(empty — will try auto)"
    );
  } catch (e) {
    console.error("FAIL: Could not list models.");
    console.error(e);
    process.exit(1);
  }

  const preferred = process.env.CAVE_CURSOR_MODEL?.trim();
  const candidates = [
    ...(preferred ? [preferred] : []),
    "auto",
    "composer-2",
    ...modelIds.filter((id) => id !== preferred && id !== "auto" && id !== "composer-2"),
  ].filter((v, i, a) => v && a.indexOf(v) === i);

  console.log("\n--- Local agent smoke test ---");
  console.log("    Needs: Cursor desktop OPEN + signed in (same account as API key)");
  console.log("    Hub cwd:", hubRoot, "\n");

  let lastFail: { status: string; id: string; detail?: string } | null = null;
  for (const modelId of candidates.slice(0, 4)) {
    try {
      const r = await smokeTestLocal(apiKey, modelId);
      if (r.ok) {
        console.log("\nOK: Local agent works with model", modelId, r.id);
        if (r.detail) console.log("    ", String(r.detail).slice(0, 200));
        console.log("\n=== Ready: ./run-grade-and-fix.sh --auto --stream ===\n");
        process.exit(0);
      }
      lastFail = r;
      console.error(`FAIL: model ${modelId} → status=${r.status} ${r.id}`);
      if (r.detail) console.error(String(r.detail).slice(0, 2000));
    } catch (e) {
      if (e instanceof CursorAgentError) {
        console.error(`FAIL: model ${modelId} startup:`, e.message);
      } else {
        console.error(`FAIL: model ${modelId}:`, e);
      }
    }
  }

  console.error("\n=== Local executor not working (SDK installed, runtime failed) ===\n");
  if (lastFail) console.error("Last run:", lastFail.status, lastFail.id);

  console.error("\nFix local (try in order):");
  console.error("  1. Quit and reopen Cursor; confirm you are signed in");
  console.error("  2. File → Open Folder →", hubRoot);
  console.error("  3. Update Cursor to latest (Help → Check for Updates)");
  console.error("  4. In .env: CAVE_CURSOR_MODEL=auto");
  console.error("\nWorkaround (no local executor):");
  console.error("  • Paste Assets/EnvironmentKit/Generated/CaveBuildAgentPrompt.md into IDE Agent chat");
  console.error("  • Or cloud: set CAVE_CURSOR_REPO_URL in .env, then:");
  console.error("    ./run-grade-and-fix.sh --cloud --stream");
  console.error("\nNote: npm install only installs the TypeScript package.");
  console.error("      Local runs still need the Cursor app as the executor.\n");
  process.exit(2);
}

main();
