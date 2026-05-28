#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Throttled Scene/Game view refresh during paced builds (shared by terrain, props, and pacing).
    /// </summary>
    public static class CaveBuildLiveSceneFlushUtility
    {
        const double MinFlushIntervalSeconds = 0.18;

        static double _lastFlushAt;

        public static void FlushWorldView(Terrain terrain = null)
        {
            if (!CaveBuildLiveSceneFeedback.Enabled && !CaveBuildLiveSceneFeedback.SessionActive)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastFlushAt < MinFlushIntervalSeconds)
                return;
            _lastFlushAt = now;

            if (terrain != null)
                terrain.Flush();

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }
}
#endif
