#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Keeps the Unity editor interactive during long paced builds.</summary>
    public static class CaveBuildEditorResponsiveness
    {
        /// <summary>Max main-thread work per editor slice before deferring (seam stitch, inner lock).</summary>
        public const double MaxSliceSeconds = 0.022;

        static double _sliceStartedAt;

        public static bool IsLongBuildActive =>
            LavaTubeCaveBuilder.IsBuildInProgress ||
            CaveBuildStartupCoordinator.IsActive ||
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
            SurfaceTerrainAiPhases.IsPipelineActive;

        public static void BeginSlice() =>
            _sliceStartedAt = EditorApplication.timeSinceStartup;

        public static bool ShouldYieldSlice() =>
            EditorApplication.timeSinceStartup - _sliceStartedAt >= MaxSliceSeconds;

        public static void ApplyForActiveBuild(CaveBuildCursorSettings settings)
        {
            if (settings == null)
                return;

            settings.editorQueueBatchSize = 1;
            settings.showLiveScenePlacement = true;
            settings.mirrorPacedBuildLogsToConsole = false;
        }

        public static void OnQueueStepCompleted()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            CaveBuildHardwareMonitor.OnQueueStepCompleted();
            EnvironmentKitHardwareBudget.OnQueueStepCompletedThrottled();
        }
    }
}
#endif
