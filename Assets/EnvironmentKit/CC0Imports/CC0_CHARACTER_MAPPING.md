# CC0 characters (replaces Mixamo)

**Downloaded:** Quaternius Ultimate Animated Character Pack (52 FBX) + Kenney Animated Characters 2.

| Slot | FBX file | Notes |
|------|----------|--------|
| NPC_PlayGuide | Wizard.fbx | Guide |
| NPC_PlayMerchant | Kimono_Female.fbx | Merchant |
| NPC_FoothillRanger | Soldier_Female.fbx | Ranger |
| NPC_FoothillHerbalist | Elf.fbx | Herbalist |
| NPC_PeakHermit | OldClassy_Female.fbx | Hermit |
| NPC_PeakOreTrader | Casual2_Male.fbx | Trader |
| NPC_PeakGuard_A | BlueSoldier_Female.fbx | Guard |
| NPC_PeakGuard_B | BlueSoldier_Male.fbx | Guard (was Knight_Golden_Female — bad mesh) |
| NPC_HorizonWatcher | Casual_Bald.fbx | Watcher |
| NPC_Ambient_A | Casual3_Female.fbx | Ambient |
| NPC_Ambient_B | Casual3_Male.fbx | Ambient |
| NPC_Ambient_C | Suit_Male.fbx | Ambient |
| BOSS_Stage | Viking_Male.fbx | Boss |
| BOSS_Add_1 | Zombie_Female.fbx | Add |
| BOSS_Add_2 | Witch.fbx | Add |
| ENEMY_Foothill_1 | Ninja_Female.fbx | Enemy |
| ENEMY_Peak_1 | Soldier_Male.fbx | Enemy |
| ENEMY_Annex_1 | Ninja_Sand.fbx | Enemy |

**Surface biome combat roster** (pipeline assigns via `BiomeEnemyCombatCatalog`):

| Biome | Prefab slot |
|-------|-------------|
| PlayKarst | BOSS_Add_2 (Witch) |
| FoothillGreen | ENEMY_Foothill_1 |
| PeakStone | ENEMY_Peak_1 |
| HorizonMist | BOSS_Add_1 (Zombie) |
| AnnexLabyrinth | ENEMY_Annex_1 |
| BossThreshold | BOSS_Stage |
| Cave route | BOSS_Add_1 |

Enemies spawn only on NavMesh inside their native biome. If a native enemy is ever in a different biome, it becomes **Legendary** (higher HP, attack, defense, speed).

Copies live in `CC0Imports/Characters/<slot>.fbx`. Each Quaternius FBX includes its own animation clips.

Extra Kenney clips: `kenney-animated-characters-2/Animations/` (idle, run, jump).

Mixamo (`AdobeImports/Mixamo/`) remains optional polish only.
