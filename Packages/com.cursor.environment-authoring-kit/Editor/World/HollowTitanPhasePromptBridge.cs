#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Runs tsx to generate Hollow Titan research + per-phase AI prompts.</summary>
    public static class HollowTitanPhasePromptBridge
    {
        public const string ActivePhasePromptPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanActivePhasePrompt.md";

        public const string ResearchAgentPromptPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanResearchAgentPrompt.md";

        public const string ResearchAgentPromptJsonPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanResearchAgentPrompt.json";

        public const string ResearchActionPlanPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanResearchActionPlan.json";

        public const string PhasePromptManifestPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanPhasePromptManifest.json";

        public const string ResearchExecutionBriefJsonPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanResearchExecutionBrief.json";

        public const string ResearchExecutionBriefMdPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanResearchExecutionBrief.md";

        public static bool ExportPhasePromptManifest(out string message) =>
            CaveBuildPhasePromptBridge.ExportHollowTitanPhasePromptManifest(out message);

        public static bool ExportResearchAgentPrompt(int phaseIndex, out string message) =>
            RunTsx(
                "generate-hollow-titan-research-prompt.ts",
                $"--phase={phaseIndex}",
                60_000,
                out message,
                $"HOLLOW_TITAN_ACTIVE_PHASE={phaseIndex}");

        public static bool ExportActivePhasePrompt(int phaseIndex, int seed, out string message) =>
            RunTsx(
                "generate-hollow-titan-active-phase-prompt.ts",
                $"--phase={phaseIndex}",
                60_000,
                out message,
                $"HOLLOW_TITAN_ACTIVE_PHASE={phaseIndex}",
                $"HOLLOW_TITAN_BUILD_SEED={seed}");

        public static bool ExportResearchActionPlan(int phaseIndex, int seed, out string message) =>
            RunTsx(
                "generate-hollow-titan-research-action-plan.ts",
                $"--phase={phaseIndex}",
                60_000,
                out message,
                $"HOLLOW_TITAN_ACTIVE_PHASE={phaseIndex}",
                $"HOLLOW_TITAN_BUILD_SEED={seed}");

        public static bool ExportResearchExecutionBrief(int phaseIndex, out string message) =>
            RunTsx(
                "export-hollow-titan-research-brief.ts",
                $"--phase={phaseIndex}",
                60_000,
                out message,
                $"HOLLOW_TITAN_ACTIVE_PHASE={phaseIndex}");

        public static bool TryBeginRunTsx(
            string scriptName,
            string phaseArg,
            int waitMs,
            Action<bool, string> onComplete,
            params string[] extraEnvs)
        {
            if (onComplete == null)
                return false;

            if (!TryPrepareRunTsx(scriptName, phaseArg, extraEnvs, out var hub, out var toolsDir, out var node, out var args, out var message, out var skipped))
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
                (ok, resultMsg) => onComplete(ok, resultMsg));
            return true;
        }

        static bool RunTsx(
            string scriptName,
            string phaseArg,
            int waitMs,
            out string message,
            params string[] extraEnvs)
        {
            if (!TryPrepareRunTsx(scriptName, phaseArg, extraEnvs, out var hub, out var toolsDir, out var node, out var args, out message, out var skipped))
                return false;

            if (skipped)
                return true;

            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                message =
                    "RunTsx called during active build — use TryBeginRunTsx (sync WaitForExit freezes Unity).";
                return false;
            }

            return CaveBuildTsxProcessRunner.Run(
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
        }

        static bool TryPrepareRunTsx(
            string scriptName,
            string phaseArg,
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
            if (!string.IsNullOrEmpty(phaseArg))
                args += " " + phaseArg;
            return true;
        }
    }
}
#endif
