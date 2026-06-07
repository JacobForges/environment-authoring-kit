#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Optional pop-out for filtered pipeline log. Build monitoring lives in Environment Kit Hub by default.
    /// </summary>
    public sealed class CaveBuildPipelineConsoleWindow : EditorWindow
    {
        Vector2 _scroll;
        string _filter = "Cave";
        bool _errorsOnly;
        double _nextRefresh;

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Pipeline Console")]
        public static void Open() => Open(forcePopOut: false);

        public static void Open(bool forcePopOut)
        {
            if (!forcePopOut && EnvironmentKitHubWindow.TryFocusBuildMonitor())
            {
                Debug.Log(
                    "[CaveBuild] Pipeline Console is merged into Environment Kit Hub (Build tab). " +
                    "Use Hub → Pop out Pipeline Console only if you need a second window.");
                return;
            }

            var w = GetWindow<CaveBuildPipelineConsoleWindow>("Cave Pipeline");
            w.minSize = new Vector2(480f, 400f);
            w.Show();
        }

        void OnEnable()
        {
            CaveBuildPipelineLog.EnsureHook();
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        void OnEditorUpdate()
        {
            var interval =
                LavaTubeCaveBuilder.IsBuildInProgress ||
                CaveBuildStartupCoordinator.IsActive ||
                CaveBuildRunStatusPublisher.HasActiveSession
                    ? 0.12
                    : 0.5;
            if (EditorApplication.timeSinceStartup < _nextRefresh)
                return;
            _nextRefresh = EditorApplication.timeSinceStartup + interval;
            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Cave Build Pipeline Console (pop-out)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Prefer Environment Kit Hub during builds — this window duplicates feeds and costs extra editor RAM.",
                MessageType.Warning);
            if (GUILayout.Button("Focus Environment Kit Hub"))
                EnvironmentKitHubWindow.EnsureOpenForBuild();

            EditorGUILayout.Space(6f);
            EnvironmentKitBuildMonitorPanels.DrawLiveStatusMarkdown(compact: false);
            EditorGUILayout.Space(6f);
            EnvironmentKitBuildMonitorPanels.DrawGradePanel();
            EditorGUILayout.Space(6f);
            DrawAgentPanel();
            EditorGUILayout.Space(6f);
            DrawLogToolbar();
            DrawLogList();
        }

        void DrawAgentPanel()
        {
            EditorGUILayout.LabelField("AI grading (optional)", EditorStyles.boldLabel);
            if (!CaveBuildSessionPreset.HasUsableAiProvider)
            {
                EditorGUILayout.HelpBox(
                    "Procedural build — no API keys in Hub. Grading agents are not used; cave/surface steps run without cloud AI.",
                    MessageType.Info);
            }
            else if (CaveBuildCursorAgentBridge.IsAgentRunning)
            {
                EditorGUILayout.HelpBox("Grading agent running — watch log below.", MessageType.Warning);
            }
            else if (!CaveBuildCursorAgentBridge.HasApiKey)
            {
                EditorGUILayout.HelpBox(
                    CaveBuildCursorSettings.GraderCredentialHint(),
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"Idle — {CaveBuildCursorSettings.ResolveActiveProvider()} ready.");
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Hub AI Settings"))
                EnvironmentKitHubWindow.EnsureOpenForBuild();
            EditorGUILayout.EndHorizontal();
        }

        void DrawLogToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            _filter = EditorGUILayout.TextField("Filter", _filter);
            _errorsOnly = EditorGUILayout.ToggleLeft("Errors/warn only", _errorsOnly, GUILayout.Width(120));
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
                CaveBuildPipelineLog.Clear();
            if (GUILayout.Button("Export JSON", GUILayout.Width(90)))
                CaveBuildPipelineLog.ExportJson();
            EditorGUILayout.EndHorizontal();
        }

        void DrawLogList()
        {
            var entries = CaveBuildPipelineLog.GetEntries();
            var snapshot = new CaveBuildPipelineLog.Entry[entries.Count];
            for (var i = 0; i < entries.Count; i++)
                snapshot[i] = entries[i];

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                for (var i = 0; i < snapshot.Length; i++)
                {
                    var e = snapshot[i];
                    if (e == null)
                        continue;
                    if (_errorsOnly && e.level != "error" && e.level != "warn")
                        continue;
                    if (!string.IsNullOrEmpty(_filter) &&
                        (e.message ?? string.Empty).IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        (e.source ?? string.Empty).IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var c = e.level switch
                    {
                        "error" => Color.red,
                        "warn" => new Color(1f, 0.7f, 0.2f),
                        _ => Color.white,
                    };
                    var prev = GUI.color;
                    GUI.color = c;
                    EditorGUILayout.LabelField($"[{FormatUtcShort(e.utc)}] {e.message}", EditorStyles.wordWrappedMiniLabel);
                    GUI.color = prev;
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        static string FormatUtcShort(string utc)
        {
            if (string.IsNullOrEmpty(utc))
                return "--:--:--";
            if (utc.Length >= 19)
                return utc.Substring(11, 8);
            return utc.Length <= 8 ? utc : utc.Substring(utc.Length - 8);
        }
    }
}
#endif
