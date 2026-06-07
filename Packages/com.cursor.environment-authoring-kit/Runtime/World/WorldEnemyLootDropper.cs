using System.Reflection;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Spawns manifest loot pickups when an enemy dies.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldEnemyLootDropper : MonoBehaviour
    {
        public WorldSurfaceBiomeId biome = WorldSurfaceBiomeId.PlayKarst;
        public int lootSeed;

        WorldSimpleHealth _simple;
        Component _combatStats;
        FieldInfo _hpField;
        bool _dropped;

        void Awake()
        {
            if (lootSeed == 0)
                lootSeed = UnityObjectCompat.ReferenceId(this);

            ResolveCombatStats();
            _simple = GetComponent<WorldSimpleHealth>();
            if (_combatStats == null && _simple == null)
            {
                _simple = gameObject.AddComponent<WorldSimpleHealth>();
                _simple.lootBiome = biome;
                _simple.lootSeed = lootSeed;
            }

            if (_simple != null)
                _simple.Died += OnSimpleDied;
        }

        void OnDestroy()
        {
            if (_simple != null)
                _simple.Died -= OnSimpleDied;
        }

        void Update()
        {
            if (_dropped || _combatStats == null || _hpField == null)
                return;

            var hp = (int)_hpField.GetValue(_combatStats);
            if (hp > 0)
                return;

            DropLoot();
        }

        void OnSimpleDied(WorldSimpleHealth _) => DropLoot();

        void ResolveCombatStats()
        {
            var type = System.Type.GetType("CombatStats, Assembly-CSharp");
            if (type == null)
                return;

            _combatStats = GetComponent(type);
            _hpField = type.GetField("currentHp", BindingFlags.Instance | BindingFlags.Public);
        }

        void DropLoot()
        {
            if (_dropped)
                return;
            _dropped = true;

            var rng = new System.Random(lootSeed ^ 0x4C4F4F54);
            if (!WorldEnemyLootTables.TryRoll(biome, rng, out var defId, out var qty))
                return;

            var pos = transform.position + Vector3.up * 0.5f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Loot_{defId}";
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.45f;
            var col = go.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;

            var pickup = go.AddComponent<WorldItemPickup>();
            pickup.Configure(defId, defId, biome, qty);
        }
    }
}
