import { copyFileSync, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const hub = process.env.HUB_ROOT ?? "/Users/jacob/Hub";
const srcBase = join(
  hub,
  "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/set-02-unity"
);
const prodBase = join(hub, "Assets/EnvironmentKit/ResearchCache/images/concepts/phases");

const folders = [
  "00_fullworld_overview",
  "01_play_disk",
  "02_foothill_ring_sculpt",
  "03_south_foothill_labyrinth_row",
  "04_peak_ring",
  "05_wilderness_mountains",
  "06_mountain_labyrinth_carve",
  "07_mountain_trails",
  "08_wilderness_cave_mouth",
  "09_cave_primary_mouth",
  "10_cave_underground",
];

let promoted = 0;
for (const folder of folders) {
  const prodDir = join(prodBase, folder);
  mkdirSync(prodDir, { recursive: true });
  for (let n = 1; n <= 5; n++) {
    const opt = String(n).padStart(2, "0");
    const src = join(srcBase, folder, `option-${opt}.png`);
    const dest = join(prodDir, `approved-${opt}.png`);
    if (!existsSync(src)) {
      console.warn("missing", src);
      continue;
    }
    copyFileSync(src, dest);
    promoted++;
  }
  // Primary hero for agents: approved-03 or first available
  const hero = join(prodDir, "approved-03.png");
  const concept = join(prodDir, "concept.png");
  if (existsSync(hero)) copyFileSync(hero, concept);
}

console.log(`Promoted ${promoted} PNGs to production concept folders (+ concept.png from approved-03).`);
