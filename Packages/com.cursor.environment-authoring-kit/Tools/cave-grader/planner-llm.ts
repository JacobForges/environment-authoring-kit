/**
 * Cursor LLM for AI Build Planner — uses Agent.prompt (fast) by default.
 * Usage: npx tsx planner-llm.ts <request.json>
 */
import { readFileSync } from "node:fs";
import { Agent } from "@cursor/sdk";

type PlannerRequest = {
  hubRoot: string;
  system: string;
  messages: Array<{ role: string; content: string }>;
  mode?: "prompt" | "agent";
};

const reqPath = process.argv[2];
if (!reqPath) {
  console.log(JSON.stringify({ error: "usage: planner-llm.ts <request.json>" }));
  process.exit(1);
}

const req = JSON.parse(readFileSync(reqPath, "utf8")) as PlannerRequest;
const apiKey = process.env.CURSOR_API_KEY?.trim();
if (!apiKey) {
  console.log(
    JSON.stringify({
      error: "CURSOR_API_KEY missing — add it to Packages/.../Tools/cave-grader/.env",
    })
  );
  process.exit(1);
}

const hubRoot = req.hubRoot?.trim() || process.env.HUB_ROOT?.trim() || process.cwd();
const modelId = process.env.CAVE_CURSOR_MODEL?.trim() || "composer-2.5";

const conversation = (req.messages ?? [])
  .map((m) => `${m.role.toUpperCase()}: ${m.content}`)
  .join("\n\n");

const prompt = `${req.system}

## Conversation so far
${conversation}

Respond with valid JSON only (no markdown fence).`;

const opts = {
  apiKey,
  model: { id: modelId },
  local: { cwd: hubRoot, settingSources: [] as const },
};

try {
  const result =
    req.mode === "agent"
      ? await (async () => {
          const agent = await Agent.create(opts);
          const run = await agent.send(prompt);
          return await run.wait();
        })()
      : await Agent.prompt(prompt, opts);

  if (result.status === "error" || result.status === "cancelled") {
    console.log(
      JSON.stringify({
        error: result.error ?? `Cursor ${result.status}`,
      })
    );
    process.exit(1);
  }
  const text =
    typeof result.result === "string"
      ? result.result
      : result.messages?.map((m) => ("text" in m ? m.text : "")).join("\n").trim() ?? "";
  console.log(JSON.stringify({ text, mode: req.mode ?? "prompt" }));
} catch (err) {
  console.log(
    JSON.stringify({
      error: err instanceof Error ? err.message : String(err),
    })
  );
  process.exit(1);
}
