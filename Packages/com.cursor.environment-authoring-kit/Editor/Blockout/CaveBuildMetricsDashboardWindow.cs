#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public sealed class CaveBuildMetricsDashboardWindow : EditorWindow
    {
        Vector2 _scroll;

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Ladder Metrics Dashboard", false, 15)]
        public static void Open()
        {
            var w = GetWindow<CaveBuildMetricsDashboardWindow>(false, "Cave Ladder Metrics", true);
            w.minSize = new Vector2(480, 320);
            w.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Environment Kit — ladder metrics", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Florida karst surface + lava-tube cave for Unity XR. " +
                "Iteration target: &lt;60s per mask tweak with incremental ladder on.",
                MessageType.Info);

            if (GUILayout.Button("Refresh"))
                Repaint();

            if (GUILayout.Button("Open metrics markdown"))
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var path = Path.Combine(hub, CaveBuildAgentContextExporter.Folder + "/CaveBuildMetricsDashboard.md");
                if (File.Exists(path))
                    EditorUtility.RevealInFinder(path);
            }

            if (GUILayout.Button("Open phase contracts JSON"))
            {
                CaveBuildPhaseContractRegistry.ExportContractsCatalog();
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var path = Path.Combine(hub, CaveBuildPhaseContractRegistry.ContractsExportRel);
                if (File.Exists(path))
                    EditorUtility.RevealInFinder(path);
            }

            if (GUILayout.Button("Invalidate all ladder rungs"))
                CaveBuildPhaseContractRegistry.InvalidateAll();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var m = CaveBuildLadderMetrics.Load();
            EditorGUILayout.LabelField("Last build", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Scene", m.lastScene);
            EditorGUILayout.LabelField("Seed", m.lastSeed.ToString());
            EditorGUILayout.LabelField("Grade", $"{m.lastLetterGrade} ({m.lastOverallScore:F0})");
            if (!string.IsNullOrEmpty(m.showcaseRecipeId))
                EditorGUILayout.LabelField("Recipe", m.showcaseRecipeId);
            EditorGUILayout.LabelField("Updated", m.updatedUtc ?? "—");

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Per-rung timings", EditorStyles.boldLabel);
            foreach (var r in m.rungs ?? System.Array.Empty<CaveBuildLadderMetrics.RungMetric>())
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(r.rungId, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Last: {r.lastDurationMs:F0} ms | Runs: {r.runCount} | Skips: {r.skipCount}");
                EditorGUILayout.LabelField(r.lastSkipped ? "Last run: SKIPPED (incremental)" : "Last run: executed");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
