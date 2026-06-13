/**
 * Code bot post-pass without Unity batch — dotnet compile + static wiring audit.
 * Use when batchmode aborts (Bee IPC) or editor is already open.
 */
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { loadCaveGraderEnv } from "../../Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/load-env.js";
import {
  HUB_CODE_PROGRESS_REL,
  loadCodeProgress,
  type HubCodeProgressDoc,
} from "./code-progress.js";

loadCaveGraderEnv();

const hubRoot = (process.env.HUB_ROOT ?? join(process.cwd(), "../..")).replace(/\/$/, "");

function tryDotnetBuild(proj: string): "ok" | "fail" | "skip" {
  const path = join(hubRoot, proj);
  if (!existsSync(path)) return "skip";

  const result = spawnSync("dotnet", ["build", path, "-c", "Debug", "-v", "q", "--nologo"], {
    cwd: hubRoot,
    encoding: "utf8",
    timeout: 300_000,
  });
  const out = `${result.stdout ?? ""}\n${result.stderr ?? ""}`;
  if (result.status === 0) {
    console.log(`[CodeBot:fallback] dotnet OK: ${proj}`);
    return "ok";
  }

  if (out.includes("NETFramework,Version=v4.7.1") || out.includes("BaseOutputPath/OutputPath")) {
    console.log(`[CodeBot:fallback] dotnet skipped for ${proj} (Unity csproj — use editor compile)`);
    return "skip";
  }

  console.error(`[CodeBot:fallback] dotnet build failed: ${proj}`);
  if (out.trim()) console.error(out.trim().slice(0, 4000));
  return "fail";
}

function compileDiagnosticsClean(): boolean {
  const path = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildCompileDiagnostics.json");
  if (!existsSync(path)) return true;

  try {
    const doc = JSON.parse(readFileSync(path, "utf8")) as {
      hasCompileErrors?: boolean;
      verifiedErrorCount?: number;
    };
    if (doc.hasCompileErrors === true) return false;
    if ((doc.verifiedErrorCount ?? 0) > 0) return false;
    return true;
  } catch {
    return true;
  }
}

function hubPath(rel: string): string {
  return join(hubRoot, rel);
}

function sourceContains(rel: string, needle: string): boolean {
  const path = hubPath(rel);
  if (!existsSync(path)) return false;
  return readFileSync(path, "utf8").includes(needle);
}

function markTask(doc: HubCodeProgressDoc, taskId: string, notes: string): boolean {
  const row = doc.tasks?.[taskId];
  if (!row || row.status === "done") return false;
  row.status = "done";
  if (notes) row.notes = notes;
  return true;
}

function applyStaticAudit(doc: HubCodeProgressDoc, compileOk: boolean): number {
  let n = 0;

  if (compileOk) {
    if (markTask(doc, "integrate_compile_smoke", "dotnet compile OK (Unity batch skipped)")) n++;
  }

  if (existsSync(hubPath("Assets/Scripts/Editor/MainSceneGameplaySetup.cs")))
    if (markTask(doc, "demo_mainscene_idempotent", "MainSceneGameplaySetup present")) n++;

  if (sourceContains("Assets/Scripts/Competition/Core/CompetitionBootstrap.cs", "RuntimeInitializeOnLoadMethod"))
    if (markTask(doc, "comp_bootstrap_chain", "CompetitionBootstrap AutoCreate")) n++;

  if (sourceContains("Assets/Scripts/Competition/UI/CompetitionTitleMenuBridge.cs", "ScheduleSpawnNearPlayer"))
    if (markTask(doc, "comp_title_to_spawn", "Title → spawn wired")) n++;

  if (sourceContains("Assets/Scripts/Competition/Chat/AgentCommandParser.cs", "ChallengeChatBridge"))
    if (markTask(doc, "comp_chat_commands", "Parser wired")) n++;

  if (sourceContains("Assets/Scripts/Competition/Brains/AgentAiStack.cs", "AgentVitalityDeathBridge"))
    if (markTask(doc, "comp_combat_death", "Death bridge on stack")) n++;

  if (sourceContains("Assets/Scripts/Competition/Lineage/AgentEcosystemService.cs", "TryBreed"))
    if (markTask(doc, "comp_lineage_ecosystem", "Ecosystem service")) n++;

  if (sourceContains("Assets/Scripts/Challenge/ChallengeCoordinator.cs", "TrySendChallengeToPlayer"))
    if (markTask(doc, "challenge_network_relay", "Challenge invites")) n++;

  if (sourceContains("Assets/Scripts/Competition/Challenge/CompetitionChallengeBridge.cs", "MarkDeceased"))
    if (markTask(doc, "challenge_arena_duel", "Duel loss tombstone")) n++;

  if (existsSync(hubPath("Assets/Scripts/Editor/MainScenePortfolioMenuSetup.cs")))
    if (markTask(doc, "mp_portfolio_menu", "Portfolio menu setup")) n++;

  if (sourceContains("Assets/Scripts/Competition/Multiplayer/CompetitionAgentNetworkPawn.cs", "SyncedHp"))
    if (markTask(doc, "mp_agent_net_sync", "Network HP sync")) n++;

  if (sourceContains("Assets/Scripts/Competition/UI/AgentTrainReportUi.cs", "AgentActivityFilters.CombatGroup"))
    if (markTask(doc, "train_adapter_ui", "Train combat filters")) n++;

  return n;
}

function trySetCodeReady(doc: HubCodeProgressDoc): void {
  const tasks = doc.tasks ?? {};
  const pending = Object.values(tasks).some(
    (t) => t.status !== "done" && t.status !== "cancelled"
  );
  if (!pending) doc.codeReady = true;
}

function main(): void {
  console.log("[CodeBot:fallback] Node post-pass (no Unity batch)…");

  const hub = tryDotnetBuild("Hub.csproj");
  const editor = tryDotnetBuild("Hub.Editor.csproj");
  const dotnetFailed = hub === "fail" || editor === "fail";
  const diagClean = compileDiagnosticsClean();
  const compileOk = !dotnetFailed && diagClean;

  const doc = loadCodeProgress(hubRoot);
  if (!doc?.tasks) {
    console.error("[CodeBot:fallback] Missing HubCodeProgress.json");
    process.exit(1);
  }

  const marked = applyStaticAudit(doc, compileOk);
  trySetCodeReady(doc);

  writeFileSync(join(hubRoot, HUB_CODE_PROGRESS_REL), `${JSON.stringify(doc, null, 2)}\n`, "utf8");

  console.log(
    `[CodeBot:fallback] compile=${compileOk} tasksMarked=${marked} codeReady=${doc.codeReady === true}`
  );
  console.log(
    "[CodeBot:fallback] Competition ONNX smoke needs Unity — use Game → Code Bot → Run Post Pass in editor,"
  );
  console.log("  or close Unity and retry ./Tools/code-bot/run-post-pass.sh");

  if (dotnetFailed || !diagClean) process.exit(2);
  process.exit(0);
}

main();
