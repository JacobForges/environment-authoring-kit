/**
 * Production bot tuning — env overrides for all 50 production features.
 */
export type BotRunMode = "interactive" | "nightly";

export type BotProductionConfig = {
  runMode: BotRunMode;
  maxAgentSessions: number;
  rungBudgetPerPostPass: number;
  meatLoopMaxPasses: number;
  stallThreshold: number;
  rollbackScoreDrop: number;
  pauseSec: number;
  sessionTimeoutMin: number;
  compileFreshnessSec: number;
  routeProbeMinPassPct: number;
  demoWorldMinScore: number;
  shipWorldMinScore: number;
  performanceTriBudget: number;
  dailyTokenBudget: number;
  webhookUrl: string;
  goldenSeed: number;
};

export function loadBotProductionConfig(hubRoot: string): BotProductionConfig {
  void hubRoot;
  const mode = (process.env.CAVE_BOT_MODE ?? "interactive").toLowerCase();
  return {
    runMode: mode === "nightly" ? "nightly" : "interactive",
    maxAgentSessions: Number.parseInt(process.env.CAVE_UNTIL_DEMO_MAX ?? "0", 10) || 0,
    rungBudgetPerPostPass: Math.max(1, Number.parseInt(process.env.CAVE_RUNG_BUDGET ?? "5", 10) || 5),
    meatLoopMaxPasses: Math.max(1, Number.parseInt(process.env.CAVE_MEAT_LOOP_PASSES ?? "8", 10) || 8),
    stallThreshold: Math.max(2, Number.parseInt(process.env.CAVE_STALL_THRESHOLD ?? "3", 10) || 3),
    rollbackScoreDrop: Math.max(1, Number.parseInt(process.env.CAVE_ROLLBACK_DROP ?? "5", 10) || 5),
    pauseSec: Math.max(0, Number.parseInt(process.env.CAVE_UNTIL_DEMO_PAUSE_SEC ?? "5", 10) || 5),
    sessionTimeoutMin: Math.max(5, Number.parseInt(process.env.CAVE_SESSION_TIMEOUT_MIN ?? "20", 10) || 20),
    compileFreshnessSec: Math.max(60, Number.parseInt(process.env.CAVE_COMPILE_FRESH_SEC ?? "600", 10) || 600),
    routeProbeMinPassPct: Math.max(50, Number.parseInt(process.env.CAVE_ROUTE_PROBE_MIN ?? "95", 10) || 95),
    demoWorldMinScore: Math.max(70, Number.parseInt(process.env.CAVE_DEMO_WORLD_MIN ?? "75", 10) || 75),
    shipWorldMinScore: Math.max(85, Number.parseInt(process.env.CAVE_SHIP_WORLD_MIN ?? "85", 10) || 85),
    performanceTriBudget: Math.max(100_000, Number.parseInt(process.env.CAVE_TRI_BUDGET ?? "800000", 10) || 800_000),
    dailyTokenBudget: Math.max(0, Number.parseInt(process.env.CAVE_DAILY_TOKEN_BUDGET ?? "0", 10) || 0),
    webhookUrl: process.env.CAVE_BOT_WEBHOOK_URL?.trim() ?? "",
    goldenSeed: Number.parseInt(process.env.CAVE_GOLDEN_SEED ?? "2048271449", 10) || 2048271449,
  };
}

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

/** Dynamic session cap from failing stage count + pending gameplay milestones. */
export function resolveMaxIterations(hubRoot: string, cfg: BotProductionConfig): number {
  if (cfg.maxAgentSessions > 0) return cfg.maxAgentSessions;
  if (cfg.runMode === "nightly") return 8;

  let failing = 12;
  let milestones = 8;
  try {
    const failPath = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildFailingStages.json");
    if (existsSync(failPath)) {
      const doc = JSON.parse(readFileSync(failPath, "utf8")) as { stages?: unknown[] };
      failing = Math.max(1, doc.stages?.length ?? 12);
    }
    const progPath = join(hubRoot, "Assets/Scripts/HubGameProgress.json");
    if (existsSync(progPath)) {
      const prog = JSON.parse(readFileSync(progPath, "utf8")) as {
        milestones?: Record<string, { status?: string }>;
      };
      milestones = Object.values(prog.milestones ?? {}).filter((m) => m.status !== "done").length || 8;
    }
  } catch {
    /* defaults */
  }
  return Math.max(16, failing * 2 + milestones * 3);
}
