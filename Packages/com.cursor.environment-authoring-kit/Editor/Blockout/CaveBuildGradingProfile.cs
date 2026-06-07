#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Task-aware grading scope — exported in quality JSON for TS prompt ladders.</summary>
    public static class CaveBuildGradingProfile
    {
        public const string FullAaaRebuild = "full_aaa_rebuild";
        public const string FullWorld = "full_world";
        public const string SurfaceOnly = "surface_only";
        public const string CaveOnly = "cave_only";
        public const string LayoutPrototype = "layout_prototype";
        public const string FullBuild = "full_build";

        public const string TerrainGeoMeatLoop = "terrain_geo_meat_loop";
        public const string TerrainPropsMeatLoop = "terrain_props_meat_loop";
        public const string TerrainMeatLoop = "terrain_meat_loop";

        public static string ResolveGradingTask(WorldGenerationRequest request, string gradingMode = null)
        {
            if (CaveBuildAaaSessionPolicy.IsFullAaaRebuild)
                return FullAaaRebuild;

            if (!string.IsNullOrEmpty(gradingMode) &&
                gradingMode.StartsWith("terrain_", System.StringComparison.Ordinal))
                return gradingMode;

            if (request == null)
                return gradingMode ?? FullBuild;

            if (request.UseLayoutPrototype)
                return LayoutPrototype;

            return request.SurfaceScope switch
            {
                SurfaceBuildScope.CaveOnly => CaveOnly,
                SurfaceBuildScope.SurfaceOnly => SurfaceOnly,
                SurfaceBuildScope.FullWorld => FullWorld,
                _ => gradingMode ?? FullBuild,
            };
        }

        public static int ExpectedSurfaceTileCount(WorldGenerationRequest request)
        {
            if (CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid)
                return SurfaceOpenWorldGridExpansion.BuildAaaExtendedPlaceOrder().Length;

            if (request?.SurfaceScope == SurfaceBuildScope.FullWorld)
                return SurfaceTerrainTileExpansion.FullWorldTerrainTileCount;

            return 1;
        }

        public static float MinPlacedTileFraction =>
            CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid ? 0.92f : 0.85f;

        public static int MinRequiredSurfaceTiles(WorldGenerationRequest request)
        {
            var planned = ExpectedSurfaceTileCount(request);
            return UnityEngine.Mathf.RoundToInt(planned * MinPlacedTileFraction);
        }

        /// <summary>True when terrain geo/props/orchestrator meat loop already graded the ladder.</summary>
        public static bool IsTerrainMeatPipelineMode(string gradingMode) =>
            gradingMode == TerrainMeatLoop ||
            gradingMode == TerrainGeoMeatLoop ||
            gradingMode == TerrainPropsMeatLoop ||
            gradingMode == "terrain_meat_loop";
    }
}
#endif
