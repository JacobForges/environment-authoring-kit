using System;
using System.Collections.Generic;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Tool → cabochon → cut gem progression per family (G-T, G-C, G-G).</summary>
    public static class WorldGemCrafting
    {
        static readonly Dictionary<string, string[]> FamilyChains = new(StringComparer.Ordinal)
        {
            ["G-T"] = new[] { "G-T01", "G-T02", "G-T03", "G-T04", "G-T05", "G-T06" },
            ["G-C"] = new[] { "G-C01", "G-C02", "G-C03", "G-C04", "G-C05", "G-C06" },
            ["G-G"] = new[] { "G-G01", "G-G02", "G-G03", "G-G04", "G-G05", "G-G06" },
        };

        public static bool TryCraftNext(PlayerCardData card, string familyPrefix, out string message)
        {
            message = string.Empty;
            if (card == null || string.IsNullOrWhiteSpace(familyPrefix))
                return false;

            if (!FamilyChains.TryGetValue(familyPrefix.Trim(), out var chain) || chain.Length < 2)
            {
                message = $"Unknown gem family '{familyPrefix}'.";
                return false;
            }

            card.inventory ??= new List<ItemStackData>();

            for (var i = 0; i < chain.Length - 1; i++)
            {
                var sourceId = chain[i];
                var nextId = chain[i + 1];
                if (!TryConsumeOne(card, sourceId))
                    continue;

                AddStack(card, nextId, 1);
                WorldItemCatalog.TryGet(nextId, out var def);
                message = $"Crafted {def?.displayName ?? nextId} from {sourceId}.";
                return true;
            }

            message = $"No {familyPrefix} tool gem in inventory to craft.";
            return false;
        }

        public static bool TryCraftAllFamilies(PlayerCardData card, out string summary)
        {
            var crafted = 0;
            summary = string.Empty;
            foreach (var family in FamilyChains.Keys)
            {
                while (TryCraftNext(card, family, out _))
                    crafted++;
            }

            summary = crafted > 0 ? $"Crafted {crafted} gem step(s)." : "Nothing to craft.";
            return crafted > 0;
        }

        static bool TryConsumeOne(PlayerCardData card, string definitionId)
        {
            for (var i = 0; i < card.inventory.Count; i++)
            {
                var stack = card.inventory[i];
                if (stack == null || stack.definitionId != definitionId || stack.quantity < 1)
                    continue;

                stack.quantity--;
                if (stack.quantity <= 0)
                    card.inventory.RemoveAt(i);
                return true;
            }

            return false;
        }

        static void AddStack(PlayerCardData card, string definitionId, int qty)
        {
            foreach (var stack in card.inventory)
            {
                if (stack == null || stack.definitionId != definitionId)
                    continue;
                stack.quantity += qty;
                return;
            }

            card.inventory.Add(new ItemStackData { definitionId = definitionId, quantity = qty });
        }
    }
}
