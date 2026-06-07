#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Runs Tools/cave-grader research-situation-grader.ts — audits research for active style/phase and repairs cache.
    /// </summary>
    public static class CaveBuildResearchSituationGrader
    {
        public const string GradeReportRel = CaveBuildAgentContextExporter.Folder + "/ResearchSituationGrade.json";

        public static bool RunEnrichAllEntries()
        {
            return RunTsx("sync-research-cache", "Enrich research entries (rewrite all content.md)");
        }

        public static bool RunGradeAndRepairForActiveBuild(string phaseId = "surface_build")
        {
            var escaped = phaseId.Replace("\"", "\\\"");
            if (!RunTsx($"research-situation-grader.ts --phase={escaped} --repair", "Grade + repair research for build"))
                return false;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, GradeReportRel);
            if (!File.Exists(path))
                return true;

            var json = File.ReadAllText(path);
            CaveBuildEditorLog.LogSurface("[Research] Situation grade:\n" + json, forceUnityConsole: true);
            return !json.Contains("\"passed\": false");
        }

        public static void QueueBeforePhaseIfNeeded(WorldGenerationRequest request, string phaseId)
        {
            if (request == null || request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return;

            if (!EditorPrefs.GetBool("EnvironmentKit_AutoGradeResearch", true))
                return;

            CaveBuildActionPacing.ScheduleLight(
                () => RunGradeAndRepairForActiveBuild(phaseId),
                CaveBuildPipelineDomains.QueueLabel("research situation grade"));
        }

        static bool RunTsx(string scriptName, string dialogTitle)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var scriptPath = Path.Combine(toolsDir, scriptName);
            if (!File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog(dialogTitle, "Missing " + scriptPath, "OK");
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out var resolveMsg))
            {
                EditorUtility.DisplayDialog(dialogTitle, resolveMsg ?? "Node not found.", "OK");
                return false;
            }

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                EditorUtility.DisplayDialog(dialogTitle, "Missing tsx — run npm install in Tools/cave-grader.", "OK");
                return false;
            }

            var args = $"\"{tsxCli}\" \"{scriptPath}\"";
            if (scriptName == "research-situation-grader.ts")
                args += " --phase=surface_build --repair";

            var ok = CaveBuildTsxProcessRunner.Run(
                new CaveBuildTsxProcessRunner.Options
                {
                    HubRoot = hub,
                    ToolsDir = toolsDir,
                    NodePath = node,
                    Arguments = args,
                    WaitMs = 120_000,
                    SuccessLabel = dialogTitle,
                    LiveOperationLabel = dialogTitle,
                },
                out var message);

            if (!ok)
            {
                EditorUtility.DisplayDialog(dialogTitle, message ?? "Research script failed.", "OK");
                return false;
            }

            Debug.Log("[CaveBuild] " + message);
            return true;
        }
    }
}
#endif
