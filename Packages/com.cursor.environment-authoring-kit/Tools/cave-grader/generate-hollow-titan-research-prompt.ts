#!/usr/bin/env npx tsx
/**
 * Writes HollowTitanResearchAgentPrompt.md + .json — consolidated landmark research prompt.
 */
import { resolveHubRoot } from "./hub-root.js";
import { writeHollowTitanResearchPrompt } from "./hollow-titan-research-prompt.js";
import { getHollowTitanPhasePrompt } from "./hollow-titan-concept-prompts.js";

function parsePhaseIndex(argv: string[]): number | undefined {
  for (const a of argv) {
    if (a.startsWith("--phase=")) {
      const n = parseInt(a.slice("--phase=".length), 10);
      if (!Number.isNaN(n) && n >= 0 && n <= 11) return n;
    }
  }
  const fromEnv = process.env.HOLLOW_TITAN_ACTIVE_PHASE?.trim();
  if (fromEnv !== undefined && fromEnv !== "") {
    const n = parseInt(fromEnv, 10);
    if (!Number.isNaN(n) && n >= 0 && n <= 11) return n;
  }
  return undefined;
}

function main() {
  const hubRoot = resolveHubRoot();
  const phaseIndex = parsePhaseIndex(process.argv);
  const { mdRel, jsonRel } = writeHollowTitanResearchPrompt(hubRoot, phaseIndex);
  const label =
    phaseIndex !== undefined
      ? `phase ${phaseIndex} (${getHollowTitanPhasePrompt(phaseIndex).id})`
      : "all phases";
  console.log(`[CaveCursor:info] Hollow Titan research prompt (${label}) → ${mdRel}, ${jsonRel}`);
}

main();
