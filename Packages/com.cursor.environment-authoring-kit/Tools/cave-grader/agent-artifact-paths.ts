/**
 * Canonical Generated/ paths: overwrite (current phase) vs lifelong memory.
 */
export const GENERATED_REL = "Assets/EnvironmentKit/Generated";

/** Overwritten each phase refresh — single active copy, not per-phase files. */
export const ACTIVE_PHASE_PROMPT_REL = `${GENERATED_REL}/CaveBuildActivePhasePrompt.md`;
export const PHASE_DATA_DIGEST_REL = `${GENERATED_REL}/CaveBuildPhaseDataDigest.md`;
export const UNIFIED_AGENT_PROMPT_REL = `${GENERATED_REL}/CaveBuildUnifiedAgentPrompt.md`;
export const JSON_MANIFEST_REL = `${GENERATED_REL}/CaveBuildGeneratedJsonManifest.json`;
export const PHASE_PROMPTS_INDEX_REL = `${GENERATED_REL}/CaveBuildPhasePromptsIndex.json`;

/** ONE consolidated research prompt (terrain + cave summaries, max 5 images). */
export const RESEARCH_AGENT_PROMPT_REL = `${GENERATED_REL}/CaveBuildResearchAgentPrompt.md`;
export const RESEARCH_AGENT_PROMPT_JSON_REL = `${GENERATED_REL}/CaveBuildResearchAgentPrompt.json`;

/** Deprecated glob patterns — purged on new build session. */
export const LEGACY_PHASE_PROMPT_PREFIX = "CaveBuildPhasePrompt_";
export const LEGACY_PHASE_DIGEST_PREFIX = "CaveBuildPhaseDataDigest_";

export function phasePromptFrontMatter(phaseId: string, iteration: number, rung: string): string {
  return [
    "---",
    `phaseId: ${phaseId}`,
    `iteration: ${iteration}`,
    `rung: ${rung}`,
    `generatedUtc: ${new Date().toISOString()}`,
    "---",
    "",
  ].join("\n");
}

export function digestFrontMatter(phaseId: string): string {
  return [
    "---",
    `phaseId: ${phaseId}`,
    `generatedUtc: ${new Date().toISOString()}`,
    "---",
    "",
  ].join("\n");
}
