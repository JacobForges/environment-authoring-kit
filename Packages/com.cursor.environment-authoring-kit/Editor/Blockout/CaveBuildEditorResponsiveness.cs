#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Keeps the Unity editor interactive during long paced builds.</summary>
    public static class CaveBuildEditorResponsiveness
    {
        public static bool IsLongBuildActive =>
            LavaTubeCaveBuilder.IsBuildInProgress ||
            CaveBuildStartupCoordinator.IsActive ||
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
            SurfaceTerrainAiPhases.IsPipelineActive;

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
            EnvironmentKitHardwareBudget.OnQueueStepCompletedThrottled();
        }
    }
}
#endif
