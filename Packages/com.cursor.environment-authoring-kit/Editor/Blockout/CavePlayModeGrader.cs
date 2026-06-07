#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    [InitializeOnLoad]
    sealed class CavePlayModeGrader
    {
        const double IntervalSeconds = 8.0;
        static double _nextCheck;
        static Transform _lastCave;

        static CavePlayModeGrader()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _nextCheck = EditorApplication.timeSinceStartup + 2.0;
                CaveLiveBuildMonitor.OnCaveReady += OnCaveReady;
                CaveLiveBuildMonitor.OnGradeRequired += OnGradeRequired;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                CaveLiveBuildMonitor.OnCaveReady -= OnCaveReady;
                CaveLiveBuildMonitor.OnGradeRequired -= OnGradeRequired;
            }
        }

        static void OnCaveReady(Transform cave) => RunLightweightGrade(cave, "cave_ready");

        static void OnGradeRequired(Transform cave) => RunLightweightGrade(cave, "grade_required");

        static void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup < _nextCheck)
                return;

            _nextCheck = EditorApplication.timeSinceStartup + IntervalSeconds;
            var cave = CaveLiveBuildMonitor.ActiveCaveRoot ?? FindCave();
            if (cave == null)
                return;

            RunLightweightGrade(cave, "periodic");
        }

        static void RunLightweightGrade(Transform caveRoot, string reason)
        {
            if (caveRoot == null || caveRoot == _lastCave && reason == "periodic")
                return;

            _lastCave = caveRoot;
            var issues = new List<string>();
            var walkFloors = CaveAdventurePlayabilityPipeline.CountWalkFloors(caveRoot);
            if (walkFloors < 4)
                issues.Add($"Only {walkFloors} walk floors during play.");

            if (!CavePlayabilityValidator.CheckSpawnReachability(caveRoot))
                issues.Add("Spawn not reachable from entrance during play.");

            if (CaveBuildQualitySystem.DetectHorizontalOnionBands(caveRoot, out var bandReason))
                issues.Add(bandReason);

            var shellScore = CaveBuildVisualShellAuditor.Audit(caveRoot).ComputeScore(
                compactRoute: true,
                layoutPrototype: CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot));
            if (shellScore < 80)
                issues.Add($"Play-mode visual shell score {shellScore}.");

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                var probe = CaveRouteProbeRunner.Run(caveRoot);
                CaveRouteProbeRunner.Export(probe, caveRoot);
                foreach (var pi in probe.Issues)
                    issues.Add($"[{pi.SuggestedStageId}] {pi.Message}");
            }

            if (issues.Count == 0)
                return;

            CaveLiveCodegenRequest.Write(caveRoot, issues, reason);
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if ((settings.autoInvokeAfterEveryBuild || settings.autoInvokeOnDud) &&
                CaveBuildCursorAgentBridge.HasApiKey)
                CaveBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(out _, includeLiveFix: true);
        }

        static Transform FindCave()
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

            return GameObject.Find(CaveGeometryPaths.LegacyCaveSystemRootName)?.transform;
        }

        [MenuItem("Window/Environment Kit/Cave Build/Request Live Fix (Cursor)")]
        public static void RequestLiveFixMenu()
        {
            var cave = FindCave();
            if (cave == null)
            {
                EditorUtility.DisplayDialog("Live Fix", "No cave root in scene.", "OK");
                return;
            }

            CaveLiveCodegenRequest.Write(cave, new List<string> { "Manual live fix requested from menu." }, "manual");
            if (CaveBuildCursorAgentBridge.TryInvokeGradeAndFix(out var msg, includeLiveFix: true))
                EditorUtility.DisplayDialog("Live Fix", "Cursor agent invoked with live fix section.", "OK");
            else
                EditorUtility.DisplayDialog("Live Fix", msg, "OK");
        }
    }
}
#endif
