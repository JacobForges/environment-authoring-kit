#if UNITY_EDITOR
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Eleven-biome FullWorld layout: 9 play + 72 mixed (rings 2–4) + 208 preset wedges (rings 5–8).
    /// </summary>
    public static class FullWorldBiomeZoneLayout
    {
        public const int PlayTileCount = 9;
        public const int MixedRingMinChebyshev = 2;
        public const int MixedRingMaxChebyshev = 4;
        public const int MixedRingTileCount = 72;
        public const int PresetBiomeCount = FullWorldConceptLayoutCatalog.ConceptCount;
        public const int PresetZoneMinChebyshev = 5;
        public const int PresetZoneMaxChebyshev = SurfaceOpenWorldGridExpansion.MaxChebyshevRadius;

        /// <summary>9 play + 72 mixed + 208 preset (20–21 each) = 289 at radius 8.</summary>
        public const int TotalAaaTileTarget =
            PlayTileCount + MixedRingTileCount + PresetZoneTileCountAtMaxRadius;

        public const int PresetZoneTileCountAtMaxRadius = 208;
        public const int PresetTilesEachBase = 20;
        public const int PresetTilesWithExtra = 21;

        public static int ChebyshevDistance(Vector2Int off) =>
            Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y));

        public static bool IsPlayOffset(Vector2Int off) => ChebyshevDistance(off) <= 1;

        public static bool IsMixedOffset(Vector2Int off)
        {
            var cheb = ChebyshevDistance(off);
            return cheb >= MixedRingMinChebyshev && cheb <= MixedRingMaxChebyshev;
        }

        public static bool IsPresetZoneOffset(Vector2Int off) =>
            ChebyshevDistance(off) >= PresetZoneMinChebyshev;

        /// <summary>0–9 wedge index from tile offset (equal 36° sectors).</summary>
        public static int ResolvePresetIndex(Vector2Int off)
        {
            if (off == Vector2Int.zero)
                return 0;

            var angle = Mathf.Atan2(off.y, off.x);
            if (angle < 0f)
                angle += Mathf.PI * 2f;
            var sector = Mathf.FloorToInt(angle / (Mathf.PI * 2f / PresetBiomeCount));
            return Mathf.Clamp(sector, 0, PresetBiomeCount - 1);
        }

        public static WorldSurfaceBiomeId PresetIndexToBiome(int index) =>
            (WorldSurfaceBiomeId)((int)WorldSurfaceBiomeId.ConceptPreset00 + Mathf.Clamp(index, 0, PresetBiomeCount - 1));

        public static int BiomeToPresetIndex(WorldSurfaceBiomeId biome)
        {
            var v = (int)biome - (int)WorldSurfaceBiomeId.ConceptPreset00;
            return v >= 0 && v < PresetBiomeCount ? v : -1;
        }

        /// <summary>Deterministic mixed-ring flavor roll (sculpt/prop hints — biome stays MixedTransition).</summary>
        public static int RollMixedFlavorIndex(int seed, Vector2Int off) =>
            Mathf.Abs(seed * 31 + off.x * 17 + off.y * 13) % PresetBiomeCount;

        public static WorldSurfaceBiomeId ResolveBiome(Vector2Int off, bool isSouthAnnex)
        {
            if (isSouthAnnex)
                return WorldSurfaceBiomeId.AnnexLabyrinth;

            if (IsPlayOffset(off))
                return WorldSurfaceBiomeId.PlayKarst;

            if (IsMixedOffset(off))
                return WorldSurfaceBiomeId.MixedTransition;

            if (IsPresetZoneOffset(off))
                return PresetIndexToBiome(ResolvePresetIndex(off));

            return WorldSurfaceBiomeId.MixedTransition;
        }

        public static string GetZoneLabel(WorldSurfaceBiomeId biome)
        {
            var preset = BiomeToPresetIndex(biome);
            if (preset >= 0)
                return FullWorldConceptLayoutCatalog.GetDisplayName(preset);

            return biome switch
            {
                WorldSurfaceBiomeId.PlayKarst => "Play disk",
                WorldSurfaceBiomeId.MixedTransition => "Mixed transition (~72 tiles)",
                WorldSurfaceBiomeId.AnnexLabyrinth => "South labyrinth annex",
                WorldSurfaceBiomeId.BossThreshold => "Boss threshold",
                _ => biome.ToString(),
            };
        }
    }
}
#endif
