#if UNITY_EDITOR
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// LiDAR DEM/hillshade drives carve/rise sculpt — not full heightmap stamp replacement.
    /// </summary>
    public static class SurfaceLidarGuidedSculptPolicy
    {
        const string PrefSculptOnly = "EnvironmentKit_LidarGuidedSculptOnly";

        public static bool PreferSculptOverStamp
        {
            get
            {
                if (EditorPrefs.HasKey(PrefSculptOnly))
                    return EditorPrefs.GetBool(PrefSculptOnly, true);

                var settings = CaveBuildCursorSettings.LoadOrCreate();
                settings.LoadFromPrefs();
                return settings.lidarGuidedSculptOnly;
            }
            set => EditorPrefs.SetBool(PrefSculptOnly, value);
        }

        public static float MaxLidarGuideInfluence =>
            CaveBuildAaaSessionPolicy.IsFullAaaRebuild ? 0.38f : 0.42f;
    }
}
#endif
