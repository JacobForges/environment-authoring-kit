/**
 * Writes a single tailored Cursor prompt from live Generated JSON (unique per invoke).
 */
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseMeatPassArg } from "./meat-loop-research.js";
import { writeResearchExecutionBrief } from "./research-execution-brief.js";
import {
  buildLadderPrompt,
  parseRungArg,
  type FailingStagesDoc,
  type LadderContextDoc,
  type PromptRung,
  type QualityReport,
  type VisualShellAudit,
} from "./prompt-ladder.js";

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../..")).replace(/\/$/, "");
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");
const tailoredPath = join(gen, "CaveBuildTailoredAgentPrompt.md");
const activePath = join(gen, "CaveBuildActiveRungPrompt.md");

function loadJson<T>(path: string): T | null {
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch {
    return null;
  }
}

function main() {
  const rung = (parseRungArg(process.argv) ?? process.env.CAVE_CURSOR_RUNG ?? "other") as PromptRung;
  const reportPath = join(gen, "CaveBuildQualityReport.json");
  const report = loadJson<QualityReport>(reportPath);
  if (!report) {
    console.error(`Missing ${reportPath} — grade in Unity first.`);
    process.exit(1);
  }

  const prompt = buildLadderPrompt({
    hubRoot,
    report,
    visualShell: loadJson<VisualShellAudit>(join(gen, "CaveBuildVisualShellAudit.json")),
    failingStages: loadJson<FailingStagesDoc>(join(gen, "CaveBuildFailingStages.json")),
    ladderContext: loadJson<LadderContextDoc>(join(gen, "CaveBuildLadderContext.json")),
    activeRung: rung,
    reportPath,
    visualShellPath: join(gen, "CaveBuildVisualShellAudit.json"),
  });

  writeFileSync(tailoredPath, prompt, "utf8");
  writeFileSync(activePath, prompt, "utf8");
  writeResearchExecutionBrief(hubRoot, rung, parseMeatPassArg(process.argv));
  console.log(`[CaveCursor:info] Tailored prompt (${rung}) → ${tailoredPath}`);
  console.log(`[CaveCursor:info] Execution brief → Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json`);
  console.log(`[CaveCursor:info] Active copy → ${activePath}`);
}

main();
