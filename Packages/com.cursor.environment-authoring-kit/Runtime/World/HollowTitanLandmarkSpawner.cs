using System.Collections.Generic;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Spawns enemies and loot only from child <see cref="HollowTitanLandmarkSpawnPoint"/> markers.
    /// Ignores global world spawn tables.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HollowTitanLandmarkSpawner : MonoBehaviour
    {
        [Header("Landmark-only roster")]
        public GameObject enemyPrefab;
        public bool spawnOnStart = true;
        public int spawnSeed;

        readonly List<GameObject> _spawned = new();

        void Start()
        {
            if (spawnOnStart)
                SpawnAll();
        }

        public int SpawnAll()
        {
            ClearSpawned();
            var markers = GetComponentsInChildren<HollowTitanLandmarkSpawnPoint>(true);
            if (markers == null || markers.Length == 0)
                return 0;

            var rng = new System.Random(spawnSeed != 0 ? spawnSeed : UnityObjectCompat.ReferenceId(this));
            var placed = 0;
            var topFloor = ResolveTopFloorIndex(markers);

            foreach (var marker in markers)
            {
                if (marker == null)
                    continue;

                switch (marker.Kind)
                {
                    case HollowTitanSpawnKind.Enemy:
                        if (TrySpawnEnemy(marker, rng, topFloor, out var enemy))
                        {
                            _spawned.Add(enemy);
                            placed++;
                        }
                        break;

                    case HollowTitanSpawnKind.Loot:
                        if (TrySpawnLoot(marker, out var loot))
                        {
                            _spawned.Add(loot);
                            placed++;
                        }
                        break;
                }
            }

            placed += SpawnPatrolAgents(markers, rng);

            return placed;
        }

        static int ResolveTopFloorIndex(HollowTitanLandmarkSpawnPoint[] markers)
        {
            var top = 0;
            foreach (var marker in markers)
            {
                if (marker != null && marker.FloorIndex > top)
                    top = marker.FloorIndex;
            }

            return top;
        }

        int SpawnPatrolAgents(HollowTitanLandmarkSpawnPoint[] markers, System.Random rng)
        {
            var byFloor = new Dictionary<int, List<Vector3>>();
            foreach (var marker in markers)
            {
                if (marker == null || marker.Kind != HollowTitanSpawnKind.Patrol)
                    continue;

                if (!byFloor.TryGetValue(marker.FloorIndex, out var list))
                {
                    list = new List<Vector3>();
                    byFloor[marker.FloorIndex] = list;
                }

                list.Add(marker.transform.position);
            }

            var placed = 0;
            foreach (var kv in byFloor)
            {
                if (kv.Value.Count < 2)
                    continue;

                var start = kv.Value[0];
                if (!NavMesh.SamplePosition(start, out var hit, 4f, NavMesh.AllAreas))
                    hit.position = start;

                var prefab = enemyPrefab
                    ?? HollowTitanLandmarkSpawnCatalog.LoadEnemyPrefab(
                        HollowTitanLandmarkSpawnCatalog.DefaultEnemyRoleId);
                var patrol = HumanoidCombatSpawner.SpawnEnemy(
                    hit.position,
                    Quaternion.identity,
                    transform,
                    prefab,
                    rng.Next(),
                    CaveMobAggression.Passive,
                    new Color(0.28f, 0.32f, 0.38f));
                if (patrol == null)
                    continue;

                patrol.name = $"HollowTitan_Patrol_F{kv.Key}";
                var agent = patrol.GetComponent<HollowTitanPatrolAgent>();
                if (agent == null)
                    agent = patrol.AddComponent<HollowTitanPatrolAgent>();
                agent.Configure(kv.Value);
                _spawned.Add(patrol);
                placed++;
            }

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

        bool TrySpawnEnemy(
            HollowTitanLandmarkSpawnPoint marker,
            System.Random rng,
            int topFloorIndex,
            out GameObject enemy)
        {
            enemy = null;
            var pos = marker.transform.position;
            if (!NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas))
                hit.position = pos;

            var roleId = string.IsNullOrEmpty(marker.DefinitionId)
                ? HollowTitanLandmarkSpawnCatalog.RoleForFloor(marker.FloorIndex, topFloorIndex)
                : marker.DefinitionId;
            var isBoss = roleId == HollowTitanLandmarkSpawnCatalog.BossRoleId;

            var prefab = ResolveEnemyPrefab(marker, roleId);
            enemy = HumanoidCombatSpawner.SpawnEnemy(
                hit.position,
                marker.transform.rotation,
                transform,
                prefab,
                rng.Next(),
                CaveMobAggression.Aggressive,
                new Color(0.35f, 0.22f, 0.12f));
            if (enemy == null)
                return false;

            enemy.name = $"HollowTitan_{roleId}_F{marker.FloorIndex}";
            BiomeEnemyCombatScaling.ApplyLegendaryAtSpawn(
                enemy,
                HollowTitanLandmarkSpawnCatalog.LandmarkBiome,
                hit.position,
                useSurfaceBiomeProbe: false,
                forceLegendary: true);

            if (isBoss)
                ConfigureTopBoss(enemy);
            else
                AttachFloorGuard(enemy, hit.position, marker.FloorIndex);

            return true;
        }

        static void ConfigureTopBoss(GameObject enemy)
        {
            enemy.transform.localScale = Vector3.one * HollowTitanLandmarkSpawnCatalog.BossScaleMultiplier;
            ScaleBossCombatStats(enemy, HollowTitanLandmarkSpawnCatalog.BossHpMultiplier);

            var guard = enemy.GetComponent<HollowTitanFloorGuardBehavior>();
            if (guard != null)
                Destroy(guard);
        }

        static void ScaleBossCombatStats(GameObject go, float hpMul)
        {
            var statsType = System.Type.GetType("CombatStats, Assembly-CSharp");
            if (statsType == null)
                return;

            var stats = go.GetComponent(statsType);
            if (stats == null)
                return;

            var maxHpField = statsType.GetField("maxHp");
            var curHpField = statsType.GetField("currentHp");
            if (maxHpField == null)
                return;

            var maxHp = (int)maxHpField.GetValue(stats);
            var newMax = Mathf.RoundToInt(maxHp * hpMul);
            maxHpField.SetValue(stats, newMax);
            curHpField?.SetValue(stats, newMax);
        }

        static void AttachFloorGuard(GameObject enemy, Vector3 home, int floorIndex)
        {
            var guard = enemy.GetComponent<HollowTitanFloorGuardBehavior>();
            if (guard == null)
                guard = enemy.AddComponent<HollowTitanFloorGuardBehavior>();

            var patrolRadius = 4.5f + floorIndex * 0.35f;
            guard.Configure(home, patrolRadius);
        }

        GameObject ResolveEnemyPrefab(HollowTitanLandmarkSpawnPoint marker, string roleId)
        {
            var fromRole = HollowTitanLandmarkSpawnCatalog.LoadEnemyPrefab(roleId);
            if (fromRole != null)
                return fromRole;

            return enemyPrefab;
        }

        bool TrySpawnLoot(HollowTitanLandmarkSpawnPoint marker, out GameObject loot)
        {
            loot = null;
            var defId = marker.DefinitionId;
            if (string.IsNullOrEmpty(defId))
                return false;

            var isHook = defId == HollowTitanLandmarkSpawnCatalog.GrapplingHookLootId;
            var itemPrefab = HollowTitanLandmarkSpawnCatalog.LoadLootPrefab(defId);
            if (itemPrefab != null)
            {
                loot = Instantiate(itemPrefab, marker.transform.position, marker.transform.rotation, transform);
                loot.name = $"HollowTitan_Loot_{defId}";
                loot.transform.localScale = Vector3.one * (isHook ? 0.65f : 0.5f);
                EnsureTriggerCollider(loot);
            }
            else
            {
                loot = GameObject.CreatePrimitive(isHook ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
                loot.name = $"HollowTitan_Loot_{defId}_Proxy";
                loot.transform.SetParent(transform, true);
                loot.transform.SetPositionAndRotation(
                    marker.transform.position,
                    marker.transform.rotation);
                loot.transform.localScale = isHook
                    ? new Vector3(0.25f, 0.55f, 0.25f)
                    : Vector3.one * 0.55f;

                var col = loot.GetComponent<Collider>();
                if (col != null)
                    col.isTrigger = true;
            }

            var pickup = loot.GetComponent<WorldItemPickup>();
            if (pickup == null)
                pickup = loot.AddComponent<WorldItemPickup>();
            pickup.Configure(
                defId,
                isHook ? "grappling_hook" : defId,
                HollowTitanLandmarkSpawnCatalog.LandmarkBiome);
            return true;
        }

        static void EnsureTriggerCollider(GameObject go)
        {
            var col = go.GetComponentInChildren<Collider>();
            if (col == null)
            {
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one * 0.8f;
                box.center = Vector3.up * 0.25f;
            }
            else
            {
                col.isTrigger = true;
            }
        }
    }
}
