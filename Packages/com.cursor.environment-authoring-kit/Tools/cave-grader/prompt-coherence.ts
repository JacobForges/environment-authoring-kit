/**
 * Resolves conflicting instructions across bot runners, rung markdown, and phase prompts.
 * Injected at the top of every agent invoke so later sections cannot override these rules.
 */
export type PromptDomain = "cave" | "terrain" | "pre_build";

export const PROMPT_HARMONY_VERSION = "2026-06-09";

export const PROMPT_HARMONY_RULES_MD = `## Prompt harmony — all bot runners (overrides contradictions)

When any other section disagrees with this block, **this block wins**.

1. **One rung, one minimal fix** — Address only the active rung/phase. No full "Build Complete Cave", no deleting \`UndergroundCaveSystem\`, no wiping \`Generated/\` or \`ResearchCache/\`.

2. **Planner session truth** — Read \`CaveBuildActiveSessionConfig.json\` before changing scope. \`agentInvokes: false\` → skip terrain meat tsx and blocking helper exports. \`use3DCaveSystem: false\` → do not block on cave-queue layout gates.

3. **Anchor immutability** — \`SceneGroundResolver\` Ground anchor XZ and the surface cave opening sector are fixed for this seed. Do **not** move cave root, entrance, or opening unless the active rung is \`ground_placement\` / \`terrain_integration\` and JSON shows mouth/placement error above tolerance.

4. **Scope separation** — Terrain agents: surface heightfield, trails, NavMesh, props only. Cave agents: underground geometry, mouth alignment, route probes only. Do not edit the other domain unless the failing stage explicitly requires it.

5. **Terrain is procedural first** — LiDAR/DEM is macro guide only (≤28% structural bias). No DEM photocopy, no map icons/legends, no flat inner disk + quilted outer ring.

6. **Fresh build hygiene** — Never resume stale paced checkpoints (>2× planned steps). Planner approve must clear \`CaveBuildPacedStepCheckpoint.json\`.

7. **Post-build finalize** — Human records Play Mode gameplay for recap; then prefab + scene save + video compose. Bot fixes code only — does not skip \`CaveBuildPostBuildFinalizeGate\`.

8. **Creativity inside code limits** — Vary segment counts, prop density, and scatter within existing C# parameters. Never contradict a locked layout roll, pinned seed, or \`preserveRootWorldXZ\` metadata.

9. **Prompt stack order** — Read in order: (a) this harmony block, (b) Hub bot setup block, (c) \`CaveBuildDoNotPrompt.md\`, (d) \`CaveBuildNextStepsPrompt.md\`, (e) active phase / tailored prompt, (f) rung checklist, (g) production playbook. Ignore duplicate or conflicting older chat instructions.`;

export function formatPromptHarmonyPrelude(
  activeRung: string,
  domain: PromptDomain = "cave"
): string {
  const scope =
    domain === "terrain"
      ? "Terrain ladder — do not edit cave spline/mesh unless `terrain_integration` is the failing stage."
      : domain === "pre_build"
        ? "Pre-build — no scene geometry until compile_gate is clean."
        : "Cave ladder — do not re-sculpt surface heightmaps unless a surface stage is failing.";

  return [
    `# Prompt harmony v${PROMPT_HARMONY_VERSION}`,
    "",
    `**Active rung:** \`${activeRung}\` | **Domain:** ${domain}`,
    `**Scope note:** ${scope}`,
    "",
    PROMPT_HARMONY_RULES_MD,
  ].join("\n");
}

export function promptAlreadyHasHarmony(prompt: string): boolean {
  return prompt.includes("Prompt harmony") || prompt.includes(PROMPT_HARMONY_VERSION);
}
