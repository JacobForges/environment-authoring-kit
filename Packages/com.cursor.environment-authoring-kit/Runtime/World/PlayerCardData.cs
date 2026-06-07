using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    [Serializable]
    public sealed class CurrencyWalletData
    {
        public long copper;
        public long silver;
        public long gold;
        public long platinum;
    }

    [Serializable]
    public sealed class SkillRankData
    {
        public string skillId = string.Empty;
        public int rank;
        public int xp;
    }

    [Serializable]
    public sealed class ItemStackData
    {
        public string definitionId = string.Empty;
        public int quantity = 1;
        public string instanceJson = string.Empty;
    }

    [Serializable]
    public sealed class EquippedSlotData
    {
        public string slotId = string.Empty;
        public string instanceId = string.Empty;
    }

    /// <summary>Serialized player progress — inventory, skills, currency, flags.</summary>
    [Serializable]
    public sealed class PlayerCardData
    {
        public string playerId = "default";
        public string displayName = "Traveler";
        public int level = 1;
        public int xp;
        public float health = 100f;
        public float stamina = 100f;
        public int buildSeedSeen;
        public CurrencyWalletData currency = new();
        public List<SkillRankData> skills = new();
        public List<ItemStackData> inventory = new();
        public List<EquippedSlotData> equipment = new();
        public bool hollowTreeEntered;
        public bool bossDefeated;
        public List<string> biomesDiscovered = new();
    }
}
