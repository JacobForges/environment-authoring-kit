/**
 * Writes terrain tailored Cursor prompt from live Generated JSON.
 */
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { loadCaveGraderEnv } from "./load-env.js";
import {
  buildTerrainLadderPrompt,
  parseTerrainRungArg,
  pickActiveTerrainRung,
  type TerrainLadderContextDoc,
  type TerrainQualityReport,
} from "./terrain-prompt-ladder.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../..")).replace(/\/$/, "");
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");
const tailoredPath = join(gen, "TerrainBuildTailoredAgentPrompt.md");
const activePath = join(gen, "SurfaceTerrainActiveRungPrompt.md");
const reportPath = join(gen, "SurfaceTerrainQualityReport.json");

function loadJson<T>(path: string): T | null {
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch {
    return null;
  }
}

function main() {
  const report =
    loadJson<TerrainQualityReport>(reportPath) ??
    loadJson<TerrainQualityReport>(join(gen, "SurfaceTerrainBuildLadderReport.json"));
  if (!report) {
    console.error(`Missing ${reportPath} — Re-grade terrain in Unity first.`);
    process.exit(1);
  }

  const rung =
    parseTerrainRungArg(process.argv) ??
    process.env.CAVE_CURSOR_RUNG ??
    pickActiveTerrainRung(report);

  const prompt = buildTerrainLadderPrompt({
    hubRoot,
    report,
    ladderContext: loadJson<TerrainLadderContextDoc>(
      join(gen, "SurfaceTerrainBuildLadderContext.json")
    ),
    activeRung: rung,
    reportPath,
  });

  writeFileSync(tailoredPath, prompt, "utf8");
  writeFileSync(activePath, prompt, "utf8");
  console.log(`[CaveCursor:info] Terrain tailored prompt (${rung}) → ${tailoredPath}`);
  console.log(`[CaveCursor:info] Active copy → ${activePath}`);
}

main();
