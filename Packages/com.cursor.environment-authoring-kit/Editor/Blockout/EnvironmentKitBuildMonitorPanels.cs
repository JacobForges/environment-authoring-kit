#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Shared live-build UI used by Environment Kit Hub (Pipeline Console is optional pop-out).</summary>
    static class EnvironmentKitBuildMonitorPanels
    {
        public static void DrawGradePanel()
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

        public static void DrawLiveStatusMarkdown(bool compact)
        {
            if (CaveBuildRunStatusPublisher.TryGetLiveStatusPreview(compact ? 1200 : 1600, out var preview))
            {
                EditorGUILayout.TextArea(preview, GUILayout.MinHeight(compact ? 80f : 120f));
            }
            else
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var path = Path.Combine(hub, CaveBuildRunStatusPublisher.GetLiveStatusReadRel());
                if (!File.Exists(path))
                {
                    EditorGUILayout.HelpBox(
                        "No live run status yet. Start a build to see phase + research notes here.",
                        MessageType.Info);
                    return;
                }

                try
                {
                    var text = File.ReadAllText(path);
                    var max = compact ? 1200 : 1600;
                    preview = text.Length > max ? text.Substring(0, max) + "\n…" : text;
                    EditorGUILayout.HelpBox("Showing last saved status file (no active session).", MessageType.Info);
                    EditorGUILayout.TextArea(preview, GUILayout.MinHeight(compact ? 80f : 120f));
                }
                catch
                {
                    EditorGUILayout.HelpBox("Could not read live status file.", MessageType.Warning);
                }
            }

            if (GUILayout.Button("Reveal full live status file"))
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var revealPath = Path.Combine(hub, CaveBuildRunStatusPublisher.GetLiveStatusReadRel());
                if (File.Exists(revealPath))
                    EditorUtility.RevealInFinder(revealPath);
            }
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
