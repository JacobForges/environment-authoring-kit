#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Runs tsx to generate per-phase AI prompts from live JSON + research URLs.</summary>
    public static class CaveBuildPhasePromptBridge
    {
        public const string PhasePromptsIndexPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildPhasePromptsIndex.json";

        /// <summary>Single overwrite prompt for the active build phase (replaces CaveBuildPhasePrompt_*.md).</summary>
        public const string ActivePhasePromptPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildActivePhasePrompt.md";

        public const string PhaseDataDigestPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildPhaseDataDigest.md";

        public const string DoNotPromptPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildDoNotPrompt.md";
        public const string NextStepsPromptPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildNextStepsPrompt.md";
        public const string AutonomousIterationPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildAutonomousIteration.json";

        public const string ResearchAgentPromptPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchAgentPrompt.md";

        public static bool ExportResearchAgentPrompt(string phaseId, string rung, out string message) =>
            RunTsx(
                "generate-research-agent-prompt.ts",
                string.IsNullOrEmpty(rung) ? null : $"--rung={rung}",
                null,
                90_000,
                out message,
                string.IsNullOrEmpty(phaseId) ? "CAVE_ACTIVE_PHASE=research" : $"CAVE_ACTIVE_PHASE={phaseId}",
                string.IsNullOrEmpty(rung) ? null : $"CAVE_ACTIVE_RUNG={rung}");

        public static bool ExportPhasePrompt(
            string phaseId,
            string rung,
            int meatPass,
            int iteration,
            out string message,
            bool skipManifestRebuild = false)
        {
            message = null;
            var extraEnvs = new System.Collections.Generic.List<string>
            {
                string.IsNullOrEmpty(phaseId) ? null : $"CAVE_ACTIVE_PHASE={phaseId}",
                $"CAVE_AUTONOMOUS_ITERATION={iteration}",
            };
            if (skipManifestRebuild || CaveBuildPromptExportSession.IsManifestFresh())
                extraEnvs.Add("CAVE_SKIP_MANIFEST_REBUILD=1");

            return RunTsx(
                "generate-phase-prompts.ts",
                $"--rung={rung}",
                meatPass >= 0 ? $"--meat-pass={meatPass}" : null,
                90_000,
                out message,
                extraEnvs.ToArray());
        }

        public static bool ExportMeatPassPlan(int meatPass, out string message) =>
            RunTsx(
                "generate-meat-pass-plan.ts",
                null,
                meatPass >= 0 ? $"--meat-pass={meatPass}" : null,
                75_000,
                out message,
                $"CAVE_MEAT_PASS={meatPass}");

        public static bool ExportAutonomousBrief(int iteration, out string message) =>
            RunTsx(
                "generate-autonomous-brief.ts",
                null,
                null,
                60_000,
                out message,
                $"CAVE_AUTONOMOUS_ITERATION={iteration}");

        /// <summary>Research-gate only: writes active-phase prompt for research (not every pipeline phase).</summary>
        public static bool ExportAllPhasePrompts(int iteration, out string message)
        {
            ExportResearchAgentPrompt("research", "research", out _);
            return RunTsx(
                "generate-phase-prompts.ts",
                null,
                null,
                90_000,
                out message,
                "CAVE_ACTIVE_PHASE=research",
                $"CAVE_AUTONOMOUS_ITERATION={iteration}");
        }

        public static bool ExportUnifiedAgentContext(string phaseId, out string message)
        {
            var extraEnvs = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(phaseId))
                extraEnvs.Add($"CAVE_ACTIVE_PHASE={phaseId}");
            if (CaveBuildPromptExportSession.ShouldSkipManifestRebuild(0))
                extraEnvs.Add("CAVE_SKIP_MANIFEST_REBUILD=1");

            var ok = RunTsx(
                "generate-unified-agent-prompt.ts",
                null,
                null,
                120_000,
                out message,
                extraEnvs.ToArray());
            if (ok)
                CaveBuildPromptExportSession.MarkManifestFresh();
            return ok;
        }

        public static bool ExportResearchActionPlan(string phaseId, int queuedStep, int seed, out string message) =>
            RunTsx(
                "generate-research-action-plan.ts",
                null,
                null,
                90_000,
                out message,
                $"CAVE_ACTIVE_PHASE={phaseId}",
                $"CAVE_QUEUED_STEP={queuedStep}",
                $"CAVE_BUILD_SEED={seed}");

        public static bool ExportNextStepsAndDoNot(string phaseId, out string message) =>
            RunTsx(
                "generate-research-action-plan.ts",
                "--next-steps-only",
                null,
                45_000,
                out message,
                $"CAVE_ACTIVE_PHASE={phaseId}");

        /// <summary>True during validate/startup/queued pipeline — sync WaitForExit must not run on the main thread.</summary>
        public static bool RequiresNonBlockingTsx =>
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
            CaveBuildStartupCoordinator.IsActive ||
            CaveBuildActionPacing.IsBusy ||
            CaveBuildHelperScriptOrchestrator.IsChainActive ||
            CaveBuildTsxProcessRunner.IsRunning ||
            SurfaceTerrainQualityMeatLoop.IsRunning;

        /// <summary>Starts tsx on a later editor update tick — required during validate/startup (no sync WaitForExit on main thread).</summary>
        public static bool TryBeginRunTsx(
            string scriptName,
            string rungArg,
            string meatPassArg,
            int waitMs,
            Action<bool, string> onComplete,
            params string[] extraEnvs)
        {
            if (onComplete == null)
                return false;

            if (!TryPrepareRunTsx(
                    scriptName,
                    rungArg,
                    meatPassArg,
                    extraEnvs,
                    out var hub,
                    out var toolsDir,
                    out var node,
                    out var args,
                    out var message,
                    out var skipped))
            {
                onComplete(false, message);
                return false;
            }

            if (skipped)
            {
                onComplete(true, message);
                return false;
            }

            CaveBuildTsxProcessRunner.BeginRun(
                new CaveBuildTsxProcessRunner.Options
                {
                    HubRoot = hub,
                    ToolsDir = toolsDir,
                    NodePath = node,
                    Arguments = args,
                    WaitMs = waitMs,
                    SuccessLabel = scriptName,
                    LiveOperationLabel = scriptName,
                    ExtraEnvs = extraEnvs,
                },
                (ok, resultMsg) =>
                {
                    if (ok)
                        OnRunTsxSucceeded(scriptName, extraEnvs, ref resultMsg);
                    onComplete(ok, resultMsg);
                });
            return true;
        }

        static bool RunTsx(
            string scriptName,
            string rungArg,
            string meatPassArg,
            int waitMs,
            out string message,
            params string[] extraEnvs)
        {
            if (!TryPrepareRunTsx(
                    scriptName,
                    rungArg,
                    meatPassArg,
                    extraEnvs,
                    out var hub,
                    out var toolsDir,
                    out var node,
                    out var args,
                    out message,
                    out var skipped))
                return false;

            if (skipped)
                return true;

            if (RequiresNonBlockingTsx)
            {
                message =
                    "RunTsx called during active build — use TryBeginRunTsx (sync WaitForExit freezes Unity).";
                return false;
            }

            var ok = CaveBuildTsxProcessRunner.Run(
                new CaveBuildTsxProcessRunner.Options
                {
                    HubRoot = hub,
                    ToolsDir = toolsDir,
                    NodePath = node,
                    Arguments = args,
                    WaitMs = waitMs,
                    SuccessLabel = scriptName,
                    LiveOperationLabel = scriptName,
                    ExtraEnvs = extraEnvs,
                },
                out message);

            if (!ok)
                return false;

            OnRunTsxSucceeded(scriptName, extraEnvs, ref message);
            return true;
        }

        static bool TryPrepareRunTsx(
            string scriptName,
            string rungArg,
            string meatPassArg,
            string[] extraEnvs,
            out string hub,
            out string toolsDir,
            out string node,
            out string args,
            out string message,
            out bool skipped)
        {
            message = null;
            skipped = false;
            hub = CaveBuildCursorSettings.ResolveHubRoot();
            if (CaveBuildPromptExportSession.ShouldSkipPromptTsxDuringValidate(scriptName, hub, extraEnvs))
            {
                message =
                    $"Skipped {scriptName} during cave validate — prompt outputs already on disk (no blocking node/tsx).";
                CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                CaveBuildRunStatusPublisher.ClearSubOperation();
                toolsDir = null;
                node = null;
                args = null;
                skipped = true;
                return true;
            }

            toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, scriptName);
            if (!File.Exists(script))
            {
                message = $"Missing {scriptName}";
                node = null;
                args = null;
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out node, out message))
            {
                args = null;
                return false;
            }

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx";
                args = null;
                return false;
            }

            args = $"\"{tsxCli}\" \"{script}\"";
            if (!string.IsNullOrEmpty(rungArg))
                args += " " + rungArg;
            if (!string.IsNullOrEmpty(meatPassArg))
                args += " " + meatPassArg;
            return true;
        }

        static void OnRunTsxSucceeded(string scriptName, string[] extraEnvs, ref string message)
        {
            message = "OK";
            if (scriptName == "generate-phase-prompts.ts" ||
                scriptName == "generate-unified-agent-prompt.ts" ||
                scriptName == "generate-research-agent-prompt.ts")
            {
                var seed = 0;
                foreach (var extra in extraEnvs)
                {
                    if (extra != null && extra.StartsWith("CAVE_BUILD_SEED=", StringComparison.Ordinal))
                    {
                        int.TryParse(extra.Substring("CAVE_BUILD_SEED=".Length), out seed);
                        break;
                    }
                }

                if (seed != 0)
                    CaveBuildPromptExportSession.MarkFresh(seed);
            }

            if (scriptName == "generate-unified-agent-prompt.ts")
                CaveBuildPromptExportSession.MarkManifestFresh();
        }

        static void ApplyExtraEnv(ProcessStartInfo psi, string extraEnv)
        {
            if (string.IsNullOrEmpty(extraEnv) || !extraEnv.Contains("="))
                return;

            var eq = extraEnv.IndexOf('=');
            psi.EnvironmentVariables[extraEnv.Substring(0, eq)] = extraEnv.Substring(eq + 1);
        }
    }
}
#endif
