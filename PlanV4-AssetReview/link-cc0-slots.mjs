#!/usr/bin/env node
/** Copy Quaternius FBX into per-slot folders for Unity import. */
import { copyFile, mkdir } from "node:fs/promises";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const fbxDir = join(
  root,
  "Assets/EnvironmentKit/CC0Imports/quaternius-ultimate-animated-characters/Ultimate Animated Character Pack - Nov 2019/FBX"
);
const outDir = join(root, "Assets/EnvironmentKit/CC0Imports/Characters");

const slots = {
  NPC_PlayGuide: "Wizard.fbx",
  NPC_PlayMerchant: "Kimono_Female.fbx",
  NPC_FoothillRanger: "Soldier_Female.fbx",
  NPC_FoothillHerbalist: "Elf.fbx",
  NPC_PeakHermit: "OldClassy_Female.fbx",
  NPC_PeakOreTrader: "Casual2_Male.fbx",
  NPC_PeakGuard_A: "BlueSoldier_Female.fbx",
  NPC_PeakGuard_B: "Knight_Golden_Female.fbx",
  NPC_HorizonWatcher: "Casual_Bald.fbx",
  NPC_Ambient_A: "Casual3_Female.fbx",
  NPC_Ambient_B: "Casual3_Male.fbx",
  NPC_Ambient_C: "Suit_Male.fbx",
  BOSS_Stage: "Viking_Male.fbx",
  BOSS_Add_1: "Zombie_Female.fbx",
  BOSS_Add_2: "Witch.fbx",
  ENEMY_Foothill_1: "Ninja_Female.fbx",
  ENEMY_Peak_1: "Soldier_Male.fbx",
  ENEMY_Annex_1: "Ninja_Sand.fbx",
};

await mkdir(outDir, { recursive: true });
for (const [slot, file] of Object.entries(slots)) {
  const src = join(fbxDir, file);
  const dest = join(outDir, `${slot}.fbx`);
  await copyFile(src, dest);
  console.log(slot, "<-", file);
}
console.log("Done:", outDir);
