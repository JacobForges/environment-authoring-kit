#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// When <c>Assets/EnvironmentKit/.run-planv4-menus</c> exists, runs cinematic + combat wiring once on domain reload.
    /// </summary>
    [InitializeOnLoad]
    static class WorldPlanV4MenuBootstrap
    {
        const string FlagAssetPath = "Assets/EnvironmentKit/.run-planv4-menus";

        static WorldPlanV4MenuBootstrap()
        {
            if (!File.Exists(FlagAssetPath))
                return;

            EditorApplication.delayCall += RunOnce;
        }

        static void RunOnce()
        {
            if (!File.Exists(FlagAssetPath))
                return;

            try
            {
                WorldProjectTagSetup.EnsureBossStagePortalTag();

                File.Delete(FlagAssetPath);
                AssetDatabase.Refresh();

                var terrain = UnityEngine.Object.FindAnyObjectByType<UnityEngine.Terrain>();
                var triggers = UnityEngine.Object.FindObjectsByType<EnvironmentAuthoringKit.World.WorldCinematicTrigger>(
                    FindObjectsInactive.Include);
                if (triggers.Length == 0)
                    WorldFxAndMediaAuthor.Apply(terrain, null);

                UnityEngine.Debug.Log("[PlanV4] Running Wire Hub Combat…");
                HubSurfaceCombatWiring.WireFromMenu();

                UnityEngine.Debug.Log("[PlanV4] Running Build Cinematic Timelines…");
                WorldCinematicTimelineAuthor.BindAllTriggersInScene();

                AssetDatabase.SaveAssets();
                UnityEngine.Debug.Log("[PlanV4] Cinematic timelines + Hub combat wiring complete.");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[PlanV4] Menu bootstrap failed: {ex.Message}");
            }
        }
    }
}
#endif
