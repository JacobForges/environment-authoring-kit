using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Play Mode playtest findings for Cursor prompts.</summary>
    public static class CavePlaytestIssueReport
    {
        public const string ReportFileName = "CavePlaytestHumanWalkReport.json";

        public static void Export(IReadOnlyList<string> issues, string phase = "play_mode")
        {
            var hub = ResolveHubRoot();
            var path = Path.Combine(hub, "Assets/EnvironmentKit/Generated", ReportFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{System.DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"phase\": \"{Escape(phase)}\",");
            sb.AppendLine($"  \"issueCount\": {issues?.Count ?? 0},");
            sb.AppendLine($"  \"passed\": {(issues == null || issues.Count == 0 ? "true" : "false")},");
            sb.AppendLine("  \"issues\": [");
            if (issues != null)
            {
                for (var i = 0; i < issues.Count; i++)
                {
                    var comma = i < issues.Count - 1 ? "," : "";
                    sb.AppendLine($"    \"{Escape(issues[i])}\"{comma}");
                }
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        static string ResolveHubRoot()
        {
            var data = Application.dataPath;
            return Directory.GetParent(data)?.FullName ?? data;
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
