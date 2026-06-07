#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Per-capture-run markers that ensure recap compose and recording-end handling run at most once.
    /// </summary>
    static class CaveBuildRecapComposeLatch
    {
        const string ComposeCompletedMarker = "RecapComposeCompleted.marker";
        const string ComposeStartedMarker = "RecapComposeStarted.marker";
        const string RecordingEndedMarker = "RecapRecordingEnded.marker";
        const long MinValidMp4Bytes = 50_000;

        public static bool IsComposeCompleted(string runFolder) =>
            !string.IsNullOrEmpty(runFolder) && MarkerExists(runFolder, ComposeCompletedMarker);

        public static bool IsComposeStarted(string runFolder) =>
            !string.IsNullOrEmpty(runFolder) &&
            (MarkerExists(runFolder, ComposeStartedMarker) || IsComposeCompleted(runFolder));

        /// <summary>Returns false when compose already started or completed for this run folder.</summary>
        public static bool TryMarkComposeStarted(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return false;

            if (IsComposeStarted(runFolder))
                return false;

            try
            {
                Directory.CreateDirectory(runFolder);
                var path = MarkerPath(runFolder, ComposeStartedMarker);
                File.WriteAllText(path, DateTime.UtcNow.ToString("o"));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not mark compose started: " + ex.Message);
                return false;
            }
        }

        public static void MarkComposeCompleted(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return;

            try
            {
                Directory.CreateDirectory(runFolder);
                File.WriteAllText(MarkerPath(runFolder, ComposeCompletedMarker), DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not mark compose completed: " + ex.Message);
            }
        }

        public static bool IsRecordingEndHandled(string runFolder) =>
            !string.IsNullOrEmpty(runFolder) && MarkerExists(runFolder, RecordingEndedMarker);

        public static void MarkRecordingEndHandled(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return;

            try
            {
                Directory.CreateDirectory(runFolder);
                File.WriteAllText(MarkerPath(runFolder, RecordingEndedMarker), DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not mark recording end handled: " + ex.Message);
            }
        }

        public static void ClearRecordingEndHandled(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return;

            TryDeleteMarker(runFolder, RecordingEndedMarker);
        }

        public static bool HasValidPresentationOutput(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return false;

            var path = Path.Combine(runFolder, "DemoRecapPresentation.mp4");
            return File.Exists(path) && new FileInfo(path).Length >= MinValidMp4Bytes;
        }

        public static bool SilentComposeVideoExists(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return false;

            var graded = Path.Combine(runFolder, "_presentation_compose", "_final_video.mp4");
            return File.Exists(graded) && new FileInfo(graded).Length > 1024;
        }

        static string MarkerPath(string runFolder, string markerName) =>
            Path.Combine(runFolder, markerName);

        static bool MarkerExists(string runFolder, string markerName) =>
            File.Exists(MarkerPath(runFolder, markerName));

        static void TryDeleteMarker(string runFolder, string markerName)
        {
            try
            {
                var path = MarkerPath(runFolder, markerName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }
}
#endif
