/** Pause between chained grader actions and agent prompts (matches Unity CaveBuildActionPacing). */
export const ACTION_DELAY_MS = 300;

export function sleep(ms: number = ACTION_DELAY_MS): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
