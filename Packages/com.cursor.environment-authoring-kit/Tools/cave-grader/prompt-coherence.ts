/**
 * Resolves conflicting instructions across bot runners, rung markdown, and phase prompts.
 * Injected at the top of every agent invoke so later sections cannot override these rules.
 */
export type PromptDomain = "cave" | "terrain" | "pre_build";

export const PROMPT_HARMONY_VERSION = "2026-05-26";

export const PROMPT_HARMONY_RULES_MD = `## Prompt harmony — all bot runners (overrides contradictions)

When any other section disagrees with this block, **this block wins**.

1. **One rung, one minimal fix** — Address only the active rung/phase. No full "Build Complete Cave", no deleting \`UndergroundCaveSystem\`, no wiping \`Generated/\` or \`ResearchCache/\`.

2. **Anchor immutability** — \`SceneGroundResolver\` Ground anchor XZ and the surface cave opening sector are fixed for this seed. Do **not** move cave root, entrance, or opening unless the active rung is \`ground_placement\` / \`terrain_integration\` and JSON shows mouth/placement error above tolerance.

3. **Scope separation** — Terrain agents: surface heightfield, trails, NavMesh, props only. Cave agents: underground geometry, mouth alignment, route probes only. Do not edit the other domain unless the failing stage explicitly requires it.

4. **Terrain is procedural first** — LiDAR/DEM is macro guide only (≤28% structural bias). No DEM photocopy, no map icons/legends, no flat inner disk + quilted outer ring.

5. **Creativity inside code limits** — Vary segment counts, prop density, and scatter within existing C# parameters. Never contradict a locked layout roll, pinned seed, or \`preserveRootWorldXZ\` metadata.

6. **Prompt stack order** — Read in order: (a) this harmony block, (b) \`CaveBuildDoNotPrompt.md\`, (c) \`CaveBuildNextStepsPrompt.md\`, (d) active phase / tailored prompt, (e) rung checklist. Ignore duplicate or conflicting older chat instructions.`;

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
