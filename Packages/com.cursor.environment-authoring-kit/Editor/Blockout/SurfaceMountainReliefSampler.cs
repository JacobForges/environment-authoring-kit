#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// World-space mountain relief — stable across terrain tiles for open-world streaming.
    /// All samples use absolute world XZ (meters), not per-tile UV.
    /// </summary>
    public static class SurfaceMountainReliefSampler
    {
        const int RidgedOctaves = 5;
        const int AppalachianOctaves = 4;
        const float DomainWarpMeters = 140f;
        const float AppalachianWarpMeters = 220f;
        const float BaseFrequency = 0.00082f;
        const float AppalachianBaseFrequency = 0.00038f;

        /// <summary>Ridged multifractal 0..1 for macro massifs and ridge lines.</summary>
        public static float SampleRidgedMassif(float wx, float wz, int seed)
        {
            WarpDomain(ref wx, ref wz, seed);
            var amplitude = 1f;
            var frequency = BaseFrequency;
            var sum = 0f;
            var norm = 0f;

            for (var octave = 0; octave < RidgedOctaves; octave++)
            {
                sum += SampleRidgedOctave(wx, wz, seed + octave * 41, frequency) * amplitude;
                norm += amplitude;
                amplitude *= 0.48f;
                frequency *= 2.12f;
            }

            return norm > 0.0001f ? Mathf.Clamp01(sum / norm) : 0f;
        }

        /// <summary>High-frequency rock / scree detail (world-stable).</summary>
        public static float SampleRockDetail(float wx, float wz, int seed)
        {
            var u = wx * 0.0046f + seed * 0.031f;
            var v = wz * 0.0044f - seed * 0.027f;
            var n = Mathf.PerlinNoise(u, v);
            n = 1f - Mathf.Abs(n * 2f - 1f);
            return n * n;
        }

        /// <summary>Soft saddle / valley carve between corner massifs (0..1 strength).</summary>
        public static float SampleInterPeakSaddle(
            float wx,
            float wz,
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            var midX = (minX + maxX) * 0.5f;
            var midZ = (minZ + maxZ) * 0.5f;
            var dx = Mathf.Abs(wx - midX) / Mathf.Max(maxX - minX, 1f);
            var dz = Mathf.Abs(wz - midZ) / Mathf.Max(maxZ - minZ, 1f);
            var cornerness = Mathf.Min(dx, dz) * 2.2f;
            var saddle = Mathf.SmoothStep(0.22f, 0.62f, cornerness) * (1f - cornerness * 0.35f);
            return Mathf.Clamp01(saddle);
        }

        /// <summary>
        /// Relief biased along tile outward direction — ridges run across the edge, mass extends outward.
        /// </summary>
        public static float SampleDirectionalEdgeRelief(
            float wx,
            float wz,
            float outDirX,
            float outDirZ,
            int seed)
        {
            var mag = Mathf.Sqrt(outDirX * outDirX + outDirZ * outDirZ);
            if (mag < 0.001f)
                return SampleRidgedMassif(wx, wz, seed);

            outDirX /= mag;
            outDirZ /= mag;
            var perpX = -outDirZ;
            var perpZ = outDirX;
            var along = wx * outDirX + wz * outDirZ;
            var across = wx * perpX + wz * perpZ;

            var macro = 0f;
            var amp = 1f;
            var freqAlong = 0.0011f;
            var freqAcross = 0.0038f;
            var norm = 0f;
            for (var i = 0; i < 4; i++)
            {
                var u = along * freqAlong + seed * 0.017f + i * 2.1f;
                var v = across * freqAcross + seed * 0.023f + i * 1.7f;
                var n = Mathf.PerlinNoise(u, v);
                n = 1f - Mathf.Abs(n * 2f - 1f);
                macro += n * n * amp;
                norm += amp;
                amp *= 0.5f;
                freqAlong *= 2.05f;
                freqAcross *= 2.05f;
            }

            macro = norm > 0.0001f ? macro / norm : 0f;
            var detail = SampleRockDetail(along, across, seed + 331);
            return Mathf.Clamp01(macro * 0.78f + detail * 0.22f);
        }

        /// <summary>
        /// Rolling Appalachian / Great Smoky ridgelines — lower frequency, broader crests, gentle foothills.
        /// </summary>
        public static float SampleAppalachianRollingRelief(
            float wx,
            float wz,
            float wildernessFactor,
            float mapEdgeFactor,
            int seed)
        {
            WarpAppalachianDomain(ref wx, ref wz, seed);
            var amplitude = 1f;
            var frequency = AppalachianBaseFrequency;
            var sum = 0f;
            var norm = 0f;

            for (var octave = 0; octave < AppalachianOctaves; octave++)
            {
                var u = wx * frequency + seed * 0.013f + octave * 1.9f;
                var v = wz * frequency + seed * 0.019f + octave * 2.3f;
                var n = Mathf.PerlinNoise(u, v);
                n = Mathf.SmoothStep(0.18f, 0.82f, n);
                sum += n * amplitude;
                norm += amplitude;
                amplitude *= 0.55f;
                frequency *= 1.85f;
            }

            var macro = norm > 0.0001f ? sum / norm : 0f;
            var ridge = SampleRidgedMassif(wx, wz, seed + 4403) * 0.22f;
            var detail = SampleRockDetail(wx, wz, seed + 9901) * 0.06f;
            var combined = macro * 0.72f + ridge * 0.2f + detail * 0.08f;
            combined *= Mathf.Clamp01(wildernessFactor);
            combined *= Mathf.Lerp(0.55f, 1f, mapEdgeFactor);
            return Mathf.Clamp01(combined);
        }

        /// <summary>
        /// Foothill rolling relief from multiple world-space directions (not radial spokes from play center).
        /// </summary>
        public static float SampleFoothillOmnidirectionalRelief(
            float wx,
            float wz,
            int seed,
            float wildernessFactor)
        {
            var wx0 = wx;
            var wz0 = wz;
            WarpAppalachianDomain(ref wx, ref wz, seed);
            var u = wx * 0.00105f + seed * 0.011f;
            var v = wz * 0.00118f - seed * 0.009f;
            var a = Mathf.PerlinNoise(u, v);
            var b = Mathf.PerlinNoise(u * 1.73f + 19f, v * 1.41f - 7f);
            var c = Mathf.PerlinNoise((wx0 + wz0) * 0.00092f, (wx0 - wz0) * 0.00088f + seed * 0.02f);
            var d = SampleRockDetail(wx0, wz0, seed + 551) * 0.42f;
            var e = SampleRockDetail(wz0, -wx0, seed + 991) * 0.28f;
            var combined = a * 0.28f + b * 0.24f + c * 0.22f + d + e;
            return Mathf.Clamp01(combined * Mathf.Clamp01(wildernessFactor));
        }

        /// <summary>Combined outer-band relief factor (0..1) before meters→normalized conversion.</summary>
        public static float SampleOuterBandRelief(
            float wx,
            float wz,
            float edgeFactor,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            int seed)
        {
            var massif = SampleRidgedMassif(wx, wz, seed);
            var detail = SampleRockDetail(wx, wz, seed + 8803);
            var saddle = SampleInterPeakSaddle(wx, wz, minX, maxX, minZ, maxZ);
            var combined = massif * 0.82f + detail * 0.14f;
            combined *= edgeFactor;
            combined -= saddle * 0.18f * edgeFactor;
            return Mathf.Clamp01(combined);
        }

        static void WarpDomain(ref float wx, ref float wz, int seed)
        {
            var ox = seed * 0.017f + 2.4f;
            var oz = seed * 0.023f + 6.1f;
            wx += (Mathf.PerlinNoise(wx * 0.00035f + ox, wz * 0.00035f + oz) - 0.5f) * DomainWarpMeters;
            wz += (Mathf.PerlinNoise(wx * 0.00038f + ox + 19f, wz * 0.00038f + oz + 11f) - 0.5f) *
                   DomainWarpMeters;
        }

        static void WarpAppalachianDomain(ref float wx, ref float wz, int seed)
        {
            var ox = seed * 0.011f + 4.2f;
            var oz = seed * 0.016f + 8.7f;
            wx += (Mathf.PerlinNoise(wx * 0.00022f + ox, wz * 0.00022f + oz) - 0.5f) * AppalachianWarpMeters;
            wz += (Mathf.PerlinNoise(wx * 0.00024f + ox + 31f, wz * 0.00024f + oz + 17f) - 0.5f) *
                   AppalachianWarpMeters;
        }

        static float SampleRidgedOctave(float wx, float wz, int seed, float frequency)
        {
            var u = wx * frequency + seed * 0.017f;
            var v = wz * frequency + seed * 0.023f;
            var n = Mathf.PerlinNoise(u, v);
            n = 1f - Mathf.Abs(n * 2f - 1f);
            return n * n;
        }
    }
}
#endif
