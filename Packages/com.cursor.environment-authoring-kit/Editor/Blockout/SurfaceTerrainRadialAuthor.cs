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
            float preserveInnerRadiusMeters = -1f)
        {
            if (terrain == null || terrain.terrainData == null)
                return;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface radial terrain");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var rng = new System.Random(seed);

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
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

                    // Mountains/roads outside playable grader band (0.08–1.05× extent) — avoids crater/spike clusters.
                    if (mountains && normDist > 0.58f && normDist < 0.95f && sectorWeight > 0.55f)
                    {
                        var peak = Mathf.SmoothStep(0f, 1f, (normDist - 0.58f) / 0.37f);
                        peak *= sectorWeight;
                        h = Mathf.Max(h, h + peak * 0.032f);
                    }

                    // Outer-band ponds only — outside core play disk (normDist &gt; 0.72) to avoid heightfield_no_craters.
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

            data.SetHeights(0, 0, heights);
        }

        public static void FlattenTrailBench(
            Terrain terrain,
            Vector3[] worldPoints,
            float halfWidthMeters,
            float flattenStrength = 0.08f)
        {
            if (terrain == null || worldPoints == null || worldPoints.Length < 2)
                return;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface trail bench");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;

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
                    var target = SampleTrailHeight(worldPoints, p);
                    var norm = target / size.y;
                    heights[y, x] = Mathf.Lerp(heights[y, x], norm, flattenStrength * t);
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
