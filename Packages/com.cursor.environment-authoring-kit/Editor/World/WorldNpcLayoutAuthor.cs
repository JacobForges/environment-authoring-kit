#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Plan v4 — full 18-slot NPC layout + biome enemy spawners (AAA).</summary>
    public static class WorldNpcLayoutAuthor
    {
        public const string RootName = "WorldNpcLayout";

        sealed class SlotPlacement
        {
            public string slot;
            public WorldSurfaceBiomeId biome;
            public float angleDeg;
            public float radius;
            public bool isEnemySpawner;
            public int enemyCount;
        }

        static readonly SlotPlacement[] AllSlots =
        {
            new() { slot = "NPC_PlayGuide", biome = WorldSurfaceBiomeId.PlayKarst, angleDeg = 20f, radius = 12f },
            new() { slot = "NPC_PlayMerchant", biome = WorldSurfaceBiomeId.PlayKarst, angleDeg = 200f, radius = 18f },
            new() { slot = "NPC_FoothillRanger", biome = WorldSurfaceBiomeId.FoothillGreen, angleDeg = 45f, radius = 55f },
            new() { slot = "NPC_FoothillHerbalist", biome = WorldSurfaceBiomeId.FoothillGreen, angleDeg = 130f, radius = 62f },
            new() { slot = "NPC_PeakHermit", biome = WorldSurfaceBiomeId.PeakStone, angleDeg = 300f, radius = 95f },
            new() { slot = "NPC_PeakOreTrader", biome = WorldSurfaceBiomeId.PeakStone, angleDeg = 15f, radius = 88f },
            new() { slot = "NPC_PeakGuard_A", biome = WorldSurfaceBiomeId.PeakStone, angleDeg = 90f, radius = 72f },
            new() { slot = "NPC_PeakGuard_B", biome = WorldSurfaceBiomeId.PeakStone, angleDeg = 270f, radius = 74f },
            new() { slot = "NPC_HorizonWatcher", biome = WorldSurfaceBiomeId.HorizonMist, angleDeg = 180f, radius = 130f },
            new() { slot = "NPC_Ambient_A", biome = WorldSurfaceBiomeId.PlayKarst, angleDeg = 310f, radius = 28f },
            new() { slot = "NPC_Ambient_B", biome = WorldSurfaceBiomeId.FoothillGreen, angleDeg = 250f, radius = 48f },
            new() { slot = "NPC_Ambient_C", biome = WorldSurfaceBiomeId.HorizonMist, angleDeg = 60f, radius = 115f },
            new() { slot = "BOSS_Add_1", biome = WorldSurfaceBiomeId.BossThreshold, angleDeg = 0f, radius = 8f },
            new() { slot = "BOSS_Add_2", biome = WorldSurfaceBiomeId.BossThreshold, angleDeg = 120f, radius = 10f },
            new()
            {
                slot = "ENEMY_Foothill_1", biome = WorldSurfaceBiomeId.FoothillGreen, isEnemySpawner = true,
                enemyCount = 4, radius = 70f, angleDeg = 0f,
            },
            new()
            {
                slot = "ENEMY_Peak_1", biome = WorldSurfaceBiomeId.PeakStone, isEnemySpawner = true,
                enemyCount = 4, radius = 100f, angleDeg = 0f,
            },
            new()
            {
                slot = "ENEMY_Annex_1", biome = WorldSurfaceBiomeId.AnnexLabyrinth, isEnemySpawner = true,
                enemyCount = 5, radius = 40f, angleDeg = 0f,
            },
        };

        public static void Apply(Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null || request == null)
                return;

            var existing = GameObject.Find(RootName);
            if (PipelineContentPreservePolicy.TryPreserveSceneRoot(RootName, out existing, "world NPC layout"))
                return;

            var root = new GameObject(RootName);
            var tier = request.ContentTier;
            var seed = request.Seed;

            if (tier != WorldBuildContentTier.Aaa)
            {
                SpawnSlot(root.transform, "NPC_PlayGuide", ResolvePosition(mainTerrain, seed, 0f, 14f), seed);
                Debug.Log($"[Surface] World NPC layout — {tier} (guide only).", root);
                return;
            }

            if (!TryPlayCenter(mainTerrain, out var center))
                return;

            var portal = WorldProjectTagSetup.FindBossStagePortalObject();
            var bossAnchor = portal != null ? portal.transform.position : center + Vector3.up * 40f;

            foreach (var p in AllSlots)
            {
                if (p.isEnemySpawner)
                {
                    SpawnBiomeEnemies(root.transform, mainTerrain, request, p, seed);
                    continue;
                }

                var pos = p.biome == WorldSurfaceBiomeId.BossThreshold
                    ? bossAnchor + Quaternion.Euler(0f, p.angleDeg, 0f) * Vector3.forward * p.radius
                    : ResolvePosition(mainTerrain, seed + p.slot.GetHashCode(), p.angleDeg, p.radius);

                SpawnSlot(root.transform, p.slot, pos, seed + p.slot.GetHashCode());
            }

            Debug.Log($"[Surface] World NPC layout — AAA full roster, seed {seed}.", root);
        }

        static void SpawnBiomeEnemies(
            Transform parent,
            Terrain mainTerrain,
            WorldGenerationRequest request,
            SlotPlacement p,
            int seed)
        {
            var host = new GameObject($"Spawner_{p.slot}");
            host.transform.SetParent(parent, false);
            var spawner = host.AddComponent<SurfaceTerrainEnemySpawner>();
            spawner.spawnCount = p.enemyCount;
            spawner.spawnSeed = seed + p.slot.GetHashCode();
            spawner.spawnOnStart = false;
            spawner.enemyPrefab =
                CaveCombatSetupUtility.EnsureEnemyPrefab()
                ?? Cc0ContentImportUtility.LoadCharacterPrefab(p.slot);
            spawner.SpawnAll();
            WireLootOnChildren(host.transform, p.biome, seed);
        }

        static void WireLootOnChildren(Transform root, WorldSurfaceBiomeId biome, int seed)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i).gameObject;
                var drop = child.GetComponent<WorldEnemyLootDropper>();
                if (drop == null)
                    drop = child.AddComponent<WorldEnemyLootDropper>();
                drop.biome = biome;
                drop.lootSeed = seed + i * 17;
            }
        }

        static void SpawnSlot(Transform parent, string slot, Vector3 position, int seed)
        {
            var prefab = Cc0ContentImportUtility.LoadCharacterPrefab(slot);
            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.name = slot;
                instance.transform.position = position;
                instance.transform.rotation = Quaternion.Euler(0f, (seed % 360 + 360) % 360, 0f);
                return;
            }

            var marker = new GameObject($"{slot}_Marker");
            marker.transform.SetParent(parent, false);
            marker.transform.position = position;
            marker.AddComponent<CaveSpawnEditorMarker>();
        }

        static Vector3 ResolvePosition(Terrain mainTerrain, int seed, float angleDeg, float radius)
        {
            if (!TryPlayCenter(mainTerrain, out var center))
                return Vector3.zero;

            var rad = angleDeg * Mathf.Deg2Rad;
            var guess = center + new Vector3(Mathf.Cos(rad) * radius, 40f, Mathf.Sin(rad) * radius);
            if (mainTerrain != null)
            {
                var local = guess - mainTerrain.transform.position;
                var td = mainTerrain.terrainData;
                if (td != null &&
                    local.x >= 0f && local.z >= 0f &&
                    local.x <= td.size.x && local.z <= td.size.z)
                {
                    var h = td.GetInterpolatedHeight(local.x / td.size.x, local.z / td.size.z);
                    guess.y = mainTerrain.transform.position.y + h + 0.1f;
                }
            }

            return guess;
        }

        static bool TryPlayCenter(Terrain mainTerrain, out Vector3 pos) =>
            TryPlayCenterPublic(mainTerrain, out pos);

        public static bool TryPlayCenterPublic(Terrain mainTerrain, out Vector3 pos)
        {
            pos = mainTerrain != null
                ? mainTerrain.transform.position + mainTerrain.terrainData.size * 0.5f
                : Vector3.zero;
            return mainTerrain != null;
        }
    }
}
#endif
