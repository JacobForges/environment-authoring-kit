import { GoogleGenAI } from "@google/genai";
import { loadSettings } from "./settings.ts";

const FALLBACK_REPLIES: Record<string, string[]> = {
  battle: [
    "TACTICAL OBSERVER: Agent brakes too long in high-velocity curves. Clone rows with reward >= 1.5.",
    "STRATEGY: Focus battle_aggressive samples with high accel and no damage spikes.",
  ],
  follow: [
    "CALIBRATION: Follower lag ~0.4s — train on matched speed profiles.",
    "LEDGER: Distance variance spikes near tight turns.",
  ],
  explore: [
    "LAB: Exploration coverage low — clone curiosity-positive explore rows.",
    "Avoid WatchReplay poison — manual explore only.",
  ],
  chat: ["INTENT: Train cooperative chat sequences.", "Vocabulary rarity spikes in greet segment."],
  reasoning: ["AUDIT: Risk margin drift — train 90+ step reasoning logs.", "Override adapter if loss > 2.4."],
};

export function rulesCoach(focus: string, userPrompt: string) {
  const category = (focus || "battle").toLowerCase();
  const replies = FALLBACK_REPLIES[category] || FALLBACK_REPLIES.battle;
  const responseText = replies[Math.floor(Math.random() * replies.length)];
  return {
    text: `[DTA OFFLINE COACH]\n\n**FACTS:** ${responseText}\n\n**STRATEGY:** Collect 24+ labeled rows for '${category}', reject WatchReplay spikes, then Train Agent in-game.\n\n_User asked:_ ${userPrompt}`,
    confidence: "MEDIUM",
    provenance: "rules-only",
  };
}

async function localGgufCoach(endpoint: string, systemPrompt: string, userPrompt: string) {
  const res = await fetch(endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      model: "local",
      messages: [
        { role: "system", content: systemPrompt },
        { role: "user", content: userPrompt },
      ],
      temperature: 0.7,
    }),
    signal: AbortSignal.timeout(60_000),
  });
  if (!res.ok) throw new Error(`Local LLM error: ${res.status}`);
  const json = (await res.json()) as { choices?: Array<{ message?: { content?: string } }> };
  const text = json.choices?.[0]?.message?.content || "No local model response.";
  return { text, confidence: "HIGH", provenance: "local-gguf" };
}

async function cursorCoach(apiKey: string, systemPrompt: string, userPrompt: string) {
  const { Agent } = await import("@cursor/sdk");
  const result = await Agent.prompt(
    `${systemPrompt}\n\n---\nPlayer question:\n${userPrompt}`,
    {
      apiKey,
      model: { id: "composer-2.5" },
      local: { cwd: process.cwd() },
    },
  );
  const text =
    (typeof result.result === "string" && result.result) ||
    (typeof result.status === "string" ? result.status : "") ||
    "Cursor coach returned no text.";
  return { text, confidence: "HIGH", provenance: "cursor-api" };
}

async function geminiCoach(apiKey: string, systemPrompt: string, userPrompt: string, chatHistory: Array<{ role: string; text: string }>, focus: string) {
  const ai = new GoogleGenAI({ apiKey });
  const contents = [];
  for (const h of chatHistory || []) {
    contents.push({ role: h.role === "user" ? "user" : "model", parts: [{ text: h.text }] });
  }
  contents.push({
    role: "user",
    parts: [{ text: userPrompt || `How should I train for ${focus || "battle"}?` }],
  });

  const response = await ai.models.generateContent({
    model: "gemini-2.0-flash",
    contents,
    config: { systemInstruction: systemPrompt, temperature: 0.7 },
  });

  return {
    text: response.text || "No advice formulated.",
    confidence: "HIGH",
    provenance: "gemini-user-key",
  };
}

export async function runCoach(body: {
  agentContext?: unknown;
  chatHistory?: Array<{ role: string; text: string }>;
  userPrompt?: string;
  focus?: string;
  engine?: string;
}) {
  const settings = loadSettings();
  const engine = (body.engine || settings.coachEngine || "auto") as string;
  const focus = body.focus || "battle";
  const userPrompt = body.userPrompt || `How should I train for ${focus}?`;

  const systemPrompt = `You are the chief AI training scientist for Deep Train Academy (Unity 6).
Guide the player on behavior cloning for squadmate agents. Use markdown. Max 3 short paragraphs.
Split into FACTS (objective bullets) and COACHING STRATEGY (actionable steps).

Agent context:
${JSON.stringify(body.agentContext || {}, null, 2)}

Focus: ${focus}`;

  if (engine === "rules-only") {
    return rulesCoach(focus, userPrompt);
  }

  if (engine === "local-gguf") {
    try {
      return await localGgufCoach(settings.localGgufEndpoint, systemPrompt, userPrompt);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "local failed";
      return { text: `[Local LLM error] ${msg}\n\n${rulesCoach(focus, userPrompt).text}`, confidence: "LOW", provenance: "local-fallback" };
    }
  }

  const cursorKey = settings.cursorApiKey || process.env.CURSOR_API_KEY || "";
  const geminiKey = settings.geminiApiKey || process.env.GEMINI_API_KEY || "";

  const tryCursor = engine === "cursor" || engine === "auto";
  const tryGemini = engine === "gemini-3.5-flash" || engine === "auto";

  if (tryCursor && cursorKey) {
    try {
      return await cursorCoach(cursorKey, systemPrompt, userPrompt);
    } catch (e: unknown) {
      if (engine === "cursor") throw e;
      console.warn("[DTA Coach] Cursor failed, falling back:", e);
    }
  }

  if (tryGemini && geminiKey) {
    try {
      return await geminiCoach(geminiKey, systemPrompt, userPrompt, body.chatHistory || [], focus);
    } catch (e: unknown) {
      if (engine === "gemini-3.5-flash") throw e;
      console.warn("[DTA Coach] Gemini failed, falling back:", e);
    }
  }

  return rulesCoach(focus, userPrompt);
}
