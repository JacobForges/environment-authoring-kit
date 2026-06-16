import { existsSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const FORCE_FROM_FILE = new Set([
  "CURSOR_API_KEY",
  "CAVE_CURSOR_MODEL",
  "CAVE_AI_PROVIDER",
  "DIRECTOR_DATA_ROOT",
]);

function applyEnvFile(envPath: string): void {
  if (!existsSync(envPath)) return;
  const text = readFileSync(envPath, "utf8");
  for (const line of text.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const eq = trimmed.indexOf("=");
    if (eq <= 0) continue;
    const key = trimmed.slice(0, eq).trim();
    let value = trimmed.slice(eq + 1).trim();
    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }
    if (FORCE_FROM_FILE.has(key)) {
      if (value) process.env[key] = value;
    } else if (!process.env[key]) {
      process.env[key] = value;
    }
  }
}

function caveGraderEnvPaths(studioRoot: string): string[] {
  const paths: string[] = [];
  const hub = process.env.HUB_ROOT?.trim();
  if (hub) {
    paths.push(
      join(
        hub,
        "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
      )
    );
  }
  const sibling = join(
    resolve(studioRoot, ".."),
    "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
  );
  if (!paths.includes(sibling)) paths.push(sibling);
  return paths.filter((p) => existsSync(p));
}

/** Loads ai-director-studio/.env (and Hub cave-grader/.env fallback) into process.env. */
export function loadCaveGraderEnv(): void {
  const dir = dirname(fileURLToPath(import.meta.url));
  const studioRoot = join(dir, "..");
  const envPath = join(studioRoot, ".env");

  applyEnvFile(envPath);

  if (!process.env.CURSOR_API_KEY?.trim()) {
    for (const caveEnv of caveGraderEnvPaths(studioRoot)) {
      applyEnvFile(caveEnv);
      if (process.env.CURSOR_API_KEY?.trim()) break;
    }
  }

  if (!process.env.STUDIO_ROOT) {
    process.env.STUDIO_ROOT = studioRoot;
  }
}

export function studioEnvPath(): string {
  const dir = dirname(fileURLToPath(import.meta.url));
  return join(dir, "..", ".env");
}
