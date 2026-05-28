#!/usr/bin/env npx tsx
/**
 * Per meat-loop pass: research → plan JSON/MD → Next Steps + DO NOT for agents.
 * Unity sets CAVE_MEAT_PASS before invoking.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { getMeatPassResearch, parseMeatPassArg, type MeatPassResearch } from "./meat-loop-research.js";
import { formatResearchExecutionBlock } from "./research-execution-brief.js";
import { MANDATORY_BUILD_RULES_MD } from "./mandatory-build-rules.js";

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../../..")).replace(/\/$/, "");
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");

const SURFACE_TASK_BY_PASS: Record<number, string> = {
  1: "lidar_refine_smooth",
  4: "intelligent_vegetation_trees",
  6: "roads_water_lidar",
  8: "surface_navmesh_polish",
  12: "intelligent_vegetation_understory",
  14: "playable_world_gate",
  15: "lidar_touchup_final",
};

const WEB_QUERIES: Record<string, string[]> = {
  lidar_refine_smooth: [
    "Florida USGS 3DEP hillshade terrain game level design",
    "Unity terrain heightmap stamp satellite relief",
  ],
  roads_water_lidar: [
    "game world road trail terrain flatten hydrography",
    "LiDAR DEM water basin depression terrain sculpt",
  ],
  intelligent_vegetation_trees: [
    "open world tree placement biome satellite reference",
    "AAA game forest edge placement trail corridor",
  ],
  intelligent_vegetation_understory: [
    "understory bushes flowers procedural placement game",
    "Florida scrub understory vegetation game art",
  ],
  surface_navmesh_polish: ["Unity NavMesh terrain trails walkable bake"],
  playable_world_gate: [
    "playable open world checklist environment art polish NPC hooks",
  ],
  lidar_touchup_final: ["terrain polish heightmap smooth game shipping"],
};

function surfaceTaskForPass(pass: number): string {
  if (process.env.CAVE_WORKFLOW === "cave") return "";
  const mod = ((pass % 16) + 16) % 16;
  return SURFACE_TASK_BY_PASS[mod] ?? "none";
}

function buildPlan(mission: MeatPassResearch, pass: number) {
  const surfaceTask = surfaceTaskForPass(pass);
  const queries = WEB_QUERIES[surfaceTask] ?? [
    "cave environment game production " + mission.researchFocus,
  ];
  return {
    meatPass: pass,
    title: mission.title,
    researchFocus: mission.researchFocus,
    surfaceTask,
    webSearchQueries: queries,
    researchCategories: mission.categories,
    entryIdPrefixes: mission.entryIdPrefixes,
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
      "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan.json",
      "Assets/EnvironmentKit/Generated/PlayableWorldStatus.json",
      "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json",
    ],
    executionSteps: [
      "1. Read ResearchCache + execution brief; run webSearchQueries and save notes under ResearchCache if new.",
      "2. Write or update this plan file; do not purge GeneratedSurfaceWorld.",
      "3. Execute exactly one surfaceTask (Unity meat pass) or one cave fix from Next Steps.",
      "4. For each new surface prop: follow agentPrompt in SurfacePropPlacementPlan.json.",
      "5. Re-grade; update DO NOT list; continue until PlayableWorldStatus.meetsPlayableWorld.",
    ],
    agentFocus:
      surfaceTask === "none"
        ? `Cave meat pass: ${mission.researchFocus}`
        : `Surface meat pass (${surfaceTask}): LiDAR-accurate terrain, roads, water, intelligent vegetation — not random scatter.`,
  };
}

function main() {
  const pass = parseMeatPassArg(process.argv) ?? parseInt(process.env.CAVE_MEAT_PASS ?? "0", 10) || 0;
  const mission = getMeatPassResearch(pass);
  const plan = buildPlan(mission, pass);

  mkdirSync(gen, { recursive: true });
  const planJson = join(gen, `CaveBuildMeatPassPlan_${pass}.json`);
  const planMd = join(gen, `CaveBuildMeatPassPlan_${pass}.md`);
  writeFileSync(planJson, JSON.stringify(plan, null, 2));

  const md = [
    `# Meat pass ${pass}: ${mission.title}`,
    "",
    MANDATORY_BUILD_RULES_MD,
    "",
    `**Surface task:** \`${plan.surfaceTask}\``,
    "",
    "## Research (required)",
    formatResearchExecutionBlock(hubRoot, "terrain_integration", pass),
    "",
    "## Web search (run each; save to ResearchCache)",
    ...plan.webSearchQueries.map((q) => `- ${q}`),
    "",
    "## Execution steps",
    ...plan.executionSteps.map((s) => `- ${s}`),
    "",
    "## Agent focus",
    plan.agentFocus,
    "",
    "## JSON paths",
    ...plan.jsonPaths.map((p) => `- \`${p}\``),
  ].join("\n");
  writeFileSync(planMd, md);

  const nextSteps = [
    `# Next Steps — meat pass ${pass}`,
    "",
    `Active: **${plan.agentFocus}**`,
    "",
    "1. Open `CaveBuildMeatPassPlan_" + pass + ".md` and execute ONE task.",
    "2. Surface: additive only — refine LiDAR, roads/water, or place vegetation per plan.",
    "3. Cave: additive meat fixes only (no PurgeShellLayersOnly).",
    "4. Re-run grade; check `PlayableWorldStatus.json`.",
    "",
    "### Reference images",
    "- ResearchCache hillshade PNGs (fl-*-hillshade)",
    "- Pull top game-dev environment screenshots via web search URLs into ResearchCache when missing.",
  ].join("\n");

  const doNot = [
    `# DO NOT — meat pass ${pass}`,
    "",
    "- Do NOT clear or rebuild entire GeneratedSurfaceWorld during meat loop.",
    "- Do NOT randomly scatter props; use SurfacePropPlacementPlan agentPrompt per instance.",
    "- Do NOT run destructive cave purge after pass 0.",
    "- Do NOT skip research brief / web search before editing terrain or vegetation.",
    "- Do NOT mark ship-ready until PlayableWorldStatus.meetsPlayableWorld is true.",
  ].join("\n");

  writeFileSync(join(gen, "CaveBuildNextStepsPrompt.md"), nextSteps);
  writeFileSync(join(gen, "CaveBuildDoNotPrompt.md"), doNot);
  writeFileSync(join(gen, "CaveBuildMeatPassPlan_latest.json"), JSON.stringify(plan, null, 2));

  console.log(`[meat-pass-plan] pass=${pass} surfaceTask=${plan.surfaceTask}`);
  console.log(`  wrote ${planJson}`);
}

main();
