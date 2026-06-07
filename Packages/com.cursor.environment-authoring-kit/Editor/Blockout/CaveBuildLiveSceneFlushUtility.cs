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
        const double SeamPhaseMinFlushIntervalSeconds = 0.55;

        static double _lastFlushAt;
        static int _seamPhaseDepth;

        public static bool InSeamPhase => _seamPhaseDepth > 0;

        public static void EnterSeamPhase() => _seamPhaseDepth++;

        public static void ExitSeamPhase() => _seamPhaseDepth = Mathf.Max(0, _seamPhaseDepth - 1);

        public static void FlushWorldView(Terrain terrain = null, bool forceRepaint = false)
        {
            if (!CaveBuildLiveSceneFeedback.Enabled && !CaveBuildLiveSceneFeedback.SessionActive)
                return;

            var now = EditorApplication.timeSinceStartup;
            var minInterval = _seamPhaseDepth > 0 && !forceRepaint
                ? SeamPhaseMinFlushIntervalSeconds
                : MinFlushIntervalSeconds;
            if (!forceRepaint && now - _lastFlushAt < minInterval)
                return;
            _lastFlushAt = now;

            if (terrain != null)
                terrain.Flush();

            EditorApplication.QueuePlayerLoopUpdate();
            // RepaintAll on nine 513² terrains during paced builds causes 10–30s Scene view hitches when orbiting.
            // Demo timelapse recording needs repaints even in background so captures show live placement.
            if (_seamPhaseDepth > 0 && !CaveBuildDemoAutoRecorder.KeepSceneViewLiveForRecording)
                return;

            if (forceRepaint ||
                CaveBuildDemoAutoRecorder.KeepSceneViewLiveForRecording ||
                !CaveBuildEditorResponsiveness.IsLongBuildActive)
                SceneView.RepaintAll();
        }
    }
}
#endif
