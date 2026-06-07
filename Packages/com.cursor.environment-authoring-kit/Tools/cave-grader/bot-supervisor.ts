/**
 * Production bot supervisor — stall detection, rollback, dashboard, webhooks, cost caps.
 */
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { loadBotProductionConfig, resolveMaxIterations, type BotProductionConfig } from "./bot-production-config.js";
import { formatPlaybookBlock, pickPlaybook } from "./bot-playbooks/index.js";
import { loadCheckpoint, recordStall, saveCheckpoint } from "./bot-session-checkpoint.js";
import {
  demoWorldGatePassed,
  formatMissionPhaseLine,
  loadHubGameProgress,
  resolveMissionPhase,
  worldGatePassed,
} from "./mission-phase.js";

export type QualitySnapshot = {
  overallScore: number;
  buildAcceptable: boolean;
  letterGrade: string;
  topFailing: string;
  routeProbePassed: boolean;
};

export type SessionSummary = {
  sessionIndex: number;
  phase: string;
  agentExit: number;
  unityExit: number | null;
  scoreBefore: number;
  scoreAfter: number;
  playbookId: string;
  stalled: boolean;
  rolledBack: boolean;
  notes: string[];
};

const SUMMARY_DIR = "Logs/bot-sessions";

function loadQuality(hubRoot: string): QualitySnapshot {
  const path = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json");
  const probePath = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildRouteProbe.json");
  let doc: Record<string, unknown> = {};
  if (existsSync(path)) {
    try {
      doc = JSON.parse(readFileSync(path, "utf8")) as Record<string, unknown>;
    } catch {
      /* empty */
    }
  }
  let routeProbePassed = true;
  if (existsSync(probePath)) {
    try {
      const probe = JSON.parse(readFileSync(probePath, "utf8")) as { passed?: boolean };
      routeProbePassed = probe.passed !== false;
    } catch {
      routeProbePassed = false;
    }
  }
  const top = Array.isArray(doc.topFailingStages) ? (doc.topFailingStages[0] as string) : "";
  return {
    overallScore: (doc.overallScore as number) ?? 0,
    buildAcceptable: doc.buildAcceptable === true,
    letterGrade: (doc.letterGrade as string) ?? "?",
    topFailing: top ?? "",
    routeProbePassed,
  };
}

function loadFailingIssueText(hubRoot: string): string {
  const path = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildFailingStages.json");
  if (!existsSync(path)) return "";
  try {
    const doc = JSON.parse(readFileSync(path, "utf8")) as {
      stages?: { id?: string; issues?: string[] }[];
    };
    const first = doc.stages?.[0];
    return `${first?.id ?? ""} ${(first?.issues ?? []).join(" ")}`;
  } catch {
    return "";
  }
}

export function shouldRunAgent(cfg: BotProductionConfig): boolean {
  if (cfg.runMode === "nightly") return false;
  return true;
}

export function resolveIterationCap(hubRoot: string): number {
  return resolveMaxIterations(hubRoot, loadBotProductionConfig(hubRoot));
}

export function preSessionPromptAugment(hubRoot: string): string {
  const issue = loadFailingIssueText(hubRoot);
  const pb = pickPlaybook(issue);
  const cp = loadCheckpoint(hubRoot);
  const blocks: string[] = [];
  if (pb) blocks.push(formatPlaybookBlock(hubRoot, issue));
  if (cp.lastPlaybookId && cp.lastPlaybookId !== pb?.id) {
    blocks.push(`Previous playbook ${cp.lastPlaybookId} did not clear — try deep repair or adjacent rung.`);
  }
  blocks.push(
    "Production bot: one rung / one fix. Tag edits with // [bot:rung:STAGE:YYYY-MM-DD]."
  );
  return blocks.filter(Boolean).join("\n\n---\n\n");
}

export function postAgentCheckpoint(hubRoot: string, sessionIndex: number, agentExit: number): void {
  const phase = resolveMissionPhase(hubRoot);
  const q = loadQuality(hubRoot);
  saveCheckpoint(hubRoot, {
    phase,
    overallScore: q.overallScore,
    buildAcceptable: q.buildAcceptable,
    demoReady: loadHubGameProgress(hubRoot)?.demoReady === true,
    totalSessions: sessionIndex,
  });
  if (agentExit !== 0 && agentExit !== 4) return;
}

