/**
 * Cursor AI mesh design for planner prop cards.
 * Returns JSON spec consumed by Unity CaveBuildPlannerGeneratedProps.
 *
 * Usage: npx tsx planner-mesh-gen.ts <request.json>
 */
import "./planner-load-dotenv.ts";
import { readFileSync } from "node:fs";
import { Agent } from "@cursor/sdk";

type MeshGenRequest = {
  hubRoot: string;
  label: string;
  slotLabel?: string;
  kind: string;
  categoryKey?: string;
  cardType?: string;
  regenIndex?: number;
  briefSnippet?: string;
};

type MeshGenResponse = {
  kind: string;
  variant: number;
  height: number;
  canopyScale: number;
  rockRadius: number;
  trunkColor: { r: number; g: number; b: number };
  leafColor: { r: number; g: number; b: number };
  albedoColor: { r: number; g: number; b: number };
  styleNotes: string;
};

const reqPath = process.argv[2];
if (!reqPath) {
  console.log(JSON.stringify({ error: "usage: planner-mesh-gen.ts <request.json>" }));
  process.exit(1);
}

const req = JSON.parse(readFileSync(reqPath, "utf8")) as MeshGenRequest;
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

const prompt = `You are a photorealistic 3D environment artist designing ONE hyper-realistic prop for a Florida floating-islands game (URP PBR).

Prop label: ${req.label}
Slot: ${req.slotLabel || req.label}
Category: ${req.kind} (${req.categoryKey || "prop"}) · cardType=${req.cardType || "prop"}
Variation #: ${regen}
${req.briefSnippet ? `World brief: ${req.briefSnippet}` : ""}

Design variation #${regen} with believable micro-detail, natural imperfection, and cinematic lighting response.
Do NOT reference third-party asset store kits.
Return ONLY valid JSON (no markdown):
{
  "kind": "${req.kind}",
  "variant": <0-3 integer>,
  "height": <0.5-3.5 float>,
  "canopyScale": <0.6-2.2 float tree/bush only else 1.0>,
  "rockRadius": <0.4-1.4 float — for orb use 0.5-1.0 as sphere radius scale>,
  "roughness": <0.15-0.95 float PBR roughness>,
  "normalStrength": <0.4-1.6 float surface detail>,
  "trunkColor": {"r":0-1,"g":0-1,"b":0-1},
  "leafColor": {"r":0-1,"g":0-1,"b":0-1},
  "albedoColor": {"r":0-1,"g":0-1,"b":0-1},
  "styleNotes": "<one sentence photoreal art direction>"
}

Rules:
- Hyper-realistic Florida karst flora/geology — not stylized/low-poly
- kind must stay "${req.kind}"
- Trees: bark grain, volumetric canopy, wind-sway rig implied
- Grass: dense blade clumps, subsurface green, brown thatch at base
- Rock: mossy cracks, weathered limestone, varied roughness
- Orb/collectible: glowing pickup sphere — emissive cyan/gold/gem tones, readable at gameplay scale, NOT furniture/lamps
- Colors linear 0-1 RGB. Vary clearly from prior variations.`;

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
  const spec = JSON.parse(raw) as MeshGenResponse;
  console.log(JSON.stringify({ ok: true, spec }));
} catch (e) {
  const msg = e instanceof Error ? e.message : String(e);
  console.log(JSON.stringify({ error: msg }));
  process.exit(1);
}
