/**
 * Canonical Generated/ paths for Hollow Titan landmark research + phase prompts.
 */
export const HOLLOW_TITAN_GENERATED_REL = "Assets/EnvironmentKit/Generated";

/** Overwritten each meat-phase refresh — single active copy. */
export const HOLLOW_TITAN_ACTIVE_PHASE_PROMPT_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanActivePhasePrompt.md`;

/** ONE consolidated research prompt (concept images + ResearchCache summaries). */
export const HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanResearchAgentPrompt.md`;
export const HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_JSON_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanResearchAgentPrompt.json`;

export const HOLLOW_TITAN_RESEARCH_ACTION_PLAN_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanResearchActionPlan.json`;
export const HOLLOW_TITAN_PHASE_RESEARCH_GATE_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanPhaseResearchGate.json`;

export const HOLLOW_TITAN_PHASE_PROMPT_MANIFEST_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanPhasePromptManifest.json`;

/** Mandatory execution brief — cache entries + concept read order. */
export const HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanResearchExecutionBrief.json`;
export const HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_MD_REL = `${HOLLOW_TITAN_GENERATED_REL}/HollowTitanResearchExecutionBrief.md`;

export function hollowTitanPhasePromptFrontMatter(
  phaseIndex: number,
  phaseId: string,
  seed: number
): string {
  return [
    "---",
    `phaseIndex: ${phaseIndex}`,
    `phaseId: ${phaseId}`,
    `buildSeed: ${seed}`,
    `generatedUtc: ${new Date().toISOString()}`,
    "---",
    "",
  ].join("\n");
}
