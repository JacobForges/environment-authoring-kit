#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// FullWorld build order is terrain-first (see <see cref="CaveBuildStartupCoordinator"/>).
    /// Legacy cave-shell → surface handoff is disabled; these APIs only track LiDAR/terrain activity.
    /// </summary>
    static class CaveBuildFullWorldSurfaceDeferral
    {
        public static void ResetForNewBuildSession()
        {
            // Terrain-first mode: this type now only exists as a compatibility shim.
        }

        public static void NotifySurfacePipelineQueued()
        {
            // Terrain-first mode: startup/surface gate owns queue truth.
        }
    }
}
#endif
