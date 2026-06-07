import type { PipelinePhaseDef } from "./pipeline-phase-types.js";

const genFocus: Record<string, string> = {
  gen_01_tps_layout_resync:
    "Regenerate maze layout with CaveThirdPersonClearance — corridor ~8.75m+, ceiling ~26m+ above floor.",
  gen_02_route_ceiling_rebuild: "Rebuild RouteTerrain floor+ceiling meshes for new headroom.",
  gen_03_headroom_intruder_clear: "Clear block rings inside third-person route column.",
  gen_04_visual_shell_repair: "Additive route shell repair — no full cave teleport.",
  gen_05_single_ceiling_enforce: "Exactly one route ceiling mesh — no stacked slabs.",
  gen_06_walkway_mark: "Mark adventure walkable floors.",
  gen_07_spawn_pad: "Spawn ground pad + reachability.",
  gen_08_floor_collision: "Player floor collision — no invisible pits.",
  gen_09_block_tunnel_audit: "Block tunnel collider audit.",
  gen_10_invisible_collider_strip: "Strip invisible blocking colliders.",
  gen_11_cavern_finish_open: "Widen finish cavern cells for TPS camera orbit.",
  gen_12_material_repair: "Cave PBR materials.",
  gen_13_adventure_lighting: "Torch/key lighting along layout.",
  gen_14_atmosphere_fog: "Underground fog/atmosphere.",
  gen_15_navmesh_bake: "NavMesh on walk surfaces.",
  gen_16_route_headroom_probe: "Audit ceiling clearance per route cell vs MinWalkClearance.",
  gen_17_cave_route_export: "Export route probe JSON.",
  gen_18_compact_layer_purge: "Purge duplicate shell layers.",
  gen_19_onion_shell_purge: "Remove onion shell offenders.",
  gen_20_platform_gap_audit: "Jump gap count sanity.",
  gen_21_mob_spawner_spacing: "Mob spawners along route.",
  gen_22_water_pool_safety: "Water pools — no spawn traps.",
  gen_23_performance_budget: "Renderer/triangle budget.",
  gen_24_adventure_visual_pass: "Block visibility + torches.",
  gen_25_ground_xz_lock: "Lock cave world XZ after mouth placement.",
  gen_26_mouth_depth_snap: "Depth-only mouth snap to terrain.",
  gen_27_navmesh_rebake_force: "Force NavMesh rebake.",
  gen_28_playable_world_gate: "PlayableWorldStatus gate.",
  gen_29_quality_grade_export: "Quality report snapshot.",
  gen_30_generation_complete: "TPS cave generation quality pass complete.",
};

export const CAVE_GENERATION_QUALITY_PHASES: PipelinePhaseDef[] = Object.entries(genFocus).map(
  ([id, focus]) => ({
    id,
    title: id.replace(/_/g, " "),
    rung: "visual_shell" as const,
    researchCategories: ["mesh_shell", "ground_placement", "visual_reference"],
    webSearchQueries: [
      "third person camera cave level design ceiling height",
      "procedural cave mesh headroom Unity",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveGenerationQualityPhaseLog.json",
      "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
      "Assets/EnvironmentKit/Generated/CaveBuildResearch.json",
    ],
    focus,
  }),
);
