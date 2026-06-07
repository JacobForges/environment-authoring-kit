#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// True only for <see cref="SurfaceBuildScope.CaveOnly"/> align builds after pre-build.
    /// FullWorld uses <see cref="BeginFullPipeline"/> (research + validate + shell-then-LiDAR deferral).
    /// </summary>
    public static class CaveBuildPipelineScope
    {
        public static bool CaveOnlyContinuation { get; private set; }

        public static void BeginCaveOnlyContinuation() => CaveOnlyContinuation = true;

        public static void BeginFullPipeline() => CaveOnlyContinuation = false;

        public static void Clear() => CaveOnlyContinuation = false;
    }
}
#endif
