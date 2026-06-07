#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Summit dressing on peak-ring plateaus — rounded Appalachian knolls + CC0 rock stacks (flat plaza optional).
    /// </summary>
    static class SurfacePeakSummitCapAuthor
    {
        const float FlatVarianceNormThreshold = 0.0012f;
        const float PlazaRadiusMeters = 26f;
        const float PlazaFlattenStrength = 0f;
        const float KnollRadiusMeters = 32f;
        const float KnollRiseMeters = 22f;
        const float SummitDomeRadiusMeters = 18f;
        const float SummitDomeRiseMeters = 28f;
        const float SpokeHalfWidthMeters = 5.5f;
        const float SpokeFlattenStrength = 0f;
        const int AspectKnollCount = 4;
        const int SummitRockStackCount = 5;

        static readonly string[] SummitRockIds = { "K01", "K02", "K03", "K04", "K05", "K06", "K07", "K08" };

        public static void QueueApplyPeakSummitCaps(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Vector3 playCenterWorld,
            Action onComplete)
        {
            if (mainTerrain?.terrainData == null || request == null || !request.UseOuterRingMountains ||
                !request.UsePeakSummitCap)
            {
                onComplete?.Invoke();
                return;
            }

            var peaks = SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain);
            if (peaks.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var labyrinthCenter = ResolveLabyrinthCenterWorld(surfaceRoot, playCenterWorld);
            var mode = request.UseFlatSummitPlaza ? "plaza + knolls" : "rounded domes + rock stacks";

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Summit dressing — {peaks.Length} peak tile(s): {mode}.",
                forceUnityConsole: true);

            QueueCapTileAtIndex(mainTerrain, request, surfaceRoot, peaks, 0, playCenterWorld, labyrinthCenter, onComplete);
        }

        static Vector3 ResolveLabyrinthCenterWorld(Transform surfaceRoot, Vector3 playCenterWorld)
        {
            if (surfaceRoot != null)
            {
                var labRoot = surfaceRoot.Find(SurfaceWorldPaths.MountainLabyrinthName);
                if (labRoot != null)
                {
                    for (var i = 0; i < labRoot.childCount; i++)
                    {
                        var child = labRoot.GetChild(i);
                        if (child.name.Contains("Center", StringComparison.Ordinal))
                            return child.position;
                    }
                }
            }

            return playCenterWorld + Vector3.back * 420f;
        }

        static void QueueCapTileAtIndex(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Terrain[] peaks,
            int index,
            Vector3 playCenterWorld,
            Vector3 labyrinthCenterWorld,
            Action onComplete)
        {
            if (index >= peaks.Length)
            {
                onComplete?.Invoke();
                return;
            }

            var tile = peaks[index];
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    if (tile != null &&
                        TryApplySummitHub(
                            tile,
                            mainTerrain,
                            surfaceRoot,
                            request,
                            index,
                            playCenterWorld,
                            labyrinthCenterWorld))
                    {
                        tile.Flush();
                        SurfaceTerrainTileExpansion.QueueStitchMountainWildernessTileSeams(
                            mainTerrain,
                            tile,
                            () => QueueCapTileAtIndex(
                                mainTerrain,
                                request,
                                surfaceRoot,
                                peaks,
                                index + 1,
                                playCenterWorld,
                                labyrinthCenterWorld,
                                onComplete));
                        return;
                    }

                    QueueCapTileAtIndex(
                        mainTerrain,
                        request,
                        surfaceRoot,
                        peaks,
                        index + 1,
                        playCenterWorld,
                        labyrinthCenterWorld,
                        onComplete);
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel($"summit hub {index + 1}/{peaks.Length}"));
        }

        static bool TryApplySummitHub(
            Terrain tile,
            Terrain mainTerrain,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            int tileIndex,
            Vector3 playCenterWorld,
            Vector3 labyrinthCenterWorld)
        {
            if (tile?.terrainData == null || mainTerrain?.terrainData == null || request == null)
                return false;

            if (!TryFindSummitPlateau(tile, out var plateauCenter, out var plateauHeightWorld))
                return false;

            var seed = request.Seed;
            var rng = new System.Random(seed + 8803 + tileIndex * 131);
            var data = tile.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var origin = tile.transform.position;
            var size = data.size;

            if (request.UseFlatSummitPlaza)
            {
                StampWalkablePlaza(heights, res, size, origin, plateauCenter, plateauHeightWorld);
            }
            else
            {
                SculptGaussianKnoll(
                    heights,
                    res,
                    size,
                    origin,
                    plateauCenter,
                    SummitDomeRadiusMeters,
                    SummitDomeRiseMeters / Mathf.Max(size.y, 1f),
                    seed + tileIndex * 17);

                for (var k = 0; k < 3; k++)
                {
                    var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var dist = SummitDomeRadiusMeters * (0.25f + (float)rng.NextDouble() * 0.45f);
                    var bumpWorld = plateauCenter + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                    SculptGaussianKnoll(
                        heights,
                        res,
                        size,
                        origin,
                        bumpWorld,
                        KnollRadiusMeters * (0.22f + (float)rng.NextDouble() * 0.1f),
                        KnollRiseMeters * (0.35f + (float)rng.NextDouble() * 0.25f) / Mathf.Max(size.y, 1f),
                        rng.Next());
                }
            }

            for (var i = 0; i < AspectKnollCount; i++)
            {
                var angle = i * Mathf.PI * 2f / AspectKnollCount + (float)rng.NextDouble() * 0.35f;
                var dist = KnollRadiusMeters * (1.1f + (float)rng.NextDouble() * 0.55f);
                var knollWorld = plateauCenter + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                var rise = KnollRiseMeters * (0.75f + (float)rng.NextDouble() * 0.55f);
                SculptGaussianKnoll(
                    heights,
                    res,
                    size,
                    origin,
                    knollWorld,
                    KnollRadiusMeters * (0.38f + (float)rng.NextDouble() * 0.12f),
                    rise / Mathf.Max(size.y, 1f),
                    rng.Next());
            }

            CaveEditorUndo.RecordObject(data, "Summit hub height");
            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(tile, 0, 0, heights, requestDelayLod: false);
            tile.Flush();

            if (request.UseFlatSummitPlaza)
            {
                CarveSpokeTrail(tile, plateauCenter, playCenterWorld);
                CarveSpokeTrail(tile, plateauCenter, labyrinthCenterWorld);
            }

            StackSummitRocks(surfaceRoot, tile, plateauCenter, seed + tileIndex);
            PlaceSummitHubMarker(surfaceRoot, tile, plateauCenter, plateauHeightWorld, seed + tileIndex);

            var label = request.UseFlatSummitPlaza
                ? "summit hub (plaza + knolls + spokes)"
                : "summit hub (rounded dome + knolls + rock stack)";
            CaveBuildEditorLog.LogSurface($"[Surface] {tile.name} — {label}.", forceUnityConsole: false);
            return true;
        }

        static void StackSummitRocks(Transform surfaceRoot, Terrain tile, Vector3 plateauCenter, int seed)
        {
            Cc0ContentImportUtility.EnsureAll(importItems: true);
            var prefabs = LoadSummitRockPrefabs();
            if (prefabs.Count == 0)
                return;

            var root = EnsureSummitRockRoot(surfaceRoot);
            var rng = new System.Random(seed + 0x524F434B);
            var originY = tile.transform.position.y;
            var baseY = tile.SampleHeight(plateauCenter) + originY;

            for (var stack = 0; stack < SummitRockStackCount; stack++)
            {
                var angle = stack * (360f / SummitRockStackCount) + (float)rng.NextDouble() * 22f;
                var rad = angle * Mathf.Deg2Rad;
                var dist = 4f + (float)rng.NextDouble() * 14f;
                var wx = plateauCenter.x + Mathf.Cos(rad) * dist;
                var wz = plateauCenter.z + Mathf.Sin(rad) * dist;
                var wy = tile.SampleHeight(new Vector3(wx, 0f, wz)) + originY;

                var layers = 1 + rng.Next(3);
                for (var layer = 0; layer < layers; layer++)
                {
                    var prefab = prefabs[rng.Next(prefabs.Count)];
                    if (prefab == null)
                        continue;

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                    if (instance == null)
                        continue;

                    CaveEditorUndo.RegisterCreated(instance, "Summit rock stack");
                    instance.name = $"SummitRock_{tile.name}_{stack:D2}_L{layer}";
                    var scale = 0.75f + (float)rng.NextDouble() * 1.6f;
                    instance.transform.position = new Vector3(
                        wx + ((float)rng.NextDouble() - 0.5f) * 2f,
                        wy + layer * scale * 0.55f,
                        wz + ((float)rng.NextDouble() - 0.5f) * 2f);
                    instance.transform.rotation = Quaternion.Euler(
                        (float)rng.NextDouble() * 12f - 6f,
                        (float)rng.NextDouble() * 360f,
                        (float)rng.NextDouble() * 12f - 6f);
                    instance.transform.localScale = Vector3.one * scale;
                }
            }
        }

        static List<GameObject> LoadSummitRockPrefabs()
        {
            var list = new List<GameObject>();
            foreach (var id in SummitRockIds)
            {
                var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
                if (prefab != null && !list.Contains(prefab))
                    list.Add(prefab);
            }

            return list;
        }

        static Transform EnsureSummitRockRoot(Transform surfaceRoot)
        {
            if (surfaceRoot == null)
                return null;

            var existing = surfaceRoot.Find(SurfaceMountainPeakDressingAuthor.PeakRocksRootName);
            if (existing != null)
                return existing;

            var go = new GameObject(SurfaceMountainPeakDressingAuthor.PeakRocksRootName);
            CaveEditorUndo.RegisterCreated(go, "Summit rocks root");
            go.transform.SetParent(surfaceRoot, false);
            return go.transform;
        }

        static void StampWalkablePlaza(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 centerWorld,
            float targetHeightWorld)
        {
            var targetNorm = (targetHeightWorld - origin.y) / Mathf.Max(size.y, 1f);
            var r2 = PlazaRadiusMeters * PlazaRadiusMeters;
            var stepX = size.x / Mathf.Max(1, res - 1);
            var stepZ = size.z / Mathf.Max(1, res - 1);
            var xMin = Mathf.Clamp(Mathf.FloorToInt((centerWorld.x - PlazaRadiusMeters - origin.x) / stepX), 0, res - 1);
            var xMax = Mathf.Clamp(Mathf.CeilToInt((centerWorld.x + PlazaRadiusMeters - origin.x) / stepX), 0, res - 1);
            var zMin = Mathf.Clamp(Mathf.FloorToInt((centerWorld.z - PlazaRadiusMeters - origin.z) / stepZ), 0, res - 1);
            var zMax = Mathf.Clamp(Mathf.CeilToInt((centerWorld.z + PlazaRadiusMeters - origin.z) / stepZ), 0, res - 1);

            for (var z = zMin; z <= zMax; z++)
            {
                for (var x = xMin; x <= xMax; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var dx = wx - centerWorld.x;
                    var dz = wz - centerWorld.z;
                    var d2 = dx * dx + dz * dz;
                    if (d2 > r2)
                        continue;

                    var t = 1f - d2 / r2;
                    heights[z, x] = Mathf.Lerp(heights[z, x], targetNorm, PlazaFlattenStrength * t);
                }
            }
        }

        static void CarveSpokeTrail(Terrain tile, Vector3 fromWorld, Vector3 toWorld)
        {
            var a = fromWorld;
            var b = toWorld;
            b.y = a.y;
            if ((b - a).sqrMagnitude < 64f)
                return;

            var points = new Vector3[5];
            for (var i = 0; i < points.Length; i++)
            {
                var t = i / (float)(points.Length - 1);
                points[i] = Vector3.Lerp(a, b, t);
                points[i].y = tile.SampleHeight(points[i]) + tile.transform.position.y;
            }

            SurfaceTerrainRadialAuthor.FlattenTrailBench(
                tile,
                points,
                SpokeHalfWidthMeters,
                SpokeFlattenStrength);
        }

        static void PlaceSummitHubMarker(
            Transform surfaceRoot,
            Terrain tile,
            Vector3 plateauCenter,
            float plateauHeightWorld,
            int seed)
        {
            if (surfaceRoot == null)
                return;

            var root = EnvironmentSceneUtility.GetOrCreateChild(surfaceRoot, SurfaceWorldPaths.SummitHubsName);
            var go = new GameObject($"SummitHub_{tile.name}_{seed & 0xFFFF}");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(plateauCenter.x, plateauHeightWorld, plateauCenter.z);
        }

        static int SculptGaussianKnoll(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 peakWorld,
            float radiusMeters,
            float addNormalized,
            int seed)
        {
            var r2 = radiusMeters * radiusMeters;
            var touched = 0;
            var stepX = size.x / Mathf.Max(1, res - 1);
            var stepZ = size.z / Mathf.Max(1, res - 1);
            var xMin = Mathf.Clamp(Mathf.FloorToInt((peakWorld.x - radiusMeters - origin.x) / stepX), 0, res - 1);
            var xMax = Mathf.Clamp(Mathf.CeilToInt((peakWorld.x + radiusMeters - origin.x) / stepX), 0, res - 1);
            var zMin = Mathf.Clamp(Mathf.FloorToInt((peakWorld.z - radiusMeters - origin.z) / stepZ), 0, res - 1);
            var zMax = Mathf.Clamp(Mathf.CeilToInt((peakWorld.z + radiusMeters - origin.z) / stepZ), 0, res - 1);

            for (var z = zMin; z <= zMax; z++)
            {
                for (var x = xMin; x <= xMax; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var dx = wx - peakWorld.x;
                    var dz = wz - peakWorld.z;
                    var d2 = dx * dx + dz * dz;
                    if (d2 > r2)
                        continue;

                    var t = 1f - d2 / r2;
                    var bump = addNormalized * t * t;
                    var before = heights[z, x];
                    heights[z, x] = Mathf.Clamp01(before + bump);
                    if (Mathf.Abs(heights[z, x] - before) > 0.0001f)
                        touched++;
                }
            }

            return touched;
        }

        static bool TryFindSummitPlateau(Terrain tile, out Vector3 plateauCenterWorld, out float plateauHeightWorld)
        {
            plateauCenterWorld = tile.transform.position;
            plateauHeightWorld = tile.transform.position.y;
            var data = tile.terrainData;
            var res = data.heightmapResolution;
            var margin = Mathf.Max(4, res / 6);
            var x0 = margin;
            var x1 = res - margin;
            var z0 = margin;
            var z1 = res - margin;
            if (x1 <= x0 || z1 <= z0)
                return false;

            var slice = data.GetHeights(x0, z0, x1 - x0, z1 - z0);
            var min = float.MaxValue;
            var max = float.MinValue;
            var sumX = 0f;
            var sumZ = 0f;
            var sumH = 0f;
            var count = 0;
            for (var z = 0; z < slice.GetLength(0); z++)
            {
                for (var x = 0; x < slice.GetLength(1); x++)
                {
                    var h = slice[z, x];
                    min = Mathf.Min(min, h);
                    max = Mathf.Max(max, h);
                    sumX += x0 + x;
                    sumZ += z0 + z;
                    sumH += h;
                    count++;
                }
            }

            if (count == 0 || max - min > FlatVarianceNormThreshold)
                return false;

            var stepX = data.size.x / (res - 1);
            var stepZ = data.size.z / (res - 1);
            var avgNorm = sumH / count;
            plateauCenterWorld = tile.transform.position + new Vector3(sumX / count * stepX, 0f, sumZ / count * stepZ);
            plateauHeightWorld = tile.transform.position.y + avgNorm * data.size.y;
            return true;
        }
    }
}
#endif
