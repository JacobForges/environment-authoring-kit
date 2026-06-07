#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Full auto-heal: checkpoint good states, rollback on failure, procedural fix, then active AI provider retry.
    /// </summary>
    public static class CaveBuildHealOrchestrator
    {
        static int _recoveryAttempts;
        static string _lastFailureContext = "";

        public static bool IsEnabled
        {
            get
            {
                var s = CaveBuildCursorSettings.LoadOrCreate();
                s.LoadFromPrefs();
                return s.enableFullAutoHeal;
            }
        }

        public static void ResetSession()
        {
            _recoveryAttempts = 0;
            _lastFailureContext = "";
        }

        public static void ScheduleDefaultRetry()
        {
            CaveBuildLayoutRollSession.RequestPreserveNextBuild();
            CaveBuildCursorAgentBridge.ScheduleAutoRebuildAfterCompile();
        }

        public static bool LooksLikeFailureReport(LavaTubeCaveBuildReport report)
        {
            if (report == null)
                return true;

            var msg = report.Message ?? string.Empty;
            if (msg.IndexOf("Complete cave in", StringComparison.OrdinalIgnoreCase) >= 0 &&
                report.QualityAcceptable)
                return false;

            if (msg.IndexOf("cancelled", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return msg.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("blocked", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   !report.QualityAcceptable;
        }

        public static void OnQueuedPipelineFailure(
            LavaTubeCaveBuildReport report,
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            Transform caveRoot,
            CaveBuildQualityReport quality)
        {
            if (!IsEnabled || request == null || !LooksLikeFailureReport(report))
                return;

            if (caveRoot == null)
                caveRoot = CaveRouteProbeRunner.FindCaveRoot();

            OnBuildFailure(
                "queued_pipeline: " + Truncate(report?.Message, 120),
                request.Seed,
                quality,
                caveRoot,
                ground,
                request,
                ScheduleDefaultRetry);
        }

        public static void TryRollbackBeforeFix()
        {
            if (!IsEnabled)
                return;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.healRollbackBeforeRetry)
                return;

            var ground = SceneGroundResolver.Resolve();
            var main = ground?.Terrain;
            if (main == null && ground?.HasAnchor == true)
                main = ground.Anchor.GetComponentInChildren<Terrain>();

            if (main != null &&
                CaveBuildHealCheckpointStore.TryRestoreBestAvailable(main, out var restoreMsg))
            {
                Debug.Log("[AutoHeal] " + restoreMsg);
            }
        }

        public static void CaptureSessionBaseline(int seed, Terrain main, Transform caveRoot)
        {
            if (!IsEnabled || main == null)
                return;

            var index = CaveBuildHealCheckpointStore.LoadIndex();
            if (!string.IsNullOrEmpty(index.baselineCheckpointId))
                return;

            CaveBuildHealCheckpointStore.Capture(
                "session_baseline",
                seed,
                0,
                "—",
                false,
                main,
                caveRoot,
                note: "first_capture_baseline");
        }

        public static void CapturePreBuildIfEnabled(
            int seed,
            CaveBuildPreBuildReport report,
            SceneGroundInfo ground,
            Transform caveRoot = null)
        {
            if (!IsEnabled || report == null || ground?.Terrain == null)
                return;

            var id = CaveBuildHealCheckpointStore.Capture(
                report.BuildAcceptable ? "pre_build_pass" : "pre_build_attempt",
                seed,
                report.OverallScore,
                report.LetterGrade ?? "?",
                report.BuildAcceptable,
                ground.Terrain,
                caveRoot,
                note: report.BuildAcceptable ? "pre_build_gate_pass" : "pre_build_gate");

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (report.BuildAcceptable && report.OverallScore >= settings.healMinScoreForGoodCheckpoint)
                CaveBuildHealCheckpointStore.PromoteGood(id, "pre_build_pass");
        }

        public static void OnMilestone(
            string milestone,
            int seed,
            CaveBuildQualityReport caveQuality,
            SurfaceTerrainLadderReport terrainReport,
            Terrain mainTerrain,
            Transform caveRoot)
        {
            if (!IsEnabled || mainTerrain == null)
                return;

            var score = caveQuality?.OverallScore ?? terrainReport?.OverallScore ?? 0;
            var grade = caveQuality?.LetterGrade ?? terrainReport?.LetterGrade ?? "?";
            var acceptable = caveQuality?.BuildAcceptable == true || terrainReport?.BuildAcceptable == true;

            var id = CaveBuildHealCheckpointStore.Capture(
                milestone,
                seed,
                score,
                grade,
                acceptable,
                mainTerrain,
                caveRoot,
                note: acceptable ? "milestone_pass" : "milestone_capture");

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (acceptable && score >= settings.healMinScoreForGoodCheckpoint)
            {
                CaveBuildHealCheckpointStore.PromoteGood(id, milestone);
                _recoveryAttempts = 0;
            }
        }

        public static void OnBuildFailure(
            string context,
            int seed,
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action onRetryScheduled)
        {
            if (!IsEnabled)
                return;

            _lastFailureContext = context ?? "build_failure";
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (_recoveryAttempts >= settings.maxHealRecoveryAttempts)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[AutoHeal] Max recovery attempts ({settings.maxHealRecoveryAttempts}) — stopping. " +
                    $"Last good: {CaveBuildHealCheckpointStore.LoadIndex().lastGoodCheckpointId}");
                return;
            }

            _recoveryAttempts++;
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "AutoHeal",
                $"recovery {_recoveryAttempts}/{settings.maxHealRecoveryAttempts} — {_lastFailureContext}");

            var main = ground?.Terrain;
            if (main == null && ground?.HasAnchor == true)
                main = ground.Anchor.GetComponentInChildren<Terrain>();

            var restored = false;
            if (settings.healRollbackBeforeRetry && main != null)
            {
                restored = CaveBuildHealCheckpointStore.TryRestoreBestAvailable(main, out var restoreMsg);
                if (restored)
                    Debug.Log("[AutoHeal] " + restoreMsg);
                else
                    Debug.LogWarning("[AutoHeal] " + restoreMsg);
            }

            var proceduralFixed = TryProceduralFixes(quality, caveRoot, request, ground, seed);

            var provider = CaveBuildCursorSettings.ResolveActiveProvider();
            var hasAi = CaveBuildCursorSettings.HasCredentialsForActiveProvider();
            if (settings.healInvokeAiAfterRollback && hasAi)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[AutoHeal] Invoking {provider} grade-and-fix after rollback (attempt {_recoveryAttempts}).",
                    forceUnityConsole: true);

                if (CaveBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(out var aiMsg, startLadderChain: false))
                    Debug.Log("[AutoHeal] AI repair queued: " + aiMsg);
            }
            else if (!hasAi)
            {
                Debug.LogWarning(
                    "[AutoHeal] AI repair skipped — " + CaveBuildCursorSettings.GraderCredentialHint());
            }

            if (proceduralFixed || hasAi || restored)
                ScheduleRetry(onRetryScheduled, settings.healRetryDelaySeconds);
        }

        static bool TryProceduralFixes(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            int seed)
        {
            if (quality == null || caveRoot == null || request == null)
                return false;

            var changed = false;
            foreach (var stage in quality.Stages)
            {
                if (stage == null || stage.Passed || stage.Score >= 65)
                    continue;

                if (!CaveBuildQualityStageFixer.CanAutoFix(stage.StageId))
                    continue;

                if (CaveBuildQualityStageFixer.TryFix(
                        stage.StageId,
                        caveRoot,
                        request,
                        ground,
                        seed,
                        out var action))
                {
                    Debug.Log($"[AutoHeal] Procedural fix {stage.StageId}: {action}");
                    changed = true;
                }
            }

            if (changed)
                AssetDatabase.SaveAssets();

            return changed;
        }

        static void ScheduleRetry(Action onRetry, float delaySeconds)
        {
            if (onRetry == null)
                return;

            var readyAt = EditorApplication.timeSinceStartup + Mathf.Max(2f, delaySeconds);

            void PollRetry()
            {
                if (EditorApplication.timeSinceStartup < readyAt || EditorApplication.isCompiling)
                {
                    CaveBuildActionPacing.ScheduleBuildStep(
                        PollRetry,
                        CaveBuildPipelineDomains.QueueLabel("auto-heal retry wait"),
                        CaveBuildActionPacing.ActionWeight.Light);
                    return;
                }

                onRetry.Invoke();
            }

            CaveBuildActionPacing.ScheduleBuildStep(
                PollRetry,
                CaveBuildPipelineDomains.QueueLabel("auto-heal retry"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        public static void CaptureIfEnabled(
            string milestone,
            int seed,
            Terrain main,
            Transform caveRoot,
            bool forceAcceptable = false)
        {
            if (!IsEnabled || main == null)
                return;

            if (milestone == "session_baseline" ||
                (CaveBuildHealCheckpointStore.LoadIndex().baselineCheckpointId == string.Empty &&
                 milestone.Contains("surface")))
            {
                CaptureSessionBaseline(seed, main, caveRoot);
            }

            CaveBuildQualityReport caveQ = null;
            SurfaceTerrainLadderReport terrainQ = null;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            if (CaveBuildQualityReportLoader.TryLoad(hub, out var cq))
                caveQ = cq;
            if (SurfaceTerrainBuildLadder.TryTakeCachedGradedReport(seed, out var tq))
                terrainQ = tq;

            if (forceAcceptable && caveQ != null)
                caveQ.BuildAcceptable = true;
            if (forceAcceptable && terrainQ != null)
                terrainQ.BuildAcceptable = true;

            OnMilestone(milestone, seed, caveQ, terrainQ, main, caveRoot);
        }

        static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "failure";
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }
    }
}
#endif
