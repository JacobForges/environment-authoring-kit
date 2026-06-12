/**
 * Batch-rewrite demo recap captions with the same external AI provider as cave-grader.
 * argv[2]: path to JSON { buildMode, frames: [{ i, phase, sub, line1, line2, line3 }] }
 * stdout: { captions: [{ i, line1, line2, line3 }] }
 */
import "./planner-load-dotenv.ts";
import { Agent } from "@cursor/sdk";
import { readFileSync } from "node:fs";

type FrameIn = {
  i: number;
  phase?: string;
  sub?: string;
  line1?: string;
  line2?: string;
  line3?: string;
};

type Body = {
  buildMode?: string;
  frames?: FrameIn[];
};

type ProviderRunResult = { status: string; result?: string };

async function invokeOpenAiCompatible(args: {
  provider: string;
  prompt: string;
  modelId: string;
  apiKey: string;
  baseUrl: string;
}): Promise<ProviderRunResult> {
  const base =
    args.baseUrl ||
    (args.provider.toLowerCase().includes("openrouter")
      ? "https://openrouter.ai/api/v1"
      : "https://api.openai.com/v1");
  const url = `${base.replace(/\/$/, "")}/chat/completions`;
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (args.apiKey) headers.Authorization = `Bearer ${args.apiKey}`;
  if (args.provider.toLowerCase().includes("openrouter")) {
    headers["HTTP-Referer"] = "https://github.com/cursor/environment-authoring-kit";
    headers["X-Title"] = "Environment Kit Demo Recap";
  }
  const res = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify({
      model: args.modelId,
      messages: [{ role: "user", content: args.prompt }],
      temperature: 0.35,
      max_tokens: 8192,
    }),
  });
  const text = await res.text();
  if (!res.ok) return { status: "error", result: text.slice(0, 2000) };
  try {
    const json = JSON.parse(text) as { choices?: Array<{ message?: { content?: string } }> };
    return { status: "ok", result: json.choices?.[0]?.message?.content ?? text };
  } catch {
    return { status: "ok", result: text };
  }
}

async function invokeAnthropic(args: {
  prompt: string;
  modelId: string;
  apiKey: string;
  baseUrl: string;
}): Promise<ProviderRunResult> {
  const url = `${(args.baseUrl || "https://api.anthropic.com/v1").replace(/\/$/, "")}/messages`;
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
      max_tokens: 8192,
      temperature: 0.35,
      messages: [{ role: "user", content: args.prompt }],
    }),
  });
  const text = await res.text();
  if (!res.ok) return { status: "error", result: text.slice(0, 2000) };
  try {
    const json = JSON.parse(text) as { content?: Array<{ text?: string }> };
    return { status: "ok", result: json.content?.map((c) => c.text ?? "").join("\n").trim() };
  } catch {
    return { status: "ok", result: text };
  }
}

async function invokeGemini(args: {
  prompt: string;
  modelId: string;
  apiKey: string;
  baseUrl: string;
}): Promise<ProviderRunResult> {
  const base = (args.baseUrl || "https://generativelanguage.googleapis.com/v1beta").replace(/\/$/, "");
  const model = args.modelId || "gemini-2.5-flash";
  const keyParam = args.apiKey ? `?key=${encodeURIComponent(args.apiKey)}` : "";
  const url = `${base}/models/${encodeURIComponent(model)}:generateContent${keyParam}`;
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      contents: [{ role: "user", parts: [{ text: args.prompt }] }],
      generationConfig: { temperature: 0.35, maxOutputTokens: 8192 },
    }),
  });
  const text = await res.text();
  if (!res.ok) return { status: "error", result: text.slice(0, 2000) };
  try {
    const json = JSON.parse(text) as {
      candidates?: Array<{ content?: { parts?: Array<{ text?: string }> } }>;
    };
    return {
      status: "ok",
      result: json.candidates?.[0]?.content?.parts?.map((p) => p.text ?? "").join("\n").trim(),
    };
  } catch {
    return { status: "ok", result: text };
  }
}

async function invokeCursor(prompt: string, modelId: string, apiKey: string): Promise<ProviderRunResult> {
  const hubRoot = process.env.HUB_ROOT?.trim() || process.cwd();
  if (!apiKey) return { status: "error", result: "CURSOR_API_KEY / CAVE_ACTIVE_API_KEY required" };
  try {
    const agent = await Agent.create({
      apiKey,
      local: { cwd: hubRoot, settingSources: [] },
      model: { id: modelId || "auto" },
    });
    const run = await agent.send(prompt);
    const result = await run.wait();
    if (result.status === "error" || result.status === "cancelled") {
      return { status: "error", result: result.error ?? `agent ${result.status}` };
    }
    const text =
      typeof result.result === "string"
        ? result.result
        : result.messages?.map((m) => ("text" in m ? m.text : "")).join("\n").trim() ?? "";
    return { status: "ok", result: text };
  } catch (err) {
    return { status: "error", result: err instanceof Error ? err.message : String(err) };
  }
}

