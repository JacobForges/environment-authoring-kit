#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Wires Hub CombatStats/NpcEnemy on surface enemies and ensures CaveEnemy prefab exists.</summary>
    public static class HubSurfaceCombatWiring
    {
        [MenuItem("Window/Environment Kit/World/Wire Hub Combat (surface + player)")]
        public static void WireFromMenu()
        {
            CaveCombatSetupUtility.EnsureEnemyPrefab();
            CaveCombatSetupUtility.WireSceneCombat();
            WireSurfaceEnemies();
            Debug.Log("[HubCombat] Surface combat wired.");
        }

        public static void WireSurfaceEnemies()
        {
            CaveCombatGameTypes.WarnIfMissing();
            if (!CaveCombatGameTypes.IsGameplayAvailable)
            {
                Debug.LogWarning("[HubCombat] Combat scripts missing — add Assets/Scripts and wait for compile.");
                return;
            }

            var prefab = CaveCombatSetupUtility.EnsureEnemyPrefab();
            foreach (var spawner in Object.FindObjectsByType<SurfaceTerrainEnemySpawner>(FindObjectsInactive.Include))
            {
                if (spawner != null && prefab != null)
                    spawner.enemyPrefab = prefab;
            }

            var statsType = CaveCombatGameTypes.CombatStats;
            if (statsType == null)
                return;

            foreach (var stats in Object.FindObjectsByType(statsType, FindObjectsInactive.Include))
            {
                if (stats == null)
                    continue;
                var go = ((Component)stats).gameObject;
                if (go.GetComponent<WorldEnemyLootDropper>() == null)
                {
                    var drop = go.AddComponent<WorldEnemyLootDropper>();
                    drop.biome = WorldSurfaceBiomeId.PlayKarst;
                }
            }
        }
    }
}
#endif
