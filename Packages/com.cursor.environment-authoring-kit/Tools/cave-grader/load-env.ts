import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const FORCE_FROM_FILE = new Set([
  "CURSOR_API_KEY",
  "CAVE_CURSOR_MODEL",
  "CAVE_AI_PROVIDER",
  "HUB_ROOT",
]);

/** Loads Tools/cave-grader/.env into process.env for all Cursor AI scripts. */
export function loadCaveGraderEnv(): void {
  const dir = dirname(fileURLToPath(import.meta.url));
  const envPath = join(dir, ".env");
  if (!existsSync(envPath)) {
    if (!process.env.HUB_ROOT) {
      process.env.HUB_ROOT = join(dir, "../../../..");
    }
    return;
  }

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
    if (FORCE_FROM_FILE.has(key) || !process.env[key]) {
      process.env[key] = value;
    }
  }

  if (!process.env.HUB_ROOT) {
    process.env.HUB_ROOT = join(dir, "../../../..");
  }
}
