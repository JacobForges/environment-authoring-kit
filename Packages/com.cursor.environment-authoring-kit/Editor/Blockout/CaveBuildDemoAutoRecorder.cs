#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Captures build checkpoints from Scene view and compiles one recap video with explanatory captions.
    /// </summary>
    [InitializeOnLoad]
    public static class CaveBuildDemoAutoRecorder
    {
        const string PrefEnabled = "EnvironmentKit_DemoRecorder_Enabled";
        const string PrefLastFolder = "EnvironmentKit_DemoRecorder_LastFolder";
        const string PrefSmartTimelapse = "EnvironmentKit_DemoRecorder_SmartTimelapse";
        const string PrefTimelapseInterval = "EnvironmentKit_DemoRecorder_TimelapseIntervalSec";
        const string PrefTargetMinutes = "EnvironmentKit_DemoRecorder_TargetMinutes";
        const string PrefForceBackgroundScene = "EnvironmentKit_DemoRecorder_ForceBackgroundScene";
        const string PrefPendingCompose = "EnvironmentKit_DemoRecorder_PendingCompose";
        const double ScenePumpIntervalSeconds = 0.12;
        const int Width = 1920;
        const int Height = 1080;
        const double MinCaptureIntervalSeconds = 2.0;
        const float DefaultFrameDurationSeconds = 2.8f;
        const double DefaultTimelapseIntervalSeconds = 2.0;
        const float DefaultTargetMinutes = 8f;
        const string CaptureFolderName = "DemoCapture";
        const string CaptureSessionRel = "Assets/EnvironmentKit/Generated/DemoCaptureSession.json";
        const string ChaptersFileName = "DemoRecapChapters.json";

        public sealed class FrameInfo
        {
            public string imagePath;
            public string phase;
            public string sub;
            public string line1;
            public string line2;
            public string line3;
            public float duration;
            public int timelapseFrame;

            public string NarrationKey => $"{line1}|{line2}|{line3}";

            public void ApplyLines(CaveBuildDemoNarration.Lines lines)
            {
                line1 = lines.Line1;
                line2 = lines.Line2;
                line3 = lines.Line3;
            }
        }

        static readonly List<FrameInfo> Frames = new();
        static readonly List<FrameInfo> Milestones = new();

        static bool _recording;
        static string _runFolder;
        static string _framesFolder;
        static string _timelapseFolder;
        static string _segmentsFolder;
        static string _lastPhase = string.Empty;
        static string _lastSub = string.Empty;
        static string _lastNarration = string.Empty;
        static int _lastStep = -1;
        static double _lastCaptureAt;
        static double _lastTimelapseAt;
        static double _lastMilestoneAt;
        static int _timelapseIndex;
        static double _lastScenePumpAt;
        static bool _savedLivePlacement;
        static bool _pendingComposeQueued;
        static bool _hubBuildRecordingSession;
        static bool _manualRecordingSession;
        static bool _composeHoldForPostBuildPlaythrough;
        static bool _timelapsePausedForPostBuild;
        static bool _urpSceneCaptureBlocked;
        static double _urpBlockLoggedAt;
        static double _lastManualRenderAt;
        /// <summary>User clicked Stop recording — do not auto-start a new capture while this build runs.</summary>
        static bool _userFinalizedCapture;
        /// <summary>Planner Q&amp;A — browser wizard frames only; Unity Scene timelapse starts when generation runs.</summary>
        static bool _deferUnitySceneCapture;
        static string _currentBuildModeLabel = "Hub build";
        static readonly List<ChapterEntry> Chapters = new();

        [Serializable]
        sealed class ChapterEntry
        {
            public string id;
            public string title;
            public string phase;
            public string source;
            public string utc;
        }

        [Serializable]
        sealed class ChaptersDoc
        {
            public int version = 1;
            public ChapterEntry[] chapters;
        }

        static CaveBuildDemoAutoRecorder()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        public static bool AutoEnabled
        {
            get => EditorPrefs.GetBool(PrefEnabled, false);
            set => EditorPrefs.SetBool(PrefEnabled, value);
        }

        /// <summary>Continuous Scene timelapse + auto-edit (not slideshow holds).</summary>
        public static bool SmartTimelapseEnabled
        {
            get => EditorPrefs.GetBool(PrefSmartTimelapse, true);
            set => EditorPrefs.SetBool(PrefSmartTimelapse, value);
        }

        public static double TimelapseIntervalSeconds
        {
            get => EditorPrefs.GetFloat(PrefTimelapseInterval, (float)DefaultTimelapseIntervalSeconds);
            set => EditorPrefs.SetFloat(PrefTimelapseInterval, (float)Math.Max(0.5, value));
        }

        public static float TargetRecapMinutes
        {
            get => EditorPrefs.GetFloat(PrefTargetMinutes, DefaultTargetMinutes);
            set => EditorPrefs.SetFloat(PrefTargetMinutes, Mathf.Clamp(value, 3f, 20f));
        }

        public static bool ProducerNarratorEnabled
        {
            get => EditorPrefs.GetBool("CaveBuildDemo.ProducerNarrator", true);
            set => EditorPrefs.SetBool("CaveBuildDemo.ProducerNarrator", value);
        }

        /// <summary>Repaint Scene view during timelapse so background builds stay visible in captures.</summary>
        public static bool ForceBackgroundSceneUpdates
        {
            get => EditorPrefs.GetBool(PrefForceBackgroundScene, true);
            set => EditorPrefs.SetBool(PrefForceBackgroundScene, value);
        }

        public static bool KeepSceneViewLiveForRecording
        {
            get
            {
                if (!_recording || _deferUnitySceneCapture)
                    return false;

                var settings = CaveBuildCursorSettings.LoadOrCreate();
                settings.LoadFromPrefs();
                return settings.forceLivePreviewWhenRecording ||
                       (SmartTimelapseEnabled && ForceBackgroundSceneUpdates);
            }
        }

        public static bool IsRecording => _recording;

        public static string LastOutputFolder => EditorPrefs.GetString(PrefLastFolder, string.Empty);

        /// <summary>Active demo capture folder (recording session or last run).</summary>
        public static string CurrentCaptureFolder =>
            !string.IsNullOrEmpty(_runFolder) ? _runFolder : LastOutputFolder;

        public static bool IsFfmpegAvailable => !string.IsNullOrEmpty(FindFfmpeg());

        public static string FfmpegPath => FindFfmpeg();

        public static void RevealLastOutput()
        {
            var path = LastOutputFolder;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                EditorUtility.RevealInFinder(path);
        }

        [Serializable]
        sealed class DemoRecapFrameManifestData
        {
            public string buildMode;
            public FrameManifestEntry[] frames;
        }

        [Serializable]
        sealed class FrameManifestEntry
        {
            public string image;
            public string phase;
            public string sub;
            public string line1;
            public string line2;
            public string line3;
        }

        public static List<FrameInfo> LoadFramesFromManifest(string runFolder)
        {
            var list = new List<FrameInfo>();
            if (string.IsNullOrEmpty(runFolder))
                return list;

            var manifest = Path.Combine(runFolder, "DemoRecapFrameManifest.json");
            if (File.Exists(manifest))
            {
                try
                {
                    var data = JsonUtility.FromJson<DemoRecapFrameManifestData>(File.ReadAllText(manifest));
                    if (data?.frames != null)
                    {
                        foreach (var e in data.frames)
                        {
                            if (string.IsNullOrEmpty(e.image) || !File.Exists(e.image))
                                continue;
                            list.Add(new FrameInfo
                            {
                                imagePath = e.image,
                                phase = e.phase,
                                sub = e.sub,
                                line1 = e.line1,
                                line2 = e.line2,
                                line3 = e.line3,
                                duration = DefaultFrameDurationSeconds,
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DemoRecorder] Could not read manifest: " + ex.Message);
                }
            }

            if (list.Count > 0)
                return list;

            var framesDir = Path.Combine(runFolder, "frames");
            if (!Directory.Exists(framesDir))
                return list;

            var pngs = Directory.GetFiles(framesDir, "frame_*.png");
            Array.Sort(pngs, StringComparer.OrdinalIgnoreCase);
            foreach (var png in pngs)
            {
                list.Add(new FrameInfo
                {
                    imagePath = png,
                    line1 = "Build checkpoint",
                    duration = DefaultFrameDurationSeconds,
                });
            }

            return list;
        }

        public static string InstallHelpText =>
            "Install ffmpeg on macOS:\n" +
            "1) Install Homebrew:\n" +
            "   /bin/bash -c \"$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\"\n" +
            "2) Install ffmpeg:\n" +
            "   brew install ffmpeg\n" +
            "3) Re-open Unity Hub window and verify ffmpeg status.";

        public static void OpenInstallGuide()
        {
            Application.OpenURL("https://ffmpeg.org/download.html#build-mac");
            Debug.Log("[DemoRecorder] " + InstallHelpText);
        }

        public static void VerifyFfmpegAndReport()
        {
            if (IsFfmpegAvailable)
            {
                Debug.Log("[DemoRecorder] ffmpeg found: " + FfmpegPath);
                return;
            }

            Debug.LogWarning("[DemoRecorder] ffmpeg not found.\n" + InstallHelpText);
        }

        /// <summary>Manual start from Hub Demo recording section (independent of auto-record toggle).</summary>
        public static void StartRecordingManual()
        {
            if (_recording)
                return;

            _userFinalizedCapture = false;
            _manualRecordingSession = true;
            BeginRecordingSession();
        }

        /// <summary>
        /// Stop Scene timelapse capture only; run full presentation compose (DemoRecapPresentation.mp4).
        /// Hub build queue keeps running — this is not Pause or Stop All Builds.
        /// </summary>
        public static void StopRecordingAndCompose()
        {
            if (!_recording)
                return;

            _userFinalizedCapture = true;
            _manualRecordingSession = false;
            _hubBuildRecordingSession = false;
            Debug.Log(
                "[DemoRecorder] Stop recording — capture ending; full presentation compose will run. " +
                "Build queue continues (not paused).");
            EndRecordingSession();
        }

        /// <summary>Hub build buttons always capture a recap for this session (Build Complete / Surface / Cave / AAA).</summary>
        public static void OnHubBuildStarting(string buildModeLabel = null)
        {
            _userFinalizedCapture = false;
            _hubBuildRecordingSession = true;
            if (!string.IsNullOrEmpty(buildModeLabel))
                _currentBuildModeLabel = buildModeLabel;

            var isPlannerOpen = string.Equals(buildModeLabel, "AI build planner", StringComparison.OrdinalIgnoreCase);
            var isBuildPipeline = string.Equals(buildModeLabel, "Build pipeline", StringComparison.OrdinalIgnoreCase);

            if (isPlannerOpen)
            {
                if (!_recording)
                    BeginRecordingSession(deferUnitySceneCapture: true);
                else
                    WriteCaptureSessionManifest(_currentBuildModeLabel);
            }
            else if (isBuildPipeline)
            {
                if (!_recording)
                    BeginRecordingSession();
                else
                {
                    WriteCaptureSessionManifest(_currentBuildModeLabel);
                    ResumeUnitySceneCapture();
                }
            }
            else if (!_recording)
                BeginRecordingSession();
            else
                WriteCaptureSessionManifest(_currentBuildModeLabel);

            if (!string.IsNullOrEmpty(buildModeLabel))
                Debug.Log("[DemoRecorder] Hub build recording armed — " + buildModeLabel);
        }

        static void TryResumeSceneCaptureAfterWizardFinalize()
        {
            if (!_recording || !_deferUnitySceneCapture || string.IsNullOrEmpty(_runFolder))
                return;

            var metaPath = Path.Combine(_runFolder, "wizard", "wizard-capture-meta.json");
            if (!File.Exists(metaPath))
                return;

            AppendChapter("wizard_complete", "Planning complete", "wizard_complete", "wizard");
            ResumeUnitySceneCapture();
        }

        /// <summary>Start Scene timelapse after planner finalize — generation is about to run.</summary>
        static void ResumeUnitySceneCapture()
        {
            if (!_recording || !_deferUnitySceneCapture)
                return;

            _deferUnitySceneCapture = false;
            WriteCaptureSessionManifest(_currentBuildModeLabel);
            if (SmartTimelapseEnabled)
            {
                var settings = CaveBuildCursorSettings.LoadOrCreate();
                settings.LoadFromPrefs();
                _savedLivePlacement = settings.showLiveScenePlacement;
                if (settings.forceLivePreviewWhenRecording || ForceBackgroundSceneUpdates)
                {
                    settings.showLiveScenePlacement = true;
                    settings.stabilizationMode = false;
                    settings.cinematicSceneCamera = true;
                    settings.SaveToPrefs();
                }

                CaveBuildLiveSceneFeedback.PushDemoRecordingSession();
                TryCaptureTimelapseFrame(force: true);
                TryRecordMilestone(CaveBuildDemoNarration.BuildStartLines, "start", string.Empty, force: true);
            }
            else
            {
                TryCaptureFrame(CaveBuildDemoNarration.BuildStartLines, "start", string.Empty, force: true);
            }

            Debug.Log("[DemoRecorder] Unity Scene capture resumed — world generation starting.");
        }

        /// <summary>Chapter marker for recap compose (wizard + Unity share one manifest).</summary>
        public static void AppendChapter(string chapterId, string title, string phase = "", string source = "unity")
        {
            if (string.IsNullOrEmpty(_runFolder))
                return;

            Chapters.Add(new ChapterEntry
            {
                id = chapterId ?? string.Empty,
                title = title ?? string.Empty,
                phase = phase ?? string.Empty,
                source = source ?? "unity",
                utc = DateTime.UtcNow.ToString("o"),
            });
            FlushChaptersToDisk();
        }

        /// <summary>Compose recap when the user stops the build in Hub (Pause, emergency stop).</summary>
        public static void TryFinalizeOnUserStop(string reason)
        {
            if (!_recording && !_hubBuildRecordingSession)
                return;

            _hubBuildRecordingSession = false;
            if (!_recording)
                return;

            Debug.Log("[DemoRecorder] Finalizing recap (" + reason + ").");
            EndRecordingSession();
        }

        /// <summary>Compose recap when the queued pipeline finishes or is aborted.</summary>
        public static void TryFinalizeOnBuildSessionEnd()
        {
            if (_composeHoldForPostBuildPlaythrough)
                return;

            if (!_recording && !_hubBuildRecordingSession)
                return;

            _hubBuildRecordingSession = false;
            if (!_recording)
                return;

            EndRecordingSession();
        }

        public static void HoldComposeForPostBuildPlaythrough()
        {
            _composeHoldForPostBuildPlaythrough = true;
            _timelapsePausedForPostBuild = true;
            _hubBuildRecordingSession = true;
        }

        public static void ReleaseComposeHoldAndFinalize()
        {
            if (!_composeHoldForPostBuildPlaythrough && !_recording && !_hubBuildRecordingSession)
                return;

            _composeHoldForPostBuildPlaythrough = false;
            _timelapsePausedForPostBuild = false;
            TryFinalizeOnBuildSessionEnd();
        }

        /// <summary>Hub idle but timelapse folder has frames and no presentation MP4 — finish compose.</summary>
        const double MinIdleSecondsBeforeOrphanCompose = 90.0;

        public static void TryComposeOrphanedCaptureIfIdle()
        {
            if (_recording || CaveBuildHubSessionReconcile.IsPacedWorkActive())
                return;

            if (CaveBuildPostBuildFinalizeGate.IsActive)
                return;

            var sinceEnd = EditorApplication.timeSinceStartup - CaveBuildRunStatusPublisher.LastSessionEndedAt;
            if (CaveBuildRunStatusPublisher.LastSessionEndedAt > 0 &&
                sinceEnd < MinIdleSecondsBeforeOrphanCompose)
                return;

            var folder = !string.IsNullOrEmpty(_runFolder) ? _runFolder : LastOutputFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;

            if (CaveBuildRecapComposeLatch.HasValidPresentationOutput(folder))
                return;

            if (CaveBuildRecapComposeLatch.IsRecordingEndHandled(folder))
                return;

            var timelapseDir = Path.Combine(folder, "timelapse");
            if (!Directory.Exists(timelapseDir))
                return;

            var frames = Directory.GetFiles(timelapseDir, "tl_*.png");
            if (frames.Length < 8)
                return;

            _runFolder = folder;
            _framesFolder = Path.Combine(folder, "frames");
            _timelapseFolder = timelapseDir;
            _segmentsFolder = Path.Combine(folder, "segments");
            _hubBuildRecordingSession = true;
            _recording = true;
            Debug.Log(
                "[DemoRecorder] Orphaned timelapse detected — composing recap video from " +
                $"{frames.Length} frame(s): {folder}");
            TryFinalizeOnBuildSessionEnd();
        }

        public static void CaptureNow(string reason = "manual checkpoint")
        {
            if (!_recording)
                BeginRecordingSession();
            var lines = new CaveBuildDemoNarration.Lines(
                "Manual checkpoint.",
                reason,
                null);
            TryCaptureFrame(lines, "manual", reason, force: true);
        }

        static void OnEditorUpdate()
        {
            TryDrainPendingCompose();
            TryResumeRecapReviewGate();
            TryResumeSceneCaptureAfterWizardFinalize();

            var hubSession = _hubBuildRecordingSession;
            var manualSession = _manualRecordingSession;
            if (!AutoEnabled && !hubSession && !manualSession)
                return;

            if (!AutoEnabled && !hubSession && manualSession)
            {
                PumpSceneViewForRecording();
                if (SmartTimelapseEnabled)
                    TryCaptureTimelapseFrame();
                return;
            }

            if (!AutoEnabled && hubSession && !manualSession)
            {
                // Hub build session recording without the legacy toggle.
            }
            else if (!AutoEnabled && !hubSession && _recording)
            {
                EndRecordingSession();
                return;
            }

            var active = LavaTubeCaveBuilder.IsBuildInProgress ||
                         CaveBuildStartupCoordinator.IsActive ||
                         CaveBuildRunStatusPublisher.HasActiveSession;

            if ((AutoEnabled || hubSession) && active && !_recording && !_userFinalizedCapture)
                BeginRecordingSession();
            else             if (!active && _recording && hubSession && IsPipelineFullyComplete() &&
                     !_composeHoldForPostBuildPlaythrough)
                TryFinalizeOnBuildSessionEnd();

            if (!active && !hubSession && _recording && !_composeHoldForPostBuildPlaythrough)
                EndRecordingSession();

            if (!_recording)
                return;

            if (!_deferUnitySceneCapture)
            {
                PumpSceneViewForRecording();

                if (SmartTimelapseEnabled)
                    TryCaptureTimelapseFrame();
            }

            if (EditorApplication.isPlaying)
                return;

            var phase = CaveBuildRunStatusPublisher.Phase ?? string.Empty;
            var sub = CaveBuildRunStatusPublisher.FormatSubActionSummary() ?? string.Empty;
            var step = CaveBuildRunStatusPublisher.CurrentQueuedStep;
            var changed = step != _lastStep ||
                          !string.Equals(phase, _lastPhase, StringComparison.Ordinal) ||
                          !string.Equals(sub, _lastSub, StringComparison.Ordinal);
            if (!changed || _deferUnitySceneCapture)
                return;

            _lastStep = step;
            _lastPhase = phase;
            _lastSub = sub;
            var mode = CaveBuildRunStatusPublisher.BuildMode ?? string.Empty;
            var lines = CaveBuildDemoNarration.ForCheckpoint(phase, sub, step, mode);

            if (SmartTimelapseEnabled)
                TryRecordMilestone(lines, phase, sub, force: false);
            else
                TryCaptureFrame(lines, phase, sub, force: false);
        }

        static void OnEditorQuitting()
        {
            if (_recording)
                EndRecordingSession();
        }

        static void OnBeforeAssemblyReload()
        {
            if (!_recording)
                return;

            _recording = false;
            if (!string.IsNullOrEmpty(_runFolder) &&
                CaveBuildRecapComposeLatch.IsComposeStarted(_runFolder) &&
                CaveBuildDemoProducerCompose.NeedsPersonalVoiceNarration(_runFolder))
            {
                EditorPrefs.SetString(PrefPendingCompose, _runFolder);
                Debug.LogWarning(
                    "[DemoRecorder] Assembly reload interrupted Personal Voice narration — will resume narration only on next editor tick: " +
                    _runFolder);
                return;
            }

            CleanupSessionIntermediates(_runFolder);
        }

        static void BeginRecordingSession(bool deferUnitySceneCapture = false)
        {
            try
            {
                PurgeAllCaptureRunFolders(keepRunFolder: null);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                _runFolder = Path.Combine(EnvironmentKitDataRoot.ResolvePath(CaptureFolderName), stamp);
                _framesFolder = Path.Combine(_runFolder, "frames");
                _timelapseFolder = Path.Combine(_runFolder, "timelapse");
                _segmentsFolder = Path.Combine(_runFolder, "segments");
                Directory.CreateDirectory(_framesFolder);
                Directory.CreateDirectory(_timelapseFolder);
                Directory.CreateDirectory(_segmentsFolder);
                Directory.CreateDirectory(Path.Combine(_runFolder, "wizard", "frames"));
                Frames.Clear();
                Milestones.Clear();
                Chapters.Clear();
                _recording = true;
                _deferUnitySceneCapture = deferUnitySceneCapture;
                _lastPhase = string.Empty;
                _lastSub = string.Empty;
                _lastNarration = string.Empty;
                _lastStep = -1;
                _lastCaptureAt = 0;
                _lastTimelapseAt = 0;
                _lastMilestoneAt = 0;
                _timelapseIndex = 0;
                CaveBuildRecapComposeLatch.ClearRecordingEndHandled(_runFolder);
                EditorPrefs.SetString(PrefLastFolder, _runFolder);
                WriteCaptureSessionManifest(_currentBuildModeLabel);
                AppendChapter("session_start", "Capture session armed", "start", "unity");
                if (deferUnitySceneCapture)
                {
                    AppendChapter("planner_open", "AI Planning Session", "wizard", "wizard");
                    Debug.Log(
                        "[DemoRecorder] Planner recording — browser UI capture only until world generation starts: " +
                        _runFolder);
                }
                else if (SmartTimelapseEnabled)
                {
                    var settings = CaveBuildCursorSettings.LoadOrCreate();
                    settings.LoadFromPrefs();
                    _savedLivePlacement = settings.showLiveScenePlacement;
                    if (settings.forceLivePreviewWhenRecording || ForceBackgroundSceneUpdates)
                    {
                        settings.showLiveScenePlacement = true;
                        settings.stabilizationMode = false;
                        settings.cinematicSceneCamera = true;
                        settings.SaveToPrefs();
                    }

                    CaveBuildLiveSceneFeedback.PushDemoRecordingSession();
                    TryCaptureTimelapseFrame(force: true);
                    TryRecordMilestone(CaveBuildDemoNarration.BuildStartLines, "start", string.Empty, force: true);
                    Debug.Log("[DemoRecorder] Smart timelapse recording started (continuous Scene capture + auto-edit): " +
                              _runFolder);
                }
                else
                {
                    TryCaptureFrame(CaveBuildDemoNarration.BuildStartLines, "start", string.Empty, force: true);
                    Debug.Log("[DemoRecorder] Slideshow recording started: " + _runFolder);
                }
            }
            catch (Exception ex)
            {
                _recording = false;
                Debug.LogWarning("[DemoRecorder] Could not start recorder: " + ex.Message);
            }
        }

        static void EndRecordingSession()
        {
            _recording = false;
            _deferUnitySceneCapture = false;
            AppendChapter("session_end", "Capture session complete", "end", "unity");
            WriteCaptureSessionManifest(_currentBuildModeLabel);

            if (!string.IsNullOrEmpty(_runFolder) &&
                CaveBuildRecapComposeLatch.IsRecordingEndHandled(_runFolder))
            {
                Debug.Log("[DemoRecorder] Recording end already handled for run — skipping: " + _runFolder);
                return;
            }

            if (!string.IsNullOrEmpty(_runFolder))
                CaveBuildRecapComposeLatch.MarkRecordingEndHandled(_runFolder);

            var buildMode = CaveBuildRunStatusPublisher.BuildMode ?? string.Empty;
            var wasSmart = SmartTimelapseEnabled;
            if (wasSmart)
                CaveBuildLiveSceneFeedback.PopDemoRecordingSession();

            if (SmartTimelapseEnabled)
            {
                TryCaptureTimelapseFrame(force: true);
                TryRecordMilestone(CaveBuildDemoNarration.BuildEndLines, "complete", string.Empty, force: true);
                CaveBuildDemoNarrationAi.TryEnhanceFrames(Milestones, buildMode, _runFolder);
                WriteTimelineJson(buildMode);
                WriteRecapNotes();
                CaveBuildRecapDashboardGate.BeginOrRunCompose(_runFolder, () => RunPostRecordingCompose(_runFolder));
            }
            else
            {
                TryCaptureFrame(CaveBuildDemoNarration.BuildEndLines, "complete", string.Empty, force: true);
                CaveBuildDemoNarrationAi.TryEnhanceFrames(Frames, buildMode, _runFolder);
                WriteRecapNotes();
                CaveBuildRecapDashboardGate.BeginOrRunCompose(_runFolder, () => RunPostRecordingCompose(_runFolder));
            }

            RestoreLivePlacementAfterRecording(wasSmart);
            if (CaveBuildRecapDashboardGate.IsWaitingForProceed)
            {
                Debug.Log(
                    "[DemoRecorder] Recording captured — recap review open in browser. " +
                    "Compose starts when you click Proceed in the dashboard (or auto-proceed timeout).");
            }
            else
            {
                Debug.Log(
                    "[DemoRecorder] Recording completed — full presentation compose started " +
                    "(DemoRecapPresentation.mp4 on data volume; Personal Voice in Terminal when enabled).");
            }
        }

        static void RunPostRecordingCompose(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return;

            var serverStarted = Path.Combine(runFolder, "RecapComposeStarted.marker");
            if (File.Exists(serverStarted))
            {
                Debug.Log(
                    "[DemoRecorder] Compose already started by AI Director — skipping Unity duplicate. " +
                    "Watch Terminal for Personal Voice narration.");
                return;
            }

            if (CaveBuildRecapComposeLatch.IsComposeCompleted(runFolder))
            {
                Debug.Log("[DemoRecorder] Compose already completed for run — skipping: " + runFolder);
                return;
            }

            if (!CaveBuildRecapComposeLatch.TryMarkComposeStarted(runFolder))
            {
                Debug.Log("[DemoRecorder] Compose already started for run — skipping: " + runFolder);
                return;
            }

            var composeFinished = false;
            try
            {
                if (SmartTimelapseEnabled || Directory.Exists(Path.Combine(runFolder, "timelapse")))
                {
                    if (CaveBuildDemoProducerCompose.TryComposeProducer(runFolder, previewOnly: false, out var recapPath))
                    {
                        Debug.Log("[DemoRecorder] Producer recap ready: " + recapPath);
                        composeFinished = true;
                    }
                    else if (!CaveBuildDemoSmartCompose.TryComposeSmartVideo(runFolder))
                    {
                        TryBuildVideoWithFfmpeg();
                        CaveBuildDemoProducerCompose.EnsurePersonalVoiceNarration(runFolder);
                    }
                    else
                    {
                        composeFinished = true;
                    }
                }
                else
                {
                    TryBuildVideoWithFfmpeg();
                    composeFinished = true;
                }

                WriteFrameManifest();
                CleanupSessionIntermediates(runFolder);
                Debug.Log("[DemoRecorder] Post-recording compose finished.");
            }
            finally
            {
                if (CaveBuildRecapComposeLatch.HasValidPresentationOutput(runFolder) || composeFinished)
                    CaveBuildRecapComposeLatch.MarkComposeCompleted(runFolder);
            }
        }

        static void TryResumeRecapReviewGate()
        {
            if (_recording || _pendingComposeQueued)
                return;

            CaveBuildRecapDashboardGate.TryResumePendingGate(RunPostRecordingCompose);
        }

        static void RestoreLivePlacementAfterRecording(bool wasSmart)
        {
            if (!wasSmart || !ForceBackgroundSceneUpdates)
                return;

            try
            {
                var settings = CaveBuildCursorSettings.LoadOrCreate();
                settings.LoadFromPrefs();
                if (settings.showLiveScenePlacement != _savedLivePlacement)
                {
                    settings.showLiveScenePlacement = _savedLivePlacement;
                    settings.SaveToPrefs();
                }
            }
            catch
            {
                // ignore
            }
        }

        static void PumpSceneViewForRecording()
        {
            if (!KeepSceneViewLiveForRecording)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastScenePumpAt < ScenePumpIntervalSeconds)
                return;
            _lastScenePumpAt = now;

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        static void TryCaptureTimelapseFrame(bool force = false)
        {
            if (_timelapsePausedForPostBuild && !force)
                return;

            if (!_recording && !force)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (!force && now - _lastTimelapseAt < TimelapseIntervalSeconds)
                return;
            _lastTimelapseAt = now;

            if (!TryCaptureSceneView(out var pngBytes))
                return;

            _timelapseIndex++;
            var file = Path.Combine(_timelapseFolder, $"tl_{_timelapseIndex:000000}.png");
            File.WriteAllBytes(file, pngBytes);
        }

        static void TryRecordMilestone(CaveBuildDemoNarration.Lines lines, string phase, string sub, bool force)
        {
            if (!_recording && !force)
                return;

            var now = EditorApplication.timeSinceStartup;
            var key = $"{lines.Line1}|{lines.Line2}|{lines.Line3}";
            if (!force)
            {
                if (string.Equals(key, _lastNarration, StringComparison.Ordinal))
                    return;

                var routineRepeat = IsRoutineSubStep(sub) && now - _lastMilestoneAt < 45.0;
                if (routineRepeat)
                    return;
            }

            _lastNarration = key;
            _lastMilestoneAt = now;

            var frameIndex = Math.Max(0, _timelapseIndex);
            var imagePath = frameIndex > 0
                ? Path.Combine(_timelapseFolder, $"tl_{frameIndex:000000}.png")
                : string.Empty;
            Milestones.Add(new FrameInfo
            {
                imagePath = imagePath,
                timelapseFrame = frameIndex,
                phase = phase,
                sub = sub,
                line1 = lines.Line1,
                line2 = lines.Line2,
                line3 = lines.Line3,
                duration = DefaultFrameDurationSeconds,
            });

            if (!SmartTimelapseEnabled)
                return;

            Frames.Add(Milestones[Milestones.Count - 1]);
        }

        static bool IsRoutineSubStep(string sub)
        {
            if (string.IsNullOrEmpty(sub))
                return false;
            var s = sub.ToLowerInvariant();
            return s.Contains("tile ") && s.Contains("/") ||
                   s.Contains("rung") ||
                   s.Contains("crater repair") ||
                   s.Contains("macro step");
        }

        static void WriteTimelineJson(string buildMode)
        {
            if (string.IsNullOrEmpty(_runFolder))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"captureIntervalSec\": {TimelapseIntervalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"targetDurationSec\": {(TargetRecapMinutes * 60f).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"buildMode\": \"{EscapeJson(buildMode)}\",");
            sb.AppendLine($"  \"narratorEnabled\": {(ProducerNarratorEnabled ? "true" : "false")},");
            sb.AppendLine("  \"recapMode\": \"presentation_pro\",");
            sb.AppendLine("  \"introTitle\": \"World Build Recap\",");
            sb.AppendLine("  \"introSubtitle\": \"Full pipeline timelapse — auto-edited with educational milestones\",");
            sb.AppendLine("  \"outroTitle\": \"Pipeline complete\",");
            sb.AppendLine("  \"outroSubtitle\": \"Routine work is sped up; milestones pause with captions\",");
            sb.AppendLine("  \"milestones\": [");
            for (var i = 0; i < Milestones.Count; i++)
            {
                var m = Milestones[i];
                if (i > 0)
                    sb.AppendLine(",");
                var frame = m.timelapseFrame > 0
                    ? m.timelapseFrame - 1
                    : Math.Min(i * Math.Max(1, _timelapseIndex - 1) / Math.Max(1, Milestones.Count - 1), Math.Max(0, _timelapseIndex - 1));
                sb.Append("    {");
                sb.Append($"\"frame\":{frame}");
                sb.Append($",\"phase\":\"{EscapeJson(m.phase ?? "")}\"");
                sb.Append($",\"sub\":\"{EscapeJson(m.sub ?? "")}\"");
                sb.Append($",\"chapter\":\"{EscapeJson(string.IsNullOrEmpty(m.phase) ? $"Milestone {i + 1}" : m.phase)}\"");
                sb.Append($",\"line1\":\"{EscapeJson(m.line1 ?? "")}\"");
                sb.Append($",\"line2\":\"{EscapeJson(m.line2 ?? "")}\"");
                sb.Append($",\"line3\":\"{EscapeJson(m.line3 ?? "")}\"");
                sb.Append('}');
            }

            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(_runFolder, "DemoRecapTimeline.json"), sb.ToString());
        }

        static int MilestoneFrameIndex(int milestoneIndex)
        {
            if (_timelapseIndex <= 0)
                return 0;
            if (milestoneIndex <= 0)
                return 0;
            if (milestoneIndex >= Milestones.Count - 1)
                return Math.Max(0, _timelapseIndex - 1);
            return (milestoneIndex * Math.Max(1, _timelapseIndex - 1)) / Math.Max(1, Milestones.Count - 1);
        }

        static int IndexFromTimelapsePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;
            var name = Path.GetFileNameWithoutExtension(path);
            var parts = name.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[^1], out var n))
                return n;
            return 0;
        }

        static void TryCaptureFrame(
            CaveBuildDemoNarration.Lines lines,
            string phase,
            string sub,
            bool force)
        {
            if (!_recording && !force)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (!force && now - _lastCaptureAt < MinCaptureIntervalSeconds)
                return;
            _lastCaptureAt = now;

            var key = $"{lines.Line1}|{lines.Line2}|{lines.Line3}";
            if (!force && string.Equals(key, _lastNarration, StringComparison.Ordinal))
                return;

            if (!TryCaptureSceneView(out var pngBytes))
                return;

            _lastNarration = key;
            var idx = Frames.Count + 1;
            var file = Path.Combine(_framesFolder, $"frame_{idx:0000}.png");
            File.WriteAllBytes(file, pngBytes);
            Frames.Add(new FrameInfo
            {
                imagePath = file,
                phase = phase,
                sub = sub,
                line1 = lines.Line1,
                line2 = lines.Line2,
                line3 = lines.Line3,
                duration = DefaultFrameDurationSeconds,
            });
        }

        static bool TryCaptureSceneView(out byte[] pngBytes)
        {
            if (EditorApplication.isPlaying && TryCaptureGameplayCamera(out pngBytes))
                return true;

            pngBytes = null;
            var view = ResolveSceneViewForCapture();
            if (view == null || view.camera == null)
                return false;

            if (KeepSceneViewLiveForRecording)
            {
                CaveBuildSceneCameraDirector.ApplyDemoCaptureFrame();
                view.Repaint();
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }

            // URP: manual Camera.Render on Scene View re-initializes Blitter and floods the console.
            if (IsUniversalRenderPipelineActive() && IsSceneViewCamera(view.camera))
                return TryCaptureSceneViewWindow(view, out pngBytes);

            return TryRenderCameraToPng(view.camera, out pngBytes);
        }

        static bool IsUniversalRenderPipelineActive()
        {
            return GraphicsSettings.currentRenderPipeline != null;
        }

        static bool IsSceneViewCamera(Camera cam)
        {
            if (cam == null)
                return false;

            foreach (SceneView sv in SceneView.sceneViews)
            {
                if (sv != null && sv.camera == cam)
                    return true;
            }

            return false;
        }

        static bool TryCaptureSceneViewWindow(SceneView view, out byte[] pngBytes)
        {
            pngBytes = null;
            if (view == null)
                return false;

            try
            {
                view.Repaint();
                var rect = view.position;
                var w = Mathf.Max(64, (int)rect.width);
                var h = Mathf.Max(64, (int)rect.height);
                var pixels = InternalEditorUtility.ReadScreenPixel(new Vector2(rect.x, rect.y), w, h);
                if (pixels == null || pixels.Length == 0)
                    return false;

                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.SetPixels(pixels);
                tex.Apply();
                pngBytes = EncodeTextureToPng(tex, w, h);
                UnityEngine.Object.DestroyImmediate(tex);
                return pngBytes != null && pngBytes.Length > 0;
            }
            catch (Exception ex)
            {
                if (!_urpSceneCaptureBlocked)
                {
                    _urpSceneCaptureBlocked = true;
                    Debug.LogWarning(
                        "[DemoRecorder] Scene View window readback failed; timelapse capture paused. " +
                        ex.Message);
                }

                return false;
            }
        }

        static byte[] EncodeTextureToPng(Texture2D tex, int srcW, int srcH)
        {
            if (srcW == Width && srcH == Height)
                return tex.EncodeToPNG();

            var scaled = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            for (var y = 0; y < Height; y++)
            {
                var sy = y * srcH / Height;
                for (var x = 0; x < Width; x++)
                {
                    var sx = x * srcW / Width;
                    scaled.SetPixel(x, y, tex.GetPixel(sx, sy));
                }
            }

            scaled.Apply();
            var png = scaled.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(scaled);
            return png;
        }

        static bool TryCaptureGameplayCamera(out byte[] pngBytes)
        {
            pngBytes = null;
            var cam = Camera.main;
            if (cam == null)
            {
                foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>())
                {
                    if (c != null && c.enabled && c.gameObject.CompareTag("MainCamera"))
                    {
                        cam = c;
                        break;
                    }
                }
            }

            if (cam == null || !cam.enabled)
                return false;

            EditorApplication.QueuePlayerLoopUpdate();
            return TryRenderCameraToPng(cam, out pngBytes);
        }

        static bool TryRenderCameraToPng(Camera cam, out byte[] pngBytes)
        {
            pngBytes = null;
            if (cam == null || _urpSceneCaptureBlocked)
                return false;

            if (IsUniversalRenderPipelineActive() && IsSceneViewCamera(cam))
                return false;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastManualRenderAt < 0.35)
                return false;

            var prevRt = cam.targetTexture;
            var prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                _lastManualRenderAt = now;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                pngBytes = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return pngBytes != null && pngBytes.Length > 0;
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("Blitter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _urpSceneCaptureBlocked = true;
                    if (now - _urpBlockLoggedAt > 2.0)
                    {
                        _urpBlockLoggedAt = now;
                        Debug.LogWarning(
                            "[DemoRecorder] URP Scene capture disabled (Blitter already initialized). " +
                            "Timelapse will use fewer frames until domain reload.");
                    }
                }

                return false;
            }
            finally
            {
                cam.targetTexture = prevRt;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static SceneView ResolveSceneViewForCapture()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
                return view;

            foreach (SceneView sv in SceneView.sceneViews)
            {
                if (sv != null && sv.camera != null)
                    return sv;
            }

            return null;
        }

        static void WriteRecapNotes()
        {
            if (string.IsNullOrEmpty(_runFolder))
                return;

            var md = new StringBuilder();
            md.AppendLine("# Demo Recap");
            md.AppendLine();
            md.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            md.AppendLine($"Frames: {Frames.Count}");
            md.AppendLine();
            md.AppendLine("## Timeline");
            for (var i = 0; i < Frames.Count; i++)
            {
                var f = Frames[i];
                md.AppendLine($"- {(i + 1):D2}. **{f.line1}**");
                if (!string.IsNullOrEmpty(f.line2))
                    md.AppendLine($"  - {f.line2}");
                if (!string.IsNullOrEmpty(f.line3))
                    md.AppendLine($"  - {f.line3}");
            }

            File.WriteAllText(Path.Combine(_runFolder, "DemoRecap.md"), md.ToString());
            WriteFrameManifest();
        }

        static void WriteFrameManifest()
        {
            if (string.IsNullOrEmpty(_runFolder) || Frames.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"buildMode\": \"{EscapeJson(CaveBuildRunStatusPublisher.BuildMode ?? "")}\",");
            sb.AppendLine("  \"frames\": [");
            for (var i = 0; i < Frames.Count; i++)
            {
                var f = Frames[i];
                if (i > 0)
                    sb.AppendLine(",");
                sb.Append("    {");
                sb.Append($"\"image\":\"{EscapeJson(f.imagePath ?? "")}\"");
                sb.Append($",\"phase\":\"{EscapeJson(f.phase ?? "")}\"");
                sb.Append($",\"sub\":\"{EscapeJson(f.sub ?? "")}\"");
                sb.Append($",\"line1\":\"{EscapeJson(f.line1 ?? "")}\"");
                sb.Append($",\"line2\":\"{EscapeJson(f.line2 ?? "")}\"");
                sb.Append($",\"line3\":\"{EscapeJson(f.line3 ?? "")}\"");
                sb.Append('}');
            }

            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(_runFolder, "DemoRecapFrameManifest.json"), sb.ToString());
        }

        static string EscapeJson(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        static void TryBuildVideoWithFfmpeg()
        {
            if (Frames.Count == 0 || string.IsNullOrEmpty(_runFolder))
                return;
            if (string.IsNullOrEmpty(FindFfmpeg()))
            {
                Debug.LogWarning("[DemoRecorder] ffmpeg not found. Captures saved without video render.");
                return;
            }

            var buildMode = CaveBuildRunStatusPublisher.BuildMode ?? "build";
            if (CaveBuildDemoCompose.TryComposeVideo(_runFolder, Frames, buildMode))
                return;

            try
            {
                BuildSegments();
                ConcatSegments();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[DemoRecorder] Video build failed (install Pillow: pip3 install --user pillow). " + ex.Message);
            }
        }

        static void BuildSegments()
        {
            const int barHeight = 168;
            var ffmpeg = FindFfmpeg();
            var font = "/System/Library/Fonts/Supplemental/Arial.ttf";
            for (var i = 0; i < Frames.Count; i++)
            {
                var frame = Frames[i];
                var outSeg = Path.Combine(_segmentsFolder, $"seg_{i:0000}.mp4");
                var l1 = EscapeForDrawText(string.IsNullOrEmpty(frame.line1) ? "Build checkpoint" : frame.line1);
                var l2 = EscapeForDrawText(frame.line2);
                var l3 = EscapeForDrawText(frame.line3);
                var filter =
                    "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:black," +
                    $"drawbox=x=0:y=h-{barHeight}:w=w:h={barHeight}:color=black@0.58:t=fill," +
                    $"drawtext=fontfile={font}:text='{l1}':fontcolor=white:fontsize=21:x=36:y=h-152," +
                    $"drawtext=fontfile={font}:text='{l2}':fontcolor=white@0.92:fontsize=17:x=36:y=h-122";
                if (!string.IsNullOrEmpty(frame.line3))
                    filter += $",drawtext=fontfile={font}:text='{l3}':fontcolor=white@0.85:fontsize=15:x=36:y=h-96";
                var args =
                    $"-y -loop 1 -t {frame.duration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"-i \"{frame.imagePath}\" -vf \"{filter}\" -c:v libx264 -pix_fmt yuv420p \"{outSeg}\"";
                RunProcess(ffmpeg, args, _runFolder);
            }
        }

        static void ConcatSegments()
        {
            var ffmpeg = FindFfmpeg();
            var list = Path.Combine(_runFolder, "segments.txt");
            var sb = new StringBuilder();
            for (var i = 0; i < Frames.Count; i++)
            {
                var seg = Path.Combine(_segmentsFolder, $"seg_{i:0000}.mp4").Replace("\\", "/");
                sb.AppendLine($"file '{seg}'");
            }

            File.WriteAllText(list, sb.ToString());
            var output = Path.Combine(_runFolder, "DemoRecap.mp4");
            RunProcess(ffmpeg, $"-y -f concat -safe 0 -i \"{list}\" -c copy \"{output}\"", _runFolder);
        }

        static string EscapeForDrawText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "Build checkpoint";
            return text
                .Replace("\\", "\\\\")
                .Replace(":", "\\:")
                .Replace("'", "\\'")
                .Replace("%", "\\%")
                .Replace("\n", " ")
                .Replace("\r", " ");
        }

        static string FindFfmpeg()
        {
            var direct = "/opt/homebrew/bin/ffmpeg";
            if (File.Exists(direct))
                return direct;
            var usr = "/usr/local/bin/ffmpeg";
            if (File.Exists(usr))
                return usr;
            var macPorts = "/opt/local/bin/ffmpeg";
            if (File.Exists(macPorts))
                return macPorts;
            var fromPath = FindFfmpegOnPath();
            if (!string.IsNullOrEmpty(fromPath))
                return fromPath;
            return string.Empty;
        }

        static string FindFfmpegOnPath()
        {
            try
            {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/which",
                    Arguments = "ffmpeg",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                p.Start();
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (p.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                    return output;
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        static void RunProcess(string exe, string args, string cwd)
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            p.Start();
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception($"ffmpeg exit {p.ExitCode}: {stderr}\n{stdout}");
        }

        /// <summary>Removes every prior run folder so only the active session writes frames.</summary>
        static void WriteCaptureSessionManifest(string buildModeLabel)
        {
            if (string.IsNullOrEmpty(_runFolder))
                return;

            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var rel = CaptureSessionRel.Replace('\\', '/');
                var abs = Path.Combine(hub, rel);
                var dir = Path.GetDirectoryName(abs);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var wizardUi = _recording && _deferUnitySceneCapture;
                var json =
                    "{\n"
                    + $"  \"recording\": {(_recording ? "true" : "false")},\n"
                    + $"  \"wizardUiCapture\": {(wizardUi ? "true" : "false")},\n"
                    + $"  \"runFolder\": {JsonEscape(_runFolder)},\n"
                    + $"  \"buildModeLabel\": {JsonEscape(buildModeLabel ?? _currentBuildModeLabel)},\n"
                    + $"  \"armedUtc\": {JsonEscape(DateTime.UtcNow.ToString("o"))}\n"
                    + "}\n";
                EnvironmentKitDataRoot.TryWriteAllText(abs, json, "demo capture session");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not write capture session manifest: " + ex.Message);
            }
        }

        static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static void FlushChaptersToDisk()
        {
            if (string.IsNullOrEmpty(_runFolder))
                return;

            try
            {
                var doc = new ChaptersDoc { chapters = Chapters.ToArray() };
                var path = Path.Combine(_runFolder, ChaptersFileName);
                File.WriteAllText(path, JsonUtility.ToJson(doc, true) + "\n");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not write chapters: " + ex.Message);
            }
        }

        static void PurgeAllCaptureRunFolders(string keepRunFolder)
        {
            try
            {
                var root = EnvironmentKitDataRoot.ResolvePath(CaptureFolderName);
                if (!Directory.Exists(root))
                    return;

                var keepFull = string.IsNullOrEmpty(keepRunFolder)
                    ? null
                    : Path.GetFullPath(keepRunFolder);

                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (keepFull != null &&
                        string.Equals(Path.GetFullPath(dir), keepFull, StringComparison.OrdinalIgnoreCase))
                        continue;

                    TryDeleteDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not purge old capture folders: " + ex.Message);
            }
        }

        /// <summary>Deletes segment intermediates after compose; keeps frames/, DemoRecap.mp4, notes, manifests.</summary>
        static void CleanupSessionIntermediates(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return;

            TryDeleteDirectory(Path.Combine(runFolder, "segments"));
            TryDeleteDirectory(Path.Combine(runFolder, "_compose_work"));
            TryDeleteFile(Path.Combine(runFolder, "segments.txt"));
            Frames.Clear();
        }

        static void TryDrainPendingCompose()
        {
            if (CaveBuildRecapDashboardGate.IsWaitingForProceed)
                return;

            var folder = EditorPrefs.GetString(PrefPendingCompose, string.Empty);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                if (!string.IsNullOrEmpty(folder))
                    EditorPrefs.DeleteKey(PrefPendingCompose);
                return;
            }

            if (!CaveBuildRecapComposeLatch.IsComposeStarted(folder))
            {
                EditorPrefs.DeleteKey(PrefPendingCompose);
                return;
            }

            if (CaveBuildRecapComposeLatch.IsComposeCompleted(folder) &&
                !CaveBuildDemoProducerCompose.NeedsPersonalVoiceNarration(folder))
            {
                EditorPrefs.DeleteKey(PrefPendingCompose);
                return;
            }

            if (!CaveBuildDemoProducerCompose.NeedsPersonalVoiceNarration(folder))
            {
                EditorPrefs.DeleteKey(PrefPendingCompose);
                return;
            }

            var hasSilentVideo = CaveBuildRecapComposeLatch.HasValidPresentationOutput(folder) ||
                                 CaveBuildRecapComposeLatch.SilentComposeVideoExists(folder);
            if (!hasSilentVideo)
                return;

            if (_pendingComposeQueued || _recording)
                return;

            _pendingComposeQueued = true;
            EditorApplication.delayCall += () =>
            {
                _pendingComposeQueued = false;
                if (!CaveBuildDemoProducerCompose.NeedsPersonalVoiceNarration(folder))
                {
                    EditorPrefs.DeleteKey(PrefPendingCompose);
                    return;
                }

                Debug.Log("[DemoRecorder] Resuming interrupted Personal Voice narration only: " + folder);
                CaveBuildDemoProducerCompose.EnsurePersonalVoiceNarration(folder);
                EditorPrefs.DeleteKey(PrefPendingCompose);
            };
        }

        static bool IsPipelineFullyComplete()
        {
            var step = CaveBuildRunStatusPublisher.CurrentQueuedStep;
            var total = CaveBuildRunStatusPublisher.QueuedStepTotal;
            if (step >= total - 1)
                return true;

            var phase = CaveBuildRunStatusPublisher.Phase ?? string.Empty;
            var lower = phase.ToLowerInvariant();
            return lower.Contains("complete") || lower.Contains("finish");
        }

        static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            Directory.Delete(path, recursive: true);
        }

        static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            File.Delete(path);
        }
    }
}
#endif
