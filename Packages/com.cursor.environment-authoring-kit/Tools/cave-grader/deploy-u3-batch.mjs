import { copyFileSync, mkdirSync, renameSync, existsSync } from "node:fs";
import { join } from "node:path";

const hub = process.env.HUB_ROOT ?? "/Users/jacob/Hub";
const srcDir = process.env.U3_SRC ?? "/Users/jacob/.cursor/projects/empty-window/assets";
const base = join(
  hub,
  "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/set-02-unity"
);
const rejectRoot = join(
  hub,
  "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_do-not/user_rejected"
);

/** Combined gallery #9,12,14,18,21 → u2 files (user pick). */
const userRejectU2 = [
  "u2-01-04.png",
  "u2-02-02.png",
  "u2-02-04.png",
  "u2-03-03.png",
  "u2-04-01.png",
];

const u2ToSlot = {
  "u2-01-04.png": ["01_play_disk", "04"],
  "u2-02-02.png": ["02_foothill_ring_sculpt", "02"],
  "u2-02-04.png": ["02_foothill_ring_sculpt", "04"],
  "u2-03-03.png": ["03_south_foothill_labyrinth_row", "03"],
  "u2-04-01.png": ["04_peak_ring", "01"],
};

mkdirSync(rejectRoot, { recursive: true });
for (const f of userRejectU2) {
  const from = join(srcDir, f);
  if (!existsSync(from)) continue;
  const dest = join(rejectRoot, f);
  renameSync(from, dest);
  const [folder, opt] = u2ToSlot[f];
  const approved = join(
    hub,
    "Assets/EnvironmentKit/ResearchCache/images/concepts/phases",
    folder,
    `approved-${opt}.png`
  );
  if (existsSync(approved)) renameSync(approved, join(rejectRoot, `approved-${folder}-${opt}.png`));
}

for (const name of [
  "u3-00-01",
  "u3-00-02",
  "u3-00-04",
  "u3-00-05",
  "u3-01-01",
  "u3-01-03",
  "u3-01-05",
  "u3-02-01",
  "u3-02-04",
  "u3-02-05",
  "u3-03-01",
  "u3-03-02",
  "u3-03-03",
  "u3-03-04",
  "u3-04-02",
  "u3-04-03",
  "u3-04-04",
  "u3-04-05",
  "u3-05-01",
  "u3-05-02",
]) {
  const m = name.match(/^u3-(\d+)-(\d+)$/);
  if (!m) continue;
  const folders = [
    "00_fullworld_overview",
    "01_play_disk",
    "02_foothill_ring_sculpt",
    "03_south_foothill_labyrinth_row",
    "04_peak_ring",
    "05_wilderness_mountains",
  ];
  const folder = folders[Number(m[1])];
  const opt = m[2];
  const from = join(srcDir, `${name}.png`);
  const destDir = join(base, folder);
  const dest = join(destDir, `option-${opt}.png`);
  if (!existsSync(from)) {
    console.warn("skip missing", from);
    continue;
  }
  mkdirSync(destDir, { recursive: true });
  copyFileSync(from, dest);
  console.log("deploy", dest);
}
