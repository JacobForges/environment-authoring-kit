#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Suppresses harmless editor console spam (OpenXR reimport, Unity AI Relay, Unity Connect/UPM token errors).
    /// Does not change Unity networking — only hides known-safe noise when suppression is explicitly enabled (default off).
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
            get => EditorPrefs.GetBool(PrefSuppressWarnings, false);
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
            QueueStartupOpenXRStabilize();
        }

        static void QueueStartupOpenXRStabilize()
        {
            if (SessionState.GetBool("EnvKit_OpenXRStartupStabilizeQueued", false))
                return;

            SessionState.SetBool("EnvKit_OpenXRStartupStabilizeQueued", true);
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool("EnvKit_OpenXRStartupStabilized", false))
                    return;

                if (!System.IO.File.Exists(OpenXRSettingsAssetPath))
                    return;

                SessionState.SetBool("EnvKit_OpenXRStartupStabilized", true);
                StabilizeOpenXRSettingsInternal(showDialog: false);
            };
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
                EditorApplication.delayCall += PruneMatchingConsoleEntries;
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
            IsInputManagerDeprecationNoise(condition) ||
            IsUnityAiRelayNoise(condition, stackTrace) ||
            IsUnitySearchDbLockNoise(condition, stackTrace) ||
            IsUnityConnectPackageManagerNoise(condition, stackTrace) ||
            IsTerrainResourceIdNoise(condition, stackTrace) ||
            Cc0ImportWarningSuppressor.ShouldSuppress(condition, stackTrace);

        internal static bool IsInputManagerDeprecationNoise(string condition)
        {
            if (string.IsNullOrEmpty(condition))
                return false;

            return condition.Contains("Input Manager", StringComparison.OrdinalIgnoreCase) &&
                   condition.Contains("deprecat", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Unity terrain DelayLOD / multi-tile builds can overflow internal GPU resource tables (harmless but noisy).
        /// Kit batches SyncHeightmap; suppress spam when suppression is on.
        /// </summary>
        internal static bool IsTerrainResourceIdNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition))
                return false;

            if (!condition.Contains("Resource ID out of range", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!condition.Contains("SetResource", StringComparison.OrdinalIgnoreCase))
                return false;

            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                return true;

            return string.IsNullOrEmpty(stackTrace) ||
                   stackTrace.Contains("Terrain", StringComparison.OrdinalIgnoreCase) ||
                   stackTrace.Contains("TerrainData", StringComparison.OrdinalIgnoreCase);
        }

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

        /// <summary>
        /// Unity Hub / Package Manager token refresh — unrelated to Environment Kit builds (offline/local UPM still works).
        /// </summary>
        internal static bool IsUnityConnectPackageManagerNoise(string condition, string stackTrace)
        {
            var message = condition ?? string.Empty;
            var stack = stackTrace ?? string.Empty;
            if (message.Length == 0 && stack.Length == 0)
                return false;

            if (message.Contains("UnityConnectWebRequestException", StringComparison.Ordinal))
                return true;

            if (message.Contains("Token Exchange failed", StringComparison.OrdinalIgnoreCase))
                return true;

            if (message.Contains("Error while getting access token", StringComparison.OrdinalIgnoreCase))
                return true;

            if (message.Contains("invalid configuration from Unity Connect", StringComparison.OrdinalIgnoreCase))
                return true;

            if (message.Contains("Error while getting product update details", StringComparison.OrdinalIgnoreCase))
                return true;

            if (stack.Contains("UnityEditor.Connect.TokenExchange", StringComparison.Ordinal))
                return true;

            if (stack.Contains("UnityEditor.Connect.UnityConnect", StringComparison.Ordinal) &&
                message.Contains("Token", StringComparison.OrdinalIgnoreCase))
                return true;

            return stack.Contains("UnityEditor.AsyncHTTPClient", StringComparison.Ordinal) &&
                   message.Contains("Package Manager", StringComparison.OrdinalIgnoreCase);
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
                        "Diagnostics → Suppress OpenXR Import Warnings optionally hides OpenXR console noise.");
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
                    ? "[CaveBuild] Console noise suppressed: OpenXR import, AI Relay, Unity Connect/Package Manager tokens. " +
                      "FBX/OBJ import warnings are never hidden — use Diagnostics → Repair Legacy Model Imports."
                    : "[CaveBuild] Editor service console suppression disabled. Import warnings remain visible.");
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Unity Connect / Package Manager Errors (Help)", false, 198)]
        static void ShowUnityConnectHelp()
        {
            EditorUtility.DisplayDialog(
                "Unity Connect errors — not Environment Kit",
                "Those messages come from the Unity Editor talking to Unity’s cloud (Package Manager updates, " +
                "sign-in tokens). Environment Kit does not call UnityConnect or block your network.\n\n" +
                "Your project already has Unity Connect analytics disabled in Project Settings.\n\n" +
                "What you can do:\n" +
                "• Sign in again via Unity Hub, then restart the Editor\n" +
                "• Close the Package Manager window when not installing packages\n" +
                "• Ignore them — Diagnostics → Suppress OpenXR Import Warnings (optional) hides them in Console\n" +
                "• Builds and local packages still work without token refresh\n\n" +
                "Packages that often trigger checks: com.unity.ai.assistant, com.unity.collab-proxy",
                "OK");
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
                    ImportAssetOptions.DontDownloadFromCacheServer | ImportAssetOptions.ForceUpdate);

                if (showDialog)
                {
                    EditorUtility.DisplayDialog(
                        "OpenXR stabilized",
                        "Queued one non-blocking reimport of OpenXRPackageSettings.\n\n" +
                        "If the Console still loops:\n" +
                        "• Close Project Settings → XR Plug-in Management\n" +
                        "• Do not run stabilize during an active cave build\n" +
                        "• Diagnostics → Suppress OpenXR Import Warnings (optional — import warnings stay visible)",
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
