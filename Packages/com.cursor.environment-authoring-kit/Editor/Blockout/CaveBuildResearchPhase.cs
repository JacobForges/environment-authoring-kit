using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Dedicated post-meat research queue phase: local catalog refresh, enrichment export, optional Cursor research rung.
    /// </summary>
    public static class CaveBuildResearchPhase
    {
        public const string EnrichmentPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchEnrichment.json";
        public const string PhaseLogPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchPhaseLog.json";

        /// <summary>
        /// Step 0: scan grade + scene, write <see cref="CaveBuildResearchNeedsAnalyzer.NeedsPath"/>,
        /// decide if web/Cursor research is warranted.
        /// </summary>
        public static CaveBuildResearchNeedsAnalyzer.ResearchNeedsSnapshot RunAnalyzeNeeds(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            var needs = CaveBuildResearchNeedsAnalyzer.AnalyzeAndWrite(quality, caveRoot, ground);
            AppendPhaseLog(
                needs.researchRequired ? "needs_analysis_required" : "needs_analysis_skip",
                needs.primaryRung,
                quality?.OverallScore ?? 0);
            return needs;
        }

        public static void RunCatalogRefresh(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (quality == null)
                return;

            var needs = CaveBuildResearchNeedsAnalyzer.BuildSnapshot(quality, caveRoot, ground);
            var rung = needs.primaryRung ?? CaveBuildPromptLadder.PickActiveRung(quality, caveRoot, ground)
                       ?? CaveBuildPromptLadder.RungResearch;
            CaveBuildResearchExporter.Write(quality, rung, quality.LadderGradePasses);
            CaveBuildAgentContextExporter.Export(quality, caveRoot, meatLoopPass: -1, ground);

            if (CaveBuildResearchCacheBridge.SyncFullResearchPull(rung, out var pullMsg))
                Debug.Log("[CaveBuild] Research pull complete — " + pullMsg);
            else
                Debug.LogError("[CaveBuild] Research pull FAILED (cache/images/hillshades required): " + pullMsg);

            AppendPhaseLog("catalog_refresh", rung, quality.OverallScore);
            Debug.Log(
                $"[CaveBuild] Research phase — mandatory pull + catalog ({CaveBuildResearchExporter.ResearchPath}, {CaveBuildResearchCacheBridge.CacheIndexPath}), rung={rung}.");
        }

        public static void RunOnlineEnrichment(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            WriteEnrichmentManifest(quality, caveRoot, ground);

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.runPostBuildResearchPhase)
                return;

            if (!settings.invokeCursorOnResearchPhase)
                return;

            if (settings.autoInvokeAfterEveryBuild)
            {
                Debug.Log(
                    "[CaveBuild] Research phase — skipping separate Cursor invoke; post-build workflow will run after pipeline finalize.");
                return;
            }

            var needsPath = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), CaveBuildResearchNeedsAnalyzer.NeedsPath);
            CaveBuildResearchNeedsAnalyzer.ResearchNeedsSnapshot needs = null;
            if (File.Exists(needsPath))
            {
                try
                {
                    needs = JsonUtility.FromJson<CaveBuildResearchNeedsAnalyzer.ResearchNeedsSnapshot>(
                        File.ReadAllText(needsPath));
                }
                catch
                {
                    // ignored
                }
            }

            if (needs != null && !needs.researchRequired)
            {
                Debug.Log(
                    "[CaveBuild] Research phase — needs analysis says no Cursor research required (Ship or no failing rungs).");
                return;
            }

            if (quality != null && CaveBuildQualityRubric.MeetsShipTarget(quality))
            {
                Debug.Log("[CaveBuild] Research phase — Ship target met; skipping Cursor research invoke.");
                return;
            }

            if (string.IsNullOrWhiteSpace(CaveBuildCursorSettings.ResolveApiKey()))
            {
                Debug.LogWarning("[CaveBuild] Research phase — no API key; enrichment JSON only.");
                return;
            }

            if (CaveBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(
                    out var msg,
                    rung: CaveBuildPromptLadder.RungResearch,
                    startLadderChain: false))
            {
                Debug.Log("[CaveBuild] Research phase — Cursor research rung started. " + msg);
                AppendPhaseLog("cursor_research_invoke", CaveBuildPromptLadder.RungResearch, quality?.OverallScore ?? 0);
            }
            else
            {
                Debug.LogWarning("[CaveBuild] Research phase — Cursor invoke skipped: " + msg);
            }
        }

        public static void RunPersistPhaseSummary(CaveBuildQualityReport quality)
        {
            CaveBuildResearchNeedsAnalyzer.TrimPhaseLogIfNeeded();
            AppendPhaseLog("phase_complete", "research", quality?.OverallScore ?? 0);
            CaveBuildDeferredAssetRefresh.RequestRefresh();
        }

        static void WriteEnrichmentManifest(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, EnrichmentPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var needs = CaveBuildResearchNeedsAnalyzer.BuildSnapshot(quality, caveRoot, ground);
            var rung = needs.primaryRung ?? CaveBuildPromptLadder.RungResearch;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:O}\",");
            sb.AppendLine($"  \"activeRung\": \"{Esc(rung)}\",");
            sb.AppendLine($"  \"overallScore\": {quality?.OverallScore ?? 0},");
            sb.AppendLine($"  \"researchRequired\": {(needs.researchRequired ? "true" : "false")},");
            sb.AppendLine($"  \"needsPath\": \"{Esc(CaveBuildResearchNeedsAnalyzer.NeedsPath)}\",");
            sb.AppendLine($"  \"policy\": \"{Esc(CaveBuildResearchSources.WorkflowHint)}\",");
            sb.AppendLine($"  \"researchManifest\": \"{Esc(CaveBuildResearchExporter.ResearchPath)}\",");
            sb.AppendLine($"  \"researchCacheIndex\": \"{Esc(CaveBuildResearchCacheBridge.CacheIndexPath)}\",");
            sb.AppendLine($"  \"researchCachePointer\": \"{Esc(CaveBuildResearchCacheBridge.GeneratedPointerPath)}\",");
            sb.AppendLine(
                "  \"cachePolicy\": \"Read ResearchCache + floridaTerrain hillshades/aquifer entries before web search. Cave structure only (no water table/bathy). See RESEARCH_DATA_ATTRIBUTION.md.\",");
            sb.AppendLine("  \"suggestedQueries\": [");
            var queries = needs.suggestedQueries ?? Array.Empty<string>();
            for (var i = 0; i < queries.Length; i++)
                sb.AppendLine($"    \"{Esc(queries[i])}\"{(i < queries.Length - 1 ? "," : "")}");
            if (queries.Length == 0)
                sb.AppendLine("    \"Unity procedural cave environment editor 2025\"");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"focusTopics\": [");
            var topics = needs.focusTopics ?? Array.Empty<string>();
            for (var i = 0; i < topics.Length; i++)
                sb.AppendLine($"    \"{Esc(topics[i])}\"{(i < topics.Length - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"catalogUrls\": [");
            var urls = CaveBuildResearchExporter.AllUrlsFromSeed();
            for (var i = 0; i < Math.Min(urls.Length, 8); i++)
                sb.AppendLine($"    \"{Esc(urls[i])}\"{(i < Math.Min(urls.Length, 8) - 1 ? "," : "")}");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[CaveBuild] Research enrichment written — {EnrichmentPath}");
        }

        static void AppendPhaseLog(string step, string rung, int score)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, PhaseLogPath);
            var entries = new List<string>();
            if (File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.StartsWith("["))
                        entries.Add(existing.Trim('[', ']').Trim());
                }
                catch
                {
                    // overwrite corrupt log
                }
            }

            var line =
                $"{{\"utc\":\"{DateTime.UtcNow:O}\",\"step\":\"{Esc(step)}\",\"rung\":\"{Esc(rung)}\",\"score\":{score}}}";
            entries.Add(line);
            var body = "[\n  " + string.Join(",\n  ", entries) + "\n]";
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, body);
        }

        static string Esc(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
