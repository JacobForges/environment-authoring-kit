# Pre-ONNX Q&A Ladder — surface-content
## scene_scope: MainScene scope
- Keywords: `MainScene, zones_in_scope, out_of_scope`
- Rubric: Must define which surface zones are authored vs excluded for now.
- Example GOOD:
  - Author full slice across town_center, battle_arena, east_trail, cave_gate. Exclude wild_east/wild_west for now to keep this pass Surface-only (variant 0).
  - Author full slice across town_center, battle_arena, east_trail, cave_gate. Exclude wild_east/wild_west for now to keep this pass Surface-only (variant 1).
- Example BAD:
  - TBD.
  - TBD.

## zones: Named zones (town, arena, trails)
- Keywords: `zone ids: town_center/battle_arena/east_trail/cave_gate + notes`
- Rubric: Must list each zone id and what it contains for this slice.
- Example GOOD:
  - zones: town_center (plaza hub), battle_arena (west arm), east_trail (east fork), cave_gate (north mouth). Provide brief notes per zone; no wild zones in scope (variant 0).
  - zones: town_center (plaza hub), battle_arena (west arm), east_trail (east fork), cave_gate (north mouth). Provide brief notes per zone; no wild zones in scope (variant 1).
- Example BAD:
  - TBD.
  - TBD.

## quest_npcs: Quest giver NPCs + objectives
- Keywords: `quest_giver, objective chain, quest ids`
- Rubric: Must name the quest giver NPC and give objective chain with ids.
- Example GOOD:
  - Quest giver: npc_sera_scout at town_center (3,-7) starts quest_sera_east_fork. Objective chain: accept at Sera -> reach prop_sera_fork_sign -> report to npc_lumen_gate; reward: shop unlock + quest_completed unblock.
  - Quest giver: npc_sera_scout at town_center (3,-7) starts quest_sera_east_fork. Objective chain: accept at Sera -> reach prop_sera_fork_sign -> report to npc_lumen_gate; reward: shop unlock + quest_completed unblock.
- Example BAD:
  - TBD.
  - TBD.

## trainer_npcs: Trainer / coach NPCs
- Keywords: `trainer role + grade gates (minTrainingGrade)`
- Rubric: Must name trainer NPC and training gates with thresholds.
- Example GOOD:
  - Trainer: npc_ren_coach at town_center (-6,5) rotY90. Provide Talk+Shop hybrid: show training grade gates minTrainingGrade70 for shop_open and minTrainingGrade40/55 for earlier coaching options.
  - Trainer: npc_ren_coach at town_center (-6,5) rotY90. Provide Talk+Shop hybrid: show training grade gates minTrainingGrade70 for shop_open and minTrainingGrade40/55 for earlier coaching options.
- Example BAD:
  - TBD.
  - TBD.

## ambient_npcs: Ambient flavor NPCs
- Keywords: `npc_ambient_*, dlg_* flavor nodes`
- Rubric: Must include 2–3 dlg_* nodes per ambient NPC (not just positions).
- Example GOOD:
  - Ambient NPCs: npc_ambient_sigrid dlg_sigrid_market, dlg_sigrid_sera_rumor; npc_ambient_jonas dlg_jonas_greetings, dlg_jonas_rumor. Pure flavor (no quests, no shop).
  - Ambient NPCs: npc_ambient_sigrid dlg_sigrid_market, dlg_sigrid_sera_rumor; npc_ambient_jonas dlg_jonas_greetings, dlg_jonas_rumor. Pure flavor (no quests, no shop).
- Example BAD:
  - Ambient NPCs positioned but not dialog-authored.
  - Ambient NPCs positioned but not dialog-authored.

## blocker_gates: Gate NPCs / blocked areas
- Keywords: `gate npc id + gateBlockers list + blocker ids`
- Rubric: Must define gate NPC + blockers + physical seal intent.
- Example GOOD:
  - Gate NPC: npc_lumen_gate blocker_gate. Talk-only sealed -> hint -> clear on quest_mara_intro_dispatch complete. gateBlockers includes blocker_quest_mara_intro_dispatch. Physical seals blocker_cave_gate_north and blocker_...
  - Gate NPC: npc_lumen_gate blocker_gate. Talk-only sealed -> hint -> clear on quest_mara_intro_dispatch complete. gateBlockers includes blocker_quest_mara_intro_dispatch. Physical seals blocker_cave_gate_north and blocker_...
- Example BAD:
  - TBD.
  - TBD.

## enemy_patrols: Enemy spawners + patrol routes
- Keywords: `enemy spawner ids + patrol waypoints`
- Rubric: Must specify spawner ids and 3–4 patrol waypoints each (or justify none).
- Example GOOD:
  - Enemy spawners: enemy_spawner_east_trail wild_east and enemy_spawner_arena_ring wild_west. Provide 3–4 patrol waypoints per spawner with concrete x,z offsets; grade gate for arena ring is minTrainingGrade70.
  - Enemy spawners: enemy_spawner_east_trail wild_east and enemy_spawner_arena_ring wild_west. Provide 3–4 patrol waypoints per spawner with concrete x,z offsets; grade gate for arena ring is minTrainingGrade70.
- Example BAD:
  - TBD.
  - TBD.

