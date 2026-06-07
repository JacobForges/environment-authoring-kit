#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Plan v2 seam policy — B (world-space edge blend), D (per-phase welds), A (light play↔foothill after annex work).
    /// </summary>
    public static class SurfaceTerrainSeamWelds
    {
        public static void QueuePerPhaseSeamWeld(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> touchedTiles,
            string phaseLabel,
            Action onComplete)
        {
            if (mainTerrain == null || touchedTiles == null || touchedTiles.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var expanded = ExpandWithOrthogonalNeighbors(mainTerrain, touchedTiles);
            if (expanded.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Per-phase seam weld ({phaseLabel}) — {expanded.Count} tile(s).",
                forceUnityConsole: true);
            CaveBuildRunStatusPublisher.PulseSubOperation("surface seams", $"phase weld — {phaseLabel}");

            SurfaceTerrainTileExpansion.QueueStitchMountainTilesTouchedByCorridor(
                mainTerrain,
                expanded,
                () =>
                {
                    SurfaceTerrainTileExpansion.RefreshMountainTerrainConnectivity(mainTerrain);
                    onComplete?.Invoke();
                });
        }

        /// <summary>
        /// After south annex labyrinth / mouths — weld 6 annex tiles + play south perimeter (light A).
        /// </summary>
        public static void QueueSouthAnnexSeamCluster(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var cluster = BuildSouthAnnexSeamCluster(mainTerrain);
            if (cluster.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] South annex seam cluster — {cluster.Count} tile(s) (6 annex + play south neighbors).",
                forceUnityConsole: true);

            SurfaceTerrainTileExpansion.QueueStitchMountainTilesTouchedByCorridor(
                mainTerrain,
                cluster,
                () =>
                    SurfaceTerrainTileExpansion.QueueStitchPlayPerimeterToFoothills(
                        mainTerrain,
                        () =>
                        {
                            SurfaceTerrainTileExpansion.RefreshMountainTerrainConnectivity(mainTerrain);
                            onComplete?.Invoke();
                        }));
        }

        static List<Terrain> BuildSouthAnnexSeamCluster(Terrain mainTerrain)
        {
            var set = new HashSet<Terrain>();
            foreach (var tile in SurfaceMountainSouthAnnex.CollectSouthAnnexTiles(mainTerrain))
            {
                if (tile?.terrainData != null)
                    set.Add(tile);
            }

            foreach (var tile in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (tile?.terrainData == null)
                    continue;

                if (!SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out var off))
                    continue;

                if (off.y == -1)
                    set.Add(tile);
            }

            return ExpandWithOrthogonalNeighbors(mainTerrain, new List<Terrain>(set));
        }

        static List<Terrain> ExpandWithOrthogonalNeighbors(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> seeds)
        {
            var set = new HashSet<Terrain>();
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i]?.terrainData != null)
                    set.Add(seeds[i]);
            }

            if (mainTerrain?.terrainData != null)
                set.Add(mainTerrain);

            var snapshot = new List<Terrain>(set);
            for (var i = 0; i < snapshot.Count; i++)
            {
                var tile = snapshot[i];
                if (tile == null ||
                    !SurfaceTerrainTileExpansion.TryResolveSurfaceTileGridOffset(mainTerrain, tile, out var off))
                    continue;

                TryAddNeighborAt(mainTerrain, set, off, new Vector2Int(1, 0));
                TryAddNeighborAt(mainTerrain, set, off, new Vector2Int(-1, 0));
                TryAddNeighborAt(mainTerrain, set, off, new Vector2Int(0, 1));
                TryAddNeighborAt(mainTerrain, set, off, new Vector2Int(0, -1));
            }

            return new List<Terrain>(set);
        }

        /// <summary>Extra weld at foothill↔peak inner edges after flat grid (Full AAA).</summary>
        public static void QueuePeakFoothillSeamWeld(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var cluster = new List<Terrain>();
            foreach (var t in SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain))
            {
                if (t != null)
                    cluster.Add(t);
            }

            foreach (var t in SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain))
            {
                if (t != null && !cluster.Contains(t))
                    cluster.Add(t);
            }

            if (cluster.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var expanded = ExpandWithOrthogonalNeighbors(mainTerrain, cluster);
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Peak↔foothill seam weld — {expanded.Count} tile(s) (no gaps at ring 2–3).",
                forceUnityConsole: true);

            SurfaceTerrainTileExpansion.QueueStitchMountainTilesTouchedByCorridor(
                mainTerrain,
                expanded,
                () =>
                    SurfaceTerrainTileExpansion.QueueStitchPlayPerimeterToFoothills(
                        mainTerrain,
                        () =>
                        {
                            SurfaceTerrainTileExpansion.RefreshMountainTerrainConnectivity(mainTerrain);
                            onComplete?.Invoke();
                        }));
        }

        static void TryAddNeighborAt(
            Terrain mainTerrain,
            HashSet<Terrain> set,
            Vector2Int off,
            Vector2Int delta)
        {
            var target = off + delta;
            if (mainTerrain != null && target == Vector2Int.zero)
            {
                set.Add(mainTerrain);
                return;
            }

            foreach (var tile in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (tile?.terrainData == null ||
                    !SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out var tileOff) ||
                    tileOff != target)
                    continue;

                set.Add(tile);
                return;
            }

            var wildernessRoot = SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(mainTerrain);
            if (wildernessRoot == null)
                return;

            foreach (var prefix in new[]
                     {
                         SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix,
                         SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                     })
            {
                var expected = $"{prefix}{target.x}_{target.y}";
                for (var c = 0; c < wildernessRoot.childCount; c++)
                {
                    var child = wildernessRoot.GetChild(c);
                    if (child.name != expected)
                        continue;

                    var terrain = child.GetComponent<Terrain>();
                    if (terrain?.terrainData != null)
                        set.Add(terrain);
                    return;
                }
            }
        }
    }
}
#endif
