using System;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace Hub
{
    /// <summary>
    /// Suppresses expected Vivox UGS init noise when dashboard credentials are not synced yet.
    /// Voice still fails gracefully via Hub.Multiplayer.PortfolioVivoxSupport until Game → Setup Vivox.
    /// </summary>
    static class PortfolioVivoxInitGuard
    {
        static bool _installed;
        static bool _skipLogged;
        static ILogHandler _defaultLogHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InstallOnLoad() => EnsureInstalled();

        public static void EnsureInstalled()
        {
            if (_installed || PortfolioVivoxRuntime.HasProjectCredentials())
                return;

            _defaultLogHandler ??= Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new FilteredLogHandler(_defaultLogHandler);
            _installed = true;
        }

        public static void LogSkippedOnce()
        {
            if (_skipLogged || PortfolioVivoxRuntime.HasProjectCredentials())
                return;

            _skipLogged = true;
            Debug.LogWarning(
                "[Multiplayer] Vivox voice unavailable — run Game → Setup Vivox in the Editor after enabling Vivox on the Unity Dashboard. "
                + "Relay and Lobby still work without voice.");
        }

        internal static bool IsUnconfiguredInitNoise(string condition, string stackTrace)
        {
            if (PortfolioVivoxRuntime.HasProjectCredentials())
                return false;

            if (string.IsNullOrEmpty(condition))
                return false;

            if (!condition.Contains("[Vivox]", StringComparison.Ordinal))
                return false;

            if (condition.Contains("Failed to pull Credentials", StringComparison.Ordinal))
                return true;

            if (condition.Contains("'server' is null or empty", StringComparison.Ordinal)
                || condition.Contains("'domain' is null or empty", StringComparison.Ordinal)
                || condition.Contains("'issuer' is null or empty", StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrEmpty(stackTrace)
                && stackTrace.Contains("VivoxServiceInternal..ctor", StringComparison.Ordinal))
                return true;

            return condition.Contains("Unable to initialize Vivox", StringComparison.Ordinal);
        }

        sealed class FilteredLogHandler : ILogHandler
        {
            readonly ILogHandler _inner;

            public FilteredLogHandler(ILogHandler inner) => _inner = inner;

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                var message = args != null && args.Length > 0 ? string.Format(format, args) : format;
                if (args != null)
                {
                    foreach (var arg in args)
                    {
                        if (arg is Exception ex && IsUnconfiguredInitNoise(ex.Message, ex.StackTrace))
                        {
                            LogSkippedOnce();
                            return;
                        }
                    }
                }

                if (IsUnconfiguredInitNoise(message, null))
                {
                    LogSkippedOnce();
                    return;
                }

                _inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                if (exception != null && IsUnconfiguredInitNoise(exception.Message, exception.StackTrace))
                {
                    LogSkippedOnce();
                    return;
                }

                if (exception != null && NgoShutdownLogFilter.IsBenignShutdownNoise(exception.Message, exception.StackTrace))
                    return;

                _inner.LogException(exception, context);
            }
        }
    }
}
