#if UNITY_EDITOR
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Modal progress bars block Unity's UI thread — use status files + throttled bars only.
    /// </summary>
    public static class CaveBuildProgressUI
    {
        static double _lastBarAt;

        public const double MinIntervalSeconds = 0.85;

        public static bool UseModalBarsDuringBuild =>
            !CaveBuildEditorResponsiveness.IsLongBuildActive;

        public static void ShowThrottled(string title, string info, float progress01)
        {
            if (!UseModalBarsDuringBuild)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastBarAt < MinIntervalSeconds)
                return;

            _lastBarAt = now;
            EditorUtility.DisplayProgressBar(title, info, progress01);
        }

        public static void ClearIfShown()
        {
            if (UseModalBarsDuringBuild)
                EditorUtility.ClearProgressBar();
        }
    }
}
#endif
