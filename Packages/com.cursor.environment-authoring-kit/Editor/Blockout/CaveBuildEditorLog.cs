#if UNITY_EDITOR
using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Routes build messages to Pipeline Console + Hub activity; Unity Console is errors-only during long builds
    /// unless <see cref="MirrorPacedBuildLogsToConsole"/> is enabled on CaveBuildCursorSettings.
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
            if (ShouldMirrorInfoToUnityConsole(forceUnityConsole))
                Debug.Log($"{CaveBuildPipelineDomains.Cave} {message}");
        }

        public static void LogCaveWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Warn(message, "Cave");
            if (ShouldMirrorWarningsToUnityConsole())
                Debug.LogWarning($"{CaveBuildPipelineDomains.Cave} {message}");
        }

        public static void LogCaveError(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Error(message, "Cave");
            Debug.LogError($"{CaveBuildPipelineDomains.Cave} {message}");
        }

        public static void LogSurface(string message, bool forceUnityConsole = false)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Info(message, "Surface");
            if (ShouldMirrorInfoToUnityConsole(forceUnityConsole))
                Debug.Log($"{CaveBuildPipelineDomains.Surface} {message}");
        }

        public static void LogSurfaceWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Warn(message, "Surface");
            if (ShouldMirrorWarningsToUnityConsole())
                Debug.LogWarning($"{CaveBuildPipelineDomains.Surface} {message}");
        }

        public static void LogSurfaceError(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            CaveBuildPipelineLog.Error(message, "Surface");
            Debug.LogError($"{CaveBuildPipelineDomains.Surface} {message}");
        }

        public static void LogQueueStep(string label, string weightSummary)
        {
            var msg = string.IsNullOrEmpty(weightSummary)
                ? label
                : $"{weightSummary} — {label}";
            CaveBuildPipelineLog.Info(msg, "Cave-Queue");
            CaveBuildRunStatusPublisher.RecordActivity("queue", msg);
            if (!string.IsNullOrEmpty(label) && label.IndexOf("run [", StringComparison.Ordinal) >= 0)
                CaveBuildRunStatusPublisher.PulseSubOperation("editor queue", label);

            if (ShouldMirrorInfoToUnityConsole(forceUnityConsole: false))
                Debug.Log($"{CaveBuildPipelineDomains.Cave} Queue {msg}");
        }

        public static void LogLiveStep(string label)
        {
            CaveBuildPipelineLog.Info(label, "Cave-Live");
            if (ShouldMirrorInfoToUnityConsole(forceUnityConsole: false))
                Debug.Log(label.StartsWith("[") ? label : $"{CaveBuildPipelineDomains.CaveLive} {label}");
        }

        /// <summary>Info/log lines go to Pipeline Console only unless mirror is on (forceUnityConsole is ignored when mirror is off).</summary>
        static bool ShouldMirrorInfoToUnityConsole(bool forceUnityConsole) =>
            MirrorPacedLogsToConsole;

        static bool ShouldMirrorWarningsToUnityConsole() =>
            MirrorPacedLogsToConsole;
    }
}
#endif
