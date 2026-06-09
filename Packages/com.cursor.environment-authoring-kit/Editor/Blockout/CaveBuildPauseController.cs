#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor;
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

        /// <summary>True during Hub playtest break — queue frozen but recap capture continues.</summary>
        public static bool PlaytestBreakActive { get; private set; }

        /// <summary>Post-build Play Mode capture — build queue finished; recap compose deferred.</summary>
        public static bool PostBuildPlaythroughActive { get; private set; }

        public static void Pause()
        {
            PlaytestBreakActive = false;
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

        /// <summary>
        /// Freeze the build queue for a Play Mode walkthrough without ending Scene timelapse capture.
        /// Enter Play Mode — Unity Recorder writes 1080p MP4 to capture/uploads/playthroughs/. Then Continue build.
        /// </summary>
        public static void PlaytestBreak()
        {
            PlaytestBreakActive = true;
            IsPaused = true;
            EditorPrefs.SetBool(PrefPaused, true);

            // Play Mode restores edit-mode from Temp/__Backupscenes — persist terrain to disk first.
            if (EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                    "playtest break (before Play Mode)",
                    out var saveDetail,
                    flushAssets: true))
            {
                Debug.Log("[CaveBuild] Playtest break — scene checkpoint saved (" + saveDetail + ").");
            }
            else
            {
                Debug.LogWarning(
                    "[CaveBuild] Playtest break — could not save scene checkpoint (" + saveDetail + "). " +
                    "Save MainScene.unity to disk before Play or terrain may vanish on exit.");
            }

            CaveBuildFullWorldGridCheckpoint.SaveActiveSession("playtest break");
            CaveBuildPacedStepPersistence.WritePlaytestBreakCheckpoint();
            CaveBuildPlaytestSessionSnapshot.Capture("playtest break");

            CaveBuildRunStatusPublisher.PulseSubOperation("build", "PLAYTEST BREAK — record Play Mode, then Continue");
            CaveBuildLiveSceneFeedback.NotifyStep(
                "Playtest break — queue frozen, recap still recording. Screen-record Play Mode, then Continue.",
                null,
                frameScene: false);
            Debug.Log(
                "[CaveBuild] Playtest break — queue frozen, recap capture continues. " +
                "Enter Play Mode: Unity Recorder saves 1080p MP4 to capture/uploads/playthroughs/.");
        }

        /// <summary>Post-build gameplay capture — build queue is idle; only recap compose is deferred.</summary>
        public static void BeginPostBuildPlaythrough()
        {
            PostBuildPlaythroughActive = true;
            PlaytestBreakActive = false;
            IsPaused = false;
            EditorPrefs.SetBool(PrefPaused, false);

            if (EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                    "post-build playthrough (before Play Mode)",
                    out var saveDetail,
                    flushAssets: true))
            {
                Debug.Log("[CaveBuild] Post-build playthrough — scene saved (" + saveDetail + ").");
            }

            CaveBuildRunStatusPublisher.PulseSubOperation(
                "post-build",
                "PLAYTHROUGH — record Game view, then exit Play Mode to finalize");
            Debug.Log(
                "[CaveBuild] Post-build playthrough — enter Play Mode. Unity Recorder saves 1080p MP4 " +
                "to DemoCapture/uploads/playthroughs/. Exit Play Mode to export prefab + compose recap.");
        }

        public static void EndPostBuildPlaythrough() => PostBuildPlaythroughActive = false;

        public static bool CanResumeQueuedPipeline() =>
            LavaTubeCaveBuilder.IsBuildInProgress ||
            CaveBuildStartupCoordinator.IsActive ||
            CaveBuildRunStatusPublisher.HasActiveSession ||
            CaveBuildActionPacing.HasQueuedWork;

        public static void Continue()
        {
            PlaytestBreakActive = false;
            PostBuildPlaythroughActive = false;

            if (CanResumeQueuedPipeline())
            {
                ClearPauseFlagOnly();
                KickQueuedPipeline();
                return;
            }

            if (CaveBuildFullWorldGridCheckpoint.TryResumeGridCheckpointSilent())
            {
                ClearPauseFlagOnly();
                LavaTubeCaveBuilder.ArmResumeBuildLock();
                CaveBuildDemoAutoRecorder.OnHubBuildStarting("grid checkpoint resume");
                Debug.Log("[CaveBuild] Continue — resumed FullWorld grid from checkpoint.");
                return;
            }

            if (CaveBuildPacedStepResume.HasResumable)
            {
                CaveBuildPacedStepResume.TryLoad(out var doc);
                var resume = EditorUtility.DisplayDialog(
                    "Resume interrupted build",
                    "Play Mode ended the in-memory build queue, but a paced-step checkpoint exists on disk.\n\n" +
                    $"Step {doc.pacedStep}: {doc.label}\n" +
                    $"Concept {doc.conceptIndex} · seed {doc.seed}\n\n" +
                    "Resume incremental build (keeps terrain)?",
                    "Resume build",
                    "Cancel");
                if (resume && CaveBuildPacedStepResume.TryResume(out var msg))
                {
                    ClearPauseFlagOnly();
                    Debug.Log("[CaveBuild] " + msg);
                }

                return;
            }

            ClearPauseFlagOnly();
            var hasTerrain = SurfaceTerrainTileExpansion.FindMainTerrainInScene() != null;
            EditorUtility.DisplayDialog(
                hasTerrain ? "Build session ended" : "Nothing to continue",
                hasTerrain
                    ? "Terrain is still in the scene but no resumable checkpoint was found. " +
                      "Try Build Complete Cave (incremental) — it skips completed ladder steps when terrain is present."
                    : "Terrain is gone and the paced queue is idle. " +
                      "Start Build Complete Cave again, or restore MainScene from a backup.",
                "OK");
            Debug.LogWarning("[CaveBuild] Continue — no active paced session and no checkpoint to resume.");
        }

        internal static void ClearPauseFlagOnly()
        {
            IsPaused = false;
            PlaytestBreakActive = false;
            PostBuildPlaythroughActive = false;
            EditorPrefs.SetBool(PrefPaused, false);
        }

        /// <summary>Pause flag left on disk after Play Mode with nothing left to resume.</summary>
        internal static void ClearStalePauseIfOrphaned()
        {
            if (!UserPauseActive)
                return;

            ClearPauseFlagOnly();
            Debug.LogWarning(
                "[CaveBuild] Cleared orphaned Hub pause flag — pipeline was idle with no checkpoint to resume.");
        }

        static void KickQueuedPipeline()
        {
            CaveBuildActionPacing.PreparePipelineChainKickoff();
            CaveBuildRunStatusPublisher.PulseSubOperation("build", "resuming…");
            CaveBuildLiveSceneFeedback.NotifyStep("Build resuming…", null, frameScene: false);
            CaveBuildDemoAutoRecorder.OnHubBuildStarting("build resumed");
            Debug.Log("[CaveBuild] Build resumed — queued steps will run again.");
        }

        public static void ClearOnNewBuildSession()
        {
            PlaytestBreakActive = false;
            PostBuildPlaythroughActive = false;
            ClearPauseFlagOnly();
        }

        internal static bool ShouldHoldQueue() => UserPauseActive;
    }
}
#endif
