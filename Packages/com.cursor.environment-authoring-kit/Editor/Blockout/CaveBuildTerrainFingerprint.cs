#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Heightmap fingerprints for incremental skip (DEM stamp, wilderness sculpt, prop categories).
    /// </summary>
    public static class CaveBuildTerrainFingerprint
    {
        public const string StoreRel = CaveBuildAgentContextExporter.Folder + "/TerrainArtifactFingerprints.json";

        [Serializable]
        public class FingerprintStore
        {
            public int seed;
            public string playDiskFingerprint;
            public string[] dirtyTerrainTileIds = Array.Empty<string>();
            public TileEntry[] tiles = Array.Empty<TileEntry>();
            public PropCategoryEntry[] propCategories = Array.Empty<PropCategoryEntry>();
        }

        [Serializable]
        public class TileEntry
        {
            public string tileId;
            public string heightmapFingerprint;
            public string innerNeighborFingerprint;
        }

        [Serializable]
        public class PropCategoryEntry
        {
            public string category;
            public string terrainFingerprint;
            public int instanceCount;
        }

        public static string ComputeTerrainHeightmapFingerprint(Terrain terrain)
        {
            if (terrain?.terrainData == null)
                return "empty";

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            if (res <= 0)
                return "empty";

            var heights = data.GetHeights(0, 0, res, res);
            using var sha = SHA256.Create();
            var bytes = new byte[res * res * 4];
            var bi = 0;
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var bits = BitConverter.GetBytes(heights[z, x]);
                    bytes[bi++] = bits[0];
                    bytes[bi++] = bits[1];
                    bytes[bi++] = bits[2];
                    bytes[bi++] = bits[3];
                }
            }

            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string ComputePlayDiskFingerprint(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return "none";

            var tiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            var parts = new List<string>(tiles.Count);
            foreach (var t in tiles)
            {
                if (t == null)
                    continue;
                parts.Add($"{t.name}:{ComputeTerrainHeightmapFingerprint(t)}");
            }

            parts.Sort(StringComparer.Ordinal);
            using var sha = SHA256.Create();
            var text = string.Join("|", parts);
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string ResolveWildernessTileId(Terrain tile) =>
            tile != null ? tile.name : "unknown";

        public static bool ShouldSkipNineTileNeighborDemStamp(
            Terrain neighborTile,
            int seed,
            out string reason)
        {
            reason = string.Empty;
            if (neighborTile?.terrainData == null)
                return false;

            var store = LoadStore(seed);
            var tileId = neighborTile.name;
            var fp = ComputeTerrainHeightmapFingerprint(neighborTile);
            foreach (var entry in store.tiles ?? Array.Empty<TileEntry>())
            {
                if (entry.tileId != tileId || string.IsNullOrEmpty(entry.heightmapFingerprint))
                    continue;
                if (entry.heightmapFingerprint != fp)
                    continue;

                reason = $"heightmap unchanged for {tileId}";
                return true;
            }

            return false;
        }

        public static bool ShouldSkipWildernessTileSculpt(
            Terrain tile,
            Terrain mainTerrain,
            int seed,
            out string reason)
        {
            reason = string.Empty;
            if (tile?.terrainData == null)
                return false;

            var store = LoadStore(seed);
            var tileId = ResolveWildernessTileId(tile);
            var fp = ComputeTerrainHeightmapFingerprint(tile);
            var innerFp = ComputeInnerNeighborFingerprint(mainTerrain, tile);
            foreach (var entry in store.tiles ?? Array.Empty<TileEntry>())
            {
                if (entry.tileId != tileId)
                    continue;
                if (entry.heightmapFingerprint == fp &&
                    entry.innerNeighborFingerprint == innerFp)
                {
                    reason = $"sculpt inputs unchanged for {tileId}";
                    return true;
                }

                return false;
            }

            return false;
        }

        public static bool ShouldSkipPropCategory(
            string category,
            Terrain mainTerrain,
            int seed,
            int instanceCount,
            out string reason)
        {
            reason = string.Empty;
            if (mainTerrain == null || string.IsNullOrEmpty(category))
                return false;

            var terrainFp = ComputePlayDiskFingerprint(mainTerrain);
            var store = LoadStore(seed);
            foreach (var entry in store.propCategories ?? Array.Empty<PropCategoryEntry>())
            {
                if (entry.category != category)
                    continue;
                if (entry.terrainFingerprint == terrainFp && entry.instanceCount == instanceCount)
                {
                    reason = $"props+terrain unchanged for {category}";
                    return true;
                }

                return false;
            }

            return false;
        }

        public static void RecordPlayDiskFingerprint(int seed, Terrain mainTerrain)
        {
            var store = LoadStore(seed);
            store.seed = seed;
            store.playDiskFingerprint = ComputePlayDiskFingerprint(mainTerrain);
            SaveStore(store);
        }

        public static void RecordWildernessTile(int seed, Terrain tile, Terrain mainTerrain)
        {
            if (tile == null)
                return;

            var store = LoadStore(seed);
            store.seed = seed;
            var list = new List<TileEntry>(store.tiles ?? Array.Empty<TileEntry>());
            var tileId = ResolveWildernessTileId(tile);
            list.RemoveAll(e => e.tileId == tileId);
            list.Add(new TileEntry
            {
                tileId = tileId,
                heightmapFingerprint = ComputeTerrainHeightmapFingerprint(tile),
                innerNeighborFingerprint = ComputeInnerNeighborFingerprint(mainTerrain, tile),
            });
            store.tiles = list.ToArray();
            SaveStore(store);
        }

        public static void RecordPropCategory(int seed, string category, Terrain mainTerrain, int instanceCount)
        {
            var store = LoadStore(seed);
            store.seed = seed;
            var list = new List<PropCategoryEntry>(store.propCategories ?? Array.Empty<PropCategoryEntry>());
            list.RemoveAll(e => e.category == category);
            list.Add(new PropCategoryEntry
            {
                category = category,
                terrainFingerprint = ComputePlayDiskFingerprint(mainTerrain),
                instanceCount = instanceCount,
            });
            store.propCategories = list.ToArray();
            SaveStore(store);
        }

        public static void MarkDirtyTiles(int seed, IReadOnlyList<string> tileIds)
        {
            if (tileIds == null || tileIds.Count == 0)
                return;

            var store = LoadStore(seed);
            store.seed = seed;
            var set = new HashSet<string>(store.dirtyTerrainTileIds ?? Array.Empty<string>());
            foreach (var id in tileIds)
                set.Add(id);
            store.dirtyTerrainTileIds = new List<string>(set).ToArray();
            SaveStore(store);
        }

        public static IReadOnlyList<string> GetDirtyTileIds(int seed)
        {
            var store = LoadStore(seed);
            return store.seed == seed
                ? store.dirtyTerrainTileIds ?? Array.Empty<string>()
                : Array.Empty<string>();
        }

        static string ComputeInnerNeighborFingerprint(Terrain mainTerrain, Terrain tile)
        {
            if (tile == null || mainTerrain == null)
                return "none";

            if (!SurfaceTerrainTileExpansion.TryParseMountainWildernessOffset(tile.name, out var off))
                return tile.name;

            var gameplay = SurfaceTerrainTileExpansion.FindTilesRootPublic(mainTerrain);
            var wilderness = SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(mainTerrain);
            var innerOff = SurfaceTerrainGridRegistry.StepTowardOrigin(off);
            if (!SurfaceTerrainTileExpansion.TryResolveTerrainAtGridOffset(
                    mainTerrain,
                    gameplay,
                    wilderness,
                    innerOff,
                    out var inner) ||
                inner == null)
                return "no_inner";

            return $"{inner.name}:{ComputeTerrainHeightmapFingerprint(inner)}";
        }

        public static FingerprintStore LoadStore(int seed)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, StoreRel);
            if (!File.Exists(path))
                return new FingerprintStore { seed = seed };

            try
            {
                var store = JsonUtility.FromJson<FingerprintStore>(File.ReadAllText(path));
                return store ?? new FingerprintStore { seed = seed };
            }
            catch
            {
                return new FingerprintStore { seed = seed };
            }
        }

        static void SaveStore(FingerprintStore store)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, StoreRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, JsonUtility.ToJson(store, true));
        }
    }
}
#endif
