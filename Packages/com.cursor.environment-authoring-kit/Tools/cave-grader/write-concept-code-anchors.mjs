import { writeFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { PHASE_CONCEPT_CODE_BY_FOLDER } from "./phase-concept-code-examples.ts";

const hub = process.env.HUB_ROOT ?? "/Users/jacob/Hub";
const base = join(
  hub,
  "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/set-02-unity"
);

for (const [folder, ex] of Object.entries(PHASE_CONCEPT_CODE_BY_FOLDER)) {
  const dir = join(base, folder);
  mkdirSync(dir, { recursive: true });
  const body = [
    `# Code anchor — ${folder}`,
    "",
    ex.caption,
    "",
    `Source: \`${hub}/${ex.sourceRel}\``,
    "",
    "```csharp",
    ex.snippet.trimEnd(),
    "```",
    "",
  ].join("\n");
  writeFileSync(join(dir, "CODE_ANCHOR.md"), body);
}

console.log(`wrote ${Object.keys(PHASE_CONCEPT_CODE_BY_FOLDER).length} CODE_ANCHOR.md under ${base}`);
