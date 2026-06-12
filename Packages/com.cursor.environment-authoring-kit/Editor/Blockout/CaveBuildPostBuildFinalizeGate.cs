#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// After a Hub build: prompt Play Mode gameplay capture, then prefab export, scene save, and recap compose.
    /// </summary>
    [InitializeOnLoad]
    public static class CaveBuildPostBuildFinalizeGate
    {
        const string PrefPrompt = "CaveBuild_PromptPlayModeAfterBuild";

        enum Phase
        {
            Idle,
            AwaitingPlayMode,
            RecordingPlayMode,
            Finalizing,
        }

        sealed class Context
        {
            public string SceneName;
            public LavaTubeCaveBuildReport Report;
            public CaveLayoutRoll Roll;
            public CaveBuildQualityReport Quality;
            public bool SkipDialogs;
        }

        static Phase _phase = Phase.Idle;
        static Context _ctx;
        static bool _dialogShown;

        static CaveBuildPostBuildFinalizeGate()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static bool PromptPlayModeRecordingAfterBuild
        {
            get => EditorPrefs.GetBool(PrefPrompt, true);
            set => EditorPrefs.SetBool(PrefPrompt, value);
        }

        public static bool IsActive => _phase != Phase.Idle;

        public static bool IsAwaitingPlayMode => _phase == Phase.AwaitingPlayMode;

        public static bool IsRecordingPlaythrough =>
            _phase == Phase.RecordingPlayMode || CaveBuildPauseController.PostBuildPlaythroughActive;

        /// <summary>Hold recap compose until Play Mode capture + finalize chain runs.</summary>
        public static void OnRunStatusSessionEnded()
        {
            if (!ShouldOfferPlayModeRecording())
            {
                CaveBuildDemoAutoRecorder.TryFinalizeOnBuildSessionEnd();
                return;
            }

            CaveBuildDemoAutoRecorder.HoldComposeForPostBuildPlaythrough();
        }

        /// <summary>Returns true when the gate owns completion (caller should skip ShowFinished).</summary>
        public static bool TryOfferPlayModeRecording(
            string sceneName,
            LavaTubeCaveBuildReport report,
            CaveLayoutRoll roll,
            CaveBuildQualityReport quality,
            bool skipDialogs)
        {
            if (!ShouldOfferPlayModeRecording() || skipDialogs || CaveBuildBatchRunner.IsActive)
                return false;

            _ctx = new Context
            {
                SceneName = sceneName,
                Report = report,
                Roll = roll,
                Quality = quality,
                SkipDialogs = skipDialogs,
            };
            _phase = Phase.AwaitingPlayMode;
            _dialogShown = false;

            CaveBuildCompletionSummary.WriteCompletionArtifacts(
                sceneName,
                report,
                roll,
                quality,
                out _,
                out _);

            CaveBuildDemoAutoRecorder.HoldComposeForPostBuildPlaythrough();
            EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                "post-build (before Play Mode prompt)",
                out var detail,
                flushAssets: true);
            Debug.Log("[CaveBuild] Post-build finalize — " + detail);

            EditorApplication.delayCall += ShowPlayModePrompt;
            EnvironmentKitHubWindow.NotifyPostBuildPlaythroughPending();
            return true;
        }

        public static void UserEnterPlayMode()
        {
            if (_phase != Phase.AwaitingPlayMode)
                return;

            _phase = Phase.RecordingPlayMode;
            CaveBuildPauseController.BeginPostBuildPlaythrough();

            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        public static void UserSkipPlaythrough()
        {
            if (_phase != Phase.AwaitingPlayMode && _phase != Phase.RecordingPlayMode)
                return;

            RunFinalizeChain(playthroughRecorded: false);
        }

        public static void CancelIfActive()
        {
            if (_phase == Phase.Idle)
                return;

            _phase = Phase.Idle;
            _ctx = null;
            CaveBuildPauseController.EndPostBuildPlaythrough();
            EnvironmentKitHubWindow.ClearPostBuildPlaythroughPending();
            CaveBuildDemoAutoRecorder.ReleaseComposeHoldAndFinalize();
        }

        static void ShowPlayModePrompt()
        {
            if (_phase != Phase.AwaitingPlayMode || _dialogShown)
                return;

            _dialogShown = true;
            if (EnvironmentKitHubWindow.IsOpen)
                return;

            var choice = EditorUtility.DisplayDialogComplex(
                "Environment Kit — record gameplay",
                "Build generation finished.\n\n" +
                "Enter Play Mode to record a 1080p Game view playthrough for the recap video. " +
                "When you stop Play Mode, the kit saves the scene, exports the world prefab, and composes DemoRecapPresentation.mp4.",
                "Enter Play Mode",
                "Skip recording",
                "Decide in Hub");

            if (choice == 0)
                UserEnterPlayMode();
            else if (choice == 1)
                UserSkipPlaythrough();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (_phase != Phase.RecordingPlayMode)
                return;

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += () =>
                    RunFinalizeChain(playthroughRecorded: true);
            }
        }

        static void RunFinalizeChain(bool playthroughRecorded)
        {
            if (_phase == Phase.Idle || _phase == Phase.Finalizing)
                return;

            _phase = Phase.Finalizing;
            CaveBuildPauseController.EndPostBuildPlaythrough();

            var ctx = _ctx;
            _ctx = null;

            if (EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                    playthroughRecorded
                        ? "post-build finalize (after Play Mode)"
                        : "post-build finalize (skipped playthrough)",
                    out var saveDetail,
                    flushAssets: true))
            {
                Debug.Log("[CaveBuild] Scene saved — " + saveDetail);
            }
            else
            {
                Debug.LogWarning("[CaveBuild] Could not save scene during post-build finalize.");
            }

            AssetDatabase.SaveAssets();

            if (ctx != null)
            {
                if (CaveBuildGenerationPrefabExporter.TryExportWhenPipelineFinished(
                        ctx.SceneName,
                        ctx.Roll?.Seed ?? 0,
                        ctx.Quality,
                        playthroughRecorded ? "post_build_playthrough" : "post_build_skip_playthrough",
                        out var prefabPath,
                        out var prefabMsg))
                {
                    Debug.Log("[CaveBuild] " + prefabMsg);
                    if (!string.IsNullOrEmpty(prefabPath))
                        EnvironmentKitHubWindow.NotifyBuildCompleted(
                            "Build Complete — Prefab saved",
                            prefabMsg);
                }
                else if (!string.IsNullOrEmpty(prefabMsg))
                {
                    Debug.Log("[CaveBuild] Prefab export: " + prefabMsg);
                }

                CaveBuildWorldSessionManifestWriter.Write(
                    ctx.SceneName,
                    ctx.Roll?.Seed ?? 0,
                    ctx.Quality,
                    playthroughRecorded,
                    playthroughRecorded ? "post_build_playthrough" : "post_build_skip_playthrough");

                CaveBuildCompletionSummary.ShowFinished(
                    ctx.SceneName,
                    ctx.Report,
                    ctx.Roll,
                    ctx.Quality,
                    showDialog: !ctx.SkipDialogs && !EnvironmentKitHubWindow.IsOpen);
            }

            CaveBuildDemoAutoRecorder.ReleaseComposeHoldAndFinalize();
            _phase = Phase.Idle;
            EnvironmentKitHubWindow.ClearPostBuildPlaythroughPending();
        }

        static bool ShouldOfferPlayModeRecording()
        {
            if (!PromptPlayModeRecordingAfterBuild)
                return false;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.exportGenerationPrefabWhenFinished &&
                !CaveBuildDemoAutoRecorder.IsRecording &&
                string.IsNullOrEmpty(CaveBuildDemoAutoRecorder.LastOutputFolder))
            {
                return false;
            }

            return CaveBuildDemoAutoRecorder.IsRecording ||
                   !string.IsNullOrEmpty(CaveBuildDemoAutoRecorder.LastOutputFolder);
        }
    }
}
#endif
