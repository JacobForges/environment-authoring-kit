import { readFileSync, existsSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { formatGameplaySmokeLine, playSmokePassed } from "./gameplay-smoke-gate.js";
import { GAMEPLAY_MILESTONE_ORDER } from "./gameplay-prompt-ladder.js";

export type MissionPhase = "world" | "gameplay" | "polish";

export type HubGameProgressDoc = {
  version?: number;
  demoReady?: boolean;
  milestones?: Record<
    string,
    { status?: string; title?: string; acceptance?: string; notes?: string }
  >;
};

export type QualityReportGate = {
  buildAcceptable?: boolean;
  overallScore?: number;
};

const WORLD_MIN_SCORE = 85;
const DEMO_WORLD_MIN_SCORE = 75;
const PROGRESS_REL = "Assets/Scripts/HubGameProgress.json";
const QUALITY_REL = "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json";
const HEAL_DB_REL = "Assets/EnvironmentKit/Generated/HealCheckpointDb";

function parseQualityFile(path: string): QualityReportGate | null {
  try {
    return JSON.parse(readFileSync(path, "utf8")) as QualityReportGate;
  } catch {
    return null;
  }
}

function findLatestHealArtifact(hubRoot: string, filename: string): string | null {
  const root = join(hubRoot, HEAL_DB_REL);
  if (!existsSync(root)) return null;

  let best: { path: string; mtime: number } | null = null;
  for (const cp of readdirSync(root)) {
    const candidate = join(root, cp, "artifacts", filename);
    if (!existsSync(candidate)) continue;
    const mtime = statSync(candidate).mtimeMs;
    if (!best || mtime > best.mtime) best = { path: candidate, mtime };
  }
  return best?.path ?? null;
}

export function loadHubGameProgress(hubRoot: string): HubGameProgressDoc | null {
  const path = join(hubRoot, PROGRESS_REL);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as HubGameProgressDoc;
  } catch {
    return null;
  }
}

/** Authoritative full-build report — used for world/gameplay gates. */
export function loadPrimaryQualityGate(hubRoot: string): QualityReportGate | null {
  const primary = join(hubRoot, QUALITY_REL);
  return existsSync(primary) ? parseQualityFile(primary) : null;
}

/** Primary report, else newest heal checkpoint artifact (display / supervisor only). */
export function loadQualityGate(hubRoot: string): QualityReportGate | null {
  const primary = loadPrimaryQualityGate(hubRoot);
  if (primary) return primary;

  const heal = findLatestHealArtifact(hubRoot, "CaveBuildQualityReport.json");
  return heal ? parseQualityFile(heal) : null;
}

/** G1–G7 done — only G8 or demoReady may remain. */
export function gameplayMilestonesReady(hubRoot: string): boolean {
  const progress = loadHubGameProgress(hubRoot);
  if (!progress?.milestones) return false;

  const prereq = GAMEPLAY_MILESTONE_ORDER.slice(0, -1);
  return prereq.every((id) => progress.milestones?.[id]?.status === "done");
}

export function shouldUseDemoWorldGate(hubRoot: string): boolean {
  if (process.env.CAVE_DEMO_WORLD_GATE === "0") return false;
  if (process.env.CAVE_DEMO_WORLD_GATE === "1") return true;
  return gameplayMilestonesReady(hubRoot);
}

export function worldGatePassed(hubRoot: string): boolean {
  const q = loadPrimaryQualityGate(hubRoot);
  if (!q) return false;
  const score = q.overallScore ?? 0;
  return q.buildAcceptable === true && score >= WORLD_MIN_SCORE;
}

/** Demo rubric — gameplay phase can start at 75+ while ship target remains 85+. */
export function demoWorldGatePassed(hubRoot: string): boolean {
  const q = loadPrimaryQualityGate(hubRoot);
  if (!q) return false;
  const score = q.overallScore ?? 0;
  return q.buildAcceptable === true && score >= DEMO_WORLD_MIN_SCORE;
}

/** G8 scene-only work without a fresh grade — opt-in via CAVE_SCENE_DEMO_BYPASS=1. */
export function sceneDemoBypassEnabled(): boolean {
  return process.env.CAVE_SCENE_DEMO_BYPASS === "1";
}

export function gameplayPhaseUnlocked(hubRoot: string): boolean {
  if (!gameplayMilestonesReady(hubRoot)) return false;

  const q = loadPrimaryQualityGate(hubRoot);
  if (!q) return sceneDemoBypassEnabled();

  return shouldUseDemoWorldGate(hubRoot)
    ? demoWorldGatePassed(hubRoot)
    : worldGatePassed(hubRoot);
}

export function resolveMissionPhase(hubRoot: string): MissionPhase {
  const progress = loadHubGameProgress(hubRoot);
  if (progress?.demoReady === true) return "polish";

  if (gameplayPhaseUnlocked(hubRoot)) return "gameplay";

  return "world";
}

export function formatMissionPhaseLine(hubRoot: string): string {
  const phase = resolveMissionPhase(hubRoot);
  const q = loadQualityGate(hubRoot);
  const progress = loadHubGameProgress(hubRoot);
  const score = q?.overallScore ?? 0;
  const acceptable = q?.buildAcceptable === true;
  const demoReady = progress?.demoReady === true;
  const smoke = phase !== "world" ? ` playSmoke=${playSmokePassed(hubRoot)}` : "";
  const g17 = gameplayMilestonesReady(hubRoot);
  const noReport = loadPrimaryQualityGate(hubRoot) == null;
  const bypass =
    g17 && noReport && sceneDemoBypassEnabled() ? " sceneDemoBypass=true" : "";
  return (
    `[CaveCursor:mission] phase=${phase} worldScore=${score} buildAcceptable=${acceptable} demoReady=${demoReady} g17=${g17}${bypass}${smoke}`
  );
}

export { playSmokePassed, formatGameplaySmokeLine };

const isCli =
  process.argv[1]?.endsWith("mission-phase.ts") ||
  process.argv[1]?.endsWith("mission-phase.js");

if (isCli) {
  const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../../..")).replace(/\/$/, "");
  console.log(formatMissionPhaseLine(hubRoot));
  console.log(resolveMissionPhase(hubRoot));
}
