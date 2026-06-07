import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { formatResearchSourcesBlock } from "./research-sources.js";
import { formatWorkflowAndMemoryBlock } from "./workflow-prompt.js";
import { buildHardcodedTerrainLadderPrompt } from "./hardcoded-agent-prompts.js";
import { useHardcodedPrompts } from "./prompt-ladder.js";
import {
  resolveTerrainThresholds,
  stageFails,
  type GradingThresholds,
} from "./grading-thresholds.js";

const ladderDir = join(dirname(fileURLToPath(import.meta.url)), "prompt-ladder", "terrain");

/** Matches SurfaceTerrainBuildLadder.RungOrder in Unity. */
export const TERRAIN_RUNG_ORDER = [
  "nine_tile_grid",
  "outer_ring_mountains",
  "cave_openings_poi",
  "satellite_cave_systems",
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

export const TERRAIN_GEO_RUNGS = TERRAIN_RUNG_ORDER.filter((r) => !r.startsWith("prop_"));
export const TERRAIN_PROPS_RUNGS = TERRAIN_RUNG_ORDER.filter((r) => r.startsWith("prop_"));

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
  gradingTask?: string;
  letterGrade: string;
  overallScore: number;
  buildAcceptable: boolean;
  targetScore?: number;
  stagePassScore?: number;
  stageFloorScore?: number;
  expectedSurfaceTileCount?: number;
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

export type TerrainWorkflowMode = "terrain" | "terrain_geo" | "terrain_props";

export function parseTerrainWorkflowArg(argv: string[]): TerrainWorkflowMode | null {
  const env = process.env.CAVE_WORKFLOW?.trim()?.replace(/-/g, "_");
  if (env === "terrain" || env === "terrain_geo" || env === "terrain_props") {
    return env as TerrainWorkflowMode;
  }
  for (const arg of argv) {
    if (arg.startsWith("--workflow=")) {
      const v = arg.slice("--workflow=".length).trim().replace(/-/g, "_");
      if (v === "terrain" || v === "terrain_geo" || v === "terrain_props") {
        return v as TerrainWorkflowMode;
      }
    }
  }
  return null;
}

function rungOrderForWorkflow(workflow: TerrainWorkflowMode | null): readonly string[] {
  if (workflow === "terrain_geo") return TERRAIN_GEO_RUNGS;
  if (workflow === "terrain_props") return TERRAIN_PROPS_RUNGS;
  return TERRAIN_RUNG_ORDER;
}

function stageInOrder(id: string, order: readonly string[]): boolean {
  return order.includes(id);
}

export function pickActiveTerrainRung(
  report: TerrainQualityReport,
  overrideRung?: string | null,
  workflow: TerrainWorkflowMode | null = null
): string {
  const order = rungOrderForWorkflow(workflow);
  const thresholds = resolveTerrainThresholds(report);

  if (overrideRung && report.stages.some((s) => s.id === overrideRung)) {
    return overrideRung;
  }
  if (overrideRung && stageInOrder(overrideRung, order)) {
    return overrideRung;
  }

  let worstCritical: TerrainStageRow | null = null;
  let worst: TerrainStageRow | null = null;

  for (const id of order) {
    const s = report.stages.find((row) => row.id === id);
    if (!s || !stageFails(s, thresholds)) continue;
    if (s.critical) {
      if (!worstCritical || s.score < worstCritical.score) worstCritical = s;
    } else if (!worst || s.score < worst.score) {
      worst = s;
    }
  }

  for (const s of report.stages) {
    if (!stageInOrder(s.id, order)) continue;
    if (!stageFails(s, thresholds)) continue;
    if (s.critical) {
      if (!worstCritical || s.score < worstCritical.score) worstCritical = s;
    } else if (!worst || s.score < worst.score) {
      worst = s;
    }
  }

  if (worstCritical) return worstCritical.id;
  if (worst) return worst.id;
  return workflow === "terrain_props" ? "prop_trees" : "heightfield_no_craters";
}

export function isTerrainRungFailing(
  rung: string,
  report: TerrainQualityReport,
  workflow: TerrainWorkflowMode | null = null
): boolean {
  const order = rungOrderForWorkflow(workflow);
  if (!stageInOrder(rung, order) && !report.stages.some((s) => s.id === rung)) {
    return true;
  }
  const stage = report.stages.find((s) => s.id === rung);
  return stageFails(stage, resolveTerrainThresholds(report));
}

export function buildTerrainLadderPrompt(options: {
  hubRoot: string;
  report: TerrainQualityReport;
  ladderContext: TerrainLadderContextDoc | null;
  activeRung: string;
  reportPath: string;
  workflow?: TerrainWorkflowMode | null;
}): string {
  const { hubRoot, report, ladderContext, activeRung, reportPath, workflow = null } = options;
  const thresholds = resolveTerrainThresholds(report);
  const stage = report.stages.find((s) => s.id === activeRung);
  const common = existsSync(join(ladderDir, "common-rules.md"))
    ? readFileSync(join(ladderDir, "common-rules.md"), "utf8")
    : "";
  const workflowMd = existsSync(join(ladderDir, "terrain-workflow.md"))
    ? readFileSync(join(ladderDir, "terrain-workflow.md"), "utf8")
    : "";
  const rungMd = readRungMd(activeRung);

  const failing = report.stages
    .filter((s) => stageFails(s, thresholds))
    .map((s) => `${s.id}:${s.score}`)
    .join(", ");

  const issues = (stage?.issues ?? []).map((i) => `- ${i}`).join("\n");
  const fixes = (stage?.fixes ?? []).map((f) => `- ${f}`).join("\n");

  const researchBlock = formatResearchSourcesBlock(activeRung, 0, hubRoot);
  const wfLabel = workflow ?? parseTerrainWorkflowArg([]) ?? "terrain";

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

  const meatHint =
    wfLabel === "terrain_geo"
      ? "Geo meat loop — fix default/unusable land (heightfield, tiles, trails). Read SurfaceDefaultTerrainStatus.json."
      : wfLabel === "terrain_props"
        ? "Props meat loop — no placeholder vegetation; one prop category per pass."
        : "";

  if (useHardcodedPrompts()) {
    return buildHardcodedTerrainLadderPrompt({
      hubRoot,
      activeRung,
      scene: report.scene,
      seed: report.seed,
      letterGrade: report.letterGrade,
      overallScore: report.overallScore,
      targetScore: thresholds.targetScore,
      reportPath,
      failingSummary: failing,
      issueLines: issues,
      fixLines: fixes,
      rungTaskMarkdown: rungMd,
      contextBlock: [ctxBlock, meatHint].filter(Boolean).join("\n"),
    });
  }

  return `# Terrain Build — Cursor fix pass

**Workflow:** \`${wfLabel}\` | **Grading task:** \`${report.gradingTask ?? "full_world"}\`
**Active rung:** \`${activeRung}\` | **Stage pass:** ${thresholds.stagePassScore}+
**Scene:** ${report.scene ?? "unknown"} | **Seed:** ${report.seed ?? 0}
**Grade:** ${report.letterGrade} (${report.overallScore}/100) | Target: ${thresholds.targetScore}+
**Report:** \`${reportPath}\`
${meatHint ? `\n${meatHint}\n` : ""}

## Failing stages
${failing || "none"}

## This rung (${activeRung}) — score ${stage?.score ?? "?"}
${issues || "- (no issues listed)"}

### Suggested fixes (Unity editor / C#)
${fixes || "- See SurfaceTerrainLadderFixer and SurfaceTerrainBuildLadder.cs"}

---

${workflowMd}

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

export function terrainThresholdsForReport(report: TerrainQualityReport): GradingThresholds {
  return resolveTerrainThresholds(report);
}
