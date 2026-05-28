#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Routes build messages to Pipeline Console; mirrors to Unity Console only when safe (avoids ConsoleWindow overload).
    /// </summary>
    public static class CaveBuildEditorLog
    {
        public static bool MirrorPacedLogsToConsole
        {
            get
            {
                var s = CaveBuildCursorSettings.LoadOrCreate();
                s.LoadFromPrefs();
                return s.mirrorPacedBuildLogsToConsole;
            }
        }

        public static bool IsPacedBuildActive =>
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive || CaveBuildActionPacing.IsBusy;

        public static void LogCave(string message, bool forceUnityConsole = false)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Info(message, "Cave");
            if (forceUnityConsole || ShouldMirrorInfoToUnityConsole())
                Debug.Log($"{CaveBuildPipelineDomains.Cave} {message}");
        }

        public static void LogCaveWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Warn(message, "Cave");
            Debug.LogWarning($"{CaveBuildPipelineDomains.Cave} {message}");
        }

        public static void LogSurface(string message, bool forceUnityConsole = false)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Info(message, "Surface");
            if (forceUnityConsole || ShouldMirrorInfoToUnityConsole())
                Debug.Log($"{CaveBuildPipelineDomains.Surface} {message}");
        }

        public static void LogSurfaceWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Warn(message, "Surface");
            Debug.LogWarning($"{CaveBuildPipelineDomains.Surface} {message}");
        }

        public static void LogQueueStep(string label, string weightSummary)
        {
            var msg = string.IsNullOrEmpty(weightSummary)
                ? label
                : $"{weightSummary} — {label}";
            CaveBuildPipelineLog.Info(msg, "Cave-Queue");
            if (MirrorPacedLogsToConsole)
                Debug.Log($"{CaveBuildPipelineDomains.Cave} Queue {msg}");
        }

        public static void LogLiveStep(string label)
        {
            CaveBuildPipelineLog.Info(label, "Cave-Live");
            if (MirrorPacedLogsToConsole || !IsPacedBuildActive)
                Debug.Log(label.StartsWith("[") ? label : $"{CaveBuildPipelineDomains.CaveLive} {label}");
        }

        static bool ShouldMirrorInfoToUnityConsole() =>
            MirrorPacedLogsToConsole || !IsPacedBuildActive;
    }
}
#endif
