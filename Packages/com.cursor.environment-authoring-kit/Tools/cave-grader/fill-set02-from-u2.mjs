import { copyFileSync, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const src = process.env.U2_SRC ?? "/Users/jacob/.cursor/projects/empty-window/assets";
const hub = process.env.HUB_ROOT ?? "/Users/jacob/Hub";
const base = join(
  hub,
  "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/set-02-unity"
);
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

const skip = new Set(["u2-01-04", "u2-02-02", "u2-02-04", "u2-03-03", "u2-04-01"]);

for (const name of [
  "u2-00-03",
  "u2-01-02",
  "u2-02-03",
  "u2-03-05",
  "u2-05-03",
  "u2-05-05",
  "u2-06-01",
  "u2-06-02",
  "u2-06-03",
  "u2-06-04",
  "u2-06-05",
  "u2-07-01",
  "u2-07-02",
  "u2-07-03",
  "u2-07-04",
  "u2-07-05",
  "u2-08-01",
  "u2-08-02",
  "u2-08-03",
  "u2-08-04",
  "u2-08-05",
  "u2-09-01",
  "u2-09-02",
  "u2-09-03",
  "u2-09-04",
  "u2-09-05",
  "u2-10-01",
  "u2-10-02",
  "u2-10-03",
  "u2-10-04",
  "u2-10-05",
  "u2-05-04",
]) {
  if (skip.has(name)) continue;
  const m = name.match(/^u2-(\d+)-(\d+)$/);
  if (!m) continue;
  const folder = folders[Number(m[1])];
  const opt = m[2].padStart(2, "0");
  const from = join(src, `${name}.png`);
  const destDir = join(base, folder);
  const dest = join(destDir, `option-${opt}.png`);
  if (!existsSync(from)) continue;
  if (existsSync(dest)) continue;
  mkdirSync(destDir, { recursive: true });
  copyFileSync(from, dest);
  console.log("filled", dest);
}
