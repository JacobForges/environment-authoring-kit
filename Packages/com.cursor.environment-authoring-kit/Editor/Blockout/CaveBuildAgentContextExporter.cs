using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Writes companion JSON files beside CaveBuildQualityReport.json for Cursor agent context.
    /// </summary>
    static class CaveBuildAgentContextExporter
    {
        public const string Folder = "Assets/EnvironmentKit/Generated";

        public const string VisualShellAuditPath = Folder + "/CaveBuildVisualShellAudit.json";
        public const string FailingStagesPath = Folder + "/CaveBuildFailingStages.json";
        public const string MeatLoopHistoryPath = Folder + "/CaveBuildMeatLoopHistory.json";
        public const string LadderContextPath = Folder + "/CaveBuildLadderContext.json";

        const int MaxHistoryEntries = 4;

        public static void Export(
            CaveBuildQualityReport report,
            Transform caveRoot,
            int meatLoopPass = -1,
            SceneGroundInfo ground = null)
        {
            if (report == null)
                return;

            EnsureFolder();
            WriteVisualShellAudit(report, caveRoot, meatLoopPass);
            WriteFailingStages(report, meatLoopPass);
            WriteLadderContext(report, caveRoot, ground, meatLoopPass);
            var activeRung = CaveBuildPromptLadder.PickActiveRung(report, caveRoot, ground);
            CaveBuildResearchExporter.Write(report, activeRung, meatLoopPass);
            AppendMeatLoopHistory(report, meatLoopPass);
            if (caveRoot != null && CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                CaveRouteProbeRunner.Export(CaveRouteProbeRunner.Run(caveRoot), caveRoot);
                CaveCombatProbeRunner.Export(CaveCombatProbeRunner.Run(caveRoot), caveRoot);
            }

            if (!CaveBuildActionPacing.IsInsideQueueInvoke)
                CaveBuildDeferredAssetRefresh.RequestRefresh();
        }

        public static void EnsureFolderPublic() => EnsureFolder();

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
        }

        static void WriteVisualShellAudit(CaveBuildQualityReport report, Transform caveRoot, int meatLoopPass)
        {
            var layoutProto = report.LayoutPrototypeMode;
            var compact = !layoutProto && IsCompactRouteBuild(caveRoot);
            var audit = caveRoot != null ? CaveBuildVisualShellAuditor.Audit(caveRoot) : default;
            audit.CollectIssues(compact, layoutProto);
            var computed = audit.ComputeScore(compact, layoutProto);
            var rubricScore = CaveBuildQualityRubric.GetStageScore(report, "visual_shell");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{Escape(report.SceneName)}\",");
            sb.AppendLine($"  \"gradingMode\": \"{Escape(report.GradingMode)}\",");
            sb.AppendLine($"  \"meatLoopPass\": {meatLoopPassJson(meatLoopPass)},");
            sb.AppendLine($"  \"layoutPrototype\": {(layoutProto ? "true" : "false")},");
            sb.AppendLine($"  \"compactRoute\": {(compact ? "true" : "false")},");
            sb.AppendLine($"  \"computedVisualScore\": {computed},");
            sb.AppendLine($"  \"rubricVisualShellScore\": {rubricScore},");
            sb.AppendLine($"  \"layeredSlabCount\": {audit.LayeredSlabCount},");
            sb.AppendLine($"  \"legacySplineLayerCount\": {audit.LegacySplineLayerCount},");
            sb.AppendLine($"  \"visibleFlatPlatformCount\": {audit.VisibleFlatPlatformCount},");
            sb.AppendLine($"  \"blockRingCount\": {audit.BlockRingCount},");
            sb.AppendLine($"  \"caveBlockCount\": {audit.CaveBlockCount},");
            sb.AppendLine($"  \"blocksPerRingAvg\": {audit.BlocksPerRingAvg:F2},");
            sb.AppendLine($"  \"hasRouteTerrainFloor\": {(audit.HasRouteTerrainFloor ? "true" : "false")},");
            sb.AppendLine($"  \"hasSingleRouteCeiling\": {(audit.HasSingleRouteCeiling ? "true" : "false")},");
            sb.AppendLine($"  \"stackedCeilingSlabCount\": {audit.StackedCeilingSlabCount},");
            sb.AppendLine($"  \"hasLayoutWalkFloor\": {(audit.HasLayoutWalkFloor ? "true" : "false")},");
            sb.AppendLine($"  \"hasAdventureShell\": {(audit.HasAdventureShell ? "true" : "false")},");
            sb.AppendLine("  \"issues\": [");
            WriteStringArray(sb, audit.Issues);
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(VisualShellAuditPath, sb.ToString());
        }

        static void WriteFailingStages(CaveBuildQualityReport report, int meatLoopPass)
        {
            var failing = new List<CaveBuildStageGrade>();
            foreach (var stage in report.Stages)
            {
                if (stage != null && stage.Score < CaveBuildQualityRubric.StagePassScore)
                    failing.Add(stage);
            }

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{Escape(report.SceneName)}\",");
            sb.AppendLine($"  \"letterGrade\": \"{Escape(report.LetterGrade)}\",");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"gradingMode\": \"{Escape(report.GradingMode)}\",");
            sb.AppendLine($"  \"meatLoopPass\": {meatLoopPassJson(meatLoopPass)},");
            sb.AppendLine($"  \"isDud\": {(report.IsDud ? "true" : "false")},");
            sb.AppendLine($"  \"recommendedAction\": \"{report.RecommendedAction}\",");
            sb.AppendLine("  \"dudReasons\": [");
            WriteStringArray(sb, report.DudReasons);
            sb.AppendLine("  ],");
            sb.AppendLine("  \"stages\": [");

            for (var i = 0; i < failing.Count; i++)
            {
                var s = failing[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{Escape(s.StageId)}\",");
                sb.AppendLine($"      \"name\": \"{Escape(s.StageName)}\",");
                sb.AppendLine($"      \"score\": {s.Score},");
                sb.AppendLine($"      \"weight\": {s.Weight},");
                sb.AppendLine($"      \"passed\": {(s.Passed ? "true" : "false")},");
                sb.AppendLine("      \"issues\": [");
                WriteStringArray(sb, s.Issues, indent: "        ");
                sb.AppendLine("      ],");
                sb.AppendLine("      \"fixes\": [");
                WriteStringArray(sb, s.Fixes, indent: "        ");
                sb.AppendLine("      ]");
                sb.Append("    }");
                if (i < failing.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(FailingStagesPath, sb.ToString());
        }

        static void WriteLadderContext(
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground,
            int meatLoopPass)
        {
            var audit = caveRoot != null ? CaveBuildVisualShellAuditor.Audit(caveRoot) : default;
            var activeRung = CaveBuildPromptLadder.PickActiveRung(report, caveRoot, ground, audit);
            var failingRungs = CaveBuildPromptLadder.GetFailingRungs(report, caveRoot, ground, audit);
            var expectedY = CaveGroundPlacementUtility.ExpectedRootWorldY(ground);
            var actualY = caveRoot != null ? caveRoot.position.y : 0f;
            var depthErr = caveRoot != null && ground != null
                ? CaveGroundPlacementUtility.MeasureRootDepthError(caveRoot, ground)
                : 0f;

            var spawn = caveRoot != null ? caveRoot.Find("Entrance/CaveEntrance_SpawnPoint") : null;
            var spawnWorld = spawn != null
                ? $"{{\"x\":{spawn.position.x:F2},\"y\":{spawn.position.y:F2},\"z\":{spawn.position.z:F2}}}"
                : "null";
            var portalLinked = GameObject.Find("PortalFive") != null || GameObject.Find("MainScene_CavePortal") != null;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{Escape(report.SceneName)}\",");
            sb.AppendLine($"  \"meatLoopPass\": {meatLoopPassJson(meatLoopPass)},");
            if (meatLoopPass >= 0)
            {
                var mission = CaveBuildMeatLoopPassPlan.GetMission(meatLoopPass);
                sb.AppendLine($"  \"meatPassTitle\": \"{Escape(mission.Title)}\",");
                sb.AppendLine($"  \"meatPassResearchFocus\": \"{Escape(mission.ResearchFocus)}\",");
                sb.AppendLine($"  \"meatPassEnrichment\": \"{mission.Enrichment}\",");
            }

            sb.AppendLine($"  \"activeRung\": \"{Escape(activeRung)}\",");
            sb.AppendLine($"  \"chainCap\": {CaveBuildPromptLadder.MaxChainPerMeatPass},");
            sb.AppendLine($"  \"expectedRootWorldY\": {expectedY:F3},");
            sb.AppendLine($"  \"actualRootWorldY\": {actualY:F3},");
            sb.AppendLine($"  \"rootDepthErrorMeters\": {depthErr:F3},");
            sb.AppendLine($"  \"undergroundDepthMeters\": {CaveGeometryPaths.UndergroundDepthMeters},");
            sb.AppendLine($"  \"surfaceY\": {(ground != null ? ground.SurfaceY : 0f):F3},");
            sb.AppendLine($"  \"caveEntranceSpawnWorld\": \"{Escape(spawnWorld)}\",");
            sb.AppendLine($"  \"portalPresentInScene\": {(portalLinked ? "true" : "false")},");
            sb.AppendLine("  \"liveManifestPaths\": [");
            var manifestPaths = CaveBuildRungPromptExporter.LiveManifestPaths;
            for (var p = 0; p < manifestPaths.Length; p++)
                sb.AppendLine($"    \"{Escape(manifestPaths[p])}\"{(p < manifestPaths.Length - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"rungOrder\": [");
            for (var r = 0; r < CaveBuildPromptLadder.AllRungsIncludingWorkflow.Length; r++)
            {
                var id = CaveBuildPromptLadder.AllRungsIncludingWorkflow[r];
                sb.AppendLine($"    \"{id}\"{(r < CaveBuildPromptLadder.AllRungsIncludingWorkflow.Length - 1 ? "," : "")}");
            }

            sb.AppendLine("  ],");
            sb.AppendLine("  \"failingRungs\": [");
            for (var f = 0; f < failingRungs.Count; f++)
                sb.AppendLine($"    \"{Escape(failingRungs[f])}\"{(f < failingRungs.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine($"  \"webResearchRequired\": true,");
            sb.AppendLine($"  \"minResearchYear\": {CaveBuildResearchSources.MinResearchYear},");
            sb.AppendLine($"  \"workflowHint\": \"{Escape(CaveBuildResearchSources.WorkflowHint)}\",");
            sb.AppendLine("  \"researchUrls\": [");
            for (var u = 0; u < CaveBuildResearchSources.All.Length; u++)
                sb.AppendLine($"    \"{Escape(CaveBuildResearchSources.All[u])}\"{(u < CaveBuildResearchSources.All.Length - 1 ? "," : "")}");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(LadderContextPath, sb.ToString());
        }

        static void AppendMeatLoopHistory(CaveBuildQualityReport report, int meatLoopPass)
        {
            var history = LoadHistory();
            var topFails = new List<string>();
            foreach (var stage in report.Stages)
            {
                if (stage == null || stage.Score >= CaveBuildQualityRubric.StagePassScore)
                    continue;
                topFails.Add($"{stage.StageId}:{stage.Score}");
            }

            history.Insert(0, new HistoryEntry
            {
                pass = meatLoopPass,
                gradingMode = report.GradingMode ?? string.Empty,
                letterGrade = report.LetterGrade ?? string.Empty,
                overallScore = report.OverallScore,
                visualShellScore = CaveBuildQualityRubric.GetStageScore(report, "visual_shell"),
                isDud = report.IsDud,
                recommendedAction = report.RecommendedAction.ToString(),
                topFailingStages = topFails.ToArray(),
                dudReasons = report.DudReasons?.ToArray() ?? Array.Empty<string>(),
                utc = DateTime.UtcNow.ToString("o"),
            });

            while (history.Count > MaxHistoryEntries)
                history.RemoveAt(history.Count - 1);

            WriteHistory(history);
        }

        static List<HistoryEntry> LoadHistory()
        {
            if (!File.Exists(MeatLoopHistoryPath))
                return new List<HistoryEntry>();

            try
            {
                var wrapper = JsonUtility.FromJson<HistoryWrapper>(File.ReadAllText(MeatLoopHistoryPath));
                if (wrapper?.entries == null || wrapper.entries.Length == 0)
                    return new List<HistoryEntry>();
                return new List<HistoryEntry>(wrapper.entries);
            }
            catch
            {
                return new List<HistoryEntry>();
            }
        }

        static void WriteHistory(List<HistoryEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"entries\": [");
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"pass\": {meatLoopPassJson(e.pass)},");
                sb.AppendLine($"      \"gradingMode\": \"{Escape(e.gradingMode)}\",");
                sb.AppendLine($"      \"letterGrade\": \"{Escape(e.letterGrade)}\",");
                sb.AppendLine($"      \"overallScore\": {e.overallScore},");
                sb.AppendLine($"      \"visualShellScore\": {e.visualShellScore},");
                sb.AppendLine($"      \"isDud\": {(e.isDud ? "true" : "false")},");
                sb.AppendLine($"      \"recommendedAction\": \"{Escape(e.recommendedAction)}\",");
                sb.AppendLine($"      \"utc\": \"{Escape(e.utc)}\",");
                sb.AppendLine("      \"topFailingStages\": [");
                WriteStringArray(sb, e.topFailingStages, indent: "        ");
                sb.AppendLine("      ],");
                sb.AppendLine("      \"dudReasons\": [");
                WriteStringArray(sb, e.dudReasons, indent: "        ");
                sb.AppendLine("      ]");
                sb.Append("    }");
                if (i < entries.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(MeatLoopHistoryPath, sb.ToString());
        }

        static bool IsCompactRouteBuild(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            var platformsRoot = geometry != null
                ? geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName)
                : null;
            var platforms = platformsRoot != null ? platformsRoot.childCount : 0;
            var shell = geometry != null
                ? geometry.Find(CaveAdventureShellBuilder.ShellRootName)
                : null;
            return platforms >= 6 && shell == null;
        }

        static void WriteStringArray(StringBuilder sb, IList<string> items, string indent = "    ")
        {
            if (items == null || items.Count == 0)
                return;

            for (var i = 0; i < items.Count; i++)
            {
                sb.Append($"{indent}\"{Escape(items[i])}\"");
                if (i < items.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
        }

        static void WriteStringArray(StringBuilder sb, string[] items, string indent = "    ") =>
            WriteStringArray(sb, (IList<string>)items, indent);

        static string meatLoopPassJson(int pass) => pass >= 0 ? pass.ToString() : "-1";

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        [Serializable]
        class HistoryWrapper
        {
            public HistoryEntry[] entries = Array.Empty<HistoryEntry>();
        }

        [Serializable]
        class HistoryEntry
        {
            public int pass = -1;
            public string gradingMode;
            public string letterGrade;
            public int overallScore;
            public int visualShellScore;
            public bool isDud;
            public string recommendedAction;
            public string[] topFailingStages;
            public string[] dudReasons;
            public string utc;
        }
    }

    /// <summary>
    /// Clears stale Generated JSON/MD before each build or Cursor invoke so agents are not confused by prior runs.
    /// </summary>
    static class CaveBuildAgentArtifacts
    {
        public const string SessionManifestPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildAgentSession.json";
        public const string TailoredPromptPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildTailoredAgentPrompt.md";
        const string ActiveRungPromptPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildActiveRungPrompt.md";
        const string LegacyAgentPromptPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildAgentPrompt.md";

        public static readonly string[] RequiredJsonForAgent =
        {
            "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
            CaveBuildAgentContextExporter.VisualShellAuditPath,
            CaveBuildAgentContextExporter.FailingStagesPath,
            CaveBuildAgentContextExporter.LadderContextPath,
            CaveBuildAgentContextExporter.MeatLoopHistoryPath,
            CaveBuildResearchExporter.ResearchPath,
            CaveBuildResearchCacheBridge.GeneratedPointerPath,
            CaveBuildResearchCacheBridge.ExecutionBriefPath,
            CaveBuildWorkflowExporter.WorkflowPath,
            CaveBuildCompileGate.DiagnosticsPath,
            CaveRouteProbeRunner.ReportPath,
            CaveCombatProbeRunner.ReportPath,
            CaveLiveCodegenRequest.ExportPath,
            CaveBuildResearchPhase.EnrichmentPath,
            CaveBuildResearchNeedsAnalyzer.NeedsPath,
            CaveBuildQualitySystem.ManifestPath,
            SessionManifestPath,
        };

        static readonly string[] StaleFilesToDeleteOnBuildStart =
        {
            TailoredPromptPath,
            ActiveRungPromptPath,
            LegacyAgentPromptPath,
            "Assets/EnvironmentKit/Generated/CaveBuildMeatLoopAgentPrompt.md",
            CaveLiveCodegenRequest.ExportPath,
            CaveRouteProbeRunner.ReportPath,
            CaveCombatProbeRunner.ReportPath,
            CaveBuildAgentContextExporter.VisualShellAuditPath,
            CaveBuildAgentContextExporter.FailingStagesPath,
            CaveBuildAgentContextExporter.LadderContextPath,
            CaveBuildResearchPhase.EnrichmentPath,
            CaveBuildResearchNeedsAnalyzer.NeedsPath,
            CaveBuildResearchCacheBridge.ExecutionBriefPath,
            SessionManifestPath,
        };

        /// <summary>Removes legacy per-phase prompt/digest markdown (and Unity .meta) from Generated/.</summary>
        public static int PurgeStaleGeneratedPhasePrompts()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var cleared = new List<string>();
            PurgeLegacyPerPhaseMarkdown(hub, cleared);
            if (cleared.Count > 0)
            {
                Debug.Log(
                    $"{CaveBuildPipelineDomains.Cave} Purged {cleared.Count} stale Generated prompt/digest file(s). " +
                    $"Canonical: {CaveBuildPhasePromptBridge.ActivePhasePromptPath}, {CaveBuildPhasePromptBridge.PhaseDataDigestPath}");
            }

            return cleared.Count;
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Purge Stale Generated Prompts", false, 6)]
        public static void PurgeStaleGeneratedPromptsMenu()
        {
            var count = PurgeStaleGeneratedPhasePrompts();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Cave build — purge stale prompts",
                count > 0
                    ? $"Removed {count} legacy file(s) under Assets/EnvironmentKit/Generated/.\n\n" +
                      "Kept: CaveBuildActivePhasePrompt.md, CaveBuildPhaseDataDigest.md."
                    : "No legacy CaveBuildPhasePrompt_*.md or CaveBuildPhaseDataDigest_*.md files found.",
                "OK");
        }

        public static void ResetForNewBuildSession(string sceneName)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var cleared = new List<string>();
            PurgeLegacyPerPhaseMarkdown(hub, cleared);
            foreach (var rel in StaleFilesToDeleteOnBuildStart)
            {
                var full = Path.Combine(hub, rel);
                if (!File.Exists(full))
                    continue;
                try
                {
                    File.Delete(full);
                    cleared.Add(rel);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CaveBuild] Could not delete stale artifact {rel}: {ex.Message}");
                }
            }

            ResetMeatLoopHistory(hub);
            WriteSessionManifest(hub, sceneName, "build_start", cleared);
            Debug.Log(
                $"{CaveBuildPipelineDomains.Cave} Agent artifacts cleared ({cleared.Count} files). Fresh JSON this cave run. " +
                $"Session → {SessionManifestPath}");
        }

        public static void ResetPromptsBeforeCursorInvoke(string sceneName, string rung, int meatLoopPass)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var cleared = new List<string>();
            foreach (var rel in new[] { TailoredPromptPath, ActiveRungPromptPath, LegacyAgentPromptPath })
            {
                var full = Path.Combine(hub, rel);
                if (!File.Exists(full))
                    continue;
                try
                {
                    File.Delete(full);
                    cleared.Add(rel);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CaveBuild] Could not delete prompt {rel}: {ex.Message}");
                }
            }

            WriteSessionManifest(hub, sceneName, $"cursor_invoke_{rung}", cleared, rung, meatLoopPass);
        }

        static void PurgeLegacyPerPhaseMarkdown(string hub, List<string> cleared)
        {
            var genDir = Path.Combine(hub, CaveBuildAgentContextExporter.Folder);
            if (!Directory.Exists(genDir))
                return;

            var keepPrompt = Path.GetFileName(CaveBuildPhasePromptBridge.ActivePhasePromptPath);
            var keepDigest = Path.GetFileName(CaveBuildPhasePromptBridge.PhaseDataDigestPath);

            foreach (var full in Directory.GetFiles(genDir))
            {
                var name = Path.GetFileName(full);
                if (name == null || !IsLegacyPerPhaseArtifact(name, keepPrompt, keepDigest))
                    continue;

                try
                {
                    File.Delete(full);
                    cleared.Add(Path.Combine(CaveBuildAgentContextExporter.Folder, name).Replace('\\', '/'));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CaveBuild] Could not delete legacy artifact {name}: {ex.Message}");
                }
            }
        }

        static bool IsLegacyPerPhaseArtifact(string fileName, string keepPrompt, string keepDigest)
        {
            var baseName = fileName;
            if (baseName.EndsWith(".meta", StringComparison.Ordinal))
                baseName = baseName.Substring(0, baseName.Length - ".meta".Length);

            if (baseName.Equals(keepPrompt, StringComparison.Ordinal) ||
                baseName.Equals(keepDigest, StringComparison.Ordinal))
                return false;

            var legacyPrompt =
                baseName.StartsWith("CaveBuildPhasePrompt_", StringComparison.Ordinal) &&
                baseName.EndsWith(".md", StringComparison.Ordinal);
            var legacyDigest =
                baseName.StartsWith("CaveBuildPhaseDataDigest_", StringComparison.Ordinal) &&
                baseName.EndsWith(".md", StringComparison.Ordinal);
            if (!legacyPrompt && !legacyDigest)
                return false;

            return fileName.EndsWith(".md", StringComparison.Ordinal) ||
                   fileName.EndsWith(".md.meta", StringComparison.Ordinal);
        }

        static void ResetMeatLoopHistory(string hub)
        {
            var path = Path.Combine(hub, CaveBuildAgentContextExporter.MeatLoopHistoryPath);
            try
            {
                File.WriteAllText(
                    path,
                    "{\n  \"clearedUtc\": \"" + DateTime.UtcNow.ToString("o") + "\",\n  \"entries\": []\n}\n");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Could not reset meat loop history: " + ex.Message);
            }
        }

        static void WriteSessionManifest(
            string hub,
            string sceneName,
            string phase,
            List<string> cleared,
            string rung = null,
            int meatLoopPass = -1)
        {
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"sessionId\": \"{sessionId}\",");
            sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"scene\": \"{Escape(sceneName ?? "")}\",");
            sb.AppendLine($"  \"phase\": \"{Escape(phase)}\",");
            sb.AppendLine($"  \"activeRung\": \"{Escape(rung ?? "")}\",");
            sb.AppendLine($"  \"meatLoopPass\": {meatLoopPass},");
            sb.AppendLine($"  \"tailoredPrompt\": \"{TailoredPromptPath}\",");
            sb.AppendLine(
                "  \"policy\": \"Only use JSON from this session; ignore artifacts from prior builds not listed in requiredJson.\",");
            sb.AppendLine("  \"clearedFiles\": [");
            for (var i = 0; i < cleared.Count; i++)
                sb.AppendLine($"    \"{Escape(cleared[i])}\"{(i < cleared.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"requiredJson\": [");
            for (var i = 0; i < RequiredJsonForAgent.Length; i++)
                sb.AppendLine(
                    $"    \"{Escape(RequiredJsonForAgent[i])}\"{(i < RequiredJsonForAgent.Length - 1 ? "," : "")}");
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(Path.Combine(hub, SessionManifestPath), sb.ToString());
        }

        static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
