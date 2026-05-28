#!/usr/bin/env npx tsx
/** Regenerates research-catalog.seed.json (Unity reads this for full CaveBuildResearch.json). */
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { buildCatalogSeedJson } from "./research-catalog.js";
import { resolveHubRoot } from "./hub-root.js";

const dir = dirname(fileURLToPath(import.meta.url));
const hubRoot = resolveHubRoot();
const out = join(dir, "research-catalog.seed.json");
writeFileSync(out, buildCatalogSeedJson(hubRoot), "utf8");
console.log(`Wrote ${out}`);
