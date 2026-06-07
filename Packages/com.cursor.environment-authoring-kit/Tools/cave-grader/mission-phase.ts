import { readFileSync, existsSync } from "node:fs";
import { join } from "node:path";

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

export function loadHubGameProgress(hubRoot: string): HubGameProgressDoc | null {
  const path = join(hubRoot, PROGRESS_REL);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as HubGameProgressDoc;
  } catch {
    return null;
  }
}

export function loadQualityGate(hubRoot: string): QualityReportGate | null {
  const path = join(hubRoot, QUALITY_REL);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as QualityReportGate;
  } catch {
    return null;
  }
}

export function worldGatePassed(hubRoot: string): boolean {
  const q = loadQualityGate(hubRoot);
  if (!q) return false;
  const score = q.overallScore ?? 0;
  return q.buildAcceptable === true && score >= WORLD_MIN_SCORE;
}

/** Demo rubric — gameplay phase can start at 75+ while ship target remains 85+. */
export function demoWorldGatePassed(hubRoot: string): boolean {
  const q = loadQualityGate(hubRoot);
  if (!q) return false;
  const score = q.overallScore ?? 0;
  return q.buildAcceptable === true && score >= DEMO_WORLD_MIN_SCORE;
}

export function resolveMissionPhase(hubRoot: string): MissionPhase {
  const demoGate =
    process.env.CAVE_DEMO_WORLD_GATE === "1" ? demoWorldGatePassed(hubRoot) : worldGatePassed(hubRoot);
  if (!demoGate) return "world";

  const progress = loadHubGameProgress(hubRoot);
  if (!progress || progress.demoReady !== true) return "gameplay";

  return "polish";
}

export function formatMissionPhaseLine(hubRoot: string): string {
  const phase = resolveMissionPhase(hubRoot);
  const q = loadQualityGate(hubRoot);
  const progress = loadHubGameProgress(hubRoot);
  const score = q?.overallScore ?? 0;
  const acceptable = q?.buildAcceptable === true;
  const demoReady = progress?.demoReady === true;
  return (
    `[CaveCursor:mission] phase=${phase} worldScore=${score} buildAcceptable=${acceptable} demoReady=${demoReady}`
  );
}

const isCli =
  process.argv[1]?.endsWith("mission-phase.ts") ||
  process.argv[1]?.endsWith("mission-phase.js");

if (isCli) {
  const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../..")).replace(/\/$/, "");
  console.log(formatMissionPhaseLine(hubRoot));
  console.log(resolveMissionPhase(hubRoot));
}
