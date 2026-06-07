#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// User pause — queue keeps state; resume continues without invalidating incremental fingerprints.
    /// </summary>
    public static class CaveBuildPauseController
    {
        const string PrefPaused = "EnvironmentKit_BuildPaused";

        static CaveBuildPauseController() =>
            IsPaused = EditorPrefs.GetBool(PrefPaused, false);

        public static bool IsPaused { get; private set; }

        public static bool UserPauseActive =>
            IsPaused || EditorPrefs.GetBool(PrefPaused, false);

        public static void Pause()
        {
            IsPaused = true;
            EditorPrefs.SetBool(PrefPaused, true);
            CaveBuildRunStatusPublisher.PulseSubOperation("build", "PAUSED — Continue when ready");
            CaveBuildLiveSceneFeedback.NotifyStep(
                "Build PAUSED — queue frozen (terrain/props preserved)",
                null,
                frameScene: false);
            Debug.Log(
                "[CaveBuild] Build paused — incremental skips still apply on resume (no blanket overwrite).");
            CaveBuildDemoAutoRecorder.TryFinalizeOnUserStop("paused in Hub");
        }

        public static void Continue()
        {
            IsPaused = false;
            EditorPrefs.SetBool(PrefPaused, false);
            CaveBuildActionPacing.PreparePipelineChainKickoff();
            CaveBuildRunStatusPublisher.PulseSubOperation("build", "resuming…");
            CaveBuildLiveSceneFeedback.NotifyStep("Build resuming…", null, frameScene: false);
            CaveBuildDemoAutoRecorder.OnHubBuildStarting("build resumed");
            Debug.Log("[CaveBuild] Build resumed — queued steps will run again.");
        }

        public static void ClearOnNewBuildSession()
        {
            IsPaused = false;
            EditorPrefs.SetBool(PrefPaused, false);
        }

        internal static bool ShouldHoldQueue() => UserPauseActive;
    }
}
#endif
