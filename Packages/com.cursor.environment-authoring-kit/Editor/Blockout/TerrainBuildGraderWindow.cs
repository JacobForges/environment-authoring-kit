#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public sealed class TerrainBuildGraderWindow : EditorWindow
    {
        SurfaceTerrainLadderReport _report;
        Vector2 _scroll;
        string _status = "Run Re-grade to evaluate above-ground terrain.";

        [MenuItem("Window/Environment Kit/Terrain Build Grader")]
        public static void Open() => GetWindow<TerrainBuildGraderWindow>("Terrain Build Grader");

        [MenuItem(CaveBuildMenuPaths.TerrainRegradeOnly, false, 12)]
        public static void MenuRegradeOnly()
        {
            Open();
            var w = GetWindow<TerrainBuildGraderWindow>();
            w.Regrade();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Above-Ground Terrain Quality", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (_report != null)
            {
                var color = _report.BuildAcceptable
                    ? Color.green
                    : _report.OverallScore < SurfaceTerrainBuildLadder.StageFloorScore
                        ? Color.red
                        : new Color(1f, 0.55f, 0.1f);
                var prev = GUI.color;
                GUI.color = color;
                EditorGUILayout.LabelField(
                    $"Letter: {_report.LetterGrade}  ({_report.OverallScore}/100)",
                    EditorStyles.largeLabel);
                GUI.color = prev;
                EditorGUILayout.LabelField(
                    $"Target: {SurfaceTerrainBuildLadder.TargetOverallScore}+  |  Acceptable: {_report.BuildAcceptable}");
            }

            EditorGUILayout.Space(8);

            var buildSurface = false;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build Surface World", GUILayout.Height(32)))
                buildSurface = true;
            EditorGUILayout.EndHorizontal();

            var regrade = false;
            var exportPrompt = false;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-grade", GUILayout.Height(28)))
                regrade = true;
            if (GUILayout.Button("Export Agent Prompt", GUILayout.Height(28)))
                exportPrompt = true;
            GUI.enabled = TerrainBuildCursorAgentBridge.HasApiKey;
            if (GUILayout.Button("Invoke Cursor Agent", GUILayout.Height(28)))
                TerrainBuildCursorAgentBridge.MenuInvoke();
            if (GUILayout.Button("Run Full Terrain Workflow", GUILayout.Height(28)))
                TerrainBuildCursorAgentBridge.MenuBeginWorkflow();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Open Quality JSON"))
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var path = System.IO.Path.Combine(hub, SurfaceTerrainQualityGrader.QualityReportPath);
                if (System.IO.File.Exists(path))
                    EditorUtility.RevealInFinder(path);
            }

            if (GUILayout.Button("Cursor Settings"))
                Selection.activeObject = CaveBuildCursorSettings.LoadOrCreate();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Terrain + Cursor workflow", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Editor-only (not Play Mode):\n" +
                "1) Build Surface World — terrain phases + props ladder.\n" +
                "2) Re-grade writes SurfaceTerrainQualityReport.json.\n" +
                "3) Invoke Cursor (--workflow=terrain) or Run Full Terrain Workflow for chained rungs.\n" +
                "CLI: Tools/cave-grader → npm run run-terrain",
                MessageType.None);

            if (TerrainBuildCursorAgentBridge.IsAgentRunning)
                EditorGUILayout.HelpBox(
                    "Cursor agent running — watch Console [TerrainCursor].",
                    MessageType.Warning);
            else if (!TerrainBuildCursorAgentBridge.HasApiKey)
                EditorGUILayout.HelpBox(CaveBuildCursorSettings.CursorWorkflowCredentialHint(), MessageType.Warning);

            var diag = TerrainBuildCursorGraderDiagnostics.TryReadLastRunSummary();
            if (!string.IsNullOrEmpty(diag))
                EditorGUILayout.HelpBox("Last run: " + diag, MessageType.Info);

            EditorGUILayout.HelpBox(_status, MessageType.Info);

            if (_report?.Stages == null || _report.Stages.Count == 0)
            {
                RunDeferred(buildSurface, regrade, exportPrompt);
                return;
            }

            EditorGUILayout.LabelField("Rungs", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var stage in _report.Stages)
            {
                if (stage == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"{stage.StageName}  [{stage.StageId}]", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Score: {stage.Score} / 100  (weight {stage.Weight})  {(stage.Passed ? "PASS" : "FAIL")}");
                foreach (var issue in stage.Issues)
                    EditorGUILayout.LabelField("• " + issue, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            RunDeferred(buildSurface, regrade, exportPrompt);
        }

        void RunDeferred(bool buildSurface, bool regrade, bool exportPrompt)
        {
            if (buildSurface)
            {
                try
                {
                    LavaTubeCaveBuilder.BuildSurfaceWorldOnlyActiveScene();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    _status = "Surface build failed: " + ex.Message;
                }
            }

            if (regrade)
                Regrade();
            if (exportPrompt)
                ExportPrompt();
        }

        void Regrade()
        {
            var ground = SceneGroundResolver.Resolve();
            var request = new WorldGenerationRequest
            {
                SurfaceScope = SurfaceBuildScope.SurfaceOnly,
                SurfaceIncludeTrails = true,
                AllowCreateTerrain = true,
            };
            var surface = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
            _report = SurfaceTerrainQualityGrader.Run(ground, request, surface);

            var active = SurfaceTerrainBuildLadder.PickActiveRung(_report);
            if (active == "heightfield_no_craters" && ground?.Terrain != null &&
                SurfaceTerrainLadderFixer.TryFix(active, ground, request, surface, out var fixMsg))
            {
                _report = SurfaceTerrainQualityGrader.Run(ground, request, surface);
                _status =
                    $"Graded {_report.SceneName} — {_report.LetterGrade} ({_report.OverallScore}). " +
                    $"Heightfield fix: {fixMsg}";
            }
            else
            {
                _status =
                    $"Graded {_report.SceneName} — {_report.LetterGrade} ({_report.OverallScore}). " +
                    $"Active rung: {active ?? "none"}";
            }

            Repaint();
        }

        void ExportPrompt()
        {
            if (_report == null)
                Regrade();
            if (_report == null)
                return;

            var rung = SurfaceTerrainBuildLadder.PickActiveRung(_report);
            if (TerrainBuildRungPromptExporter.PrepareAgentInvoke(rung, out var msg))
                _status = "Exported terrain agent prompt. " + msg;
            else
                _status = "Export failed: " + msg;
            Repaint();
        }
    }
}
#endif
