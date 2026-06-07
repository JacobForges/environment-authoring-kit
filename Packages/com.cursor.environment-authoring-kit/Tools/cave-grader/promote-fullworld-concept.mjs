/**
 * Promote approved FullWorld concept image from pending-review to production.
 * Run: HUB_ROOT=/Users/jacob/Hub node promote-fullworld-concept.mjs --preset=0 --option=01
 */
import { copyFileSync, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const hub = process.env.HUB_ROOT ?? "/Users/jacob/Hub";
const args = Object.fromEntries(
  process.argv.slice(2).map((a) => {
    const [k, v] = a.replace(/^--/, "").split("=");
    return [k, v ?? "true"];
  })
);

const preset = String(args.preset ?? "0").padStart(2, "0");
const option = String(args.option ?? "01").padStart(2, "0");

const base = join(hub, "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts");
const src = join(base, "_pending-review", preset, `option-${option}.png`);
const destDir = join(base, preset);
const dest = join(destDir, "concept.png");

if (!existsSync(src)) {
  console.error("Missing pending image:", src);
  process.exit(1);
}

mkdirSync(destDir, { recursive: true });
copyFileSync(src, dest);
console.log(`Promoted preset ${preset} option-${option} -> ${dest}`);
