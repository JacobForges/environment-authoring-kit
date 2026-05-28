#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Tracks Florida DEM stamp during the 12-phase surface world so terrain AI phases do not re-stamp.</summary>
    static class SurfaceFloridaDemBuildState
    {
        public static bool AuthoritativeStampCompletedThisBuild { get; private set; }

        public static void ResetForBuildSession() => AuthoritativeStampCompletedThisBuild = false;

        public static void MarkAuthoritativeStampCompleted() => AuthoritativeStampCompletedThisBuild = true;
    }
}
#endif
