#if UNITY_EDITOR
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Diagnostics only — normal builds use Window → Environment Kit → Build Complete Cave Level.</summary>
    public static class CaveBuildFullRunMenu
    {
        [MenuItem(CaveBuildMenuPaths.Advanced + "Diagnostics/View Preflight Report (last run)", false, 200)]
        public static void OpenLastPreflightReport()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = System.IO.Path.Combine(hub, CaveBuildFullRunPreflight.ReportRel);
            if (!System.IO.File.Exists(path))
            {
                EditorUtility.DisplayDialog(
                    "Preflight",
                    "No report yet — run Build Complete Cave (FullWorld) once; preflight runs automatically.",
                    "OK");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Diagnostics/Open Completion Contract JSON", false, 201)]
        public static void OpenCompletionContract()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = System.IO.Path.Combine(hub, CaveBuildCompletionContract.ContractJsonRel);
            if (!System.IO.File.Exists(path))
            {
                EditorUtility.DisplayDialog(
                    "Completion contract",
                    "No contract yet — finish Build Complete Cave (FullWorld); contract writes automatically.",
                    "OK");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }
    }
}
#endif
