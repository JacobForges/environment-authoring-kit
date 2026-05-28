#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// LiDAR / DEM as macro structure guide — not a heightmap photocopy. Procedural FBM is the primary landscape;
    /// elevation grid and hillshade only bias slope, basins, and ridge trends.
    /// </summary>
    public static class SurfaceTerrainLidarCreativeGuide
    {
        /// <summary>Max blend toward LiDAR-derived structure (remainder stays procedural).</summary>
        public const float MaxGuideInfluence = 0.28f;

        /// <summary>How much of the play disk receives guide + creative blend (not a tiny inner preserve only).</summary>
        public const float PlayDiskBlendStrength = 0.62f;

        /// <summary>Domain warp as fraction of surface extent — breaks 1:1 map fingerprint.</summary>
        public const float UvWarpExtentFraction = 0.14f;

        public static Vector2 WarpDemUv(float u, float v, float wx, float wz, int seed, float extentMeters)
        {
            var warp = Mathf.Max(12f, extentMeters * UvWarpExtentFraction);
            var n0 = Hash01(wx, wz, seed + 17) * 2f - 1f;
            var n1 = Hash01(wx, wz, seed + 41) * 2f - 1f;
            var n2 = Hash01(wx * 0.37f, wz * 0.29f, seed + 73) * 2f - 1f;
            var du = (n0 * 0.55f + n2 * 0.25f) * warp / Mathf.Max(extentMeters, 1f);
            var dv = (n1 * 0.55f - n2 * 0.25f) * warp / Mathf.Max(extentMeters, 1f);
            return new Vector2(Mathf.Clamp01(u + du), Mathf.Clamp01(v + dv));
        }

        /// <summary>Seed-locked playable height from FBM (primary creative surface).</summary>
        public static float SampleCreativeHeightNorm(
            float wx,
            float wz,
            int seed,
            float anchorNorm,
            float terrainHeightMeters,
            bool mountains)
        {
            var fbm = SurfaceTerrainCenteredAuthor.SampleWorldFbm(wx, wz, seed);
            var micro = Mathf.PerlinNoise(wx * 0.011f + seed * 0.31f, wz * 0.011f - seed * 0.19f) * 2f - 1f;
            var ampMeters = mountains ? 32f : 20f;
            var microMeters = 8f;
            var normAmp = ampMeters / Mathf.Max(terrainHeightMeters, 1f);
            var microNorm = microMeters / Mathf.Max(terrainHeightMeters, 1f);
            return Mathf.Clamp01(anchorNorm + fbm * normAmp * 0.72f + micro * microNorm * 0.55f);
        }

        /// <summary>Smoothed elevation-grid sample — macro highs/lows only, not pixel cliffs.</summary>
        public static float SampleStructuralGuideNormFromElev(
            SurfaceDemGeoreferenceAuthor.ElevationGridFile grid,
            float u,
            float v,
            float anchorNorm,
            float terrainHeightMeters,
            float maxGuideReliefMeters)
        {
            if (grid?.values == null || grid.width < 2 || grid.height < 2)
                return anchorNorm;

            var elev = SampleElevSmoothed(grid, u, v);
            if (float.IsNaN(elev))
                return anchorNorm;

            var range = Mathf.Max(0.5f, grid.maxElevationMeters - grid.minElevationMeters);
            var norm = (elev - grid.minElevationMeters) / range;
            var bias = (norm - 0.5f) * 0.55f;
            var normDelta = bias * (maxGuideReliefMeters / Mathf.Max(terrainHeightMeters, 1f)) * 2.1f;
            return Mathf.Clamp01(anchorNorm + normDelta);
        }

        /// <summary>Hillshade luminance as weak slope hint when no elevation grid exists.</summary>
        public static float SampleStructuralGuideNormFromHillshade(
            float luminance,
            float anchorNorm,
            float terrainHeightMeters,
            float maxGuideReliefMeters)
        {
            var bias = (luminance - 0.5f) * 0.45f;
            var normDelta = bias * (maxGuideReliefMeters / Mathf.Max(terrainHeightMeters, 1f));
            return Mathf.Clamp01(anchorNorm + normDelta);
        }

        /// <summary>Blend procedural base with LiDAR structure guide.</summary>
        public static float ComposeTargetHeightNorm(
            float creativeNorm,
            float guideNorm,
            float guideWeight01)
        {
            var w = Mathf.Clamp01(guideWeight01) * MaxGuideInfluence;
            return Mathf.Clamp01(Mathf.Lerp(creativeNorm, guideNorm, w));
        }

        static float SampleElevSmoothed(
            SurfaceDemGeoreferenceAuthor.ElevationGridFile grid,
            float u,
            float v)
        {
            var w = grid.width;
            var h = grid.height;
            var fx = Mathf.Clamp01(u) * (w - 1);
            var fy = Mathf.Clamp01(v) * (h - 1);
            var sum = 0f;
            var count = 0;
            for (var oy = -1; oy <= 1; oy++)
            {
                for (var ox = -1; ox <= 1; ox++)
                {
                    var ix = Mathf.Clamp(Mathf.RoundToInt(fx) + ox, 0, w - 1);
                    var iy = Mathf.Clamp(Mathf.RoundToInt(fy) + oy, 0, h - 1);
                    var e = grid.values[iy * w + ix];
                    if (float.IsNaN(e) || e <= grid.nodata + 0.01f)
                        continue;
                    sum += e;
                    count++;
                }
            }

            return count > 0 ? sum / count : float.NaN;
        }

        static float Hash01(float wx, float wz, int seed)
        {
            var h = Mathf.Sin(wx * 12.9898f + wz * 78.233f + seed * 0.137f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }
    }
}
#endif
