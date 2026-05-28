import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  formatResearchSourcesBlock,
  formatSearchQueriesBlock,
} from "./research-sources.js";
import {
  formatResearchExecutionBlock,
  writeResearchExecutionBrief,
} from "./research-execution-brief.js";
import { formatVisualReferencesBlock } from "./research-visual-references.js";
import { formatWorkflowAndMemoryBlock } from "./workflow-prompt.js";
import { buildTailoredFixBrief } from "./tailored-fix-brief.js";
import { buildHardcodedCaveLadderPrompt } from "./hardcoded-agent-prompts.js";

export function useHardcodedPrompts(): boolean {
  return process.env.CAVE_HARDCODED_PROMPTS !== "0";
}

const ladderDir = join(dirname(fileURLToPath(import.meta.url)), "prompt-ladder");

export const RUNG_ORDER = [
  "research",
  "compile_gate",
  "visual_shell",
  "ground_placement",
  "floor_collision",
  "navmesh",
  "materials",
  "performance",
  "other",
] as const;

/** Scene ladder only (after mandatory post-build phases). */
export const SCENE_RUNG_ORDER = RUNG_ORDER.filter(
  (r) => r !== "research" && r !== "compile_gate"
) as Exclude<PromptRung, "research" | "compile_gate">[];

export type PromptRung = (typeof RUNG_ORDER)[number];

// floor_collision rung uses rung-floor-collision.md (hyphenated filename)

export type StageRow = {
  id: string;
  score: number;
  issues?: string[];
  fixes?: string[];
  weight?: number;
};

export type QualityReport = {
  scene?: string;
  letterGrade: string;
  overallScore: number;
  buildAcceptable: boolean;
  isDud?: boolean;
  recommendedAction?: string;
  dudReasons?: string[];
  stages: StageRow[];
};

export type VisualShellAudit = {
  computedVisualScore?: number;
  rubricVisualShellScore?: number;
  blockRingCount?: number;
  blocksPerRingAvg?: number;
  hasAdventureShell?: boolean;
  stackedCeilingSlabCount?: number;
  hasRouteTerrainFloor?: boolean;
  visibleFlatPlatformCount?: number;
  issues?: string[];
};

export type FailingStagesDoc = {
  stages?: StageRow[];
  dudReasons?: string[];
};

export type LadderContextDoc = {
  scene?: string;
  meatLoopPass?: number;
  activeRung?: string;
  rootDepthErrorMeters?: number;
  expectedRootWorldY?: number;
  actualRootWorldY?: number;
  caveEntranceSpawnWorld?: string;
  portalPresentInScene?: boolean;
  liveManifestPaths?: string[];
};

const VISUAL_STAGE_IDS = new Set([
  "visual_shell",
  "enclosure_policy",
  "mode_consistency",
  "geometry_integrity",
  "layout_integrity",
  "organic_mesh",
  "block_tunnel",
  "enclosure",
  "interior_ribs",
  "walkways",
]);

const GROUND_STAGE_IDS = new Set([
  "ground",
  "terrain_integration",
  "terrain_carve",
  "spawn_reachability",
  "portal",
]);

const FLOOR_COLLISION_STAGE_IDS = new Set([
  "player_floor",
  "walkways",
  "spawn_reachability",
  "portal",
]);

const STAGE_PASS = 95;
const STAGE_FLOOR = 70;

function stageScore(report: QualityReport, id: string): number {
  const row = report.stages.find((s) => s.id === id);
  return row?.score ?? 100;
}

function anyStageBelow(report: QualityReport, ids: Set<string>, threshold: number): boolean {
  for (const s of report.stages) {
    if (ids.has(s.id) && s.score < threshold) return true;
  }
  return false;
}

function readRungMarkdown(rung: PromptRung): string {
  const path = join(ladderDir, `rung-${rung}.md`);
  if (!existsSync(path)) return "";
  return readFileSync(path, "utf8").trim();
}

