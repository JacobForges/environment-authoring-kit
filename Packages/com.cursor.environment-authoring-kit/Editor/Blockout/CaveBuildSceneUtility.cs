using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    internal static class CaveBuildSceneUtility
    {
        public static void ClearChildrenFast(Transform root)
        {
            if (root == null)
                return;

            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child != null &&
                    !PipelineContentPreservePolicy.ShouldAllowDestroy(child.gameObject, "cave clear children"))
                    continue;

                Object.DestroyImmediate(child.gameObject);
            }
        }

        public static bool ReportProgress(string title, string detail, float normalized, bool cancelable = true)
        {
            if (string.IsNullOrEmpty(title))
                return false;

            if (cancelable)
                return EditorUtility.DisplayCancelableProgressBar(title, detail, Mathf.Clamp01(normalized));

            EditorUtility.DisplayProgressBar(title, detail, Mathf.Clamp01(normalized));
            return false;
        }

        public static void RepaintViews()
        {
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
