import { existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { loadCaveGraderEnv } from "./load-env.js";

const PLACEHOLDER_HUB = /\/path\/to\/hub/i;

function toolsDir(): string {
  return dirname(fileURLToPath(import.meta.url));
}

function defaultHubFromPackage(): string {
  return join(toolsDir(), "../../../..");
}

function isValidHubRoot(root: string): boolean {
  const normalized = root.replace(/\/$/, "");
  if (!normalized || PLACEHOLDER_HUB.test(normalized)) return false;
  return existsSync(join(normalized, "Assets"));
}

/**
 * Resolves Unity Hub project root. Loads Tools/cave-grader/.env first.
 * Ignores invalid placeholders like /path/to/Hub from copy-pasted docs.
 */
export function resolveHubRoot(): string {
  loadCaveGraderEnv();

  const candidates: string[] = [];
  const fromEnv = process.env.HUB_ROOT?.trim();
  if (fromEnv) candidates.push(fromEnv);
  candidates.push(defaultHubFromPackage());

  for (const c of candidates) {
    const root = c.replace(/\/$/, "");
    if (isValidHubRoot(root)) {
      process.env.HUB_ROOT = root;
      return root;
    }
  }

  const tried = candidates.map((c) => c.replace(/\/$/, "")).join(", ");
  throw new Error(
    `Invalid HUB_ROOT — none of these contain an Assets/ folder: ${tried}\n` +
      `Fix: cd Tools/cave-grader && cp .env.example .env\n` +
      `Set HUB_ROOT=/Users/jacob/Hub (your real Hub path), or run without HUB_ROOT= if .env is correct.\n` +
      `Do not use the documentation placeholder /path/to/Hub.`
  );
}
