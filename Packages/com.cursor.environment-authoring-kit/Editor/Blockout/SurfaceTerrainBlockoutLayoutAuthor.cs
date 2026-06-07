#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Keeps the scene's modular Grid / EnvironmentRooms layout as the play reference while
    /// 81-tile mountain core + ~289-tile extended open world when UseExtendedOpenWorldGrid is on.
    /// </summary>
    public static class SurfaceTerrainBlockoutLayoutAuthor
    {
        public const string GridObjectName = "Grid";
        public const string EnvironmentRoomsName = "EnvironmentRooms";

        /// <summary>Modular room kit under Environment — not a FullWorld terrain anchor (with or without EnvironmentRooms).</summary>
        public static bool IsModularKitBlockoutGrid(Transform t)
        {
            if (t == null)
                return false;

            if (!string.Equals(t.name, GridObjectName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (t.Find(EnvironmentRoomsName) != null)
                return true;

            var p = t.parent;
            while (p != null)
            {
                if (string.Equals(p.name, EnvironmentRoot.DefaultName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.name, "Environment", StringComparison.OrdinalIgnoreCase))
                    return true;
                p = p.parent;
            }

            return false;
        }

        public static void DemoteModularGridGroundTag()
        {
            if (!TryFindBlockoutGrid(out var grid, out _))
                return;

            if (grid.CompareTag("Ground"))
            {
                try
                {
                    grid.tag = "Untagged";
                }
                catch
                {
                    // Tag may not exist in project.
                }
            }
        }

        public static void PromoteMainTerrainGroundTag(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return;

            DemoteModularGridGroundTag();
            foreach (var tag in UnityEditorInternal.InternalEditorUtility.tags)
            {
                if (!string.Equals(tag, "Ground", StringComparison.Ordinal))
                    continue;

                mainTerrain.gameObject.tag = "Ground";
                SceneGroundResolver.SaveAssignedGround(mainTerrain.transform);
                return;
            }
        }

        public const float FootprintCarveHalfWidthMeters = 3.4f;
        public const float FootprintCarveStrength = 0.52f;

        public static bool TryFindBlockoutGrid(out Transform grid, out Transform rooms)
        {
            grid = null;
            rooms = null;

            var env = GameObject.Find(EnvironmentRoot.DefaultName)?.transform;
            if (env != null)
                grid = env.Find(GridObjectName);

            grid ??= GameObject.Find(GridObjectName)?.transform;
            if (grid == null)
                return false;

            rooms = grid.Find(EnvironmentRoomsName);
            return true;
        }

        public static bool ComputeBlockoutBounds(Transform grid, out Bounds bounds)
        {
            bounds = default;
            if (grid == null)
                return false;

            var renderers = grid.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(grid.position, new Vector3(24f, 4f, 24f));
                return true;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.sqrMagnitude > 0.01f;
        }

        /// <summary>Move the nine-tile play disk so its center matches the modular room cluster on Grid.</summary>
        public static bool AlignPlayDiskToBlockoutGrid(Terrain mainTerrain, SceneGroundInfo ground)
        {
            if (mainTerrain?.terrainData == null || !TryFindBlockoutGrid(out var grid, out _))
                return false;

            if (!ComputeBlockoutBounds(grid, out var blockout))
                return false;

            var tileSize = mainTerrain.terrainData.size;
            var targetCenter = new Vector3(blockout.center.x, mainTerrain.transform.position.y, blockout.center.z);
            var desiredOrigin = new Vector3(
                targetCenter.x - tileSize.x * 0.5f,
                blockout.max.y - tileSize.y * 0.42f,
                targetCenter.z - tileSize.z * 0.5f);

            var delta = desiredOrigin - mainTerrain.transform.position;
            if (delta.sqrMagnitude < 0.04f)
                return false;

            var group = SurfaceTerrainTileExpansion.BuildPlayDiskTerrainGroup(mainTerrain);
            foreach (var tile in group)
            {
                if (tile == null)
                    continue;
                tile.transform.position += delta;
            }

            if (ground != null && ground.HasAnchor)
                ground.SurfaceY = blockout.max.y;

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Play disk aligned to {GridObjectName} blockout (Δ {delta.magnitude:F1}m) — rooms stay authoritative layout.",
                forceUnityConsole: true);
            return true;
        }

        public static void QueueGeoCarvePlayDisk(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Action onComplete)
        {
            if (mainTerrain == null || request == null || !request.CarveTerrainToBlockoutFootprints)
            {
                onComplete?.Invoke();
                return;
            }

            if (!TryFindBlockoutGrid(out var grid, out var rooms))
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Blockout geo carve skipped — no Grid/EnvironmentRooms in scene.",
                    forceUnityConsole: false);
                onComplete?.Invoke();
                return;
            }

            var footprints = CollectFootprintPolylines(rooms != null ? rooms : grid);
            if (footprints.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Blockout geo carve — {footprints.Count} footprint(s) on play disk (frame-paced).",
                forceUnityConsole: true);

            var tiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            var center = SurfaceTerrainTileExpansion.TileCenterWorld(mainTerrain);
            var extent = mainTerrain.terrainData.size.x * 1.6f;
            QueueCarveFootprintAtIndex(tiles, footprints, 0, () =>
            {
                foreach (var tile in tiles)
                {
                    if (tile?.terrainData != null)
                        SurfaceTerrainCraterRepair.RepairHeightfieldPlayable(tile, center, extent, maxPasses: 4);
                }

                onComplete?.Invoke();
            });
        }

        static List<Vector3[]> CollectFootprintPolylines(Transform root)
        {
            var list = new List<Vector3[]>();
            if (root == null)
                return list;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                var b = renderer.bounds;
                if (b.size.x < 0.5f || b.size.z < 0.5f)
                    continue;

                var y = b.min.y + 0.05f;
                list.Add(new[]
                {
                    new Vector3(b.min.x, y, b.min.z),
                    new Vector3(b.max.x, y, b.min.z),
                    new Vector3(b.max.x, y, b.max.z),
                    new Vector3(b.min.x, y, b.max.z),
                    new Vector3(b.min.x, y, b.min.z),
                });
            }

            return list;
        }

        static void QueueCarveFootprintAtIndex(
            IReadOnlyList<Terrain> tiles,
            List<Vector3[]> footprints,
            int index,
            Action onComplete)
        {
            if (index >= footprints.Count)
            {
                onComplete?.Invoke();
                return;
            }

            var poly = footprints[index];
            foreach (var tile in tiles)
            {
                if (tile?.terrainData == null)
                    continue;
                if (!PolylineHitsTerrain(tile, poly))
                    continue;

                SampleHeights(tile, poly);
                SurfaceTerrainRadialAuthor.FlattenTrailBench(
                    tile,
                    poly,
                    FootprintCarveHalfWidthMeters,
                    FootprintCarveStrength);
                tile.Flush();
            }

            CaveBuildActionPacing.ScheduleLight(
                () => QueueCarveFootprintAtIndex(tiles, footprints, index + 1, onComplete),
                CaveBuildPipelineDomains.QueueLabel("blockout geo carve — next footprint"));
        }

        static bool PolylineHitsTerrain(Terrain terrain, Vector3[] poly)
        {
            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var pad = FootprintCarveHalfWidthMeters + 2f;
            foreach (var p in poly)
            {
                if (p.x >= origin.x - pad && p.x <= origin.x + size.x + pad &&
                    p.z >= origin.z - pad && p.z <= origin.z + size.z + pad)
                    return true;
            }

            return false;
        }

        static void SampleHeights(Terrain terrain, Vector3[] poly)
        {
            var y0 = terrain.transform.position.y;
            for (var i = 0; i < poly.Length; i++)
            {
                var p = poly[i];
                p.y = terrain.SampleHeight(p) + y0;
                poly[i] = p;
            }
        }

        /// <summary>FullWorld terrain-first: main play terrain owns Ground anchor (not Environment/Grid).</summary>
        public static void RebindFullWorldGroundToMainTerrain(SceneGroundInfo ground, Terrain mainTerrain)
        {
            if (ground == null || mainTerrain == null)
                return;

            if (mainTerrain.GetComponent<EnvironmentAuthoringKit.EnvironmentRoot>() == null &&
                mainTerrain.name != SurfaceTerrainTileExpansion.MainTerrainName)
                mainTerrain.name = SurfaceTerrainTileExpansion.MainTerrainName;

            ground.Terrain = mainTerrain;
            ground.Anchor = mainTerrain.transform;
            PromoteMainTerrainGroundTag(mainTerrain);
            SceneGroundResolver.RefreshSurface(ground);
        }

        public static void BindMainTerrainToGroundInfo(SceneGroundInfo ground)
        {
            if (ground == null)
                return;

            var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
            foreach (var t in terrains)
            {
                if (t == null || t.name != SurfaceTerrainTileExpansion.MainTerrainName)
                    continue;

                ground.Terrain = t;
                if (ground.Anchor == null ||
                    string.Equals(ground.Anchor.name, GridObjectName, StringComparison.OrdinalIgnoreCase))
                {
                    ground.Anchor = t.transform;
                }

                return;
            }
        }
    }
}
#endif
