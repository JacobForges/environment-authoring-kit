#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Starts Play Mode route + combat bots and merges results into live fix request.</summary>
    [InitializeOnLoad]
    static class CavePlaytestRouteBotBridge
    {
        public const string AutoRunKey = "CaveBuild_AutoRunPlaytestBot";

        static CavePlaytestRouteBotBridge()
        {
            // Prevent stale pending bot schedules from hijacking normal Play presses.
            EditorPrefs.SetBool(AutoRunKey, false);
            HookPlayMode();
        }

        /// <summary>Queues Play Mode route + combat bots after the next Enter Play Mode (used post-build).</summary>
        public static void ScheduleAfterBuild(Transform cave)
        {
            if (cave == null)
                return;

            var ground = SceneGroundResolver.Resolve();
            var request = WorldGenerationRequest.LoadOrDefault();
            CavePlaytestPreBuildPipeline.Run(cave, ground, request, out _);

            EnsureBots(cave);
            var player = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (player != null)
                PlayerCameraRig.Ensure(player);
            var surface = SurfacePlaytestValidator.Run(cave);
            SurfacePlaytestValidator.Export(surface);
            EditorPrefs.SetBool(AutoRunKey, true);
            var surfaceNote = surface.Passed
                ? "Surface probe OK."
                : $"Surface probe: {surface.Issues.Count} issue(s) — see {SurfacePlaytestValidator.ReportPath}.";
            Debug.Log(
                "[CaveBuild] Playtest bot scheduled (human NavMesh capsule + spectator cam) — enter Play Mode. " +
                surfaceNote);
        }

        [MenuItem(CaveBuildMenuPaths.PlayMode + "Run Cave Playtest Bot (Play Mode)")]
        public static void RunPlayModeBotMenu()
        {
            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (cave == null)
            {
                EditorUtility.DisplayDialog("Cave Playtest Bot", "Build a cave first.", "OK");
                return;
            }

            EnsureBots(cave);
            EditorPrefs.SetBool(AutoRunKey, true);
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
            else
            {
                cave.GetComponent<CavePlaytestRouteBot>()?.BeginRouteWalk();
                CaveCombatGameTypes.EnsureCombatPlaytestBot(cave, beginProbe: true);
            }
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Run Probe + Request Cursor Fix")]
        public static void RunProbeAndCursorMenu()
        {
            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (cave == null)
            {
                EditorUtility.DisplayDialog("Cave Probe", "No cave in scene.", "OK");
                return;
            }

            CaveRouteProbeRunner.ExportAndNotifyCursor(cave, invokeAgent: true);
        }

        static void HookPlayMode()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(AutoRunKey, false))
                {
                    EditorPrefs.SetBool(AutoRunKey, false);
                    EditorApplication.delayCall += () =>
                    {
                        var cave = CaveRouteProbeRunner.FindCaveRoot();
                        if (cave == null)
                            return;
                        EnsureBots(cave);
                        cave.GetComponent<CavePlaytestRouteBot>()?.BeginRouteWalk();
                        CaveCombatGameTypes.EnsureCombatPlaytestBot(cave, beginProbe: true);
                    };
                }

                if (state == PlayModeStateChange.ExitingPlayMode)
                {
                    var player = GameObject.FindGameObjectWithTag("Player")?.transform;
                    player?.GetComponent<PlayerCameraRig>()?.SetPlaytestSpectate(false, null);
                }

                if (state != PlayModeStateChange.ExitingPlayMode)
                    return;

                var caveRoot = CaveRouteProbeRunner.FindCaveRoot();
                if (caveRoot == null)
                    return;

                var issues = new List<string>();
                var routeBot = caveRoot.GetComponent<CavePlaytestRouteBot>();
                if (routeBot != null)
                {
                    foreach (var line in routeBot.PlayIssues)
                        issues.Add(line);
                }

                CaveCombatGameTypes.CollectCombatPlaytestIssues(caveRoot, issues);

                CavePlaytestIssueReport.Export(issues, "play_mode_exit");

                if (issues.Count == 0)
                    return;

                CaveLiveCodegenRequest.Write(caveRoot, issues, "playtest_bot");
            };
        }

        static void EnsureBots(Transform cave)
        {
            if (cave.GetComponent<CavePlaytestRouteBot>() == null)
                cave.gameObject.AddComponent<CavePlaytestRouteBot>();
            CaveCombatGameTypes.EnsureCombatPlaytestBot(cave, beginProbe: false);
        }
    }
}
#endif