## props_layout: Props, signs, interactables
- Keywords: `prop ids + interactable vs dressing + blocker collider props`
- Rubric: Must name prop ids with positions and mark interactable behavior vs dressing.
- Example GOOD:
  - Props: list prop_depot_sign, prop_mara_manifest_crate, prop_depot_pennant (town_center); prop_sera_fork_sign, prop_sera_fork_pennant, prop_jonas_rumor_board (east_trail); prop_cave_mouth_arch (cave_gate); prop_ren_bleach...
  - Props: list prop_depot_sign, prop_mara_manifest_crate, prop_depot_pennant (town_center); prop_sera_fork_sign, prop_sera_fork_pennant, prop_jonas_rumor_board (east_trail); prop_cave_mouth_arch (cave_gate); prop_ren_bleach...
- Example BAD:
  - TBD.
  - TBD.

## textures: Texture / material look per prop cluster
- Keywords: `textureHint per prop cluster (weathered-wood/slate/cyan-banner)`
- Rubric: Must assign textureHint values consistently per prop cluster.
- Example GOOD:
  - textureHint clusters: weathered-wood on depot sign+crate+fork sign; slate on cave mouth arch + rumor board; cyan-banner on elevated pennants/banners. No grade tint; invisible blockers untextured (variant 0).
  - textureHint clusters: weathered-wood on depot sign+crate+fork sign; slate on cave mouth arch + rumor board; cyan-banner on elevated pennants/banners. No grade tint; invisible blockers untextured (variant 1).
- Example BAD:
  - TBD.
  - TBD.

## dialog_hybrid: Hybrid dialog (Talk branches + Shop tab)
- Keywords: `Talk node ids + Shop inventory + blockers arrays`
- Rubric: Must map Talk node chain to Shop and Leave, with blockers where needed.
- Example GOOD:
  - Hybrid dialog: npc_mara_dispatch chain dlg_mara_dispatch->dlg_mara_crate_hint, plus Shop mapping with items and blockers[]. npc_sera_scout offers opt_accept_fork leading to quest_sera_east_fork. Ren has Talk nodes dlg_re...
  - Hybrid dialog: npc_mara_dispatch chain dlg_mara_dispatch->dlg_mara_crate_hint, plus Shop mapping with items and blockers[]. npc_sera_scout offers opt_accept_fork leading to quest_sera_east_fork. Ren has Talk nodes dlg_re...
- Example BAD:
  - TBD.
  - TBD.

## blockers_training: Agent training level blockers
- Keywords: `minTrainingGrade gates with blocker objects`
- Rubric: Must list blocker objects with minTrainingGrade and hint text.
- Example GOOD:
  - Training blockers: blocker_agent_training_70 {type: agent_training_level, minTrainingGrade:70, hint:'Train to 70% in arena first'}; blocker_agent_training_80 and blocker_agent_training_90 similarly; apply to specific sho...
  - Training blockers: blocker_agent_training_70 {type: agent_training_level, minTrainingGrade:70, hint:'Train to 70% in arena first'}; blocker_agent_training_80 and blocker_agent_training_90 similarly; apply to specific sho...
- Example BAD:
  - TBD.
  - TBD.

## blockers_quest: Quest prerequisite blockers
- Keywords: `quest_completed blockers with quest ids`
- Rubric: Must list quest_completed blockers with quest ids and where they apply.
- Example GOOD:
  - Quest blockers: blocker_quest_mara_intro_dispatch {type: quest_completed, questId: quest_mara_intro_dispatch, hint:'Complete dispatch to lift cave gate'}; blocker_quest_sera_east_fork {type: quest_completed, questId: que...
  - Quest blockers: blocker_quest_mara_intro_dispatch {type: quest_completed, questId: quest_mara_intro_dispatch, hint:'Complete dispatch to lift cave gate'}; blocker_quest_sera_east_fork {type: quest_completed, questId: que...
- Example BAD:
  - TBD.
  - TBD.

## placement: World positions & facing
- Keywords: `world positions x,y,z + rotation + facing rules`
- Rubric: Must give x,z positions and facing (rotationY).
- Example GOOD:
  - Placement/facing: npc_mara_dispatch (-7,0,-3) rotY90; npc_sera_scout (3,0,-7) rotY180; npc_lumen_gate (-2,0,8) rotY180; npc_ren_coach (-6,0,5) rotY90. Props at zone offsets near each NPC; ensure rotations face plaza.
  - Placement/facing: npc_mara_dispatch (-7,0,-3) rotY90; npc_sera_scout (3,0,-7) rotY180; npc_lumen_gate (-2,0,8) rotY180; npc_ren_coach (-6,0,5) rotY90. Props at zone offsets near each NPC; ensure rotations face plaza.
- Example BAD:
  - TBD.
  - TBD.

## playmode_smoke: Play Mode smoke test plan
- Keywords: `requiredNpcIds + ordered steps[]`
- Rubric: Must give requiredNpcIds and ordered steps with expected outcomes.
- Example GOOD:
  - requiredNpcIds: ['npc_mara_dispatch','npc_sera_scout','npc_lumen_gate','npc_ren_coach']. steps: 1) Walk to Mara -> accept quest_mara_intro_dispatch -> verify Shop unlock. 2) Talk Lumen sealed until Mara quest complete. 3...
  - requiredNpcIds: ['npc_mara_dispatch','npc_sera_scout','npc_lumen_gate','npc_ren_coach']. steps: 1) Walk to Mara -> accept quest_mara_intro_dispatch -> verify Shop unlock. 2) Talk Lumen sealed until Mara quest complete. 3...
- Example BAD:
  - TBD QA plan.
  - TBD QA plan.

