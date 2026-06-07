import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

export const IDEAL_LAYOUT_STYLE_ID = "concept_00_ideal_labyrinth";
export const CLASSIC_FULLWORLD_STYLE_ID = "concept_01_classic_rings";
/** @deprecated use IDEAL_LAYOUT_STYLE_ID */
export const LEGACY_IDEAL_LAYOUT_STYLE_ID = "ideal_layout_49";

export const ACTIVE_GENERATION_STYLE_REL =
  "Assets/EnvironmentKit/Generated/ActiveGenerationStyle.json";

export const CONCEPTS_ROOT_REL =
  "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts";

export type ActiveGenerationStyle = {
  conceptIndex?: number;
  styleIndex?: number;
  styleId?: string;
  displayName?: string;
  seed?: number;
  conceptImageRel?: string;
  conceptImageExists?: boolean;
  randomOnBuild?: boolean;
  similarityRule?: string;
};

export function loadActiveGenerationStyle(hubRoot: string): ActiveGenerationStyle {
  const path = join(hubRoot, ACTIVE_GENERATION_STYLE_REL);
  if (!existsSync(path)) return {};
  try {
    return JSON.parse(readFileSync(path, "utf8")) as ActiveGenerationStyle;
  } catch {
    return {};
  }
}

export function loadActiveGenerationStyleId(hubRoot: string): string {
  const raw = loadActiveGenerationStyle(hubRoot);
  return raw.styleId?.trim() || CLASSIC_FULLWORLD_STYLE_ID;
}

export function loadActiveConceptIndex(hubRoot: string): number {
  const raw = loadActiveGenerationStyle(hubRoot);
  const idx = raw.conceptIndex ?? raw.styleIndex ?? 0;
  return Math.max(0, Math.min(9, Math.floor(idx)));
}

export function loadActiveConceptImageRel(hubRoot: string): string {
  const raw = loadActiveGenerationStyle(hubRoot);
  if (raw.conceptImageRel?.trim()) return raw.conceptImageRel.trim();
  const idx = loadActiveConceptIndex(hubRoot);
  return `${CONCEPTS_ROOT_REL}/${String(idx).padStart(2, "0")}/concept.png`;
}

export function isIdealLayoutStyle(hubRoot: string): boolean {
  const id = loadActiveGenerationStyleId(hubRoot);
  return (
    id === IDEAL_LAYOUT_STYLE_ID ||
    id === LEGACY_IDEAL_LAYOUT_STYLE_ID ||
    loadActiveConceptIndex(hubRoot) === 0
  );
}
