import { appendFileSync, mkdirSync, readFileSync, existsSync } from "node:fs";
import { join } from "node:path";

/** Matches Unity CaveBuildHealCheckpointStore.DbRootRel */
export const HEAL_DB_REL = "Assets/EnvironmentKit/Generated/HealCheckpointDb";
const LEDGER = "ledger.jsonl";

export type HealLedgerEvent = {
  eventType: string;
  utc?: string;
  checkpointId?: string;
  milestone?: string;
  seed?: number;
  score?: number;
  letterGrade?: string;
  buildAcceptable?: boolean;
  note?: string;
  aiProvider?: string;
  workflow?: string;
  rung?: string;
  exitCode?: number;
  runId?: string;
};

export function healDbRoot(hubRoot: string): string {
  return join(hubRoot, HEAL_DB_REL);
}

export function readHealIndex(hubRoot: string): {
  lastGoodCheckpointId?: string;
  headCheckpointId?: string;
  lastGoodScore?: number;
  lastGoodMilestone?: string;
} {
  const path = join(healDbRoot(hubRoot), "index.json");
  if (!existsSync(path)) return {};
  try {
    return JSON.parse(readFileSync(path, "utf8")) as ReturnType<typeof readHealIndex>;
  } catch {
    return {};
  }
}

/** Append-only ledger row — same file Unity CaveBuildHealCheckpointStore uses. */
export function appendHealLedgerEvent(hubRoot: string, evt: HealLedgerEvent): void {
  const dir = healDbRoot(hubRoot);
  mkdirSync(dir, { recursive: true });
  const line = JSON.stringify({
    ...evt,
    utc: evt.utc ?? new Date().toISOString(),
    aiProvider: evt.aiProvider ?? process.env.CAVE_AI_PROVIDER ?? "Cursor",
  });
  appendFileSync(join(dir, LEDGER), `${line}\n`, "utf8");
}
