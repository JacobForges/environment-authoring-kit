#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// South mountain annex — 3×2 terrain grid (foothill row + peak row) below the nine-tile play disk.
    /// Grid offsets: positive Y in tile names = +Z world (north); south annex uses negative Y.
    /// </summary>
    public static class SurfaceMountainSouthAnnex
    {
        public const int SouthColumns = 3;
        public const int SouthRows = 2;

        public static readonly Vector2Int[] SouthFoothillAnnexOffsets =
        {
            new(-1, -2),
            new(0, -2),
            new(1, -2),
        };

        public static readonly Vector2Int[] SouthPeakAnnexOffsets =
        {
            new(-1, -3),
            new(0, -3),
            new(1, -3),
        };

        public static bool IsSouthAnnexOffset(Vector2Int off) =>
            IsSouthFoothillAnnexOffset(off) || IsSouthPeakAnnexOffset(off);

        public static bool IsSouthFoothillAnnexOffset(Vector2Int off)
        {
            for (var i = 0; i < SouthFoothillAnnexOffsets.Length; i++)
            {
                if (SouthFoothillAnnexOffsets[i] == off)
                    return true;
            }

            return false;
        }

        public static bool IsSouthPeakAnnexOffset(Vector2Int off)
        {
            for (var i = 0; i < SouthPeakAnnexOffsets.Length; i++)
            {
                if (SouthPeakAnnexOffsets[i] == off)
                    return true;
            }

            return false;
        }

        public static bool IsSouthFoothillAnnexTile(Terrain tile) =>
            TryGetAnnexOffset(tile, out var off) && IsSouthFoothillAnnexOffset(off);

        public static bool IsSouthPeakAnnexTile(Terrain tile) =>
            TryGetAnnexOffset(tile, out var off) && IsSouthPeakAnnexOffset(off);

        public static bool IsSouthAnnexTile(Terrain tile) =>
            IsSouthFoothillAnnexTile(tile) || IsSouthPeakAnnexTile(tile);

        static bool TryGetAnnexOffset(Terrain tile, out Vector2Int off)
        {
            off = default;
            if (tile == null)
                return false;

            return SurfaceTerrainTileExpansion.TryParseOuterRingTileOffset(tile.name, out off);
        }

        /// <summary>Foothill ring tiles that are not part of the south 3×2 labyrinth annex.</summary>
        public static Terrain[] CollectFoothillTilesOutsideSouthAnnex(Terrain mainTerrain)
        {
            var all = SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain);
            if (all.Length == 0)
                return Array.Empty<Terrain>();

            var list = new List<Terrain>(all.Length);
            for (var i = 0; i < all.Length; i++)
            {
                var tile = all[i];
                if (tile?.terrainData == null || IsSouthFoothillAnnexTile(tile))
                    continue;

                list.Add(tile);
            }

            return list.ToArray();
        }

        public static Terrain[] CollectSouthFoothillAnnexTiles(Terrain mainTerrain) =>
            CollectTilesAtOffsets(mainTerrain, SouthFoothillAnnexOffsets, SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix);

        public static Terrain[] CollectSouthPeakAnnexTiles(Terrain mainTerrain) =>
            CollectTilesAtOffsets(mainTerrain, SouthPeakAnnexOffsets, SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix);

        /// <summary>All six south annex tiles — foothill row (near play) then peak row (deeper south).</summary>
        public static Terrain[] CollectSouthAnnexTiles(Terrain mainTerrain)
        {
            var foothill = CollectSouthFoothillAnnexTiles(mainTerrain);
            var peak = CollectSouthPeakAnnexTiles(mainTerrain);
            if (foothill.Length == 0 && peak.Length == 0)
                return Array.Empty<Terrain>();

            var all = new Terrain[foothill.Length + peak.Length];
            Array.Copy(foothill, 0, all, 0, foothill.Length);
            Array.Copy(peak, 0, all, foothill.Length, peak.Length);
            return all;
        }

        public static void ComputeSouthAnnexWorldBounds(
            Terrain mainTerrain,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            var first = true;
            foreach (var tile in CollectSouthAnnexTiles(mainTerrain))
            {
                if (tile?.terrainData == null)
                    continue;

                var o = tile.transform.position;
                var s = tile.terrainData.size;
                var tMinX = o.x;
                var tMaxX = o.x + s.x;
                var tMinZ = o.z;
                var tMaxZ = o.z + s.z;

                if (first)
                {
                    minX = tMinX;
                    maxX = tMaxX;
                    minZ = tMinZ;
                    maxZ = tMaxZ;
                    first = false;
                    continue;
                }

                minX = Mathf.Min(minX, tMinX);
                maxX = Mathf.Max(maxX, tMaxX);
                minZ = Mathf.Min(minZ, tMinZ);
                maxZ = Mathf.Max(maxZ, tMaxZ);
            }

            if (first && mainTerrain?.terrainData != null)
            {
                SurfaceOuterRingMountainsAuthor.ComputeNineTileWorldBounds(
                    SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain),
                    out minX,
                    out maxX,
                    out minZ,
                    out maxZ);
                var tileSize = Mathf.Max(mainTerrain.terrainData.size.x, mainTerrain.terrainData.size.z);
                var cx = (minX + maxX) * 0.5f;
                minX = cx - tileSize * 1.55f;
                maxX = cx + tileSize * 1.55f;
                minZ -= tileSize * 2.1f;
            }
        }

        static Terrain[] CollectTilesAtOffsets(
            Terrain mainTerrain,
            IReadOnlyList<Vector2Int> offsets,
            string prefix)
        {
            if (mainTerrain == null || offsets == null || offsets.Count == 0)
                return Array.Empty<Terrain>();

            var root = SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(mainTerrain);
            if (root == null)
                return Array.Empty<Terrain>();

            var list = new List<Terrain>(offsets.Count);
            for (var i = 0; i < offsets.Count; i++)
            {
                var expected = $"{prefix}{offsets[i].x}_{offsets[i].y}";
                for (var c = 0; c < root.childCount; c++)
                {
                    var child = root.GetChild(c);
                    if (child.name != expected)
                        continue;

                    var tile = child.GetComponent<Terrain>();
                    if (tile?.terrainData != null)
                        list.Add(tile);
                    break;
                }
            }

            return list.ToArray();
        }
    }
}
#endif
