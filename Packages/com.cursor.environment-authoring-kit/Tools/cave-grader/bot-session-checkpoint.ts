import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

export type BotSessionCheckpoint = {
  version: number;
  updatedUtc: string;
  phase: string;
  rung: string;
  overallScore: number;
  buildAcceptable: boolean;
  demoReady: boolean;
  lastFixHash: string;
  stallCounts: Record<string, number>;
  totalSessions: number;
  totalTokensEstimate: number;
  lastPlaybookId: string;
  rollbackSnapshots: string[];
};

const REL = "Assets/EnvironmentKit/Generated/CaveBuildBotCheckpoint.json";

export function checkpointPath(hubRoot: string): string {
  return join(hubRoot, REL);
}

export function loadCheckpoint(hubRoot: string): BotSessionCheckpoint {
  const path = checkpointPath(hubRoot);
  if (!existsSync(path)) return freshCheckpoint();
  try {
    return JSON.parse(readFileSync(path, "utf8")) as BotSessionCheckpoint;
  } catch {
    return freshCheckpoint();
  }
}

export function saveCheckpoint(hubRoot: string, patch: Partial<BotSessionCheckpoint>): BotSessionCheckpoint {
  const path = checkpointPath(hubRoot);
  mkdirSync(join(hubRoot, "Assets/EnvironmentKit/Generated"), { recursive: true });
  const next = { ...loadCheckpoint(hubRoot), ...patch, updatedUtc: new Date().toISOString() };
  writeFileSync(path, `${JSON.stringify(next, null, 2)}\n`, "utf8");
  return next;
}

function freshCheckpoint(): BotSessionCheckpoint {
  return {
    version: 1,
    updatedUtc: new Date().toISOString(),
    phase: "world",
    rung: "",
    overallScore: 0,
    buildAcceptable: false,
    demoReady: false,
    lastFixHash: "",
    stallCounts: {},
    totalSessions: 0,
    totalTokensEstimate: 0,
    lastPlaybookId: "",
    rollbackSnapshots: [],
  };
}

export function hashFixSignature(files: string[]): string {
  return files.sort().join("|").slice(0, 120);
}

export function recordStall(
  hubRoot: string,
  rung: string,
  errorSig: string,
  threshold: number
): { stalled: boolean; count: number } {
  const cp = loadCheckpoint(hubRoot);
  const key = `${rung}::${errorSig.slice(0, 80)}`;
  const count = (cp.stallCounts[key] ?? 0) + 1;
  saveCheckpoint(hubRoot, { stallCounts: { ...cp.stallCounts, [key]: count } });
  return { stalled: count >= threshold, count };
}
