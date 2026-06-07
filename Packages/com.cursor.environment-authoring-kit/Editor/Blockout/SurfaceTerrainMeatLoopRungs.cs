#if UNITY_EDITOR
using System;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Terrain ladder rung sets for geo vs props meat loops (aligned with TS TERRAIN_RUNG_ORDER).</summary>
    public static class SurfaceTerrainMeatLoopRungs
    {
        public static readonly string[] GeoRungs =
        {
            "nine_tile_grid",
            "outer_ring_mountains",
            "cave_openings_poi",
            "satellite_cave_systems",
            "heightfield_no_craters",
            "playable_slopes",
            "trail_walkability",
            "surface_navmesh",
            "surface_playtest",
            "cave_mouth_grounding",
        };

        public static readonly string[] PropsRungs =
        {
            "prop_trees",
            "prop_grass",
            "prop_bushes",
            "prop_ground_cover",
        };

        public static bool IsGeoRung(string rungId) =>
            !string.IsNullOrEmpty(rungId) &&
            Array.IndexOf(GeoRungs, rungId) >= 0;

        public static bool IsPropsRung(string rungId) =>
            !string.IsNullOrEmpty(rungId) &&
            rungId.StartsWith("prop_", StringComparison.Ordinal);

        public static bool MatchesFilter(string rungId, Func<string, bool> filter) =>
            filter == null || filter(rungId);
    }
}
#endif
