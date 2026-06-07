using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    public enum GemGrade
    {
        Tool = 0,
        Cabochon = 1,
        CutGem = 2,
    }

    public enum CurrencyType
    {
        Copper = 0,
        Silver = 1,
        Gold = 2,
        Platinum = 3,
    }

    public enum ItemCategory
    {
        Weapon,
        Armor,
        Consumable,
        Currency,
        Gem,
        Key,
        Material,
        Landmark,
    }

    /// <summary>World pickup — references catalog id; unique behavior via traitId.</summary>
    public sealed class WorldItemPickup : MonoBehaviour
    {
        [SerializeField] string definitionId = string.Empty;
        [SerializeField] string traitId = string.Empty;
        [SerializeField] WorldSurfaceBiomeId biome;
        [SerializeField] int quantity = 1;

        public string DefinitionId => definitionId;
        public string TraitId => traitId;
        public WorldSurfaceBiomeId Biome => biome;
        public int Quantity => quantity;

        public void Configure(string defId, string trait, WorldSurfaceBiomeId biomeId, int qty = 1)
        {
            definitionId = defId ?? string.Empty;
            traitId = trait ?? string.Empty;
            biome = biomeId;
            quantity = Mathf.Max(1, qty);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsPlayerCollider(other))
                return;

            var persistence = other.GetComponentInParent<PlayerPersistence>();
            if (persistence == null)
                return;

            if (WorldItemInventory.TryCollect(persistence, persistence.Card, this, out var msg))
            {
                Debug.Log($"[WorldItemPickup] {msg}", this);
                persistence.Save();
                WorldEconomyHud.NotifyCollected(msg);
            }
        }

        static bool IsPlayerCollider(Collider other)
        {
            if (other.CompareTag("Player"))
                return true;

            var controller = other.GetComponentInParent<CharacterController>();
            if (controller == null)
                return false;

            return controller.GetComponentInParent<PlayerPersistence>() != null ||
                   controller.CompareTag("Player");
        }
    }

    public static class WorldItemInventory
    {
        public static bool TryCollect(
            PlayerPersistence persistence,
            PlayerCardData card,
            WorldItemPickup pickup,
            out string message)
        {
            message = string.Empty;
            if (card == null || pickup == null)
                return false;

            card.inventory ??= new System.Collections.Generic.List<ItemStackData>();
            var def = WorldItemCatalog.GetOrDefault(pickup.DefinitionId);

            if (def.isCurrency || pickup.DefinitionId.StartsWith("C0", StringComparison.Ordinal))
            {
                AddCurrencyFromDefinition(card, pickup.DefinitionId, pickup.Quantity, def);
                message = $"Collected {def.displayName} x{pickup.Quantity}";
                UnityEngine.Object.Destroy(pickup.gameObject);
                return true;
            }

            AddOrStack(card, pickup.DefinitionId, pickup.Quantity, def.stackMax);
            message = $"Collected {def.displayName} x{pickup.Quantity}";

            if (pickup.DefinitionId == PlayerGrapplingHookController.ItemDefinitionId ||
                pickup.TraitId == "grappling_hook")
            {
                EquipGrapplingHook(persistence);
            }

            UnityEngine.Object.Destroy(pickup.gameObject);
            return true;
        }

        static void EquipGrapplingHook(PlayerPersistence persistence)
        {
            if (persistence == null)
                return;

            var controller = persistence.GetComponent<PlayerGrapplingHookController>();
            if (controller == null)
                controller = persistence.gameObject.AddComponent<PlayerGrapplingHookController>();
            controller.SetEquipped(true);
        }

        static void AddCurrencyFromDefinition(
            PlayerCardData card,
            string defId,
            int qty,
            WorldItemDefinitionEntry def)
        {
            var tier = defId switch
            {
                "C01" => CurrencyType.Copper,
                "C02" => CurrencyType.Silver,
                "C03" => CurrencyType.Gold,
                "C04" => CurrencyType.Platinum,
                _ => CurrencyType.Copper,
            };

            var perUnit = def.buyPriceCopper > 0 ? def.buyPriceCopper : 1;
            if (tier == CurrencyType.Copper && perUnit < 10)
                WorldCurrencyService.AddCopper(card, perUnit * qty);
            else
                WorldCurrencyService.AddTier(card, tier, qty);

            WorldCurrencyService.Normalize(card);
        }

        static void AddOrStack(PlayerCardData card, string defId, int qty, int stackMax)
        {
            foreach (var stack in card.inventory)
            {
                if (stack == null || stack.definitionId != defId)
                    continue;
                stack.quantity = Mathf.Min(stackMax, stack.quantity + qty);
                return;
            }

            card.inventory.Add(new ItemStackData
            {
                definitionId = defId,
                quantity = Mathf.Min(stackMax, qty),
            });
        }
    }
}
