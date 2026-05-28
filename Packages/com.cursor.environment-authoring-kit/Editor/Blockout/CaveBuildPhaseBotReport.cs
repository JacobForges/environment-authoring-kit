#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Overwritten JSON after each pipeline phase — feeds Cursor phase prompts and autonomous loop.
    /// </summary>
    public static class CaveBuildPhaseBotReport
    {
        public const string ReportRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildPhaseBotReport.json";

        /// <summary>Geo steps 1–12: no surface NavMesh bake or route probe (was freezing after validate).</summary>
        const int GeoCompleteQueuedStep = 13;

        public static void RecordAfterQueuedStep(int step, Transform caveRoot, string phaseId)
        {
            if (string.IsNullOrEmpty(phaseId))
                phaseId = CaveBuildPhaseResearchGate.ResolvePhaseId(step);

            if (step < GeoCompleteQueuedStep)
            {
                WriteMinimalReport(step, phaseId);
                return;
            }

            var surfaceRoute = SurfaceRouteProbeRunner.Run(caveRoot);
            SurfaceRouteProbeRunner.Export(surfaceRoute);

            CaveRouteProbeRunner.ProbeReport caveRoute = null;
            if (caveRoot != null && step >= 14)
            {
                caveRoute = CaveRouteProbeRunner.Run(caveRoot);
                if (step == 32 || step == 36 || step == 47 || step == 48)
                    CaveRouteProbeRunner.Export(caveRoute, caveRoot);
            }

            var surfaceProbe = SurfacePlaytestValidator.Run(caveRoot);
            SurfacePlaytestValidator.Export(surfaceProbe);

            WriteReport(step, phaseId, surfaceRoute, caveRoute, surfaceProbe);

            if (step > 0 && CaveBuildPhaseResearchGate.IsPhaseBoundary(step))
            {
                CaveBuildUnifiedPromptBridge.RefreshForPhase(phaseId, "other", -1, step, 0, out _);
            }
        }

        static void WriteMinimalReport(int step, string phaseId)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var body =
                "{\n" +
                $"  \"generatedUtc\": \"{System.DateTime.UtcNow:o}\",\n" +
                $"  \"queuedStep\": {step},\n" +
                $"  \"phaseId\": \"{Escape(phaseId)}\",\n" +
                "  \"deferredProbes\": true,\n" +
                "  \"note\": \"Surface/cave route probes run after geo step 13 — not during validate or per-geo substep.\"\n" +
                "}\n";
            File.WriteAllText(path, body);
            CaveBuildPipelineLog.Info($"Phase bot report (deferred probes) step {step} ({phaseId})", "Bot");
        }

        static void WriteReport(
            int step,
            string phaseId,
            SurfaceRouteProbeRunner.SurfaceRouteReport surfaceRoute,
            CaveRouteProbeRunner.ProbeReport caveRoute,
            SurfacePlaytestValidator.SurfaceProbeReport surfaceProbe)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{System.DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"queuedStep\": {step},");
            sb.AppendLine($"  \"phaseId\": \"{Escape(phaseId)}\",");
            sb.AppendLine("  \"probeOrder\": [\"surface_trails_roads\", \"cave_mouth\", \"underground_route\"],");
            sb.AppendLine($"  \"surfaceRoutePassed\": {(surfaceRoute != null && surfaceRoute.Passed ? "true" : "false")},");
            sb.AppendLine($"  \"surfaceRouteWaypoints\": {surfaceRoute?.WaypointCount ?? 0},");
            sb.AppendLine($"  \"reachedCaveMouth\": {(surfaceRoute != null && surfaceRoute.ReachedCaveMouth ? "true" : "false")},");
            sb.AppendLine($"  \"caveRoutePassed\": {(caveRoute != null && caveRoute.Passed ? "true" : "false")},");
            sb.AppendLine($"  \"caveRouteIssues\": {caveRoute?.Issues.Count ?? 0},");
            sb.AppendLine($"  \"surfaceProbePassed\": {(surfaceProbe != null && surfaceProbe.Passed ? "true" : "false")},");
            sb.AppendLine("  \"cursorPromptInputs\": [");
            sb.AppendLine($"    \"{ReportRel}\",");
            sb.AppendLine($"    \"{SurfaceRouteProbeRunner.ReportPath}\",");
            sb.AppendLine($"    \"{SurfacePlaytestValidator.ReportPath}\",");
            sb.AppendLine($"    \"{CaveRouteProbeRunner.ReportPath}\",");
            sb.AppendLine($"    \"{CaveBuildPhaseResearchGate.ActionPlanRel}\"");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"suggestedNextActions\": [");
            AppendSuggestions(sb, surfaceRoute, caveRoute, surfaceProbe);
            sb.AppendLine("  ],");
            sb.AppendLine("  \"doNot\": [");
            sb.AppendLine("    \"Do not rebuild entire cave for surface-only issues — fix terrain/trails/mouth only.\",");
            sb.AppendLine("    \"Do not radiate-replace main land center disk.\",");
            sb.AppendLine("    \"Do not skip ResearchCache URLs in CaveBuildResearchActionPlan before C# edits.\"");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            CaveBuildPipelineLog.Info($"Phase bot report step {step} ({phaseId})", "Bot");
        }

        static void AppendSuggestions(
            StringBuilder sb,
            SurfaceRouteProbeRunner.SurfaceRouteReport surfaceRoute,
            CaveRouteProbeRunner.ProbeReport caveRoute,
            SurfacePlaytestValidator.SurfaceProbeReport surfaceProbe)
        {
            var items = new System.Collections.Generic.List<string>();
            if (surfaceRoute != null)
            {
                foreach (var issue in surfaceRoute.Issues)
                    items.Add(issue);
                if (!surfaceRoute.ReachedCaveMouth)
                    items.Add("Align cave mouth to walkable trail terminus before underground probe.");
            }

            if (surfaceProbe != null)
            {
                foreach (var issue in surfaceProbe.Issues)
                    items.Add(issue);
            }

            if (caveRoute != null)
            {
                foreach (var issue in caveRoute.Issues)
                    items.Add($"[{issue.SuggestedStageId}] {issue.Message}");
            }

            if (items.Count == 0)
                items.Add("Continue next pipeline phase — surface and cave probes clean.");

            for (var i = 0; i < items.Count; i++)
            {
                var comma = i < items.Count - 1 ? "," : "";
                sb.AppendLine($"    {JsonQuote(items[i])}{comma}");
            }
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static string JsonQuote(string s) => "\"" + Escape(s) + "\"";
    }
}
#endif
