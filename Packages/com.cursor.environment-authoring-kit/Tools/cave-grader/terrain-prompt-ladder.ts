import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { formatResearchSourcesBlock } from "./research-sources.js";
import { formatWorkflowAndMemoryBlock } from "./workflow-prompt.js";
import { buildHardcodedTerrainLadderPrompt } from "./hardcoded-agent-prompts.js";
import { useHardcodedPrompts } from "./prompt-ladder.js";

const ladderDir = join(dirname(fileURLToPath(import.meta.url)), "prompt-ladder", "terrain");

/** Matches SurfaceTerrainBuildLadder.RungOrder in Unity. */
export const TERRAIN_RUNG_ORDER = [
  "terrain_research",
  "heightfield_no_craters",
  "playable_slopes",
  "trail_walkability",
  "surface_navmesh",
  "prop_trees",
  "prop_grass",
  "prop_bushes",
  "prop_ground_cover",
  "surface_playtest",
  "cave_mouth_grounding",
] as const;

export type TerrainRung = (typeof TERRAIN_RUNG_ORDER)[number];

export type TerrainStageRow = {
  id: string;
  name?: string;
  score: number;
  weight?: number;
  critical?: boolean;
  passed?: boolean;
  issues?: string[];
  fixes?: string[];
};

export type TerrainQualityReport = {
  scene?: string;
  seed?: number;
  gradingMode?: string;
  letterGrade: string;
  overallScore: number;
  buildAcceptable: boolean;
  targetScore?: number;
  stages: TerrainStageRow[];
};

export type TerrainLadderContextDoc = {
  overallScore?: number;
  activeRung?: string;
  failingRungs?: string[];
  terrainPhaseLog?: string;
  propPlan?: string;
  surfaceManifest?: string;
};

function readRungMd(rung: string): string {
  const hyphen = rung.replace(/_/g, "-");
  for (const name of [`rung-${rung}.md`, `rung-${hyphen}.md`]) {
    const p = join(ladderDir, name);
    if (existsSync(p)) return readFileSync(p, "utf8");
  }
  return "";
}

export function parseTerrainRungArg(argv: string[]): string | null {
  const env = process.env.CAVE_CURSOR_RUNG?.trim() ?? process.env.TERRAIN_CURSOR_RUNG?.trim();
  if (env) return env;
  for (const arg of argv) {
    if (arg.startsWith("--rung=")) return arg.slice("--rung=".length).trim();
  }
  return null;
}

export function parseTerrainWorkflowArg(argv: string[]): "terrain" | null {
  const env = process.env.CAVE_WORKFLOW?.trim();
  if (env === "terrain") return "terrain";
  for (const arg of argv) {
    if (arg.startsWith("--workflow=")) {
      const v = arg.slice("--workflow=".length).trim().replace(/-/g, "_");
      if (v === "terrain") return "terrain";
    }
  }
  return null;
}

export function pickActiveTerrainRung(
  report: TerrainQualityReport,
  overrideRung?: string | null
): string {
  if (overrideRung && TERRAIN_RUNG_ORDER.includes(overrideRung as TerrainRung)) {
    return overrideRung;
  }

  let worstCritical: TerrainStageRow | null = null;
  let worst: TerrainStageRow | null = null;

  for (const s of report.stages) {
    if (s.passed && s.score >= 90) continue;
    if (s.critical) {
      if (!worstCritical || s.score < worstCritical.score) worstCritical = s;
    } else if (!worst || s.score < worst.score) {
      worst = s;
    }
  }

  if (worstCritical) return worstCritical.id;
  if (worst) return worst.id;
  return "heightfield_no_craters";
}

export function isTerrainRungFailing(rung: string, report: TerrainQualityReport): boolean {
  const stage = report.stages.find((s) => s.id === rung);
  if (!stage) return true;
  return !stage.passed || stage.score < 90;
}

