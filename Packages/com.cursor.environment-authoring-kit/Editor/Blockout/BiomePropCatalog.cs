#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Per-biome surface prop pools (trees, grass, bushes, rocks, ground cover) filtered from
    /// LPMagicalForest + CC0 scan. Distinct catalogs so biomes read differently on foot.
    /// </summary>
    public sealed class BiomePropCatalog
    {
        public const string LpMagicalForestRoot = "Assets/PolitePenguin/LPMagicalForest/Prefabs";

        readonly Dictionary<WorldSurfaceBiomeId, BiomePropSet> _sets = new();
        SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog _base;

        public bool HasAny => _sets.Values.Any(s => s.HasAny);

        public sealed class BiomePropSet
        {
            public readonly List<GameObject> Trees = new();
            public readonly List<GameObject> Bushes = new();
            public readonly List<GameObject> Grass = new();
            public readonly List<GameObject> Flowers = new();
            public readonly List<GameObject> Rocks = new();
            public readonly List<GameObject> GroundCover = new();

            public bool HasAny =>
                Trees.Count + Bushes.Count + Grass.Count + Flowers.Count + Rocks.Count + GroundCover.Count > 0;

            public List<GameObject> PoolFor(SurfacePropCategory category) =>
                category switch
                {
                    SurfacePropCategory.Trees => Trees,
                    SurfacePropCategory.Grass => Grass.Count > 0 ? Grass : Flowers,
                    SurfacePropCategory.Bushes => Bushes,
                    SurfacePropCategory.GroundCover => GroundCover.Count > 0 ? GroundCover : Flowers,
                    SurfacePropCategory.Rocks => Rocks,
                    _ => Trees,
                };
        }

        public static BiomePropCatalog Load(WorldGenerationRequest request = null)
        {
            var cat = new BiomePropCatalog
            {
                _base = SurfaceIntelligentPropPlacer.LoadVegetationCatalog(),
            };

            foreach (WorldSurfaceBiomeId biome in Enum.GetValues(typeof(WorldSurfaceBiomeId)))
                cat._sets[biome] = new BiomePropSet();

            cat.ClassifyAllPrefabs(request);
            cat.InjectCc0Prefabs();
            cat.EnsureFallbacks();
            return cat;
        }

        public List<GameObject> PoolForBiome(WorldSurfaceBiomeId biome, SurfacePropCategory category)
        {
            if (!_sets.TryGetValue(biome, out var set) || !set.HasAny)
                return FallbackPool(category);

            var pool = set.PoolFor(category);
            return pool.Count > 0 ? pool : FallbackPool(category);
        }

        public List<GameObject> BlendedPool(
            BiomePropFeatherResolver.BiomePropBlend blend,
            SurfacePropCategory category)
        {
            if (blend.SecondaryWeight <= 0.001f)
                return PoolForBiome(blend.Primary, category);

            if (blend.SecondaryWeight >= 0.999f)
                return PoolForBiome(blend.Secondary, category);

            var primary = PoolForBiome(blend.Primary, category);
            var secondary = PoolForBiome(blend.Secondary, category);
            if (secondary.Count == 0)
                return primary;
            if (primary.Count == 0)
                return secondary;

            // Weighted union — placement RNG picks index; duplicate entries bias toward primary.
            var merged = new List<GameObject>(primary.Count + secondary.Count);
            var primaryRepeats = Mathf.Max(1, Mathf.RoundToInt((1f - blend.SecondaryWeight) * 4f));
            var secondaryRepeats = Mathf.Max(1, Mathf.RoundToInt(blend.SecondaryWeight * 4f));
            for (var i = 0; i < primaryRepeats; i++)
                AppendUnique(merged, primary);
            for (var i = 0; i < secondaryRepeats; i++)
                AppendUnique(merged, secondary);
            return merged;
        }

        List<GameObject> FallbackPool(SurfacePropCategory category) =>
            SurfaceIntelligentPropPlacer.PoolForCategory(_base, category);

        void ClassifyAllPrefabs(WorldGenerationRequest request)
        {
            var all = new List<GameObject>();
            _base.AppendAllTo(all);

            foreach (var prefab in all)
            {
                if (prefab == null)
                    continue;

                var path = UnityEditor.AssetDatabase.GetAssetPath(prefab) ?? prefab.name;
                var pathLower = path.ToLowerInvariant();
                var nameLower = prefab.name.ToLowerInvariant();

                foreach (var biome in ResolveTargetBiomes(pathLower, nameLower, request))
                {
                    if (!_sets.TryGetValue(biome, out var set))
                        continue;
                    ClassifyIntoSet(set, prefab, pathLower, nameLower);
                }
            }
        }

        static IEnumerable<WorldSurfaceBiomeId> ResolveTargetBiomes(
            string pathLower,
            string nameLower,
            WorldGenerationRequest request)
        {
            // Path-color tags from LPMagicalForest folder layout.
            var isBlue = pathLower.Contains("/blue/") || nameLower.Contains("_blue_");
            var isPink = pathLower.Contains("/pink/") || nameLower.Contains("_pink_");
            var isGreen = pathLower.Contains("/green/") || nameLower.Contains("_green_");
            var isRock = nameLower.Contains("rock") || nameLower.Contains("boulder") ||
                         nameLower.Contains("stone") || nameLower.Contains("cliff");
            var isWaterPlant = nameLower.Contains("lotus") || nameLower.Contains("reed") ||
                               nameLower.Contains("alocasia") || nameLower.Contains("lily");
            var isFern = nameLower.Contains("fern") || nameLower.Contains("moss") ||
                         nameLower.Contains("mushroom") || nameLower.Contains("mushlight");
            var isMeadow = nameLower.Contains("poplar") || nameLower.Contains("beech") ||
                           nameLower.Contains("meadow") || nameLower.Contains("clover");
            var isPeak = nameLower.Contains("spruce") || nameLower.Contains("pine") ||
                         nameLower.Contains("cedar") || isBlue;
            var isSparse = nameLower.Contains("portal") || isRock;

            yield return WorldSurfaceBiomeId.PlayKarst;
            if (isMeadow || isGreen || isFern)
            {
                yield return WorldSurfaceBiomeId.FoothillGreen;
                yield return WorldSurfaceBiomeId.MixedTransition;
            }

            if (isPeak || isBlue)
            {
                yield return WorldSurfaceBiomeId.PeakStone;
                yield return WorldSurfaceBiomeId.HorizonMist;
            }

            if (isWaterPlant || isBlue)
            {
                yield return WorldSurfaceBiomeId.ConceptPreset04;
                yield return WorldSurfaceBiomeId.ConceptPreset07;
            }

            if (isRock)
            {
                yield return WorldSurfaceBiomeId.PeakStone;
                yield return WorldSurfaceBiomeId.MixedTransition;
            }

            if (isFern || isPink)
                yield return WorldSurfaceBiomeId.AnnexLabyrinth;

            if (!isSparse)
            {
                for (var i = 0; i < FullWorldConceptLayoutCatalog.ConceptCount; i++)
                    yield return FullWorldBiomeZoneLayout.PresetIndexToBiome(i);
            }

            if (request != null && request.SurfaceIncludeWater)
            {
                yield return WorldSurfaceBiomeId.ConceptPreset02;
                yield return WorldSurfaceBiomeId.ConceptPreset04;
                yield return WorldSurfaceBiomeId.ConceptPreset07;
            }
        }

        static void ClassifyIntoSet(BiomePropSet set, GameObject prefab, string pathLower, string nameLower)
        {
            if (nameLower.Contains("tree") || nameLower.Contains("pine") || nameLower.Contains("oak") ||
                nameLower.Contains("spruce") || nameLower.Contains("poplar") || nameLower.Contains("beech"))
                AddUnique(set.Trees, prefab);
            else if (nameLower.Contains("bush") || nameLower.Contains("shrub") || nameLower.Contains("hedge"))
                AddUnique(set.Bushes, prefab);
            else if (nameLower.Contains("grass") || nameLower.Contains("meadow") || nameLower.Contains("hay"))
                AddUnique(set.Grass, prefab);
            else if (nameLower.Contains("flower") || nameLower.Contains("bloom") || nameLower.Contains("lily") ||
                     nameLower.Contains("rose") || nameLower.Contains("lotus"))
                AddUnique(set.Flowers, prefab);
            else if (nameLower.Contains("rock") || nameLower.Contains("boulder") || nameLower.Contains("stone") ||
                     nameLower.Contains("cliff") || nameLower.Contains("granite"))
                AddUnique(set.Rocks, prefab);
            else if (nameLower.Contains("fern") || nameLower.Contains("moss") || nameLower.Contains("plant") ||
                     nameLower.Contains("mushroom") || pathLower.Contains("/plants/"))
                AddUnique(set.GroundCover, prefab);
            else if (pathLower.Contains("magicalforest"))
                AddUnique(set.GroundCover, prefab);
        }

        void InjectCc0Prefabs()
        {
            Cc0ContentImportUtility.EnsureAll(importItems: true);

            for (var g = 1; g <= 6; g++)
            {
                InjectGrass($"G-G{g:D2}", WorldSurfaceBiomeId.FoothillGreen, WorldSurfaceBiomeId.PlayKarst);
                InjectGrass($"G-C{g:D2}", WorldSurfaceBiomeId.FoothillGreen, WorldSurfaceBiomeId.MixedTransition);
                InjectGrass($"G-T{g:D2}", WorldSurfaceBiomeId.PeakStone, WorldSurfaceBiomeId.HorizonMist);
            }

            for (var k = 1; k <= 8; k++)
                InjectRock($"K{k:D2}", WorldSurfaceBiomeId.PeakStone, WorldSurfaceBiomeId.HorizonMist);

            for (var b = 1; b <= 15; b++)
                InjectBush($"B{b:D2}", WorldSurfaceBiomeId.FoothillGreen, WorldSurfaceBiomeId.MixedTransition);

            for (var l = 1; l <= 10; l++)
                InjectTree($"L{l:D2}", WorldSurfaceBiomeId.FoothillGreen, WorldSurfaceBiomeId.PeakStone);
        }

        void InjectGrass(string id, params WorldSurfaceBiomeId[] biomes)
        {
            var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
            if (prefab == null)
                return;

            foreach (var biome in biomes)
            {
                if (_sets.TryGetValue(biome, out var set))
                    AddUnique(set.Grass, prefab);
            }
        }

        void InjectRock(string id, params WorldSurfaceBiomeId[] biomes)
        {
            var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
            if (prefab == null)
                return;

            foreach (var biome in biomes)
            {
                if (_sets.TryGetValue(biome, out var set))
                    AddUnique(set.Rocks, prefab);
            }
        }

        void InjectBush(string id, params WorldSurfaceBiomeId[] biomes)
        {
            var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
            if (prefab == null)
                return;

            foreach (var biome in biomes)
            {
                if (_sets.TryGetValue(biome, out var set))
                    AddUnique(set.Bushes, prefab);
            }
        }

        void InjectTree(string id, params WorldSurfaceBiomeId[] biomes)
        {
            var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
            if (prefab == null)
                return;

            foreach (var biome in biomes)
            {
                if (_sets.TryGetValue(biome, out var set))
                    AddUnique(set.Trees, prefab);
            }
        }

        void EnsureFallbacks()
        {
            var play = _sets[WorldSurfaceBiomeId.PlayKarst];
            var foothill = _sets[WorldSurfaceBiomeId.FoothillGreen];
            var peak = _sets[WorldSurfaceBiomeId.PeakStone];
            var mixed = _sets[WorldSurfaceBiomeId.MixedTransition];

            CopyMissing(play, foothill);
            CopyMissing(foothill, play);
            CopyMissing(peak, foothill);
            CopyMissing(mixed, play);
            CopyMissing(mixed, foothill);

            for (var i = 0; i < FullWorldConceptLayoutCatalog.ConceptCount; i++)
            {
                var presetBiome = FullWorldBiomeZoneLayout.PresetIndexToBiome(i);
                if (!_sets.TryGetValue(presetBiome, out var presetSet))
                    continue;

                ApplyPresetFlavor(presetSet, i);
                if (!presetSet.HasAny)
                    CopyMissing(presetSet, mixed.HasAny ? mixed : play);
            }
        }

        static void ApplyPresetFlavor(BiomePropSet set, int presetIndex)
        {
            switch (presetIndex)
            {
                case 2: // Florida karst
                case 4: // Coastal fog
                case 7: // Water labyrinth
                    PromoteCategory(set, s => s.Grass, s => s.Flowers);
                    break;
                case 3: // Appalachian peaks
                    PromoteCategory(set, s => s.Trees, s => s.Rocks);
                    break;
                case 6: // Sparse trails
                case 9: // Speed minimal
                    TrimToSparse(set);
                    break;
            }
        }

        static void PromoteCategory(
            BiomePropSet set,
            Func<BiomePropSet, List<GameObject>> primary,
            Func<BiomePropSet, List<GameObject>> secondary)
        {
            foreach (var p in secondary(set))
                AddUnique(primary(set), p);
        }

        static void TrimToSparse(BiomePropSet set)
        {
            if (set.Trees.Count > 4)
                set.Trees.RemoveRange(4, set.Trees.Count - 4);
            if (set.Bushes.Count > 3)
                set.Bushes.RemoveRange(3, set.Bushes.Count - 3);
        }

        static void CopyMissing(BiomePropSet dest, BiomePropSet source)
        {
            AppendUnique(dest.Trees, source.Trees);
            AppendUnique(dest.Bushes, source.Bushes);
            AppendUnique(dest.Grass, source.Grass);
            AppendUnique(dest.Flowers, source.Flowers);
            AppendUnique(dest.Rocks, source.Rocks);
            AppendUnique(dest.GroundCover, source.GroundCover);
        }

        static void AppendUnique(List<GameObject> dest, List<GameObject> source)
        {
            foreach (var p in source)
                AddUnique(dest, p);
        }

        static void AddUnique(List<GameObject> list, GameObject prefab)
        {
            if (prefab != null && !list.Contains(prefab))
                list.Add(prefab);
        }

        public int PoolSizeForBiome(WorldSurfaceBiomeId biome, SurfacePropCategory category) =>
            PoolForBiome(biome, category).Count;
    }
}
#endif
