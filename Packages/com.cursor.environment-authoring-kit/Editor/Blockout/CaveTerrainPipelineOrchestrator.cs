#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// FullWorld build order (paced via <see cref="CaveBuildActionPacing"/> — never blocking WaitForExit):
    /// 1. Pre-build ladder (C#) — may run after surface when started from startup coordinator
    /// 2. Surface world + terrain AI phases + terrain ladder + terrain meat loop (Ground anchor up)
    /// 3. Cave geometry queue (underground, mouth aligned to finished terrain)
    /// 4. Cave validation / world / cave meat loop (underground grading only)
    /// 5. Post-meat, research, commercial manifest, finalize
    /// </summary>
    public static class CaveTerrainPipelineOrchestrator
    {
        public const string BuildOrderDoc =
            "FullWorld: surface/terrain grade (Ground up) → cave shell aligned down → cave grade → manifest.";

        const float DefaultWaitTimeoutSeconds = 900f;
        const float WaitLogIntervalSeconds = 20f;

        static double _waitStartedAt;
        static string _lastLoggedBlockers = string.Empty;
        static double _lastWaitLogAt;

        public static void ResetWaitState()
        {
            _waitStartedAt = 0;
            _lastLoggedBlockers = string.Empty;
            _lastWaitLogAt = 0;
        }

        /// <summary>Cave meat loop must not run until above-ground terrain pipeline finished for this seed.</summary>
        public static bool CanStartCaveMeatLoop(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (request == null)
                return true;

            if (request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return true;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.waitForTerrainBeforeCaveMeat)
                return true;

            if (CaveBuildSurfaceCompletionGate.IsReadyForCaveMeatLoop(request, ground))
                return true;

            if (ShouldForceCaveMeatDespiteTerrainWait(request, ground, settings, out _))
                return true;

            return false;
        }

        static bool ShouldForceCaveMeatDespiteTerrainWait(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            CaveBuildCursorSettings settings,
            out string reason)
        {
            reason = null;

            if (CaveBuildSurfaceCompletionGate.SurfacePipelineFailed)
            {
                reason =
                    "Surface/terrain pipeline failed — continuing with underground cave meat loop only.";
                return true;
            }

            if (!CaveBuildSurfaceCompletionGate.WasSurfacePipelineQueued &&
                !SurfaceTerrainAiPhases.IsPipelineActive &&
                !CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive)
            {
                reason =
                    "Surface/terrain pipeline never started — continuing with cave meat loop (re-run Build Complete Cave).";
                return true;
            }

            var timeout = settings.caveMeatTerrainWaitTimeoutSeconds > 0f
                ? settings.caveMeatTerrainWaitTimeoutSeconds
                : DefaultWaitTimeoutSeconds;
            if (_waitStartedAt > 0 &&
                EditorApplication.timeSinceStartup - _waitStartedAt >= timeout)
            {
                reason =
                    $"Terrain wait exceeded {timeout:F0}s — continuing cave meat loop; " +
                    "surface may still be running in background.";
                return true;
            }

            return false;
        }

        public static void ScheduleCaveMeatWhenTerrainReady(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            System.Action onReady)
        {
            if (onReady == null)
                return;

            if (CanStartCaveMeatLoop(request, ground))
            {
                ResetWaitState();
                onReady();
                return;
            }

            if (_waitStartedAt <= 0)
                _waitStartedAt = EditorApplication.timeSinceStartup;

            TryKickStalledSurfaceHandoff(request, ground);

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (ShouldForceCaveMeatDespiteTerrainWait(request, ground, settings, out var forceReason))
            {
                CaveBuildEditorLog.LogCaveWarning("[Orchestrator] " + forceReason);
                ResetWaitState();
                onReady();
                return;
            }

            MaybeLogTerrainWait(request, ground);
            UpdateCaveWaitProgressBar(request, ground);

            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    if (CanStartCaveMeatLoop(request, ground))
                    {
                        ResetWaitState();
                        onReady();
                    }
                    else
                        ScheduleCaveMeatWhenTerrainReady(request, ground, onReady);
                },
                CaveBuildPipelineDomains.QueueLabel("cave meat — wait terrain"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static double _sculptStallKickAt;

        static void TryKickStalledSurfaceHandoff(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (request?.SurfaceScope != SurfaceBuildScope.FullWorld)
                return;

            if (SurfaceTerrainCenteredAuthor.IsQueuedPassesActive)
            {
                var elapsed = _waitStartedAt > 0
                    ? EditorApplication.timeSinceStartup - _waitStartedAt
                    : 0;
                if (elapsed > 45.0 &&
                    EditorApplication.timeSinceStartup - _sculptStallKickAt > 30.0)
                {
                    _sculptStallKickAt = EditorApplication.timeSinceStartup;
                    CaveBuildActionPacing.PreparePipelineChainKickoff();
                    CaveBuildEditorLog.LogSurface(
                        "[Orchestrator] Prioritizing stalled terrain sculpt queue (cave meat waiting).",
                        forceUnityConsole: true);
                }

                return;
            }

            if (CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive ||
                SurfaceTerrainAiPhases.IsPipelineActive)
                return;

            if (!CaveBuildSurfaceCompletionGate.WasSurfacePipelineQueued &&
                ground != null &&
                request != null)
            {
                CaveBuildEditorLog.LogCaveWarning(
                    "[Orchestrator] Cave meat waiting but surface never ran — run Build Complete Cave (terrain-first).");
            }
        }

        static void UpdateCaveWaitProgressBar(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (request?.SurfaceScope != SurfaceBuildScope.FullWorld)
                return;

            var elapsed = _waitStartedAt > 0
                ? EditorApplication.timeSinceStartup - _waitStartedAt
                : 0;
            var blockers = CaveBuildSurfaceCompletionGate.DescribeCaveMeatLoopBlockers(request, ground);
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Cave] Waiting for surface/terrain ({elapsed:F0}s) — {blockers}",
                0.48f);
        }

        static void MaybeLogTerrainWait(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            var blockers = CaveBuildSurfaceCompletionGate.DescribeCaveMeatLoopBlockers(request, ground);
            var now = EditorApplication.timeSinceStartup;
            var elapsed = _waitStartedAt > 0 ? now - _waitStartedAt : 0;
            if (blockers == _lastLoggedBlockers &&
                now - _lastWaitLogAt < WaitLogIntervalSeconds)
                return;

            _lastLoggedBlockers = blockers;
            _lastWaitLogAt = now;
            CaveBuildEditorLog.LogCave(
                $"[Orchestrator] Cave meat loop waiting for terrain ({elapsed:F0}s) — {blockers}. " +
                "Surface runs in parallel after cave shell; underground grading starts when terrain finishes.",
                forceUnityConsole: true);
        }
    }
}
#endif
