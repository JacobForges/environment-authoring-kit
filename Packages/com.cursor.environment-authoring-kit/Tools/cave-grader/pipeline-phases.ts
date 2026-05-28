/**
 * Cave (underground) pipeline phases for AI prompt generation.
 * Above-ground terrain phases live in terrain-pipeline-phases.ts.
 */
import { CAVE_GENERATION_QUALITY_PHASES } from "./cave-generation-phases.js";
import { PLAYTEST_POLISH_PHASES } from "./playtest-polish-phases.js";
import { TERRAIN_PIPELINE_PHASES } from "./terrain-pipeline-phases.js";

export type { PipelinePhaseDef } from "./pipeline-phase-types.js";
import type { PipelinePhaseDef } from "./pipeline-phase-types.js";

export const CAVE_PIPELINE_PHASES: PipelinePhaseDef[] = [
  {
    id: "research",
    title: "Research pull + catalog",
    rung: "research",
    researchCategories: ["terrain", "ground_placement", "visual_reference"],
    webSearchQueries: [
      "Unity 6 terrain cave entrance carve best practices",
      "commercial game environment art ship criteria playtest",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildResearch.json",
      "Assets/EnvironmentKit/Generated/CaveBuildResearchCache.json",
      "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json",
      "Assets/EnvironmentKit/ResearchCache/index.json",
    ],
    focus: "Use ResearchCache entries and hillshades; plan only — no C# until compile_gate passes.",
  },
  {
    id: "compile_gate",
    title: "Compile gate (zero CS errors)",
    rung: "compile_gate",
    researchCategories: ["visual_reference"],
    webSearchQueries: ["Unity Editor script compile error fix workflow"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/CaveBuildCompileDiagnostics.json"],
    focus:
      "Read CaveBuildCompileDiagnostics.json — fix ONLY verifiedOnDisk errors. If staleErrorCount>0, do NOT edit lines already fixed on disk; wait for Unity recompile.",
  },
  {
    id: "visual_shell",
    title: "Visual shell / route meshes",
    rung: "visual_shell",
    meatPass: 0,
    researchCategories: ["visual_reference", "terrain"],
    webSearchQueries: ["procedural cave mesh floor ceiling no z-fighting Unity"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildVisualShellAudit.json",
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
    ],
    focus: "RouteTerrain strips, no onion shells, invisible collider cleanup.",
  },
  {
    id: "ground_placement",
    title: "Underground mouth seal",
    rung: "ground_placement",
    meatPass: 1,
    researchCategories: ["ground_placement"],
    webSearchQueries: ["cave entrance mouth underground seal game level design"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildLadderContext.json",
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
    ],
    focus: "Snap UndergroundCaveSystem mouth depth-only — do not sculpt above-ground terrain.",
  },
  {
    id: "cave_mouth_seal",
    title: "Underground mouth seal",
    rung: "ground_placement",
    meatPass: 1,
    researchCategories: ["ground_placement"],
    webSearchQueries: ["cave entrance mouth underground seal"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json"],
    focus: "Grade/fix UndergroundCaveSystem mouth only — terrain is SurfaceTerrainBuildLadder.",
  },
  {
    id: "layout_platforms",
    title: "Layout — walk platforms + gaps",
    rung: "floor_collision",
    meatPass: 2,
    researchCategories: ["ground_placement", "visual_reference"],
    webSearchQueries: [
      "cave platformer moving platform spacing commercial game",
      "Unity walk mesh platform jump gap recovery pit design",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildRouteProbe.json",
      "Assets/EnvironmentKit/Generated/CaveBuildPhaseBotReport.json",
      "Assets/EnvironmentKit/Generated/CaveBuildResearchActionPlan.json",
    ],
    focus:
      "ONE task: ensure ≥6 route platforms on solution path, even spacing, pit recovery at gaps. Read bot report before edits.",
  },
  {
    id: "moving_platforms",
    title: "Moving platforms + timing",
    rung: "floor_collision",
    meatPass: 2,
    researchCategories: ["visual_reference"],
    webSearchQueries: [
      "game design moving platform timing cave level",
      "Unity Translate moving platform CharacterController",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
      "Assets/EnvironmentKit/Generated/CaveBuildPhaseBotReport.json",
    ],
    focus:
      "ONE task: add or tune moving platforms on hard gaps only — slow oscillation, visible mesh, no invisible ride colliders.",
  },
  {
    id: "fog_layout",
    title: "Fog layout — surface open / cave mist",
    rung: "materials",
    meatPass: 6,
    researchCategories: ["visual_reference"],
    webSearchQueries: [
      "URP height fog cave entrance transition open world",
      "Unity RenderSettings fog distance underground only",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
    ],
    focus:
      "ONE task: fog/mist under cave mouth and interior only; surface stays clear sunny sky. No global fog on trails.",
  },
  {
    id: "floor_collision",
    title: "Player floor / walkability",
    rung: "floor_collision",
    meatPass: 2,
    researchCategories: ["ground_placement"],
    webSearchQueries: ["Unity CharacterController cave floor fall through fix"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildRouteProbe.json",
      "Assets/EnvironmentKit/Generated/CaveBuildFailingStages.json",
    ],
    focus: "Walk colliders, spawn pad, route probe pass.",
  },
  {
    id: "cinematic_lighting",
    title: "Cinematic lighting (AAA)",
    rung: "materials",
    researchCategories: ["visual_reference"],
    webSearchQueries: [
      "AAA game environment lighting key fill rim cave",
      "Unity URP cinematic lighting reflection probes",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveCinematicLightingManifest.json",
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
    ],
    focus:
      "ONE task: tune CaveCinematicLightingPass — surface sun, cave key/fill/rim, no global fog on trails. Read manifest JSON.",
  },
  {
    id: "materials_lighting",
    title: "Materials + lighting",
    rung: "materials",
    meatPass: 5,
    researchCategories: ["visual_reference"],
    webSearchQueries: ["URP cave lighting interior emissive best practices"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json"],
    focus: "URP materials, point lights, no blown exposure.",
  },
  {
    id: "atmosphere_fog",
    title: "Atmosphere + fog",
    rung: "materials",
    meatPass: 6,
    researchCategories: ["visual_reference"],
    webSearchQueries: ["Unity URP volumetric fog underground performance"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json"],
    focus: "Fog/mist only in cave — keep surface open sky.",
  },
  {
    id: "mob_spawns",
    title: "Enemies + mob coverage",
    rung: "other",
    meatPass: 7,
    researchCategories: ["visual_reference"],
    webSearchQueries: ["action adventure cave enemy spawn pacing design"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildCombatProbe.json",
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
    ],
    focus: "Mob markers along route; combat probe pass.",
  },
  {
    id: "performance",
    title: "Performance / XR budget",
    rung: "performance",
    meatPass: 10,
    researchCategories: ["visual_reference"],
    webSearchQueries: ["Unity mobile cave mesh triangle budget LOD"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json"],
    focus: "Triangle budget, culling — no full rebuild.",
  },
  {
    id: "packaging_ship",
    title: "Packaging / ship gate",
    rung: "other",
    meatPass: 14,
    researchCategories: ["ground_placement", "terrain"],
    webSearchQueries: ["game vertical slice ship criteria environment art"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
      "Assets/EnvironmentKit/Generated/CaveBuildGradingManifest.json",
    ],
    focus: "First-minute play, underground mouth, packaging_readiness stage.",
  },
  {
    id: "meat_loop_additive",
    title: "Meat loop — additive pass (no purge)",
    rung: "other",
    researchCategories: ["visual_reference"],
    webSearchQueries: [],
    jsonPaths: ["Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json"],
    focus:
      "ONE task: add props/light/mobs for this underground pass only. Do NOT sculpt terrain or PurgeShellLayersOnly.",
  },
  ...PLAYTEST_POLISH_PHASES,
  ...CAVE_GENERATION_QUALITY_PHASES,
];

/** Cave + terrain + polish — used for manifest / catalog discovery. */
export const PIPELINE_PHASES: PipelinePhaseDef[] = [
  ...CAVE_PIPELINE_PHASES,
  ...TERRAIN_PIPELINE_PHASES,
];

export function phaseForPolishStep(step: number): PipelinePhaseDef | undefined {
  if (step < 1000 || step >= 1060) return undefined;
  const id = `polish_${String(step - 999).padStart(2, "0")}_`;
  return PLAYTEST_POLISH_PHASES.find((p) => p.id.startsWith(id)) ?? PLAYTEST_POLISH_PHASES[step - 1000];
}

export function phaseForMeatPass(pass: number): PipelinePhaseDef | undefined {
  return CAVE_PIPELINE_PHASES.find((p) => p.meatPass === pass);
}

export { phaseForTerrainMeatPass, TERRAIN_PIPELINE_PHASES } from "./terrain-pipeline-phases.js";

export function phaseForRung(rung: string): PipelinePhaseDef | undefined {
  return PIPELINE_PHASES.find((p) => p.rung === rung);
}
