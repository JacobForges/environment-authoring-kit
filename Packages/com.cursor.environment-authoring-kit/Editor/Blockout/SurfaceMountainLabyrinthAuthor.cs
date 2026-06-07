#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Carves research-guided surface labyrinth corridors (spine paths, not per-cell edge grid).
    /// </summary>
    public static class SurfaceMountainLabyrinthAuthor
    {
        public const string LabyrinthMarkerPrefix = "MountainLabyrinth_";
        public const float CorridorHalfWidthMeters = 12.5f;
        public const float PeakCorridorHalfWidthExtraMeters = 2.5f;
        public const float FoothillWalkwayFlattenStrength = 0.24f;
        public const float FoothillWallBandMeters = 9.5f;
        public const float FoothillWallRaiseMeters = 2.6f;
        public const float FoothillWalkwaySmoothStrength = 0.07f;
        public const float PeakMassifMoldStrength = 0.38f;
        public const float PeakMassifShaveMeters = 3.4f;

        public enum MountainLabyrinthCarveMode
        {
            FoothillWalkway,
            PeakMassif,
        }

        public static void QueueApplyPeakMountainLabyrinth(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Vector3 center,
            int seed,
            Action onComplete) =>
            QueueApply(
                mainTerrain,
                ground,
                request,
                surfaceRoot,
                center,
                seed,
                MountainLabyrinthCarveMode.PeakMassif,
                onComplete);

        public static void QueueApplyFoothillWalkwayLabyrinth(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Vector3 center,
            int seed,
            Action onComplete) =>
            QueueApply(
                mainTerrain,
                ground,
                request,
                surfaceRoot,
                center,
                seed,
                MountainLabyrinthCarveMode.FoothillWalkway,
                onComplete);

        public static void QueueApply(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Vector3 center,
            int seed,
            Action onComplete) =>
            QueueApply(
                mainTerrain,
                ground,
                request,
                surfaceRoot,
                center,
                seed,
                MountainLabyrinthCarveMode.FoothillWalkway,
                onComplete);

        public static void QueueApply(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Vector3 center,
            int seed,
            MountainLabyrinthCarveMode carveMode,
            Action onComplete)
        {
            if (mainTerrain?.terrainData == null || request == null ||
                !request.SurfaceIncludeMountains || !request.SurfaceIncludeMountainLabyrinth ||
                !request.UseOuterRingMountains)
            {
                onComplete?.Invoke();
                return;
            }

            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (nine.Count < 9)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Mountain labyrinth skipped — {nine.Count}/9 play tiles.",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

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
            layout.GetWorldBounds(out var labMinX, out var labMaxX, out var labMinZ, out var labMaxZ);
            var polylines = layout.BuildCarvePolylines(playMinX, playMaxX, playMinZ, playMaxZ);

            PlaceLabyrinthMarkers(surfaceRoot, layout, seed);
            var modeLabel = "seed-random maze spines (play disk south 2 rows — 6 tiles)";
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Mountain labyrinth — {modeLabel} — {layout.Width}×{layout.Height} cells, " +
                $"{polylines.Count} carve polyline(s), play rows y∈{{-1,0}} (trails → 3 south peak mouths).",
                forceUnityConsole: true);

            var tiles = CollectCarveTerrains(mainTerrain);
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Mountain labyrinth — play-area carve on {tiles.Length}/6 south play tile(s).",
                forceUnityConsole: true);

            QueueCarveAllSpines(mainTerrain, tiles, polylines, () => AfterLabyrinthCarve(mainTerrain, tiles, onComplete));
        }

        static void AfterLabyrinthCarve(Terrain mainTerrain, Terrain[] annexTiles, Action onComplete)
        {
            if (annexTiles == null || annexTiles.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            void SeamAndFinish()
            {
                SurfaceTerrainSeamWelds.QueuePerPhaseSeamWeld(
                    mainTerrain,
                    annexTiles,
                    "mountain_labyrinth_carve",
                    () =>
                    {
                        SurfaceTerrainTileExpansion.QueueStitchNeighborSeamsOnly(
                            mainTerrain,
                            () =>
                            {
                                CaveBuildEditorLog.LogSurface(
                                    "[Surface] Mountain labyrinth — play-disk benches + seam stitch complete (mouth trails in mountain_trails).",
                                    forceUnityConsole: true);
                                onComplete?.Invoke();
                            });
                    });
            }

            SeamAndFinish();
        }

        static void SplitSouthAnnexTiles(Terrain[] annexTiles, out Terrain[] foothillAnnex, out Terrain[] peakAnnex)
        {
            var foothill = new List<Terrain>(3);
            var peak = new List<Terrain>(3);
            for (var i = 0; i < annexTiles.Length; i++)
            {
                var tile = annexTiles[i];
                if (tile?.terrainData == null)
                    continue;
                if (SurfaceMountainSouthAnnex.IsSouthFoothillAnnexTile(tile))
                    foothill.Add(tile);
                else if (SurfaceMountainSouthAnnex.IsSouthPeakAnnexTile(tile))
                    peak.Add(tile);
            }

            foothillAnnex = foothill.ToArray();
            peakAnnex = peak.ToArray();
        }

        static void QueueCarveAllSpines(
            Terrain mainTerrain,
            Terrain[] tiles,
            List<Vector3[]> polylines,
            Action onComplete)
        {
            var touched = new HashSet<Terrain>();
            QueueCarveSpinePolylineAtIndex(mainTerrain, tiles, polylines, 0, touched, onComplete);
        }

        static void QueueCarveSpinePolylineAtIndex(
            Terrain mainTerrain,
            Terrain[] tiles,
            List<Vector3[]> polylines,
            int polyIndex,
            HashSet<Terrain> touched,
            Action onComplete)
        {
            while (polyIndex < polylines.Count &&
                   (polylines[polyIndex] == null || polylines[polyIndex].Length < 2))
                polyIndex++;

            if (polyIndex >= polylines.Count)
            {
                if (touched.Count == 0)
                {
                    onComplete?.Invoke();
                    return;
                }

                SurfaceTerrainTileExpansion.QueueStitchMountainTilesTouchedByCorridor(
                    mainTerrain,
                    new List<Terrain>(touched),
                    onComplete);
                return;
            }

            var poly = polylines[polyIndex];
            var nextIndex = polyIndex + 1;
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    CaveBuildRunStatusPublisher.PulseSubOperation(
                        "mountain labyrinth",
                        $"spine {polyIndex + 1}/{polylines.Count}");

                    foreach (var tile in tiles)
                    {
                        if (tile?.terrainData == null || !PolylineIntersectsTerrain(tile, poly))
                            continue;

                        CarveSpineOnTile(mainTerrain, tile, poly);
                        tile.Flush();
                        touched.Add(tile);
                    }

                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueCarveSpinePolylineAtIndex(
                            mainTerrain,
                            tiles,
                            polylines,
                            nextIndex,
                            touched,
                            onComplete),
                        CaveBuildPipelineDomains.QueueLabel("mountain labyrinth — next spine"));
                },
                CaveBuildPipelineDomains.QueueLabel("mountain labyrinth — spine carve"));
        }

        static void CarveSpineOnTile(Terrain mainTerrain, Terrain tile, Vector3[] poly)
        {
            if (tile?.terrainData == null || poly == null || poly.Length < 2)
                return;

            if (mainTerrain == null || !SurfaceMountainPlayLabyrinthRegion.IsPlayLabyrinthTile(tile, mainTerrain))
                return;

            SamplePolylineHeights(tile, poly);
            SurfaceTerrainRadialAuthor.SculptLabyrinthWalkwayWithRaisedWalls(
                tile,
                poly,
                CorridorHalfWidthMeters * 0.92f,
                FoothillWallBandMeters,
                FoothillWallRaiseMeters,
                FoothillWalkwaySmoothStrength);
        }

        /// <summary>Restore rolling Appalachian foothills on the 13-tile ring outside the south annex after labyrinth carve.</summary>
        public static void QueueRestoreNonAnnexFoothillRollingHills(Terrain mainTerrain, Action onComplete)
        {
            var foothills = SurfaceMountainSouthAnnex.CollectFoothillTilesOutsideSouthAnnex(mainTerrain);
            if (foothills.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Foothill rolling-hill restore — {foothills.Length} non-annex tile(s) keep sculpt (no denoise flatten).",
                forceUnityConsole: true);
            onComplete?.Invoke();
        }

        static Terrain[] CollectCarveTerrains(Terrain mainTerrain)
        {
            var tiles = SurfaceMountainPlayLabyrinthRegion.CollectPlayLabyrinthTiles(mainTerrain);
            if (tiles.Length < SurfaceMountainPlayLabyrinthRegion.PlayLabyrinthOffsets.Length)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[Surface] Labyrinth carve — play labyrinth incomplete ({tiles.Length}/6 tiles).");
            }

            return tiles;
        }

        static bool TileIntersectsWorldAabb(
            Terrain terrain,
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            if (terrain?.terrainData == null)
                return false;

            var o = terrain.transform.position;
            var s = terrain.terrainData.size;
            return o.x + s.x >= minX && o.x <= maxX && o.z + s.z >= minZ && o.z <= maxZ;
        }

        static bool PolylineIntersectsTerrain(Terrain terrain, Vector3[] poly)
        {
            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var minX = origin.x;
            var maxX = origin.x + size.x;
            var minZ = origin.z;
            var maxZ = origin.z + size.z;
            var pad = CorridorHalfWidthMeters + PeakCorridorHalfWidthExtraMeters + 2f;

            for (var i = 0; i < poly.Length; i++)
            {
                var p = poly[i];
                if (p.x >= minX - pad && p.x <= maxX + pad && p.z >= minZ - pad && p.z <= maxZ + pad)
                    return true;
            }

            return false;
        }

        static void SamplePolylineHeights(Terrain terrain, Vector3[] poly)
        {
            var y = terrain.transform.position.y;
            for (var i = 0; i < poly.Length; i++)
            {
                var p = poly[i];
                p.y = terrain.SampleHeight(p) + y;
                poly[i] = p;
            }
        }

        static void PlaceLabyrinthMarkers(Transform surfaceRoot, SurfaceMountainLabyrinthLayout layout, int seed)
        {
            if (surfaceRoot == null)
                return;

            var root = EnvironmentSceneUtility.GetOrCreateChild(surfaceRoot, SurfaceWorldPaths.MountainLabyrinthName);
            if (PipelineContentPreservePolicy.HasMeaningfulContent(root))
                return;

            CreateMarker(root, "Entrance", layout.PlayEntranceWorld, seed);
            CreateMarker(root, "Center", layout.MazeCenterWorld, seed + 1);
            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var c = layout.SolutionPath[i];
                CreateMarker(root, $"Waypoint_{i}", layout.CellToWorld(c.x, c.y), seed + 10 + i);
            }
        }

        static void CreateMarker(Transform root, string label, Vector3 world, int seed)
        {
            var go = new GameObject(LabyrinthMarkerPrefix + label);
            go.transform.SetParent(root, false);
            go.transform.position = world;
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
#endif
