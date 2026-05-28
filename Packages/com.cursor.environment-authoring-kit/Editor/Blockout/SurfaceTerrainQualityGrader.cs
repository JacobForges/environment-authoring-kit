#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Full above-ground quality report for Terrain Build Grader + Cursor terrain workflow.
    /// </summary>
    public static class SurfaceTerrainQualityGrader
    {
        public const string QualityReportPath =
            CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainQualityReport.json";
        public const string FailingStagesPath =
            CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainFailingStages.json";

        public static SurfaceTerrainLadderReport Run(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot)
        {
            var seed = request?.Seed ?? 0;
            if (CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive &&
                SurfaceTerrainBuildLadder.TryTakeCachedGradedReport(seed, out var cached))
            {
                cached.GradingMode = "terrain_quality";
                WriteQualityReport(cached);
                WriteFailingStages(cached);
                CaveBuildEditorLog.LogSurface(
                    $"[TerrainLadder] Reused paced grade report ({cached.OverallScore} {cached.LetterGrade}) — skipped duplicate full ladder.",
                    forceUnityConsole: true);
                return cached;
            }

            RepairHeightfieldBeforeGrade(ground, request);

            var report = SurfaceTerrainBuildLadder.Run(ground, request, surfaceRoot);
            report.GradingMode = "terrain_quality";
            WriteQualityReport(report);
            WriteFailingStages(report);
            return report;
        }

        public static Transform ResolveSurfaceRoot()
        {
            var env = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            return env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
        }

        static void WriteQualityReport(SurfaceTerrainLadderReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, QualityReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{report.SceneName}\",");
            sb.AppendLine($"  \"seed\": {report.Seed},");
            sb.AppendLine($"  \"gradingMode\": \"{report.GradingMode}\",");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"letterGrade\": \"{report.LetterGrade}\",");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"targetScore\": {SurfaceTerrainBuildLadder.TargetOverallScore},");
            sb.AppendLine($"  \"ladderReportPath\": \"{SurfaceTerrainBuildLadder.ReportPath}\",");
            sb.AppendLine("  \"stages\": [");
            for (var i = 0; i < report.Stages.Count; i++)
            {
                var s = report.Stages[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{s.StageId}\",");
                sb.AppendLine($"      \"name\": \"{Escape(s.StageName)}\",");
                sb.AppendLine($"      \"score\": {s.Score},");
                sb.AppendLine($"      \"weight\": {s.Weight},");
                sb.AppendLine($"      \"critical\": {(s.Critical ? "true" : "false")},");
                sb.AppendLine($"      \"passed\": {(s.Passed ? "true" : "false")},");
                sb.AppendLine("      \"issues\": [");
                for (var j = 0; j < s.Issues.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Issues[j])}\"{(j < s.Issues.Count - 1 ? "," : "")}");
                sb.AppendLine("      ],");
                sb.AppendLine("      \"fixes\": [");
                for (var j = 0; j < s.Fixes.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Fixes[j])}\"{(j < s.Fixes.Count - 1 ? "," : "")}");
                sb.AppendLine("      ]");
                sb.AppendLine(i < report.Stages.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            report.ExportPath = QualityReportPath;

            var ladderPath = Path.Combine(hub, SurfaceTerrainBuildLadder.ReportPath);
            if (!string.Equals(path, ladderPath, System.StringComparison.Ordinal))
                File.Copy(path, ladderPath, overwrite: true);
        }

        static void WriteFailingStages(SurfaceTerrainLadderReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, FailingStagesPath);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"stages\": [");
            var first = true;
            foreach (var s in report.Stages)
            {
                if (s.Passed && s.Score >= SurfaceTerrainBuildLadder.StagePassScore)
                    continue;
                if (!first)
                    sb.AppendLine(",");
                first = false;
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{s.StageId}\",");
                sb.AppendLine($"      \"score\": {s.Score},");
                sb.AppendLine("      \"issues\": [");
                for (var j = 0; j < s.Issues.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Issues[j])}\"{(j < s.Issues.Count - 1 ? "," : "")}");
                sb.AppendLine("      ],");
                sb.AppendLine("      \"fixes\": [");
                for (var j = 0; j < s.Fixes.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Fixes[j])}\"{(j < s.Fixes.Count - 1 ? "," : "")}");
                sb.AppendLine("      ]");
                sb.Append("    }");
            }

            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Cursor / terrain-quality regrade has no ladder pre-pass — repair all tiles before analyze.</summary>
        static void RepairHeightfieldBeforeGrade(SceneGroundInfo ground, WorldGenerationRequest request)
        {
            if (ground?.Terrain == null)
                return;

            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var extent = SurfaceTerrainPlayRegion.ResolveRepairExtentMeters(
                ground.Terrain,
                center,
                request);
            var repaired = 0;
            foreach (var terrain in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain))
            {
                if (terrain == null)
                    continue;
                repaired += SurfaceTerrainCraterRepair.RepairHeightfieldPlayable(
                    terrain,
                    center,
                    extent,
                    maxPasses: 22);
                terrain.Flush();
            }

            if (repaired > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[TerrainLadder] Pre-grade heightfield repair — {repaired} cell operation(s) (unified extent {extent:F0}m).",
                    forceUnityConsole: true);
            }
        }
    }
}
#endif
