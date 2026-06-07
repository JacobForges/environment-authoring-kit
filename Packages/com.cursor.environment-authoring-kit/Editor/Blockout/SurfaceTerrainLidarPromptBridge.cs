#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Delegates to <see cref="CaveBuildUnifiedPromptBridge"/> — all Generated JSON feed AI prompts.</summary>
    static class SurfaceTerrainLidarPromptBridge
    {
        public static void ExportBeforeTerrainWork(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            int seed)
        {
            CaveBuildUnifiedPromptBridge.RefreshForPhase(
                "terrain_phase_dem",
                "terrain_integration",
                1,
                41,
                seed,
                out _);
        }
    }
}
#endif
