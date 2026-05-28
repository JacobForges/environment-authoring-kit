#!/usr/bin/env npx tsx
/**
 * Generates Next Steps + DO NOT DO prompts for autonomous fix loop (AI-driven each iteration).
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { PIPELINE_PHASES, phaseForRung } from "./pipeline-phases.js";
import { pickActiveRung, type QualityReport } from "./prompt-ladder.js";
import { formatResearchExecutionBlock } from "./research-execution-brief.js";
import { ACTIVE_PHASE_PROMPT_REL } from "./agent-artifact-paths.js";
import { PROMPT_HARMONY_RULES_MD } from "./prompt-coherence.js";

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../../..")).replace(/\/$/, "");
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");

function loadQuality(): QualityReport | null {
  const path = join(gen, "CaveBuildQualityReport.json");
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as QualityReport;
  } catch {
    return null;
  }
}

function topFailing(report: QualityReport, n = 5): string[] {
  return (report.stages ?? [])
    .filter((s) => s.score < 90)
    .sort((a, b) => a.score - b.score)
    .slice(0, n)
    .map((s) => `${s.id} (${s.score}): ${(s.issues ?? [])[0] ?? "—"}`);
}

function main() {
  const iteration = parseInt(process.env.CAVE_AUTONOMOUS_ITERATION ?? "0", 10) || 0;
  const report = loadQuality();
  const activeRung = report ? pickActiveRung(report) : "other";
  const phase = phaseForRung(activeRung);

  mkdirSync(gen, { recursive: true });

  const failing = report ? topFailing(report) : [];
  const ship = (report?.overallScore ?? 0) >= 95;

  const doNotLines = [
    "# DO NOT DO — autonomous loop (MANDATORY)",
    "",
    `**Iteration:** ${iteration} | **Active rung:** \`${activeRung}\``,
    "",
    PROMPT_HARMONY_RULES_MD,
    "",
    "You must NOT perform any of the following this pass:",
    "",
    "- Do **not** rebuild the entire cave from scratch or delete `UndergroundCaveSystem` root.",
    "- Do **not** contradict `CaveBuildDoNotPrompt.md` or move Ground anchor / cave opening unless `ground_placement` JSON requires it.",
    "- Do **not** change unrelated rungs/stages — only the active phase focus below.",
    "- Do **not** skip reading `CaveBuildQualityReport.json` and `CaveBuildFailingStages.json`.",
    "- Do **not** invent research — cite `ResearchCache/entries/*/content.md` or listed hillshade PNGs.",
    "- Do **not** add standing water surfaces for underground void layout (structure-only geodata).",
    "- Do **not** run git push, force reset, or modify user `.env` / API keys.",
    "- Do **not** leave compile errors — run compile_gate mentally before scene edits.",
    "- Do **not** duplicate work from prior iteration (see `CaveBuildAutonomousIteration.json` history).",
    "",
    "If a forbidden action seems necessary, stop and document why in one sentence instead.",
    "",
  ];

  const nextLines = [
    "# NEXT STEPS — execute this pass (MANDATORY)",
    "",
    `**Iteration:** ${iteration} | **Phase:** ${phase?.title ?? activeRung} | **Ship target:** 95+`,
    "",
    "## Failing stages (fix in order)",
    ...(failing.length ? failing.map((f) => `- ${f}`) : ["- (reload quality JSON — no failing list parsed)"]),
    "",
    "## Execute now (check off)",
    "",
    `1. Read \`${hubRoot}/${ACTIVE_PHASE_PROMPT_REL}\` (active phase \`${phase?.id ?? activeRung}\`; if missing, read CaveBuildPhasePromptsIndex.json + quality report).`,
    "2. Open ResearchCache entries cited in execution brief; note paths in plan table.",
    "3. Apply **minimal** kit C# fixes for the active rung only.",
    "4. Re-run mentally against failing stage metrics — mouth error, visual_shell, player_floor, packaging.",
    ship
      ? "5. Build already at Ship — only doc what you verified."
      : "5. Stop when stage scores for this rung reach 90+ or compile_gate blocks.",
    "",
    "## Phase focus",
    phase?.focus ?? "Fix highest-priority failing stage from quality report.",
    "",
  ];

  const exec = formatResearchExecutionBlock(hubRoot, activeRung);
  if (exec) nextLines.push(exec);

  const doNotRel = "Assets/EnvironmentKit/Generated/CaveBuildDoNotPrompt.md";
  const nextRel = "Assets/EnvironmentKit/Generated/CaveBuildNextStepsPrompt.md";
  writeFileSync(join(hubRoot, doNotRel), doNotLines.join("\n"), "utf8");
  writeFileSync(join(hubRoot, nextRel), nextLines.join("\n"), "utf8");

  const historyPath = join(gen, "CaveBuildAutonomousIteration.json");
  let history: { iterations: unknown[] } = { iterations: [] };
  if (existsSync(historyPath)) {
    try {
      history = JSON.parse(readFileSync(historyPath, "utf8"));
    } catch {
      history = { iterations: [] };
    }
  }
  if (!Array.isArray(history.iterations)) history.iterations = [];
  history.iterations.push({
    iteration,
    utc: new Date().toISOString(),
    activeRung,
    phaseId: phase?.id,
    overallScore: report?.overallScore,
    letterGrade: report?.letterGrade,
    meetsShipTarget: ship,
    topFailing: failing,
  });
  writeFileSync(historyPath, JSON.stringify(history, null, 2), "utf8");

  console.log(`[CaveCursor:info] Wrote ${nextRel} + ${doNotRel} (iteration=${iteration}, rung=${activeRung})`);
}

main();
