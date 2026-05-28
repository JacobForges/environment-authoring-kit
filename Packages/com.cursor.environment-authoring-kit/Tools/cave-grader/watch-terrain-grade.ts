import { existsSync, mkdirSync, readFileSync, watch, type FSWatcher } from "node:fs";
import { dirname, join } from "node:path";
import { spawn } from "node:child_process";
import { loadCaveGraderEnv } from "./load-env.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? process.cwd()).replace(/\/$/, "");
const reportPath = join(
  hubRoot,
  "Assets/EnvironmentKit/Generated/SurfaceTerrainQualityReport.json"
);
const generatedDir = dirname(reportPath);
const reportName = "SurfaceTerrainQualityReport.json";
const toolsDir = join(
  hubRoot,
  "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
);
const nodeBin = process.execPath;

let lastScore = 100;
let running = false;
let watcher: FSWatcher | null = null;

function runPromptExport(): void {
  spawn(
    nodeBin,
    ["--import", "tsx", "generate-research-agent-prompt.ts", "--phase=terrain_fix"],
    { cwd: toolsDir, stdio: "inherit", env: { ...process.env, HUB_ROOT: hubRoot } }
  );
  spawn(
    nodeBin,
    ["--import", "tsx", "export-terrain-rung-prompt.ts"],
    { cwd: toolsDir, stdio: "inherit", env: { ...process.env, HUB_ROOT: hubRoot, CAVE_WORKFLOW: "terrain" } }
  );
}

function runGradeFix(): void {
  if (running) return;
  running = true;
  const child = spawn(
    nodeBin,
    ["--import", "tsx", "grade-and-fix.ts", "--auto", "--stream", "--workflow=terrain"],
    {
      cwd: toolsDir,
      stdio: "inherit",
      env: { ...process.env, HUB_ROOT: hubRoot, CAVE_WORKFLOW: "terrain" },
    }
  );
  child.on("close", (code) => {
    running = false;
    if (code !== 0) {
      console.log(
        "[watch-terrain-grade] Agent pass failed — refreshed TerrainBuildTailoredAgentPrompt.md on disk."
      );
      runPromptExport();
    }
  });
}

function onReportChanged(): void {
  if (!existsSync(reportPath)) return;
  try {
    const text = readFileSync(reportPath, "utf8");
    const json = JSON.parse(text) as { overallScore?: number; buildAcceptable?: boolean };
    const score = json.overallScore ?? 0;
    if (score < lastScore || !json.buildAcceptable) {
      console.log(`Terrain score ${lastScore} → ${score}, invoking agent…`);
      lastScore = score;
      runGradeFix();
    } else {
      lastScore = score;
    }
  } catch {
    /* parse races */
  }
}

function attachWatcher(): void {
  watcher?.close();
  watcher = null;

  if (existsSync(reportPath)) {
    console.log(`Watching ${reportPath} (current score ${lastScore})…`);
    watcher = watch(reportPath, { persistent: true }, () => onReportChanged());
  } else {
    console.log(
      `Report not found yet.\n` +
        `  → Unity: Window → Environment Kit → Terrain Build Grader → Re-grade\n` +
        `Watching ${generatedDir}/ for ${reportName}…`
    );
    watcher = watch(generatedDir, { persistent: true }, (_event, filename) => {
      if (filename && filename !== reportName) return;
      if (!existsSync(reportPath)) return;
      onReportChanged();
      attachWatcher();
    });
  }

  watcher?.on("error", (err) => {
    console.error("[watch-terrain-grade] watcher error:", err.message);
    setTimeout(attachWatcher, 3000);
  });
}

mkdirSync(generatedDir, { recursive: true });

if (existsSync(reportPath)) {
  try {
    const json = JSON.parse(readFileSync(reportPath, "utf8")) as { overallScore?: number };
    lastScore = json.overallScore ?? 100;
  } catch {
    lastScore = 100;
  }
} else {
  lastScore = 0;
}

attachWatcher();

// If report is failing now, run one fix pass immediately (don't wait for Unity to rewrite JSON).
if (existsSync(reportPath)) {
  try {
    const json = JSON.parse(readFileSync(reportPath, "utf8")) as {
      overallScore?: number;
      buildAcceptable?: boolean;
    };
    if (!json.buildAcceptable) {
      console.log("Terrain below target — starting grade-and-fix now…");
      runGradeFix();
    }
  } catch {
    /* ignore */
  }
}
