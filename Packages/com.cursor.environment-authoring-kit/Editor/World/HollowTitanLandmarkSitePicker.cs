#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Surface site selection for Hollow Titan — excludes mouths, labyrinth, trails.</summary>
    internal static class HollowTitanLandmarkSitePicker
    {
        const float MinMouthDistanceMeters = 36f;
        const float MinTrailDistanceMeters = 22f;

        public static bool TryPickLandmarkSpot(
            Terrain mainTerrain,
            int seed,
            bool useAllSurfaceTiles,
            out Terrain tile,
            out Vector3 worldPos)
        {
            tile = null;
            worldPos = default;
            var terrains = useAllSurfaceTiles
                ? SurfaceTerrainPlayRegion.CollectAllKitGroundTerrains(mainTerrain)
                : SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (terrains == null || terrains.Count == 0)
                return false;

            ComputeLabyrinthExclusion(mainTerrain, seed, out var labMinX, out var labMaxX, out var labMinZ, out var labMaxZ);

            if (HollowTitanLandmarkSitePlanner.TryResolvePeakSite(mainTerrain, seed, out var plannedTile, out var plannedPos) &&
                !IsInsideLabyrinth(plannedPos, labMinX, labMaxX, labMinZ, labMaxZ) &&
                !SurfaceIntelligentPropPlacer.IsNearCaveEntrance(plannedPos, MinMouthDistanceMeters) &&
                !IsNearTrailPolyline(mainTerrain, plannedPos, MinTrailDistanceMeters))
            {
                tile = plannedTile;
                worldPos = plannedPos;
                return true;
            }

            var rng = new System.Random(seed + 0x484F4C4C);
            var candidates = new List<(Terrain t, Vector3 pos)>();
            var attempts = useAllSurfaceTiles ? 384 : 96;
            var requirePeakTile = useAllSurfaceTiles;

            if (useAllSurfaceTiles && CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid)
            {
                TryAddExtendedGridSlotCandidates(
                    mainTerrain,
                    rng,
                    labMinX,
                    labMaxX,
                    labMinZ,
                    labMaxZ,
                    candidates,
                    attempts / 2,
                    requirePeakOnly: true);
            }

            var pickPool = FilterPeakTerrainsIfAvailable(terrains, useAllSurfaceTiles);

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var t = pickPool[rng.Next(pickPool.Count)];
                if (t?.terrainData == null)
                    continue;

                var td = t.terrainData;
                var localX = (float)rng.NextDouble() * td.size.x;
                var localZ = (float)rng.NextDouble() * td.size.z;
                var worldX = t.transform.position.x + localX;
                var worldZ = t.transform.position.z + localZ;

                if (!TryAcceptLandmarkCandidate(
                        mainTerrain,
                        t,
                        worldX,
                        worldZ,
                        useAllSurfaceTiles,
                        labMinX,
                        labMaxX,
                        labMinZ,
                        labMaxZ,
                        requirePeakTile,
                        out var pos))
                    continue;

                candidates.Add((t, pos));
                if (candidates.Count >= 12)
                    break;
            }

            if (candidates.Count == 0 && requirePeakTile)
            {
                TryAddExtendedGridSlotCandidates(
                    mainTerrain,
                    rng,
                    labMinX,
                    labMaxX,
                    labMinZ,
                    labMaxZ,
                    candidates,
                    attempts / 2,
                    requirePeakOnly: false);

                var fallbackPool = terrains;
                for (var attempt = 0; attempt < attempts / 2 && candidates.Count < 12; attempt++)
                {
                    var t = fallbackPool[rng.Next(fallbackPool.Count)];
                    if (t?.terrainData == null)
                        continue;

                    var td = t.terrainData;
                    var worldX = t.transform.position.x + (float)rng.NextDouble() * td.size.x;
                    var worldZ = t.transform.position.z + (float)rng.NextDouble() * td.size.z;

                    if (!TryAcceptLandmarkCandidate(
                            mainTerrain,
                            t,
                            worldX,
                            worldZ,
                            useAllSurfaceTiles,
                            labMinX,
                            labMaxX,
                            labMinZ,
                            labMaxZ,
                            requirePeakTile: false,
                            out var pos))
                        continue;

                    candidates.Add((t, pos));
                }
            }

            if (candidates.Count == 0)
                return false;

            var pick = PickBestCandidate(candidates, rng);
            tile = pick.t;
            worldPos = pick.pos;
            return true;
        }

        static (Terrain t, Vector3 pos) PickBestCandidate(
            List<(Terrain t, Vector3 pos)> candidates,
            System.Random rng)
        {
            var peakOnly = new List<(Terrain t, Vector3 pos)>();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (IsPeakWildernessTile(candidates[i].t))
                    peakOnly.Add(candidates[i]);
            }

            var pool = peakOnly.Count > 0 ? peakOnly : candidates;
            (Terrain t, Vector3 pos) best = pool[0];
            for (var i = 1; i < pool.Count; i++)
            {
                if (pool[i].pos.y > best.pos.y)
                    best = pool[i];
            }

            if (pool.Count <= 1)
                return best;

            var top = new List<(Terrain t, Vector3 pos)>();
            var threshold = best.pos.y - 2f;
            for (var i = 0; i < pool.Count; i++)
            {
                if (pool[i].pos.y >= threshold)
                    top.Add(pool[i]);
            }

            return top[rng.Next(top.Count)];
        }

        static bool IsPeakWildernessTile(Terrain tile)
        {
            if (tile == null || string.IsNullOrEmpty(tile.name))
                return false;
            return tile.name.StartsWith(
                       SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                       StringComparison.Ordinal) ||
                   (SurfaceTerrainTileExpansion.TryParseOuterRingTileOffset(tile.name, out var off) &&
                    SurfaceTerrainTileExpansion.IsPeakRingOffset(off));
        }

        static void TryAddExtendedGridSlotCandidates(
            Terrain mainTerrain,
            System.Random rng,
            float labMinX,
            float labMaxX,
            float labMinZ,
            float labMaxZ,
            List<(Terrain t, Vector3 pos)> candidates,
            int maxAttempts,
            bool requirePeakOnly)
        {
            if (mainTerrain?.terrainData == null || maxAttempts <= 0)
                return;

            var offsets = SurfaceOpenWorldGridExpansion.BuildAaaExtendedPlaceOrder();
            if (offsets == null || offsets.Length == 0)
                return;

            var gameplayRoot = SurfaceTerrainTileExpansion.FindTilesRootPublic(mainTerrain);
            var wildernessRoot = SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(mainTerrain);

            for (var attempt = 0; attempt < maxAttempts && candidates.Count < 12; attempt++)
            {
                var off = offsets[rng.Next(offsets.Length)];
                if (SurfaceTerrainTileExpansion.IsNineTileGameplayOffset(off))
                    continue;

                if (requirePeakOnly && !SurfaceTerrainTileExpansion.IsPeakRingOffset(off))
                    continue;

                if (!SurfaceTerrainTileExpansion.TryResolveTerrainAtGridOffset(
                        mainTerrain,
                        gameplayRoot,
                        wildernessRoot,
                        off,
                        out var t) ||
                    t?.terrainData == null)
                    continue;

                var td = t.terrainData;
                var worldX = t.transform.position.x + td.size.x * 0.5f;
                var worldZ = t.transform.position.z + td.size.z * 0.5f;

                if (!TryAcceptLandmarkCandidate(
                        mainTerrain,
                        t,
                        worldX,
                        worldZ,
                        useAllSurfaceTiles: true,
                        labMinX,
                        labMaxX,
                        labMinZ,
                        labMaxZ,
                        requirePeakOnly,
                        out var pos))
                    continue;

                candidates.Add((t, pos));
            }
        }

        static List<Terrain> FilterPeakTerrainsIfAvailable(List<Terrain> terrains, bool useAllSurfaceTiles)
        {
            if (!useAllSurfaceTiles || terrains == null || terrains.Count == 0)
                return terrains;

            var peaks = new List<Terrain>();
            for (var i = 0; i < terrains.Count; i++)
            {
                if (IsPeakWildernessTile(terrains[i]))
                    peaks.Add(terrains[i]);
            }

            return peaks.Count > 0 ? peaks : terrains;
        }

        static bool TryAcceptLandmarkCandidate(
            Terrain mainTerrain,
            Terrain tile,
            float worldX,
            float worldZ,
            bool useAllSurfaceTiles,
            float labMinX,
            float labMaxX,
            float labMinZ,
            float labMaxZ,
            bool requirePeakTile,
            out Vector3 pos)
        {
            pos = default;
            if (!HollowTitanLandmarkTerrainSnap.TrySampleGroundY(
                    mainTerrain,
                    worldX,
                    worldZ,
                    out var groundY,
                    out _))
                return false;

            pos = new Vector3(worldX, groundY, worldZ);

            if (!useAllSurfaceTiles && pos.y < tile.transform.position.y + 1.5f)
                return false;
            if (IsInsideLabyrinth(pos, labMinX, labMaxX, labMinZ, labMaxZ))
                return false;
            if (useAllSurfaceTiles && requirePeakTile && !IsPeakWildernessTile(tile))
                return false;
            if (SurfaceIntelligentPropPlacer.IsNearCaveEntrance(pos, MinMouthDistanceMeters))
                return false;
            if (IsNearTrailPolyline(mainTerrain, pos, MinTrailDistanceMeters))
                return false;

            return true;
        }

        static void ComputeLabyrinthExclusion(
            Terrain mainTerrain,
            int seed,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (nine.Count < 9)
                return;

            SurfaceOuterRingMountainsAuthor.ComputeNineTileWorldBounds(
                nine,
                out var playMinX,
                out var playMaxX,
                out var playMinZ,
                out var playMaxZ);
            SurfaceTerrainTileExpansion.ComputeFoothillRingWorldBounds(
                mainTerrain,
                out var fhMinX,
                out var fhMaxX,
                out var fhMinZ,
                out var fhMaxZ);

            var layout = SurfaceMountainLabyrinthLayout.Generate(
                seed,
                playMinX,
                playMaxX,
                playMinZ,
                playMaxZ,
                fhMinX,
                fhMaxX,
                fhMinZ,
                fhMaxZ,
                mainTerrain);
            layout.GetWorldBounds(out minX, out maxX, out minZ, out maxZ);
        }

        static bool IsInsideLabyrinth(Vector3 world, float minX, float maxX, float minZ, float maxZ)
        {
            if (maxX <= minX || maxZ <= minZ)
                return false;
            return world.x >= minX && world.x <= maxX && world.z >= minZ && world.z <= maxZ;
        }

        static bool IsNearTrailPolyline(Terrain mainTerrain, Vector3 world, float minDistanceMeters)
        {
            var env = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
            var trails = surface != null ? surface.Find(SurfaceWorldPaths.TrailsName) : null;
            if (trails == null)
                return false;

            var minSq = minDistanceMeters * minDistanceMeters;
            foreach (Transform child in trails)
            {
                if (child == null)
                    continue;
                var pts = CollectTrailPoints(child);
                for (var i = 0; i < pts.Count; i++)
                {
                    if ((pts[i] - world).sqrMagnitude < minSq)
                        return true;
                }
            }

            return false;
        }

        static List<Vector3> CollectTrailPoints(Transform trail)
        {
            var list = new List<Vector3>(16);
            for (var i = 0; i < trail.childCount; i++)
            {
                var c = trail.GetChild(i);
                if (c != null)
                    list.Add(c.position);
            }

            if (list.Count == 0)
                list.Add(trail.position);
            return list;
        }
    }
}
#endif
