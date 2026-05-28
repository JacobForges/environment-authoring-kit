#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Runs Tools/cave-grader helper tsx scripts in pipeline order with paced cooldowns (never sync WaitForExit during builds).
    /// </summary>
    public static class CaveBuildHelperScriptOrchestrator
    {
        public enum Moment
        {
            BuildSessionStart,
            PreBuildGateComplete,
            SurfacePrePlacement,
            TerrainPhaseStart,
            TerrainMeatPassStart,
            TerrainLadderRung,
            TerrainPipelineComplete,
            CaveValidatePrompts,
            CavePhasePromptRefresh,
            CaveMeatPassStart,
            CavePostBuildResearch,
            AutonomousIteration,
            BuildComplete,
        }

        public struct Context
        {
            public WorldGenerationRequest Request;
            public string PhaseId;
            public string Rung;
            public int MeatPass;
            public int QueuedStep;
            public int TerrainPhaseIndex;
            public int AutonomousIteration;
            public bool AdditiveSurface;
        }

        readonly struct ScriptStep
        {
            public readonly string Script;
            public readonly string RungArg;
            public readonly string MeatPassArg;
            public readonly int WaitMs;
            public readonly string[] ExtraEnvs;

            public ScriptStep(
                string script,
                string rungArg = null,
                string meatPassArg = null,
                int waitMs = 90_000,
                params string[] extraEnvs)
            {
                Script = script;
                RungArg = rungArg;
                MeatPassArg = meatPassArg;
                WaitMs = waitMs;
                ExtraEnvs = extraEnvs ?? Array.Empty<string>();
            }
        }

        static bool _chainActive;

        public static bool IsChainActive => _chainActive;

        public static void Queue(Moment moment, Context ctx, Action<bool, string> onComplete = null)
        {
            var steps = BuildSteps(moment, ctx);
            if (steps.Count == 0)
            {
                onComplete?.Invoke(true, $"[{moment}] no helper scripts scheduled.");
                return;
            }

            CaveBuildEditorLog.LogCave(
                $"[Helpers] {moment} — queuing {steps.Count} script(s) with paced cooldowns.",
                forceUnityConsole: moment.ToString().StartsWith("Terrain", StringComparison.Ordinal) ||
                                   moment == Moment.SurfacePrePlacement ||
                                   moment == Moment.TerrainPipelineComplete
                    ? false
                    : true);

            _chainActive = true;
            QueueStepAt(0, steps, moment, onComplete);
        }

        /// <summary>Runs prompt refresh bundle for research gates (cave workflow).</summary>
        public static void QueueCavePhasePromptRefresh(
            Context ctx,
            Action<bool, string> onComplete,
            Action<bool> onAwaitingTsx = null)
        {
            var steps = BuildSteps(Moment.CavePhasePromptRefresh, ctx);
            if (steps.Count == 0)
            {
                onComplete?.Invoke(true, "No prompt scripts.");
                return;
            }

            _chainActive = true;
            QueueStepAt(
                0,
                steps,
                Moment.CavePhasePromptRefresh,
                (ok, msg) =>
                {
                    onComplete?.Invoke(ok, msg);
                },
                onAwaitingTsx);
        }

        static void QueueStepAt(
            int index,
            List<ScriptStep> steps,
            Moment moment,
            Action<bool, string> onComplete,
            Action<bool> onAwaitingTsx = null)
        {
            if (index >= steps.Count)
            {
                _chainActive = false;
                CaveBuildActionPacing.ApplyCooldownTimers(CaveBuildActionPacing.ActionWeight.Light);
                onComplete?.Invoke(true, $"[{moment}] helper scripts finished ({steps.Count} steps).");
                return;
            }

            var step = steps[index];
            var label = CaveBuildPipelineDomains.QueueLabel(
                $"helper {moment} {index + 1}/{steps.Count} — {step.Script}");

            CaveBuildActionPacing.ScheduleHeavyChain(
                () => RunOneStep(step, moment, (ok, msg, awaiting) =>
                {
                    if (awaiting)
                    {
                        onAwaitingTsx?.Invoke(true);
                        return;
                    }

                    if (!ok)
                    {
                        _chainActive = false;
                        CaveBuildEditorLog.LogCaveWarning($"[Helpers] {step.Script} failed: {msg}");
                        onComplete?.Invoke(false, msg);
                        return;
                    }

                    CaveBuildActionPacing.ApplyCooldownTimers(CaveBuildActionPacing.ActionWeight.Light);
                    QueueStepAt(index + 1, steps, moment, onComplete, onAwaitingTsx);
                }),
                label);
        }

        static void RunOneStep(
            ScriptStep step,
            Moment moment,
            Action<bool, string, bool> onDone)
        {
            if (TryRunResearchScriptAsync(step, onDone))
                return;

            var useAsync =
                CaveBuildPhasePromptBridge.RequiresNonBlockingTsx ||
                _chainActive ||
                CaveBuildTsxProcessRunner.IsRunning;

            if (useAsync)
            {
                CaveBuildPhasePromptBridge.TryBeginRunTsx(
                    step.Script,
                    step.RungArg,
                    step.MeatPassArg,
                    step.WaitMs,
                    (ok, msg) => onDone(ok, msg, false),
                    step.ExtraEnvs);
                return;
            }

            var syncOk = RunSync(step, out var syncMsg);
            onDone(syncOk, syncMsg, false);
        }

        static bool TryRunResearchScriptAsync(ScriptStep step, Action<bool, string, bool> onDone)
        {
            switch (step.Script)
            {
                case "research-cache-sync.ts":
                    if (CaveBuildResearchCacheBridge.ShouldSkipCacheTsxSync())
                    {
                        onDone(true, "Skipped research-cache-sync — cache on disk.", false);
                        return true;
                    }

                    return RunResearchExporterAsync(
                        step,
                        (ok, msg) => onDone(
                            ok || CaveBuildResearchCacheBridge.HasUsableLocalResearchCache(),
                            msg,
                            false));

                case "florida-lidar-hillshade.ts":
                    if (CaveBuildResearchCacheBridge.ShouldSkipHillshadeTsxSync())
                    {
                        onDone(true, "Skipped florida-lidar-hillshade — hillshades on disk.", false);
                        return true;
                    }

                    return RunResearchExporterAsync(step, (ok, msg) => onDone(ok, msg, false));

                case "export-research-catalog.ts":
                    if (CaveBuildResearchCacheBridge.ShouldSkipCatalogTsxSync())
                    {
                        onDone(true, "Skipped export-research-catalog — catalog on disk.", false);
                        return true;
                    }

                    return RunResearchExporterAsync(step, (ok, msg) => onDone(ok, msg, false));

                case "export-research-execution-brief.ts":
                    if (CaveBuildResearchCacheBridge.ShouldSkipBriefTsxSync())
                    {
                        onDone(true, "Skipped export-research-execution-brief — brief on disk.", false);
                        return true;
                    }

                    return RunResearchExporterAsync(step, (ok, msg) => onDone(ok, msg, false));

                default:
                    return false;
            }
        }

        static bool RunResearchExporterAsync(ScriptStep step, Action<bool, string> onComplete)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = System.IO.Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out var resolveMsg))
            {
                onComplete(false, resolveMsg);
                return true;
            }

            var tsxCli = System.IO.Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            var script = System.IO.Path.Combine(toolsDir, step.Script);
            if (!System.IO.File.Exists(script))
            {
                onComplete(false, $"Missing {step.Script}");
                return true;
            }

            var args = $"\"{tsxCli}\" \"{script}\"";
            if (!string.IsNullOrEmpty(step.RungArg))
                args += " " + step.RungArg;
            if (!string.IsNullOrEmpty(step.MeatPassArg))
                args += " " + step.MeatPassArg;

            if (!CaveBuildResearchCacheBridge.TryBeginRunTsxProcess(
                    hub,
                    toolsDir,
                    node,
                    args,
                    step.WaitMs,
                    step.Script,
                    onComplete))
            {
                onComplete(false, $"Could not start {step.Script}");
            }

            return true;
        }

        public static Context MakeContext(WorldGenerationRequest request) =>
            new() { Request = request };

        static bool RunSync(ScriptStep step, out string message)
        {
            return step.Script switch
            {
                "generate-unified-agent-prompt.ts" =>
                    CaveBuildPhasePromptBridge.ExportUnifiedAgentContext(
                        EnvPhase(step.ExtraEnvs), out message),
                "generate-research-agent-prompt.ts" =>
                    CaveBuildPhasePromptBridge.ExportResearchAgentPrompt(
                        EnvPhase(step.ExtraEnvs),
                        EnvRung(step.ExtraEnvs),
                        out message),
                "generate-research-action-plan.ts" =>
                    CaveBuildPhasePromptBridge.ExportResearchActionPlan(
                        EnvPhase(step.ExtraEnvs),
                        EnvInt(step.ExtraEnvs, "CAVE_QUEUED_STEP", -1),
                        EnvInt(step.ExtraEnvs, "CAVE_BUILD_SEED", 0),
                        out message),
                "generate-phase-prompts.ts" =>
                    CaveBuildPhasePromptBridge.ExportPhasePrompt(
                        EnvPhase(step.ExtraEnvs),
                        EnvRung(step.ExtraEnvs),
                        EnvInt(step.ExtraEnvs, "CAVE_MEAT_PASS", -1),
                        EnvInt(step.ExtraEnvs, "CAVE_AUTONOMOUS_ITERATION", 0),
                        out message),
                "generate-meat-pass-plan.ts" =>
                    CaveBuildPhasePromptBridge.ExportMeatPassPlan(
                        EnvInt(step.ExtraEnvs, "CAVE_MEAT_PASS", 0),
                        out message),
                "generate-autonomous-brief.ts" =>
                    CaveBuildPhasePromptBridge.ExportAutonomousBrief(
                        EnvInt(step.ExtraEnvs, "CAVE_AUTONOMOUS_ITERATION", 0),
                        out message),
                "verify-package-tooling.ts" => RunVerifyPackageTooling(out message),
                "export-terrain-rung-prompt.ts" => RunExportTerrainRung(
                    EnvRung(step.ExtraEnvs), out message),
                "export-rung-prompt.ts" => RunExportCaveRung(
                    EnvRung(step.ExtraEnvs), out message),
                "research-cache-sync.ts" =>
                    CaveBuildResearchCacheBridge.SyncCache(EnvRung(step.ExtraEnvs) ?? "research", true, out message),
                "florida-lidar-hillshade.ts" =>
                    CaveBuildResearchCacheBridge.SyncFloridaHillshades(out message),
                "export-research-catalog.ts" =>
                    CaveBuildResearchCacheBridge.SyncResearchCatalog(out message),
                "export-research-execution-brief.ts" =>
                    CaveBuildResearchCacheBridge.SyncResearchExecutionBrief(
                        EnvRung(step.ExtraEnvs) ?? "research",
                        EnvInt(step.ExtraEnvs, "CAVE_MEAT_PASS", -1),
                        out message),
                _ => UnknownScript(step, out message),
            };
        }

        static bool UnknownScript(ScriptStep step, out string message)
        {
            message = $"Unknown helper script {step.Script}";
            return false;
        }

        static string EnvPhase(string[] envs)
        {
            foreach (var e in envs)
            {
                if (e != null && e.StartsWith("CAVE_ACTIVE_PHASE=", StringComparison.Ordinal))
                    return e.Substring("CAVE_ACTIVE_PHASE=".Length);
            }

            return "research";
        }

        static string EnvRung(string[] envs)
        {
            foreach (var e in envs)
            {
                if (e != null && e.StartsWith("CAVE_ACTIVE_RUNG=", StringComparison.Ordinal))
                    return e.Substring("CAVE_ACTIVE_RUNG=".Length);
            }

            return null;
        }

        static int EnvInt(string[] envs, string key, int fallback)
        {
            var prefix = key + "=";
            foreach (var e in envs)
            {
                if (e != null && e.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(e.Substring(prefix.Length), out var v))
                    return v;
            }

            return fallback;
        }

        static bool RunVerifyPackageTooling(out string message)
        {
            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                message = "verify-package-tooling deferred — run during pre-build when editor idle.";
                return true;
            }

            return CaveBuildResearchCacheBridge.TryRunVerifyPackageTooling(out message);
        }

        static bool RunExportTerrainRung(string rung, out string message)
        {
            if (string.IsNullOrEmpty(rung))
            {
                message = "No terrain rung.";
                return true;
            }

            return TerrainBuildRungPromptExporter.TryExportRungAsync(rung, out message);
        }

        static bool RunExportCaveRung(string rung, out string message)
        {
            if (string.IsNullOrEmpty(rung))
            {
                message = "No cave rung.";
                return true;
            }

            return CaveBuildRungPromptExporter.TryExportRungPromptForHelper(rung, out message);
        }

        static List<ScriptStep> BuildSteps(Moment moment, Context ctx)
        {
            var steps = new List<ScriptStep>();
            var seed = ctx.Request?.Seed ?? 0;
            var phase = string.IsNullOrEmpty(ctx.PhaseId) ? "research" : ctx.PhaseId;
            var rung = ctx.Rung ?? "research";
            var meatPass = ctx.MeatPass;
            var workflowTerrain = moment is Moment.TerrainPhaseStart or Moment.TerrainMeatPassStart
                or Moment.TerrainLadderRung or Moment.TerrainPipelineComplete
                or Moment.SurfacePrePlacement;

            string[] BaseEnvs()
            {
                var list = new List<string>
                {
                    $"CAVE_BUILD_SEED={seed}",
                    $"CAVE_ACTIVE_PHASE={phase}",
                    $"CAVE_ACTIVE_RUNG={rung}",
                    $"CAVE_QUEUED_STEP={ctx.QueuedStep}",
                    $"CAVE_MEAT_PASS={meatPass}",
                    $"CAVE_AUTONOMOUS_ITERATION={ctx.AutonomousIteration}",
                    workflowTerrain ? "CAVE_WORKFLOW=terrain" : "CAVE_WORKFLOW=cave",
                };
                if (ctx.AdditiveSurface)
                    list.Add("CAVE_SURFACE_ADDITIVE=1");
                list.Add("CAVE_HARDCODED_PROMPTS=1");
                list.Add("CAVE_USE_EXPORTED_PROMPT=1");
                if (moment is Moment.CaveMeatPassStart or Moment.TerrainMeatPassStart
                    or Moment.TerrainLadderRung or Moment.AutonomousIteration)
                    list.Add("CAVE_FORCE_PROMPT_EXPORT=1");
                return list.ToArray();
            }

            switch (moment)
            {
                case Moment.BuildSessionStart:
                    if (!CaveBuildProjectSetup.IsTsxInstalled())
                        steps.Add(new ScriptStep("verify-package-tooling.ts", waitMs: 30_000, extraEnvs: BaseEnvs()));
                    break;

                case Moment.PreBuildGateComplete:
                    steps.Add(new ScriptStep(
                        "export-rung-prompt.ts",
                        "--rung=compile_gate",
                        null,
                        60_000,
                        BaseEnvs()));
                    steps.Add(new ScriptStep(
                        "generate-research-action-plan.ts",
                        null,
                        null,
                        60_000,
                        BaseEnvs()));
                    break;

                case Moment.TerrainPhaseStart:
                    if (ctx.TerrainPhaseIndex >= 0 &&
                        ctx.TerrainPhaseIndex < SurfaceTerrainAiPhases.PhaseIds.Length)
                    {
                        phase = SurfaceTerrainAiPhases.PhaseIds[ctx.TerrainPhaseIndex];
                        rung = "terrain_integration";
                    }

                    steps.Add(new ScriptStep(
                        "export-terrain-rung-prompt.ts",
                        $"--rung={rung}",
                        null,
                        75_000,
                        WithPhase(BaseEnvs(), phase, rung)));
                    break;

                case Moment.TerrainMeatPassStart:
                {
                    var meatRung = string.IsNullOrEmpty(ctx.Rung) ? "heightfield_no_craters" : ctx.Rung;
                    var meatPhase = "terrain_meat_loop";
                    steps.Add(new ScriptStep(
                        "generate-meat-pass-plan.ts",
                        null,
                        meatPass >= 0 ? $"--meat-pass={meatPass}" : null,
                        75_000,
                        WithPhase(BaseEnvs(), meatPhase, meatRung)));
                    steps.Add(new ScriptStep(
                        "generate-research-agent-prompt.ts",
                        $"--phase=terrain_fix --rung={meatRung}",
                        null,
                        90_000,
                        WithPhase(BaseEnvs(), "terrain_fix", meatRung)));
                    steps.Add(new ScriptStep(
                        "export-terrain-rung-prompt.ts",
                        $"--rung={meatRung}",
                        null,
                        90_000,
                        WithPhase(BaseEnvs(), meatPhase, meatRung)));
                    steps.Add(new ScriptStep(
                        "generate-phase-prompts.ts",
                        $"--rung={meatRung}",
                        meatPass >= 0 ? $"--meat-pass={meatPass}" : null,
                        90_000,
                        WithPhase(BaseEnvs(), meatPhase, meatRung)));
                    break;
                }

                case Moment.SurfacePrePlacement:
                    steps.Add(new ScriptStep(
                        "research-cache-sync.ts",
                        "--rung=terrain_integration",
                        null,
                        120_000,
                        WithPhase(BaseEnvs(), "research", "terrain_integration")));
                    steps.Add(new ScriptStep(
                        "florida-lidar-hillshade.ts",
                        null,
                        null,
                        120_000,
                        WithPhase(BaseEnvs(), "research", "terrain_integration")));
                    steps.Add(new ScriptStep(
                        "export-research-catalog.ts",
                        null,
                        null,
                        60_000,
                        WithPhase(BaseEnvs(), "research", "terrain_integration")));
                    steps.Add(new ScriptStep(
                        "export-research-execution-brief.ts",
                        null,
                        null,
                        60_000,
                        WithPhase(BaseEnvs(), "research", "terrain_integration")));
                    AddPromptBundle(
                        steps,
                        WithPhase(BaseEnvs(), "research", "terrain_integration"),
                        includeMeatPlan: false);
                    break;

                case Moment.TerrainLadderRung:
                    if (!string.IsNullOrEmpty(ctx.Rung))
                    {
                        steps.Add(new ScriptStep(
                            "generate-research-agent-prompt.ts",
                            $"--phase=terrain_fix --rung={ctx.Rung}",
                            null,
                            75_000,
                            WithPhase(BaseEnvs(), "terrain_fix", ctx.Rung)));
                        steps.Add(new ScriptStep(
                            "export-terrain-rung-prompt.ts",
                            $"--rung={ctx.Rung}",
                            null,
                            90_000,
                            WithPhase(BaseEnvs(), $"terrain_ladder_{ctx.Rung}", ctx.Rung)));
                    }

                    break;

                case Moment.TerrainPipelineComplete:
                    steps.Add(new ScriptStep(
                        "export-research-execution-brief.ts",
                        null,
                        null,
                        60_000,
                        WithPhase(BaseEnvs(), "surface_playable_world_gate", "terrain_integration")));
                    steps.Add(new ScriptStep(
                        "generate-phase-prompts.ts",
                        "--rung=terrain_integration",
                        null,
                        90_000,
                        WithPhase(BaseEnvs(), "surface_playable_world_gate", "terrain_integration")));
                    break;

                case Moment.CaveValidatePrompts:
                    AddPromptBundle(steps, WithPhase(BaseEnvs(), "research", "research"), includeMeatPlan: false);
                    break;

                case Moment.CavePhasePromptRefresh:
                    AddPromptBundle(steps, WithPhase(BaseEnvs(), phase, rung), includeMeatPlan: false);
                    break;

                case Moment.CaveMeatPassStart:
                    steps.Add(new ScriptStep(
                        "generate-meat-pass-plan.ts",
                        null,
                        meatPass >= 0 ? $"--meat-pass={meatPass}" : null,
                        75_000,
                        WithPhase(BaseEnvs(), CaveBuildPhaseResearchGate.PhaseForMeatPass(meatPass), rung)));
                    steps.Add(new ScriptStep(
                        "export-rung-prompt.ts",
                        string.IsNullOrEmpty(rung) ? null : $"--rung={rung}",
                        null,
                        60_000,
                        WithPhase(BaseEnvs(), CaveBuildPhaseResearchGate.PhaseForMeatPass(meatPass), rung)));
                    steps.Add(new ScriptStep(
                        "generate-phase-prompts.ts",
                        string.IsNullOrEmpty(rung) ? null : $"--rung={rung}",
                        meatPass >= 0 ? $"--meat-pass={meatPass}" : null,
                        90_000,
                        WithPhase(BaseEnvs(), CaveBuildPhaseResearchGate.PhaseForMeatPass(meatPass), rung)));
                    break;

                case Moment.CavePostBuildResearch:
                    steps.Add(new ScriptStep("export-research-catalog.ts", waitMs: 90_000, extraEnvs: BaseEnvs()));
                    steps.Add(new ScriptStep("export-research-execution-brief.ts", waitMs: 60_000, extraEnvs: BaseEnvs()));
                    break;

                case Moment.AutonomousIteration:
                    steps.Add(new ScriptStep(
                        "generate-autonomous-brief.ts",
                        null,
                        null,
                        60_000,
                        WithPhase(BaseEnvs(), "meat_loop_additive", rung)));
                    AddPromptBundle(steps, WithPhase(BaseEnvs(), "meat_loop_additive", rung), includeMeatPlan: false);
                    break;

                case Moment.BuildComplete:
                    steps.Add(new ScriptStep("export-research-execution-brief.ts", waitMs: 60_000, extraEnvs: BaseEnvs()));
                    steps.Add(new ScriptStep(
                        "generate-unified-agent-prompt.ts",
                        null,
                        null,
                        120_000,
                        WithPhase(BaseEnvs(), "packaging_ship", "packaging_readiness")));
                    LogWatchCliHints();
                    break;
            }

            return steps;
        }

        static void AddPromptBundle(List<ScriptStep> steps, string[] envs, bool includeMeatPlan)
        {
            steps.Add(new ScriptStep("generate-unified-agent-prompt.ts", extraEnvs: envs));
            steps.Add(new ScriptStep("generate-research-agent-prompt.ts", extraEnvs: envs));
            steps.Add(new ScriptStep("generate-research-action-plan.ts", extraEnvs: envs));
            steps.Add(new ScriptStep("generate-phase-prompts.ts", extraEnvs: envs));
            if (includeMeatPlan)
            {
                steps.Add(new ScriptStep(
                    "generate-meat-pass-plan.ts",
                    null,
                    $"--meat-pass={EnvInt(envs, "CAVE_MEAT_PASS", 0)}",
                    extraEnvs: envs));
            }
        }

        static string[] WithPhase(string[] baseEnvs, string phaseId, string rung)
        {
            var list = new List<string>(baseEnvs);
            list.RemoveAll(e =>
                e != null &&
                (e.StartsWith("CAVE_ACTIVE_PHASE=", StringComparison.Ordinal) ||
                 e.StartsWith("CAVE_ACTIVE_RUNG=", StringComparison.Ordinal)));
            list.Add($"CAVE_ACTIVE_PHASE={phaseId}");
            list.Add($"CAVE_ACTIVE_RUNG={rung}");
            return list.ToArray();
        }

        static void LogWatchCliHints()
        {
            if (!CaveBuildCursorSettings.LoadOrCreate().suggestTerrainGradeWatcher)
                return;

            CaveBuildEditorLog.LogCave(
                "[Helpers] Optional CLI watchers (outside Unity): " +
                "cd Tools/cave-grader && npm run watch-grade | npm run watch-terrain-grade",
                forceUnityConsole: true);
        }
    }
}
#endif
