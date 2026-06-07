using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Maps surface biomes to CC0 character slots and legendary combat scaling.</summary>
    public static class BiomeEnemyCombatCatalog
    {
        public const string PrefabFolder = "Assets/EnvironmentKit/CC0Imports/Prefabs/Characters";

        public static readonly IReadOnlyDictionary<WorldSurfaceBiomeId, string> SlotByBiome =
            new Dictionary<WorldSurfaceBiomeId, string>
            {
                [WorldSurfaceBiomeId.PlayKarst] = "BOSS_Add_2",
                [WorldSurfaceBiomeId.FoothillGreen] = "ENEMY_Foothill_1",
                [WorldSurfaceBiomeId.PeakStone] = "ENEMY_Peak_1",
                [WorldSurfaceBiomeId.HorizonMist] = "BOSS_Add_1",
                [WorldSurfaceBiomeId.AnnexLabyrinth] = "ENEMY_Annex_1",
                [WorldSurfaceBiomeId.BossThreshold] = "BOSS_Stage",
                [WorldSurfaceBiomeId.MixedTransition] = "ENEMY_Foothill_1",
                [WorldSurfaceBiomeId.ConceptPreset00] = "BOSS_Add_2",
                [WorldSurfaceBiomeId.ConceptPreset01] = "ENEMY_Foothill_1",
                [WorldSurfaceBiomeId.ConceptPreset02] = "ENEMY_Annex_1",
                [WorldSurfaceBiomeId.ConceptPreset03] = "ENEMY_Peak_1",
                [WorldSurfaceBiomeId.ConceptPreset04] = "BOSS_Add_1",
                [WorldSurfaceBiomeId.ConceptPreset05] = "ENEMY_Annex_1",
                [WorldSurfaceBiomeId.ConceptPreset06] = "ENEMY_Foothill_1",
                [WorldSurfaceBiomeId.ConceptPreset07] = "ENEMY_Annex_1",
                [WorldSurfaceBiomeId.ConceptPreset08] = "ENEMY_Foothill_1",
                [WorldSurfaceBiomeId.ConceptPreset09] = "ENEMY_Peak_1",
            };

        /// <summary>Cave route mobs — distinct from surface rings.</summary>
        public const string CaveEnemySlot = "BOSS_Add_1";

        public static string SlotFor(WorldSurfaceBiomeId biome) =>
            SlotByBiome.TryGetValue(biome, out var slot) ? slot : "ENEMY_Foothill_1";

        public static string PrefabPathForSlot(string slot) => $"{PrefabFolder}/{slot}.prefab";

        public static bool IsOutOfNativeBiome(
            WorldSurfaceBiomeId native,
            WorldSurfaceBiomeId atPosition) =>
            native != atPosition;
    }

    public static class BiomeEnemyCombatScaling
    {
        const float LegendaryHpMultiplier = 2.35f;
        const float LegendaryAttackMultiplier = 1.65f;
        const float LegendaryDefenseBonus = 6f;
        const float LegendarySpeedMultiplier = 1.18f;

        public static void Apply(BiomeEnemyIdentity identity)
        {
            if (identity == null || !identity.isLegendaryWanderer)
                return;

            var atBiome = WorldBiomeProbe.ResolveAt(identity.transform.position);
            if (!BiomeEnemyCombatCatalog.IsOutOfNativeBiome(identity.nativeBiome, atBiome))
                return;

            ScaleCombatStats(identity.gameObject, LegendaryHpMultiplier, LegendaryAttackMultiplier, LegendaryDefenseBonus);
            ScaleNavAgent(identity.gameObject, LegendarySpeedMultiplier);
            identity.gameObject.name = $"Legendary_{identity.nativeBiome}_{identity.gameObject.name}";
        }

        public static void ApplyLegendaryAtSpawn(
            GameObject instance,
            WorldSurfaceBiomeId native,
            Vector3 spawnPos,
            bool useSurfaceBiomeProbe = true,
            bool forceLegendary = false)
        {
            if (instance == null)
                return;

            var legendary = forceLegendary ||
                (useSurfaceBiomeProbe &&
                    BiomeEnemyCombatCatalog.IsOutOfNativeBiome(
                        native,
                        WorldBiomeProbe.ResolveAt(spawnPos)));
            var id = instance.GetComponent<BiomeEnemyIdentity>();
            if (id == null)
                id = instance.AddComponent<BiomeEnemyIdentity>();
            id.Configure(native, legendary);

            if (forceLegendary)
            {
                ScaleCombatStats(instance, LegendaryHpMultiplier, LegendaryAttackMultiplier, LegendaryDefenseBonus);
                ScaleNavAgent(instance, LegendarySpeedMultiplier);
            }
        }

        static void ScaleCombatStats(GameObject go, float hpMul, float atkMul, float defBonus)
        {
            var statsType = System.Type.GetType("CombatStats, Assembly-CSharp");
            if (statsType == null)
                return;

            var stats = go.GetComponent(statsType);
            if (stats == null)
                return;

            var maxHpField = statsType.GetField("maxHp");
            var curHpField = statsType.GetField("currentHp");
            var atkField = statsType.GetField("attackPower");
            var defField = statsType.GetField("defense");
            if (maxHpField == null)
                return;

            var maxHp = (int)maxHpField.GetValue(stats);
            var newMax = Mathf.RoundToInt(maxHp * hpMul);
            maxHpField.SetValue(stats, newMax);
            curHpField?.SetValue(stats, newMax);
            if (atkField != null)
            {
                var atk = (int)atkField.GetValue(stats);
                atkField.SetValue(stats, Mathf.RoundToInt(atk * atkMul));
            }

            if (defField != null)
            {
                var def = (int)defField.GetValue(stats);
                defField.SetValue(stats, def + Mathf.RoundToInt(defBonus));
            }
        }

        static void ScaleNavAgent(GameObject go, float speedMul)
        {
            var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent == null)
                return;
            agent.speed *= speedMul;
        }
    }
}
