#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            new("Live status", CaveBuildRunStatusPublisher.LiveStatusRel),
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
        CaveBuildCursorSettings _settings;
        UnityEditor.Editor _settingsEditor;
        bool _showAdvancedSettings;
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
        string _caveLavaFolders;
        string _cavePropFolders;
        bool _caveScanAllAssets;

        [MenuItem(CaveBuildMenuPaths.Hub, false, -100)]
        public static void Open()
        {
            var window = GetWindow<EnvironmentKitHubWindow>("Environment Kit Hub");
            window.minSize = new Vector2(620f, 560f);
            // Utility window stays floating and tends to remain on top of docked editor panes.
            window.ShowUtility();
            window.Focus();
        }

        [MenuItem(CaveBuildMenuPaths.Root + "Hub/Bring Hub To Front", false, -99)]
        public static void BringToFront() => Open();

        [MenuItem(CaveBuildMenuPaths.Root + "Hub/Force Bring Hub To Front (During Build)", false, -98)]
        public static void ForceBringToFront()
        {
            Open();
            EditorApplication.delayCall += Open;
            EditorApplication.delayCall += Open;
        }

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
            var interval = LavaTubeCaveBuilder.IsBuildInProgress ? 0.4 : 1.2;
            if (EditorApplication.timeSinceStartup < _nextRefreshAt)
                return;

            _nextRefreshAt = EditorApplication.timeSinceStartup + interval;
            if (_pinDuringBuild &&
                LavaTubeCaveBuilder.IsBuildInProgress &&
                EditorApplication.timeSinceStartup >= _nextPinAt)
            {
                _nextPinAt = EditorApplication.timeSinceStartup + 1.5;
                ShowUtility();
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
                "Single place to run 120-step builds, tune settings, and inspect live generated data before export/prefab decisions.",
                MessageType.None);
            EditorGUILayout.BeginHorizontal();
            _pinDuringBuild = EditorGUILayout.ToggleLeft("Pin Hub during active build", _pinDuringBuild);
            if (GUILayout.Button("Bring To Front Now", GUILayout.Width(160f)))
                ForceBringToFront();
            EditorGUILayout.EndHorizontal();
            EditorPrefs.SetBool(PrefPinDuringBuild, _pinDuringBuild);
        }

        void DrawBuildTab()
        {
            var inProgress = LavaTubeCaveBuilder.IsBuildInProgress;
            var mode = inProgress ? "Running" : "Idle";
            var current = CaveBuildRunStatusPublisher.CurrentQueuedStep;
            var total = CaveBuildRunStatusPublisher.QueuedStepTotal;
            var stepText = current >= 0 ? $"{current + 1}/{Mathf.Max(1, total)}" : "n/a";

            EditorGUILayout.LabelField($"Pipeline: {mode}  |  Step: {stepText}", EditorStyles.boldLabel);
            if (CaveBuildStartupCoordinator.IsActive)
            {
                EditorGUILayout.HelpBox(
                    "Startup in progress (tooling → scene → surface → cave). Watch the editor progress bar and " +
                    "Cave Build → Diagnostics → Pipeline Console. First surface pass can take several minutes.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(inProgress))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Build Complete Cave (120)", GUILayout.Height(30f)))
                    DeferGuiAction(LavaTubeCaveBuilder.BuildCompleteCaveActiveScene);
                if (GUILayout.Button("Build Surface Only", GUILayout.Height(30f)))
                    DeferGuiAction(LavaTubeCaveBuilder.BuildSurfaceWorldOnlyActiveScene);
                if (GUILayout.Button("Build Cave Only", GUILayout.Height(30f)))
                    DeferGuiAction(LavaTubeCaveBuilder.BuildCaveOnlyActiveScene);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Full AAA Rebuild", GUILayout.Height(24f)))
                    DeferGuiAction(LavaTubeCaveBuilder.BuildCompleteCaveFullAaaRebuild);
                if (GUILayout.Button("Apply MacBook Air Budget", GUILayout.Height(24f)))
                    DeferGuiAction(LavaTubeCaveBuilder.ApplyMacBookAirHardwareBudgetMenu);
                if (GUILayout.Button("Apply Offline (No API)", GUILayout.Height(24f)))
                    DeferGuiAction(() => CaveBuildOfflineNoApiPreset.Apply(savePrefs: true));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Pipeline Console"))
                CaveBuildPipelineConsoleWindow.Open();
            if (GUILayout.Button("Open Cave Grader"))
                CaveBuildGraderWindow.Open();
            if (GUILayout.Button("Open Terrain Grader"))
                TerrainBuildGraderWindow.Open();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            DrawLiveStatusPreview();
        }

        void DrawLiveStatusPreview()
        {
            var text = LoadFilePreview(CaveBuildRunStatusPublisher.LiveStatusRel, 1800, out var exists, out _);
            EditorGUILayout.LabelField("Live build status (from Generated markdown)", EditorStyles.boldLabel);
            if (!exists)
            {
                EditorGUILayout.HelpBox(
                    "No live status yet. Start a build and this section will auto-refresh with phase + step details.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.TextArea(text, GUILayout.MinHeight(180f));
            if (GUILayout.Button("Reveal full live status file"))
                RevealRelativeFile(CaveBuildRunStatusPublisher.LiveStatusRel);
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
            _settings.enableAutonomousUntilShip = EditorGUILayout.Toggle(
                "Autonomous until ship target",
                _settings.enableAutonomousUntilShip);
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

            if (!File.Exists(ToAbsolute(CaveBuildRunStatusPublisher.LiveStatusRel)) &&
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
