#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class SurfaceTerrainEnemySpawnerPlacement
    {
        public const string SpawnerObjectName = "SurfaceTerrainEnemies";

        static readonly WorldSurfaceBiomeId[] SurfaceBiomes =
        {
            WorldSurfaceBiomeId.PlayKarst,
            WorldSurfaceBiomeId.MixedTransition,
            WorldSurfaceBiomeId.ConceptPreset00,
            WorldSurfaceBiomeId.ConceptPreset01,
            WorldSurfaceBiomeId.ConceptPreset02,
            WorldSurfaceBiomeId.ConceptPreset03,
            WorldSurfaceBiomeId.ConceptPreset04,
            WorldSurfaceBiomeId.ConceptPreset05,
            WorldSurfaceBiomeId.ConceptPreset06,
            WorldSurfaceBiomeId.ConceptPreset07,
            WorldSurfaceBiomeId.ConceptPreset08,
            WorldSurfaceBiomeId.ConceptPreset09,
            WorldSurfaceBiomeId.AnnexLabyrinth,
            WorldSurfaceBiomeId.BossThreshold,
        };

        public static int EnsureOnSurface(Transform surfaceRoot, WorldGenerationRequest request, GameObject _)
        {
            if (surfaceRoot == null)
                return 0;

            BiomeEnemyCombatAuthor.EnsureImported();
            WorldRuntimeResourcesAuthor.EnsureAllEnemyPrefabsInResources();

            var existing = surfaceRoot.Find(SpawnerObjectName);
            GameObject host;
            if (existing != null)
                host = existing.gameObject;
            else
            {
                host = new GameObject(SpawnerObjectName);
                host.transform.SetParent(surfaceRoot, false);
                Undo.RegisterCreatedObjectUndo(host, "Surface enemy spawner");
            }

            var spawner = host.GetComponent<SurfaceTerrainEnemySpawner>();
            if (spawner == null)
                spawner = host.AddComponent<SurfaceTerrainEnemySpawner>();

            spawner.enemyPrefab = null;
            spawner.spawnSeed = request != null ? request.Seed + 8803 : UnityObjectCompat.ReferenceId(host);
            spawner.spawnCount = 0;

            if (request != null)
            {
                spawner.maxRadiusFromCenter = request.SurfaceExtentMeters > 10f
                    ? request.SurfaceExtentMeters * 0.92f
                    : spawner.maxRadiusFromCenter;
            }

            spawner.biomeGroups = BuildBiomeGroups(request);
            spawner.spawnOnStart = true;
            EditorUtility.SetDirty(host);
            return TotalCount(spawner.biomeGroups);
        }

        static BiomeEnemySpawnGroup[] BuildBiomeGroups(WorldGenerationRequest request)
        {
            var seed = request != null ? request.Seed : 0;
            var rng = new System.Random(seed + 8803);
            var groups = new List<BiomeEnemySpawnGroup>();

            foreach (var biome in SurfaceBiomes)
            {
                var prefab = BiomeEnemyCombatAuthor.PrefabForBiome(biome);
                if (prefab == null)
                    continue;

                var count = biome switch
                {
                    WorldSurfaceBiomeId.PlayKarst => 4 + rng.Next(3),
                    WorldSurfaceBiomeId.MixedTransition => 8 + rng.Next(6),
                    WorldSurfaceBiomeId.AnnexLabyrinth => 3 + rng.Next(2),
                    WorldSurfaceBiomeId.BossThreshold => 2 + rng.Next(2),
                    >= WorldSurfaceBiomeId.ConceptPreset00 and <= WorldSurfaceBiomeId.ConceptPreset09
                        => 3 + rng.Next(3),
                    WorldSurfaceBiomeId.FoothillGreen => 5 + rng.Next(4),
                    WorldSurfaceBiomeId.PeakStone => 4 + rng.Next(3),
                    WorldSurfaceBiomeId.HorizonMist => 3 + rng.Next(3),
                    _ => 2,
                };

                groups.Add(new BiomeEnemySpawnGroup
                {
                    biome = biome,
                    enemyPrefab = prefab,
                    count = count,
                });
            }

            return groups.ToArray();
        }

        static int TotalCount(BiomeEnemySpawnGroup[] groups)
        {
            if (groups == null)
                return 0;
            var n = 0;
            foreach (var g in groups)
                n += g.count;
            return n;
        }
    }
}
#endif
