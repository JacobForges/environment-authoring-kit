/**
 * Above-ground terrain / surface phases — separate from cave (underground) grading.
 */
import type { PipelinePhaseDef } from "./pipeline-phase-types.js";

export const TERRAIN_PIPELINE_PHASES: PipelinePhaseDef[] = [
  {
    id: "dem_georeference",
    title: "DEM georeferenced terrain stamp",
    rung: "ground_placement",
    researchCategories: ["terrain"],
    webSearchQueries: ["WGS84 DEM terrain Unity georeference heightmap", "USGS 3DEP bbox terrain alignment"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceDemGeorefStatus.json",
      "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json",
    ],
    focus:
      "ONE task: stamp heights using county manifest bbox (lon/lat) — not naive UV. Preserve main-land center disk.",
  },
  {
    id: "surface_route_bot",
    title: "Surface route bot (trails → mouth)",
    rung: "ground_placement",
    researchCategories: ["terrain", "ground_placement"],
    webSearchQueries: ["open world trail playtest bot walkability", "cave entrance trail approach level design"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildSurfaceRouteProbe.json",
      "Assets/EnvironmentKit/Generated/CaveBuildPhaseBotReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
    ],
    focus:
      "ONE task: fix surface route probe failures on trails/roads/mouth. Do not edit UndergroundCaveSystem geometry.",
  },
  {
    id: "surface_terrain_extend",
    title: "Surface — ensure terrain extends from main land",
    rung: "ground_placement",
    researchCategories: ["terrain", "ground_placement"],
    webSearchQueries: ["Unity terrain extend existing mesh ground blend edge"],
    jsonPaths: [
      "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json",
      "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
    ],
    focus:
      "ONE task: integration terrain inset at Ground anchor. Do NOT delete UndergroundCaveSystem.",
  },
  {
    id: "surface_lidar_stamp",
    title: "Surface — LiDAR hillshade height stamp",
    rung: "ground_placement",
    researchCategories: ["terrain"],
    webSearchQueries: ["USGS 3DEP Florida panhandle DEM hillshade terrain authoring"],
    jsonPaths: [
      "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json",
      "Assets/EnvironmentKit/ResearchCache/images/fl-bay-hillshade/hillshade.png",
    ],
    focus:
      "ONE task: stamp ResearchCache hillshade relief outward only; preserve main-land center disk.",
  },
  {
    id: "surface_navmesh",
    title: "Surface — NavMesh for player/NPC",
    rung: "navmesh",
    researchCategories: ["ground_placement"],
    webSearchQueries: ["Unity NavMesh terrain trails walkable bake"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
    ],
    focus:
      "ONE task: walkable NavMesh on terrain + trails only. Exclude water, mountains, cave opening markers.",
  },
  {
    id: "surface_vegetation_intelligent",
    title: "Surface — intelligent vegetation (not random)",
    rung: "terrain_integration",
    meatPass: 4,
    researchCategories: ["visual_reference", "terrain"],
    webSearchQueries: [
      "open world tree placement satellite biome",
      "AAA environment prop placement trail corridor",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
      "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json",
    ],
    focus:
      "ONE task: place trees/bushes/flowers from project prefabs along trail sectors per SurfacePropPlacementPlan.",
  },
  {
    id: "surface_roads_water_lidar",
    title: "Surface — roads and water from LiDAR",
    rung: "terrain_integration",
    meatPass: 6,
    researchCategories: ["terrain"],
    webSearchQueries: ["LiDAR hydrography terrain depression game", "road bench flatten hillshade"],
    jsonPaths: [
      "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json",
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
    ],
    focus:
      "ONE task: refine road/trail benches and water basins using hillshade luminance (additive terrain height only).",
  },
  {
    id: "terrain_phase_sculpt",
    title: "Surface sculpt — world FBM blend (Ground-centered)",
    rung: "terrain_integration",
    researchCategories: ["terrain"],
    webSearchQueries: [
      "Unity terrain world space FBM heightmap no banding",
      "terrain heightmap terrace artifact perlin strata avoid",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainSculptAgentPrompt.md",
      "Assets/EnvironmentKit/Generated/CaveBuildResearchActionPlan.json",
    ],
    focus:
      "ONE task: SurfaceTerrainCenteredAuthor world-space FBM — no full-map SetHeights freeze after normalize.",
  },
  {
    id: "terrain_phase_dem",
    title: "Terrain AI phase 1 — DEM georeference stamp",
    rung: "terrain_integration",
    meatPass: 1,
    researchCategories: ["terrain"],
    webSearchQueries: ["DEM georeference terrain stamp game world", "LiDAR hillshade terrain alignment Unity"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json",
      "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json",
    ],
    focus: "ONE task: georeference-stamp terrain from ResearchCache hillshades. No cave geometry above surface.",
  },
  {
    id: "terrain_phase_smooth",
    title: "Terrain AI phase 2 — outer ring smooth",
    rung: "terrain_integration",
    meatPass: 2,
    researchCategories: ["terrain"],
    webSearchQueries: ["terrain heightmap denoise outer ring smooth open world"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json"],
    focus: "ONE task: smooth outer height ring — preserve main play bowl.",
  },
  {
    id: "terrain_phase_trails",
    title: "Terrain AI phase 3 — trail and road benches",
    rung: "terrain_integration",
    meatPass: 3,
    researchCategories: ["terrain", "ground_placement"],
    webSearchQueries: ["trail bench terrain game design", "road flatten hillshade luminance"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json",
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
    ],
    focus: "ONE task: bench trails/roads; NavMesh must connect trail → cave mouth.",
  },
  {
    id: "terrain_phase_water",
    title: "Terrain AI phase 4 — water basins polish",
    rung: "terrain_integration",
    meatPass: 4,
    researchCategories: ["terrain"],
    webSearchQueries: ["hydrography terrain depression game lake basin"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json"],
    focus: "ONE task: polish water basins; no floating water above terrain.",
  },
  {
    id: "terrain_phase_final_polish",
    title: "Terrain AI phase 5 — final playable smooth + NavMesh",
    rung: "terrain_integration",
    meatPass: 5,
    researchCategories: ["terrain", "ground_placement"],
    webSearchQueries: ["NavMesh open world terrain bake Unity 6", "playable slope limit walking game"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json",
      "Assets/EnvironmentKit/Generated/CaveBuildSurfaceRouteProbe.json",
    ],
    focus: "ONE task: final gentle smooth + bake surface NavMesh.",
  },
  {
    id: "terrain_ladder_heightfield_no_craters",
    title: "Terrain ladder — no craters/bowls",
    rung: "terrain_integration",
    researchCategories: ["terrain"],
    webSearchQueries: [
      "terrain heightmap crater artifact fix",
      "radial water bowl terrain depression game level",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainActiveRungPrompt.md",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json",
    ],
    focus: "ONE task: eliminate center craters and bowl cells on above-ground heightfield.",
  },
  {
    id: "terrain_ladder_prop_trees",
    title: "Terrain ladder — place trees one-by-one",
    rung: "terrain_integration",
    researchCategories: ["terrain", "visual_reference"],
    webSearchQueries: ["open world tree placement trail spacing game art"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
    ],
    focus: "ONE task: place ONLY tree category along trail sectors from catalog.",
  },
  {
    id: "terrain_ladder_prop_grass",
    title: "Terrain ladder — place grass one-by-one",
    rung: "terrain_integration",
    researchCategories: ["terrain"],
    webSearchQueries: ["grass ground cover terrain snap prefab placement"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
    ],
    focus: "ONE task: place grass/ground-cover prefabs — trail-adjacent, terrain-snapped.",
  },
  {
    id: "terrain_ladder_prop_bushes",
    title: "Terrain ladder — place bushes one-by-one",
    rung: "terrain_integration",
    researchCategories: ["terrain"],
    webSearchQueries: ["understory bush placement open world environment"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
    ],
    focus: "ONE task: place bush/shrub prefabs only from scanned assets.",
  },
  {
    id: "terrain_ladder_prop_ground_cover",
    title: "Terrain ladder — flowers / ground cover",
    rung: "terrain_integration",
    researchCategories: ["terrain", "visual_reference"],
    webSearchQueries: ["flower ground cover placement game environment"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
    ],
    focus: "ONE task: place flower/ground-cover prefabs only.",
  },
  {
    id: "surface_playable_world_gate",
    title: "Above-ground playable world gate",
    rung: "packaging_readiness",
    meatPass: 14,
    researchCategories: ["ground_placement", "terrain"],
    webSearchQueries: ["playable open world environment checklist ship criteria"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/PlayableWorldStatus.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
    ],
    focus:
      "ONE task: clear PlayableWorldStatus blockers for surface/terrain only (trails, vegetation, mouth approach).",
  },
  {
    id: "terrain_meat_loop",
    title: "Terrain meat loop — grade/fix above-ground",
    rung: "terrain_integration",
    researchCategories: ["terrain"],
    webSearchQueries: [],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json",
    ],
    focus: "ONE task: fix weakest terrain ladder rung only — no UndergroundCaveSystem edits.",
  },
];

const TERRAIN_MEAT_PASS_PHASE: Record<number, string> = {
  1: "terrain_phase_dem",
  2: "terrain_phase_smooth",
  3: "terrain_phase_trails",
  4: "terrain_phase_water",
  5: "terrain_phase_final_polish",
  6: "surface_roads_water_lidar",
  8: "surface_navmesh",
  12: "surface_vegetation_intelligent",
  14: "surface_playable_world_gate",
  15: "surface_lidar_stamp",
};

export function phaseForTerrainMeatPass(pass: number): PipelinePhaseDef | undefined {
  const mod = ((pass % 16) + 16) % 16;
  const id = TERRAIN_MEAT_PASS_PHASE[mod];
  if (id) return TERRAIN_PIPELINE_PHASES.find((p) => p.id === id);
  return TERRAIN_PIPELINE_PHASES.find((p) => p.meatPass === pass);
}

export function phaseForTerrainRung(rung: string): PipelinePhaseDef | undefined {
  return TERRAIN_PIPELINE_PHASES.find((p) => p.rung === rung);
}
