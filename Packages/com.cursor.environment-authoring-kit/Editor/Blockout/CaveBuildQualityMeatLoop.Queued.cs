using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static partial class CaveBuildQualityMeatLoop
    {
        public enum QueuedMeatPhase
        {
            Purge = 0,
            GradeBatch0 = 1,
            GradeBatch1 = 2,
            GradeBatch2 = 3,
            GradeBatch3 = 4,
            GradeFinalize = 5,
            Fix = 6,
        }

        public sealed class QueuedMeatState
        {
            public Transform CaveRoot;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public LavaTubeCaveBuildReport BuildReport;
            public bool ShowProgress;
            public int Pass;
            public QueuedMeatPhase Phase = QueuedMeatPhase.Purge;
            public int GradeBatch;
            public int FixesApplied;
            public int GradePasses;
            public HashSet<string> Skipped = new();
            public CaveBuildQualityReport Quality;
            public bool Adventure;
            public bool LayoutPrototype;
            public bool MouthGrounded;
            public bool Done;
            public bool AwaitingHelperScripts;
            public int HelperScriptsPass = -1;
            public string LastFixRung;
            public int SameRungFixStreak;
            public int LastOverallScore = -1;
        }

        public static QueuedMeatState BeginQueued(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            bool showProgress) =>
            new QueuedMeatState
            {
                CaveRoot = caveRoot,
                Ground = ground,
                Request = request,
                BuildReport = buildReport,
                ShowProgress = showProgress,
                Adventure = CaveGeometryPaths.IsAdventureCave(caveRoot),
                LayoutPrototype = request != null && request.UseLayoutPrototype ||
                                  CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot),
            };

        /// <summary>One editor-queue tick. Returns true when the meat loop is finished.</summary>
        public static bool RunQueuedPhase(QueuedMeatState state, System.Action<int, string> onStep)
        {
            if (state == null || state.Done)
                return true;

            if (state.LayoutPrototype)
            {
                RunLayoutPrototypeOnly(state, onStep);
                state.Done = true;
                return true;
            }

            switch (state.Phase)
            {
                case QueuedMeatPhase.Purge:
                    if (state.HelperScriptsPass != state.Pass)
                    {
                        state.HelperScriptsPass = state.Pass;
                        state.AwaitingHelperScripts = true;
                        var helperCtx = new CaveBuildHelperScriptOrchestrator.Context
                        {
                            Request = state.Request,
                            MeatPass = state.Pass,
                            Rung = state.Quality != null
                                ? CaveBuildPromptLadder.PickActiveRung(state.Quality, state.CaveRoot, state.Ground)
                                : "visual_shell",
                        };
                        CaveBuildHelperScriptOrchestrator.Queue(
                            CaveBuildHelperScriptOrchestrator.Moment.CaveMeatPassStart,
                            helperCtx,
                            (_, _) => state.AwaitingHelperScripts = false);
                        return false;
                    }

                    if (state.AwaitingHelperScripts)
                        return false;

                    onStep?.Invoke(
                        state.Pass,
                        state.Pass == 0 ? "Phase: baseline purge (once)" : $"Phase: additive pass {state.Pass} (no purge)");
                    if (state.ShowProgress)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Cave Meat Loop",
                            state.Pass == 0 ? "Baseline purge (once)" : $"Additive pass {state.Pass}",
                            Mathf.Clamp01(state.Pass / (float)(AdventureMaxPasses + 2)));
                    }

                    if (state.Adventure && state.Pass == 0)
                    {
                        CaveCompactLayerPurge.PurgeShellLayersOnly(state.CaveRoot);
                        CaveEnclosureShellBuilder.HideRoutePlatformSlabs(state.CaveRoot);
                        CaveBuildPipelineLog.Info("Meat loop baseline shell purge (pass 0 only).", "MeatLoop");
                    }
                    else if (state.Pass > 0)
                    {
                        CaveBuildPipelineLog.Info(
                            $"Meat pass {state.Pass} — additive enrichment only (preserving prior work).",
                            "MeatLoop");
                    }

                    state.GradeBatch = 0;
                    state.Phase = QueuedMeatPhase.GradeBatch0;
                    return false;

                case QueuedMeatPhase.GradeBatch0:
                case QueuedMeatPhase.GradeBatch1:
                case QueuedMeatPhase.GradeBatch2:
                case QueuedMeatPhase.GradeBatch3:
                    if (state.Quality == null)
                        state.Quality = CaveBuildQualityGrader.BeginMeatLoopGradeReport(
                            state.CaveRoot, state.Ground, state.Request);
                    state.Quality.GradingVersion = CaveBuildQualitySystem.GradingVersion;

                    if (state.Pass > 0)
                    {
                        var mission = CaveBuildMeatLoopPassPlan.GetMission(state.Pass);
                        onStep?.Invoke(state.Pass, $"Grade: {mission.Title}");
                        CaveBuildMeatLoopPassGrader.MergePassGrades(
                            state.Quality,
                            state.CaveRoot,
                            state.Ground,
                            state.Request,
                            state.BuildReport,
                            state.Pass);
                        state.Phase = QueuedMeatPhase.GradeFinalize;
                        return false;
                    }

                    state.GradeBatch = (int)state.Phase - (int)QueuedMeatPhase.GradeBatch0;
                    onStep?.Invoke(
                        state.Pass,
                        $"Grade batch {state.GradeBatch + 1}/{CaveBuildQualityGrader.MeatLoopGradeBatchCount}");
                    state.Quality.GradingMode = "meat_loop_pass_baseline";
                    CaveBuildQualityGrader.AppendMeatLoopGradeBatch(
                        state.Quality,
                        state.CaveRoot,
                        state.Ground,
                        state.Request,
                        state.BuildReport,
                        state.GradeBatch);
                    state.Phase = state.GradeBatch >= CaveBuildQualityGrader.MeatLoopGradeBatchCount - 1
                        ? QueuedMeatPhase.GradeFinalize
                        : (QueuedMeatPhase)((int)state.Phase + 1);
                    return false;

                case QueuedMeatPhase.GradeFinalize:
                    onStep?.Invoke(state.Pass, "Grade finalize");
                    if (state.Quality == null)
                        state.Quality = CaveBuildQualityGrader.BeginMeatLoopGradeReport(
                            state.CaveRoot, state.Ground, state.Request);
                    state.Quality.AdventureMode = state.Adventure;
                    state.Quality.GradingVersion = CaveBuildQualitySystem.GradingVersion;
                    state.Quality.GradingMode = "meat_loop_pass";
                    CaveBuildQualitySystem.ApplyDudDetection(state.Quality, state.CaveRoot, state.Request);
                    state.Quality.RecalculateOverall();
                    state.GradePasses++;
                    CaveBuildQualitySystem.ApplyRecommendedAction(state.Quality, state.Request);
                    CaveBuildGradingManifestWriter.Write();
                    CaveBuildQualityReportWriter.Write(state.Quality, gradingMode: state.Quality.GradingMode);
                    CaveBuildQualitySystem.SetLastGradedReport(state.Quality);
                    CaveBuildAgentContextExporter.Export(
                        state.Quality, state.CaveRoot, meatLoopPass: state.Pass, state.Ground);
                    CaveBuildPipelineLog.Info(
                        $"Meat pass {state.Pass}: {state.Quality.LetterGrade} " +
                        $"({state.Quality.OverallScore}/100 weighted {state.Quality.WeightedOverallScore}) " +
                        $"— {CaveBuildMeatLoopPassPlan.GetMission(state.Pass).Title}");
                    var fixRung = CaveBuildPromptLadder.PickActiveRung(state.Quality, state.CaveRoot, state.Ground);
                    System.Environment.SetEnvironmentVariable("CAVE_FORCE_PROMPT_EXPORT", "1");
                    CaveBuildRungPromptExporter.EnsureTailoredPromptOnDisk(
                        fixRung, state.Quality, state.Pass);

                    if (CaveBuildQualityRubric.MeetsShipTarget(state.Quality))
                    {
                        Debug.Log(
                            $"[CaveBuild] Meat loop DONE: {state.Quality.LetterGrade} ({state.Quality.OverallScore}), " +
                            $"passes={state.Pass}, fixes={state.FixesApplied}, grades={state.GradePasses}.");
                        FinalizeQueuedMeatReport(state);
                        state.Done = true;
                        return true;
                    }

                    TryInvokeCursorDuringMeatLoop(state.Quality, state.Pass, state.CaveRoot, state.Ground);

                    if (state.Pass >= AdventureMaxPasses)
                    {
                        Debug.LogWarning(
                            $"[CaveBuild] Meat loop EXHAUSTED at {state.Quality.LetterGrade} ({state.Quality.OverallScore}). " +
                            "See Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json");
                        FinalizeQueuedMeatReport(state);
                        state.Done = true;
                        return true;
                    }

                    state.Phase = QueuedMeatPhase.Fix;
                    return false;

                case QueuedMeatPhase.Fix:
                    if (CaveBuildWorkflowCoordinator.IsGroundPlacementLocked)
                    {
                        state.MouthGrounded = true;
                        var rootPlacementOk = state.CaveRoot == null || state.Ground == null ||
                                              !state.Ground.HasAnchor ||
                                              CaveGroundPlacementUtility.IsRootPlacementAcceptable(
                                                  state.CaveRoot, state.Ground);
                        if (rootPlacementOk)
                        {
                            state.Skipped.Add("ground_placement");
                            state.Skipped.Add("cave_mouth_seal");
                        }
                    }

                    if (CaveBuildMeatLoopEnrichment.TryApply(
                            state.CaveRoot,
                            state.Request,
                            state.Ground,
                            state.Pass,
                            out var enrichAction))
                    {
                        state.FixesApplied++;
                        Debug.Log($"[CaveBuild] Meat enrichment pass {state.Pass}: {enrichAction}");
                    }

                    if (!state.MouthGrounded &&
                        state.Ground != null &&
                        state.Ground.HasAnchor)
                    {
                        CaveBuildWorkflowCoordinator.TryAutoLockIfPlacementReady(
                            state.CaveRoot, state.Ground);
                        if (CaveBuildWorkflowCoordinator.MouthIsGrounded)
                        {
                            state.MouthGrounded = true;
                            state.Skipped.Add("cave_mouth_seal");
                            state.Skipped.Add("ground_placement");
                            state.Skipped.Add("terrain_carve");
                        }
                    }

                    var rung = CaveBuildQualityLadder.PickNextRung(state.Quality, state.Skipped);
                    if (rung != null)
                    {
                        var rungId = rung.StageId;
                        if (rungId == state.LastFixRung &&
                            state.Quality.OverallScore <= state.LastOverallScore)
                            state.SameRungFixStreak++;
                        else
                        {
                            state.LastFixRung = rungId;
                            state.SameRungFixStreak = 1;
                        }

                        state.LastOverallScore = state.Quality.OverallScore;
                    }

                    if (rung != null && state.SameRungFixStreak >= 3)
                    {
                        Debug.LogWarning(
                            $"[CaveBuild] Meat pass {state.Pass}: '{rung.StageId}' failed to improve score after " +
                            $"{state.SameRungFixStreak} attempt(s) — skipping rung (see CaveBuildTailoredAgentPrompt.md).");
                        state.Skipped.Add(rung.StageId);
                        state.SameRungFixStreak = 0;
                        rung = CaveBuildQualityLadder.PickNextRung(state.Quality, state.Skipped);
                    }

                    if (rung == null)
                    {
                        Debug.LogWarning(
                            "[CaveBuild] Meat loop: no fixable rung — additive visual_shell route repair only (no purge).");
                        if (CaveBuildQualityStageFixer.TryFix(
                                "visual_shell",
                                state.CaveRoot,
                                state.Request,
                                state.Ground,
                                state.Request.Seed,
                                out var forced))
                        {
                            state.FixesApplied++;
                            Debug.Log($"[CaveBuild] Forced additive fix: {forced}");
                        }
                        else
                        {
                            FinalizeQueuedMeatReport(state);
                            state.Done = true;
                            return true;
                        }
                    }
                    else if (!CaveBuildQualityStageFixer.TryFix(
                                 rung.StageId,
                                 state.CaveRoot,
                                 state.Request,
                                 state.Ground,
                                 state.Request.Seed,
                                 out var action))
                    {
                        state.Skipped.Add(rung.StageId);
                    }
                    else
                    {
                        state.FixesApplied++;
                        Debug.Log($"[CaveBuild] Meat fix #{state.FixesApplied} [{rung.StageId}]: {action}");
                        if (rung.StageId is "cave_mouth_seal" or "terrain_integration" or "ground_placement" or "terrain_carve")
                        {
                            CaveBuildWorkflowCoordinator.TryAutoLockIfPlacementReady(
                                state.CaveRoot, state.Ground);
                            state.MouthGrounded = CaveBuildWorkflowCoordinator.MouthIsGrounded;
                        }

                        if ((rung.StageId == "navmesh" || rung.StageId == "walkways" ||
                             rung.StageId == "spawn_reachability") &&
                            CaveBuildWorkflowCoordinator.TryConsumeNavMeshBake())
                        {
                            state.BuildReport.NavMeshBuilt =
                                LavaTubeCavePostProcess.BakeNavMeshOnly(state.CaveRoot);
                        }
                        else if (rung.StageId == "visual_shell" || rung.StageId == "geometry_integrity")
                            CaveBuildWorkflowCoordinator.InvalidateNavMesh();
                    }

                    state.Pass++;
                    state.Phase = QueuedMeatPhase.Purge;
                    return false;
            }

            return false;
        }

        static void RunLayoutPrototypeOnly(QueuedMeatState state, System.Action<int, string> onStep)
        {
            onStep?.Invoke(0, "Layout prototype — grade only (no meat rebuild)");
            state.Quality = CaveBuildQualityGrader.GradeFullBuild(
                state.CaveRoot, state.Ground, state.Request, state.BuildReport);
            state.Quality.AdventureMode = true;
            state.Quality.LayoutPrototypeMode = true;
            state.Quality.RecalculateOverall();
            state.GradePasses = 1;
            CaveBuildQualityReportWriter.Write(state.Quality, gradingMode: "layout_prototype");
            CaveBuildAgentContextExporter.Export(state.Quality, state.CaveRoot, meatLoopPass: 0, state.Ground);
            if (!CaveBuildCursorSettings.DefersPostBuildCursorToAutonomousLoop())
            {
                CaveBuildCursorAgentBridge.TryAutoInvokeAfterBuildComplete(
                    state.Quality, state.CaveRoot, state.Ground, afterPostBuildPasses: false);
            }
            if (state.CaveRoot != null)
                EditorUtility.SetDirty(state.CaveRoot.gameObject);
        }

        public static void FinalizeQueuedMeatReport(QueuedMeatState state)
        {
            EditorUtility.ClearProgressBar();
            if (state.Adventure)
                CaveEnclosureShellBuilder.HideRoutePlatformSlabs(state.CaveRoot);

            CaveBuildQualitySystem.ApplyDudDetection(state.Quality, state.CaveRoot, state.Request);
            state.Quality.RemediationPasses = state.FixesApplied;
            state.Quality.LadderGradePasses = state.GradePasses;
            state.Quality.RecalculateOverall();
            CaveBuildQualitySystem.ApplyRecommendedAction(state.Quality, state.Request);
            CaveBuildQualityReportWriter.Write(state.Quality, gradingMode: "meat_loop");
            CaveBuildAgentContextExporter.Export(
                state.Quality, state.CaveRoot, meatLoopPass: AdventureMaxPasses + 1, state.Ground);
            CaveBuildQualityAgentBridge.WriteStructuredPrompt(state.Quality, includeLiveSection: false);
            if (state.CaveRoot != null)
                EditorUtility.SetDirty(state.CaveRoot.gameObject);
        }
    }
}
