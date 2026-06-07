#!/usr/bin/env node
/** CC0 audio + lava texture for world FX (no login). */
import { createWriteStream, existsSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";
import { execSync } from "node:child_process";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const audioDir = join(root, "Assets/EnvironmentKit/CC0Imports/Audio");
const texDir = join(root, "Assets/EnvironmentKit/CC0Imports/Textures");
const zips = join(root, "Assets/EnvironmentKit/CC0Imports/_zips");

const files = [
  { url: "https://opengameart.org/sites/default/files/8BitBattleLoop.ogg", dest: "battle_loop.ogg" },
  { url: "https://opengameart.org/sites/default/files/adventuring_song.mp3", dest: "adventure_explore.mp3" },
  { url: "https://opengameart.org/sites/default/files/theme-loop.ogg", dest: "theme_loop.ogg" },
  { url: "https://opengameart.org/sites/default/files/lava_-_starninjas.png", dest: "lava_seamless.png" },
];
const zipPack = {
  url: "https://opengameart.org/sites/default/files/loops.zip",
  name: "cc0-music-loops.zip",
};

async function dl(url, dest) {
  const res = await fetch(url, { headers: { "User-Agent": "Mozilla/5.0" } });
  if (!res.ok) throw new Error(String(res.status));
  await pipeline(Readable.fromWeb(res.body), createWriteStream(dest));
}

await mkdir(audioDir, { recursive: true });
await mkdir(texDir, { recursive: true });
await mkdir(zips, { recursive: true });

for (const f of files) {
  const dest = f.dest.endsWith(".png") ? join(texDir, f.dest) : join(audioDir, f.dest);
  try {
    if (!existsSync(dest)) {
      console.log("GET", f.dest);
      await dl(f.url, dest);
    } else console.log("skip", f.dest);
  } catch (e) {
    console.warn("FAIL", f.dest, e.message);
  }
}

try {
  const zipPath = join(zips, zipPack.name);
  const unpack = join(audioDir, "loops-pack");
  if (!existsSync(unpack)) {
    if (!existsSync(zipPath)) {
      console.log("GET loops.zip");
      await dl(zipPack.url, zipPath);
    }
    execSync(`unzip -o -q "${zipPath}" -d "${unpack}"`);
    console.log("unpacked loops-pack");
  }
} catch (e) {
  console.warn("loops.zip", e.message);
}

console.log("Done → CC0Imports/Audio and Textures");
