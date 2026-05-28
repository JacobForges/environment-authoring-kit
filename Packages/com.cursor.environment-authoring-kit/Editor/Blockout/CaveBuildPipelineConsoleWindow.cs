#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Live build pipeline log, grades, ship blockers, and agent status.</summary>
    public sealed class CaveBuildPipelineConsoleWindow : EditorWindow
    {
        Vector2 _scroll;
        string _filter = "Cave";
        bool _errorsOnly;
        double _nextRefresh;

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Pipeline Console")]
        public static void Open() => GetWindow<CaveBuildPipelineConsoleWindow>("Cave Pipeline");

        void OnEnable()
        {
            CaveBuildPipelineLog.EnsureHook();
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        void OnEditorUpdate()
        {
            var interval = LavaTubeCaveBuilder.IsBuildInProgress ? 1.0 : 0.5;
            if (EditorApplication.timeSinceStartup < _nextRefresh)
                return;
            _nextRefresh = EditorApplication.timeSinceStartup + interval;
            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Cave Build Pipeline Console", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Paced build steps log here first (not Unity Console) to keep the editor stable. " +
                "Enable mirror on CaveBuildCursorSettings if you need Console echo.",
                MessageType.None);
            DrawRunStatusPanel();
            EditorGUILayout.Space(6);
            DrawGradePanel();
            EditorGUILayout.Space(6);
            DrawAgentPanel();
            EditorGUILayout.Space(6);
            DrawLogToolbar();
            DrawLogList();
        }

        void DrawRunStatusPanel()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = System.IO.Path.Combine(hub, CaveBuildRunStatusPublisher.GetLiveStatusReadRel());
            if (!System.IO.File.Exists(path))
            {
                EditorGUILayout.HelpBox(
                    "No live run status yet. Start Build Complete Cave to see phase + research notes.",
                    MessageType.Info);
                return;
            }

            try
            {
                var text = System.IO.File.ReadAllText(path);
                var preview = text.Length > 1600 ? text.Substring(0, 1600) + "\n…" : text;
                EditorGUILayout.LabelField("Live run status", EditorStyles.boldLabel);
                if (LavaTubeCaveBuilder.IsBuildInProgress)
                {
                    EditorGUILayout.HelpBox(
                        "Build in progress — this panel refreshes ~5×/sec. Watch Working now / Last activity.",
                        MessageType.None);
                }

                EditorGUILayout.TextArea(preview, GUILayout.MinHeight(120));
                if (GUILayout.Button("Open full CaveBuildLiveRunStatus.md"))
                    EditorUtility.RevealInFinder(path);
            }
            catch
            {
                EditorGUILayout.HelpBox("Could not read live status file.", MessageType.Warning);
            }
        }

        void DrawGradePanel()
        {
            var report = CaveBuildQualitySystem.LastGradedReport ??
                         CaveBuildQualityReportLoader.LoadOrNull();
            if (report == null)
            {
                EditorGUILayout.HelpBox(
                    "No quality report loaded. Run Build Complete Cave or Re-grade.",
                    MessageType.Info);
                return;
            }

            var pass = report.BuildAcceptable && report.MeetsShipTarget;
            var color = pass ? Color.green : report.IsDud ? Color.red : new Color(1f, 0.55f, 0.1f);
            var prev = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField(
                $"Grade: {report.LetterGrade} ({report.OverallScore}/100)  |  Weighted: {report.WeightedOverallScore}/100",
                EditorStyles.largeLabel);
            GUI.color = prev;

            EditorGUILayout.LabelField(
                $"Beta+: {report.MeetsBetaTarget}  |  Ship: {report.MeetsShipTarget}  |  Acceptable: {report.BuildAcceptable}  |  Dud: {report.IsDud}");
            EditorGUILayout.LabelField($"Mode: {report.GradingMode}  |  Action: {report.RecommendedAction}");

            if (report.ShipBlockers.Count > 0)
            {
                EditorGUILayout.LabelField("Ship blockers:", EditorStyles.miniBoldLabel);
                foreach (var b in report.ShipBlockers)
                    EditorGUILayout.LabelField("• " + b, EditorStyles.wordWrappedMiniLabel);
            }

            var failing = CaveBuildQualityRubric.GetFailingStages(report);
            if (failing.Count > 0)
            {
                EditorGUILayout.LabelField("Failing stages:", EditorStyles.miniBoldLabel);
                foreach (var s in failing)
                {
                    var issue = s.Issues.Count > 0 ? s.Issues[0] : "—";
                    EditorGUILayout.LabelField($"• {s.StageId} ({s.Score}): {issue}", EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Quality JSON"))
                CaveBuildQualityMenu.OpenQualityReport();
            if (GUILayout.Button("Re-grade Scene"))
            {
                var cave = FindCave();
                if (cave != null)
                    CaveBuildQualitySystem.Grade(
                        cave,
                        SceneGroundResolver.Resolve(),
                        null,
                        null,
                        invokeCursorAgent: false);
            }

            EditorGUILayout.EndHorizontal();
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
                EnvironmentKitHubWindow.Open();
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

        static Transform FindCave()
        {
            var grid = GameObject.Find("Grid");
            if (grid != null)
            {
                var t = grid.transform.Find(CaveGeometryPaths.CaveSystemRootName);
                if (t != null)
                    return t;
            }

            return GameObject.Find(CaveGeometryPaths.LegacyCaveSystemRootName)?.transform;
        }
    }
}
#endif
