#!/usr/bin/env node
/** Downloads CC0 character + animation packs (no login). */
import { createWriteStream } from "node:fs";
import { mkdir } from "node:fs/promises";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";
import { execSync } from "node:child_process";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const outRoot = join(root, "Assets/EnvironmentKit/CC0Imports");

const packs = [
  {
    name: "kenney-animated-characters-2",
    url: "https://opengameart.org/sites/default/files/kenney_animated-characters-2.zip",
  },
  {
    name: "quaternius-ultimate-animated-characters",
    url: "https://opengameart.org/sites/default/files/ultimate_animated_character_pack_by_quaternius.zip",
  },
  {
    name: "quaternius-animated-zombie-pack",
    url: "https://opengameart.org/sites/default/files/animatedzombiepack.zip",
  },
  {
    name: "quaternius-animated-knight-pack",
    url: "https://opengameart.org/sites/default/files/animatedknightpack.zip",
  },
  {
    name: "quaternius-easy-enemy-pack",
    url: "https://opengameart.org/sites/default/files/easyenemypack.zip",
  },
];

async function download(url, dest) {
  console.log("GET", url);
  const res = await fetch(url, { redirect: "follow" });
  if (!res.ok) throw new Error(`${res.status} ${url}`);
  await pipeline(Readable.fromWeb(res.body), createWriteStream(dest));
  const size = execSync(`stat -f%z "${dest}"`).toString().trim();
  console.log("  ->", dest, `(${size} bytes)`);
  if (Number(size) < 5000) throw new Error("file too small — likely HTML error page");
}

await mkdir(outRoot, { recursive: true });
const zips = join(outRoot, "_zips");
await mkdir(zips, { recursive: true });

for (const pack of packs) {
  const zipPath = join(zips, `${pack.name}.zip`);
  try {
    await download(pack.url, zipPath);
    const dest = join(outRoot, pack.name);
    await mkdir(dest, { recursive: true });
    execSync(`unzip -o -q "${zipPath}" -d "${dest}"`, { stdio: "inherit" });
    console.log("  unpacked", pack.name);
  } catch (e) {
    console.warn("  SKIP", pack.name, e.message);
  }
}

console.log("Done. Run: node link-cc0-slots.mjs && node download-cc0-items.mjs && node link-cc0-items (if items script exists)");
console.log("In Unity: Window → Environment Kit → World → Import CC0 Characters & Items");