export function buildTerrainLadderPrompt(options: {
  hubRoot: string;
  report: TerrainQualityReport;
  ladderContext: TerrainLadderContextDoc | null;
  activeRung: string;
  reportPath: string;
}): string {
  const { hubRoot, report, ladderContext, activeRung, reportPath } = options;
  const stage = report.stages.find((s) => s.id === activeRung);
  const common = existsSync(join(ladderDir, "common-rules.md"))
    ? readFileSync(join(ladderDir, "common-rules.md"), "utf8")
    : "";
  const workflow = existsSync(join(ladderDir, "terrain-workflow.md"))
    ? readFileSync(join(ladderDir, "terrain-workflow.md"), "utf8")
    : "";
  const rungMd = readRungMd(activeRung);

  const failing = report.stages
    .filter((s) => !s.passed || s.score < 90)
    .map((s) => `${s.id}:${s.score}`)
    .join(", ");

  const issues = (stage?.issues ?? []).map((i) => `- ${i}`).join("\n");
  const fixes = (stage?.fixes ?? []).map((f) => `- ${f}`).join("\n");

  const researchBlock = formatResearchSourcesBlock(activeRung, 0, hubRoot);

  const ctxBlock = ladderContext
    ? [
        `Active rung (context): ${ladderContext.activeRung ?? activeRung}`,
        `Failing rungs: ${(ladderContext.failingRungs ?? []).join(", ") || "none"}`,
        `Terrain phase log: ${ladderContext.terrainPhaseLog ?? "n/a"}`,
        `Prop plan: ${ladderContext.propPlan ?? "n/a"}`,
        `Surface manifest: ${ladderContext.surfaceManifest ?? "n/a"}`,
      ].join("\n")
    : "";

  const memory = formatWorkflowAndMemoryBlock(activeRung, hubRoot);

  if (useHardcodedPrompts()) {
    return buildHardcodedTerrainLadderPrompt({
      hubRoot,
      activeRung,
      scene: report.scene,
      seed: report.seed,
      letterGrade: report.letterGrade,
      overallScore: report.overallScore,
      targetScore: report.targetScore,
      reportPath,
      failingSummary: failing,
      issueLines: issues,
      fixLines: fixes,
      rungTaskMarkdown: rungMd,
      contextBlock: ctxBlock,
    });
  }

  return `# Terrain Build — Cursor fix pass

**Workflow:** \`terrain\` | **Active rung:** \`${activeRung}\`
**Scene:** ${report.scene ?? "unknown"} | **Seed:** ${report.seed ?? 0}
**Grade:** ${report.letterGrade} (${report.overallScore}/100) | Target: ${report.targetScore ?? 85}+
**Report:** \`${reportPath}\`

## Failing stages
${failing || "none"}

## This rung (${activeRung}) — score ${stage?.score ?? "?"}
${issues || "- (no issues listed)"}

### Suggested fixes (Unity editor / C#)
${fixes || "- See SurfaceTerrainLadderFixer and SurfaceTerrainBuildLadder.cs"}

---

${workflow}

---

${common}

---

${rungMd || `## Rung ${activeRung}\nImprove above-ground terrain for rung \`${activeRung}\` using Hub scripts under Packages/com.cursor.environment-authoring-kit.`}

---

${ctxBlock}

---

${researchBlock}

---

${memory}

## Rules
1. Work in **Hub** (\`${hubRoot}\`) — editor terrain, not Play Mode-only hacks.
2. Prefer **SurfaceTerrainLadderFixer**, **SurfaceTerrainCraterRepair**, **SurfaceIntelligentPropPlacer** — one prop category per pass when on prop_* rungs.
3. Do **not** delete GeneratedSurfaceWorld or cave systems; additive fixes only.
4. After edits, Unity will re-grade via **Terrain Build Grader** window.
5. Emit no secrets; keep changes minimal and scoped to this rung.
`;
}
