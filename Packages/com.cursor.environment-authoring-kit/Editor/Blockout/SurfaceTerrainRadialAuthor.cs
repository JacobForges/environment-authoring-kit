using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Stamps heightmap features radiating from the ground anchor (mountains, ponds, trail benches).</summary>
    static class SurfaceTerrainRadialAuthor
    {
        public static void ApplyRadialLandscape(
            Terrain terrain,
            Vector3 centerWorld,
            int directionCount,
            float extentMeters,
            int seed,
            bool mountains,
            bool water,
            bool roads,
            float preserveInnerRadiusMeters = -1f,
            System.Action onComplete = null)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                var dirCount = directionCount;
                CaveBuildMicroTerrainHeightmap.QueueMutateRowBands(
                    terrain,
                    "Surface radial terrain",
                    CaveBuildPipelineDomains.QueueLabel("radial landscape"),
                    (heights, rowStart, res, origin, size) =>
                    {
                        MutateRadialLandscapeBand(
                            heights,
                            rowStart,
                            res,
                            origin,
                            size,
                            centerWorld,
                            dirCount,
                            extentMeters,
                            seed,
                            mountains,
                            water,
                            roads,
                            preserveInnerRadiusMeters);
                    },
                    onComplete ?? (() => { }));
                return;
            }

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface radial terrain");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            MutateRadialLandscapeBand(
                heights,
                0,
                res,
                origin,
                size,
                centerWorld,
                directionCount,
                extentMeters,
                seed,
                mountains,
                water,
                roads,
                preserveInnerRadiusMeters);

            data.SetHeights(0, 0, heights);
            onComplete?.Invoke();
        }

        static void MutateRadialLandscapeBand(
            float[,] heights,
            int rowStart,
            int res,
            Vector3 origin,
            Vector3 size,
            Vector3 centerWorld,
            int directionCount,
            float extentMeters,
            int seed,
            bool mountains,
            bool water,
            bool roads,
            float preserveInnerRadiusMeters)
        {
            var rng = new System.Random(seed);
            for (var y = 0; y < heights.GetLength(0); y++)
            {
                var gy = rowStart + y;
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + gy / (float)(res - 1) * size.z;
                    var dx = wx - centerWorld.x;
                    var dz = wz - centerWorld.z;
                    var dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > extentMeters * 1.15f)
                        continue;

                    if (preserveInnerRadiusMeters > 0f && dist < preserveInnerRadiusMeters)
                        continue;

                    var angle = Mathf.Atan2(dz, dx);
                    var sector = Mathf.RoundToInt((angle / (Mathf.PI * 2f) + 1f) * directionCount) % directionCount;
                    var sectorAngle = sector / (float)directionCount * Mathf.PI * 2f;
                    var angleDelta = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, sectorAngle * Mathf.Rad2Deg));
                    var sectorWeight = Mathf.Clamp01(1f - angleDelta / (180f / directionCount));

                    var h = heights[y, x];
                    var normDist = dist / extentMeters;

                    if (mountains && normDist > 0.58f && normDist < 0.95f && sectorWeight > 0.55f)
                    {
                        var peak = Mathf.SmoothStep(0f, 1f, (normDist - 0.58f) / 0.37f);
                        peak *= sectorWeight;
                        h = Mathf.Max(h, h + peak * 0.032f);
                    }

                    if (water && normDist > 0.96f && normDist < 0.99f && sector % 2 == 0)
                    {
                        var bowl = (1f - Mathf.Abs(normDist - 0.975f) / 0.025f) * 0.00004f;
                        h = Mathf.Max(0f, h - bowl);
                    }

                    if (roads && normDist > 0.64f && normDist < 0.78f && angleDelta < 12f)
                    {
                        var road = Mathf.SmoothStep(0.78f, 0.64f, normDist) * 0.008f;
                        h = Mathf.Max(0f, h - road);
                    }

                    var micro = normDist < 0.58f ? 0f : (float)(rng.NextDouble() - 0.5) * 0.0006f;
                    heights[y, x] = Mathf.Clamp01(h + micro);
                }
            }
        }

        public static void FlattenTrailBench(
            Terrain terrain,
            Vector3[] worldPoints,
            float halfWidthMeters,
            float flattenStrength = 0.08f,
            System.Action onComplete = null)
        {
            if (CaveBuildSurfaceTerrainLock.TryBlockSurfaceMutation("radial trail bench"))
            {
                onComplete?.Invoke();
                return;
            }

            if (terrain == null || worldPoints == null || worldPoints.Length < 2)
            {
                onComplete?.Invoke();
                return;
            }

            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                CaveBuildMicroTerrainHeightmap.QueueMutateRowBands(
                    terrain,
                    "Surface trail bench",
                    CaveBuildPipelineDomains.QueueLabel("radial trail bench"),
                    (heights, rowStart, res, origin, size) =>
                    {
                        for (var y = 0; y < heights.GetLength(0); y++)
                        {
                            var gy = rowStart + y;
                            for (var x = 0; x < res; x++)
                            {
                                var wx = origin.x + x / (float)(res - 1) * size.x;
                                var wz = origin.z + gy / (float)(res - 1) * size.z;
                                var p = new Vector3(wx, 0f, wz);
                                var dist = DistanceToPolylineXZ(p, worldPoints);
                                if (dist > halfWidthMeters)
                                    continue;

                                var t = 1f - dist / halfWidthMeters;
                                var target = SampleTrailHeight(worldPoints, p);
                                var norm = target / size.y;
                                heights[y, x] = Mathf.Lerp(heights[y, x], norm, flattenStrength * t);
                            }
                        }
                    },
                    onComplete ?? (() => { }));
                return;
            }

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface trail bench");
            var resSync = data.heightmapResolution;
            var heightsSync = data.GetHeights(0, 0, resSync, resSync);
            var sizeSync = data.size;
            var originSync = terrain.transform.position;

            for (var y = 0; y < resSync; y++)
            {
                for (var x = 0; x < resSync; x++)
                {
                    var wx = originSync.x + x / (float)(resSync - 1) * sizeSync.x;
                    var wz = originSync.z + y / (float)(resSync - 1) * sizeSync.z;
                    var p = new Vector3(wx, 0f, wz);
                    var dist = DistanceToPolylineXZ(p, worldPoints);
                    if (dist > halfWidthMeters)
                        continue;

                    var t = 1f - dist / halfWidthMeters;
                    var target = SampleTrailHeight(worldPoints, p);
                    var norm = target / sizeSync.y;
                    heightsSync[y, x] = Mathf.Lerp(heightsSync[y, x], norm, flattenStrength * t);
                }
            }

            data.SetHeights(0, 0, heightsSync);
            onComplete?.Invoke();
        }

        /// <summary>
        /// South foothill labyrinth row: keep walkable floor near existing height; raise terrain in the wall band outside the corridor.
        /// </summary>
        public static void SculptLabyrinthWalkwayWithRaisedWalls(
            Terrain terrain,
            Vector3[] worldPoints,
            float corridorHalfWidthMeters,
            float wallBandMeters,
            float wallRaiseMeters,
            float walkwaySmoothStrength = 0.07f)
        {
            if (terrain?.terrainData == null || worldPoints == null || worldPoints.Length < 2)
                return;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Labyrinth raised walls");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var raiseNorm = Mathf.Clamp(wallRaiseMeters / Mathf.Max(size.y, 1f), 0.002f, 0.08f);
            var outerBand = corridorHalfWidthMeters + Mathf.Max(wallBandMeters, 1f);

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var p = new Vector3(wx, 0f, wz);
                    var dist = DistanceToPolylineXZ(p, worldPoints);

                    if (dist <= corridorHalfWidthMeters)
                    {
                        if (walkwaySmoothStrength <= 0.0001f)
                            continue;

                        var tWalk = 1f - dist / corridorHalfWidthMeters;
                        var h = heights[y, x];
                        var avg = h;
                        var n = 0;
                        if (x > 0)
                        {
                            avg += heights[y, x - 1];
                            n++;
                        }

                        if (x < res - 1)
                        {
                            avg += heights[y, x + 1];
                            n++;
                        }

                        if (y > 0)
                        {
                            avg += heights[y - 1, x];
                            n++;
                        }

                        if (y < res - 1)
                        {
                            avg += heights[y + 1, x];
                            n++;
                        }

                        if (n > 0)
                        {
                            avg /= n + 1;
                            heights[y, x] = Mathf.Lerp(h, avg, walkwaySmoothStrength * tWalk * 0.45f);
                        }

                        continue;
                    }

                    if (dist > outerBand)
                        continue;

                    var tWall = 1f - (dist - corridorHalfWidthMeters) / wallBandMeters;
                    tWall = Mathf.Clamp01(tWall);
                    heights[y, x] = Mathf.Min(1f, heights[y, x] + raiseNorm * tWall * tWall);
                }
            }

            data.SetHeights(0, 0, heights);
        }

        /// <summary>
        /// Peak-ring labyrinth: shave high-frequency bumps along a corridor without flattening to a single bowl height.
        /// </summary>
        public static void MoldMountainCorridor(
            Terrain terrain,
            Vector3[] worldPoints,
            float halfWidthMeters,
            float shaveMeters = 1.35f,
            float smoothStrength = 0.2f)
        {
            if (terrain?.terrainData == null || worldPoints == null || worldPoints.Length < 2)
                return;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Mountain labyrinth mold");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var shaveNorm = Mathf.Clamp(shaveMeters / Mathf.Max(size.y, 1f), 0.001f, 0.06f);

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var p = new Vector3(wx, 0f, wz);
                    var dist = DistanceToPolylineXZ(p, worldPoints);
                    if (dist > halfWidthMeters)
                        continue;

                    var t = 1f - dist / halfWidthMeters;
                    var h = heights[y, x];
                    var avg = h;
                    var n = 0;
                    if (x > 0)
                    {
                        avg += heights[y, x - 1];
                        n++;
                    }

                    if (x < res - 1)
                    {
                        avg += heights[y, x + 1];
                        n++;
                    }

                    if (y > 0)
                    {
                        avg += heights[y - 1, x];
                        n++;
                    }

                    if (y < res - 1)
                    {
                        avg += heights[y + 1, x];
                        n++;
                    }

                    if (n > 0)
                        avg = (avg + h) / (n + 1);

                    var ridge = Mathf.Max(avg, h);
                    var target = Mathf.Max(ridge - shaveNorm * t, avg - shaveNorm * 0.35f * t);
                    if (h > target + 0.0015f)
                        heights[y, x] = Mathf.Lerp(h, target, smoothStrength * t);
                    else if (h < avg - 0.004f)
                        heights[y, x] = Mathf.Lerp(h, avg, smoothStrength * t * 0.25f);
                }
            }

            data.SetHeights(0, 0, heights);
        }

        static float SampleTrailHeight(Vector3[] points, Vector3 xz)
        {
            var best = 0f;
            var bestD = float.MaxValue;
            for (var i = 0; i < points.Length; i++)
            {
                var d = (new Vector2(points[i].x, points[i].z) - new Vector2(xz.x, xz.z)).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = points[i].y;
                }
            }

            return best;
        }

        static float DistanceToPolylineXZ(Vector3 p, Vector3[] points)
        {
            var best = float.MaxValue;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var ab = b - a;
                ab.y = 0f;
                var ap = p - a;
                ap.y = 0f;
                var t = ab.sqrMagnitude < 0.001f ? 0f : Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
                var closest = a + ab * t;
                var d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(closest.x, closest.z));
                if (d < best)
                    best = d;
            }

            return best;
        }
    }
}
