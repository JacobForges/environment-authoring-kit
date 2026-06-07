#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Prop feather bands at biome boundaries — mix adjacent biome prop pools by distance, not alphamap fade.
    /// </summary>
    public static class BiomePropFeatherResolver
    {
        public const float FeatherBandTiles = 1.75f;
        public const float TileSizeMeters = 220f;

        public readonly struct BiomePropBlend
        {
            public readonly WorldSurfaceBiomeId Primary;
            public readonly WorldSurfaceBiomeId Secondary;
            public readonly float SecondaryWeight;

            public BiomePropBlend(WorldSurfaceBiomeId primary, WorldSurfaceBiomeId secondary, float secondaryWeight)
            {
                Primary = primary;
                Secondary = secondary;
                SecondaryWeight = Mathf.Clamp01(secondaryWeight);
            }

            public static BiomePropBlend Pure(WorldSurfaceBiomeId biome) => new(biome, biome, 0f);
        }

        public static BiomePropBlend ResolveAtWorld(
            Vector3 worldPos,
            Vector3 playCenter,
            int seed,
            WorldGenerationRequest request,
            bool isSouthAnnex = false)
        {
            var off = TileOffsetFromWorld(worldPos, playCenter);
            return ResolveAtOffset(off, seed, request, isSouthAnnex);
        }

        public static BiomePropBlend ResolveAtOffset(
            Vector2Int off,
            int seed,
            WorldGenerationRequest request,
            bool isSouthAnnex = false)
        {
            if (isSouthAnnex)
                return BiomePropBlend.Pure(WorldSurfaceBiomeId.AnnexLabyrinth);

            var cheb = FullWorldBiomeZoneLayout.ChebyshevDistance(off);
            var primary = FullWorldBiomeZoneLayout.ResolveBiome(off, false);
            var fractionalCheb = FractionalChebyshev(off);

            // Play disk ↔ mixed ring boundary (~cheb 1.5)
            if (cheb <= 2)
            {
                var inner = WorldSurfaceBiomeId.PlayKarst;
                var outer = ResolveMixedInnerFlavor(off, seed);
                var t = SmoothStep(
                    fractionalCheb,
                    FullWorldBiomeZoneLayout.MixedRingMinChebyshev - FeatherBandTiles,
                    FullWorldBiomeZoneLayout.MixedRingMinChebyshev + FeatherBandTiles);
                if (t <= 0.001f)
                    return BiomePropBlend.Pure(inner);
                return new BiomePropBlend(inner, outer, t);
            }

            // Mixed ring ↔ preset wedge boundary (~cheb 4.5)
            if (cheb <= FullWorldBiomeZoneLayout.MixedRingMaxChebyshev + 1)
            {
                var inner = ResolveMixedInnerFlavor(off, seed);
                var outer = FullWorldBiomeZoneLayout.PresetIndexToBiome(
                    FullWorldBiomeZoneLayout.ResolvePresetIndex(off));
                var t = SmoothStep(
                    fractionalCheb,
                    FullWorldBiomeZoneLayout.PresetZoneMinChebyshev - FeatherBandTiles,
                    FullWorldBiomeZoneLayout.PresetZoneMinChebyshev + FeatherBandTiles);
                if (t <= 0.001f)
                    return BlendMixedRing(off, seed, inner);
                return new BiomePropBlend(inner, ApplyPresetOverride(outer, request), t);
            }

            // Preset wedge center — pure preset biome (optionally water/coastal override).
            return BiomePropBlend.Pure(ApplyPresetOverride(primary, request));
        }

        static BiomePropBlend BlendMixedRing(Vector2Int off, int seed, WorldSurfaceBiomeId innerFlavor)
        {
            // Sub-feather within mixed ring: play-karst meadow → foothill → peak by cheb sub-zones.
            var cheb = FullWorldBiomeZoneLayout.ChebyshevDistance(off);
            var play = WorldSurfaceBiomeId.PlayKarst;
            var foothill = WorldSurfaceBiomeId.FoothillGreen;
            var peak = WorldSurfaceBiomeId.PeakStone;

            if (cheb <= 4)
            {
                var t = SmoothStep(cheb, 2f, 4f);
                return t <= 0.001f
                    ? BiomePropBlend.Pure(play)
                    : new BiomePropBlend(play, foothill, t);
            }

            if (cheb <= 7)
            {
                var t = SmoothStep(cheb, 4f, 7f);
                return t <= 0.001f
                    ? BiomePropBlend.Pure(foothill)
                    : new BiomePropBlend(foothill, peak, t);
            }

            var horizon = WorldSurfaceBiomeId.HorizonMist;
            var presetHint = FullWorldBiomeZoneLayout.PresetIndexToBiome(
                FullWorldBiomeZoneLayout.RollMixedFlavorIndex(seed, off));
            var tOuter = SmoothStep(cheb, 7f, 9f);
            var secondary = presetHint;
            return tOuter <= 0.001f
                ? new BiomePropBlend(peak, horizon, 0.35f)
                : new BiomePropBlend(peak, secondary, tOuter);
        }

        static WorldSurfaceBiomeId ResolveMixedInnerFlavor(Vector2Int off, int seed)
        {
            var cheb = FullWorldBiomeZoneLayout.ChebyshevDistance(off);
            if (cheb <= 3)
                return WorldSurfaceBiomeId.FoothillGreen;
            if (cheb <= 6)
                return WorldSurfaceBiomeId.PeakStone;
            return FullWorldBiomeZoneLayout.PresetIndexToBiome(
                FullWorldBiomeZoneLayout.RollMixedFlavorIndex(seed, off));
        }

        static WorldSurfaceBiomeId ApplyPresetOverride(WorldSurfaceBiomeId biome, WorldGenerationRequest request)
        {
            if (request == null)
                return biome;

            var preset = FullWorldBiomeZoneLayout.BiomeToPresetIndex(biome);
            if (preset < 0)
                return biome;

            if ((preset == 2 || preset == 4 || preset == 7) && request.SurfaceIncludeWater)
                return biome; // coastal/water presets keep blue plant pools

            return biome;
        }

        public static Vector2Int TileOffsetFromWorld(Vector3 worldPos, Vector3 playCenter)
        {
            var dx = worldPos.x - playCenter.x;
            var dz = worldPos.z - playCenter.z;
            var tileX = Mathf.RoundToInt(dx / TileSizeMeters);
            var tileY = Mathf.RoundToInt(dz / TileSizeMeters);
            return new Vector2Int(tileX, tileY);
        }

        static float FractionalChebyshev(Vector2Int off)
        {
            var ax = Mathf.Abs(off.x);
            var ay = Mathf.Abs(off.y);
            return Mathf.Max(ax, ay);
        }

        static float SmoothStep(float value, float edge0, float edge1)
        {
            if (edge1 <= edge0)
                return value >= edge1 ? 1f : 0f;
            var t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
#endif
