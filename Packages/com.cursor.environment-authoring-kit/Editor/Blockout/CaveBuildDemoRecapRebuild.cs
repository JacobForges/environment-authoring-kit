#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveBuildDemoRecapRebuild
    {
        [MenuItem("Window/Environment Kit/Rebuild Demo Recap From Last Capture")]
        static void RebuildFromLast()
        {
            var folder = CaveBuildDemoAutoRecorder.LastOutputFolder;
            if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
            {
                Debug.LogWarning("[DemoRecorder] No last capture folder.");
                return;
            }

            var frames = CaveBuildDemoAutoRecorder.LoadFramesFromManifest(folder);
            if (frames == null || frames.Count == 0)
            {
                Debug.LogWarning("[DemoRecorder] No frames in last capture folder.");
                return;
            }

            var mode = CaveBuildRunStatusPublisher.BuildMode ?? "build";
            if (CaveBuildDemoNarrationAi.CanRun)
                CaveBuildDemoNarrationAi.TryEnhanceFrames(frames, mode, folder);

            if (CaveBuildDemoProducerCompose.TryComposeProducer(folder, previewOnly: false, out var outPath))
            {
                EditorUtility.RevealInFinder(outPath);
                return;
            }

            if (CaveBuildDemoSmartCompose.TryComposeSmartVideo(folder))
                EditorUtility.RevealInFinder(System.IO.Path.Combine(folder, "DemoRecap.mp4"));
            else if (CaveBuildDemoCompose.TryComposeVideo(folder, frames, mode))
                EditorUtility.RevealInFinder(System.IO.Path.Combine(folder, "DemoRecap.mp4"));
        }

        [MenuItem("Window/Environment Kit/Rebuild Demo Recap From Last Capture", true)]
        static bool RebuildFromLastValidate() => CaveBuildDemoAutoRecorder.HubBuildRecordingOptIn;

        [MenuItem("Window/Environment Kit/Rebuild Producer Recap Preview (Desktop)")]
        static void RebuildProducerPreview()
        {
            var folder = CaveBuildDemoAutoRecorder.LastOutputFolder;
            if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
            {
                Debug.LogWarning("[DemoRecorder] No last capture folder.");
                return;
            }

            if (CaveBuildDemoProducerCompose.TryComposeProducer(folder, previewOnly: true, out var outPath))
                EditorUtility.RevealInFinder(outPath);
        }

        [MenuItem("Window/Environment Kit/Rebuild Producer Recap Preview (Desktop)", true)]
        static bool RebuildProducerPreviewValidate() => CaveBuildDemoAutoRecorder.HubBuildRecordingOptIn;
    }
}
#endif
