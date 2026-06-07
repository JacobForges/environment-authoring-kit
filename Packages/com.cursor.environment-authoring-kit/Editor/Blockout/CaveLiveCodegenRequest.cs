#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveLiveCodegenRequest
    {
        public const string ExportPath = "Assets/EnvironmentKit/Generated/CaveLiveFixRequest.json";

        public static void Write(Transform caveRoot, IReadOnlyList<string> issues, string trigger)
        {
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var projectRoot = Path.GetDirectoryName(Application.dataPath);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{System.DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"scene\": \"{Escape(SceneManager.GetActiveScene().name)}\",");
            sb.AppendLine($"  \"trigger\": \"{Escape(trigger)}\",");
            sb.AppendLine($"  \"caveRoot\": \"{Escape(caveRoot != null ? caveRoot.name : "")}\",");
            sb.AppendLine($"  \"hubGameplayScripts\": \"Assets/Scripts/\",");
            sb.AppendLine($"  \"tailoredPrompt\": \"{CaveBuildAgentArtifacts.TailoredPromptPath}\",");
            sb.AppendLine($"  \"sessionManifest\": \"{CaveBuildAgentArtifacts.SessionManifestPath}\",");
            sb.AppendLine($"  \"qualityReport\": \"Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json\",");
            sb.AppendLine($"  \"routeProbeReport\": \"{CaveRouteProbeRunner.ReportPath}\",");
            sb.AppendLine($"  \"combatProbeReport\": \"{CaveCombatProbeRunner.ReportPath}\",");
            sb.AppendLine($"  \"researchManifest\": \"{CaveBuildResearchExporter.ResearchPath}\",");
            sb.AppendLine("  \"requiredJson\": [");
            var req = CaveBuildAgentArtifacts.RequiredJsonForAgent;
            for (var r = 0; r < req.Length; r++)
                sb.AppendLine($"    \"{Escape(req[r])}\"{(r < req.Length - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"issues\": [");
            for (var i = 0; i < issues.Count; i++)
                sb.AppendLine($"    \"{Escape(issues[i])}\"{(i < issues.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine(
                "  \"instructions\": \"Read sessionManifest + requiredJson for THIS build only. Open CaveBuildResearchExecutionBrief.json and cited ResearchCache/entries/*/content.md + hillshade PNGs — plan table and C# fixes must cite those paths. Open tailoredPrompt for the per-pass action list. Apply targeted CaveBuildQualityStageFixer fixes for suggestedStageId from route/combat probes — no full cave rebuild unless path/ground_placement failed.\"");
            sb.AppendLine("}");

            File.WriteAllText(Path.Combine(projectRoot, ExportPath), sb.ToString());
            Debug.Log(
                $"[CaveLive] Wrote {ExportPath} ({issues.Count} issue(s), trigger={trigger}). " +
                $"Use {CaveBuildAgentArtifacts.TailoredPromptPath} after export-rung-prompt.");
        }

        static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
