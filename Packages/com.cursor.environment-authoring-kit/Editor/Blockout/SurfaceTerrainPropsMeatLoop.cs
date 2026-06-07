#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Props meat loop — trees/grass/bushes/ground cover; runs after geo meat passes.</summary>
    static class SurfaceTerrainPropsMeatLoop
    {
        public const int MaxFixRounds = 4;
        public const int TargetOverallScore = 82;

        public static bool IsRunning =>
            SurfaceTerrainMeatLoopCore.IsRunning(SurfaceTerrainMeatLoopCore.Kind.Props);

        public static void Queue(
            SurfaceTerrainAiPhases.QueueState state,
            System.Action<SurfaceTerrainAiPhases.QueueState> onComplete)
        {
            SurfaceTerrainMeatLoopCore.Queue(
                state,
                new SurfaceTerrainMeatLoopCore.Config
                {
                    LoopKind = SurfaceTerrainMeatLoopCore.Kind.Props,
                    GradingMode = CaveBuildGradingProfile.TerrainPropsMeatLoop,
                    WorkflowEnv = "terrain_props",
                    PhaseId = "terrain_props_meat_loop",
                    LogPrefix = "[PropsMeat]",
                    MaxFixRounds = MaxFixRounds,
                    TargetOverallScore = TargetOverallScore,
                    RungFilter = SurfaceTerrainMeatLoopRungs.IsPropsRung,
                    RequirePlayableLandBeforeFinish = false,
                    OnComplete = onComplete,
                });
        }
    }
}
#endif
