/**
 * Hollow Titan landmark meat phases (0–11) — research categories + agent focus.
 * Always includes hollow_titan + hollow_titan_do_not + phase-specific categories.
 */
import type { PipelinePhaseDef } from "./pipeline-phase-types.js";

const HT = "hollow_titan";
const HT_DO_NOT = "hollow_titan_do_not";
const FW_DO_NOT = "fullworld_do_not";

export const HOLLOW_TITAN_PIPELINE_PHASES: PipelinePhaseDef[] = [
  {
    id: "hollow_titan_site_pick",
    title: "Hollow Titan — site pick (peak ring tile)",
    rung: "ground_placement",
    meatPass: 0,
    researchCategories: [HT, HT_DO_NOT, FW_DO_NOT, "terrain", "ground_placement", "mountain_terrain"],
    webSearchQueries: [
      "open world landmark POI peak tile placement game design",
      "Elden Ring erdtree vista navigation landmark",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/HollowTitanPhasePromptManifest.json",
      "Assets/EnvironmentKit/ResearchCache/categories/hollow_titan/index.json",
    ],
    focus: "ONE task: pick one eligible peak-ring surface tile; write build data + floor plan.",
    allowed: ["HollowTitanLandmarkSitePicker peak ring", "Destroy prior root", "Floor plan config"],
    forbidden: ["Cave mouth tiles", "Labyrinth bench tiles", "Trail corridor tiles"],
  },
  {
    id: "hollow_titan_base_snap",
    title: "Hollow Titan — base snap to terrain",
    rung: "ground_placement",
    meatPass: 1,
    researchCategories: [HT, HT_DO_NOT, "terrain", "ground_placement", "engine_docs"],
    webSearchQueries: ["Unity Terrain.SampleHeight prop grounding karst hillside"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/HollowTitanActivePhasePrompt.md",
    ],
    focus: "ONE task: SnapRootBaseToGround at site XZ + clearance — no float, no bury.",
    allowed: ["SampleHeight snap", "Refresh SiteWorldPosition", "ResnapAfterTerrainSculpt hook"],
    forbidden: ["Re-sculpt terrain under landmark", "Skip snap after outer-ring sculpt"],
  },
  {
    id: "hollow_titan_trunk_shell",
    title: "Hollow Titan — exterior stump shell (terraform bowl)",
    rung: "visual_shell",
    meatPass: 2,
    researchCategories: [HT, HT_DO_NOT, "terrain", "surface_props", "visual_reference", "materials_lighting"],
    webSearchQueries: [
      "hollow dead tree stump terrain sculpt game environment bowl rim",
      "Unity terrain heightmap carve landmark POI stump",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/HollowTitanBuildLadderReport.json",
      "Assets/EnvironmentKit/ResearchCache/images/hollow-titan-concepts/02/concept.png",
    ],
    focus:
      "ONE task: BarkOuter collision shell + hollow stump bowl terraform (StumpTerraformDone marker) — NO log/branch props.",
    allowed: ["Exterior/TrunkShell/BarkOuter", "HollowTitanExteriorTerraform sculpt", "BossStagePortal child"],
    forbidden: ["Branch_/Log_ CC0 props on exterior", "LPMagicalForest trunk stack as primary shell", "DeadBranches scatter"],
  },
  {
    id: "hollow_titan_hollow_carve",
    title: "Hollow Titan — interior void marker",
    rung: "visual_shell",
    meatPass: 3,
    researchCategories: [HT, HT_DO_NOT, "mesh_shell", "visual_reference"],
    webSearchQueries: ["vertical dungeon hollow interior void game environment"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json"],
    focus: "ONE task: HollowVolume marker ~62% radius — collider off, renderer off.",
    allowed: ["Interior/HollowVolume child", "Offset above base", "Cathedral void readability"],
    forbidden: ["Enabled HollowVolume collider", "Solid trunk interior"],
  },
  {
    id: "hollow_titan_floor_plates",
    title: "Hollow Titan — interior floor plates",
    rung: "floor_collision",
    meatPass: 4,
    researchCategories: [HT, HT_DO_NOT, "floor_collision", "adventure"],
    webSearchQueries: ["multi-floor vertical dungeon ring platforms game design"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json"],
    focus: "ONE task: four stacked ring platforms from FloorPlan.Floors.",
    allowed: ["Cylinder plates per floor", "Dark wood tint", "Narrowing toward crown"],
    forbidden: ["Single flat floor only", "Gaps breaking sightlines across void"],
  },
  {
    id: "hollow_titan_stair_spiral",
    title: "Hollow Titan — spiral stair ramp",
    rung: "floor_collision",
    meatPass: 5,
    researchCategories: [HT, HT_DO_NOT, "adventure", "visual_reference"],
    webSearchQueries: ["spiral ramp interior medieval tree dungeon stairs"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json"],
    focus: "ONE task: continuous spiral steps between all four floors.",
    allowed: ["Cube steps from FloorPlan.Stairs", "Inner bark wall hug", "Brown wood tint"],
    forbidden: ["Disconnected landings", "Modern stair rail meshes"],
  },
  {
    id: "hollow_titan_dead_branch_scatter",
    title: "Hollow Titan — dead branch scatter (deprecated)",
    rung: "prop_trees",
    meatPass: 6,
    researchCategories: [HT, HT_DO_NOT, "surface_props", "visual_reference"],
    webSearchQueries: ["hollow stump landmark exterior terraform only no branch props"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/HollowTitanBuildLadderReport.json",
    ],
    focus:
      "DEPRECATED — exterior is terraform-only stump bowl. Meat phase clears DeadBranches; ladder skips this rung.",
    allowed: ["Empty Exterior/DeadBranches", "Proceed to entrance_framing"],
    forbidden: ["Branch_01–04 scatter", "Log_Branched_01 props", "Any CC0 branch dressing on exterior"],
  },
  {
    id: "hollow_titan_entrance_framing",
    title: "Hollow Titan — entrance arch frame",
    rung: "visual_shell",
    meatPass: 7,
    researchCategories: [HT, HT_DO_NOT, "visual_reference", "adventure"],
    webSearchQueries: ["medieval hollow log entrance arch game landmark"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/ResearchCache/images/hollow-titan-concepts/07/concept.png",
    ],
    focus: "ONE task: Log_Hollow_01 arch along EntranceForward — not a cave mouth.",
    allowed: ["TryPlaceEntranceArch", "Boss portal approach", "Cube fallback if LP missing"],
    forbidden: ["Cube arch when Log_Hollow_01 loads", "Orient away from EntranceForward"],
  },
  {
    id: "hollow_titan_per_floor_spawn_markers",
    title: "Hollow Titan — per-floor enemy spawn markers",
    rung: "other",
    meatPass: 8,
    researchCategories: [HT, HT_DO_NOT, "adventure", "qa_testing"],
    webSearchQueries: ["landmark encounter spawn pacing AAA open world"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/HollowTitanLandmarkSpawnManifest.json",
    ],
    focus: "ONE task: landmark-only enemy capsules — 2 per floor, offset from stairs.",
    allowed: ["HollowTitanLandmarkSpawnPoint", "HollowTitanLandmarkSpawner", "ENEMY_Annex"],
    forbidden: ["SurfaceWorld enemy scatter tables", "World pickup spawns on landmark"],
  },
  {
    id: "hollow_titan_lighting_fog_mood",
    title: "Hollow Titan — lighting & fog mood",
    rung: "materials",
    meatPass: 9,
    researchCategories: [HT, HT_DO_NOT, "materials_lighting", "engine_docs"],
    webSearchQueries: [
      "URP fog interior warm lighting exterior dark contrast",
      "Unity light probes multi-floor interior",
    ],
    jsonPaths: ["Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json"],
    focus: "ONE task: warm floor point lights + interior fog volume — dark exterior bark.",
    allowed: ["Point lights per floor", "Mood/FogVolume marker", "Light probe placement"],
    forbidden: ["Uniform exterior/interior brightness", "Harsh fog band at entrance"],
  },
  {
    id: "hollow_titan_landmark_loot_table",
    title: "Hollow Titan — landmark loot table",
    rung: "other",
    meatPass: 10,
    researchCategories: [HT, HT_DO_NOT, "adventure", "qa_testing"],
    webSearchQueries: ["landmark loot table scoped encounter game design"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/HollowTitanLandmarkSpawnManifest.json",
    ],
    focus: "ONE task: green loot markers per floor — landmark catalog IDs + manifest JSON.",
    allowed: ["PickLootId from landmark catalog", "HollowTitanLandmarkSpawnManifest.Write"],
    forbidden: ["World pickup loot IDs", "Dense loot clutter blocking stairs"],
  },
  {
    id: "hollow_titan_enemy_patrol_nodes",
    title: "Hollow Titan — enemy patrol nodes",
    rung: "other",
    meatPass: 11,
    researchCategories: [HT, HT_DO_NOT, "adventure", "qa_testing"],
    webSearchQueries: ["landmark patrol ring encounter loop game AI"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json",
      "Assets/EnvironmentKit/Generated/HollowTitanLandmarkSpawnManifest.json",
    ],
    focus: "ONE task: 3 patrol nodes per floor at ~75% radius — completes encounter loop.",
    allowed: ["Patrol spawn points", "HollowTitanPatrolAgent hooks", "Log site tile + seed"],
    forbidden: ["World patrol graphs", "Patrol nodes blocking entrance arch"],
  },
];

export function hollowTitanPhaseForIndex(phaseIndex: number) {
  return HOLLOW_TITAN_PIPELINE_PHASES[phaseIndex] ?? HOLLOW_TITAN_PIPELINE_PHASES[0];
}