async function invokeProvider(prompt: string): Promise<ProviderRunResult> {
  const provider = (process.env.CAVE_AI_PROVIDER ?? "OpenAICompatible").toLowerCase();
  const modelId = process.env.CAVE_ACTIVE_MODEL ?? process.env.CAVE_CURSOR_MODEL ?? "gpt-4o-mini";
  const apiKey =
    process.env.CAVE_ACTIVE_API_KEY ??
    process.env.CURSOR_API_KEY ??
    process.env.OPENAI_API_KEY ??
    "";
  const baseUrl = process.env.CAVE_ACTIVE_BASE_URL ?? "";
  const args = { provider, prompt, modelId, apiKey, baseUrl };
  if (provider.includes("cursor")) return invokeCursor(prompt, modelId, apiKey);
  if (provider.includes("anthropic")) return invokeAnthropic(args);
  if (provider.includes("google") || provider.includes("gemini")) return invokeGemini(args);
  if (provider.includes("ollama") || provider.includes("lmstudio")) {
    return invokeOpenAiCompatible({
      ...args,
      apiKey: apiKey || "ollama",
      baseUrl: baseUrl || "http://localhost:11434/v1",
    });
  }
  return invokeOpenAiCompatible(args);
}

function buildPrompt(body: Body): string {
  const frames = body.frames ?? [];
  const buildMode = body.buildMode ?? "unknown";
  const lines = frames.map((f) => {
    const draft = [f.line1, f.line2, f.line3].filter(Boolean).join(" | ");
    return `- i=${f.i} phase="${f.phase ?? ""}" sub="${f.sub ?? ""}" draft="${draft}"`;
  });
  return `You write voiceover captions for a Unity Editor world-build recap video (terrain + mountains + caves).

Build mode: ${buildMode}

For EACH milestone below, output JSON captions in the voice of a university professor teaching procedural world authoring:
- line1: lecture headline — what the student should notice in the Scene view now (max 88 chars)
- line2: teach the WHY — pipeline ordering, invariants, failure modes, playability (max 155 chars)
- line3: concrete lab note — what to compare frame-to-frame, counts, scope, next dependency (max 120 chars, else "")

Rules:
- Do NOT paste raw log tags, [Cave|Queue], "Step N", load×, or file paths.
- Sound like a patient professor: precise, causal, slightly formal but clear — not hype or marketing.
- Pack teaching into line2/line3; avoid empty platitudes.
- Keep facts consistent with phase/sub; infer sensible pedagogy from context when needed.
- Return ONLY valid JSON, no markdown:

{"captions":[{"i":0,"line1":"...","line2":"...","line3":""}, ...]}

Milestones:
${lines.join("\n")}
`;
}

function extractJson(text: string): unknown {
  const start = text.indexOf("{");
  const end = text.lastIndexOf("}");
  if (start < 0 || end <= start) throw new Error("no JSON in model output");
  return JSON.parse(text.slice(start, end + 1));
}

async function main() {
  const inputPath = process.argv[2];
  if (!inputPath) {
    console.error("usage: demo-recap-narrate.ts <request.json>");
    process.exit(1);
  }
  const raw = readFileSync(inputPath, "utf8");
  const body = JSON.parse(raw) as Body;
  if (!body.frames?.length) {
    process.stdout.write(JSON.stringify({ captions: [] }));
    return;
  }

  const chunkSize = 35;
  const all: Array<{ i: number; line1: string; line2: string; line3: string }> = [];

  for (let offset = 0; offset < body.frames.length; offset += chunkSize) {
    const chunk = body.frames.slice(offset, offset + chunkSize);
    const prompt = buildPrompt({ buildMode: body.buildMode, frames: chunk });
    const run = await invokeProvider(prompt);
    if (run.status !== "ok" || !run.result) {
      console.error(run.result ?? "provider error");
      process.exit(1);
    }
    const parsed = extractJson(run.result) as {
      captions?: Array<{ i: number; line1?: string; line2?: string; line3?: string }>;
    };
    for (const c of parsed.captions ?? []) {
      all.push({
        i: c.i,
        line1: c.line1 ?? "",
        line2: c.line2 ?? "",
        line3: c.line3 ?? "",
      });
    }
  }

  process.stdout.write(JSON.stringify({ captions: all }));
}

main().catch((err) => {
  console.error(err instanceof Error ? err.message : String(err));
  process.exit(1);
});
