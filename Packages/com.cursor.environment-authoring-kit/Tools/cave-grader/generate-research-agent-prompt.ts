#!/usr/bin/env npx tsx
/**
 * Writes CaveBuildResearchAgentPrompt.md — ONE consolidated research prompt (terrain + cave).
 */
import { resolveHubRoot } from "./hub-root.js";
import { writeResearchAgentPrompt } from "./research-agent-prompt.js";

function parsePhaseArg(argv: string[]): string | undefined {
  for (const a of argv) {
    if (a.startsWith("--phase=")) return a.slice("--phase=".length).trim();
  }
  return process.env.CAVE_ACTIVE_PHASE?.trim() || "research";
}

function parseRungArg(argv: string[]): string | undefined {
  for (const a of argv) {
    if (a.startsWith("--rung=")) return a.slice("--rung=".length).trim();
  }
  return process.env.CAVE_ACTIVE_RUNG?.trim();
}

function main() {
  const hubRoot = resolveHubRoot();
  const phaseId = parsePhaseArg(process.argv);
  const rung = parseRungArg(process.argv) as import("./prompt-ladder.js").PromptRung | undefined;
  const { mdRel, jsonRel } = writeResearchAgentPrompt(hubRoot, phaseId, rung);
  console.log(
    `[CaveCursor:info] Consolidated research agent prompt → ${mdRel}, ${jsonRel}`
  );
}

main();
