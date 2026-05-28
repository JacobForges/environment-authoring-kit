#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Recovery when the editor locks up during cave builds (stuck queue, refresh suppression, etc.).
    /// </summary>
    static class CaveBuildEmergencyRecovery
    {
        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Invalidate All Ladder Rungs", false, 5)]
        public static void InvalidateAllLadderRungs() =>
            CaveBuildPhaseContractRegistry.InvalidateAll();

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Emergency: Unfreeze Editor", false, 0)]
        public static void UnfreezeEditor() => StopAllBuildActivity(showDialog: true, pinLastSeed: false);

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Stop All Builds (Hold Seed)", false, 1)]
        public static void StopAllBuildsHoldSeed() => StopAllBuildActivity(showDialog: true, pinLastSeed: true);

        public static void StopAllBuildActivity(bool showDialog, bool pinLastSeed)
        {
            CaveBuildActionPacing.EmergencyAbortAll();
            CaveBuildCursorAgentBridge.CancelPendingAutoRebuild();
            CaveBuildAutonomousOrchestrator.ForceStop("emergency stop");
            CaveBuildBatchRunner.CancelActive("emergency stop");
            CaveTerrainPipelineOrchestrator.ResetWaitState();
            CaveBuildSurfaceCompletionGate.ResetForNewBuildSession();
            SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();
            CaveBuildStartupCoordinator.EmergencyResetStartup();
            LavaTubeCaveBuildPipeline.EmergencyAbortQueuedBuild();
            LavaTubeCaveBuilder.ReleaseBuildLock();

            for (var i = 0; i < 4; i++)
                AssetDatabase.AllowAutoRefresh();

            CaveBuildDeferredAssetRefresh.RequestRefresh();

            if (pinLastSeed)
                CaveBuildLayoutRollSession.PinLastSeedForDebugging();

            var seedNote = pinLastSeed && CaveBuildLayoutRollSession.LastRecordedSeed > 0
                ? $"\n\nPinned seed {CaveBuildLayoutRollSession.LastRecordedSeed} and turned off random seed per build."
                : string.Empty;

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Cave build recovery",
                    "Stopped queued builds, autonomous loop, batch jobs, and pending auto-rebuilds." +
                    seedNote +
                    "\n\nIf Unity was spinning, give it a few seconds. If it stays frozen, force quit and reopen.",
                    "OK");
            }

            Debug.Log(
                "[CaveBuild] Emergency recovery: queue cleared, agents/auto-rebuild/batch stopped" +
                (pinLastSeed ? ", seed pinned." : "."));
        }
    }
}
#endif
