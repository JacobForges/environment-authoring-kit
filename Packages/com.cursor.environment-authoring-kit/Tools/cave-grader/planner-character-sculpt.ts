/**
 * Cursor AI character morph/sculpt for planner NPC / enemy / player cards.
 *
 * Usage: npx tsx planner-character-sculpt.ts <request.json>
 */
import "./planner-load-dotenv.ts";
import { readFileSync } from "node:fs";
import { Agent } from "@cursor/sdk";

type SculptRequest = {
  hubRoot: string;
  label: string;
  cardType: string;
  sculptHint: string;
  regenIndex?: number;
  briefSnippet?: string;
};

type SculptResponse = {
  heightScale: number;
  buildScale: number;
  shoulderScale: number;
  headScale: number;
  limbScale: number;
  skinTint: { r: number; g: number; b: number };
  clothTint: { r: number; g: number; b: number };
  styleNotes: string;
};

const reqPath = process.argv[2];
if (!reqPath) {
  console.log(JSON.stringify({ error: "usage: planner-character-sculpt.ts <request.json>" }));
  process.exit(1);
}

const req = JSON.parse(readFileSync(reqPath, "utf8")) as SculptRequest;
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
const regen = Math.max(1, Number(req.regenIndex) || 1);
const hint = (req.sculptHint || "").trim();
const cardType = (req.cardType || "npc").toLowerCase();

const prompt = `You are a character art director morphing ONE game character for a Florida karst floating-islands demo (URP).

Character: ${req.label}
Role: ${cardType}
User sculpt hint (1–2 words): "${hint}"
Variation #: ${regen}
${req.briefSnippet ? `World brief: ${req.briefSnippet}` : ""}

Translate the hint into believable body proportions and color direction.
Return ONLY valid JSON (no markdown):
{
  "heightScale": <0.72-1.35 float>,
  "buildScale": <0.75-1.4 float bulk/mass>,
  "shoulderScale": <0.8-1.35 float>,
  "headScale": <0.82-1.22 float>,
  "limbScale": <0.85-1.2 float>,
  "skinTint": {"r":0-1,"g":0-1,"b":0-1},
  "clothTint": {"r":0-1,"g":0-1,"b":0-1},
  "styleNotes": "<one sentence>"
}

Rules:
- Honor "${hint}" clearly (e.g. "tall lanky" → height↑ build↓, "stocky guard" → build↑ shoulders↑).
- ${cardType === "enemy" ? "Enemy: slightly menacing palette, darker cloth." : cardType === "player" ? "Player hero: readable silhouette, warm skin, distinct outfit." : "NPC: friendly readable silhouette."}
- Colors linear 0-1 RGB. Vary from prior variations.`;

try {
  const result = await Agent.prompt(prompt, {
    apiKey,
    model: { id: modelId },
    local: { cwd: hubRoot, settingSources: [] },
  });

  if (result.status === "error" || result.status === "cancelled") {
    console.log(JSON.stringify({ error: result.error ?? `Cursor ${result.status}` }));
    process.exit(1);
  }

  let raw = (typeof result.result === "string" ? result.result : "").trim();
  const fence = raw.match(/```(?:json)?\s*([\s\S]*?)```/);
  if (fence) raw = fence[1].trim();
  const spec = JSON.parse(raw) as SculptResponse;
  console.log(JSON.stringify({ ok: true, spec }));
} catch (e) {
  const msg = e instanceof Error ? e.message : String(e);
  console.log(JSON.stringify({ error: msg }));
  process.exit(1);
}
