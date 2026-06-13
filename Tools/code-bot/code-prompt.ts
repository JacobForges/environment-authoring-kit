import { join } from "node:path";
import {
  HUB_CODE_PROGRESS_REL,
  type CodeTaskId,
  type HubCodeProgressDoc,
  pendingCodeTasks,
} from "./code-progress.js";

export function buildCodeBotPrompt(opts: {
  hubRoot: string;
  activeTask: string;
  progress: HubCodeProgressDoc | null;
}): string {
  const row = opts.progress?.tasks?.[opts.activeTask];
  const title = row?.title ?? opts.activeTask;
  const acceptance = row?.acceptance ?? "Task completes with compile-clean wiring.";
  const stream = row?.stream ?? "integration";
  const files = row?.files?.length ? row.files.map((f) => `- \`${f}\``).join("\n") : "- `Assets/Scripts/`";

  const pending = pendingCodeTasks(opts.progress);

  return [
    "# Hub Code Bot — finish wiring (one task per session)",
    "",
    "## Mission",
    "You are the **code completion bot**. Wire up gameplay, competition, challenge, and multiplayer code.",
    "**Do not** edit world generation (`Packages/com.cursor.environment-authoring-kit/Editor/Blockout/`).",
    "**Do not** run Full AAA Rebuild or paced terrain/cave builds.",
    "",
    "## Active task",
    `- **ID:** \`${opts.activeTask}\``,
    `- **Stream:** ${stream}`,
    `- **Title:** ${title}`,
    `- **Acceptance:** ${acceptance}`,
    "",
    "## Primary files",
    files,
    "",
    "## Rules",
    "1. Complete **only** this task — smallest correct diff.",
    "2. Edit `Assets/Scripts/` (+ `Assets/Scripts/Editor/` for setup menus). Scene wiring via editor setup scripts, not hand-editing huge .unity YAML.",
    "3. Reuse EnvKit runtime types — do not duplicate `PlayerController`, spawners, etc.",
    "4. After edits: update `" + HUB_CODE_PROGRESS_REL + "` — set this task `status` to `done` or `blocked` with `notes`.",
    "5. Set `codeReady: true` only when **every** task is `done` and compile is clean.",
    "6. Tag non-obvious fixes: `// [code-bot:task_id:YYYY-MM-DD]`.",
    "",
    "## Post-pass report (if present)",
    `- \`${opts.hubRoot}/Assets/EnvironmentKit/Generated/CodeBotPostPassReport.json\``,
    "",
    "## Read first",
    `- \`${opts.hubRoot}/.cursor/skills/hub-code-completion/SKILL.md\``,
    `- \`${opts.hubRoot}/Assets/Scripts/HubCodeProgress.json\``,
    `- \`${opts.hubRoot}/Assets/Scripts/HubGameProgress.json\` (demo milestones G1–G8)`,
    `- \`${opts.hubRoot}/docs/GAMEPLAY_DEMO_ACCEPTANCE.md\``,
    "",
    "## Wiring checklist (verify for this task)",
    "- Types compile across Hub + Hub.Editor asmdefs",
    "- Runtime bootstrap exists (no missing singleton on Play Mode enter)",
    "- UI show/hide pairs match (`ShowForGameplay` / `HideForMenu`)",
    "- Chat commands registered in `AgentCommandParser` + `AgentCommandHelp`",
    "- Death/lineage uses `MarkDeceased` not hard delete for agents",
    "",
    "## Pending tasks",
    pending.length ? pending.map((id) => `- ${id}`).join("\n") : "- none — set codeReady if all acceptance met",
    "",
    "## Implement now",
    `Finish **${opts.activeTask}** — ${title}.`,
  ].join("\n");
}

export function ensureCodeProgressFile(hubRoot: string): string {
  const path = join(hubRoot, HUB_CODE_PROGRESS_REL);
  return path;
}
