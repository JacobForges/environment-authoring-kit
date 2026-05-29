#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Twin elevated ridges + opposing walkable ascent/descent trails (enhancement hook).</summary>
    public static class SurfaceMountainTrailEnhancement
    {
        const float PeakRadiusMeters = 38f;
        const float PeakHeightNormalized = 0.11f;

        public static string Apply(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 center,
            float extentMeters,
            int seed)
        {
            if (terrain == null || terrain.terrainData == null)
                return "skipped (no terrain)";

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var rng = new System.Random(seed + 6102);

            var right = Vector3.Cross(Vector3.up, Vector3.forward).normalized;
            var caveRoot = GameObject.Find("UndergroundCaveSystem");
            if (caveRoot != null)
            {
                var mouth = caveRoot.transform.position - center;
                mouth.y = 0f;
                if (mouth.sqrMagnitude > 4f)
                    right = Vector3.Cross(Vector3.up, mouth.normalized).normalized;
            }

            var peakOffset = extentMeters * (0.22f + (float)rng.NextDouble() * 0.12f);
            SculptGaussianPeak(heights, res, size, origin, center + right * peakOffset, PeakRadiusMeters, PeakHeightNormalized, seed + 11);
            SculptGaussianPeak(heights, res, size, origin, center - right * peakOffset, PeakRadiusMeters * 0.92f, PeakHeightNormalized * 0.88f, seed + 29);

            data.SetHeights(0, 0, heights);

            var trailsRoot = surfaceRoot != null
                ? EnvironmentSceneUtility.GetOrCreateChild(surfaceRoot, SurfaceWorldPaths.TrailsName)
                : null;
            var trailCount = 0;
            if (trailsRoot != null)
            {
                trailCount += BuildFlankTrail(trailsRoot, terrain, center, right, extentMeters, seed + 101, rng, ascend: true);
                trailCount += BuildFlankTrail(trailsRoot, terrain, center, -right, extentMeters, seed + 202, rng, ascend: false);
            }

            SurfaceTerrainPlayRegion.FlushAllSurfaceTerrains(terrain);
            return $"twin peaks + {trailCount} flank trail(s)";
        }

        static void SculptGaussianPeak(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 peakWorld,
            float radiusMeters,
            float addNormalized,
            int seed)
        {
            var rng = new System.Random(seed);
            var r2 = radiusMeters * radiusMeters;
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var dx = wx - peakWorld.x;
                    var dz = wz - peakWorld.z;
                    var d2 = dx * dx + dz * dz;
                    if (d2 > r2)
                        continue;
                    var t = 1f - d2 / r2;
                    var bump = addNormalized * t * t * (0.85f + (float)rng.NextDouble() * 0.2f);
                    heights[z, x] = Mathf.Clamp01(heights[z, x] + bump);
                }
            }
        }

        static int BuildFlankTrail(
            Transform trailsRoot,
            Terrain terrain,
            Vector3 center,
            Vector3 flank,
            float extent,
            int seed,
            System.Random rng,
            bool ascend)
        {
            var points = new List<Vector3>();
            var steps = 6 + rng.Next(0, 4);
            var forward = flank.normalized;
            var maxDist = extent * 0.55f;

            for (var s = 0; s <= steps; s++)
            {
                var t = s / (float)steps;
                var dist = Mathf.Lerp(extent * 0.08f, maxDist, t);
                var along = center + forward * dist;
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(terrain, along.x, along.z, out _))
                    break;

                var baseY = terrain.SampleHeight(along);
                var climb = ascend ? Mathf.Lerp(0.4f, 6.5f, t) : Mathf.Lerp(5.5f, 0.5f, t);
                along.y = baseY + climb + (float)(rng.NextDouble() * 0.35);
                points.Add(along);
                SurfaceTerrainRadialAuthor.FlattenTrailBench(terrain, new[] { along }, 3.2f, 0.42f);
            }

            if (points.Count < 2)
                return 0;

            var trailGo = new GameObject($"MountainTrail_{(ascend ? "Up" : "Down")}_{seed % 1000}");
            CaveEditorUndo.RegisterCreated(trailGo, "Mountain flank trail");
            trailGo.transform.SetParent(trailsRoot, false);
            for (var i = 0; i < points.Count; i++)
            {
                var wp = new GameObject($"Waypoint_{i}");
                CaveEditorUndo.RegisterCreated(wp, "Waypoint");
                wp.transform.SetParent(trailGo.transform, false);
                wp.transform.position = points[i];
            }

            return 1;
        }
    }
}
#endif
