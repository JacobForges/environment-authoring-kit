/**
 * Per-phase art-direction + builder prompts for Hollow Titan landmark meat phases (0–11).
 * Master concept: Assets/EnvironmentKit/ResearchCache/images/hollow-titan-concepts/master/concept.png
 */

export const HOLLOW_TITAN_CONCEPTS_ROOT_REL =
  "Assets/EnvironmentKit/ResearchCache/images/hollow-titan-concepts";

export const HOLLOW_TITAN_MASTER_IMAGE_REL = `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/master/concept.png`;

export const HOLLOW_TITAN_SCALE_BLOCK = [
  "Hollow Titan landmark: one massive mystical medieval hollow dead tree per FullWorld seed.",
  "Surface terrain tile only — never cave mouths, labyrinth benches, or trail corridors.",
  "Massive scale: trunk radius ~30 m, height ~82 m, 4 interior floors @ ~6.5 m each.",
  "Landmark-only spawns (enemies, loot, patrol) — never world scatter tables.",
  "Ground snap: SampleHeight at site XZ + base clearance (~0.35 m). Re-snap after terrain sculpt.",
  "Similar silhouette per buildSeed — not a pixel clone of concept.png.",
].join("\n");

export type HollowTitanPhasePrompt = {
  index: number;
  id: string;
  label: string;
  /** What builders/agents should do visually this step */
  promptCore: string;
  /** Kit execution notes */
  builderNotes: string[];
  /** Relative path under hollow-titan-concepts/ */
  conceptImageRel: string;
};

