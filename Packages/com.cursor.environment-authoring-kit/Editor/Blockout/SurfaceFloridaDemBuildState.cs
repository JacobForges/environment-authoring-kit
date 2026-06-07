#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Tracks Florida DEM stamp during the 12-phase surface world so terrain AI phases do not re-stamp.</summary>
    static class SurfaceFloridaDemBuildState
    {
        public static bool AuthoritativeStampCompletedThisBuild { get; private set; }

        /// <summary>Surface finish stamped all 8 neighbors + locked nine-tile seams (do not re-stitch in terrain phase 0).</summary>
        public static bool NineTilePlayDiskPolishCompletedThisBuild { get; private set; }

        /// <summary>FullWorld flat grid spawned + directional N→S→W→E polish (81 core or ~289 extended).</summary>
        public static bool FullWorldDirectionalBuildCompletedThisBuild { get; private set; }

        public static void ResetForBuildSession()
        {
            AuthoritativeStampCompletedThisBuild = false;
            NineTilePlayDiskPolishCompletedThisBuild = false;
            FullWorldDirectionalBuildCompletedThisBuild = false;
        }

        public static void MarkAuthoritativeStampCompleted() => AuthoritativeStampCompletedThisBuild = true;

        public static void MarkNineTilePlayDiskPolishCompleted() => NineTilePlayDiskPolishCompletedThisBuild = true;

        public static void MarkFullWorldDirectionalBuildCompleted() =>
            FullWorldDirectionalBuildCompletedThisBuild = true;
    }
}
#endif
