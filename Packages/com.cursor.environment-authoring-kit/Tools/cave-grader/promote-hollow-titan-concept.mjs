/**
 * Promote approved Hollow Titan concept image from pending-review to production.
 * Run: HUB_ROOT=/Users/jacob/Hub node promote-hollow-titan-concept.mjs --target=master --option=01
 *      HUB_ROOT=/Users/jacob/Hub node promote-hollow-titan-concept.mjs --target=05 --option=01
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

const target = String(args.target ?? "master");
const optionRaw = String(args.option ?? "01");
const option = /^\d+$/.test(optionRaw) ? optionRaw.padStart(2, "0") : optionRaw;

const base = join(hub, "Assets/EnvironmentKit/ResearchCache/images/hollow-titan-concepts");
const src = join(base, "_pending-review", target, `option-${option}.png`);
const destDir = join(base, target);
const dest = join(destDir, "concept.png");

if (!existsSync(src)) {
  console.error("Missing pending image:", src);
  process.exit(1);
}

mkdirSync(destDir, { recursive: true });
copyFileSync(src, dest);
console.log(`Promoted ${target} option-${option} -> ${dest}`);
