#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Unity Recorder Game view capture during Hub playtest breaks — 1080p MP4 to capture/uploads/playthroughs/.
    /// </summary>
    [InitializeOnLoad]
    static class CaveBuildPlayModeUnityRecorder
    {
        const string PrefAutoRecord = "EnvironmentKit_PlayModeUnityRecorder";
        const int OutputWidth = 1920;
        const int OutputHeight = 1080;
        const int FrameRate = 30;

        static RecorderController _controller;
        static string _outputPath = string.Empty;

        static CaveBuildPlayModeUnityRecorder() =>
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        public static bool AutoRecordDuringPlaytestBreak
        {
            get => EditorPrefs.GetBool(PrefAutoRecord, false);
            set => EditorPrefs.SetBool(PrefAutoRecord, value);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Easter egg: CAVE_ENABLE_DEMO_RECORDER=1
            if (!CaveBuildDemoAutoRecorder.HubBuildRecordingOptIn)
                return;

            var recordPostBuild = CaveBuildPostBuildFinalizeGate.IsRecordingPlaythrough;
            if (!AutoRecordDuringPlaytestBreak ||
                (!CaveBuildDemoAutoRecorder.HubBuildRecordingEnabled &&
                 !CaveBuildDemoAutoRecorder.IsRecording) ||
                (!CaveBuildPauseController.PlaytestBreakActive && !recordPostBuild))
                return;

            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    TryPrepareRecording();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    TryStartRecording();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    TryStopRecording();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    CleanupController();
                    break;
            }
        }

        static string ResolveCaptureFolder()
        {
            var folder = CaveBuildDemoAutoRecorder.CurrentCaptureFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Debug.LogWarning(
                    "[PlayModeRecorder] No active DemoCapture folder — start a Hub build or demo recording first.");
                return null;
            }

            return folder;
        }

        static string NextPlaythroughPath(string captureFolder)
        {
            var outDir = Path.Combine(captureFolder, "uploads", "playthroughs");
            Directory.CreateDirectory(outDir);
            var existing = Directory.Exists(outDir)
                ? Directory.GetFiles(outDir, "playtest-*.*")
                    .Count(p => p.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                || p.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
                : 0;
            var pass = existing + 1;
            _outputPath = Path.Combine(outDir, $"playtest-{pass:000}.mp4");
            return Path.Combine(outDir, $"playtest-{pass:000}");
        }

        static void TryPrepareRecording()
        {
            CleanupController();
            var capture = ResolveCaptureFolder();
            if (string.IsNullOrEmpty(capture))
                return;

            var outputBase = NextPlaythroughPath(capture);

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Environment Kit Playtest";
            movie.Enabled = true;
            movie.CaptureAudio = false;
            movie.CaptureAlpha = false;
            movie.OutputFile = outputBase;

            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = OutputWidth,
                OutputHeight = OutputHeight,
            };

            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = FrameRate;
            settings.CapFrameRate = true;

            _controller = new RecorderController(settings);
            try
            {
                _controller.PrepareRecording();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayModeRecorder] PrepareRecording failed — check Recorder package / Game view: {ex.Message}");
                CleanupController();
                return;
            }

            Debug.Log($"[PlayModeRecorder] Prepared Game view {OutputWidth}x{OutputHeight} @ {FrameRate}fps → {_outputPath}");
        }

        static void TryStartRecording()
        {
            if (_controller == null)
                return;

            if (!_controller.StartRecording())
            {
                Debug.LogWarning("[PlayModeRecorder] StartRecording failed.");
                CleanupController();
                return;
            }

            Debug.Log("[PlayModeRecorder] Recording Play Mode (Unity Recorder)…");
        }

        static void TryStopRecording()
        {
            if (_controller == null)
                return;

            _controller.StopRecording();
            if (!string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
            {
                var mb = new FileInfo(_outputPath).Length / (1024.0 * 1024.0);
                Debug.Log($"[PlayModeRecorder] Playthrough saved → {_outputPath} ({mb:F1} MB)");
            }
            else
            {
                Debug.LogWarning(
                    "[PlayModeRecorder] Recording stopped but output file missing — check Console for Recorder errors.");
            }

            CleanupController();
        }

        static void CleanupController()
        {
            _controller = null;
            _outputPath = string.Empty;
        }
    }
}
#endif
