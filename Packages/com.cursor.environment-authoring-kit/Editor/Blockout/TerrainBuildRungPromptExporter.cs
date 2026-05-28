#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class TerrainBuildRungPromptExporter
    {
        public const string TailoredPromptPath =
            CaveBuildAgentContextExporter.Folder + "/TerrainBuildTailoredAgentPrompt.md";
        public const string ActiveRungPromptPath = SurfaceTerrainBuildLadder.ActivePromptPath;

        public static readonly string[] LiveManifestPaths =
        {
            SurfaceTerrainQualityGrader.QualityReportPath,
            SurfaceTerrainBuildLadder.ReportPath,
            SurfaceTerrainBuildLadder.ContextPath,
            SurfaceTerrainQualityGrader.FailingStagesPath,
            SurfacePlaytestValidator.ReportPath,
            SurfaceRouteProbeRunner.ReportPath,
            SurfaceTerrainAiPhases.LogRel,
            SurfaceIntelligentPropPlacer.PlacementPlanRel,
        };

        /// <summary>
        /// Always writes a full fix prompt immediately (issues + suggested fixes + JSON paths).
        /// Called before every terrain meat-loop fix stage.
        /// </summary>
        public static void WriteTailoredFixPrompt(
            string rung,
            SurfaceTerrainLadderReport report,
            int seed,
            int fixRound)
        {
            if (string.IsNullOrEmpty(rung) || report == null)
                return;

            CaveBuildPhasePromptBridge.ExportResearchAgentPrompt("terrain_fix", rung, out _);
            System.Environment.SetEnvironmentVariable("CAVE_CURSOR_RUNG", rung);
            System.Environment.SetEnvironmentVariable("CAVE_WORKFLOW", "terrain");
            System.Environment.SetEnvironmentVariable("CAVE_FORCE_PROMPT_EXPORT", "1");
            if (TryExportRungSync(rung, out _))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[TerrainMeat] Fix prompt (research + ladder) → {TailoredPromptPath}",
                    forceUnityConsole: true);
                return;
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var tailoredPath = Path.Combine(hub, TailoredPromptPath);
            var activePath = Path.Combine(hub, ActiveRungPromptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(tailoredPath) ?? hub);

            var stage = report.Stages?.Find(s => s.StageId == rung);
            var sb = new StringBuilder();
            sb.AppendLine("# Terrain fix pass — read before editing");
            sb.AppendLine();
            sb.AppendLine($"**Fix round:** {fixRound + 1} | **Seed:** {seed} | **Rung:** `{rung}`");
            sb.AppendLine(
                $"**Overall:** {report.OverallScore}/100 ({report.LetterGrade}) — target {SurfaceTerrainQualityMeatLoop.TargetOverallScore}+");
            sb.AppendLine();
            sb.AppendLine("## What is wrong (this rung)");
            if (stage != null && stage.Issues != null && stage.Issues.Count > 0)
            {
                foreach (var issue in stage.Issues)
                    sb.AppendLine($"- {issue}");
            }
            else
                sb.AppendLine("- See failing stages in report JSON below.");
            sb.AppendLine();
            sb.AppendLine("## How to fix (this rung only)");
            if (stage != null && stage.Fixes != null && stage.Fixes.Count > 0)
            {
                foreach (var fix in stage.Fixes)
                    sb.AppendLine($"- {fix}");
            }
            else
            {
                sb.AppendLine($"- Hub package: `SurfaceTerrainLadderFixer`, `SurfaceTerrainBuildLadder.cs`");
                sb.AppendLine("- One minimal C# change for this rung; re-grade in Unity after compile.");
            }

            sb.AppendLine();
            sb.AppendLine("## All failing rungs (summary)");
            foreach (var s in report.Stages ?? new System.Collections.Generic.List<TerrainStageGrade>())
            {
                if (!s.Passed || s.Score < SurfaceTerrainBuildLadder.StagePassScore)
                    sb.AppendLine($"- `{s.StageId}`: {s.Score}/100");
            }

            sb.AppendLine();
            sb.AppendLine("## Live JSON (read on disk)");
            sb.AppendLine($"- `{hub}/{SurfaceTerrainBuildLadder.ReportPath}`");
            sb.AppendLine($"- `{hub}/{SurfaceTerrainQualityGrader.QualityReportPath}`");
            sb.AppendLine($"- `{hub}/{SurfaceTerrainBuildLadder.ContextPath}`");
            sb.AppendLine($"- `{hub}/Assets/EnvironmentKit/Generated/CaveBuildGeneratedJsonManifest.json`");
            sb.AppendLine();
            sb.AppendLine("## Agent rules");
            sb.AppendLine("1. Fix **only** the active rung above — smallest correct diff in Hub C#.");
            sb.AppendLine("2. Read `SurfaceTerrainSculptAgentPrompt.md` — play center XZ + seed are the geo anchor; do not relocate layout.");
            sb.AppendLine("3. LiDAR is **≤28% structural bias**; procedural FBM is the playable surface (no flat inner disk / quilted ring).");
            sb.AppendLine("4. Edit `SurfaceDemGeoreferenceAuthor`, `SurfaceTerrainCenteredAuthor`, `SurfaceTerrainLidarCreativeGuide` — not Terrain Inspector.");
            sb.AppendLine("5. Do not delete Generated/ or restart the full build from the agent.");
            sb.AppendLine("6. After edits, Unity will re-grade automatically.");
            AppendResearchPromptSection(sb, hub);

            var body = sb.ToString();
            File.WriteAllText(tailoredPath, body, Encoding.UTF8);
            File.WriteAllText(activePath, body, Encoding.UTF8);

            CaveBuildEditorLog.LogSurface(
                $"[TerrainMeat] Fix prompt written (fallback) → {TailoredPromptPath}",
                forceUnityConsole: true);
        }

        static void AppendResearchPromptSection(StringBuilder sb, string hub)
        {
            var researchPath = Path.Combine(hub, CaveBuildPhasePromptBridge.ResearchAgentPromptPath);
            sb.AppendLine();
            sb.AppendLine("## Research (summarized — read before fix)");
            if (!File.Exists(researchPath))
            {
                sb.AppendLine(
                    $"- Missing `{CaveBuildPhasePromptBridge.ResearchAgentPromptPath}` — run `npm run generate-research-agent-prompt` in Tools/cave-grader.");
                return;
            }

            try
            {
                var text = File.ReadAllText(researchPath, Encoding.UTF8);
                const int maxChars = 6000;
                if (text.Length > maxChars)
                    text = text.Substring(0, maxChars) + "\n\n…(truncated — open full file on disk)";
                sb.AppendLine(text);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"- Could not read research prompt: {ex.Message}");
            }
        }

        public static bool PrepareAgentInvokeFromReport(
            string rung,
            SurfaceTerrainLadderReport report,
            out string message)
        {
            message = null;
            if (report == null)
            {
                message = "No terrain report.";
                return false;
            }

            rung = string.IsNullOrWhiteSpace(rung)
                ? SurfaceTerrainBuildLadder.PickActiveRung(report)
                : rung.Trim();
            if (string.IsNullOrEmpty(rung))
            {
                message = "All terrain rungs passing.";
                return false;
            }

            WriteTailoredFixPrompt(rung, report, report.Seed, 0);
            return TryExportRungSync(rung, out message) || File.Exists(
                Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), TailoredPromptPath));
        }

        public static bool PrepareAgentInvoke(string rung, out string message)
        {
            message = null;
            var ground = SceneGroundResolver.Resolve();
            var surface = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
            var request = new WorldGenerationRequest
            {
                SurfaceScope = SurfaceBuildScope.SurfaceOnly,
                SurfaceIncludeTrails = true,
            };

            var report = SurfaceTerrainQualityGrader.Run(ground, request, surface);
            return PrepareAgentInvokeFromReport(rung, report, out message);
        }

        public static bool TryExportRungSync(string rung, out string message) => TryExportViaScript(rung, out message);

        public static bool TryExportRungAsync(string rung, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(rung))
            {
                message = "No terrain rung.";
                return true;
            }

            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                var began = CaveBuildPhasePromptBridge.TryBeginRunTsx(
                    "export-terrain-rung-prompt.ts",
                    $"--rung={rung}",
                    null,
                    120_000,
                    (_, _) => { },
                    "CAVE_WORKFLOW=terrain",
                    $"CAVE_CURSOR_RUNG={rung}",
                    "CAVE_FORCE_PROMPT_EXPORT=1");
                message = began
                    ? $"Queued export-terrain-rung-prompt.ts (rung={rung})."
                    : "Could not queue export-terrain-rung-prompt.ts.";
                return began;
            }

            return TryExportViaScript(rung, out message);
        }

        static bool TryExportViaScript(string rung, out string message)
        {
            message = null;
            var hub = CaveBuildCursorAgentBridge.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, "export-terrain-rung-prompt.ts");
            if (!File.Exists(script))
            {
                message = "Missing export-terrain-rung-prompt.ts";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx — npm install in Tools/cave-grader.";
                return false;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = node,
                Arguments = $"\"{tsxCli}\" \"{script}\" --rung={rung}",
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            CaveBuildCursorProcessResolver.ApplyEnvironment(psi, hub, rung, workflow: "terrain");
            psi.EnvironmentVariables["CAVE_FORCE_PROMPT_EXPORT"] = "1";

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                message = "Failed to start export-terrain-rung-prompt.ts";
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120_000);
            if (proc.ExitCode != 0)
            {
                message = $"export-terrain-rung-prompt exit {proc.ExitCode}: {stderr}";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(stdout))
                Debug.Log(stdout.TrimEnd());
            return true;
        }
    }
}
#endif
