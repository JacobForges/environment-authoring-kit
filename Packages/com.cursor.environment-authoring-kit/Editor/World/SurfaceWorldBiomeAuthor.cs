#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Assigns <see cref="WorldSurfaceBiomeId"/> from play / mixed / preset zones.</summary>
    public static class SurfaceWorldBiomeAuthor
    {
        public const string BiomeMarkerPrefix = "WorldBiome_";

        public static WorldSurfaceBiomeId ResolveBiomeForTileOffset(Vector2Int off, bool isSouthAnnex) =>
            FullWorldBiomeZoneLayout.ResolveBiome(off, isSouthAnnex);

        public static void TagAllSurfaceTiles(Terrain mainTerrain, int seed)
        {
            if (mainTerrain == null)
                return;

            var annexOffsets = CollectAnnexOffsets(mainTerrain);
            var root = EnvironmentSceneUtility.GetOrCreateChild(
                mainTerrain.transform.root,
                "WorldBiomeMarkers");

            foreach (var tile in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (tile == null ||
                    !SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out var off))
                    continue;

                var biome = ResolveBiomeForTileOffset(off, annexOffsets.Contains(off));
                PlaceMarker(root, tile, biome, off);
            }

            Debug.Log(
                $"[Surface] World biomes tagged — seed {seed}, annex {annexOffsets.Count}, " +
                $"layout 9 play + ~{FullWorldBiomeZoneLayout.MixedRingTileCount} mixed + 10 presets.");
        }

        /// <summary>Every kit terrain tagged — mixed ring + preset wedges on open-world tiles.</summary>
        public static void TagAllGroundTerrains(Terrain mainTerrain, int seed)
        {
            if (mainTerrain == null)
                return;

            var annexOffsets = CollectAnnexOffsets(mainTerrain);
            var root = EnvironmentSceneUtility.GetOrCreateChild(
                mainTerrain.transform.root,
                "WorldBiomeMarkers");

            var tagged = 0;
            foreach (var tile in SurfaceTerrainPlayRegion.CollectAllKitGroundTerrains(mainTerrain))
            {
                if (tile?.terrainData == null ||
                    !SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out var off))
                    continue;

                var biome = ResolveBiomeForTileOffset(off, annexOffsets.Contains(off));
                var label = $"{BiomeMarkerPrefix}{biome}_{off.x}_{off.y}";
                var existing = root.Find(label);
                if (existing != null)
                    continue;

                PlaceMarker(root, tile, biome, off);
                tagged++;
            }

            Debug.Log(
                $"[Surface] World biomes (AAA) — {tagged} tile(s), seed {seed}. " +
                "Zones: play(9) → mixed(~72) → preset wedges(0–9).");
        }

        static HashSet<Vector2Int> CollectAnnexOffsets(Terrain mainTerrain)
        {
            var annexOffsets = new HashSet<Vector2Int>();
            foreach (var t in SurfaceMountainSouthAnnex.CollectSouthAnnexTiles(mainTerrain))
            {
                if (t != null &&
                    SurfaceTerrainTileExpansion.TryParseTileOffset(t.name, out var annexOff))
                    annexOffsets.Add(annexOff);
            }

            return annexOffsets;
        }

        static void PlaceMarker(Transform root, Terrain tile, WorldSurfaceBiomeId biome, Vector2Int off)
        {
            var label = $"{BiomeMarkerPrefix}{biome}_{off.x}_{off.y}";
            if (root.Find(label) != null)
                return;

            var go = new GameObject(label);
            go.transform.SetParent(root, false);
            var center = tile.transform.position + new Vector3(
                tile.terrainData.size.x * 0.5f,
                0f,
                tile.terrainData.size.z * 0.5f);
            go.transform.position = center;
        }
    }
}
#endif
