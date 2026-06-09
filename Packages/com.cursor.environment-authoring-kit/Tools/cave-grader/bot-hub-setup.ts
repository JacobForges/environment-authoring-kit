/**
 * Hub 2026-06 bot context — planner-first workflow, post-build finalize, storage.
 */
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { listPlaybookIds } from "./bot-playbooks/index.js";

export const HUB_SETUP_VERSION = "2026-06-09";

export function formatHubBotSetupBlock(hubRoot: string): string {
  const sessionPath = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json");
  let sessionLine = "No finalized planner session on disk — read Generated manifest before editing.";
  if (existsSync(sessionPath)) {
    try {
      const doc = JSON.parse(readFileSync(sessionPath, "utf8")) as {
        label?: string;
        tileCount?: number;
        agentInvokes?: boolean;
        use3DCaveSystem?: boolean;
      };
      sessionLine =
        `Active planner: **${doc.label ?? "session"}** — ${doc.tileCount ?? "?"} tiles, ` +
        `agentInvokes=${doc.agentInvokes !== false}, caves=${doc.use3DCaveSystem !== false}.`;
    } catch {
      sessionLine = "CaveBuildActiveSessionConfig.json present but unreadable.";
    }
  }

  return [
    `## Hub bot setup v${HUB_SETUP_VERSION}`,
    "",
    sessionLine,
    "",
    "### Mandatory behavior",
    "1. **Planner-first** — `CaveBuildActiveSessionConfig.json` is scope truth. Never expand to 289 tiles unless planner requests extended grid.",
    "2. **One rung / one fix** — `./Tools/cursor-bot/run-session.sh --stream`. No Full AAA Rebuild unless the human explicitly asked in chat.",
    "3. **Fast demo** — when `agentInvokes: false`, skip terrain meat tsx, crater repair loops, and blocking helper exports.",
    "4. **Fresh builds** — on planner approve call `CaveBuildPersistedSessionReset.ClearForNewBuild`; reject paced checkpoints >2× planned budget.",
    "5. **Post-build human gate** — generation ends with Play Mode prompt → gameplay MP4 → scene save → prefab export → `DemoRecapPresentation.mp4`. Bot does not skip this.",
    "6. **Storage** — `Assets/EnvironmentKit/Generated/` may symlink to Lexar; JSON on disk is truth.",
    "7. **Playbooks** — cite one of: " + listPlaybookIds().join(", "),
    "",
    "### Do not",
    "- Resume 8000+ step checkpoints from broken terrain runs.",
    "- Block on cave queue when planner has `use3DCaveSystem: false`.",
    "- Finalize demo recording before Play Mode when post-build gate is active.",
  ].join("\n");
}
