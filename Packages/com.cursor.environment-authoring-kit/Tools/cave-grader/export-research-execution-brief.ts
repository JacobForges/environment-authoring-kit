#!/usr/bin/env npx tsx
import { join } from "node:path";
import { parseMeatPassArg } from "./meat-loop-research.js";
import { parseRungArg } from "./prompt-ladder.js";
import {
  writeResearchExecutionBrief,
  type ResearchBriefScope,
} from "./research-execution-brief.js";
import { resolveHubRoot } from "./hub-root.js";

function parseScopeArg(argv: string[]): ResearchBriefScope {
  for (const a of argv) {
    if (a.startsWith("--scope=")) {
      const v = a.slice("--scope=".length).toLowerCase();
      if (v === "terrain" || v === "cave" || v === "combined") return v;
    }
  }
  return "combined";
}

const hubRoot = resolveHubRoot();
const rung = parseRungArg(process.argv) ?? "other";
const meatPass = parseMeatPassArg(process.argv);
const scope = parseScopeArg(process.argv);

const rel = writeResearchExecutionBrief(hubRoot, rung, meatPass, scope);
const meatNote = meatPass !== undefined ? ` meat-pass=${meatPass}` : "";
console.log(
  `[ResearchExecution] Wrote ${rel} (scope=${scope}, rung=${rung}${meatNote})` +
    (scope === "combined" ? " + TerrainResearchExecutionBrief + CaveResearchExecutionBrief" : "")
);
