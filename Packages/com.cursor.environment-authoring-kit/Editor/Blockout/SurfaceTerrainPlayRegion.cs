#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Limits terrain grader / ladder edits to Environment Kit surface terrains (main + neighbor tiles), not the whole scene.
    /// </summary>
    static class SurfaceTerrainPlayRegion
    {
        public const float BoundsInsetMeters = 0.75f;

        public static bool ContainsWorldXZ(Terrain terrain, float worldX, float worldZ, float insetMeters = BoundsInsetMeters)
        {
            if (terrain?.terrainData == null)
                return false;

            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var minX = origin.x + insetMeters;
            var maxX = origin.x + size.x - insetMeters;
            var minZ = origin.z + insetMeters;
            var maxZ = origin.z + size.z - insetMeters;
            return worldX >= minX && worldX <= maxX && worldZ >= minZ && worldZ <= maxZ;
        }

        public static bool TryTerrainAtWorldXZ(Terrain mainTerrain, float worldX, float worldZ, out Terrain terrain)
        {
            terrain = null;
            if (mainTerrain == null)
                return false;

            if (ContainsWorldXZ(mainTerrain, worldX, worldZ))
            {
                terrain = mainTerrain;
                return true;
            }

            foreach (var tile in SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain))
            {
                if (tile != null && ContainsWorldXZ(tile, worldX, worldZ))
                {
                    terrain = tile;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Ground anchor when tagged; otherwise main terrain tile center.</summary>
        public static Vector3 ResolveUnifiedPlayCenter(SceneGroundInfo ground, Terrain mainTerrain)
        {
            if (ground != null && ground.HasAnchor)
                return ground.Anchor.position;

            return TerrainTileCenter(mainTerrain);
        }

        public static float ResolveRequestExtentMeters(WorldGenerationRequest request) =>
            request?.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;

        /// <summary>World disk for sculpt / crater repair — matches terrain pipeline (all tiles, one center).</summary>
        public static float ResolveRepairExtentMeters(
            Terrain mainTerrain,
            Vector3 playCenter,
            WorldGenerationRequest request) =>
            ResolveUnifiedSurfaceExtent(
                mainTerrain,
                playCenter,
                ResolveRequestExtentMeters(request));

        /// <summary>Extent that covers every surface terrain tile corner from the play center (one world disk, not per-tile circles).</summary>
        public static float ResolveUnifiedSurfaceExtent(
            Terrain mainTerrain,
            Vector3 playCenter,
            float requestExtentMeters)
        {
            var extent = Mathf.Max(80f, requestExtentMeters);
            if (mainTerrain?.terrainData == null)
                return extent;

            var playXZ = new Vector2(playCenter.x, playCenter.z);
            foreach (var terrain in CollectSurfaceTerrains(mainTerrain))
            {
                if (terrain?.terrainData == null)
                    continue;

                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                var maxCorner = Mathf.Max(
                    Vector2.Distance(playXZ, new Vector2(origin.x, origin.z)),
                    Vector2.Distance(playXZ, new Vector2(origin.x + size.x, origin.z)),
                    Vector2.Distance(playXZ, new Vector2(origin.x, origin.z + size.z)),
                    Vector2.Distance(playXZ, new Vector2(origin.x + size.x, origin.z + size.z)));
                extent = Mathf.Max(extent, maxCorner * 1.08f);
            }

            return Mathf.Clamp(extent, requestExtentMeters, 2048f);
        }

        public static void ForEachSurfaceTerrain(Terrain mainTerrain, Action<Terrain, Vector3> action)
        {
            if (mainTerrain == null || action == null)
                return;

            var center = TerrainTileCenter(mainTerrain);
            action(mainTerrain, center);

            foreach (var tile in SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain))
            {
                if (tile != null)
                    action(tile, center);
            }
        }

        /// <summary>All surface terrains use one play center (Ground anchor) so sculpt/smooth/LiDAR do not leave per-tile circles.</summary>
        public static void ForEachSurfaceTerrainUnified(
            Terrain mainTerrain,
            Vector3 unifiedPlayCenter,
            Action<Terrain, Vector3> action)
        {
            if (mainTerrain == null || action == null)
                return;

            action(mainTerrain, unifiedPlayCenter);

            foreach (var tile in SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain))
            {
                if (tile != null)
                    action(tile, unifiedPlayCenter);
            }
        }

        public static Vector3 TerrainTileCenter(Terrain terrain)
        {
            if (terrain == null)
                return Vector3.zero;

            var pos = terrain.transform.position;
            if (terrain.terrainData == null)
                return pos;

            var size = terrain.terrainData.size;
            return new Vector3(pos.x + size.x * 0.5f, pos.y, pos.z + size.z * 0.5f);
        }

        /// <summary>Play annulus around <paramref name="playCenter"/> intersected with this terrain tile footprint.</summary>
        public static bool InPlayAnnulusOnTerrain(
            Terrain terrain,
            int x,
            int y,
            int res,
            Vector3 playCenter,
            float extentMeters,
            float innerFraction = 0.08f,
            float outerFraction = 1.05f)
        {
            if (terrain?.terrainData == null)
                return false;

            var size = terrain.terrainData.size;
            var origin = terrain.transform.position;
            var wx = origin.x + x / (float)Mathf.Max(1, res - 1) * size.x;
            var wz = origin.z + y / (float)Mathf.Max(1, res - 1) * size.z;

            if (!ContainsWorldXZ(terrain, wx, wz))
                return false;

            var dist = Vector2.Distance(
                new Vector2(wx, wz),
                new Vector2(playCenter.x, playCenter.z));
            var inner = extentMeters * innerFraction;
            var outer = extentMeters * outerFraction;
            return dist >= inner && dist <= outer;
        }

        public static List<Terrain> CollectSurfaceTerrains(Terrain mainTerrain)
        {
            var list = new List<Terrain>(9);
            if (mainTerrain == null)
                return list;

            list.Add(mainTerrain);
            list.AddRange(SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain));
            return list;
        }

        /// <summary>Sync heightmap collision on main + neighbor tiles after sculpt/bench edits.</summary>
        public static void FlushAllSurfaceTerrains(Terrain mainTerrain)
        {
            foreach (var terrain in CollectSurfaceTerrains(mainTerrain))
            {
                if (terrain != null)
                    terrain.Flush();
            }
        }

        /// <summary>Max forward-axis distance from play center that stays inside surface tile footprints.</summary>
        public static float ResolveMaxTrailDistance(Terrain mainTerrain, Vector3 playCenter, Vector3 forward)
        {
            if (mainTerrain == null)
                return 80f;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            var best = 0f;
            foreach (var terrain in CollectSurfaceTerrains(mainTerrain))
            {
                if (terrain?.terrainData == null)
                    continue;

                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                var corners = new[]
                {
                    new Vector3(origin.x, 0f, origin.z),
                    new Vector3(origin.x + size.x, 0f, origin.z),
                    new Vector3(origin.x, 0f, origin.z + size.z),
                    new Vector3(origin.x + size.x, 0f, origin.z + size.z),
                };

                foreach (var corner in corners)
                {
                    var delta = corner - playCenter;
                    delta.y = 0f;
                    var along = Vector3.Dot(delta, forward);
                    if (along > best)
                        best = along;
                }
            }

            return Mathf.Max(48f, best * 0.88f);
        }
    }
}
#endif
