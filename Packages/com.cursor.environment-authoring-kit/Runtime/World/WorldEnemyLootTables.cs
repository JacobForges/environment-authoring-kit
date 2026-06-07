using System;
using System.Collections.Generic;

namespace EnvironmentAuthoringKit.World
{
    public readonly struct LootRoll
    {
        public readonly string DefinitionId;
        public readonly int MinQty;
        public readonly int MaxQty;
        public readonly float Weight;

        public LootRoll(string definitionId, int minQty, int maxQty, float weight = 1f)
        {
            DefinitionId = definitionId;
            MinQty = minQty;
            MaxQty = maxQty;
            Weight = weight;
        }
    }

    /// <summary>Manifest-aligned drop tables by biome / enemy role.</summary>
    public static class WorldEnemyLootTables
    {
        static readonly Dictionary<WorldSurfaceBiomeId, LootRoll[]> ByBiome = new()
        {
            [WorldSurfaceBiomeId.PlayKarst] = new[]
            {
                new LootRoll("C01", 1, 3, 2f),
                new LootRoll("P01", 1, 1, 1f),
                new LootRoll("G-T01", 1, 1, 0.4f),
            },
            [WorldSurfaceBiomeId.FoothillGreen] = new[]
            {
                new LootRoll("C02", 1, 2, 2f),
                new LootRoll("G-T02", 1, 1, 0.8f),
                new LootRoll("G-C01", 1, 1, 0.5f),
                new LootRoll("W02", 1, 1, 0.3f),
            },
            [WorldSurfaceBiomeId.PeakStone] = new[]
            {
                new LootRoll("C03", 1, 2, 2f),
                new LootRoll("G-G01", 1, 1, 0.7f),
                new LootRoll("G-G02", 1, 1, 0.4f),
                new LootRoll("M01", 1, 2, 1f),
            },
            [WorldSurfaceBiomeId.HorizonMist] = new[]
            {
                new LootRoll("C02", 1, 3, 1.5f),
                new LootRoll("G-C05", 1, 1, 1f),
                new LootRoll("G-G03", 1, 1, 0.35f),
            },
            [WorldSurfaceBiomeId.AnnexLabyrinth] = new[]
            {
                new LootRoll("K01", 1, 1, 1.5f),
                new LootRoll("K02", 1, 1, 0.8f),
                new LootRoll("G-C03", 1, 1, 0.6f),
            },
            [WorldSurfaceBiomeId.BossThreshold] = new[]
            {
                new LootRoll("C04", 1, 2, 2f),
                new LootRoll("G-G06", 1, 1, 1f),
                new LootRoll("G-T06", 1, 1, 0.8f),
            },
        };

        public static bool TryRoll(WorldSurfaceBiomeId biome, System.Random rng, out string definitionId, out int quantity)
        {
            definitionId = string.Empty;
            quantity = 1;
            if (!ByBiome.TryGetValue(biome, out var table) || table == null || table.Length == 0)
                return false;

            var total = 0f;
            foreach (var roll in table)
                total += roll.Weight;

            var pick = (float)rng.NextDouble() * total;
            foreach (var roll in table)
            {
                pick -= roll.Weight;
                if (pick > 0f)
                    continue;

                definitionId = roll.DefinitionId;
                quantity = rng.Next(roll.MinQty, roll.MaxQty + 1);
                return true;
            }

            var last = table[table.Length - 1];
            definitionId = last.DefinitionId;
            quantity = rng.Next(last.MinQty, last.MaxQty + 1);
            return true;
        }
    }
}
