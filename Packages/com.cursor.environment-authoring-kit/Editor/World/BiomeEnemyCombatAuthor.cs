#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Loads CC0 combat prefabs per biome for surface and cave pipelines.</summary>
    public static class BiomeEnemyCombatAuthor
    {
        static readonly Dictionary<WorldSurfaceBiomeId, GameObject> _cache = new();

        public static void EnsureImported()
        {
            Cc0ContentImportUtility.EnsureAll(importItems: false, reimportFbx: false);
        }

        public static GameObject PrefabForBiome(WorldSurfaceBiomeId biome)
        {
            if (_cache.TryGetValue(biome, out var cached) && cached != null)
                return cached;

            EnsureImported();
            var slot = WorldEnemySpawnCatalog.Cc0SlotForBiome(biome);
            var prefab = Cc0ContentImportUtility.LoadCharacterPrefab(slot);
            if (prefab == null)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BiomeEnemyCombatCatalog.PrefabPathForSlot(slot));

            if (prefab != null)
                _cache[biome] = prefab;
            return prefab;
        }

        public static GameObject PrefabForCave()
        {
            EnsureImported();
            var slot = BiomeEnemyCombatCatalog.CaveEnemySlot;
            return Cc0ContentImportUtility.LoadCharacterPrefab(slot)
                ?? AssetDatabase.LoadAssetAtPath<GameObject>(BiomeEnemyCombatCatalog.PrefabPathForSlot(slot));
        }

        public static GameObject DefaultSurfacePrefab() =>
            PrefabForBiome(WorldSurfaceBiomeId.FoothillGreen);

        public static void ClearCache() => _cache.Clear();
    }
}
#endif
