using System;
using System.Collections;
using System.Collections.Generic;
using EnvironmentAuthoringKit.World;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Cave
{
    [Serializable]
    public struct BiomeEnemySpawnGroup
    {
        public WorldSurfaceBiomeId biome;
        public GameObject enemyPrefab;
        public int count;
    }

    /// <summary>Spawns biome-native CC0 enemies on NavMesh; wrong-biome placement becomes legendary.</summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceTerrainEnemySpawner : MonoBehaviour
    {
        [Header("Legacy single prefab (ignored when biome groups set)")]
        public GameObject enemyPrefab;

        [Header("Per-biome roster")]
        public BiomeEnemySpawnGroup[] biomeGroups;

        [Header("Spawn")]
        public int spawnCount = 12;
        public float minRadiusFromCenter = 18f;
        public float maxRadiusFromCenter = 160f;
        public float sampleHeight = 40f;
        public bool spawnOnStart = true;
        public int spawnSeed;

        [Header("Behavior mix")]
        public CaveMobAggression defaultAggression = CaveMobAggression.Aggressive;

        readonly List<GameObject> _spawned = new();

        void Start()
        {
            if (spawnOnStart)
                StartCoroutine(DeferredSpawnAll());
        }

        IEnumerator DeferredSpawnAll()
        {
            const float pollInterval = 0.5f;
            const float maxWaitSeconds = 300f;
            var elapsed = 0f;
            while (elapsed < maxWaitSeconds)
            {
                if (NavMeshSpawnGate.HasNavMeshData())
                {
                    SpawnAll();
                    yield break;
                }

                elapsed += pollInterval;
                yield return new WaitForSeconds(pollInterval);
            }

            NavMeshSpawnGate.WarnOnce(
                nameof(SurfaceTerrainEnemySpawner),
                "NavMesh not ready — surface enemy spawns skipped.");
        }

        public int SpawnAll()
        {
            ClearSpawned();
            if (spawnCount <= 0 && (biomeGroups == null || biomeGroups.Length == 0))
                return 0;

            var groups = ResolveGroups();
            if (groups.Count == 0)
                return 0;

            var center = ResolveCenter();
            var rng = new System.Random(spawnSeed != 0 ? spawnSeed : UnityObjectCompat.ReferenceId(this));
            var placed = 0;

            foreach (var group in groups)
            {
                if (group.count <= 0 || group.enemyPrefab == null)
                    continue;

                var attempts = group.count * 24;
                for (var i = 0; i < attempts; i++)
                {
                    if (CountPlacedForBiome(group.biome) >= group.count)
                        break;

                    if (!TrySampleNavPoint(center, rng, out var hit))
                        continue;

                    var atBiome = WorldBiomeProbe.ResolveAt(hit.position);
                    if (atBiome != group.biome)
                        continue;

                    if (!TrySpawnOne(group, hit.position, rng, placed, i, out var enemy))
                        continue;

                    BiomeEnemyCombatScaling.ApplyLegendaryAtSpawn(enemy, group.biome, hit.position);
                    enemy.name = $"SurfaceEnemy_{group.biome}_{CountPlacedForBiome(group.biome):D2}";
                    _spawned.Add(enemy);
                    placed++;
                }
            }

            if (placed < TotalBudget(groups))
                Debug.LogWarning(
                    $"[SurfaceTerrainEnemySpawner] Placed {placed}/{TotalBudget(groups)} — expand NavMesh or check biome markers.",
                    this);

            return placed;
        }

        List<BiomeEnemySpawnGroup> ResolveGroups()
        {
            var list = new List<BiomeEnemySpawnGroup>();
            if (biomeGroups != null && biomeGroups.Length > 0)
            {
                foreach (var g in biomeGroups)
                {
                    if (g.count > 0 && (g.enemyPrefab != null || enemyPrefab != null))
                        list.Add(g);
                }

                if (list.Count > 0)
                    return list;
            }

            if (enemyPrefab != null)
            {
                list.Add(new BiomeEnemySpawnGroup
                {
                    biome = WorldSurfaceBiomeId.FoothillGreen,
                    enemyPrefab = enemyPrefab,
                    count = spawnCount,
                });
            }

            return list;
        }

        static int TotalBudget(List<BiomeEnemySpawnGroup> groups)
        {
            var n = 0;
            foreach (var g in groups)
                n += g.count;
            return n;
        }

        int CountPlacedForBiome(WorldSurfaceBiomeId biome)
        {
            var n = 0;
            foreach (var go in _spawned)
            {
                if (go == null)
                    continue;
                var id = go.GetComponent<BiomeEnemyIdentity>();
                if (id != null && id.nativeBiome == biome)
                    n++;
            }

            return n;
        }

        bool TrySampleNavPoint(Vector3 center, System.Random rng, out NavMeshHit hit)
        {
            hit = default;
            for (var t = 0; t < 8; t++)
            {
                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var dist = Mathf.Lerp(minRadiusFromCenter, maxRadiusFromCenter, (float)rng.NextDouble());
                var guess = center + new Vector3(Mathf.Cos(angle) * dist, sampleHeight * 0.5f, Mathf.Sin(angle) * dist);
                if (!NavMesh.SamplePosition(guess, out hit, sampleHeight, NavMesh.AllAreas))
                    continue;

                if (Vector3.Distance(new Vector3(hit.position.x, 0f, hit.position.z),
                        new Vector3(center.x, 0f, center.z)) < minRadiusFromCenter * 0.85f)
                    continue;

                return true;
            }

            return false;
        }

        bool TrySpawnOne(
            BiomeEnemySpawnGroup group,
            Vector3 position,
            System.Random rng,
            int placedIndex,
            int attemptIndex,
            out GameObject enemy)
        {
            var rot = Quaternion.Euler(0f, rng.Next(0, 360), 0f);
            var seed = spawnSeed + (int)group.biome * 503 + placedIndex * 991 + attemptIndex;
            var aggression = PickAggression(rng, placedIndex);
            var prefab = group.enemyPrefab != null ? group.enemyPrefab : enemyPrefab;
            if (prefab == null)
                prefab = WorldEnemySpawnCatalog.LoadEnemyForBiome(group.biome);
            enemy = HumanoidCombatSpawner.SpawnEnemy(
                position,
                rot,
                transform,
                prefab,
                seed,
                aggression);
            return enemy != null;
        }

        public void ClearSpawned()
        {
            for (var i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i]);
            }

            _spawned.Clear();
        }

        Vector3 ResolveCenter()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                return player.transform.position;

            return transform.position;
        }

        static CaveMobAggression PickAggression(System.Random rng, int index)
        {
            var roll = rng.NextDouble();
            if (roll < 0.12)
                return CaveMobAggression.Passive;
            if (roll < 0.32)
                return CaveMobAggression.Defensive;
            return CaveMobAggression.Aggressive;
        }
    }
}