function readCommonRules(): string {
  let mandatory = "";
  try {
    const mPath = join(ladderDir, "../mandatory-build-rules.ts");
    if (existsSync(mPath)) {
      const raw = readFileSync(mPath, "utf8");
      const match = raw.match(/MANDATORY_BUILD_RULES_MD = `([\s\S]*?)`;/);
      if (match) mandatory = match[1].trim() + "\n\n";
    }
  } catch {
    /* optional */
  }
  const path = join(ladderDir, "common-rules.md");
  const common = existsSync(path) ? readFileSync(path, "utf8").trim() : "";
  return mandatory + common;
}

function readResearchWorkflow(): string {
  const path = join(ladderDir, "research-workflow.md");
  if (!existsSync(path)) return "";
  return readFileSync(path, "utf8").trim();
}

export function isRungFailing(
  rung: PromptRung,
  report: QualityReport,
  visualShell: VisualShellAudit | null,
  ladderContext: LadderContextDoc | null
): boolean {
  const dud = report.dudReasons ?? [];

  switch (rung) {
    case "research":
    case "compile_gate":
      return false;
    case "visual_shell": {
      if (dud.some((r) => /onion|adventureshell/i.test(r))) return true;
      if (visualShell?.hasAdventureShell) return true;
      if ((visualShell?.stackedCeilingSlabCount ?? 0) > 0) return true;
      if (
        visualShell?.hasRouteTerrainFloor &&
        (visualShell.visibleFlatPlatformCount ?? 0) > 0
      )
        return true;
      const rings = visualShell?.blockRingCount ?? 0;
      const avg = visualShell?.blocksPerRingAvg ?? 0;
      if (rings > 26 && avg > 36) return true;
      if (stageScore(report, "visual_shell") < 95) return true;
      return anyStageBelow(report, VISUAL_STAGE_IDS, STAGE_PASS);
    }
    case "ground_placement": {
      const err = ladderContext?.rootDepthErrorMeters;
      if (err != null && Math.abs(err) > 1.5) return true;
      if (dud.some((r) => /float|above.*ground|placement|surface/i.test(r))) return true;
      return anyStageBelow(report, GROUND_STAGE_IDS, STAGE_PASS);
    }
    case "floor_collision": {
      if (stageScore(report, "player_floor") < STAGE_PASS) return true;
      if (dud.some((r) => /fall|walkable|floor collider|fall-through/i.test(r)))
        return true;
      return anyStageBelow(report, FLOOR_COLLISION_STAGE_IDS, STAGE_PASS);
    }
    case "navmesh":
      return stageScore(report, "navmesh") < STAGE_PASS;
    case "materials":
      return (
        stageScore(report, "materials") < STAGE_PASS ||
        stageScore(report, "lighting") < STAGE_PASS
      );
    case "performance":
      return stageScore(report, "performance") < STAGE_FLOOR;
    case "other":
      return !report.buildAcceptable || !!report.isDud;
    default:
      return false;
  }
}

export function pickActiveRung(
  report: QualityReport,
  visualShell: VisualShellAudit | null,
  ladderContext: LadderContextDoc | null,
  overrideRung?: string | null,
  skipRungs: Set<string> = new Set()
): PromptRung {
  if (overrideRung && RUNG_ORDER.includes(overrideRung as PromptRung)) {
    return overrideRung as PromptRung;
  }

  for (const rung of SCENE_RUNG_ORDER) {
    if (skipRungs.has(rung)) continue;
    if (isRungFailing(rung, report, visualShell, ladderContext)) return rung;
  }

  return "other";
}

