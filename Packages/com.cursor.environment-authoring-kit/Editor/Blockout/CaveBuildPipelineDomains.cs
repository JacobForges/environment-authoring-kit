#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Console + pipeline log prefixes so cave and surface work stay separable for humans and agents.</summary>
    public static class CaveBuildPipelineDomains
    {
        public const string Cave = "[Cave]";
        public const string Surface = "[Surface]";
        public const string CaveQueue = "[Cave|Queue]";
        public const string SurfaceQueue = "[Surface|Queue]";
        public const string CaveLive = "[Cave|Live]";
        public const string SurfaceLive = "[Surface|Live]";

        public static string ActiveBuildPrefix =>
            CaveBuildPipelineScope.CaveOnlyContinuation ? Cave : Cave;

        public static string ActiveQueuePrefix =>
            CaveBuildPipelineScope.CaveOnlyContinuation ? CaveQueue : CaveQueue;

        public static string ActiveLivePrefix =>
            CaveBuildPipelineScope.CaveOnlyContinuation ? CaveLive : CaveLive;

        public static string QueueLabel(string action) => $"{ActiveQueuePrefix} {action}";

        public static string SurfaceQueueLabel(string action) => $"{SurfaceQueue} {action}";

        public static void LogCave(string message, bool forceUnityConsole = false) =>
            CaveBuildEditorLog.LogCave(message, forceUnityConsole);

        public static void LogCaveWarning(string message) =>
            CaveBuildEditorLog.LogCaveWarning(message);

        public static void LogSurface(string message, bool forceUnityConsole = false) =>
            CaveBuildEditorLog.LogSurface(message, forceUnityConsole);

        public static void LogSurfaceWarning(string message) =>
            Debug.LogWarning($"{Surface} {message}");
    }
}
#endif
