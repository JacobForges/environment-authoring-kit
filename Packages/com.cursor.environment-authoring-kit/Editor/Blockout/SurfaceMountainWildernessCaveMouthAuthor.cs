#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// South peak annex — three cave mouth bowls (one per south peak tile), north-facing cliff faces toward play disk.
    /// Runs before labyrinth carve (massif beat). Each mouth gets a satellite POI cave (sectors 201–203).
    /// </summary>
    public static class SurfaceMountainWildernessCaveMouthAuthor
    {
        public const float WildernessMouthRadiusMeters = 14f;
        public const float WildernessMouthDepthMeters = 3.1f;
        public const string MarkerName = "MountainWildernessCaveMouth";
        public const string MarkerNamePrefix = "MountainWildernessCaveMouth_South";

        /// <summary>Center south peak — main mountain connector (surface trail + full POI cave).</summary>
        public const int SectorMainMountain = 201;

        /// <summary>West south peak — long dead-end trap POI.</summary>
        public const int SectorTrapMountain = 202;

        /// <summary>East south peak — labyrinth cadence POI to large underground chamber.</summary>
        public const int SectorLabyrinthMountain = 203;

        public static void QueueApply(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            int seed,
            Action onComplete)
        {
            if (mainTerrain?.terrainData == null || request == null || !request.UseMountainWildernessCaveMouth)
            {
                onComplete?.Invoke();
                return;
            }

            var southPeaks = SurfaceMountainSouthAnnex.CollectSouthPeakAnnexTiles(mainTerrain);
            if (southPeaks.Length == 0)
            {
                QueueApplyLegacySingle(mainTerrain, ground, request, surfaceRoot, seed, onComplete);
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] South peak cave mouths — {southPeaks.Length} north-facing cliff entrance(s) " +
                $"(main={SectorMainMountain}, trap={SectorTrapMountain}, labyrinth={SectorLabyrinthMountain}).",
                forceUnityConsole: true);

            var touched = new List<Terrain>();
            QueueMouthOnSouthPeakAtIndex(
                mainTerrain,
                ground,
                request,
                surfaceRoot,
                seed,
                southPeaks,
                0,
                touched,
                onComplete);
        }

        static void QueueMouthOnSouthPeakAtIndex(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            int seed,
            Terrain[] southPeaks,
            int index,
            List<Terrain> touched,
            Action onComplete)
        {
            if (index >= southPeaks.Length)
            {
                if (touched.Count == 0)
                {
                    onComplete?.Invoke();
                    return;
                }

                SurfaceOuterRingMountainsAuthor.QueueDenoiseWildernessTiles(
                    mainTerrain,
                    touched.ToArray(),
                    () =>
                        SurfaceTerrainTileExpansion.QueueStitchMountainTilesTouchedByCorridor(
                            mainTerrain,
                            touched,
                            () => CaveBuildActionPacing.QueueWhenIdle(
                                onComplete,
                                CaveBuildPipelineDomains.QueueLabel("south peak cave mouths — done"))));
                return;
            }

            var tile = southPeaks[index];
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    if (tile == null ||
                        !TryPickSouthPeakMouthSite(tile, ground, mainTerrain, seed, index, out var mouthWorld, out var mouthForward))
                    {
                        QueueMouthOnSouthPeakAtIndex(
                            mainTerrain,
                            ground,
                            request,
                            surfaceRoot,
                            seed,
                            southPeaks,
                            index + 1,
                            touched,
                            onComplete);
                        return;
                    }

                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] South peak cave mouth {index + 1}/{southPeaks.Length} — '{tile.name}' sector {SectorForPeakIndex(index)}.",
                        forceUnityConsole: true);

                    var intoMountain = mouthForward;
                    intoMountain.y = 0f;
                    if (intoMountain.sqrMagnitude < 0.01f)
                        intoMountain = Vector3.back;
                    CaveTerrainCarveUtility.CarveCliffTunnelMouth(
                        tile,
                        mouthWorld,
                        intoMountain,
                        widthMeters: WildernessMouthRadiusMeters * 2.4f,
                        heightMeters: WildernessMouthRadiusMeters * 1.6f,
                        depthMeters: WildernessMouthDepthMeters + 8f);

                    if (index == 0)
                        CarveSummitApproach(tile, mouthWorld, intoMountain, seed);

                    FlattenApproachBench(tile, mouthWorld, intoMountain);
                    EnsureMarker(surfaceRoot, mouthWorld, intoMountain, index);
                    tile.Flush();
                    touched.Add(tile);

                    QueueMouthOnSouthPeakAtIndex(
                        mainTerrain,
                        ground,
                        request,
                        surfaceRoot,
                        seed,
                        southPeaks,
                        index + 1,
                        touched,
                        onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel($"south peak cave mouth {index + 1}/{southPeaks.Length}"));
        }

        public static int SectorForPeakIndex(int peakIndex) =>
            peakIndex switch
            {
                0 => SectorMainMountain,
                1 => SectorTrapMountain,
                _ => SectorLabyrinthMountain,
            };

        /// <summary>Matches <see cref="SurfaceMountainSouthAnnex.SouthPeakAnnexOffsets"/> order: 0=west, 1=center, 2=east (Cave E).</summary>
        public const int SouthPeakMouthWest = 0;
        public const int SouthPeakMouthCenter = 1;
        public const int SouthPeakMouthEast = 2;

        public static bool TryFindMainMountainMouthWorld(Transform surfaceRoot, out Vector3 world) =>
            TryFindSouthPeakMouthWorld(surfaceRoot, SouthPeakMouthCenter, out world);

        /// <summary>peakIndex 0=west (Cave W), 1=center (Cave C), 2=east (Cave E) — south peak annex tile order.</summary>
        public static bool TryFindSouthPeakMouthWorld(Transform surfaceRoot, int peakIndex, out Vector3 world)
        {
            world = Vector3.zero;
            if (surfaceRoot == null)
                return false;

            var openings = surfaceRoot.Find(SurfaceWorldPaths.CaveOpeningsName);
            if (openings == null)
                return false;

            var markerName = peakIndex == 0 ? MarkerName : $"{MarkerNamePrefix}{peakIndex}";
            var child = openings.Find(markerName);
            if (child == null)
            {
                for (var i = 0; i < openings.childCount; i++)
                {
                    var c = openings.GetChild(i);
                    if (c.name != markerName)
                        continue;
                    child = c;
                    break;
                }
            }

            if (child == null)
                return false;

            world = child.position;
            return true;
        }

        static void QueueApplyLegacySingle(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            int seed,
            Action onComplete)
        {
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    if (!TryPickMouthSiteLegacy(mainTerrain, ground, seed, out var tile, out var mouthWorld, out var mouthForward))
                    {
                        CaveBuildEditorLog.LogSurfaceWarning(
                            "[Surface] Mountain wilderness cave mouth — no foothill/peak tile; skipping.");
                        onComplete?.Invoke();
                        return;
                    }

                    var intoMountain = mouthForward;
                    intoMountain.y = 0f;
                    if (intoMountain.sqrMagnitude < 0.01f)
                        intoMountain = Vector3.back;
                    CaveTerrainCarveUtility.CarveCliffTunnelMouth(
                        tile,
                        mouthWorld,
                        intoMountain,
                        widthMeters: WildernessMouthRadiusMeters * 2.4f,
                        heightMeters: WildernessMouthRadiusMeters * 1.6f,
                        depthMeters: WildernessMouthDepthMeters + 8f);

                    FlattenApproachBench(tile, mouthWorld, mouthForward);
                    EnsureMarker(surfaceRoot, mouthWorld, mouthForward, 0);
                    tile.Flush();

                    var reshapeTiles = new[] { tile };
                    SurfaceOuterRingMountainsAuthor.QueueDenoiseWildernessTiles(
                        mainTerrain,
                        reshapeTiles,
                        () =>
                            SurfaceTerrainTileExpansion.QueueStitchMountainWildernessTileSeams(
                                mainTerrain,
                                tile,
                                () => CaveBuildActionPacing.QueueWhenIdle(
                                    onComplete,
                                    CaveBuildPipelineDomains.QueueLabel("mountain wilderness cave mouth — done"))));
                },
                CaveBuildPipelineDomains.QueueLabel("mountain wilderness cave mouth — carve"));
        }

        static bool TryPickSouthPeakMouthSite(
            Terrain tile,
            SceneGroundInfo ground,
            Terrain mainTerrain,
            int seed,
            int peakIndex,
            out Vector3 mouthWorld,
            out Vector3 mouthForward)
        {
            mouthWorld = Vector3.zero;
            mouthForward = Vector3.forward;

            if (tile?.terrainData == null)
                return false;

            var playCenter = SurfaceTerrainTileExpansion.ResolvePlayDiskCenterXZ(ground, mainTerrain);
            var tileOrigin = tile.transform.position;
            var size = tile.terrainData.size;
            var tileCenter = tileOrigin + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);

            var toPlay = playCenter - tileCenter;
            toPlay.y = 0f;
            if (toPlay.sqrMagnitude < 4f)
                toPlay = Vector3.forward;
            // Marker + tunnel axis point INTO the mountain (south), not toward play.
            mouthForward = (-toPlay).normalized;

            // North edge of south peak tile (cliff face toward play).
            var northZ = tileOrigin.z + size.z * 0.88f;
            var mouthX = peakIndex switch
            {
                0 => tileOrigin.x + size.x * 0.50f,
                1 => tileOrigin.x + size.x * 0.28f,
                _ => tileOrigin.x + size.x * 0.72f,
            };

            var rng = new System.Random(seed + 77109 + peakIndex * 997);
            mouthX += ((float)rng.NextDouble() - 0.5f) * size.x * 0.04f;
            northZ -= size.z * (0.04f + (float)rng.NextDouble() * 0.03f);

            mouthX = Mathf.Clamp(mouthX, tileOrigin.x + size.x * 0.2f, tileOrigin.x + size.x * 0.8f);
            northZ = Mathf.Clamp(northZ, tileOrigin.z + size.z * 0.55f, tileOrigin.z + size.z * 0.9f);

            var sample = new Vector3(mouthX, 0f, northZ);
            var groundY = tile.SampleHeight(sample) + tileOrigin.y;
            mouthWorld = new Vector3(mouthX, groundY, northZ);
            return true;
        }

        static bool TryPickMouthSiteLegacy(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            int seed,
            out Terrain tile,
            out Vector3 mouthWorld,
            out Vector3 mouthForward)
        {
            tile = null;
            mouthWorld = Vector3.zero;
            mouthForward = Vector3.forward;

            var candidates = new List<Terrain>();
            candidates.AddRange(SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain));
            candidates.AddRange(SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain));
            if (candidates.Count == 0)
                return false;

            var rng = new System.Random(seed + 77109);
            tile = candidates[rng.Next(candidates.Count)];
            return tile != null &&
                   TryPickSouthPeakMouthSite(tile, ground, mainTerrain, seed, 0, out mouthWorld, out mouthForward);
        }

        static void CarveSummitApproach(Terrain terrain, Vector3 mouthWorld, Vector3 mouthForward, int seed)
        {
            if (terrain?.terrainData == null)
                return;

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var forward = mouthForward;
            forward.y = 0f;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var rng = new System.Random(seed + 99173);
            var steps = 28;
            var riseNorm = 0.14f / Mathf.Max(size.y, 1f);

            for (var s = 0; s < steps; s++)
            {
                var t = s / (float)(steps - 1);
                var center = mouthWorld + forward * (6f + t * 42f) + right * ((float)rng.NextDouble() - 0.5f) * 8f;
                var wx = center.x;
                var wz = center.z;
                var nx = Mathf.Clamp01((wx - origin.x) / size.x);
                var nz = Mathf.Clamp01((wz - origin.z) / size.z);
                var gx = Mathf.RoundToInt(nx * (res - 1));
                var gz = Mathf.RoundToInt(nz * (res - 1));
                var rad = Mathf.Max(3, Mathf.RoundToInt(5f + t * 4f));
                for (var dz = -rad; dz <= rad; dz++)
                {
                    for (var dx = -rad; dx <= rad; dx++)
                    {
                        var x = gx + dx;
                        var z = gz + dz;
                        if (x < 0 || z < 0 || x >= res || z >= res)
                            continue;
                        if (dx * dx + dz * dz > rad * rad)
                            continue;
                        heights[z, x] = Mathf.Clamp01(heights[z, x] + riseNorm * (1f - t * 0.35f));
                    }
                }
            }

            CaveEditorUndo.RecordObject(data, "Summit approach");
            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        static void FlattenApproachBench(Terrain terrain, Vector3 mouthWorld, Vector3 mouthForward)
        {
            var approach = mouthForward;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f)
                approach = Vector3.forward;
            approach.Normalize();

            var points = new Vector3[4];
            for (var i = 0; i < points.Length; i++)
            {
                var t = (i / (float)(points.Length - 1)) * 2f - 1f;
                points[i] = mouthWorld - approach * SurfacePrimaryMouthTerrainAuthor.ApproachBenchRadiusMeters +
                            Vector3.Cross(Vector3.up, approach) * t * 2.5f;
            }

            SurfaceTerrainRadialAuthor.FlattenTrailBench(
                terrain,
                points,
                SurfacePrimaryMouthTerrainAuthor.ApproachBenchRadiusMeters,
                0.18f);
        }

        static void EnsureMarker(Transform surfaceRoot, Vector3 mouthWorld, Vector3 forward, int index)
        {
            if (surfaceRoot == null)
                return;

            var openings = surfaceRoot.Find(SurfaceWorldPaths.CaveOpeningsName);
            if (openings == null)
                openings = EnvironmentSceneUtility.GetOrCreateChild(surfaceRoot, SurfaceWorldPaths.CaveOpeningsName).transform;

            var markerName = index == 0 ? MarkerName : $"{MarkerNamePrefix}{index}";
            Transform existing = null;
            for (var i = 0; i < openings.childCount; i++)
            {
                var child = openings.GetChild(i);
                if (child.name == markerName)
                {
                    existing = child;
                    break;
                }
            }

            var go = existing != null ? existing.gameObject : new GameObject(markerName);
            if (existing == null)
            {
                go.transform.SetParent(openings, false);
                go.AddComponent<SurfaceCaveOpeningMarker>();
            }

            go.transform.position = mouthWorld;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var marker = go.GetComponent<SurfaceCaveOpeningMarker>();
            if (marker == null)
                return;

            marker.isPrimaryEntrance = index == 0;
            marker.sectorIndex = SectorForPeakIndex(index);
            marker.suggestedDepthMeters = index switch
            {
                0 => 18f,
                1 => 26f,
                _ => 32f,
            };
        }
    }
}
#endif
