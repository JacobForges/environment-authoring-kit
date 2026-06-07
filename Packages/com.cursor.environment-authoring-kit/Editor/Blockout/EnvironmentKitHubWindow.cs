#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EnvironmentAuthoringKit.GaussianSplat;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Single-pane operational hub for Environment Kit build controls, settings, and generated data.
    /// </summary>
    public sealed class EnvironmentKitHubWindow : EditorWindow
    {
        const string PrefPinDuringBuild = "EnvironmentKitHub_PinDuringBuild";

        enum Tab
        {
            Build,
            Settings,
            Data
        }

        readonly struct ArtifactItem
        {
            public readonly string label;
            public readonly string relPath;

            public ArtifactItem(string label, string relPath)
            {
                this.label = label;
                this.relPath = relPath;
            }
        }

        static readonly ArtifactItem[] Artifacts =
        {
            new("Live status", CaveBuildRunStatusPublisher.LiveStatusLibraryRel),
            new("Quality report", CaveBuildQualityReport.DefaultExportPath),
            new("Surface quality report", SurfaceTerrainQualityGrader.QualityReportPath),
            new("Phase contracts", CaveBuildPhaseContractRegistry.ContractsExportRel),
            new("Completion contract", CaveBuildCompletionContract.ContractJsonRel),
            new("Visual shell audit", SurfaceCaveRoofAuditor.ReportRel),
            new("Preflight report", CaveBuildFullRunPreflight.ReportRel),
            new("Research gate", CaveBuildPhaseResearchGate.GateRel),
            new("Research action plan", CaveBuildPhaseResearchGate.ActionPlanRel),
        };

        Tab _tab;
        Vector2 _scroll;
        Vector2 _pipelineLogScroll;
        int _lastPipelineLogLength;
        CaveBuildCursorSettings _settings;
        UnityEditor.Editor _settingsEditor;
        bool _showAdvancedSettings;
        bool _showRecoveryBuild;
        string _apiKey;
        string _googleApiKey;
        string _anthropicApiKey;
        string _openAiApiKey;
        string _openRouterApiKey;
        string _customApiKey;
        bool _showApiKey;
        int _selectedArtifact;
        string _artifactPreview = string.Empty;
        string _lastDataError = string.Empty;
        double _nextRefreshAt;
        double _nextPinAt;
        bool _pinDuringBuild = true;
        bool _showCompletionPanel;
        bool _showGradeDuringBuild;
        string _completionTitle = string.Empty;
        string _completionSummary = string.Empty;
        string _caveLavaFolders;
        string _cavePropFolders;
        bool _caveScanAllAssets;
        int _generationStyleIndex;

        public static bool IsOpen =>
            Resources.FindObjectsOfTypeAll<EnvironmentKitHubWindow>().Length > 0;

        [MenuItem(CaveBuildMenuPaths.Hub, false, -100)]
        public static void Open()
        {
            var window = GetWindow<EnvironmentKitHubWindow>("Environment Kit Hub");
            window.minSize = new Vector2(620f, 560f);
            // Utility window stays floating and tends to remain on top of docked editor panes.
            window.ShowUtility();
            window.Focus();
        }

        /// <summary>One Hub for build monitoring — closes duplicate Pipeline Console to save editor RAM.</summary>
        public static void EnsureOpenForBuild()
        {
            CloseLegacyPipelineConsole();
            var windows = Resources.FindObjectsOfTypeAll<EnvironmentKitHubWindow>();
            if (windows.Length > 0)
            {
                windows[0]._tab = Tab.Build;
                windows[0].Focus();
                return;
            }

            Open();
        }

        public static bool TryFocusBuildMonitor()
        {
            var windows = Resources.FindObjectsOfTypeAll<EnvironmentKitHubWindow>();
            if (windows.Length == 0)
                return false;

            windows[0]._tab = Tab.Build;
            windows[0].Focus();
            return true;
        }

        public static void CloseLegacyPipelineConsole()
        {
            foreach (var console in Resources.FindObjectsOfTypeAll<CaveBuildPipelineConsoleWindow>())
                console.Close();
        }

        public static void NotifyBuildCompleted(string title, string shortSummary)
        {
            foreach (var hub in Resources.FindObjectsOfTypeAll<EnvironmentKitHubWindow>())
            {
                hub._completionTitle = title ?? string.Empty;
                hub._completionSummary = shortSummary ?? string.Empty;
                hub._showCompletionPanel = true;
                hub._tab = Tab.Build;
                hub.Focus();
                hub.Repaint();
            }
        }

        [MenuItem(CaveBuildMenuPaths.Root + "Hub/Bring Hub To Front", false, -99)]
        public static void BringToFront() => EnsureOpenForBuild();

        [MenuItem(CaveBuildMenuPaths.Root + "Hub/Force Bring Hub To Front (During Build)", false, -98)]
        public static void ForceBringToFront() => EnsureOpenForBuild();

        void OnEnable()
        {
            _settings = CaveBuildCursorSettings.LoadOrCreate();
            _settings.LoadFromPrefs();
            _apiKey = _settings.GetApiKey();
            _googleApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.GoogleGemini);
            _anthropicApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.AnthropicClaude);
            _openAiApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.OpenAICompatible);
            _openRouterApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.OpenRouter);
            _customApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.CustomEndpoint);
            _pinDuringBuild = EditorPrefs.GetBool(PrefPinDuringBuild, true);
            _generationStyleIndex = FullWorldGenerationStylePreset.LoadSelectedIndex();
            LoadPrefabFolderPrefs();
            _selectedArtifact = Mathf.Clamp(_selectedArtifact, 0, Artifacts.Length - 1);
            RefreshArtifactPreview();
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (_settingsEditor != null)
                DestroyImmediate(_settingsEditor);
        }

        void OnEditorUpdate()
        {
            var buildActive =
                LavaTubeCaveBuilder.IsBuildInProgress ||
                CaveBuildStartupCoordinator.IsActive ||
                CaveBuildRunStatusPublisher.HasActiveSession;
            var interval = _tab == Tab.Build && buildActive ? 0.2 : buildActive ? 0.4 : 1.2;
            if (EditorApplication.timeSinceStartup < _nextRefreshAt)
                return;

            _nextRefreshAt = EditorApplication.timeSinceStartup + interval;
            if (_pinDuringBuild &&
                LavaTubeCaveBuilder.IsBuildInProgress &&
                EditorApplication.timeSinceStartup >= _nextPinAt)
            {
                _nextPinAt = EditorApplication.timeSinceStartup + 1.5;
                Focus();
            }

            if (_tab == Tab.Data)
                RefreshArtifactPreview();
            Repaint();
        }

        void OnGUI()
        {
            DrawHeader();
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Build", "Settings", "Data" });
            EditorGUILayout.Space(6f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                switch (_tab)
                {
                    case Tab.Build:
                        DrawBuildTab();
                        break;
                    case Tab.Settings:
                        DrawSettingsTab();
                        break;
                    case Tab.Data:
                        DrawDataTab();
                        break;
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>IMGUI requires balanced layout; never start builds inside button handlers.</summary>
        static void DeferGuiAction(Action action)
        {
            if (action == null)
                return;
            EditorApplication.delayCall += () => action();
        }

        void DrawHeader()
        {
            EditorGUILayout.LabelField("Environment Kit Hub", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Single place to run FullWorld builds (~289 terrain tiles, 17×17 grid) with paced Step counter, tune settings, and inspect generated data before export/prefab decisions.",
                MessageType.None);
            EditorGUILayout.BeginHorizontal();
            _pinDuringBuild = EditorGUILayout.ToggleLeft("Pin Hub during active build", _pinDuringBuild);
            EditorGUI.BeginChangeCheck();
            var sequential = SurfaceTerrainTileExpansion.PreferSequentialFullWorldTerrain;
            sequential = EditorGUILayout.ToggleLeft(
                "Sequential FullWorld terrain (~289 tiles, one tile fully done before next)",
                sequential);
            if (EditorGUI.EndChangeCheck())
                SurfaceTerrainTileExpansion.PreferSequentialFullWorldTerrain = sequential;
            if (GUILayout.Button("Bring To Front Now", GUILayout.Width(160f)))
                ForceBringToFront();
            EditorGUILayout.EndHorizontal();
            EditorPrefs.SetBool(PrefPinDuringBuild, _pinDuringBuild);
        }

        void DrawBuildTab()
        {
            DrawBuildCompletionBanner();

            var inProgress =
                LavaTubeCaveBuilder.IsBuildInProgress ||
                CaveBuildStartupCoordinator.IsActive ||
                CaveBuildRunStatusPublisher.HasActiveSession;
            var mode = inProgress ? "Running" : "Idle";
            var stepText = CaveBuildStepCounter.FormatHubStepLine();

            EditorGUILayout.LabelField($"Pipeline: {mode}  |  Step: {stepText}", EditorStyles.boldLabel);
            var phaseLine = CaveBuildStepCounter.FormatHubPhaseLine();
            if (!string.IsNullOrEmpty(phaseLine))
                EditorGUILayout.LabelField(phaseLine, EditorStyles.miniLabel);
            if (inProgress)
            {
                EditorGUILayout.BeginHorizontal();
                CaveBuildHardwareMonitor.DrawMiniGraph(280f, 78f);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(
                    $"Budget ~{EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb():F0} GB",
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"Elapsed {CaveBuildStepCounter.FormatElapsed()}",
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"ETC {CaveBuildStepCounter.FormatEtc()}",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            if (inProgress)
                DrawBuildLiveActivityPanel();
            DrawBuildPauseControls(inProgress);
            var aiNote = CaveBuildSessionPreset.HasUsableAiProvider
                ? $"AI: {CaveBuildCursorSettings.ResolveActiveProvider()} (grading enabled when steps need it)"
                : CaveBuildSessionPreset.HasLocalResearchCache
                    ? "AI: none — procedural build + on-disk research data"
                    : "AI: none — procedural build only (import ResearchCache optional)";
            EditorGUILayout.LabelField(aiNote, EditorStyles.miniLabel);
            if (CaveBuildStartupCoordinator.IsActive)
            {
                EditorGUILayout.HelpBox(
                    "Startup in progress (tooling → scene → surface → cave). Step counter + pipeline log below; " +
                    "sub-action shows the current queue item. First surface pass can take several minutes.",
                    MessageType.Info);
            }

            if (!inProgress)
            {
                EditorGUILayout.HelpBox(
                    "Start here: Build Complete Cave. Empty scene OK — kit auto-creates Ground (tagged), PortalFive, grid anchors, and EnvironmentRoot.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Concept layout (0–9, applied before build)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _generationStyleIndex = EditorGUILayout.Popup(
                "Layout concept",
                _generationStyleIndex,
                FullWorldGenerationStylePreset.DisplayNames);
            if (EditorGUI.EndChangeCheck())
                FullWorldGenerationStylePreset.SaveSelectedIndex(_generationStyleIndex);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Roll random concept"))
            {
                var rolled = FullWorldConceptLayoutCatalog.RollRandomIndex();
                _generationStyleIndex = rolled;
                FullWorldGenerationStylePreset.SaveSelectedIndex(rolled);
            }
            EditorGUI.BeginChangeCheck();
            var randomOnBuild = FullWorldConceptLayoutCatalog.RandomOnBuildEnabled;
            randomOnBuild = EditorGUILayout.ToggleLeft("Random on build (from seed)", randomOnBuild);
            if (EditorGUI.EndChangeCheck())
                FullWorldConceptLayoutCatalog.SetRandomOnBuild(randomOnBuild);
            EditorGUILayout.EndHorizontal();

            var styleId = FullWorldGenerationStyleCatalog.GetStyleId(_generationStyleIndex);
            EditorGUILayout.LabelField($"Concept { _generationStyleIndex}: {styleId}", EditorStyles.miniLabel);

            var conceptRel = FullWorldGenerationStylePreset.ConceptImageRelForIndex(_generationStyleIndex);
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var conceptPath = System.IO.Path.Combine(hub, conceptRel);
            EditorGUILayout.HelpBox(
                randomOnBuild
                    ? "Random on build: each FullWorld build rolls concept 0–9 from build seed (similar per seed, not identical)."
                    : "Fixed concept: Hub selection is applied on every build.",
                MessageType.Info);
            if (System.IO.File.Exists(conceptPath))
            {
                if (GUILayout.Button($"Reveal concept {_generationStyleIndex} guide image"))
                    EditorUtility.RevealInFinder(conceptPath);
            }
            else if (GUILayout.Button($"Generate concept images (0–9)"))
            {
                DeferGuiAction(() =>
                {
                    FullWorldConceptImageAuthor.GenerateAll();
                    AssetDatabase.Refresh();
                });
            }

            EditorGUILayout.BeginHorizontal();
            var biomeMasterRel = BiomeLayoutConceptCatalog.MasterConceptImageRel;
            var biomeMasterPath = System.IO.Path.Combine(hub, biomeMasterRel);
            var biomePresetRel = BiomeLayoutConceptCatalog.GetPresetConceptImageRel(_generationStyleIndex);
            var biomePresetPath = System.IO.Path.Combine(hub, biomePresetRel);
            if (System.IO.File.Exists(biomeMasterPath))
            {
                if (GUILayout.Button("Reveal biome master guide"))
                    EditorUtility.RevealInFinder(biomeMasterPath);
            }
            else if (GUILayout.Button("Generate biome layout guides"))
            {
                DeferGuiAction(() =>
                {
                    BiomeLayoutConceptImageAuthor.GenerateAll();
                    AssetDatabase.Refresh();
                });
            }

            if (System.IO.File.Exists(biomePresetPath))
            {
                if (GUILayout.Button($"Reveal biome guide ({_generationStyleIndex})"))
                    EditorUtility.RevealInFinder(biomePresetPath);
            }

            if (GUILayout.Button("Export biome prompt manifest"))
                DeferGuiAction(() => BiomeLayoutConceptCatalog.ExportPhasePromptManifest(out _));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Biome prop guides: master layout + per-preset feather/prop rules. " +
                "Manifest: Assets/EnvironmentKit/Generated/BiomeLayoutPhasePromptManifest.json",
                MessageType.None);

            if (FullWorldGenerationStylePreset.IsIdealLayoutIndex(_generationStyleIndex))
            {
                EditorGUILayout.HelpBox(
                    "Concept 0 — Ideal: 9 play + foothill/peak/horizon rings + south labyrinth. " +
                    "Agents read the active concept guide each phase (similar per seed, not identical).",
                    MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Enrich research entries"))
                DeferGuiAction(() => CaveBuildResearchSituationGrader.RunEnrichAllEntries());
            if (GUILayout.Button("Grade research for build"))
                DeferGuiAction(() => CaveBuildResearchSituationGrader.RunGradeAndRepairForActiveBuild());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(inProgress))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Build Complete Cave", GUILayout.Height(30f)))
                    DeferGuiAction(LavaTubeCaveBuilder.BuildCompleteCaveActiveScene);
                if (GUILayout.Button("Build Surface Only", GUILayout.Height(30f)))
                    DeferGuiAction(LavaTubeCaveBuilder.BuildSurfaceWorldOnlyActiveScene);
                if (GUILayout.Button("Build Cave Only", GUILayout.Height(30f)))
                    DeferGuiAction(LavaTubeCaveBuilder.BuildCaveOnlyActiveScene);
                EditorGUILayout.EndHorizontal();

                _showRecoveryBuild = EditorGUILayout.Foldout(
                    _showRecoveryBuild,
                    "Stuck or half-built? Force full rebuild (AAA)");
                if (_showRecoveryBuild)
                {
                    EditorGUILayout.HelpBox(
                        "Only if a normal build stopped mid-way or reuses bad geometry. Same 120 steps as above, " +
                        "but always clears incremental cache.",
                        MessageType.None);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Full AAA Rebuild", GUILayout.Height(24f)))
                        DeferGuiAction(LavaTubeCaveBuilder.BuildCompleteCaveFullAaaRebuild);
                    if (GUILayout.Button("Full AAA Rebuild + Recording", GUILayout.Height(24f)))
                    {
                        DeferGuiAction(() =>
                        {
                            CaveBuildDemoAutoRecorder.AutoEnabled = true;
                            LavaTubeCaveBuilder.BuildCompleteCaveFullAaaRebuild();
                        });
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(8f);
            DrawExperienceSection(inProgress);

            EditorGUILayout.Space(8f);
            if (!inProgress)
            {
                EditorGUILayout.LabelField("Last build quality", EditorStyles.boldLabel);
                EnvironmentKitBuildMonitorPanels.DrawGradePanel();
                DrawLiveStatusPreview();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Pop out Pipeline Console (extra RAM)"))
                CaveBuildPipelineConsoleWindow.Open(forcePopOut: true);
            if (GUILayout.Button("Open Cave Grader"))
                CaveBuildGraderWindow.Open();
            if (GUILayout.Button("Open Terrain Grader"))
                TerrainBuildGraderWindow.Open();
            EditorGUILayout.EndHorizontal();
        }

        void DrawBuildCompletionBanner()
        {
            if (!_showCompletionPanel)
                return;

            var msgType = _completionTitle.Contains("Review", StringComparison.Ordinal)
                ? MessageType.Warning
                : MessageType.Info;
            EditorGUILayout.LabelField(_completionTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_completionSummary, msgType);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reveal completion readout"))
            {
                var path = CaveBuildCompletionSummary.LastReadoutFullPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    EditorUtility.RevealInFinder(path);
            }

            if (GUILayout.Button("Dismiss"))
                _showCompletionPanel = false;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);
        }

        void DrawBuildPauseControls(bool buildInProgress)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Build control", EditorStyles.boldLabel);
            var paused = CaveBuildPauseController.UserPauseActive;
            if (paused)
            {
                EditorGUILayout.HelpBox(
                    "Build is PAUSED — queued steps are frozen. Terrain, props, and heightmaps already written are kept. " +
                    "A recap video is generated from captures so far. Continue starts a new capture segment for the rest of the build.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!buildInProgress || paused))
            {
                if (GUILayout.Button("Pause build", GUILayout.Height(26f)))
                    CaveBuildPauseController.Pause();
            }

            using (new EditorGUI.DisabledScope(!paused))
            {
                if (GUILayout.Button("Continue build", GUILayout.Height(26f)))
                    CaveBuildPauseController.Continue();
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawExperienceSection(bool buildInProgress)
        {
            EditorGUILayout.LabelField("Scene view & playtest", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _settings.showLiveScenePlacement = EditorGUILayout.Toggle(
                "Show each edit in Scene view (live terrain + placements)",
                _settings.showLiveScenePlacement);
            _settings.lidarGuidedSculptOnly = EditorGUILayout.Toggle(
                "LiDAR guides sculpt/carve (no heightmap stamp)",
                _settings.lidarGuidedSculptOnly);
            SurfaceLidarGuidedSculptPolicy.PreferSculptOverStamp = _settings.lidarGuidedSculptOnly;
            _settings.cinematicSceneCamera = EditorGUILayout.Toggle(
                "Cinematic Scene camera (follow active tile)",
                _settings.cinematicSceneCamera);
            _settings.liveSceneCameraZoomOut = EditorGUILayout.Slider(
                new GUIContent(
                    "Scene camera zoom-out",
                    "1 = tight on active tile · 6 = default wide · 35 = grid context · 100 = maximum pull-back."),
                _settings.liveSceneCameraZoomOut,
                1f,
                100f);
            _settings.forceLivePreviewWhenRecording = EditorGUILayout.Toggle(
                "Force live preview while recording",
                _settings.forceLivePreviewWhenRecording);
            _settings.autoRunPlaytestBotAfterBuild = EditorGUILayout.Toggle(
                "Auto-run playtest bot when build finishes",
                _settings.autoRunPlaytestBotAfterBuild);
            if (EditorGUI.EndChangeCheck())
            {
                _settings.SaveToPrefs();
                EditorUtility.SetDirty(_settings);
            }

            EditorGUILayout.HelpBox(
                "Keep Scene view visible while building — terrain rows and cave pieces update as each paced step runs. " +
                "LiDAR sculpt uses DEM/hillshade as structural guide plus centered passes (not full DEM overwrite). " +
                "Pause/Continue freezes the queue without deleting finished tiles. " +
                "Playtest bot: Window → Environment Kit → Cave Build → Play Mode → Run Cave Playtest Bot.",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Demo recording", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Hub builds (Complete Cave, Surface Only, Cave Only, AAA Rebuild) auto-capture Scene timelapse. " +
                "Recap video composes when the pipeline finishes, you Pause build, or Stop All Builds. " +
                "Legacy toggle below also enables recording for menu-item builds outside Hub.",
                MessageType.None);
            CaveBuildDemoAutoRecorder.AutoEnabled = EditorGUILayout.Toggle(
                "Auto-record build recap video",
                CaveBuildDemoAutoRecorder.AutoEnabled);
            CaveBuildDemoAutoRecorder.SmartTimelapseEnabled = EditorGUILayout.Toggle(
                "Smart timelapse (continuous Scene capture + auto-edit)",
                CaveBuildDemoAutoRecorder.SmartTimelapseEnabled);
            if (CaveBuildDemoAutoRecorder.SmartTimelapseEnabled)
            {
                CaveBuildDemoAutoRecorder.TimelapseIntervalSeconds = EditorGUILayout.Slider(
                    "Capture interval (seconds)",
                    (float)CaveBuildDemoAutoRecorder.TimelapseIntervalSeconds,
                    1f,
                    5f);
                CaveBuildDemoAutoRecorder.TargetRecapMinutes = EditorGUILayout.Slider(
                    "Target recap length (minutes)",
                    CaveBuildDemoAutoRecorder.TargetRecapMinutes,
                    4f,
                    15f);
                CaveBuildDemoAutoRecorder.ForceBackgroundSceneUpdates = EditorGUILayout.Toggle(
                    "Force Scene updates while recording (background timelapse)",
                    CaveBuildDemoAutoRecorder.ForceBackgroundSceneUpdates);
            }

            CaveBuildDemoNarrationAi.AiNarrationEnabled = EditorGUILayout.Toggle(
                "AI polish captions (uses Hub AI provider)",
                CaveBuildDemoNarrationAi.AiNarrationEnabled);
            CaveBuildDemoAutoRecorder.ProducerNarratorEnabled = EditorGUILayout.Toggle(
                "Narrator: Personal Voice (Jacob Adkins)",
                CaveBuildDemoAutoRecorder.ProducerNarratorEnabled);
            CaveBuildRecapDashboardGate.OpenRecapDashboardBeforeCompose = EditorGUILayout.Toggle(
                "Open recap dashboard before compose",
                CaveBuildRecapDashboardGate.OpenRecapDashboardBeforeCompose);
            CaveBuildRecapDashboardGate.SkipRecapReview = EditorGUILayout.Toggle(
                "Skip recap review",
                CaveBuildRecapDashboardGate.SkipRecapReview);
            if (CaveBuildRecapDashboardGate.OpenRecapDashboardBeforeCompose &&
                !CaveBuildRecapDashboardGate.SkipRecapReview)
            {
                CaveBuildRecapDashboardGate.AutoProceedTimeoutMinutes = EditorGUILayout.IntSlider(
                    "Auto-proceed after (minutes, 0 = off)",
                    CaveBuildRecapDashboardGate.AutoProceedTimeoutMinutes,
                    0,
                    120);
                if (CaveBuildRecapDashboardGate.IsWaitingForProceed)
                {
                    EditorGUILayout.HelpBox(
                        "Waiting for recap review — adjust timeline in the dashboard, then click Proceed to compose.",
                        MessageType.Info);
                }
            }

            if (CaveBuildDemoAutoRecorder.ProducerNarratorEnabled)
            {
                EditorGUILayout.HelpBox(
                    "After compose, Terminal.app opens for Personal Voice narration (~30–60 min). Keep that window open until it finishes; the final MP4 opens when done.",
                    MessageType.Info);
            }
            var aiReady = CaveBuildDemoNarrationAi.CanRun;
            EditorGUILayout.HelpBox(
                CaveBuildDemoAutoRecorder.IsRecording
                    ? "Recording: timelapse + forced Scene repaints so placement stays visible even if Unity is in the background (keep Unity awake)."
                    : "Can record for hours. On stop: Producer recap (emerald cards + presentation + OpenCV) when brand assets exist under DemoRecapApproved on the active data volume, else legacy smart compose. Needs python3, Pillow, ffmpeg. Menu: Rebuild Producer Recap Preview.",
                MessageType.None);
            EditorGUILayout.LabelField($"Data storage: {EnvironmentKitDataRoot.DescribeStorage()}", EditorStyles.miniLabel);
            if (CaveBuildDemoNarrationAi.AiNarrationEnabled && !aiReady)
            {
                EditorGUILayout.HelpBox(
                    "AI captions need API credentials for the active provider in Cave Build Cursor Settings.",
                    MessageType.Warning);
            }
            EditorGUILayout.LabelField(
                CaveBuildDemoAutoRecorder.IsFfmpegAvailable
                    ? $"ffmpeg: found at {CaveBuildDemoAutoRecorder.FfmpegPath}"
                    : "ffmpeg: not found (frames will save, recap mp4 will not render).",
                EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(CaveBuildDemoAutoRecorder.IsRecording))
            {
                if (GUILayout.Button("Start recording"))
                    CaveBuildDemoAutoRecorder.StartRecordingManual();
            }

            using (new EditorGUI.DisabledScope(!CaveBuildDemoAutoRecorder.IsRecording))
            {
                if (GUILayout.Button("Stop recording & generate video"))
                    CaveBuildDemoAutoRecorder.StopRecordingAndCompose();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture demo checkpoint now"))
                CaveBuildDemoAutoRecorder.CaptureNow();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(CaveBuildDemoAutoRecorder.LastOutputFolder)))
            {
                if (GUILayout.Button("Reveal last demo output"))
                    CaveBuildDemoAutoRecorder.RevealLastOutput();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Verify ffmpeg now"))
                CaveBuildDemoAutoRecorder.VerifyFfmpegAndReport();
            if (GUILayout.Button("Install ffmpeg helper"))
                CaveBuildDemoAutoRecorder.OpenInstallGuide();
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(buildInProgress))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Run Playtest Bot (Play Mode)"))
                    DeferGuiAction(CavePlaytestRouteBotBridge.RunPlayModeBotMenu);
                if (GUILayout.Button("Run Batch Cave Builds"))
                    DeferGuiAction(LavaTubeCaveBuilder.RunBatchCaveBuildsActiveScene);
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawBuildLiveActivityPanel()
        {
            EditorGUILayout.LabelField("Main action", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(CaveBuildRunStatusPublisher.FormatMainActionSummary(), MessageType.None);

            var sub = CaveBuildRunStatusPublisher.FormatSubActionSummary();
            if (!string.IsNullOrEmpty(sub))
            {
                EditorGUILayout.LabelField("Sub-action (right now)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(sub, MessageType.Info);
            }

            if (!string.IsNullOrEmpty(CaveBuildRunStatusPublisher.BuildMode))
            {
                EditorGUILayout.LabelField(
                    CaveBuildRunStatusPublisher.BuildMode,
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4f);
            var buildLive =
                LavaTubeCaveBuilder.IsBuildInProgress ||
                CaveBuildStartupCoordinator.IsActive ||
                CaveBuildRunStatusPublisher.HasActiveSession;

            var pipelineText = CaveBuildPipelineLog.GetRecentText(48);
            var stickPipeline = buildLive || pipelineText.Length != _lastPipelineLogLength;
            _lastPipelineLogLength = pipelineText.Length;

            EditorGUILayout.LabelField(
                buildLive
                    ? "Pipeline log (newest at bottom — auto-scroll)"
                    : "Pipeline log (newest at bottom)",
                EditorStyles.boldLabel);
            EnvironmentKitFeedScrollView.Draw(ref _pipelineLogScroll, pipelineText, 200f, stickPipeline);

            _showGradeDuringBuild = EditorGUILayout.Foldout(
                _showGradeDuringBuild,
                "Quality grade (during build)");
            if (_showGradeDuringBuild)
                EnvironmentKitBuildMonitorPanels.DrawGradePanel();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reveal status file"))
                RevealRelativeFile(CaveBuildRunStatusPublisher.GetLiveStatusReadRel());
            EditorGUILayout.EndHorizontal();
        }

        void DrawLiveStatusPreview()
        {
            var text = LoadFilePreview(CaveBuildRunStatusPublisher.GetLiveStatusReadRel(), 1800, out var exists, out _);
            EditorGUILayout.LabelField("Last build status (markdown snapshot)", EditorStyles.boldLabel);
            if (!exists)
            {
                EditorGUILayout.HelpBox(
                    "No status file yet. Start Build Complete Cave — pipeline log appears above while running.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.TextArea(text, GUILayout.MinHeight(120f));
            if (GUILayout.Button("Reveal full live status file"))
                RevealRelativeFile(CaveBuildRunStatusPublisher.GetLiveStatusReadRel());
        }

        void DrawSettingsTab()
        {
            if (_settings == null)
                _settings = CaveBuildCursorSettings.LoadOrCreate();

            EditorGUILayout.LabelField("Primary hub settings", EditorStyles.boldLabel);
            _settings.hubProjectRoot = EditorGUILayout.TextField("Hub project root", _settings.hubProjectRoot);
            _settings.aiProvider = (EnvironmentKitAiProvider)EditorGUILayout.EnumPopup("Active provider", _settings.aiProvider);
            _settings.modelId = EditorGUILayout.TextField("Cursor model", _settings.modelId);
            _settings.hardwareBudget = (EnvironmentKitHardwareBudget.Preset)EditorGUILayout.EnumPopup(
                "Hardware budget",
                _settings.hardwareBudget);

            EditorGUI.BeginChangeCheck();
            var ramBudgetGb = EditorPrefs.GetFloat(EnvironmentKitHardwareBudget.EditorRamBudgetGbKey, 0f);
            ramBudgetGb = EditorGUILayout.FloatField(
                "Editor RAM budget (GB)",
                ramBudgetGb > 0f ? ramBudgetGb : EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb());
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetFloat(
                    EnvironmentKitHardwareBudget.EditorRamBudgetGbKey,
                    ramBudgetGb >= 4f ? ramBudgetGb : 0f);

            EditorGUILayout.LabelField(
                ramBudgetGb >= 4f
                    ? $"Hub RAM candlestick chart uses {ramBudgetGb:F0} GB (percentage = Unity working set ÷ this cap)."
                    : $"Using preset default (~{( _settings.hardwareBudget == EnvironmentKitHardwareBudget.Preset.MacBookAir16Gb ? EnvironmentKitHardwareBudget.MacBookAirPresetRamBudgetGb : EnvironmentKitHardwareBudget.DefaultPresetRamBudgetGb):F0} GB). Set this field to 13 for a 13 GB cap.",
                EditorStyles.miniLabel);
            if (ramBudgetGb >= 4f && GUILayout.Button("Reset RAM budget to preset default", GUILayout.Width(240f)))
                EditorPrefs.SetFloat(EnvironmentKitHardwareBudget.EditorRamBudgetGbKey, 0f);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Scene safeguards", EditorStyles.boldLabel);
            EnvironmentKitSceneSafeguards.AutoSaveBeforeBuildEnabled = EditorGUILayout.Toggle(
                "Auto-save scenes before build (no save dialog)",
                EnvironmentKitSceneSafeguards.AutoSaveBeforeBuildEnabled);
            EnvironmentKitSceneSafeguards.NeverSwitchSceneDuringBuildEnabled = EditorGUILayout.Toggle(
                "Never switch scenes during build",
                EnvironmentKitSceneSafeguards.NeverSwitchSceneDuringBuildEnabled);
            EditorGUILayout.HelpBox(
                "When enabled, Build Complete Cave saves MainScene (and other open scenes) before running, " +
                "and will not switch away mid-build. Use Rebuild Complete Cave (MainScene) menu only when you intend to open MainScene.",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("3D Gaussian Splat (optional)", EditorStyles.boldLabel);
            _settings.enableGaussianSplatHeroAtCaveMouth = EditorGUILayout.Toggle(
                "Hero splat at cave mouth",
                _settings.enableGaussianSplatHeroAtCaveMouth);
            using (new EditorGUI.DisabledScope(!_settings.enableGaussianSplatHeroAtCaveMouth))
            {
                _settings.gaussianSplatAssetPath = EditorGUILayout.TextField(
                    "Splat asset path",
                    _settings.gaussianSplatAssetPath);
                _settings.gaussianSplatHeroQuality = (GaussianSplatHeroSlot.QualityTier)
                    EditorGUILayout.EnumPopup("Quality", _settings.gaussianSplatHeroQuality);
                _settings.gaussianSplatRenderInEditMode = EditorGUILayout.Toggle(
                    "Render in Edit Mode (GPU heavy)",
                    _settings.gaussianSplatRenderInEditMode);
                _settings.gaussianSplatRenderInPlayMode = EditorGUILayout.Toggle(
                    "Render in Play Mode",
                    _settings.gaussianSplatRenderInPlayMode);
            }

            if (_settings.hardwareBudget == EnvironmentKitHardwareBudget.Preset.MacBookAir16Gb)
            {
                EditorGUILayout.HelpBox(
                    "MacBook budget disables Gaussian splats automatically during builds.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Prefab folders", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Optional: limit scanning to specific folders. When empty, the kit scans all of Assets/ and picks floor/wall/ceiling prefabs by name + mesh shape. " +
                "Texture-only packs and 2D tile sprites cannot be used as cave modules.\n\n" +
                "AI: Active provider can be Cursor, Gemini, Claude, OpenAI, OpenRouter, Ollama, or LM Studio — not Cursor-only. " +
                "No API? Use Build tab → Apply Offline (No API) for procedural-only runs.",
                MessageType.None);
            _caveLavaFolders = DrawPrefabFolderField("Prefab folders for environment modules", _caveLavaFolders);
            _cavePropFolders = DrawPrefabFolderField("Prefab folders for props", _cavePropFolders);
            EditorGUILayout.LabelField("Asset scan", "All of Assets/ (automatic)", EditorStyles.miniLabel);
            if (GUILayout.Button("Refresh prefab catalog (re-scan folders)", GUILayout.Height(22f)))
            {
                DeferGuiAction(() =>
                {
                    SavePrefabFolderPrefs();
                    RefreshModulePrefabCatalogAndMaterials();
                });
            }

            _showApiKey = EditorGUILayout.Toggle("Show API key", _showApiKey);
            DrawProviderSettings();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Automation + quality", EditorStyles.boldLabel);
            _settings.usePhasedCaveBuild = EditorGUILayout.Toggle("Use phased cave build", _settings.usePhasedCaveBuild);
            _settings.useIncrementalLadder = EditorGUILayout.Toggle("Use incremental ladder", _settings.useIncrementalLadder);
            _settings.stabilizationMode = EditorGUILayout.Toggle("Stabilization mode", _settings.stabilizationMode);
            _settings.enableQualityFastGates = EditorGUILayout.Toggle(
                "Quality fast gates (tile count, Hollow Titan, skip redundant AI)",
                _settings.enableQualityFastGates);
            if (_settings.enableQualityFastGates)
            {
                EditorGUI.indentLevel++;
                _settings.requireHollowTitanOnSurface = EditorGUILayout.Toggle(
                    "Require massive Hollow Titan on surface",
                    _settings.requireHollowTitanOnSurface);
                if (_settings.requireHollowTitanOnSurface)
                {
                    EditorGUI.indentLevel++;
                    _settings.hollowTitanPhasedBuild = EditorGUILayout.Toggle(
                        "Phased meat build (12 steps)",
                        _settings.hollowTitanPhasedBuild);
                    _settings.hollowTitanBaseClearanceMeters = EditorGUILayout.Slider(
                        "Base clearance above terrain (m)",
                        _settings.hollowTitanBaseClearanceMeters,
                        0.05f,
                        2f);
                    _settings.hollowTitanFloorCount = EditorGUILayout.IntSlider(
                        "Interior floor count",
                        _settings.hollowTitanFloorCount,
                        2,
                        8);
                    _settings.hollowTitanEnemiesPerFloor = EditorGUILayout.IntField(
                        "Enemies per floor (landmark-only)",
                        _settings.hollowTitanEnemiesPerFloor);
                    _settings.hollowTitanLootPerFloor = EditorGUILayout.IntField(
                        "Loot per floor (landmark-only)",
                        _settings.hollowTitanLootPerFloor);
                    var titanHub = CaveBuildCursorSettings.ResolveHubRoot();
                    var titanMasterRel = EnvironmentAuthoringKit.Editor.World.HollowTitanConceptCatalog.MasterConceptImageRel;
                    var titanMasterPath = System.IO.Path.Combine(titanHub, titanMasterRel);
                    EditorGUILayout.BeginHorizontal();
                    if (System.IO.File.Exists(titanMasterPath))
                    {
                        if (GUILayout.Button("Reveal Hollow Titan master guide"))
                            EditorUtility.RevealInFinder(titanMasterPath);
                    }
                    else if (GUILayout.Button("Generate Hollow Titan concept images"))
                    {
                        DeferGuiAction(() =>
                        {
                            EnvironmentAuthoringKit.Editor.World.HollowTitanConceptImageAuthor.GenerateAll();
                            AssetDatabase.Refresh();
                        });
                    }
                    if (GUILayout.Button("Export phase prompt manifest"))
                        DeferGuiAction(() =>
                            EnvironmentAuthoringKit.Editor.World.HollowTitanConceptCatalog.ExportPhasePromptManifest(out _));
                    if (GUILayout.Button("Export Hollow Titan research prompts"))
                        DeferGuiAction(() =>
                            EnvironmentAuthoringKit.Editor.World.HollowTitanPhaseResearchGate.EnsureBeforeMeatPhase(
                                0,
                                0,
                                out _));
                    if (GUILayout.Button("Sync Hollow Titan research"))
                        DeferGuiAction(() =>
                        {
                            EnvironmentAuthoringKit.Editor.World.HollowTitanPhasePromptBridge.ExportResearchExecutionBrief(0, out _);
                            EnvironmentAuthoringKit.Editor.World.HollowTitanPhaseResearchGate.EnsureBeforeMeatPhase(0, 0, out _);
                        });
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.HelpBox(
                        "Each meat phase exports research gate + active prompt. Manifest: HollowTitanPhasePromptManifest.json | Active: HollowTitanActivePhasePrompt.md",
                        MessageType.None);
                    EditorGUI.indentLevel--;
                }
                _settings.skipAutonomousWhenGradeAtOrAbove = EditorGUILayout.Toggle(
                    "Skip autonomous loop when prior grade good",
                    _settings.skipAutonomousWhenGradeAtOrAbove);
                _settings.fastGateMinOverallScore = EditorGUILayout.IntSlider(
                    "Min overall score to skip autonomous",
                    _settings.fastGateMinOverallScore,
                    70,
                    98);
                EditorGUI.indentLevel--;
            }

            _settings.enableAutonomousUntilShip = EditorGUILayout.Toggle(
                "Autonomous until ship target",
                _settings.enableAutonomousUntilShip);
            _settings.enableFullAutoHeal = EditorGUILayout.Toggle(
                "Auto-heal checkpoints + rollback retry",
                _settings.enableFullAutoHeal);
            if (_settings.enableFullAutoHeal)
            {
                EditorGUI.indentLevel++;
                _settings.healMinScoreForGoodCheckpoint = EditorGUILayout.IntSlider(
                    "Min score for good checkpoint",
                    _settings.healMinScoreForGoodCheckpoint,
                    60,
                    100);
                _settings.maxHealRecoveryAttempts = EditorGUILayout.IntField(
                    "Max heal recovery attempts",
                    _settings.maxHealRecoveryAttempts);
                _settings.healRollbackBeforeRetry = EditorGUILayout.Toggle(
                    "Rollback before retry",
                    _settings.healRollbackBeforeRetry);
                _settings.healInvokeAiAfterRollback = EditorGUILayout.Toggle(
                    "Invoke AI after rollback",
                    _settings.healInvokeAiAfterRollback);
                var healIndex = CaveBuildHealCheckpointStore.LoadIndex();
                EditorGUILayout.LabelField(
                    "Last good checkpoint",
                    string.IsNullOrEmpty(healIndex.lastGoodCheckpointId)
                        ? "(none yet)"
                        : $"{healIndex.lastGoodCheckpointId} — {healIndex.lastGoodMilestone} ({healIndex.lastGoodScore}/100)",
                    EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            _settings.exportGenerationPrefabWhenFinished = EditorGUILayout.Toggle(
                "Export generation prefab when finished",
                _settings.exportGenerationPrefabWhenFinished);
            _settings.runPostBuildResearchPhase = EditorGUILayout.Toggle(
                "Run post-build research phase",
                _settings.runPostBuildResearchPhase);
            _settings.autoInvokeAfterEveryBuild = EditorGUILayout.Toggle(
                "Auto invoke Cursor after every build",
                _settings.autoInvokeAfterEveryBuild);
            _settings.autoInvokeOnDud = EditorGUILayout.Toggle("Auto invoke on dud grade", _settings.autoInvokeOnDud);
            _settings.enforcePreBuildGate = EditorGUILayout.Toggle("Enforce pre-build gate", _settings.enforcePreBuildGate);
            _settings.preBuildReloopUntilPass = EditorGUILayout.Toggle(
                "Pre-build reloop until pass",
                _settings.preBuildReloopUntilPass);
            _settings.queuedStepTimeoutSeconds = EditorGUILayout.FloatField(
                "Queued step timeout (sec)",
                _settings.queuedStepTimeoutSeconds);

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Hub Settings", GUILayout.Height(26f)))
            {
                DeferGuiAction(() =>
                {
                    _settings.SetApiKey(_apiKey);
                    _settings.SetApiKey(EnvironmentKitAiProvider.GoogleGemini, _googleApiKey);
                    _settings.SetApiKey(EnvironmentKitAiProvider.AnthropicClaude, _anthropicApiKey);
                    _settings.SetApiKey(EnvironmentKitAiProvider.OpenAICompatible, _openAiApiKey);
                    _settings.SetApiKey(EnvironmentKitAiProvider.OpenRouter, _openRouterApiKey);
                    _settings.SetApiKey(EnvironmentKitAiProvider.CustomEndpoint, _customApiKey);
                    _settings.SaveToPrefs();
                    SavePrefabFolderPrefs();
                    RefreshModulePrefabCatalogAndMaterials();
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                });
            }

            if (GUILayout.Button("Reload From Prefs", GUILayout.Height(26f)))
            {
                _settings.LoadFromPrefs();
                LoadPrefabFolderPrefs();
                _apiKey = _settings.GetApiKey();
                _googleApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.GoogleGemini);
                _anthropicApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.AnthropicClaude);
                _openAiApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.OpenAICompatible);
                _openRouterApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.OpenRouter);
                _customApiKey = _settings.GetApiKey(EnvironmentKitAiProvider.CustomEndpoint);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);
            _showAdvancedSettings = EditorGUILayout.Foldout(
                _showAdvancedSettings,
                "Advanced settings inspector (full)");
            if (_showAdvancedSettings)
            {
                if (_settingsEditor == null)
                    _settingsEditor = UnityEditor.Editor.CreateEditor(_settings);
                _settingsEditor.OnInspectorGUI();
            }
        }

        void DrawProviderSettings()
        {
            EditorGUILayout.LabelField("Provider routing", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Choose provider credentials/endpoints here. Non-Cursor providers can run through the external execution layer when enabled below.",
                MessageType.None);

            _apiKey = DrawApiField("CURSOR_API_KEY", _apiKey);
            _googleApiKey = DrawApiField("GOOGLE_API_KEY", _googleApiKey);
            _anthropicApiKey = DrawApiField("ANTHROPIC_API_KEY", _anthropicApiKey);
            _openAiApiKey = DrawApiField("OPENAI_API_KEY", _openAiApiKey);
            _openRouterApiKey = DrawApiField("OPENROUTER_API_KEY", _openRouterApiKey);
            _customApiKey = DrawApiField("CUSTOM_API_KEY", _customApiKey);

            _settings.openAiCompatibleBaseUrl = EditorGUILayout.TextField(
                "OpenAI-compatible base URL",
                _settings.openAiCompatibleBaseUrl);
            _settings.googleModelId = EditorGUILayout.TextField("Google model", _settings.googleModelId);
            _settings.anthropicModelId = EditorGUILayout.TextField("Anthropic model", _settings.anthropicModelId);
            _settings.openAiModelId = EditorGUILayout.TextField("OpenAI-compatible model", _settings.openAiModelId);
            _settings.openRouterModelId = EditorGUILayout.TextField("OpenRouter model", _settings.openRouterModelId);
            _settings.ollamaModelId = EditorGUILayout.TextField("Ollama model", _settings.ollamaModelId);
            _settings.lmStudioModelId = EditorGUILayout.TextField("LM Studio model", _settings.lmStudioModelId);
            _settings.customProviderLabel = EditorGUILayout.TextField("Custom provider label", _settings.customProviderLabel);
            _settings.allowExternalProviderEdits = EditorGUILayout.Toggle(
                "Allow external provider edits",
                _settings.allowExternalProviderEdits);
            _settings.externalProviderEditsDryRun = EditorGUILayout.Toggle(
                "External edits dry-run only",
                _settings.externalProviderEditsDryRun);

            var provider = _settings.aiProvider;
            var needsKey = CaveBuildCursorSettings.ProviderNeedsApiKey(provider);
            var hasCreds = !needsKey || !string.IsNullOrWhiteSpace(_settings.GetApiKey(provider));
            var summary = needsKey
                ? (hasCreds ? "credentials present" : "credentials missing")
                : "no key required";
            EditorGUILayout.HelpBox(
                $"Active provider: {provider} ({summary}). Active model: {CaveBuildCursorSettings.ResolveActiveModelId()}",
                hasCreds ? MessageType.Info : MessageType.Warning);
            if (provider != EnvironmentKitAiProvider.Cursor && _settings.allowExternalProviderEdits)
            {
                EditorGUILayout.HelpBox(
                    _settings.externalProviderEditsDryRun
                        ? "Execution layer enabled in dry-run mode (no file writes)."
                        : "Execution layer enabled in write mode. External model edits can modify files under allowed paths.",
                    _settings.externalProviderEditsDryRun ? MessageType.Info : MessageType.Warning);
            }
        }

        string DrawApiField(string label, string value) =>
            _showApiKey
                ? EditorGUILayout.TextField(label, value)
                : EditorGUILayout.PasswordField(label, value);

        void DrawDataTab()
        {
            EditorGUILayout.LabelField("Build artifacts and data used", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Inspect live status, grading reports, contracts, and research gate outputs from Assets/EnvironmentKit/Generated.",
                MessageType.None);

            var names = new string[Artifacts.Length];
            for (var i = 0; i < Artifacts.Length; i++)
                names[i] = Artifacts[i].label;

            var next = EditorGUILayout.Popup("Artifact", _selectedArtifact, names);
            if (next != _selectedArtifact)
            {
                _selectedArtifact = next;
                RefreshArtifactPreview();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh"))
                RefreshArtifactPreview();
            if (GUILayout.Button("Reveal File"))
                RevealRelativeFile(Artifacts[_selectedArtifact].relPath);
            if (GUILayout.Button("Open Generated Folder"))
                RevealGeneratedFolder();
            EditorGUILayout.EndHorizontal();

            var selected = Artifacts[_selectedArtifact];
            var fullPath = ToAbsolute(selected.relPath);
            EditorGUILayout.LabelField("Path", fullPath, EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(_lastDataError))
                EditorGUILayout.HelpBox(_lastDataError, MessageType.Warning);

            EditorGUILayout.TextArea(_artifactPreview, GUILayout.MinHeight(300f));

            EditorGUILayout.Space(8f);
            DrawGeneratedFileSummary();
            EditorGUILayout.Space(8f);
            DrawFlowAuditSummary();
        }

        void DrawGeneratedFileSummary()
        {
            var generatedAbs = ToAbsolute(CaveBuildAgentContextExporter.Folder);
            if (!Directory.Exists(generatedAbs))
            {
                EditorGUILayout.HelpBox("Generated folder not found yet.", MessageType.Info);
                return;
            }

            var files = Directory.GetFiles(generatedAbs, "*", SearchOption.TopDirectoryOnly);
            var sb = new StringBuilder();
            sb.AppendLine($"Files: {files.Length}");
            var top = Mathf.Min(12, files.Length);
            for (var i = 0; i < top; i++)
                sb.AppendLine("- " + Path.GetFileName(files[i]));
            if (files.Length > top)
                sb.AppendLine($"... and {files.Length - top} more");

            EditorGUILayout.LabelField("Generated snapshot", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(sb.ToString(), MessageType.None);
        }

        void DrawFlowAuditSummary()
        {
            var issues = new List<string>();
            var provider = _settings != null ? _settings.aiProvider : CaveBuildCursorSettings.ResolveActiveProvider();
            var activeNeedsKey = CaveBuildCursorSettings.ProviderNeedsApiKey(provider);
            var hasCreds = CaveBuildCursorSettings.HasCredentialsForActiveProvider();
            var automationOn = _settings != null &&
                               (_settings.autoInvokeAfterEveryBuild ||
                                _settings.autoInvokeOnDud ||
                                _settings.autoInvokePreBuildWorkflow ||
                                _settings.autoInvokeEachMeatLoopPass);

            if (automationOn && !hasCreds)
            {
                issues.Add(
                    $"Agent automation is on but {provider} is not ready — " +
                    CaveBuildCursorSettings.GraderCredentialHint());
            }

            if (activeNeedsKey && !hasCreds)
                issues.Add($"Active provider {provider} requires an API key but none is configured.");

            if (!File.Exists(ToAbsolute(CaveBuildRunStatusPublisher.GetLiveStatusReadRel())) &&
                LavaTubeCaveBuilder.IsBuildInProgress)
            {
                issues.Add("Build is active but live status file is missing.");
            }

            EditorGUILayout.LabelField("Flow audit", EditorStyles.boldLabel);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No immediate configuration mismatches detected.", MessageType.Info);
                return;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < issues.Count; i++)
                sb.AppendLine($"- {issues[i]}");
            EditorGUILayout.HelpBox(sb.ToString(), MessageType.Warning);
        }

        void RefreshArtifactPreview()
        {
            _lastDataError = string.Empty;
            _artifactPreview = LoadFilePreview(Artifacts[_selectedArtifact].relPath, 5000, out var exists, out var fullPath);
            if (!exists)
                _lastDataError = $"File not found yet: {fullPath}";
        }

        static string LoadFilePreview(string relPath, int maxChars, out bool exists, out string fullPath)
        {
            fullPath = ToAbsolute(relPath);
            exists = File.Exists(fullPath);
            if (!exists)
                return string.Empty;

            try
            {
                var text = File.ReadAllText(fullPath);
                if (text.Length <= maxChars)
                    return text;
                return text.Substring(0, maxChars) + "\n…";
            }
            catch (Exception ex)
            {
                return "Failed to read file: " + ex.Message;
            }
        }

        static string ToAbsolute(string relPath)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return Path.Combine(hub, relPath.Replace('/', Path.DirectorySeparatorChar));
        }

        static void RevealRelativeFile(string relPath)
        {
            var path = ToAbsolute(relPath);
            if (File.Exists(path) || Directory.Exists(path))
                EditorUtility.RevealInFinder(path);
            else
                EditorUtility.DisplayDialog("Environment Kit Hub", $"File not found:\n{path}", "OK");
        }

        static void RevealGeneratedFolder()
        {
            var path = ToAbsolute(CaveBuildAgentContextExporter.Folder);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        static string DrawPrefabFolderField(string label, string value)
        {
            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            var fieldRect = new Rect(
                rect.x + EditorGUIUtility.labelWidth,
                rect.y,
                rect.width - EditorGUIUtility.labelWidth,
                rect.height);

            EditorGUI.LabelField(labelRect, label);
            var next = EditorGUI.TextField(fieldRect, value ?? string.Empty);
            HandlePrefabFolderDragDrop(rect, ref next);
            return next;
        }

        static void HandlePrefabFolderDragDrop(Rect dropRect, ref string fieldValue)
        {
            var evt = Event.current;
            if (evt == null || !dropRect.Contains(evt.mousePosition))
                return;

            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;

            if (!TryResolveDroppedPrefabFolder(out var folderPath))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                evt.Use();
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                fieldValue = AppendPrefabFolderPath(fieldValue, folderPath);
                GUI.changed = true;
            }

            evt.Use();
        }

        static bool TryResolveDroppedPrefabFolder(out string folderPath)
        {
            folderPath = null;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj == null)
                    continue;

                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    folderPath = NormalizeAssetsFolderPath(path);
                    return true;
                }

                var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parent) && parent.StartsWith("Assets", StringComparison.Ordinal))
                {
                    folderPath = NormalizeAssetsFolderPath(parent);
                    return true;
                }
            }

            var assetsRoot = Application.dataPath.Replace('\\', '/');
            if (DragAndDrop.paths == null)
                return false;

            foreach (var raw in DragAndDrop.paths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var absolute = Path.GetFullPath(raw).Replace('\\', '/');
                if (!absolute.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = "Assets" + absolute.Substring(assetsRoot.Length);
                if (AssetDatabase.IsValidFolder(rel))
                {
                    folderPath = NormalizeAssetsFolderPath(rel);
                    return true;
                }
            }

            return false;
        }

        static string NormalizeAssetsFolderPath(string path)
        {
            var p = (path ?? string.Empty).Replace('\\', '/').Trim();
            if (!p.StartsWith("Assets", StringComparison.Ordinal))
                return p;
            return p.EndsWith("/", StringComparison.Ordinal) ? p : p + "/";
        }

        static string AppendPrefabFolderPath(string existing, string folder)
        {
            folder = NormalizeAssetsFolderPath(folder).TrimEnd('/');
            if (string.IsNullOrEmpty(folder))
                return existing ?? string.Empty;

            var parts = string.IsNullOrWhiteSpace(existing)
                ? new List<string>()
                : existing
                    .Split(';')
                    .Select(p => NormalizeAssetsFolderPath(p).TrimEnd('/'))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

            if (parts.Any(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase)))
                return string.Join(";", parts);

            parts.Add(folder);
            return string.Join(";", parts);
        }

        void LoadPrefabFolderPrefs()
        {
            _caveLavaFolders = EnvironmentKitSettings.CaveLavaPrefabFolders;
            _cavePropFolders = EnvironmentKitSettings.CavePropPrefabFolders;
            _caveScanAllAssets = EnvironmentKitSettings.CaveScanAllAssets;
        }

        void SavePrefabFolderPrefs()
        {
            EnvironmentKitSettings.CaveLavaPrefabFolders = _caveLavaFolders;
            EnvironmentKitSettings.CavePropPrefabFolders = _cavePropFolders;
            EnvironmentKitSettings.CaveScanAllAssets = _caveScanAllAssets;
        }

        static void RefreshModulePrefabCatalogAndMaterials()
        {
            var catalog = LavaTubePrefabCatalog.Load(forceRefresh: true);
            LavaTubeMaterialUpgrader.UpgradeAllPackMaterials();
            EnvironmentKitScopedAssetRefresh.ImportMaterialsPackNow();
            if (!catalog.IsValid)
                Debug.LogWarning(
                    "[EnvironmentKit] Prefab catalog missing floor/wall/ceiling — check Console for [CaveCatalog] counts.");
        }
    }
}
#endif
