using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Benign NGO double-shutdown noise when exiting Play Mode or quitting without hosting.</summary>
    public static class NgoShutdownLogFilter
    {
        static bool s_LogHookInstalled;
        static ILogHandler s_DefaultLogHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InstallLogHook()
        {
            if (s_LogHookInstalled)
                return;

            s_DefaultLogHandler ??= Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new FilteredLogHandler(s_DefaultLogHandler);
            s_LogHookInstalled = true;
        }

        public static bool IsBenignShutdownNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace) || string.IsNullOrEmpty(condition))
                return false;

            if (!condition.Contains("NullReferenceException", StringComparison.Ordinal))
                return false;

            if (!stackTrace.Contains("Unity.Netcode.NetworkManager.OnDestroy", StringComparison.Ordinal) &&
                !stackTrace.Contains("Unity.Netcode.NetworkManager.OnApplicationQuit", StringComparison.Ordinal))
                return false;

            return stackTrace.Contains("NetworkSceneManager.Dispose", StringComparison.Ordinal) ||
                   stackTrace.Contains("NetworkTimeSystem.Shutdown", StringComparison.Ordinal);
        }

        sealed class FilteredLogHandler : ILogHandler
        {
            readonly ILogHandler _inner;

            public FilteredLogHandler(ILogHandler inner) => _inner = inner;

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) =>
                _inner.LogFormat(logType, context, format, args);

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                if (exception != null && IsBenignShutdownNoise(exception.Message, exception.StackTrace))
                    return;

                _inner.LogException(exception, context);
            }
        }
    }
}