function compactStageContext(report: QualityReport, rung: PromptRung): StageRow[] {
  const ids =
    rung === "visual_shell"
      ? VISUAL_STAGE_IDS
        : rung === "ground_placement"
          ? GROUND_STAGE_IDS
          : rung === "floor_collision"
            ? FLOOR_COLLISION_STAGE_IDS
            : rung === "navmesh"
          ? new Set(["navmesh", "walkways", "playability"])
          : rung === "materials"
            ? new Set(["materials", "lighting", "atmosphere"])
            : rung === "performance"
              ? new Set(["performance"])
              : null;

  const failing = report.stages.filter((s) => s.score < STAGE_PASS);
  if (!ids) return failing.slice(0, 6);
  return failing.filter((s) => ids.has(s.id)).slice(0, 8);
}

function compactVisualContext(visualShell: VisualShellAudit | null): Record<string, unknown> | null {
  if (!visualShell) return null;
  return {
    computedVisualScore: visualShell.computedVisualScore,
    rubricVisualShellScore: visualShell.rubricVisualShellScore,
    hasAdventureShell: visualShell.hasAdventureShell,
    stackedCeilingSlabCount: visualShell.stackedCeilingSlabCount,
    hasRouteTerrainFloor: visualShell.hasRouteTerrainFloor,
    visibleFlatPlatformCount: visualShell.visibleFlatPlatformCount,
    blockRingCount: visualShell.blockRingCount,
    blocksPerRingAvg: visualShell.blocksPerRingAvg,
    issues: (visualShell.issues ?? []).slice(0, 8),
  };
}

