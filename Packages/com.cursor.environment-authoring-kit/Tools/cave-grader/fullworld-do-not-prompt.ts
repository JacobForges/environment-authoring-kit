export { FULLWORLD_DO_NOT_PROMPT_BULLETS } from "./fullworld-do-not-papers.js";

export function isFullWorldSurfacePhase(phaseId: string | undefined): boolean {
  if (!phaseId) return false;
  return (
    phaseId.startsWith("mountain_") ||
    phaseId.startsWith("surface_") ||
    phaseId === "ground_placement" ||
    phaseId === "terrain_integration" ||
    phaseId === "nine_tile_grid" ||
    phaseId === "outer_ring_mountains"
  );
}
