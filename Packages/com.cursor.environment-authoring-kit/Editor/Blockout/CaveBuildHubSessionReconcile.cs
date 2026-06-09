#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Clears orphaned pause flags, concept locks, and surface-pipeline latches so Hub buttons work again.
    /// </summary>
    public static class CaveBuildHubSessionReconcile
    {
        static double _lastReconcileAt;

        public static bool IsCoreBuildRunning() =>
            LavaTubeCaveBuilder.IsBuildInProgress ||
            CaveBuildStartupCoordinator.IsActive ||
            CaveBuildRunStatusPublisher.HasActiveSession;

        public static bool IsPacedWorkActive() =>
            IsCoreBuildRunning() ||
            CaveBuildActionPacing.HasQueuedWork ||
            CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive ||
            CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive;

        public static void ReconcileStaleState(bool force = false)
        {
            var now = EditorApplication.timeSinceStartup;
            if (!force && now - _lastReconcileAt < 0.75)
                return;

            _lastReconcileAt = now;

            if (IsPacedWorkActive())
                return;

            var hasResume =
                CaveBuildFullWorldGridCheckpoint.HasResumable ||
                CaveBuildPacedStepResume.HasResumable;

            if (CaveBuildPauseController.UserPauseActive && !hasResume)
                CaveBuildPauseController.ClearStalePauseIfOrphaned();

            CaveBuildSurfaceCompletionGate.ClearOrphanedActiveFlags();
            CaveBuildConceptSession.ClearLock();
            if (!CaveBuildPostBuildFinalizeGate.IsActive)
                CaveBuildDemoAutoRecorder.TryComposeOrphanedCaptureIfIdle();
        }
    }
}
#endif
