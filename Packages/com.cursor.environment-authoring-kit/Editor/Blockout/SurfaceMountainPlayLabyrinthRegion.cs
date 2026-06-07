#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Surface labyrinth lives on the southern two rows of the 3×3 play disk (6 tiles),
    /// not on the south foothill/peak annex. Grid Y: +1 north, 0 center, -1 south.
    /// </summary>
    public static class SurfaceMountainPlayLabyrinthRegion
    {
        public const int PlayLabyrinthRows = 2;

        /// <summary>South row (y=-1) and middle row (y=0) — entire 3×3 width.</summary>
        public static readonly Vector2Int[] PlayLabyrinthOffsets =
        {
            new(-1, -1), new(0, -1), new(1, -1),
            new(-1, 0), new(0, 0), new(1, 0),
        };

        public static bool IsPlayLabyrinthOffset(Vector2Int off)
        {
            for (var i = 0; i < PlayLabyrinthOffsets.Length; i++)
            {
                if (PlayLabyrinthOffsets[i] == off)
                    return true;
            }

            return false;
        }

        public static bool IsPlayLabyrinthTile(Terrain tile, Terrain mainTerrain)
        {
            if (tile == null || mainTerrain == null)
                return false;

            return SurfaceTerrainTileExpansion.TryParsePlayDiskGridOffset(mainTerrain, tile, out var off) &&
                   IsPlayLabyrinthOffset(off);
        }

        public static Terrain[] CollectPlayLabyrinthTiles(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return Array.Empty<Terrain>();

            var group = SurfaceTerrainTileExpansion.BuildPlayDiskTerrainGroup(mainTerrain);
            var list = new List<Terrain>(PlayLabyrinthOffsets.Length);
            for (var i = 0; i < group.Length; i++)
            {
                var tile = group[i];
                if (tile?.terrainData == null)
                    continue;
                if (!IsPlayLabyrinthTile(tile, mainTerrain))
                    continue;
                list.Add(tile);
            }

            return list.ToArray();
        }

        public static void ComputePlayLabyrinthWorldBounds(
            Terrain mainTerrain,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            var tiles = CollectPlayLabyrinthTiles(mainTerrain);
            var first = true;
            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i];
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

            if (!first)
                return;

            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            SurfaceOuterRingMountainsAuthor.ComputeNineTileWorldBounds(
                nine,
                out minX,
                out maxX,
                out minZ,
                out maxZ);
        }

        public const int ExitWest = 0;
        public const int ExitCenter = 1;
        public const int ExitEast = 2;
        public const int ExitTitan = 3;

        public const int ExitCount = 4;

        /// <summary>Four labyrinth exits — W/C/E south → peak cave mouths; Titan → Hollow Titan tree on peak ring.</summary>
        public static Vector3[] ResolveLabyrinthExitWorldPoints(
            Terrain mainTerrain,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ,
            int seed)
        {
            var south = ResolveSouthCaveExitWorldPoints(mainTerrain, playMinX, playMaxX, playMinZ, playMaxZ);
            var titan = south[1];
            if (HollowTitanLandmarkSitePlanner.TryResolveLabyrinthTitanTarget(mainTerrain, seed, out var titanTarget))
                titan = titanTarget;
            else
            {
                ComputePlayLabyrinthWorldBounds(mainTerrain, out var labMinX, out var labMaxX, out _, out _);
                var cx = (playMinX + playMaxX) * 0.5f;
                var cz = (playMinZ + playMaxZ) * 0.5f;
                var dx = labMaxX - cx;
                var dz = (playMinZ + playMaxZ) * 0.5f - cz;
                titan = new Vector3(cx + dx * 0.85f, 0f, cz + dz * 0.15f);
            }

            return new[] { south[0], south[1], south[2], titan };
        }

        /// <summary>Three south-edge exits — west / center / east (Cave W / C / E trails).</summary>
        public static Vector3[] ResolveSouthCaveExitWorldPoints(
            Terrain mainTerrain,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            return ResolveSouthExitWorldPoints(mainTerrain, playMinX, playMaxX, playMinZ, playMaxZ);
        }

        /// <summary>Three south-edge exits — west / center / east (Cave W / C / E trails).</summary>
        public static Vector3[] ResolveSouthExitWorldPoints(
            Terrain mainTerrain,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            ComputePlayLabyrinthWorldBounds(mainTerrain, out _, out _, out var labMinZ, out _);
            var southZ = labMinZ + 22f;
            var cx = (playMinX + playMaxX) * 0.5f;
            var span = playMaxX - playMinX;
            return new[]
            {
                new Vector3(cx - span * 0.28f, 0f, southZ),
                new Vector3(cx, 0f, southZ),
                new Vector3(cx + span * 0.28f, 0f, southZ),
            };
        }
    }
}
#endif
