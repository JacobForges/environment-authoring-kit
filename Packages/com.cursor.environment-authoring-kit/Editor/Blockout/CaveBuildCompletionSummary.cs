#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>One start confirm + one finish readout for Build Complete Cave Level (no duplicate dialogs).</summary>
    public static class CaveBuildCompletionSummary
    {
        public const string ReadoutRelativePath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildCompletionReadout.md";

        public static string LastReadoutFullPath { get; private set; }

        public static bool ConfirmStartBuild(
            SceneGroundInfo ground,
            string portalName,
            CaveLayoutRoll roll,
            SurfaceBuildScope surfaceScope,
            out bool cancelled)
        {
            cancelled = true;
            var hubOpen = Resources.FindObjectsOfTypeAll<EnvironmentKitHubWindow>().Length > 0;
            if (hubOpen)
            {
                Debug.Log(
                    $"[CaveBuild] Hub-driven start (no start popup). Scope={surfaceScope}, " +
                    $"Ground={(ground.HasAnchor ? ground.Anchor.name : "(none)")}, Portal={portalName}.");
                CaveBuildDialogPolicy.BeginUnifiedSession();
                cancelled = false;
                return true;
            }

            var sceneName = SceneManager.GetActiveScene().name;
            var seedNote = EditorPrefs.GetBool("CaveBuild_RandomizeEachTime", true)
                ? "New random layout seed this run."
                : $"Fixed seed {EditorPrefs.GetInt("CaveBuild_FixedSeed", 0)}.";

            var scopeNote = surfaceScope switch
            {
                SurfaceBuildScope.SurfaceOnly =>
                    "• Open-sky surface ONLY (trails, roads, water, mountains, cave mouth markers)\n" +
                    "• Does NOT rebuild UndergroundCaveSystem\n",
                SurfaceBuildScope.CaveOnly =>
                    "• Underground cave ONLY (uses existing surface + opening markers)\n" +
                    "• Does NOT regenerate surface world\n",
                _ =>
                    "• Open-sky surface from Ground center (5 equal passes + 1 trail axis)\n" +
                    "• Then full underground cave pipeline\n",
            };

            var intro =
                $"Scene: {sceneName}\n" +
                $"Scope: {surfaceScope}\n" +
                $"Ground center: {(ground.HasAnchor ? ground.Anchor.name : "(none)")}\n" +
                $"Portal: {portalName}\n" +
                $"{seedNote}\n\n" +
                scopeNote +
                "\nRun starts now and continues automatically until completion:\n" +
                (surfaceScope == SurfaceBuildScope.SurfaceOnly
                    ? "• Radial terrain, trails, water, cave opening markers\n"
                    : $"• Pre-build readiness\n• {CaveBuildUnifiedFlow.QueuedPipelineStepCount}-step cave pipeline\n") +
                "• Exports JSON/MD under Assets/EnvironmentKit/Generated/\n" +
                "• Live status and data are visible in Environment Kit Hub\n\n" +
                "Save the scene when finished.";

            var title = surfaceScope switch
            {
                SurfaceBuildScope.SurfaceOnly => "Build Surface World Only",
                SurfaceBuildScope.CaveOnly => "Build Cave Only (Align to Surface)",
                _ => "Build Complete Cave Level (Surface + Cave)",
            };

            if (!EditorUtility.DisplayDialog(title, intro, "Start Build", "Cancel"))
                return false;

            CaveBuildDialogPolicy.BeginUnifiedSession();
            cancelled = false;
            return true;
        }

        public static void WriteCompletionArtifacts(
            string sceneName,
            LavaTubeCaveBuildReport buildReport,
            CaveLayoutRoll roll,
            CaveBuildQualityReport quality,
            out string readoutPath,
            out string shortDialogMessage)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            readoutPath = Path.Combine(projectRoot, ReadoutRelativePath);
            LastReadoutFullPath = readoutPath;

            var body = BuildReadoutMarkdown(sceneName, buildReport, roll, quality, projectRoot);
            EnsureGeneratedFolder();
            File.WriteAllText(readoutPath, body);
            shortDialogMessage = BuildShortDialogMessage(sceneName, buildReport, roll, quality, readoutPath);
        }

        public static void ShowFinished(
            string sceneName,
            LavaTubeCaveBuildReport buildReport,
            CaveLayoutRoll roll,
            CaveBuildQualityReport quality = null,
            bool showDialog = true)
        {
            WriteCompletionArtifacts(sceneName, buildReport, roll, quality, out var readoutPath, out var shortMsg);

            var seed = roll?.Seed ?? 0;
            string prefabNote = null;
            if (CaveBuildGenerationPrefabExporter.TryExportWhenPipelineFinished(
                    sceneName,
                    seed,
                    quality,
                    "build_complete_dialog",
                    out var prefabPath,
                    out var prefabMsg))
            {
                prefabNote = $"\n\nGeneration prefab:\n{ToFileUrl(prefabPath)}";
                Debug.Log("[CaveBuild] " + prefabMsg);
            }
            else if (!string.IsNullOrEmpty(prefabMsg) &&
                     !prefabMsg.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            {
                if (prefabMsg.Contains("Deferred", StringComparison.OrdinalIgnoreCase))
                {
                    var scene = sceneName;
                    var q = quality;
                    EditorApplication.delayCall += () =>
                    {
                        if (CaveBuildGenerationPrefabExporter.TryExportWhenPipelineFinished(
                                scene,
                                seed,
                                q,
                                "build_complete_delayed",
                                out var delayedPath,
                                out var delayedMsg))
                            Debug.Log("[CaveBuild] " + delayedMsg + " " + delayedPath);
                    };
                }

                Debug.Log("[CaveBuild] Generation prefab: " + prefabMsg);
            }

            if (!string.IsNullOrEmpty(prefabNote))
                shortMsg += prefabNote;

            var title = buildReport != null && buildReport.QualityAcceptable
                ? "Build Complete — Finished"
                : "Build Complete — Review";

            if (showDialog)
                EditorUtility.DisplayDialog(title, shortMsg, "OK");
            else
                Debug.Log($"[CaveBuild] Completion (dialog deferred): {title}\n{shortMsg}");
            if (quality != null)
            {
                foreach (var b in quality.ShipBlockers)
                    Debug.LogWarning("[CaveBuild] Ship blocker: " + b);
            }

            Debug.Log($"[CaveBuild] Completion readout:\n{readoutPath}\n\n{shortMsg}");
            CaveBuildPipelineLog.Info(shortMsg.Replace("\n", " | "), "Completion");
            CaveBuildUnifiedFlow.LogFlowComplete(buildReport);
            CaveBuildDialogPolicy.EndUnifiedSessionWhenAgentIdle();
        }

        public static void ShowBlocked(string reason, string extraPath = null)
        {
            CaveBuildDialogPolicy.EndUnifiedSession();
            var msg = reason;
            if (!string.IsNullOrEmpty(extraPath))
                msg += $"\n\nSee:\n{ToFileUrl(extraPath)}";
            EditorUtility.DisplayDialog("Build Complete — Not Started", msg, "OK");
            Debug.LogWarning("[CaveBuild] " + reason);
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Open Last Build Completion Readout")]
        public static void OpenLastReadoutMenu()
        {
            if (string.IsNullOrEmpty(LastReadoutFullPath) || !File.Exists(LastReadoutFullPath))
            {
                var fallback = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                    ReadoutRelativePath);
                if (!File.Exists(fallback))
                {
                    EditorUtility.DisplayDialog(
                        "Build Readout",
                        "No completion readout yet. Run Build Complete Cave Level first.",
                        "OK");
                    return;
                }

                LastReadoutFullPath = fallback;
            }

            EditorUtility.RevealInFinder(LastReadoutFullPath);
        }

        static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder(CaveBuildAgentContextExporter.Folder))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
        }

        static string BuildShortDialogMessage(
            string sceneName,
            LavaTubeCaveBuildReport buildReport,
            CaveLayoutRoll roll,
            CaveBuildQualityReport quality,
            string readoutPath)
        {
            buildReport ??= new LavaTubeCaveBuildReport { Message = "No build report." };
            string grade;
            bool pass;
            if (quality != null)
            {
                pass = quality.BuildAcceptable && quality.MeetsShipTarget;
                grade = quality.WeightedOverallScore != quality.OverallScore
                    ? $"{quality.LetterGrade} ({quality.OverallScore}/100 gate, weighted {quality.WeightedOverallScore})"
                    : $"{quality.LetterGrade} ({quality.OverallScore}/100)";
            }
            else
            {
                grade = $"{buildReport.QualityLetter} ({buildReport.QualityScore}/100)";
                pass = buildReport.QualityAcceptable;
            }

            return
                $"Scene '{sceneName}' — {(pass ? "PASS" : "NEEDS WORK")}\n" +
                $"Grade: {grade}\n" +
                $"{buildReport.Message}\n\n" +
                $"Pieces: {buildReport.PieceCount}, shell: {buildReport.ShellPieceCount}, " +
                $"NavMesh: {buildReport.NavMeshBuilt}\n\n" +
                $"Full debug readout (paths, file:// URLs, failing stages, prompts):\n" +
                $"{ToFileUrl(ReadoutRelativePath)}\n\n" +
                $"Finder: {readoutPath}\n\n" +
                "Menu: Cave Build → Diagnostics → Open Last Build Completion Readout";
        }

        static string BuildReadoutMarkdown(
            string sceneName,
            LavaTubeCaveBuildReport buildReport,
            CaveLayoutRoll roll,
            CaveBuildQualityReport quality,
            string projectRoot)
        {
            buildReport ??= new LavaTubeCaveBuildReport();
            var sb = new StringBuilder();
            sb.AppendLine("# Cave Build Completion Readout");
            sb.AppendLine();
            sb.AppendLine($"**Scene:** {sceneName}");
            sb.AppendLine($"**Time (UTC):** {System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**Layout:** {roll}");
            sb.AppendLine();

            if (quality != null)
            {
                sb.AppendLine("## Quality");
                sb.AppendLine($"- Grade: **{quality.LetterGrade}** ({quality.OverallScore}/100)");
                if (quality.WeightedOverallScore != quality.OverallScore)
                    sb.AppendLine($"- Weighted average: **{quality.WeightedOverallScore}/100** (before critical-stage gate)");
                sb.AppendLine($"- Ship target: {CaveBuildQualityRubric.TargetGrade} ({CaveBuildQualityRubric.ShipScore}+)");
                sb.AppendLine($"- Meets Beta+: {quality.MeetsBetaTarget} | Meets Ship: {quality.MeetsShipTarget}");
                sb.AppendLine($"- Acceptable: {quality.BuildAcceptable}");
                sb.AppendLine($"- Dud: {quality.IsDud}");
                if (quality.ShipBlockers.Count > 0)
                {
                    sb.AppendLine("- Ship blockers:");
                    foreach (var b in quality.ShipBlockers)
                        sb.AppendLine($"  - {b}");
                }
                if (quality.DudReasons.Count > 0)
                {
                    sb.AppendLine("- Dud reasons:");
                    foreach (var r in quality.DudReasons)
                        sb.AppendLine($"  - {r}");
                }

                sb.AppendLine();
                sb.AppendLine("### Failing stages");
                var anyFail = false;
                foreach (var stage in quality.Stages)
                {
                    if (stage == null || stage.Score >= CaveBuildQualityRubric.StagePassScore)
                        continue;
                    anyFail = true;
                    sb.AppendLine($"- **{stage.StageId}** ({stage.Score}/100): {string.Join("; ", stage.Issues)}");
                }

                if (!anyFail)
                    sb.AppendLine("- (none — all stages at/above pass threshold)");
            }

            sb.AppendLine();
            sb.AppendLine("## Build metrics");
            sb.AppendLine($"- Message: {buildReport.Message}");
            sb.AppendLine($"- Pieces: {buildReport.PieceCount}, shell: {buildReport.ShellPieceCount}, minables: {buildReport.MinableCount}");
            sb.AppendLine($"- NavMesh: {buildReport.NavMeshBuilt}");
            sb.AppendLine();

            var existingReports = new List<string>();
            var missingReports = new List<string>();
            foreach (var rel in CollectReportPaths(quality))
            {
                if (File.Exists(Path.Combine(projectRoot, rel)))
                    existingReports.Add(rel);
                else
                    missingReports.Add(rel);
            }

            sb.AppendLine("## Report files (project-relative)");
            foreach (var rel in existingReports)
                sb.AppendLine($"- `{rel}`");
            if (existingReports.Count == 0)
                sb.AppendLine("- (no report files found)");

            if (missingReports.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Expected but not generated this run");
                foreach (var rel in missingReports)
                    sb.AppendLine($"- `{rel}`");
            }

            sb.AppendLine();
            sb.AppendLine("## file:// URLs (open in browser or Cursor)");
            foreach (var rel in existingReports)
                sb.AppendLine($"- [{Path.GetFileName(rel)}]({ToFileUrl(rel)}) — ok");

            sb.AppendLine();
            sb.AppendLine("## Prompt / agent tuning (tailored per pass)");
            sb.AppendLine($"- **Tailored prompt (use this):** `{CaveBuildRungPromptExporter.TailoredPromptPath}`");
            sb.AppendLine($"- Session manifest: `{CaveBuildAgentArtifacts.SessionManifestPath}`");
            sb.AppendLine($"- Active rung copy: `{CaveBuildRungPromptExporter.ActiveRungPromptPath}`");
            sb.AppendLine($"- Tools ladder: `Tools/cave-grader/prompt-ladder/`");
            sb.AppendLine($"- Cursor settings asset: `{CaveBuildCursorSettings.AssetPath}`");
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            sb.AppendLine($"- autoInvokeAfterEveryBuild: {settings.autoInvokeAfterEveryBuild}");
            sb.AppendLine($"- autoInvokeOnDud: {settings.autoInvokeOnDud}");
            sb.AppendLine($"- autoInvokePreBuildWorkflow: {settings.autoInvokePreBuildWorkflow}");
            sb.AppendLine($"- enforcePreBuildGate: {settings.enforcePreBuildGate}");

            sb.AppendLine();
            sb.AppendLine("## Next steps");
            sb.AppendLine("1. **CaveBuildNextStepsPrompt.md** + **CaveBuildDoNotPrompt.md** (autonomous loop).");
            sb.AppendLine("2. Active phase: **CaveBuildActivePhasePrompt.md** + **CaveBuildTailoredAgentPrompt.md** + **CaveBuildUnifiedAgentPrompt.md**.");
            sb.AppendLine("3. Open failing-stage JSON, route/combat probes, and `CaveBuildAgentSession.json` requiredJson.");
            sb.AppendLine("4. Re-run **Build Complete Cave Level** from Hub (Hub mode skips start popup; finish readout only).");

            return sb.ToString();
        }

        static IEnumerable<string> CollectReportPaths(CaveBuildQualityReport quality)
        {
            yield return quality?.ExportPath ?? "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json";
            yield return CaveBuildQualitySystem.ManifestPath;
            yield return CaveRouteProbeRunner.ReportPath;
            yield return SurfaceRouteProbeRunner.ReportPath;
            yield return SurfacePlaytestValidator.ReportPath;
            yield return CaveBuildPhaseBotReport.ReportRel;
            yield return CaveBuildPhaseResearchGate.ActionPlanRel;
            yield return CaveBuildPhaseResearchGate.GateRel;
            yield return CaveCombatProbeRunner.ReportPath;
            yield return CaveLiveCodegenRequest.ExportPath;
            yield return CaveBuildAgentArtifacts.SessionManifestPath;
            yield return CaveBuildRungPromptExporter.TailoredPromptPath;
            yield return CaveBuildPhasePromptBridge.NextStepsPromptPath;
            yield return CaveBuildPhasePromptBridge.DoNotPromptPath;
            yield return CaveBuildPhasePromptBridge.ActivePhasePromptPath;
            yield return CaveBuildPhasePromptBridge.PhaseDataDigestPath;
            yield return CaveBuildUnifiedPromptBridge.UnifiedPromptRel;
            yield return CaveBuildPhasePromptBridge.PhasePromptsIndexPath;
            yield return CaveBuildPhasePromptBridge.AutonomousIterationPath;
            yield return CaveBuildResearchPhase.EnrichmentPath;
            yield return CaveBuildResearchNeedsAnalyzer.NeedsPath;
            yield return CaveBuildPreBuildLadder.ReportPath;
            yield return CaveBuildPreBuildLadder.ContextPath;
            yield return CaveBuildPreBuildWorkflowExporter.WorkflowPath;
            yield return CaveBuildAgentContextExporter.VisualShellAuditPath;
            yield return CaveBuildAgentContextExporter.FailingStagesPath;
            yield return CaveBuildAgentContextExporter.LadderContextPath;
            yield return CaveBuildAgentContextExporter.MeatLoopHistoryPath;
            yield return CaveBuildResearchExporter.ResearchPath;
            yield return CaveBuildResearchCacheBridge.GeneratedPointerPath;
            yield return CaveBuildResearchCacheBridge.CacheIndexPath;
            yield return CaveBuildWorkflowExporter.WorkflowPath;
            yield return CaveBuildCompileGate.DiagnosticsPath;
            yield return CaveBuildAgentMemoryExporter.MemoryPath;
            yield return CaveBuildBatchRunner.LogPath;
            yield return CaveBuildAaaFeatureGrader.ManifestPath;
            yield return ReadoutRelativePath;
        }

        static string ToFileUrl(string projectRelativePath)
        {
            var full = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, projectRelativePath));
            return "file://" + full.Replace(" ", "%20");
        }
    }
}
#endif
