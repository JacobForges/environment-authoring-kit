/** Machine-readable lines Unity parses from grade-and-fix stdout/stderr. */
export const PHASE_COMPLETE_PREFIX = "[CaveCursor:phase-complete]";

export type WorkflowMode = "pre_build" | "post_build" | "terrain";

export function emitPhaseComplete(
  workflow: WorkflowMode,
  rung: string,
  reason: "done" | "already_passing" | "no_errors" = "done"
): void {
  console.log(`${PHASE_COMPLETE_PREFIX} workflow=${workflow} rung=${rung} reason=${reason}`);
}

/** Legacy tokens still accepted by Unity stream parser. */
export function legacyTokensForRung(rung: string): string[] {
  if (rung === "compile_gate") return ["PREBUILD_COMPILE_CLEAN"];
  return [`PREBUILD_RUNG_COMPLETE:${rung}`];
}
