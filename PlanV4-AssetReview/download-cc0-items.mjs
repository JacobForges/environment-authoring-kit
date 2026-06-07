#!/usr/bin/env node
import { createWriteStream, existsSync } from "node:fs";
import { mkdir, readFile, copyFile, readdir } from "node:fs/promises";
import { join, dirname, extname } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";
import { execSync } from "node:child_process";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const outRoot = join(root, "Assets/EnvironmentKit/CC0Imports");
const zips = join(outRoot, "_zips");
const itemsDir = join(outRoot, "Items");
const manifest = JSON.parse(await readFile(join(here, "asset-manifest.json"), "utf8"));

const packs = [
  {
    name: "rpg-items-1",
    url: "https://opengameart.org/sites/default/files/low-poly%20RPG%20item%20collection.zip",
  },
  {
    name: "rpg-items-1-alt",
    url: "https://opengameart.org/sites/default/files/low-poly_RPG_item_collection.zip",
  },
  {
    name: "rpg-items-2",
    url: "https://opengameart.org/sites/default/files/low-poly%20RPG%20item%20collection%202.zip",
  },
  {
    name: "rpg-items-3",
    url: "https://opengameart.org/sites/default/files/low-poly%20RPG%20item%20collection%203.zip",
  },
];

async function download(url, dest) {
  const res = await fetch(url, { redirect: "follow", headers: { "User-Agent": "Mozilla/5.0" } });
  if (!res.ok) throw new Error(`${res.status}`);
  await pipeline(Readable.fromWeb(res.body), createWriteStream(dest));
}

await mkdir(zips, { recursive: true });
const allMeshes = [];
for (const p of packs) {
  const zipPath = join(zips, `${p.name}.zip`);
  const unpack = join(outRoot, p.name);
  try {
    if (!existsSync(unpack)) {
      console.log("GET", p.name);
      if (!existsSync(zipPath)) await download(p.url, zipPath);
      execSync(`unzip -o -q "${zipPath}" -d "${unpack}"`);
      console.log("  unpacked");
    } else {
      console.log("  reuse", p.name);
    }
  } catch (e) {
    console.warn("  SKIP", p.name, e.message);
    continue;
  }
  async function walk(dir) {
    for (const ent of await readdir(dir, { withFileTypes: true })) {
      const p = join(dir, ent.name);
      if (ent.isDirectory()) await walk(p);
      else {
        const ext = extname(ent.name).toLowerCase();
        if (ext === ".fbx" || ext === ".obj") allMeshes.push(p);
      }
    }
  }
  await walk(unpack);
}

if (allMeshes.length === 0) {
  console.error("No item meshes downloaded.");
  process.exit(1);
}

await mkdir(itemsDir, { recursive: true });
let i = 0;
for (const item of manifest.items) {
  const src = allMeshes[i % allMeshes.length];
  i++;
  const ext = extname(src);
  await copyFile(src, join(itemsDir, `${item.id}${ext}`));
}
console.log(`Linked ${manifest.items.length} items from ${allMeshes.length} meshes → Items/`);
