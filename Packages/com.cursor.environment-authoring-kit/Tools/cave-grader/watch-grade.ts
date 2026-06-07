import { readFileSync, watch } from "node:fs";
import { join } from "node:path";
import { spawn } from "node:child_process";
import { loadCaveGraderEnv } from "./load-env.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? process.cwd()).replace(/\/$/, "");
const reportPath = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json");
let lastScore = 100;
let running = false;

function runGradeFix(): void {
  if (running) return;
  running = true;
  const toolsDir = join(
    hubRoot,
    "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
  );
  const child = spawn(
    process.execPath,
    ["--import", "tsx", "grade-and-fix.ts", "--auto", "--stream"],
    {
      cwd: toolsDir,
      stdio: "inherit",
      env: { ...process.env, HUB_ROOT: hubRoot },
    }
  );
  child.on("close", () => {
    running = false;
  });
}

console.log(`Watching ${reportPath} (re-invoke when overallScore drops)…`);
watch(reportPath, { persistent: true }, () => {
  try {
    const text = readFileSync(reportPath, "utf8");
    const json = JSON.parse(text) as { overallScore?: number; isDud?: boolean };
    const score = json.overallScore ?? 0;
    if (score < lastScore || json.isDud) {
      console.log(`Score dropped ${lastScore} → ${score}, invoking agent…`);
      lastScore = score;
      runGradeFix();
    } else {
      lastScore = score;
    }
  } catch {
    /* ignore parse races */
  }
});
