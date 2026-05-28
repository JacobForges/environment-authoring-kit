#!/usr/bin/env npx tsx
/**
 * Pre-build package_tooling gate — verifies kit paths before Unity/Cursor agents run.
 * Plan step 4 — Microsoft EvoTest (ICLR 2026): episode-level tooling checks before agent work.
 * https://www.microsoft.com/en-us/research/publication/evotest-evolutionary-test-time-learning-for-self-improving-agentic-systems/
 */
import { existsSync } from "node:fs";
import { join } from "node:path";

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../../../..")).replace(
  /\/$/,
  ""
);
const pkg = "Packages/com.cursor.environment-authoring-kit";

const requiredPaths = [
  `${pkg}/package.json`,
  `${pkg}/Editor/EnvironmentAuthoringKit.Editor.asmdef`,
  `${pkg}/Runtime/EnvironmentAuthoringKit.Runtime.asmdef`,
  `${pkg}/Tools/cave-grader/package.json`,
  `${pkg}/Tools/cave-grader/grade-and-fix.ts`,
  `${pkg}/Tools/cave-grader/research-catalog.ts`,
  `${pkg}/Tools/cave-grader/research-catalog.seed.json`,
  `${pkg}/Tools/cave-grader/research-cache-sync.ts`,
  `${pkg}/Tools/cave-grader/florida-research-paths.ts`,
  `${pkg}/Tools/cave-grader/florida-lidar-hillshade.ts`,
  `${pkg}/Tools/cave-grader/research-sources.ts`,
  `${pkg}/docs/RESEARCH_DATA_ATTRIBUTION.md`,
  `${pkg}/Tools/cave-grader/pre-build-prompt-ladder.ts`,
  `${pkg}/Tools/cave-grader/prompt-ladder.ts`,
  `${pkg}/Tools/cave-grader/run-grade-and-fix.sh`,
  `${pkg}/Tools/cave-grader/node_modules/@cursor/sdk/package.json`,
];

const missing: string[] = [];
for (const rel of requiredPaths) {
  const abs = join(hubRoot, rel);
  if (!existsSync(abs)) missing.push(rel);
}

if (missing.length) {
  console.error("package_tooling FAIL — missing paths:");
  for (const p of missing) console.error(`  - ${p}`);
  console.error(
    "\nFix: cd Tools/cave-grader && npm install && HUB_ROOT=<hub> npm run sync-research-catalog"
  );
  process.exit(1);
}

console.log(`package_tooling OK (${requiredPaths.length} paths) hub=${hubRoot}`);
