import { readFileSync, existsSync } from "node:fs";
import { join } from "node:path";

export type PlaySmokeReport = {
  acceptancePass?: boolean;
  playerSpawned?: boolean;
  portalEntered?: boolean;
  goalReached?: boolean;
  damageTaken?: boolean;
  enemyDamaged?: boolean;
};

const PLAY_REPORT_REL =
  "Assets/EnvironmentKit/Generated/HubDemoSmokePlayReport.json";
const CHECKLIST_REL =
  "Assets/EnvironmentKit/Generated/HubDemoSmokeChecklist.json";

export function loadPlaySmokeReport(hubRoot: string): PlaySmokeReport | null {
  const path = join(hubRoot, PLAY_REPORT_REL);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as PlaySmokeReport;
  } catch {
    return null;
  }
}

export function playSmokePassed(hubRoot: string): boolean {
  return loadPlaySmokeReport(hubRoot)?.acceptancePass === true;
}

export function loadSmokeChecklist(hubRoot: string): Record<string, unknown> | null {
  const path = join(hubRoot, CHECKLIST_REL);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as Record<string, unknown>;
  } catch {
    return null;
  }
}

export function formatGameplaySmokeLine(hubRoot: string): string {
  const report = loadPlaySmokeReport(hubRoot);
  const checklist = loadSmokeChecklist(hubRoot);
  const g8 = (checklist?.milestones as Record<string, string> | undefined)
    ?.G8_demo_smoke_pass;
  return (
    `[CaveCursor:gameplay-smoke] playAcceptance=${report?.acceptancePass === true} ` +
    `G8=${g8 ?? "unknown"}`
  );
}
