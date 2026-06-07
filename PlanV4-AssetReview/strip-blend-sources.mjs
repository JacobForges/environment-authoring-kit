#!/usr/bin/env node
/** Remove .blend sources from CC0Imports — Unity needs FBX/OBJ only (no Blender install). */
import { readdir, unlink, rm } from "node:fs/promises";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const cc0 = join(root, "Assets/EnvironmentKit/CC0Imports");

async function walk(dir) {
  let removed = 0;
  for (const ent of await readdir(dir, { withFileTypes: true })) {
    const p = join(dir, ent.name);
    if (ent.isDirectory()) {
      if (ent.name === "Blends") {
        await rm(p, { recursive: true, force: true });
        console.log("removed folder", p);
        removed++;
        continue;
      }
      removed += await walk(p);
      continue;
    }
    if (ent.name.endsWith(".blend") || ent.name.endsWith(".blend.meta")) {
      await unlink(p);
      removed++;
    }
  }
  return removed;
}

const n = await walk(cc0);
console.log(`Stripped ${n} blender source file(s) under CC0Imports.`);