export function buildLadderPrompt(options: {
  hubRoot: string;
  report: QualityReport;
  visualShell: VisualShellAudit | null;
  failingStages: FailingStagesDoc | null;
  ladderContext: LadderContextDoc | null;
  activeRung: PromptRung;
  reportPath: string;
  visualShellPath: string;
}): string {
  const {
    hubRoot,
    report,
    visualShell,
    failingStages,
    ladderContext,
    activeRung,
    reportPath,
    visualShellPath,
  } = options;

  const meatPass = ladderContext?.meatLoopPass;

  const tailoredBrief = buildTailoredFixBrief({
    hubRoot,
    activeRung,
    report,
    visualShell,
    failingStages,
    ladderContext,
    meatLoopPass: ladderContext?.meatLoopPass ?? -1,
  });

  const executionBlock = formatResearchExecutionBlock(
    hubRoot,
    activeRung,
    meatPass !== undefined && meatPass >= 0 ? meatPass : undefined
  );

  const stages = compactStageContext(report, activeRung);
  const failingStageLines = stages.map((s) => {
    const fix = (s.fixes ?? [])[0];
    return fix
      ? `- ${s.id} (${s.score}): ${(s.issues ?? [])[0] ?? "—"} → ${fix}`
      : `- ${s.id} (${s.score}): ${(s.issues ?? [])[0] ?? "—"}`;
  });
  const dudReasonLines = (report.dudReasons ?? []).slice(0, 6).map((r) => `- ${r}`);

  if (useHardcodedPrompts()) {
    writeResearchExecutionBrief(
      hubRoot,
      activeRung,
      meatPass !== undefined && meatPass >= 0 ? meatPass : undefined
    );
    return buildHardcodedCaveLadderPrompt({
      hubRoot,
      activeRung,
      letterGrade: report.letterGrade,
      overallScore: report.overallScore,
      buildAcceptable: report.buildAcceptable,
      isDud: report.isDud,
      recommendedAction: report.recommendedAction,
      reportPath,
      failingStageLines,
      dudReasonLines,
      rungTaskMarkdown: readRungMarkdown(activeRung),
      tailoredBrief,
      executionBrief: executionBlock,
    });
  }

  const lines: string[] = [
    "You are fixing a Unity cave build for com.cursor.environment-authoring-kit.",
    `Hub root: ${hubRoot}`,
    `Active ladder rung: **${activeRung}** (focused pass — do not fix unrelated stages).`,
    `Grade: ${report.letterGrade} (${report.overallScore}/100), acceptable=${report.buildAcceptable}, dud=${report.isDud ?? false}`,
    `Recommended action: ${report.recommendedAction ?? "RunMeatLoop"}`,
    "",
    executionBlock,
    "",
    tailoredBrief,
    "",
    formatVisualReferencesBlock(activeRung, hubRoot),
    "",
    "**CRITICAL:** Post-build workflow: research → compile_gate (zero CS errors) → ladder rung fixes. Check off checklist items; read failure memory.",
    "",
    formatWorkflowAndMemoryBlock(activeRung, hubRoot),
    "",
    readResearchWorkflow(),
    "",
    formatResearchSourcesBlock(activeRung, ladderContext?.meatLoopPass ?? -1, hubRoot),
    "",
    formatSearchQueriesBlock(activeRung, report, hubRoot),
    "",
    readCommonRules(),
    "",
    readRungMarkdown(activeRung),
    "",
  ];

  if (report.dudReasons?.length) {
    lines.push("Dud reasons (reference):");
    for (const r of report.dudReasons.slice(0, 6)) lines.push(`- ${r}`);
    lines.push("");
  }

  if (failingStageLines.length) {
    lines.push("Failing stages for this rung (detail):");
    lines.push(...failingStageLines);
    lines.push("");
  }

  if (activeRung === "visual_shell") {
    const compact = compactVisualContext(visualShell);
    if (compact) {
      lines.push("Visual shell audit (compact):");
      lines.push(JSON.stringify(compact, null, 2));
      lines.push("");
    }
  }

  if (activeRung === "ground_placement" && ladderContext) {
    lines.push("Ground placement (compact):");
    lines.push(JSON.stringify(ladderContext, null, 2));
    lines.push("");
  }

  if (activeRung === "floor_collision") {
    const pf = report.stages.find((s) => s.id === "player_floor");
    if (pf) {
      lines.push("Player floor stage (must fix fall-through):");
      lines.push(JSON.stringify(pf, null, 2));
      lines.push("");
    }
  }

  if (failingStages?.dudReasons?.length && activeRung === "other") {
    lines.push("Top failing stage ids:");
    lines.push(
      (failingStages.stages ?? [])
        .slice(0, 5)
        .map((s) => s.id)
        .join(", ")
    );
    lines.push("");
  }

  const manifest =
    ladderContext?.liveManifestPaths?.length ?
      ladderContext.liveManifestPaths
    : [
        reportPath,
        visualShellPath,
        "Assets/EnvironmentKit/Generated/CaveBuildWorkflowContext.json",
        "Assets/EnvironmentKit/Generated/CaveBuildCompileDiagnostics.json",
        "Assets/EnvironmentKit/Generated/CaveBuildAgentMemory.json",
        "Assets/EnvironmentKit/Generated/CaveBuildResearch.json",
        "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json",
        "Assets/EnvironmentKit/Generated/CaveBuildLadderContext.json",
        "Assets/EnvironmentKit/Generated/CaveBuildFailingStages.json",
        "Assets/EnvironmentKit/Generated/CaveBuildMeatLoopHistory.json",
      ];

  lines.push("Live manifest (read for this rung before editing):");
  for (const p of manifest) lines.push(`- ${hubRoot}/${p}`.replace(/\/+/g, "/"));
  if (ladderContext?.caveEntranceSpawnWorld) {
    lines.push("");
    lines.push(`Cave entrance spawn (world): ${ladderContext.caveEntranceSpawnWorld}`);
  }

  writeResearchExecutionBrief(
    hubRoot,
    activeRung,
    meatPass !== undefined && meatPass >= 0 ? meatPass : undefined
  );

  return lines.filter((l) => l.length > 0).join("\n");
}

export function parseRungArg(argv: string[]): string | null {
  for (const arg of argv) {
    if (arg.startsWith("--rung=")) return arg.slice("--rung=".length).trim();
  }
  return process.env.CAVE_CURSOR_RUNG?.trim() || null;
}
