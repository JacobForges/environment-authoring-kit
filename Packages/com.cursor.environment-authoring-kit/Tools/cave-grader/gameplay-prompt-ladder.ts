import { readFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import {
  loadHubGameProgress,
  type HubGameProgressDoc,
} from "./mission-phase.js";

export const GAMEPLAY_MILESTONE_ORDER = [
  "G1_assembly_bootstrap",
  "G2_player_scene_wiring",
  "G3_combat_stats",
  "G4_surface_to_portal",
  "G5_cave_goal_marker",
  "G6_combat_encounter",
  "G7_hud_objective",
  "G8_demo_smoke_pass",
] as const;

export type GameplayMilestoneId = (typeof GAMEPLAY_MILESTONE_ORDER)[number];

export const HUB_GAME_PROGRESS_REL = "Assets/Scripts/HubGameProgress.json";

export function pickActiveGameplayMilestone(
  progress: HubGameProgressDoc | null,
  override?: string | null
): GameplayMilestoneId {
  if (override && GAMEPLAY_MILESTONE_ORDER.includes(override as GameplayMilestoneId)) {
    return override as GameplayMilestoneId;
  }
  for (const id of GAMEPLAY_MILESTONE_ORDER) {
    const row = progress?.milestones?.[id];
    const status = row?.status ?? "pending";
    if (status !== "done") return id;
  }
  return "G8_demo_smoke_pass";
}

export function parseGameplayMilestoneArg(argv: string[]): string | null {
  for (const arg of argv) {
    if (arg.startsWith("--milestone=")) return arg.slice("--milestone=".length).trim();
  }
  return process.env.CAVE_GAMEPLAY_MILESTONE?.trim() || null;
}

export function buildGameplayLadderPrompt(opts: {
  hubRoot: string;
  activeMilestone: GameplayMilestoneId;
  progressPath: string;
}): string {
  const progress = loadHubGameProgress(opts.hubRoot);
  const row = progress?.milestones?.[opts.activeMilestone];
  const title = row?.title ?? opts.activeMilestone;
  const acceptance = row?.acceptance ?? "See docs/GAMEPLAY_DEMO_ACCEPTANCE.md";

  const pending = GAMEPLAY_MILESTONE_ORDER.filter(
    (id) => (progress?.milestones?.[id]?.status ?? "pending") !== "done"
  );

  return [
    "# Hub Phase 2 — Gameplay demo (one milestone per session)",
    "",
    "## Mission",
    "World pipeline gate passed. Build **playable demo** in `Assets/Scripts/` + scene wiring.",
    "",
    "## Active milestone",
    `- **ID:** \`${opts.activeMilestone}\``,
    `- **Title:** ${title}`,
    `- **Acceptance:** ${acceptance}`,
    "",
    "## Rules",
    "- Edit **only** this milestone. Smallest correct diff.",
    "- Prefer reusing EnvKit runtime: `PlayerController`, `HumanoidCombatSpawner`, `WorldItemInventory`.",
    "- **Do not** run Full AAA Rebuild.",
    `- After edits: update \`${HUB_GAME_PROGRESS_REL}\` — set this milestone status to done or blocked with notes.`,
    "- Set `demoReady: true` only when G8 complete and GAMEPLAY_DEMO_ACCEPTANCE criteria met.",
    "- G8: set milestone `ready_for_human` after automated smoke; human Play Mode confirms then sets `demoReady: true`.",
    "",
    "## Read first",
    `- \`${opts.hubRoot}/docs/GAMEPLAY_DEMO_ACCEPTANCE.md\``,
    `- \`${opts.hubRoot}/Assets/Narrative/HubStoryBible.json\` (tone, acts, myth variants)`,
    `- \`${opts.hubRoot}/Assets/Narrative/BarkDatabase.json\` + \`NarrationBarkPlayer\``,
    `- \`${opts.hubRoot}/docs/CURSOR_BOT_BACKLOG.md\` (Stream F)`,
    `- \`${opts.hubRoot}/.cursor/skills/hub-gameplay-completion/SKILL.md\``,
    `- Progress: \`${opts.progressPath}\``,
    `- Smoke tests: \`Assets/Tests/PlayMode/HubDemoSmokeTests.cs\``,
    "",
    "## Narrative (production demo)",
    "- Three-act arc: surface walkie → cave Lumen → goal epilogue (`DemoEpilogueDirector`).",
    "- Wire `BarkTrigger` on trail beacons, `InspectableStoryProp` on titan rim, `AudioLogPickup` along route.",
    "- Use `DemoObjectiveHud` for G7 (not full MMO HUD unless stretch).",
    "- `LocalizedStringTable.csv` for player-facing strings.",
    "",
    "## Pending milestones",
    pending.length ? pending.map((id) => `- ${id}`).join("\n") : "- none (verify demoReady)",
    "",
    "## Implement now",
    `Complete **${opts.activeMilestone}** — ${title}.`,
  ].join("\n");
}

export function ensureHubGameProgressFile(hubRoot: string): string {
  const path = join(hubRoot, HUB_GAME_PROGRESS_REL);
  if (!existsSync(path)) {
    throw new Error(
      `Missing ${path}. Copy template from docs or create Assets/Scripts/HubGameProgress.json.`
    );
  }
  return path;
}
