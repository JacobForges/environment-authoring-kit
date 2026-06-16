using System;
using System.Collections;
using System.Reflection;
using EnvironmentAuthoringKit.World;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Spawns cave enemies at play time with per-enemy skill + stat rolls via ICaveSpawnedEnemy / NpcEnemy.</summary>
    public class CaveMobSpawner : MonoBehaviour
    {
        static bool _loggedFallbackOnce;
        static int _globalFallbackSpawnCount;
        static Type _npcEnemyType;

        public GameObject enemyPrefab;
        public CaveMobAggression mobAggression = CaveMobAggression.Aggressive;
        public int spawnCount = 1;
        public float radius = 8f;
        public bool spawnOnStart = true;
        public int maxGlobalFallbackCapsules = 12;
        public int spawnSeed = 0;

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
                nameof(CaveMobSpawner),
                "NavMesh not ready — mob spawns skipped.");
        }

        public void SpawnAll()
        {
            if (spawnSeed == 0)
                spawnSeed = UnityObjectCompat.ReferenceId(transform);

            var useFallbackCapsules = enemyPrefab == null;
            if (useFallbackCapsules && !_loggedFallbackOnce)
            {
                Debug.Log("[CaveMobSpawner] enemyPrefab not assigned. Spawning NpcEnemy capsules.", this);
                _loggedFallbackOnce = true;
            }

            for (var i = 0; i < spawnCount; i++)
            {
                var offset = UnityEngine.Random.insideUnitSphere * radius;
                offset.y = Mathf.Abs(offset.y) * 0.35f;
                var pos = transform.position + offset;
                if (Physics.Raycast(pos + Vector3.up * 8f, Vector3.down, out var hit, 16f))
                    pos = hit.point + Vector3.up * 0.2f;

                if (!NavMeshSpawnGate.CanPlaceAgent(pos, radius + 4f))
                    continue;

                var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                var seed = spawnSeed + i * 997 + UnityObjectCompat.ReferenceId(this) + (int)mobAggression * 17;
                GameObject instance;
                if (useFallbackCapsules)
                {
                    if (_globalFallbackSpawnCount >= maxGlobalFallbackCapsules)
                        break;
                    instance = HumanoidCombatSpawner.SpawnEnemy(
                        pos, rot, transform, null, seed, mobAggression,
                        new Color(0.68f, 0.22f, 0.22f, 1f));
                    _globalFallbackSpawnCount++;
                }
                else
                {
                    instance = HumanoidCombatSpawner.SpawnEnemy(
                        pos, rot, transform, enemyPrefab, seed, mobAggression);
                }

                if (instance != null)
                {
                    var id = instance.GetComponent<BiomeEnemyIdentity>() ??
                        instance.AddComponent<BiomeEnemyIdentity>();
                    id.Configure(WorldSurfaceBiomeId.PlayKarst, legendary: false);

                    ConfigureSpawnedEnemy(instance, seed);
                    ApplyAggression(instance);
                    EnsureLootDropper(instance, seed);
                }
            }
        }

        static void EnsureLootDropper(GameObject instance, int seed)
        {
            if (instance == null)
                return;

            var dropper = instance.GetComponent<WorldEnemyLootDropper>();
            if (dropper == null)
                dropper = instance.AddComponent<WorldEnemyLootDropper>();
            dropper.biome = WorldSurfaceBiomeId.PlayKarst;
            dropper.lootSeed = UnityObjectCompat.ReferenceId(instance) ^ seed;
            dropper.dropChance = 0.45f + (seed % 23) * 0.01f;
        }

        void ConfigureSpawnedEnemy(GameObject instance, int seed)
        {
            if (instance == null)
                return;

            var configurer = instance.GetComponent<ICaveSpawnedEnemy>();
            if (configurer != null)
            {
                configurer.Configure(seed);
                return;
            }

            TryConfigureNpcEnemyByReflection(instance, seed);
        }

        void ApplyAggression(GameObject instance)
        {
            ResolveGameTypes();
            if (_npcEnemyType == null)
                return;

            var comp = instance.GetComponent(_npcEnemyType);
            if (comp == null)
                return;

            var aggressionField = _npcEnemyType.GetField("aggression");
            if (aggressionField != null && aggressionField.FieldType.IsEnum)
                aggressionField.SetValue(comp, mobAggression);

            var setMethod = _npcEnemyType.GetMethod("SetAggression", BindingFlags.Instance | BindingFlags.Public);
            setMethod?.Invoke(comp, new object[] { mobAggression });
        }

        static void TryConfigureNpcEnemyByReflection(GameObject instance, int seed)
        {
            ResolveGameTypes();
            if (_npcEnemyType == null)
                return;

            var comp = instance.GetComponent(_npcEnemyType);
            if (comp == null)
                comp = instance.AddComponent(_npcEnemyType);

            var method = _npcEnemyType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public);
            method?.Invoke(comp, new object[] { seed });
        }

        static void ResolveGameTypes()
        {
            _npcEnemyType ??= Type.GetType("NpcEnemy, Assembly-CSharp");
        }
    }
}
