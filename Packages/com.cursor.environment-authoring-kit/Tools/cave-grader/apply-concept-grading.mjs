/**
 * Apply Set 02 grading: archive rejects, promote keeps to production folders.
 * Run: HUB_ROOT=/Users/jacob/Hub node --import tsx apply-concept-grading.mjs
 */
import { copyFileSync, mkdirSync, renameSync, existsSync } from "node:fs";
import { join } from "node:path";

const hub = process.env.HUB_ROOT ?? "/Users/jacob/Hub";
const base = join(
  hub,
  "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/set-02-unity"
);
const prodRoot = join(hub, "Assets/EnvironmentKit/ResearchCache/images/concepts/phases");
const rejectRoot = join(prodRoot, "_do-not");

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join as joinPath } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
/** @type {{ folder: string; option: number; verdict: string; doNotTag?: string }[]} */
const grades = JSON.parse(
  readFileSync(joinPath(here, "set-02-grades.json"), "utf8")
);

for (const g of grades) {
  const opt = String(g.option).padStart(2, "0");
  const src = join(base, g.folder, `option-${opt}.png`);
  if (!existsSync(src)) {
    console.warn("missing", src);
    continue;
  }
  if (g.verdict === "keep") {
    const dest = join(prodRoot, g.folder, `approved-${opt}.png`);
    mkdirSync(join(prodRoot, g.folder), { recursive: true });
    copyFileSync(src, dest);
    console.log("KEEP ->", dest);
  } else {
    const tag = g.doNotTag ?? "rejected";
    const destDir = join(rejectRoot, g.folder);
    mkdirSync(destDir, { recursive: true });
    const dest = join(destDir, `option-${opt}-${tag}.png`);
    renameSync(src, dest);
    console.log("REJECT ->", dest);
  }
}
