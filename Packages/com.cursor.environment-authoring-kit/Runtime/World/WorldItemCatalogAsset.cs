using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    [Serializable]
    public sealed class WorldItemDefinitionEntry
    {
        public string id = string.Empty;
        public string displayName = string.Empty;
        public string archetype = string.Empty;
        public ItemCategory category = ItemCategory.Material;
        public GemGrade gemGrade = GemGrade.Tool;
        public string gemFamily = string.Empty;
        public int buyPriceCopper;
        public int sellPriceCopper;
        public int stackMax = 99;
        public bool isCurrency;
        public bool isLandmark;
    }

    [CreateAssetMenu(
        fileName = "WorldItemCatalog",
        menuName = "Environment Kit/World Item Catalog",
        order = 400)]
    public sealed class WorldItemCatalogAsset : ScriptableObject
    {
        public List<WorldItemDefinitionEntry> entries = new();
    }
}
