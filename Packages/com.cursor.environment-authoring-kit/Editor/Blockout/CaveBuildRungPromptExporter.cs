using System;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Refreshes live Generated JSON and writes per-rung Cursor prompts (tsx buildLadderPrompt).
    /// </summary>
    public static class CaveBuildRungPromptExporter
    {
        public const string ActiveRungPromptPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildActiveRungPrompt.md";
        public const string AgentPromptPath = "Assets/EnvironmentKit/Generated/CaveBuildAgentPrompt.md";
        public const string TailoredPromptPath = CaveBuildAgentArtifacts.TailoredPromptPath;

        public static readonly string[] LiveManifestPaths =
        {
            "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
            CaveBuildAgentContextExporter.VisualShellAuditPath,
            CaveBuildAgentContextExporter.FailingStagesPath,
            CaveBuildAgentContextExporter.LadderContextPath,
            CaveBuildAgentContextExporter.MeatLoopHistoryPath,
            CaveBuildResearchExporter.ResearchPath,
            CaveBuildResearchCacheBridge.ExecutionBriefPath,
            "Assets/EnvironmentKit/Generated/CaveBuildWorkflowContext.json",
            "Assets/EnvironmentKit/Generated/CaveBuildCompileDiagnostics.json",
            "Assets/EnvironmentKit/Generated/CaveBuildAgentMemory.json",
            "Assets/EnvironmentKit/Generated/CaveBuildPreBuildReport.json",
            "Assets/EnvironmentKit/Generated/CaveBuildPreBuildWorkflow.json",
        };

        public static bool PrepareAgentInvoke(
            string rung,
            Transform caveRoot,
            SceneGroundInfo ground,
            CaveBuildQualityReport report = null,
            int meatLoopPass = -1)
        {
            report ??= TryLoadQualityReport();
            if (report == null)
            {
                Debug.LogWarning("[CaveCursor] Per-rung prompt skipped — no CaveBuildQualityReport.json.");
                return false;
            }

            if (caveRoot != null)
            {
                CaveBuildAgentContextExporter.Export(report, caveRoot, meatLoopPass, ground);
                if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                    CaveSpawnTeleportAuthority.ApplyMainAreaTeleportSpawn(caveRoot);
            }

            CaveBuildCompileGate.ExportDiagnostics();
            rung = string.IsNullOrWhiteSpace(rung)
                ? CaveBuildPromptLadder.PickActiveRung(report, caveRoot, ground)
                : rung.Trim();

            CaveBuildAgentArtifacts.ResetPromptsBeforeCursorInvoke(report.SceneName, rung, meatLoopPass);

            if (CaveBuildResearchCacheBridge.TryFastPathResearchPull(rung, out var fastMsg))
            {
                Debug.Log("[CaveCursor] Research fast path before agent — " + fastMsg);
            }
            else if (!CaveBuildResearchCacheBridge.CacheExists())
            {
                if (!CaveBuildResearchCacheBridge.SyncFullResearchPull(rung, out var pullMsg))
                    Debug.LogWarning("[CaveCursor] Research pull before agent: " + pullMsg);
                else
                    Debug.Log("[CaveCursor] Research pull before agent — " + pullMsg);
            }
            else if (!CaveBuildResearchCacheBridge.SyncCache(rung, pullImages: true, out var cacheMsg))
            {
                Debug.LogWarning("[CaveCursor] Research cache refresh: " + cacheMsg);
            }

            CaveBuildResearchCacheBridge.SyncResearchExecutionBrief(rung, meatLoopPass, out _);

            var iteration = Mathf.Max(0, meatLoopPass);
            if (!CaveBuildPhasePromptBridge.ExportAutonomousBrief(iteration, out var briefMsg))
                Debug.LogWarning("[CaveCursor] Autonomous brief: " + briefMsg);

            var phaseId = PhaseIdForRung(rung);
            if (!CaveBuildPhasePromptBridge.ExportPhasePrompt(phaseId, rung, meatLoopPass, iteration, out var phaseMsg))
                Debug.LogWarning("[CaveCursor] Phase prompt: " + phaseMsg);

            EnsureTailoredPromptOnDisk(rung, report, meatLoopPass);
            var hub = CaveBuildCursorAgentBridge.ResolveHubRoot();
            if (!File.Exists(Path.Combine(hub, TailoredPromptPath)))
            {
                Debug.LogWarning("[CaveCursor] Tailored prompt missing after export.");
                return false;
            }

            Debug.Log(
                $"[CaveCursor] Tailored prompt ready (rung={rung}) → {TailoredPromptPath}");
            return true;
        }

        static string PhaseIdForRung(string rung)
        {
            if (rung == CaveBuildPromptLadder.RungGroundPlacement)
                return "ground_placement";
            if (rung == CaveBuildPromptLadder.RungVisualShell)
                return "visual_shell";
            if (rung == CaveBuildPromptLadder.RungFloorCollision)
                return "floor_collision";
            if (rung == CaveBuildPromptLadder.RungMaterials)
                return "materials_lighting";
            if (rung == CaveBuildPromptLadder.RungPerformance)
                return "performance";
            if (rung == CaveBuildPromptLadder.RungResearch)
                return "research";
            return "packaging_ship";
        }

        public static CaveBuildQualityReport TryLoadQualityReport()
        {
            var hub = CaveBuildCursorAgentBridge.ResolveHubRoot();
            var path = Path.Combine(hub, CaveBuildQualityReport.DefaultExportPath);
            if (!File.Exists(path))
                return null;

            if (CaveBuildQualityReportLoader.TryLoad(hub, out var report))
                return report;

            Debug.LogWarning("[CaveCursor] Could not load quality report from " + path);
            return null;
        }

        /// <summary>Helper pipeline: always refresh tailored prompt (async during active builds).</summary>
        public static bool TryExportRungPromptForHelper(string rung, out string message)
        {
            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                message = $"Queued export-rung-prompt.ts (rung={rung}) — use async helper path.";
                return true;
            }

            return TryExportRungPromptViaScript(rung, out message);
        }

        /// <summary>Writes tailored + active rung prompts from latest quality JSON (tsx or C# fallback).</summary>
        public static void EnsureTailoredPromptOnDisk(
            string rung,
            CaveBuildQualityReport report,
            int meatLoopPass = -1)
        {
            if (report == null)
                return;

            rung = string.IsNullOrWhiteSpace(rung)
                ? CaveBuildPromptLadder.PickActiveRung(report, null, default)
                : rung.Trim();

            if (TryExportRungPromptViaScript(rung, out var msg))
            {
                Debug.Log($"[CaveCursor] Tailored prompt exported ({rung}): {msg}");
                return;
            }

            Debug.LogWarning("[CaveCursor] export-rung-prompt.ts failed — writing C# fallback tailored prompt.");
            WriteFallbackPrompt(report, rung, meatLoopPass);
        }

        static bool TryExportRungPromptViaScript(string rung, out string message)
        {
            message = null;
            var hub = CaveBuildCursorAgentBridge.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, "export-rung-prompt.ts");
            if (!File.Exists(script))
            {
                message = "Missing export-rung-prompt.ts — run npm install in Tools/cave-grader.";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx in Tools/cave-grader — run npm install.";
                return false;
            }

            var args = $"\"{tsxCli}\" \"{script}\" --rung={rung}";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = node,
                Arguments = args,
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            CaveBuildCursorProcessResolver.ApplyEnvironment(psi, hub, rung, workflow: "cave");

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                message = "Failed to start export-rung-prompt.ts.";
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120_000);
            if (proc.ExitCode != 0)
            {
                message = $"export-rung-prompt.ts exit {proc.ExitCode}: {stderr}";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(stdout))
                Debug.Log(stdout.TrimEnd());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            return true;
        }

        static void WriteFallbackPrompt(CaveBuildQualityReport report, string rung, int pass)
        {
            CaveBuildQualityAgentBridge.WriteStructuredPrompt(report, includeLiveSection: false, pass: pass);
        }
    }
}
