#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Clears ephemeral JSON/IPC/status files on each Build click so prior runs never confuse agents or the wizard.
    /// Heavy paths resolve through Generated/ResearchCache symlinks and Library/EnvironmentKit data root (Lexar when mounted).
    /// </summary>
    public static class CaveBuildSessionScratchReset
    {
        public const string RunSessionStampRel =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildRunSessionStamp.json";

        const double MinSecondsBetweenClears = 3.0;

        static double _lastClearAt;

        static readonly string[] EphemeralGeneratedRelPaths =
        {
            CaveBuildQualityReport.DefaultExportPath,
            CaveBuildPhaseContractRegistry.CompletionRel,
            "Assets/EnvironmentKit/Generated/CaveBuildBatchLog.json",
            "Assets/EnvironmentKit/Generated/CaveBuildPreflightReport.md",
            CaveBuildQualitySystem.ManifestPath,
            "Assets/EnvironmentKit/Generated/CaveBuildCompletionReadout.md",
            "Assets/EnvironmentKit/Generated/CaveBuildDoNotPrompt.md",
            "Assets/EnvironmentKit/Generated/CaveBuildNextStepsPrompt.md",
            "Assets/EnvironmentKit/Generated/CaveBuildWizardState.json",
            CaveBuildPlaytestSessionSnapshot.RelPath,
            "Assets/EnvironmentKit/Generated/CaveBuildPreBuildLadderReport.json",
            CaveRouteProbeRunner.ReportPath,
            SurfaceRouteProbeRunner.ReportPath,
            "Assets/EnvironmentKit/Generated/CaveBuildCompileDiagnostics.json",
            PlayableWorldGate.StatusRel,
            "Assets/EnvironmentKit/Generated/CaveBuildPlannerFidelityReport.json",
            "Assets/EnvironmentKit/Generated/CaveBuildGradingManifest.json",
            "Assets/EnvironmentKit/Generated/CaveBuildWorkflowContext.json",
            "Assets/EnvironmentKit/Generated/CaveBuildPreBuildWorkflowContext.json",
            "Assets/EnvironmentKit/Generated/CaveBuildAgentMemory.json",
            "Assets/EnvironmentKit/Generated/CaveBuildPreBuildReport.json",
            CaveBuildRunStatusPublisher.LiveStatusRel,
            CaveBuildWizardGate.KitCatalogExportRequestRel,
            CaveBuildWizardGate.KitCatalogExportDoneRel,
            CaveBuildWizardGate.ConceptCardThumbRequestRel,
            CaveBuildWizardGate.ConceptCardThumbDoneRel,
            CaveBuildWizardGate.ConceptCardMeshRequestRel,
            CaveBuildWizardGate.ConceptCardMeshDoneRel,
            CaveBuildWizardGate.ConceptCardSculptRequestRel,
            CaveBuildWizardGate.ConceptCardSculptDoneRel,
            CaveBuildPlannerGeneratedProps.RequestRel,
            CaveBuildPlannerGeneratedProps.DoneRel,
            "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.log",
            "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.log",
            "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.log",
        };

        public static void ClearAll(string reason, string sceneName = null)
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastClearAt < MinSecondsBetweenClears)
                return;

            _lastClearAt = now;
            CaveBuildAgentArtifacts.ResetStaleFilesFlagForBuildClick();

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var cleared = new List<string>();

            CaveBuildRunStatusPublisher.ResetForNewBuild();
            CaveBuildAgentArtifacts.ClearStaleFilesForBuildClick(hub, cleared);
            DeleteRelPaths(hub, EphemeralGeneratedRelPaths, cleared);
            DeletePlannerIpcGlob(hub, cleared);
            DeleteLibraryEphemeral(cleared);
            ClearPlannerSessionVolatile(hub);
            WriteRunSessionStamp(hub, reason, sceneName, cleared);

            var storage = EnvironmentKitExternalStorageMigrate.DescribeProjectHeavyStorage();
            var onExternal = EnvironmentKitDataRoot.IsUsingExternalVolume();
            if (!onExternal)
            {
                Debug.LogWarning(
                    "[EnvironmentKit] Session scratch cleared on Mac disk — mount Lexar and run " +
                    "Environment Kit → Storage → Move heavy data to external drive to protect the internal SSD.");
            }

            CaveBuildEditorLog.LogSurface(
                $"[Build] Session scratch cleared ({cleared.Count} ephemeral file(s), {reason}). Storage: {storage}",
                forceUnityConsole: true);
        }

        static void DeleteRelPaths(string hub, IEnumerable<string> relPaths, List<string> cleared)
        {
            foreach (var rel in relPaths)
            {
                if (string.IsNullOrWhiteSpace(rel))
                    continue;
                TryDelete(Path.Combine(hub, rel), rel, cleared);
                TryDelete(Path.Combine(hub, rel + ".meta"), rel + ".meta", cleared);
            }
        }

        static void DeletePlannerIpcGlob(string hub, List<string> cleared)
        {
            var genDir = Path.Combine(hub, CaveBuildAgentContextExporter.Folder);
            if (!Directory.Exists(genDir))
                return;

            foreach (var pattern in new[] { "planner-*.request", "planner-*.request.json", "planner-*.done.json" })
            {
                foreach (var full in Directory.GetFiles(genDir, pattern))
                {
                    var rel = ToHubRel(hub, full);
                    TryDelete(full, rel, cleared);
                    TryDelete(full + ".meta", rel + ".meta", cleared);
                }
            }
        }

        static void DeleteLibraryEphemeral(List<string> cleared)
        {
            var liveStatus = EnvironmentKitDataRoot.ResolvePath("CaveBuildLiveRunStatus.md");
            TryDelete(liveStatus, CaveBuildRunStatusPublisher.LiveStatusLibraryRel, cleared);
        }

        static void ClearPlannerSessionVolatile(string hub)
        {
            if (!CaveBuildCursorProcessResolver.TryRunPythonScript(
                    hub,
                    "clear-session-scratch.py",
                    $"--hub \"{hub}\"",
                    timeoutMs: 8000,
                    out _,
                    out var stderr,
                    out var error,
                    workflow: "session_scratch"))
            {
                if (!string.IsNullOrEmpty(error) || !string.IsNullOrEmpty(stderr))
                {
                    Debug.LogWarning(
                        "[EnvironmentKit] Planner volatile scratch clear skipped: " +
                        (error ?? stderr).Trim());
                }
            }
        }

        static void WriteRunSessionStamp(string hub, string reason, string sceneName, List<string> cleared)
        {
            var stampPath = Path.Combine(hub, RunSessionStampRel);
            var utc = DateTime.UtcNow.ToString("o");
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"buildStartedUtc\": \"{utc}\",");
            sb.AppendLine($"  \"reason\": \"{EscapeJson(reason)}\",");
            sb.AppendLine($"  \"sceneName\": \"{EscapeJson(sceneName ?? string.Empty)}\",");
            sb.AppendLine($"  \"dataRoot\": \"{EscapeJson(EnvironmentKitDataRoot.ResolveRoot())}\",");
            sb.AppendLine($"  \"generatedRoot\": \"{EscapeJson(EnvironmentKitDataRoot.ResolveProjectGeneratedRoot())}\",");
            sb.AppendLine($"  \"usingExternalVolume\": {EnvironmentKitDataRoot.IsUsingExternalVolume().ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"clearedCount\": {cleared.Count},");
            sb.AppendLine("  \"cleared\": [");
            for (var i = 0; i < cleared.Count; i++)
            {
                sb.Append("    \"").Append(EscapeJson(cleared[i])).Append('"');
                if (i < cleared.Count - 1)
                    sb.Append(',');
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            EnvironmentKitDataRoot.TryWriteAllText(stampPath, sb.ToString(), "run session stamp");
        }

        static string EscapeJson(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        static void TryDelete(string fullPath, string relForLog, List<string> cleared)
        {
            if (!File.Exists(fullPath))
                return;

            try
            {
                File.Delete(fullPath);
                cleared.Add(relForLog.Replace('\\', '/'));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CaveBuild] Could not delete scratch file {relForLog}: {ex.Message}");
            }
        }

        static string ToHubRel(string hub, string fullPath)
        {
            var hubFull = Path.GetFullPath(hub).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileFull = Path.GetFullPath(fullPath);
            if (fileFull.StartsWith(hubFull + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                fileFull.StartsWith(hubFull + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                return fileFull.Substring(hubFull.Length + 1).Replace('\\', '/');
            return fileFull;
        }
    }
}
#endif
