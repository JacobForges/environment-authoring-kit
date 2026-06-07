#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Lightweight grass/rock alphamap from planned biome — visible right after flat ground lay (no prop scatter).
    /// </summary>
    static class SurfaceTerrainBiomePreviewAuthor
    {
        public static void ApplyPlannedBiomeAlphamap(Terrain tile, Vector2Int off, int seed, bool isSouthAnnex = false)
        {
            if (tile?.terrainData == null)
                return;

            var grassLayers = ProjectTerrainLayerResolver.TryResolveTerrainLayers(1);
            var rockLayers = ProjectTerrainLayerResolver.TryResolveMountainRockLayers(2);
            if (grassLayers == null || grassLayers.Length == 0 || grassLayers[0] == null)
                return;

            var rockWeight = ResolvePlannedRockWeight01(tile, off, seed, isSouthAnnex);
            if (rockLayers == null || rockLayers.Length == 0 || rockWeight <= 0.001f)
            {
                ApplyUniformAlphamap(tile, grassLayers, grassWeight: 1f);
                return;
            }

            var layers = new TerrainLayer[1 + rockLayers.Length];
            layers[0] = grassLayers[0];
            for (var i = 0; i < rockLayers.Length; i++)
                layers[i + 1] = rockLayers[i];

            ApplyGrassRockAlphamap(tile, layers, rockWeight);
        }

        static float ResolvePlannedRockWeight01(Terrain tile, Vector2Int off, int seed, bool isSouthAnnex)
        {
            var name = tile.name ?? string.Empty;
            if (name.StartsWith(SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix, System.StringComparison.Ordinal) ||
                name.StartsWith(SurfaceTerrainTileExpansion.MountainHorizonTileNamePrefix, System.StringComparison.Ordinal))
                return 0.78f;

            if (name.StartsWith(SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix, System.StringComparison.Ordinal))
                return 0.42f;

            if (name.StartsWith(SurfaceTerrainTileExpansion.OpenWorldTileNamePrefix, System.StringComparison.Ordinal))
            {
                var preset = FullWorldBiomeZoneLayout.BiomeToPresetIndex(
                    FullWorldBiomeZoneLayout.PresetIndexToBiome(FullWorldBiomeZoneLayout.ResolvePresetIndex(off)));
                return preset >= 0 ? 0.22f + preset * 0.04f : 0.35f;
            }

            var biome = SurfaceWorldBiomeAuthor.ResolveBiomeForTileOffset(off, isSouthAnnex);
            switch (biome)
            {
                case WorldSurfaceBiomeId.PlayKarst:
                    return 0.08f;
                case WorldSurfaceBiomeId.MixedTransition:
                {
                    var cheb = FullWorldBiomeZoneLayout.ChebyshevDistance(off);
                    if (cheb <= 3)
                        return 0.18f;
                    if (cheb <= 6)
                        return 0.32f;
                    return 0.48f;
                }
                case WorldSurfaceBiomeId.PeakStone:
                case WorldSurfaceBiomeId.HorizonMist:
                    return 0.72f;
                case WorldSurfaceBiomeId.FoothillGreen:
                    return 0.38f;
                case WorldSurfaceBiomeId.AnnexLabyrinth:
                    return 0.55f;
                default:
                {
                    var presetIndex = FullWorldBiomeZoneLayout.BiomeToPresetIndex(biome);
                    if (presetIndex >= 0)
                        return 0.16f + presetIndex * 0.05f;
                    var roll = FullWorldBiomeZoneLayout.RollMixedFlavorIndex(seed, off);
                    return 0.2f + roll * 0.03f;
                }
            }
        }

        static void ApplyUniformAlphamap(Terrain tile, TerrainLayer[] layers, float grassWeight)
        {
            var data = tile.terrainData;
            CaveEditorUndo.RecordObject(data, "Biome preview alphamap");
            data.terrainLayers = layers;
            var w = data.alphamapWidth;
            var h = data.alphamapHeight;
            var map = new float[w, h, layers.Length];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                    map[x, y, 0] = grassWeight;
            }

            data.SetAlphamaps(0, 0, map);
        }

        static void ApplyGrassRockAlphamap(Terrain tile, TerrainLayer[] layers, float rockWeight01)
        {
            var data = tile.terrainData;
            CaveEditorUndo.RecordObject(data, "Biome preview alphamap");
            data.terrainLayers = layers;
            var w = data.alphamapWidth;
            var h = data.alphamapHeight;
            var map = new float[w, h, layers.Length];
            rockWeight01 = Mathf.Clamp01(rockWeight01);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var nx = x / (float)(w - 1);
                    var ny = y / (float)(h - 1);
                    var noise = Mathf.PerlinNoise(nx * 5.1f + 0.17f, ny * 5.1f + 0.23f);
                    var rock = Mathf.Clamp01(rockWeight01 + (noise - 0.5f) * 0.12f);
                    map[x, y, 0] = 1f - rock;
                    if (layers.Length > 1)
                        map[x, y, 1] = rock * 0.72f;
                    if (layers.Length > 2)
                        map[x, y, 2] = rock * 0.28f;
                }
            }

            data.SetAlphamaps(0, 0, map);
        }
    }
}
#endif
