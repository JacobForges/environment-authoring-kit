#if UNITY_EDITOR
using System;
using System.IO;
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
            var ampMeters = mountains ? 56f : 20f;
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
            if (!SurfaceHillshadeObjectMask.UseHillshadeForTerrainHeight(luminance, Color.gray))
                return anchorNorm;

            var bias = (luminance - 0.5f) * 0.45f;
            var normDelta = bias * (maxGuideReliefMeters / Mathf.Max(terrainHeightMeters, 1f));
            return Mathf.Clamp01(anchorNorm + normDelta);
        }

        /// <summary>RGB-aware — skips tree canopy / water pixels (satellite object detection).</summary>
        public static float SampleStructuralGuideNormFromHillshade(
            float luminance,
            Color rgb,
            float anchorNorm,
            float terrainHeightMeters,
            float maxGuideReliefMeters)
        {
            if (!SurfaceHillshadeObjectMask.UseHillshadeForTerrainHeight(luminance, rgb))
                return anchorNorm;

            var bias = (luminance - 0.5f) * 0.38f;
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

        /// <summary>Humanoid scale for walkable relief (meters).</summary>
        public const float CharacterHeightMeters = 2.2f;

        public struct LandscapeReliefProfile
        {
            public float MaxRidgeRiseMeters;
            public float MaxValleyCarveMeters;
            public float ResearchMicroMeters;
            public float StructuralGuideScale;
            public int ResearchEntryCount;

            public static LandscapeReliefProfile Default(int seed) =>
                new()
                {
                    MaxRidgeRiseMeters = 6f + (seed % 7) * 1.1f,
                    MaxValleyCarveMeters = 4.5f + (seed % 5) * 0.85f,
                    ResearchMicroMeters = 1.4f,
                    StructuralGuideScale = 1f,
                    ResearchEntryCount = 0,
                };
        }

        /// <summary>ResearchCache + execution brief tune ridge/carve meters (not a flat stamp target).</summary>
        public static LandscapeReliefProfile ResolveLandscapeReliefProfile(int seed)
        {
            var profile = LandscapeReliefProfile.Default(seed);
            TryApplyResearchBrief(ref profile, seed);
            return profile;
        }

        /// <summary>
        /// Procedural FBM base + LiDAR/research-guided carve (valleys) and rise (ridges) at character scale.
        /// </summary>
        public static float ApplyLandscapeCarveAndRise(
            float proceduralNorm,
            float guideNorm,
            float terrainHeightMeters,
            float guideWeight01,
            float distFromCenterMeters,
            float extentMeters,
            int seed,
            float wx,
            float wz,
            LandscapeReliefProfile profile)
        {
            var w = Mathf.Clamp01(guideWeight01);
            if (w <= 0.001f)
                return proceduralNorm;

            var structural = (guideNorm - proceduralNorm) * profile.StructuralGuideScale;
            var riseMeters = Mathf.Max(0f, structural) * profile.MaxRidgeRiseMeters * w;
            var carveMeters = Mathf.Max(0f, -structural) * profile.MaxValleyCarveMeters * w;

            var distNorm = distFromCenterMeters / Mathf.Max(extentMeters, 1f);
            var playDiskFalloff = Mathf.Clamp01(1.15f - distNorm * 0.95f);
            riseMeters *= playDiskFalloff;
            carveMeters *= playDiskFalloff;

            var micro =
                (Hash01(wx, wz, seed + 901) * 2f - 1f) *
                profile.ResearchMicroMeters *
                w *
                (0.35f + profile.ResearchEntryCount * 0.02f);
            var ridgeMicro =
                Mathf.Max(0f, structural) *
                profile.ResearchMicroMeters *
                0.5f *
                w;

            var deltaMeters = riseMeters - carveMeters + micro + ridgeMicro;
            var normDelta = deltaMeters / Mathf.Max(terrainHeightMeters, 1f);
            return Mathf.Clamp01(proceduralNorm + normDelta);
        }

        static void TryApplyResearchBrief(ref LandscapeReliefProfile profile, int seed)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var jsonPath = Path.Combine(
                hub,
                CaveBuildAgentContextExporter.Folder,
                "TerrainResearchExecutionBrief.json");
            if (!File.Exists(jsonPath))
                return;

            try
            {
                var text = File.ReadAllText(jsonPath);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                profile.ResearchEntryCount = CountOccurrences(text, "\"category\"");
                profile.StructuralGuideScale = 1f + Mathf.Min(0.35f, profile.ResearchEntryCount * 0.012f);

                var lower = text.ToLowerInvariant();
                if (lower.Contains("carve") || lower.Contains("valley") || lower.Contains("bowl"))
                    profile.MaxValleyCarveMeters *= 1.12f;
                if (lower.Contains("rise") || lower.Contains("ridge") || lower.Contains("foothill"))
                    profile.MaxRidgeRiseMeters *= 1.15f;
                if (lower.Contains("walkable") || lower.Contains("trail") || lower.Contains("bench"))
                {
                    profile.MaxRidgeRiseMeters = Mathf.Min(profile.MaxRidgeRiseMeters, CharacterHeightMeters * 8f);
                    profile.ResearchMicroMeters *= 1.08f;
                }

                profile.MaxRidgeRiseMeters = Mathf.Clamp(profile.MaxRidgeRiseMeters, CharacterHeightMeters * 1.5f, 32f);
                profile.MaxValleyCarveMeters = Mathf.Clamp(profile.MaxValleyCarveMeters, CharacterHeightMeters * 0.8f, 18f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Surface] Terrain research brief parse skipped: " + ex.Message);
            }
        }

        static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return 0;

            var count = 0;
            var idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }

            return count;
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