export function evaluatePostUnity(
  hubRoot: string,
  cfg: BotProductionConfig,
  scoreBefore: number
): { rollback: boolean; stalled: boolean; playbookId: string; notes: string[] } {
  const q = loadQuality(hubRoot);
  const notes: string[] = [];
  const issue = loadFailingIssueText(hubRoot);
  const pb = pickPlaybook(issue);
  const playbookId = pb?.id ?? "";

  if (scoreBefore - q.overallScore >= cfg.rollbackScoreDrop) {
    notes.push(`Score regressed ${scoreBefore} → ${q.overallScore}; rollback recommended.`);
    return { rollback: true, stalled: false, playbookId, notes };
  }

  const stall = recordStall(hubRoot, q.topFailing.split(":")[0] || "other", issue, cfg.stallThreshold);
  if (stall.stalled) {
    notes.push(`Stall detected (${stall.count}× same rung/issue) — escalate to deep repair playbook.`);
  }

  if (!q.routeProbePassed) {
    notes.push("Route probe failed — world gate blocked until traversable.");
  }

  saveCheckpoint(hubRoot, { lastPlaybookId: playbookId, rung: q.topFailing });
  return { rollback: false, stalled: stall.stalled, playbookId, notes };
}

export function tryGitRollback(hubRoot: string): boolean {
  try {
    execFileSync("git", ["checkout", "--", "."], { cwd: hubRoot, stdio: "pipe" });
    return true;
  } catch {
    return false;
  }
}

export function writeSessionDashboard(hubRoot: string, summary: SessionSummary): void {
  const dir = join(hubRoot, SUMMARY_DIR);
  mkdirSync(dir, { recursive: true });
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const jsonPath = join(dir, `session-${summary.sessionIndex}-${stamp}.json`);
  writeFileSync(jsonPath, `${JSON.stringify(summary, null, 2)}\n`, "utf8");

  const mdPath = join(hubRoot, "Logs/bot-session-summary.md");
  const lines = [
    `# Bot session ${summary.sessionIndex}`,
    `- Phase: ${summary.phase}`,
    `- Score: ${summary.scoreBefore} → ${summary.scoreAfter}`,
    `- Agent exit: ${summary.agentExit} | Unity exit: ${summary.unityExit ?? "skipped"}`,
    `- Playbook: ${summary.playbookId || "none"}`,
    `- Stalled: ${summary.stalled} | Rolled back: ${summary.rolledBack}`,
    "",
    "## Notes",
    ...summary.notes.map((n) => `- ${n}`),
    "",
    formatMissionPhaseLine(hubRoot),
    "",
  ];
  writeFileSync(mdPath, lines.join("\n"), "utf8");
}

export function postWebhook(cfg: BotProductionConfig, summary: SessionSummary): void {
  if (!cfg.webhookUrl) return;
  try {
    const body = JSON.stringify({
      text: `[Hub bot] session ${summary.sessionIndex} phase=${summary.phase} score ${summary.scoreBefore}→${summary.scoreAfter} playbook=${summary.playbookId}`,
    });
    execFileSync(
      "curl",
      ["-sS", "-X", "POST", "-H", "Content-Type: application/json", "-d", body, cfg.webhookUrl],
      { stdio: "ignore", timeout: 15_000 }
    );
  } catch {
    /* optional */
  }
}

export function trackTokenEstimate(hubRoot: string, delta: number, cfg: BotProductionConfig): boolean {
  if (cfg.dailyTokenBudget <= 0) return true;
  const cp = loadCheckpoint(hubRoot);
  const total = cp.totalTokensEstimate + delta;
  saveCheckpoint(hubRoot, { totalTokensEstimate: total });
  return total <= cfg.dailyTokenBudget;
}

export function exportProductionGateStatus(hubRoot: string): void {
  const cfg = loadBotProductionConfig(hubRoot);
  const q = loadQuality(hubRoot);
  const out = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildBotProductionGate.json");
  mkdirSync(join(hubRoot, "Assets/EnvironmentKit/Generated"), { recursive: true });
  const doc = {
    capturedUtc: new Date().toISOString(),
    demoWorldGate: demoWorldGatePassed(hubRoot),
    shipWorldGate: worldGatePassed(hubRoot),
    demoMinScore: cfg.demoWorldMinScore,
    shipMinScore: cfg.shipWorldMinScore,
    routeProbePassed: q.routeProbePassed,
    routeProbeMinPct: cfg.routeProbeMinPassPct,
    performanceTriBudget: cfg.performanceTriBudget,
    goldenSeed: cfg.goldenSeed,
  };
  writeFileSync(out, `${JSON.stringify(doc, null, 2)}\n`, "utf8");
}
