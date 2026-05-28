using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Spawns combat-ready enemies at random NavMesh points on surface terrain.</summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceTerrainEnemySpawner : MonoBehaviour
    {
        [Header("Prefab (optional — capsule default matches Player size)")]
        public GameObject enemyPrefab;

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
                SpawnAll();
        }

        public int SpawnAll()
        {
            ClearSpawned();
            if (spawnCount <= 0)
                return 0;

            var center = ResolveCenter();
            var rng = new System.Random(spawnSeed != 0 ? spawnSeed : UnityObjectCompat.ReferenceId(this));
            var placed = 0;
            var attempts = spawnCount * 12;

            for (var i = 0; i < attempts && placed < spawnCount; i++)
            {
                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var dist = Mathf.Lerp(minRadiusFromCenter, maxRadiusFromCenter, (float)rng.NextDouble());
                var guess = center + new Vector3(Mathf.Cos(angle) * dist, sampleHeight * 0.5f, Mathf.Sin(angle) * dist);

                if (!NavMesh.SamplePosition(guess, out var hit, sampleHeight, NavMesh.AllAreas))
                    continue;

                if (Vector3.Distance(new Vector3(hit.position.x, 0f, hit.position.z),
                        new Vector3(center.x, 0f, center.z)) < minRadiusFromCenter * 0.85f)
                    continue;

                var rot = Quaternion.Euler(0f, rng.Next(0, 360), 0f);
                var seed = spawnSeed + placed * 991 + i;
                var aggression = PickAggression(rng, placed);
                var enemy = HumanoidCombatSpawner.SpawnEnemy(
                    hit.position,
                    rot,
                    transform,
                    enemyPrefab,
                    seed,
                    aggression);
                if (enemy == null)
                    continue;

                enemy.name = $"SurfaceEnemy_{placed:D2}";
                _spawned.Add(enemy);
                placed++;
            }

            if (placed < spawnCount)
                Debug.LogWarning(
                    $"[SurfaceTerrainEnemySpawner] Placed {placed}/{spawnCount} — expand NavMesh or adjust radii.",
                    this);

            return placed;
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
