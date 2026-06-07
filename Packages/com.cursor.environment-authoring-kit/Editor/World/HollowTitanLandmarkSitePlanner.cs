#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Seed-locked Hollow Titan peak site + labyrinth titan exit bearing (same target for trails + tree).
    /// </summary>
    internal static class HollowTitanLandmarkSitePlanner
    {
        const float SiteJitterMeters = 48f;

        public static bool TryResolvePeakSite(
            Terrain mainTerrain,
            int seed,
            out Terrain tile,
            out Vector3 worldPos)
        {
            tile = null;
            worldPos = default;
            if (mainTerrain?.terrainData == null)
                return false;

            var rng = new System.Random(seed + 0x484F4C4C);
            var peaks = CollectPeakTiles(mainTerrain);
            if (peaks.Count == 0)
                return false;

            var pick = peaks[rng.Next(peaks.Count)];
            var td = pick.terrainData;
            var lx = td.size.x * (0.35f + (float)rng.NextDouble() * 0.3f);
            var lz = td.size.z * (0.35f + (float)rng.NextDouble() * 0.3f);
            var wx = pick.transform.position.x + lx;
            var wz = pick.transform.position.z + lz;

            if (!HollowTitanLandmarkTerrainSnap.TrySampleGroundY(mainTerrain, wx, wz, out var gy, out _))
                return false;

            tile = pick;
            worldPos = new Vector3(wx, gy, wz);
            return true;
        }

        /// <summary>World target for labyrinth titan exit trail (same seed as tree site).</summary>
        public static bool TryResolveLabyrinthTitanTarget(
            Terrain mainTerrain,
            int seed,
            out Vector3 worldTarget)
        {
            worldTarget = default;
            if (!TryResolvePeakSite(mainTerrain, seed, out _, out worldTarget))
                return false;

            worldTarget += new Vector3(
                (float)new System.Random(seed + 99173).NextDouble() * SiteJitterMeters * 2f - SiteJitterMeters,
                0f,
                (float)new System.Random(seed + 77101).NextDouble() * SiteJitterMeters * 2f - SiteJitterMeters);

            if (HollowTitanLandmarkTerrainSnap.TrySampleGroundY(
                    mainTerrain,
                    worldTarget.x,
                    worldTarget.z,
                    out var gy,
                    out _))
                worldTarget.y = gy;

            return true;
        }

        static List<Terrain> CollectPeakTiles(Terrain mainTerrain)
        {
            var all = SurfaceTerrainPlayRegion.CollectAllKitGroundTerrains(mainTerrain);
            var peaks = new List<Terrain>();
            for (var i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t?.terrainData == null)
                    continue;
                if (t.name.StartsWith(
                        SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                        StringComparison.Ordinal))
                    peaks.Add(t);
                else if (SurfaceTerrainTileExpansion.TryParseOuterRingTileOffset(t.name, out var off) &&
                         SurfaceTerrainTileExpansion.IsPeakRingOffset(off))
                    peaks.Add(t);
            }

            if (peaks.Count == 0)
            {
                var fromExpansion = SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain);
                peaks.AddRange(fromExpansion);
            }

            return peaks;
        }
    }
}
#endif
