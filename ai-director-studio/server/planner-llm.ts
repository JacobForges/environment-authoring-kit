/**
 * Cursor LLM for AI Build Planner — prompt (fast) or streamed agent.send.
 * Usage: npx tsx planner-llm.ts <request.json>
 *
 * Stream mode prints NDJSON lines to stdout:
 *   {"type":"delta","text":"..."}
 *   {"type":"done","text":"full accumulated"}
 */
import "./planner-load-dotenv.ts";
import { readFileSync } from "node:fs";
import { Agent } from "@cursor/sdk";

type PlannerRequest = {
  hubRoot: string;
  system: string;
  messages: Array<{ role: string; content: string }>;
  mode?: "prompt" | "agent";
  stream?: boolean;
  /** When true, model returns plain chat text (auto-responder user bot). */
  plainText?: boolean;
};

const reqPath = process.argv[2];
if (!reqPath) {
  console.log(JSON.stringify({ error: "usage: planner-llm.ts <request.json>" }));
  process.exit(1);
}

const req = JSON.parse(readFileSync(reqPath, "utf8")) as PlannerRequest;
const studioRoot =
  process.env.STUDIO_ROOT?.trim() ||
  new URL("..", import.meta.url).pathname.replace(/\/$/, "");
const envFile = `${studioRoot}/.env`;
const apiKey = process.env.CURSOR_API_KEY?.trim();
if (!apiKey) {
  console.log(
    JSON.stringify({
      error: `CURSOR_API_KEY missing — set it in ${envFile}`,
    })
  );
  process.exit(1);
}

const hubRoot = req.hubRoot?.trim() || process.env.STUDIO_ROOT?.trim() || process.cwd();
const modelId = process.env.CAVE_CURSOR_MODEL?.trim() || "composer-2.5";

const conversation = (req.messages ?? [])
  .map((m) => `${m.role.toUpperCase()}: ${m.content}`)
  .join("\n\n");

const prompt = req.plainText
  ? `${req.system}

## Planner message
${conversation}

Write one concise user chat reply (plain text only, no JSON).`
  : `${req.system}

## Conversation so far
${conversation}

Respond with valid JSON only (no markdown fence).`;

const opts = {
  apiKey,
  model: { id: modelId },
  local: { cwd: hubRoot, settingSources: [] as const },
};

function emit(line: Record<string, unknown>) {
  // Line-delimited JSON; force flush when piped to Python (otherwise deltas batch until exit).
  process.stdout.write(`${JSON.stringify(line)}\n`);
  const stdout = process.stdout as NodeJS.WriteStream & {
    _handle?: { setBlocking?: (blocking: boolean) => void };
  };
  try {
    stdout._handle?.setBlocking?.(true);
  } catch {
    /* optional on some runtimes */
  }
}

async function runStreamed(): Promise<string> {
  await using agent = await Agent.create(opts);
  const run = await agent.send(prompt);
  let accumulated = "";

  for await (const event of run.stream()) {
    if (event.type !== "assistant") continue;
    for (const block of event.message.content) {
      if (block.type !== "text" || !block.text) continue;
      accumulated += block.text;
      emit({ type: "delta", text: block.text, accumulated });
    }
  }

  const result = await run.wait();
  if (result.status === "error" || result.status === "cancelled") {
    emit({ type: "error", error: result.error ?? `Cursor ${result.status}` });
    process.exit(1);
  }

  const final =
    typeof result.result === "string" && result.result.trim()
      ? result.result
      : accumulated.trim();

  emit({ type: "done", text: final, mode: "stream" });
  return final;
}

try {
  if (req.stream) {
    await runStreamed();
    process.exit(0);
  }

  const result =
    req.mode === "agent"
      ? await (async () => {
          await using agent = await Agent.create(opts);
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
