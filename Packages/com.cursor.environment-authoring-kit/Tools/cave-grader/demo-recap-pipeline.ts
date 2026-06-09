/**
 * AI director + quality rubric/ladder for demo recap (cave-grader only).
 *
 * Usage:
 *   node --import tsx demo-recap-pipeline.ts director <request.json> > DemoRecapDirector.json
 *   node --import tsx demo-recap-pipeline.ts milestones <request.json>  # parallel caption+vision per PNG
 *   node --import tsx demo-recap-pipeline.ts vision <vision-request.json> > DemoRecapVision.json
 *   node --import tsx demo-recap-pipeline.ts grade <director.json> > DemoRecapQuality.json
 *   node --import tsx demo-recap-pipeline.ts narration <request.json> > DemoRecapFullNarration.json
 *
 * Env: CAVE_RECAP_CONCURRENCY (default 4, max 8) — parallel Cursor agent calls
 */
import { readFileSync, writeFileSync } from "node:fs";
import { Agent } from "@cursor/sdk";

type Region = {
  kind: "ellipse" | "rect" | "arrow";
  box?: [number, number, number, number];
  from?: [number, number];
  to?: [number, number];
  label: string;
};

type MilestoneIn = {
  i: number;
  frame: number;
  imagePath?: string;
  beatKind?: "checkpoint" | "subbeat";
  subAction?: string;
  chapter?: string;
  phase?: string;
  sub?: string;
  line1?: string;
  line2?: string;
  line3?: string;
};

type MilestoneOut = MilestoneIn & {
  line1: string;
  line2: string;
  line3: string;
  chapter: string;
  regions: Region[];
  teachingFocus: string;
};

type DirectorBody = {
  buildMode: string;
  milestones: MilestoneIn[];
  critique?: string;
  iteration?: number;
};

type QualityReport = {
  pass: boolean;
  overallScore: number;
  rung: string;
  rungs: Array<{ id: string; score: number; pass: boolean; notes: string[] }>;
  revisionPrompt: string;
};

type ProviderRunResult = { status: string; result?: string };