export const HOLLOW_TITAN_PHASE_PROMPTS: HollowTitanPhasePrompt[] = [
  {
    index: 0,
    id: "hollow_titan_site_pick",
    label: "Hollow Titan — site pick",
    promptCore:
      "Pick one random eligible surface peak tile in Florida karst terrain. Exclude cave mouths, labyrinth annex, and trail corridors. Place root at sampled ground Y. Massive scale jitter from seed (~30 m trunk, ~82 m tall, 4 floors).",
    builderNotes: [
      "HollowTitanLandmarkSitePicker — Chebyshev peak ring tiles only.",
      "Destroy prior HollowTitanLandmark root; write HollowTitanLandmarkBuildData.",
      "Configure floor plan via HollowTitanLandmarkFloorPlanner.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/00/concept.png`,
  },
  {
    index: 1,
    id: "hollow_titan_base_snap",
    label: "Hollow Titan — base snap",
    promptCore:
      "Snap tree base to terrain SampleHeight at stored site XZ with small clearance above ground. Root must sit naturally on karst hillside — no floating trunk, no buried base.",
    builderNotes: [
      "HollowTitanLandmarkTerrainSnap.SnapRootBaseToGround.",
      "Refresh SiteWorldPosition in build data after snap.",
      "Re-run after terrain sculpt via ResnapAfterTerrainSculpt.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/01/concept.png`,
  },
  {
    index: 2,
    id: "hollow_titan_trunk_shell",
    label: "Hollow Titan — trunk shell",
    promptCore:
      "Exterior dead-tree mass: hyper-real LPMagicalForest trunk stack — Tree_Mammoth_Green_01 hero column, oak/beech curtain rings, spruce/pine canopy, moss/fern/vine base dress. Yaw jitter from seed. Mystical hollow titan silhouette — ancient, weathered, slightly Jim Henson labyrinth mood. CC0 L01 cylinders only when LP assets are missing.",
    builderNotes: [
      "HollowTitanExteriorDressing.BuildHyperRealTrunkShell — LPMagicalForest prefabs (primary).",
      "Hero: Tree_Mammoth_Green_01 stack; curtain: Tree_Oak_Green_01, Tree_Beech_Green_01, Tree_Poplar_Green_01 rings.",
      "Canopy: Tree_Spruce_Green_01, Tree_Pine_Green_01 at crown; base: fern, mossy rocks, Vines_01/02.",
      "CC0 L01–L04 bark column fallback only when LPMagicalForest missing from project.",
      "Ensure BossStagePortal child exists at entrance approach.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/02/concept.png`,
  },
  {
    index: 3,
    id: "hollow_titan_hollow_carve",
    label: "Hollow Titan — hollow carve",
    promptCore:
      "Carve interior void: inner cylinder ~62% trunk radius, ~88% height, offset slightly above base. Invisible collider-free volume marker — the playable hollow core agents will fill with floors and stairs.",
    builderNotes: [
      "Interior/HollowVolume primitive — renderer off, collider removed.",
      "Keep exterior shell readable; void must read as walkable cathedral space.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/03/concept.png`,
  },
  {
    index: 4,
    id: "hollow_titan_floor_plates",
    label: "Hollow Titan — floor plates",
    promptCore:
      "Four stacked ring platforms inside the hollow trunk — dark wood/earth tone plates at planned floor Y. Each ring slightly narrower toward the top. Clear sightlines across the hollow core.",
    builderNotes: [
      "Interior/Floors — cylinder plates from FloorPlan.Floors.",
      "Plate scale ~1.85× floor radius, 0.35 m thick.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/04/concept.png`,
  },
  {
    index: 5,
    id: "hollow_titan_stair_spiral",
    label: "Hollow Titan — stair spiral",
    promptCore:
      "Spiral ramp of wooden steps hugging the inner bark wall — readable third-person climb between all four floors. Warm medieval craft, not modern stairs. Continuous spiral, no gaps between floor landings.",
    builderNotes: [
      "Interior/Stairs — cube steps from FloorPlan.Stairs.",
      "Position/rotate per stair node; brown wood tint.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/05/concept.png`,
  },
  {
    index: 6,
    id: "hollow_titan_dead_branch_scatter",
    label: "Hollow Titan — dead branches",
    promptCore:
      "~14–18 dead diagonal LP branch stubs on exterior mid-to-upper trunk — Branch_01–04 and Log_Branched_01, broken and leafless, gothic fairy-tale mood. Radial scatter with seed jitter; never block the main entrance arch. CC0 L05–L10 limbs only as fallback.",
    builderNotes: [
      "HollowTitanExteriorDressing.ScatterDeadBranches — LP Branch_01–04 + Log_Branched_01 (primary).",
      "CC0 L05–L10 limb fallback only when LP branch prefabs missing.",
      "Height 22–90% of trunk; reach beyond trunk radius; never block entrance arch.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/06/concept.png`,
  },
  {
    index: 7,
    id: "hollow_titan_entrance_framing",
    label: "Hollow Titan — entrance frame",
    promptCore:
      "Grand arched entrance on trunk base — Log_Hollow_01 hyper-real hollow log frame with tall opening into the interior. Boss portal approach visible. Mystical medieval gate, not a cave mouth. Cube arch fallback only when LP prefab missing.",
    builderNotes: [
      "HollowTitanExteriorDressing.TryPlaceEntranceArch — Log_Hollow_01 prefab (primary).",
      "Cube arch + darker opening cube fallback when Log_Hollow_01 missing.",
      "Orient along FloorPlan.EntranceForward from planner.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/07/concept.png`,
  },
  {
    index: 8,
    id: "hollow_titan_per_floor_spawn_markers",
    label: "Hollow Titan — floor spawn markers",
    promptCore:
      "Landmark-only enemy spawn capsules on each floor ring — 2 per floor, offset from stairs. Red preview markers; ENEMY_Annex spawner on root. Never mix with world enemy scatter.",
    builderNotes: [
      "Spawns/Enemies — HollowTitanLandmarkSpawnPoint per floor.",
      "Ensure HollowTitanLandmarkSpawner on root with guard prefab.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/08/concept.png`,
  },
  {
    index: 9,
    id: "hollow_titan_lighting_fog_mood",
    label: "Hollow Titan — lighting & fog",
    promptCore:
      "Warm amber point lights at each floor center — brighter ground floor. Soft interior fog volume filling mid-trunk. Mystical medieval lantern mood inside the hollow; darker exterior bark contrast.",
    builderNotes: [
      "Mood/Lights — point lights per floor; range ~1.2× trunk radius.",
      "Mood/FogVolume — invisible sphere marker at 45% height.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/09/concept.png`,
  },
  {
    index: 10,
    id: "hollow_titan_landmark_loot_table",
    label: "Hollow Titan — landmark loot table",
    promptCore:
      "Green loot preview markers on each floor — landmark loot IDs only. Write spawn manifest JSON for agents. One loot per floor default; offset from enemy clusters.",
    builderNotes: [
      "Spawns/Loot — HollowTitanLandmarkSpawnManifest.Write.",
      "PickLootId from landmark catalog — never world pickup tables.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/10/concept.png`,
  },
  {
    index: 11,
    id: "hollow_titan_enemy_patrol_nodes",
    label: "Hollow Titan — enemy patrol nodes",
    promptCore:
      "Blue patrol node ring on each floor — 3 nodes per floor at ~75% floor radius. Completes landmark encounter loop. Final phase logs site tile, seed, floor count.",
    builderNotes: [
      "Spawns/Patrol — patrol spawn points per floor.",
      "Patrol nodes complement per-floor enemy spawns — landmark scope only.",
    ],
    conceptImageRel: `${HOLLOW_TITAN_CONCEPTS_ROOT_REL}/11/concept.png`,
  },
];

export function getHollowTitanPhasePrompt(index: number): HollowTitanPhasePrompt {
  return HOLLOW_TITAN_PHASE_PROMPTS[index] ?? HOLLOW_TITAN_PHASE_PROMPTS[0];
}

export function formatHollowTitanPhasePromptBlock(
  phaseIndex: number,
  hubRoot: string
): string {
  const hub = hubRoot.replace(/\/$/, "");
  const phase = getHollowTitanPhasePrompt(phaseIndex);
  const lines = [
    "## Hollow Titan phase guide (planning — open before editing)",
    "",
    `**Master concept:** \`${hub}/${HOLLOW_TITAN_MASTER_IMAGE_REL}\``,
    `**Phase concept:** \`${hub}/${phase.conceptImageRel}\``,
    "",
    HOLLOW_TITAN_SCALE_BLOCK,
    "",
    `### ${phase.label} (phase ${phase.index})`,
    "",
    phase.promptCore,
    "",
    "**Builder notes:**",
    ...phase.builderNotes.map((n) => `- ${n}`),
    "",
  ];
  return lines.join("\n");
}
