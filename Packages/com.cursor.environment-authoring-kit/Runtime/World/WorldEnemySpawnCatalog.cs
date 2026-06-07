using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// World-wide surface enemy roster — biome scatter tables only.
    /// Never used by <see cref="HollowTitanLandmarkSpawnCatalog"/> (legendary landmark slots).
    /// </summary>
    public static class WorldEnemySpawnCatalog
    {
        public const string ManifestScope = "WorldSurfaceScatter";

        /// <summary>All CC0 character slots referenced by world biome scatter.</summary>
        public static readonly string[] AllWorldEnemyCc0Slots =
        {
            "ENEMY_Foothill_1",
            "ENEMY_Peak_1",
            "ENEMY_Annex_1",
            "BOSS_Add_1",
            "BOSS_Add_2",
            "BOSS_Stage",
        };

        public static string Cc0SlotForBiome(WorldSurfaceBiomeId biome) =>
            BiomeEnemyCombatCatalog.SlotFor(biome);

        public static GameObject LoadEnemyForBiome(WorldSurfaceBiomeId biome) =>
            Cc0RuntimePrefabResolver.LoadCharacter(Cc0SlotForBiome(biome));

        public static GameObject LoadEnemy(string cc0Slot) =>
            Cc0RuntimePrefabResolver.LoadCharacter(cc0Slot);

        public static IEnumerable<string> EnumerateUniqueCc0Slots()
        {
            var seen = new HashSet<string>();
            foreach (var slot in AllWorldEnemyCc0Slots)
            {
                if (seen.Add(slot))
                    yield return slot;
            }

            foreach (var kv in BiomeEnemyCombatCatalog.SlotByBiome)
            {
                if (!string.IsNullOrEmpty(kv.Value) && seen.Add(kv.Value))
                    yield return kv.Value;
            }

            if (seen.Add(BiomeEnemyCombatCatalog.CaveEnemySlot))
                yield return BiomeEnemyCombatCatalog.CaveEnemySlot;
        }
    }
}
