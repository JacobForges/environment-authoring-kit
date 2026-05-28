using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Writes machine-readable quality JSON for human/AI review in Cursor.</summary>
    static class CaveBuildQualityReportWriter
    {
        const string Folder = "Assets/EnvironmentKit/Generated";

        public static void Write(CaveBuildQualityReport report, string gradingMode = "rule_ladder")
        {
            if (report == null)
                return;

            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", Folder);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{Escape(report.SceneName)}\",");
            sb.AppendLine($"  \"seed\": {report.Seed},");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"weightedOverallScore\": {report.WeightedOverallScore},");
            sb.AppendLine($"  \"letterGrade\": \"{Escape(report.LetterGrade)}\",");
            sb.AppendLine($"  \"gradeDescription\": \"{Escape(CaveBuildQualityRubric.DescribeGrade(report.LetterGrade))}\",");
            sb.AppendLine($"  \"gradingStandard\": \"{CaveBuildQualityRubric.GradingStandard}\",");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"meetsBetaTarget\": {(report.MeetsBetaTarget ? "true" : "false")},");
            sb.AppendLine($"  \"meetsShipTarget\": {(report.MeetsShipTarget ? "true" : "false")},");
            sb.AppendLine("  \"shipBlockers\": [");
            for (var b = 0; b < report.ShipBlockers.Count; b++)
                sb.AppendLine($"    \"{Escape(report.ShipBlockers[b])}\"{(b < report.ShipBlockers.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine($"  \"meetsStrictTarget\": {(report.MeetsShipTarget ? "true" : "false")},");
            sb.AppendLine($"  \"isDud\": {(report.IsDud ? "true" : "false")},");
            sb.AppendLine($"  \"hardFailure\": {(report.HardFailure ? "true" : "false")},");
            sb.AppendLine($"  \"gradingVersion\": \"{Escape(report.GradingVersion)}\",");
            sb.AppendLine($"  \"recommendedAction\": \"{report.RecommendedAction}\",");
            sb.AppendLine($"  \"manifestPath\": \"{CaveBuildQualitySystem.ManifestPath}\",");
            sb.AppendLine("  \"dudReasons\": [");
            for (var d = 0; d < report.DudReasons.Count; d++)
                sb.AppendLine($"    \"{Escape(report.DudReasons[d])}\"{(d < report.DudReasons.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine($"  \"targetGrade\": \"{CaveBuildQualityRubric.TargetGrade}\",");
            sb.AppendLine($"  \"betaGrade\": \"{CaveBuildQualityRubric.BetaGrade}\",");
            sb.AppendLine($"  \"targetScore\": {CaveBuildQualityRubric.ShipScore},");
            sb.AppendLine($"  \"betaScore\": {CaveBuildQualityRubric.BetaScore},");
            sb.AppendLine($"  \"stagePassScore\": {CaveBuildQualityRubric.StagePassScore},");
            sb.AppendLine($"  \"remediationPasses\": {report.RemediationPasses},");
            sb.AppendLine($"  \"ladderGradePasses\": {report.LadderGradePasses},");
            sb.AppendLine($"  \"adventureMode\": {(report.AdventureMode ? "true" : "false")},");
            sb.AppendLine($"  \"gradingMode\": \"{Escape(gradingMode)}\",");
            sb.AppendLine("  \"strictVisualRequired\": true,");
            sb.AppendLine("  \"agentPromptPath\": \"Assets/EnvironmentKit/Generated/CaveBuildMeatLoopAgentPrompt.md\",");
            sb.AppendLine("  \"agentContextPaths\": [");
            sb.AppendLine($"    \"{CaveBuildAgentContextExporter.VisualShellAuditPath}\",");
            sb.AppendLine($"    \"{CaveBuildAgentContextExporter.FailingStagesPath}\",");
            sb.AppendLine($"    \"{CaveBuildAgentContextExporter.MeatLoopHistoryPath}\",");
            sb.AppendLine($"    \"{CaveBuildAgentContextExporter.LadderContextPath}\",");
            sb.AppendLine($"    \"{CaveBuildResearchExporter.ResearchPath}\",");
            sb.AppendLine($"    \"{CaveBuildWorkflowExporter.WorkflowPath}\",");
            sb.AppendLine($"    \"{CaveBuildCompileGate.DiagnosticsPath}\",");
            sb.AppendLine($"    \"{CaveBuildAgentMemoryExporter.MemoryPath}\"");
            sb.AppendLine("  ],");
            sb.AppendLine(
                "  \"ladderHint\": \"Commercial production ladder: research → compile_gate → fix rung. Target Ship (95+). Beta (85+) = playtest milestone.\",");
            sb.AppendLine(
                "  \"reviewHint\": \"Read CaveBuildQualityReport.json + failing stages. Target Ship for release-ready slice, not marketing AAA.\",");
            sb.AppendLine("  \"topFailingStages\": [");
            var topFails = new List<string>();
            foreach (var stage in report.Stages)
            {
                if (stage != null && stage.Score < CaveBuildQualityRubric.StagePassScore)
                    topFails.Add($"{stage.StageId}:{stage.Score}");
            }

            for (var t = 0; t < topFails.Count; t++)
                sb.AppendLine($"    \"{Escape(topFails[t])}\"{(t < topFails.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");

            sb.AppendLine("  \"stages\": [");

            for (var i = 0; i < report.Stages.Count; i++)
            {
                var s = report.Stages[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{Escape(s.StageId)}\",");
                sb.AppendLine($"      \"name\": \"{Escape(s.StageName)}\",");
                sb.AppendLine($"      \"score\": {s.Score},");
                sb.AppendLine($"      \"weight\": {s.Weight},");
                sb.AppendLine($"      \"passed\": {(s.Passed ? "true" : "false")},");
                sb.AppendLine("      \"issues\": [");
                for (var j = 0; j < s.Issues.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Issues[j])}\"{(j < s.Issues.Count - 1 ? "," : "")}");
                sb.AppendLine("      ],");
                sb.AppendLine("      \"fixes\": [");
                for (var j = 0; j < s.Fixes.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Fixes[j])}\"{(j < s.Fixes.Count - 1 ? "," : "")}");
                sb.AppendLine("      ]");
                sb.Append("    }");
                if (i < report.Stages.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(report.ExportPath, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            Debug.Log(
                $"[CaveBuild] Commercial grade: {report.LetterGrade} ({report.OverallScore}/100) " +
                $"Ship={report.MeetsShipTarget} Beta={report.MeetsBetaTarget} — {report.ExportPath}");
        }

        static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