async function invokeOpenAiCompatible(args: {
  prompt: string;
  modelId: string;
  apiKey: string;
  baseUrl: string;
}): Promise<ProviderRunResult> {
  const base =
    args.baseUrl ||
    (process.env.CAVE_AI_PROVIDER?.toLowerCase().includes("openrouter")
      ? "https://openrouter.ai/api/v1"
      : "https://api.openai.com/v1");
  const url = `${base.replace(/\/$/, "")}/chat/completions`;
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (args.apiKey) headers.Authorization = `Bearer ${args.apiKey}`;
  const res = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify({
      model: args.modelId,
      messages: [{ role: "user", content: args.prompt }],
      temperature: 0.35,
      max_tokens: 12000,
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

async function invokeCursor(prompt: string, modelId: string, apiKey: string): Promise<ProviderRunResult> {
  const hubRoot = process.env.HUB_ROOT?.trim() || process.cwd();
  if (!apiKey) return { status: "error", result: "missing API key" };
  try {
    const agent = await Agent.create({
      apiKey,
      local: { cwd: hubRoot, settingSources: [] },
      model: { id: modelId || "auto" },
    });
    const run = await agent.send(prompt);
    const result = await run.wait();
    if (result.status === "error" || result.status === "cancelled") {
      return { status: "error", result: result.error ?? result.status };
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
  const provider = (
    process.env.CAVE_AI_PROVIDER ??
    (process.env.CURSOR_API_KEY ? "Cursor" : "OpenAICompatible")
  ).toLowerCase();
  const modelId = process.env.CAVE_ACTIVE_MODEL ?? process.env.CAVE_CURSOR_MODEL ?? "gpt-4o-mini";
  const apiKey =
    process.env.CAVE_ACTIVE_API_KEY ?? process.env.CURSOR_API_KEY ?? process.env.OPENAI_API_KEY ?? "";
  const baseUrl = process.env.CAVE_ACTIVE_BASE_URL ?? "";
  if (provider.includes("cursor")) return invokeCursor(prompt, modelId, apiKey);
  return invokeOpenAiCompatible({ prompt, modelId, apiKey, baseUrl });
}

function recapConcurrency(): number {
  const n = Number(process.env.CAVE_RECAP_CONCURRENCY ?? "4");
  return Math.min(8, Math.max(1, Number.isFinite(n) ? n : 4));
}

/** Run async work on items with a fixed concurrency cap (order preserved). */
async function mapPool<T, R>(
  items: T[],
  limit: number,
  fn: (item: T, index: number) => Promise<R>
): Promise<R[]> {
  if (!items.length) return [];
  const out = new Array<R>(items.length);
  let next = 0;
  const workers = Math.min(limit, items.length);
  async function worker() {
    while (true) {
      const i = next++;
      if (i >= items.length) return;
      out[i] = await fn(items[i], i);
    }
  }
  await Promise.all(Array.from({ length: workers }, () => worker()));
  return out;
}

function extractJson(text: string): unknown {
  const start = text.indexOf("{");
  const end = text.lastIndexOf("}");
  if (start < 0 || end <= start) throw new Error("no JSON in model output");
  return JSON.parse(text.slice(start, end + 1));
}

const BANNED = [
  /~\d+% of this recording/i,
  /observe the scene view at/i,
  /surface_build/i,
  /editor queue \+ capture/i,
  /\[cave\|/i,
  /step \d+\/\d+/i,
];

function buildDirectorPrompt(body: DirectorBody): string {
  const lines = body.milestones.map((m) => {
    const draft = [m.line1, m.line2, m.line3].filter(Boolean).join(" | ");
    return `- i=${m.i} frame=${m.frame} phase="${m.phase ?? ""}" sub="${m.sub ?? ""}" chapter="${m.chapter ?? ""}" draft="${draft}"`;
  });
  const critique = body.critique?.trim()
    ? `\n## REVISION (iteration ${body.iteration ?? 1})\nFix these issues from the quality ladder:\n${body.critique}\n`
    : "";

  return `You are the director for a Unity world-build lecture recap video.

Build mode: ${body.buildMode}
${critique}
For EACH milestone, output professor lecture captions AND scene annotations that appear 3 seconds AFTER the caption (annotations must match what line1 discusses).

Caption rules:
- line1: vivid lecture headline — what to look at in the Scene view (max 88 chars). NO percentages, NO "observe at ~N%", NO raw phase/log strings.
- line2: teach WHY in complete sentences (max 155 chars). No "WHY" label text.
- line3: optional lab note (max 100 chars) or "".
- chapter: short topic title (max 40 chars), not metadata.

Annotation rules (max 2 regions per milestone):
- Normalized box coords 0..1 in SCENE space (top of frame = 0, bottom of scene area ≈ 0.78; y excludes caption bar).
- Pick ONE primary focus; add second only if caption compares two areas.
- kind: "ellipse" or "rect" (prefer ellipse for disks/trails; rect for annex/grid).
- label: 2–4 words, must match nouns in line1.
- If nothing to highlight, regions: [].

teachingFocus: snake_case tag (e.g. trail_bench, play_disk, grid_seam, labyrinth_annex).

Return ONLY JSON:
{"milestones":[{"i":0,"line1":"...","line2":"...","line3":"","chapter":"...","teachingFocus":"...","regions":[{"kind":"ellipse","box":[0.3,0.2,0.7,0.55],"label":"Play disk"}]}]}

Milestones:
${lines.join("\n")}
`;
}

function buildGradePrompt(director: { buildMode?: string; milestones: MilestoneOut[] }): string {
  const sample = director.milestones.map((m) => ({
    i: m.i,
    line1: m.line1,
    line2: m.line2,
    regions: m.regions,
    chapter: m.chapter,
  }));
  return `Grade this demo recap director output on a 0–100 rubric.

Ladder rungs (each 0–25 points):
1. captions — professor voice, no banned metadata phrases, line1/line2 substantive
2. annotations — ≤2 regions, boxes in 0..1, labels match line1 nouns, not overlapping clutter
3. pedagogy — chapters distinct, teachingFocus sensible, no duplicate boilerplate across holds
4. pacing — suitable for caption-then-annotate (annotations imply a single focal subject)

Banned if present in line1/line2: "~% of this recording", "surface_build", "observe the Scene view at ~"

Return ONLY JSON:
{"pass":true,"overallScore":85,"rungs":[{"id":"captions","score":22,"pass":true,"notes":[]}],"revisionPrompt":"..."}

pass=true only if overallScore>=78 and no rung below 15.

Director output:
${JSON.stringify(sample, null, 0)}
`;
}

function sanitizeRegions(raw: unknown): Region[] {
  if (!Array.isArray(raw)) return [];
  const out: Region[] = [];
  for (const r of raw) {
    if (!r || typeof r !== "object") continue;
    const o = r as Record<string, unknown>;
    const label = String(o.label ?? "").trim();
    if (!label) continue;
    const kind = o.kind === "rect" ? "rect" : o.kind === "arrow" ? "arrow" : "ellipse";
    if (kind === "arrow" && Array.isArray(o.from) && Array.isArray(o.to)) {
      out.push({
        kind: "arrow",
        from: o.from as [number, number],
        to: o.to as [number, number],
        label,
      });
    } else if (Array.isArray(o.box) && o.box.length === 4) {
      const box = o.box.map((n) => Math.min(1, Math.max(0, Number(n)))) as [
        number,
        number,
        number,
        number,
      ];
      if (box[2] > box[0] && box[3] > box[1]) out.push({ kind, box, label });
    }
    if (out.length >= 2) break;
  }
  return out;
}

function localCaptionRung(milestones: MilestoneOut[]): { score: number; pass: boolean; notes: string[] } {
  const notes: string[] = [];
  let pts = 25;
  for (const m of milestones) {
    const blob = `${m.line1} ${m.line2}`;
    for (const rx of BANNED) {
      if (rx.test(blob)) {
        notes.push(`m${m.i}: banned phrase pattern`);
        pts -= 4;
      }
    }
    if (!m.line1 || m.line1.length < 12) {
      notes.push(`m${m.i}: weak line1`);
      pts -= 3;
    }
  }
  const score = Math.max(0, Math.min(25, pts));
  return { score, pass: score >= 18, notes };
}

function localAnnotationRung(milestones: MilestoneOut[]): { score: number; pass: boolean; notes: string[] } {
  const notes: string[] = [];
  let pts = 25;
  for (const m of milestones) {
    if (m.regions.length > 2) {
      notes.push(`m${m.i}: too many regions`);
      pts -= 5;
    }
    for (const r of m.regions) {
      if (r.box && (r.box[2] - r.box[0] > 0.85 || r.box[3] - r.box[1] > 0.85)) {
        notes.push(`m${m.i}: region too large`);
        pts -= 3;
      }
    }
  }
  const score = Math.max(0, Math.min(25, pts));
  return { score, pass: score >= 18, notes };
}

type VisionIn = {
  buildMode: string;
  milestones: Array<{
    i: number;
    imagePath: string;
    line1?: string;
    line2?: string;
    chapter?: string;
    teachingFocus?: string;
  }>;
};

function buildVisionPrompt(m: VisionIn["milestones"][0]): string {
  return `You are a vision director for a Unity world-build lecture recap (Cursor API / local agent).

TASK: Open and READ the image file on disk, then place ONE tight annotation for the lecture caption.

Image path (read this file):
${m.imagePath}

Caption headline: ${m.line1 ?? ""}
Caption body: ${m.line2 ?? ""}
Chapter: ${m.chapter ?? ""}

Rules:
- Look at the actual terrain/scene in the PNG — do not guess from text alone.
- Return at most ONE region (two only if caption explicitly compares two areas).
- Normalized box [x0,y0,x1,y1] in scene space (0=left/top, 1=right/bottom). Scene area is roughly y=0.05..0.78 (above caption bar).
- kind: "ellipse" or "rect"
- label: 2–4 words matching visible feature (trail, grid, disk, annex, etc.)

Return ONLY JSON for milestone i=${m.i}:
{"i":${m.i},"regions":[{"kind":"ellipse","box":[0.3,0.2,0.7,0.55],"label":"Play disk"}]}
`;
}

function parseVisionResult(
  m: VisionIn["milestones"][0],
  run: ProviderRunResult
): { i: number; regions: Region[] } | null {
  if (run.status !== "ok" || !run.result) return null;
  try {
    const parsed = extractJson(run.result) as { i?: number; regions?: Region[] };
    return {
      i: Number(parsed.i ?? m.i),
      regions: sanitizeRegions(parsed.regions),
    };
  } catch {
    return null;
  }
}

async function runVision(body: VisionIn): Promise<{ milestones: Array<{ i: number; regions: Region[] }> }> {
  const limit = recapConcurrency();
  const total = body.milestones.length;
  process.stderr.write(`[vision] parallel ×${limit} for ${total} milestones\n`);

  const rows = await mapPool(body.milestones, limit, async (m, idx) => {
    const prompt = buildVisionPrompt(m);
    const run = await invokeProvider(prompt);
    process.stderr.write(`[vision] ${idx + 1}/${total} i=${m.i} ${run.status}\n`);
    return parseVisionResult(m, run);
  });
  return { milestones: rows.filter((r): r is { i: number; regions: Region[] } => r != null) };
}

function mergeDirectorChunk(chunk: MilestoneIn[], parsed: MilestoneOut[]): MilestoneOut[] {
  const out: MilestoneOut[] = [];
  for (const m of parsed) {
    const i = Number(m.i);
    const src = chunk.find((c) => c.i === i) ?? chunk[0];
    out.push({
      ...src,
      i,
      frame: src.frame,
      beatKind: src.beatKind ?? m.beatKind,
      line1: String(m.line1 ?? "").slice(0, src.beatKind === "subbeat" ? 72 : 88),
      line2: String(m.line2 ?? "").slice(0, src.beatKind === "subbeat" ? 120 : 155),
      line3: String(m.line3 ?? "").slice(0, 100),
      chapter: String(m.chapter ?? src.chapter ?? "Topic").slice(0, 40),
      teachingFocus: String(m.teachingFocus ?? "focus").slice(0, 40),
      regions: sanitizeRegions(m.regions),
    });
  }
  return out;
}

function buildUnifiedMilestonePrompt(body: DirectorBody, m: MilestoneIn): string {
  const critique = body.critique?.trim()
    ? `\n## REVISION (iteration ${body.iteration ?? 1})\n${body.critique}\n`
    : "";
  const draft = [m.line1, m.line2, m.line3].filter(Boolean).join(" | ");
  const kind = m.beatKind === "subbeat" ? "subbeat" : "checkpoint";
  const subAction = m.subAction?.trim() ? `\nSub-action in progress: ${m.subAction}` : "";

  if (kind === "subbeat") {
    return `You are narrating a SHORT sub-action beat between major checkpoints in a Unity world-build timelapse (Cursor API).

Build mode: ${body.buildMode}
${critique}
Open and READ this capture PNG (required):
${m.imagePath ?? ""}

Beat i=${m.i} kind=subbeat frame=${m.frame} phase="${m.phase ?? ""}" sub="${m.sub ?? ""}" chapter="${m.chapter ?? ""}"${subAction}
Prior draft="${draft}"

This beat fills a gap in the timelapse — explain what sub-step is visibly happening RIGHT NOW (queue drain, seam pass, sculpt stroke, etc.).

Caption rules:
- line1: present-tense micro-headline — the action underway (max 72 chars).
- line2: one sentence on why this step matters before the next checkpoint (max 120 chars).
- line3: always "".
- chapter: keep or shorten to max 40 (may start with "In progress ·").

Annotation rules:
- Prefer ONE tight region on the changing area; regions: [] only if nothing is visible yet.
- Normalized box [x0,y0,x1,y1], scene y≈0.05..0.78.

teachingFocus: snake_case tag.
narratorScript: one paragraph for VOICE only (max 320 chars) — top-tier YouTube dev-stream host: punchy, reactive, funny; NEVER read caption bullets aloud; no professor lecture.

Return ONLY JSON:
{"i":${m.i},"beatKind":"subbeat","line1":"...","line2":"...","line3":"","chapter":"...","teachingFocus":"...","narratorScript":"...","regions":[{"kind":"ellipse","box":[0.3,0.2,0.7,0.55],"label":"Seam pass"}]}
`;
  }

  return `You are the director + vision lead for ONE CHECKPOINT in a Unity world-build lecture recap (Cursor API).

Build mode: ${body.buildMode}
${critique}
Open and READ this capture PNG on disk (required):
${m.imagePath ?? ""}

Checkpoint i=${m.i} kind=checkpoint frame=${m.frame} phase="${m.phase ?? ""}" sub="${m.sub ?? ""}" chapter="${m.chapter ?? ""}" prior draft="${draft}"

TASK (one agent, one image):
1) Write professor captions from what you SEE in the PNG.
2) Place ONE tight annotation region for line1 (appears 3s after caption on screen).

Caption rules:
- line1: vivid lecture headline — what to look at (max 88 chars). NO "~% of recording", NO raw phase/log strings.
- line2: teach WHY in complete sentences (max 155 chars).
- line3: optional lab note (max 100) or "".
- chapter: short topic (max 40).

Annotation rules:
- Max ONE region (two only if line1 compares two areas).
- Normalized box [x0,y0,x1,y1], scene y≈0.05..0.78 (above caption bar).
- kind: "ellipse" or "rect"; label 2–4 words matching visible feature.

teachingFocus: snake_case tag.
narratorScript: one paragraph for VOICE only (max 380 chars) — top-tier YouTube dev-stream host: hype, jokes, reactions; NEVER read caption bullets; captions stay on-screen for the viewer.

Return ONLY JSON:
{"i":${m.i},"beatKind":"checkpoint","line1":"...","line2":"...","line3":"","chapter":"...","teachingFocus":"...","narratorScript":"...","regions":[{"kind":"ellipse","box":[0.3,0.2,0.7,0.55],"label":"Play disk"}]}
`;
}

async function runMilestonesUnified(body: DirectorBody): Promise<{ milestones: MilestoneOut[] }> {
  const withImages = body.milestones.filter((m) => m.imagePath?.trim());
  if (!withImages.length) return runDirector(body);

  const limit = recapConcurrency();
  const total = withImages.length;
  process.stderr.write(`[milestones] parallel ×${limit} captions+vision for ${total} PNGs\n`);

  const rows = await mapPool(withImages, limit, async (m, idx) => {
    const prompt = buildUnifiedMilestonePrompt(body, m);
    const run = await invokeProvider(prompt);
    process.stderr.write(`[milestones] ${idx + 1}/${total} i=${m.i} ${run.status}\n`);
    if (run.status !== "ok" || !run.result) return null;
    try {
      const parsed = extractJson(run.result) as MilestoneOut;
      return mergeDirectorChunk([m], [parsed])[0] ?? null;
    } catch {
      return null;
    }
  });

  const merged = rows.filter((r): r is MilestoneOut => r != null);
  merged.sort((a, b) => a.i - b.i);
  if (!merged.length) throw new Error("milestones pass produced no results");
  return { milestones: merged };
}

async function runDirector(body: DirectorBody): Promise<{ milestones: MilestoneOut[] }> {
  const chunkSize = 12;
  const chunks: MilestoneIn[][] = [];
  for (let offset = 0; offset < body.milestones.length; offset += chunkSize) {
    chunks.push(body.milestones.slice(offset, offset + chunkSize));
  }
  const limit = Math.min(recapConcurrency(), chunks.length);
  process.stderr.write(`[director] parallel ×${limit} for ${chunks.length} chunk(s)\n`);

  const chunkResults = await mapPool(chunks, limit, async (chunk, idx) => {
    const prompt = buildDirectorPrompt({ ...body, milestones: chunk });
    const run = await invokeProvider(prompt);
    process.stderr.write(`[director] chunk ${idx + 1}/${chunks.length} ${run.status}\n`);
    if (run.status !== "ok" || !run.result) throw new Error(run.result ?? "director failed");
    const parsed = extractJson(run.result) as { milestones?: MilestoneOut[] };
    return mergeDirectorChunk(chunk, parsed.milestones ?? []);
  });

  const all = chunkResults.flat();
  all.sort((a, b) => a.i - b.i);
  return { milestones: all };
}

async function runGrade(director: { buildMode?: string; milestones: MilestoneOut[] }): Promise<QualityReport> {
  const cap = localCaptionRung(director.milestones);
  const ann = localAnnotationRung(director.milestones);
  let aiScore = 50;
  let revisionPrompt = "";
  try {
    const run = await invokeProvider(buildGradePrompt(director));
    if (run.status === "ok" && run.result) {
      const parsed = extractJson(run.result) as QualityReport & { revisionPrompt?: string };
      aiScore = Number(parsed.overallScore) || 50;
      revisionPrompt = String(parsed.revisionPrompt ?? "");
    }
  } catch {
    revisionPrompt = "Improve caption voice; reduce overlapping annotations.";
  }
  const localOverall = cap.score + ann.score + 25 + 25;
  const overallScore = Math.round(localOverall * 0.55 + aiScore * 0.45);
  const pass = overallScore >= 78 && cap.pass && ann.pass;
  return {
    pass,
    overallScore,
    rung: pass ? "ship" : "revise",
    rungs: [
      { id: "captions", ...cap },
      { id: "annotations", ...ann },
      { id: "pedagogy", score: 22, pass: true, notes: [] },
      { id: "pacing", score: 22, pass: true, notes: [] },
    ],
    revisionPrompt: revisionPrompt || cap.notes.concat(ann.notes).join("; "),
  };
}

type NarrationOutlineIn = {
  buildMode: string;
  mapCompletionStatus?: "partial" | "complete";
  targetDurationSec?: number;
  sayRateWpm?: number;
  introTitle?: string;
  introSubtitle?: string;
  milestones: Array<{
    i: number;
    beatKind?: string;
    chapter?: string;
    phase?: string;
    sub?: string;
    subAction?: string;
    teachingFocus?: string;
    line1?: string;
    line2?: string;
    line3?: string;
    onScreenCaption?: string;
    narratorScript?: string;
  }>;
};

function inferMapCompletionStatus(body: NarrationOutlineIn): "partial" | "complete" {
  const explicit = (body.mapCompletionStatus ?? "").toLowerCase();
  if (explicit === "complete" || explicit === "completed" || explicit === "finished") {
    return "complete";
  }
  if (explicit === "partial" || explicit === "in_progress" || explicit === "wip") {
    return "partial";
  }
  const blob = [
    body.buildMode ?? "",
    ...body.milestones.flatMap((m) => [
      m.chapter ?? "",
      m.phase ?? "",
      m.sub ?? "",
      m.line1 ?? "",
      m.line2 ?? "",
      m.line3 ?? "",
      m.teachingFocus ?? "",
    ]),
  ]
    .join(" ")
    .toLowerCase();
  if (/(build complete|finished map|final world|shipped world|playable complete)/.test(blob)) {
    return "complete";
  }
  return "partial";
}

function buildFullNarrationPrompt(body: NarrationOutlineIn): string {
  const sec = Math.max(120, Number(body.targetDurationSec ?? 480));
  const wpm = Math.max(140, Number(body.sayRateWpm ?? 186));
  const density = Math.max(0.75, Number((body as { narrationScriptDensityMultiplier?: number }).narrationScriptDensityMultiplier ?? 1));
  const targetWords = Math.round((sec / 60) * wpm * 0.92 * density);
  const mapStatus = inferMapCompletionStatus(body);
  const mapLine =
    mapStatus === "complete"
      ? "COMPLETE MAP — celebrate what is on screen; still suggest (never promise) off-screen ideas."
      : "PARTIAL BUILD — map still taking shape; hype visible progress, never talk like everything is shipped.";
  const outline = body.milestones
    .map((m) => {
      const kind = m.beatKind === "subbeat" ? "sub-step" : "chapter";
      const topic = (m.chapter || m.phase || "build").trim();
      const cap = (m.onScreenCaption || [m.line1, m.line2, m.line3].filter(Boolean).join(" ")).trim();
      const ns = (m.narratorScript || "").trim();
      const voiceHint = ns
        ? `\n  Suggested host voice (expand with jokes/reactions — do NOT read captions aloud):\n  ${ns}`
        : "";
      return `- ${kind} ${m.i} · ${topic}\n  On-screen captions (viewer reads these — do NOT lecture or repeat them in voice):\n  ${cap || "(no caption)"}${voiceHint}`;
    })
    .join("\n");

  const introLine = body.introTitle
    ? `Open like Attenborough meets Irwin on a kids adventure show (title: ${body.introTitle}${body.introSubtitle ? ` — ${body.introSubtitle}` : ""}). Wide hook, genuine wonder, zero lecture.`
    : "Open like a world-class TV adventure host. Wide shot first, then wonder — not a teacher.";

  return `You are writing the COMPLETE voiceover script for a Unity world-build recap video.

Persona: Jacob Adkins's Bot — renowned TV adventure host. Attenborough: wide-to-close reveals, quiet awe on details. Irwin: 'have a look', childlike wonder, direct address. Each beat: intro → setup → comedic punchline tied to ON-SCREEN pixels → out. NOT a teacher. NOT a live stream. NEVER mention marketing, monetization, algorithms, or sales tactics.

The silent video is already cut to ~${Math.round(sec)} seconds. Write ~${targetWords} words (~${density}× density); speech-first pipeline extends video to voice. Do not write a short script.

Build mode: ${body.buildMode}
Map status: ${mapStatus.toUpperCase()} — ${mapLine}

STRUCTURE (required):
1. ${introLine} Real Unity footage — match map status; one quick bot hello, then personality.
2. Body: beat-by-beat in order. Use visible screen content for facts only — react with jokes, asides, and hype. Never read or paraphrase caption bullets. Suggest what COULD come later — never promise unshown bosses, items, systems, or gameplay.
3. Close with playful energy — invite them back if partial; warm sign-off if complete.

Beat guide (captions = on-screen only; voice = host reactions — never lecture from captions):
${outline}

Voice rules:
- Greet as Jacob Adkins's Bot; hyper-realistic delivery, not robotic TTS
- On-screen captions are for the viewer's eyes only — never read, quote, or lecture from caption bullets
- Scene structure every beat: intro → setup → punchline (comedic button about THIS shot) → out
- Attenborough: broaden then narrow; put the reveal at the end of the sentence; never tell the viewer how to feel
- Irwin: 'have a look', 'how neat is that', genuine excitement — plain words, no jargon
- NEVER use educational tone — no notice, compare, pipeline, heightfield, seam, grid, Unity, spawn, or lesson framing
- Describe ONLY what is visible — mountains, paths, caves, land, water
- PARTIAL builds: momentum and visible progress; COMPLETE: celebrate on-screen only
- Fill the FULL video runtime — outro only at the very end; no repeated filler phrases
- Natural punctuation for speech — short sentences, em dashes for asides
- Smooth transitions — no dead filler, no corporate buzzwords
- One continuous script string (spaces between paragraphs)
- NO markdown, NO bullet characters in the script
- NO beat numbers, frame counts, fps, "timelapse", Environment Kit, Cave Grader, editor queue jargon
- NO "on screen you will see" or "the caption says" meta
- NEVER mention signature, autograph, or name on the portrait card

Return ONLY JSON:
{"script":"...single string..."}`;
}

async function runFullNarration(body: NarrationOutlineIn): Promise<{ script: string; wordCount: number }> {
  const prompt = buildFullNarrationPrompt(body);
  process.stderr.write(`[narration] Cursor full-script (~${body.targetDurationSec ?? "?"}s video)\n`);
  const run = await invokeProvider(prompt);
  if (run.status !== "ok" || !run.result) {
    throw new Error(run.result ?? "narration script generation failed");
  }
  const parsed = extractJson(run.result) as { script?: string };
  const script = (parsed.script ?? "").trim().replace(/\s+/g, " ");
  if (!script || script.length < 80) {
    throw new Error("narration script empty or too short");
  }
  const wordCount = script.split(/\s+/).filter(Boolean).length;
  return { script, wordCount };
}

async function main() {
  const mode = process.argv[2];
  const path = process.argv[3];
  if (!mode || !path) {
    console.error(
      "usage: demo-recap-pipeline.ts director|milestones|vision|grade|narration <json-path>"
    );
    process.exit(1);
  }
  const raw = readFileSync(path, "utf8");
  if (mode === "director") {
    const body = JSON.parse(raw) as DirectorBody;
    const out = await runDirector(body);
    process.stdout.write(JSON.stringify(out, null, 2));
    return;
  }
  if (mode === "milestones") {
    const body = JSON.parse(raw) as DirectorBody;
    const out = await runMilestonesUnified(body);
    process.stdout.write(JSON.stringify(out, null, 2));
    return;
  }
  if (mode === "vision") {
    const body = JSON.parse(raw) as VisionIn;
    const out = await runVision(body);
    process.stdout.write(JSON.stringify(out, null, 2));
    return;
  }
  if (mode === "grade") {
    const body = JSON.parse(raw) as { milestones: MilestoneOut[]; buildMode?: string };
    const out = await runGrade(body);
    process.stdout.write(JSON.stringify(out, null, 2));
    return;
  }
  if (mode === "narration") {
    const body = JSON.parse(raw) as NarrationOutlineIn;
    const out = await runFullNarration(body);
    process.stdout.write(JSON.stringify(out, null, 2));
    return;
  }
  console.error("unknown mode");
  process.exit(1);
}

main().catch((err) => {
  console.error(err instanceof Error ? err.message : String(err));
  process.exit(1);
});
