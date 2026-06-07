#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Geo meat loop — tiles, heightfield, slopes, trails, nav; blocks default/unusable land.</summary>
    static class SurfaceTerrainGeoMeatLoop
    {
        public const int MaxFixRounds = 5;
        public const int TargetOverallScore = 85;

        public static bool IsRunning =>
            SurfaceTerrainMeatLoopCore.IsRunning(SurfaceTerrainMeatLoopCore.Kind.Geo);

        public static void Queue(
            SurfaceTerrainAiPhases.QueueState state,
            System.Action<SurfaceTerrainAiPhases.QueueState> onComplete)
        {
            SurfaceTerrainMeatLoopCore.Queue(
                state,
                new SurfaceTerrainMeatLoopCore.Config
                {
                    LoopKind = SurfaceTerrainMeatLoopCore.Kind.Geo,
                    GradingMode = CaveBuildGradingProfile.TerrainGeoMeatLoop,
                    WorkflowEnv = "terrain_geo",
                    PhaseId = "terrain_geo_meat_loop",
                    LogPrefix = "[GeoMeat]",
                    MaxFixRounds = MaxFixRounds,
                    TargetOverallScore = TargetOverallScore,
                    RungFilter = SurfaceTerrainMeatLoopRungs.IsGeoRung,
                    RequirePlayableLandBeforeFinish = true,
                    OnComplete = onComplete,
                });
        }
    }
}
#endif
