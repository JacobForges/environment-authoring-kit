/**
 * Thresholds exported by Unity quality JSON — single source for TS ladders.
 * Fallbacks match CaveBuildQualityRubric + SurfaceTerrainBuildLadder in C#.
 */

export type GradingThresholds = {
  stagePassScore: number;
  stageFloorScore: number;
  targetScore: number;
  shipScore: number;
  betaScore: number;
};

export type ThresholdReport = {
  stagePassScore?: number;
  stageFloorScore?: number;
  targetScore?: number;
  betaScore?: number;
  meetsShipTarget?: boolean;
};

const CAVE_DEFAULTS: GradingThresholds = {
  stagePassScore: 90,
  stageFloorScore: 80,
  targetScore: 95,
  shipScore: 95,
  betaScore: 85,
};

const TERRAIN_DEFAULTS: GradingThresholds = {
  stagePassScore: 90,
  stageFloorScore: 65,
  targetScore: 85,
  shipScore: 95,
  betaScore: 85,
};

export function resolveCaveThresholds(report: ThresholdReport | null | undefined): GradingThresholds {
  return {
    stagePassScore: report?.stagePassScore ?? CAVE_DEFAULTS.stagePassScore,
    stageFloorScore: report?.stageFloorScore ?? CAVE_DEFAULTS.stageFloorScore,
    targetScore: report?.targetScore ?? CAVE_DEFAULTS.targetScore,
    shipScore: report?.targetScore ?? CAVE_DEFAULTS.shipScore,
    betaScore: report?.betaScore ?? CAVE_DEFAULTS.betaScore,
  };
}

export function resolveTerrainThresholds(
  report: ThresholdReport | null | undefined
): GradingThresholds {
  return {
    stagePassScore: report?.stagePassScore ?? TERRAIN_DEFAULTS.stagePassScore,
    stageFloorScore: report?.stageFloorScore ?? TERRAIN_DEFAULTS.stageFloorScore,
    targetScore: report?.targetScore ?? TERRAIN_DEFAULTS.targetScore,
    shipScore: TERRAIN_DEFAULTS.shipScore,
    betaScore: TERRAIN_DEFAULTS.betaScore,
  };
}

export function stageFails(
  row: { score: number; passed?: boolean } | undefined,
  thresholds: GradingThresholds
): boolean {
  if (!row) return true;
  return !row.passed || row.score < thresholds.stagePassScore;
}
