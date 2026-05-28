#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Suppresses harmless editor console spam (OpenXR reimport loop, Unity AI Assistant Relay WebSocket).
    /// </summary>
    [InitializeOnLoad]
    static class OpenXRImportLoopGuard
    {
        public const string OpenXRSettingsAssetPath = "Assets/XR/Settings/OpenXRPackageSettings.asset";

        const string PrefSuppressWarnings = "CaveBuild_SuppressOpenXRImportWarnings";
        const double WindowSeconds = 8.0;
        const int WarnThreshold = 6;

        static double _windowStart;
        static int _importCount;
        static bool _warnedThisSession;
        static ILogHandler _defaultLogHandler;
        static bool _logHookInstalled;
        static bool _stabilizeInProgress;
        static double _stabilizeStartedAt;
        static double _importCooldownUntil;
        static bool _stabilizeQueuedAfterBuild;
        static int _mainThreadId;

        public static bool SuppressImporterWarnings
        {
            get => EditorPrefs.GetBool(PrefSuppressWarnings, true);
            set
            {
                EditorPrefs.SetBool(PrefSuppressWarnings, value);
                ApplyLogHook();
            }
        }

        static OpenXRImportLoopGuard()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.update += OnEditorUpdateDeferredStabilize;
            ApplyLogHook();
        }

        static void OnEditorUpdateDeferredStabilize()
        {
            if (!_stabilizeQueuedAfterBuild)
                return;

            if (LavaTubeCaveBuilder.IsBuildInProgress ||
                LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
                CaveBuildStartupCoordinator.IsActive)
                return;

            _stabilizeQueuedAfterBuild = false;
            EditorApplication.delayCall += StabilizeOpenXRSettingsInternal_Delay;
        }

        static void ApplyLogHook()
        {
            if (SuppressImporterWarnings)
            {
                if (_logHookInstalled)
                    return;

                _defaultLogHandler ??= Debug.unityLogger.logHandler;
                Debug.unityLogger.logHandler = new FilteredLogHandler(_defaultLogHandler);
                Application.logMessageReceived -= OnLogMessageReceived;
                Application.logMessageReceived += OnLogMessageReceived;
                _logHookInstalled = true;
                return;
            }

            if (!_logHookInstalled)
                return;

            Application.logMessageReceived -= OnLogMessageReceived;
            if (_defaultLogHandler != null)
                Debug.unityLogger.logHandler = _defaultLogHandler;
            _logHookInstalled = false;
        }

        static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!SuppressImporterWarnings || !ShouldSuppressConsoleNoise(condition, stackTrace))
                return;

            EditorApplication.delayCall -= PruneMatchingConsoleEntries;
            EditorApplication.delayCall += PruneMatchingConsoleEntries;
        }

        static void PruneMatchingConsoleEntries()
        {
            try
            {
                var logEntriesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType == null)
                    return;

                var getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                var getEntryInternal = logEntriesType.GetMethod(
                    "GetEntryInternal",
                    BindingFlags.Static | BindingFlags.Public);
                var deleteEntry = logEntriesType.GetMethod(
                    "DeleteEntry",
                    BindingFlags.Static | BindingFlags.Public);

                if (getCount == null || getEntryInternal == null || deleteEntry == null)
                    return;

                var count = (int)getCount.Invoke(null, null);
                for (var i = count - 1; i >= 0; i--)
                {
                    var args = new object[] { i, string.Empty, string.Empty, LogType.Log };
                    getEntryInternal.Invoke(null, args);
                    var message = (string)args[1];
                    var stack = (string)args[2];
                    if (!ShouldSuppressConsoleNoise(message, stack))
                        continue;

                    deleteEntry.Invoke(null, new object[] { i });
                }
            }
            catch
            {
                // LogEntries API varies by Unity version — ILogHandler filter is the primary path.
            }
        }

        internal static bool ShouldSuppressConsoleNoise(string condition, string stackTrace) =>
            IsOpenXrImporterNoise(condition, stackTrace) ||
            IsUnityAiRelayNoise(condition, stackTrace) ||
            IsUnitySearchDbLockNoise(condition, stackTrace);

        internal static bool IsOpenXrImporterNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition))
                return false;

            if (!condition.Contains("inconsistent result", StringComparison.OrdinalIgnoreCase))
                return false;

            if (condition.Contains("OpenXRPackageSettings", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.Contains("3dd11dcd12272e54f8a19d8224e35f53", StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrEmpty(stackTrace) &&
                   stackTrace.Contains("OpenXRPackageSettings", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Unity AI Assistant Relay (com.unity.ai.assistant) — offline / no relay server is harmless for cave build.
        /// </summary>
        internal static bool IsUnityAiRelayNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition))
                return false;

            var fromRelayStack =
                !string.IsNullOrEmpty(stackTrace) &&
                (stackTrace.Contains("Unity.Relay.Editor.RelayService", StringComparison.Ordinal) ||
                 stackTrace.Contains("com.unity.ai.assistant", StringComparison.OrdinalIgnoreCase));

            if (!fromRelayStack &&
                !condition.Contains("RelayService", StringComparison.Ordinal) &&
                !condition.Contains("connection.state_change", StringComparison.Ordinal))
                return false;

            if (condition.Contains("connection.state_change", StringComparison.Ordinal))
                return true;

            if (condition.Contains("RelayService", StringComparison.Ordinal) &&
                condition.Contains("WebSocket", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.Contains("Unable to connect to the remote server", StringComparison.OrdinalIgnoreCase) &&
                fromRelayStack)
                return true;

            return fromRelayStack &&
                   (condition.Contains("newState=Failed", StringComparison.Ordinal) ||
                    condition.Contains("Relay", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsUnitySearchDbLockNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition))
                return false;

            if (!condition.Contains("Sharing violation", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!condition.Contains("Library/Search", StringComparison.OrdinalIgnoreCase) &&
                !condition.Contains("propertyAliases.db", StringComparison.OrdinalIgnoreCase))
                return false;

            return !string.IsNullOrEmpty(stackTrace) &&
                   stackTrace.Contains("UnityEditor.Search", StringComparison.Ordinal);
        }

        static void OnEditorUpdate()
        {
            if (_stabilizeInProgress &&
                EditorApplication.timeSinceStartup - _stabilizeStartedAt > 45.0)
            {
                _stabilizeInProgress = false;
                Debug.LogWarning(
                    "[CaveBuild] OpenXR stabilize timed out — you can retry: Diagnostics → Stabilize OpenXR Import Loop.");
            }

            if (_importCount < WarnThreshold)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now < _importCooldownUntil)
                return;

            if (!_warnedThisSession)
            {
                _warnedThisSession = true;
                if (!SuppressImporterWarnings)
                {
                    Debug.LogWarning(
                        "[CaveBuild] OpenXRPackageSettings is reimporting repeatedly — scheduling one non-blocking stabilize pass. " +
                        "Diagnostics → Suppress OpenXR Import Warnings hides console noise.");
                }
            }

            if (_stabilizeInProgress || _stabilizeQueuedAfterBuild)
                return;

            _importCooldownUntil = now + 30.0;
            EditorApplication.delayCall += StabilizeOpenXRSettingsInternal_Delay;
        }

        static void RegisterImport()
        {
            if (_stabilizeInProgress || EditorApplication.timeSinceStartup < _importCooldownUntil)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now - _windowStart > WindowSeconds)
            {
                _windowStart = now;
                _importCount = 0;
            }

            _importCount++;
        }

        sealed class FilteredLogHandler : ILogHandler
        {
            readonly ILogHandler _inner;

            public FilteredLogHandler(ILogHandler inner) => _inner = inner;

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                var message = args != null && args.Length > 0 ? string.Format(format, args) : format;
                if (ShouldSuppressConsoleNoise(message, null))
                    return;

                _inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                if (exception == null)
                    return;

                var stack = exception.StackTrace ?? string.Empty;
                if (ShouldSuppressConsoleNoise(exception.Message, stack))
                    return;

                if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                {
                    _inner.LogException(exception, context);
                    return;
                }

                // Avoid Unity crash reporter calls from worker threads.
                EditorApplication.delayCall += () => _inner.LogException(exception, context);
            }
        }

        class ImportWatcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                if (importedAssets == null)
                    return;

                if (importedAssets.Any(p => p == OpenXRSettingsAssetPath))
                    RegisterImport();
            }
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Suppress OpenXR Import Warnings", true, 199)]
        static bool SuppressOpenXrImportWarningsValidate()
        {
            Menu.SetChecked(
                CaveBuildMenuPaths.Diagnostics + "Suppress OpenXR Import Warnings",
                SuppressImporterWarnings);
            return true;
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Suppress OpenXR Import Warnings", false, 199)]
        static void ToggleSuppressOpenXrImportWarnings()
        {
            SuppressImporterWarnings = !SuppressImporterWarnings;
            Debug.Log(
                SuppressImporterWarnings
                    ? "[CaveBuild] OpenXR import + Unity AI Relay WebSocket noise suppressed in Console."
                    : "[CaveBuild] OpenXR/Relay console suppression disabled.");
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Stabilize OpenXR Import Loop", false, 200)]
        public static void StabilizeOpenXRSettings()
        {
            StabilizeOpenXRSettingsInternal(showDialog: true);
        }

        static void StabilizeOpenXRSettingsInternal_Delay()
        {
            EditorApplication.delayCall -= StabilizeOpenXRSettingsInternal_Delay;
            StabilizeOpenXRSettingsInternal(showDialog: false);
        }

        static void StabilizeOpenXRSettingsInternal(bool showDialog)
        {
            if (_stabilizeInProgress &&
                EditorApplication.timeSinceStartup - _stabilizeStartedAt < 45.0)
                return;

            _stabilizeInProgress = true;
            _stabilizeStartedAt = EditorApplication.timeSinceStartup;
            _importCooldownUntil = _stabilizeStartedAt + 5.0;
            if (!System.IO.File.Exists(OpenXRSettingsAssetPath))
            {
                try
                {
                    if (showDialog)
                    {
                        EditorUtility.DisplayDialog(
                            "OpenXR",
                            "Asset not found:\n" + OpenXRSettingsAssetPath,
                            "OK");
                    }
                }
                finally
                {
                    _stabilizeInProgress = false;
                }
                return;
            }

            try
            {
                _importCount = 0;
                _warnedThisSession = false;

                // Single async import only — Refresh/SaveAssets/ForceSynchronousImport freeze the editor during cave builds.
                AssetDatabase.ImportAsset(
                    OpenXRSettingsAssetPath,
                    ImportAssetOptions.DontDownloadFromCacheServer);

                if (showDialog)
                {
                    EditorUtility.DisplayDialog(
                        "OpenXR stabilized",
                        "Queued one non-blocking reimport of OpenXRPackageSettings.\n\n" +
                        "If the Console still loops:\n" +
                        "• Close Project Settings → XR Plug-in Management\n" +
                        "• Do not run stabilize during an active cave build\n" +
                        "• Diagnostics → Suppress OpenXR Import Warnings (on by default)",
                        "OK");
                }
                else
                {
                    Debug.Log("[CaveBuild] OpenXRPackageSettings — non-blocking reimport queued (auto-stabilize).");
                }
            }
            finally
            {
                _stabilizeInProgress = false;
            }
        }
    }
}
#endif
