#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public sealed class CaveBuildGraderWindow : EditorWindow
    {
        CaveBuildQualityReport _report;
        Vector2 _scroll;
        string _status = "Run Re-grade to evaluate the active scene cave.";

        [MenuItem("Window/Environment Kit/Cave Build Grader")]
        public static void Open() => GetWindow<CaveBuildGraderWindow>("Cave Build Grader");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Cave Build Quality", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (_report != null)
            {
                var color = _report.BuildAcceptable ? Color.green : _report.IsDud ? Color.red : new Color(1f, 0.55f, 0.1f);
                var prev = GUI.color;
                GUI.color = color;
                EditorGUILayout.LabelField($"Letter: {_report.LetterGrade}  ({_report.OverallScore}/100)", EditorStyles.largeLabel);
                GUI.color = prev;
                EditorGUILayout.LabelField($"Acceptable: {_report.BuildAcceptable}  |  Dud: {_report.IsDud}  |  Action: {_report.RecommendedAction}");
                if (_report.DudReasons.Count > 0)
                {
                    EditorGUILayout.LabelField("Dud reasons:", EditorStyles.miniBoldLabel);
                    foreach (var r in _report.DudReasons)
                        EditorGUILayout.LabelField("• " + r, EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.Space(8);

            var generateCave = false;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate Complete Cave", GUILayout.Height(32)))
                generateCave = true;
            EditorGUILayout.EndHorizontal();

            var regrade = false;
            var exportPrompt = false;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-grade", GUILayout.Height(28)))
                regrade = true;
            if (GUILayout.Button("Export Agent Prompt", GUILayout.Height(28)))
                exportPrompt = true;
            GUI.enabled = CaveBuildCursorAgentBridge.HasApiKey;
            if (GUILayout.Button("Invoke Cursor Agent", GUILayout.Height(28)))
                CaveBuildCursorAgentBridge.MenuInvoke();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Open Quality JSON"))
                CaveBuildQualityMenu.OpenQualityReport();

            if (GUILayout.Button("Cursor Settings"))
                Selection.activeObject = CaveBuildCursorSettings.LoadOrCreate();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Unity + Cursor workflow", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Editor build (menu), not Play Mode:\n" +
                "Each meat-loop pass writes CaveBuildQualityReport.json (fresh scores).\n" +
                "1) Build Complete Cave → meat loop fixes scene in-editor (up to 16 passes).\n" +
                "2) Cursor API: once at end by default; enable 'Invoke each meat-loop pass' in Cursor Settings to send JSON every pass.\n" +
                "3) When agent OK: auto-rebuild applies new C# to the scene.\n" +
                "Play Mode only grades; it does not run the full build pipeline.",
                MessageType.None);
            EditorGUILayout.Space(4);
            if (CaveBuildCursorAgentBridge.IsAgentRunning)
                EditorGUILayout.HelpBox("Cursor agent is running in background — check Console [CaveCursor].", MessageType.Warning);
            else if (!CaveBuildCursorAgentBridge.HasApiKey)
                EditorGUILayout.HelpBox(CaveBuildCursorSettings.CursorWorkflowCredentialHint(), MessageType.Warning);
            else
            {
                var s = CaveBuildCursorSettings.LoadOrCreate();
                s.LoadFromPrefs();
                if (s.autoInvokeAfterEveryBuild)
                    EditorGUILayout.HelpBox(
                        "Auto-invoke ON: Cursor runs after each build that has not reached Ship (commercial production, 95+).",
                        MessageType.Info);
            }

            var caveDiag = CaveBuildCursorGraderDiagnostics.TryReadLastRunSummary();
            if (!string.IsNullOrEmpty(caveDiag))
                EditorGUILayout.HelpBox("Last cave agent: " + caveDiag, MessageType.None);
            if (GUILayout.Button("Open Terrain Build Grader"))
                TerrainBuildGraderWindow.Open();

            EditorGUILayout.HelpBox(_status, MessageType.Info);

            if (_report?.Stages == null || _report.Stages.Count == 0)
            {
                RunDeferredActions(generateCave, regrade, exportPrompt);
                return;
            }

            EditorGUILayout.LabelField("Stages", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var stage in _report.Stages)
            {
                if (stage == null)
                    continue;
                var fail = !stage.Passed;
                using (new EditorGUI.DisabledScope(!fail))
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"{stage.StageName}  [{stage.StageId}]", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Score: {stage.Score} / 100  (weight {stage.Weight})  {(stage.Passed ? "PASS" : "FAIL")}");
                    foreach (var issue in stage.Issues)
                        EditorGUILayout.LabelField("• " + issue, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.EndScrollView();
            RunDeferredActions(generateCave, regrade, exportPrompt);
        }

        void RunDeferredActions(bool generateCave, bool regrade, bool exportPrompt)
        {
            if (generateCave)
            {
                try
                {
                    LavaTubeCaveBuilder.BuildInActiveScene(
                        openMainSceneFirst: false,
                        hideLegacyBlockout: true);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    _status = "Build failed: " + ex.Message;
                }
            }

            if (regrade)
                Regrade();
            if (exportPrompt)
                ExportPrompt();
        }

        void Regrade()
        {
            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.GradingOnly);
            var cave = FindCaveRoot();
            if (cave == null)
            {
                _status = "LavaTubeCaveSystem / UndergroundCaveSystem not found.";
                return;
            }

            var ground = SceneGroundResolver.Resolve();
            var request = new WorldGenerationRequest
            {
                UseLayoutPrototype = CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(cave),
                UseSplineMesh = true,
                UseBlockTunnel = true,
                UseTrue3DCaveSystem = true
            };
            _report = CaveBuildQualityGrader.GradeFullBuild(cave, ground, request, null);
            CaveBuildQualitySystem.SetLastGradedReport(_report);
            _status = $"Graded {_report.SceneName} — {_report.LetterGrade} ({_report.OverallScore}).";
            Repaint();
        }

        void ExportPrompt()
        {
            if (_report == null)
                Regrade();
            if (_report == null)
                return;

            CaveBuildQualityAgentBridge.WriteStructuredPrompt(_report, includeLiveSection: false);
            _status = "Wrote CaveBuildAgentPrompt.md + meat loop prompt.";
            Repaint();
        }

        static Transform FindCaveRoot()
        {
            var grid = GameObject.Find("Grid");
            if (grid != null)
            {
                var t = grid.transform.Find(CaveGeometryPaths.CaveSystemRootName);
                if (t != null)
                    return t;
                t = grid.transform.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (t != null)
                    return t;
            }

            var legacy = GameObject.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            return legacy != null ? legacy.transform : null;
        }
    }
}
#endif
