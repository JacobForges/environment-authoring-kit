#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Terrain meat orchestrator: geo meat loop → props meat loop → terrain ladder continuation.
    /// Replaces the old single-pass loop that mixed heightfield and prop rungs.
    /// </summary>
    static class SurfaceTerrainQualityMeatLoop
    {
        public const int MaxFixRounds = SurfaceTerrainGeoMeatLoop.MaxFixRounds;
        public const int TargetOverallScore = SurfaceTerrainGeoMeatLoop.TargetOverallScore;

        public static bool IsRunning =>
            SurfaceTerrainGeoMeatLoop.IsRunning ||
            SurfaceTerrainPropsMeatLoop.IsRunning ||
            SurfaceTerrainMeatLoopCore.IsAnyRunning;

        public static void QueueAfterTerrainPhases(SurfaceTerrainAiPhases.QueueState state)
        {
            if (state?.Ground?.Terrain == null)
            {
                SurfaceTerrainAiPhases.ContinueAfterTerrainMeatLoop(state);
                return;
            }

            if (IsRunning)
            {
                CaveBuildEditorLog.LogSurface(
                    "[TerrainMeat] Orchestrator already running — duplicate queue ignored.",
                    forceUnityConsole: true);
                return;
            }

            CaveBuildEditorLog.LogSurface(
                "[TerrainMeat] Orchestrator — geo meat → props meat → ladder handoff.",
                forceUnityConsole: true);

            state.LadderReport ??= new SurfaceTerrainLadderReport
            {
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                Seed = state.Request?.Seed ?? 0,
                GradingMode = CaveBuildGradingProfile.TerrainMeatLoop,
                GradingTask = CaveBuildGradingProfile.ResolveGradingTask(state.Request),
            };

            SurfaceTerrainGeoMeatLoop.Queue(
                state,
                _ => SurfaceTerrainPropsMeatLoop.Queue(state, SurfaceTerrainAiPhases.ContinueAfterTerrainMeatLoop));
        }
    }
}
#endif
