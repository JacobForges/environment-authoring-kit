#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Opens recap dashboard after recording stops and gates producer compose until the user proceeds
    /// (dashboard UI, Hub skip toggle, or optional auto-proceed timeout).
    /// </summary>
    static class CaveBuildRecapDashboardGate
    {
        const string PrefOpenBeforeCompose = "EnvironmentKit_RecapDashboard_OpenBeforeCompose";
        const string PrefSkipReview = "EnvironmentKit_RecapDashboard_SkipReview";
        const string PrefAutoProceedMinutes = "EnvironmentKit_RecapDashboard_AutoProceedMinutes";
        const string PrefPendingGateFolder = "EnvironmentKit_RecapDashboard_PendingGateFolder";
        const string EnsureScript = "ensure-recap-dashboard.py";
        const string LauncherScript = "start-recap-dashboard.sh";
        const int DefaultApiPort = 8765;
        const int VitePort = 5173;
        const string PortFileRelative = "recap-dashboard-port.txt";

        static string _waitingRunFolder;
        static double _gateStartedAt;
        static double _autoProceedDeadline;

        public static bool OpenRecapDashboardBeforeCompose
        {
            get
            {
                if (!EditorPrefs.HasKey(PrefOpenBeforeCompose))
                    return Application.platform == RuntimePlatform.OSXEditor;
                return EditorPrefs.GetBool(PrefOpenBeforeCompose, true);
            }
            set => EditorPrefs.SetBool(PrefOpenBeforeCompose, value);
        }

        public static bool SkipRecapReview
        {
            get => EditorPrefs.GetBool(PrefSkipReview, false);
            set => EditorPrefs.SetBool(PrefSkipReview, value);
        }

        /// <summary>0 = disabled; otherwise auto-proceed after N minutes waiting on dashboard.</summary>
        public static int AutoProceedTimeoutMinutes
        {
            get => EditorPrefs.GetInt(PrefAutoProceedMinutes, 0);
            set => EditorPrefs.SetInt(PrefAutoProceedMinutes, Mathf.Max(0, value));
        }

        public static bool IsWaitingForProceed => !string.IsNullOrEmpty(_waitingRunFolder);

        public static string WaitingRunFolder => _waitingRunFolder;

        /// <summary>
        /// After timeline + notes are written, optionally open dashboard and defer compose callback.
        /// </summary>
        public static void BeginOrRunCompose(string runFolder, Action composeAction)
        {
            if (composeAction == null || string.IsNullOrEmpty(runFolder))
                return;

            if (CaveBuildRecapComposeLatch.IsComposeCompleted(runFolder))
            {
                Debug.Log("[DemoRecorder] Compose already completed — skipping recap gate: " + runFolder);
                return;
            }

            if (SkipRecapReview || !OpenRecapDashboardBeforeCompose)
            {
                composeAction();
                return;
            }

            _waitingRunFolder = runFolder;
            EditorPrefs.SetString(PrefPendingGateFolder, runFolder);
            RegisterGateWithServer(runFolder);

            if (!TryEnsureDashboardReady(runFolder, out var dashboardUrl))
            {
                Debug.LogWarning(
                    "[DemoRecorder] Recap dashboard not ready yet — will retry ensure-recap-dashboard.py on gate poll.");
            }
            else
            {
                TryOpenBrowser(dashboardUrl);
            }

            _gateStartedAt = EditorApplication.timeSinceStartup;
            var timeoutMin = AutoProceedTimeoutMinutes;
            _autoProceedDeadline = timeoutMin > 0
                ? _gateStartedAt + timeoutMin * 60.0
                : 0;

            Debug.Log(
                "[DemoRecorder] Recap review gate active — verify cards in dashboard, then click Proceed.");

            void OnProceed()
            {
                EditorApplication.update -= PollGate;
                _waitingRunFolder = null;
                EditorPrefs.DeleteKey(PrefPendingGateFolder);
                composeAction();
            }

            var lastEnsureAttempt = 0.0;

            void PollGate()
            {
                if (!string.Equals(_waitingRunFolder, runFolder, StringComparison.Ordinal))
                {
                    EditorApplication.update -= PollGate;
                    return;
                }

                if (!IsApiHealthy() || !ApiHasReviewRoutes())
                {
                    if (EditorApplication.timeSinceStartup - lastEnsureAttempt >= 5.0)
                    {
                        lastEnsureAttempt = EditorApplication.timeSinceStartup;
                        if (TryEnsureDashboardReady(runFolder, out var url))
                            TryOpenBrowser(url);
                    }

                    return;
                }

                if (ShouldProceed(runFolder))
                {
                    EditorApplication.update -= PollGate;
                    OnProceed();
                }
            }

            EditorApplication.update += PollGate;
        }

        /// <summary>Resume gate polling after assembly reload if user had not proceeded yet.</summary>
        public static void TryResumePendingGate(Action<string> composeAction)
        {
            if (composeAction == null || !string.IsNullOrEmpty(_waitingRunFolder))
                return;

            var folder = EditorPrefs.GetString(PrefPendingGateFolder, string.Empty);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                if (!string.IsNullOrEmpty(folder))
                    EditorPrefs.DeleteKey(PrefPendingGateFolder);
                return;
            }

            if (CaveBuildRecapComposeLatch.IsComposeCompleted(folder))
            {
                Debug.Log("[DemoRecorder] Compose already completed — clearing stale recap gate: " + folder);
                EditorPrefs.DeleteKey(PrefPendingGateFolder);
                return;
            }

            if (SkipRecapReview || !OpenRecapDashboardBeforeCompose)
            {
                EditorPrefs.DeleteKey(PrefPendingGateFolder);
                composeAction(folder);
                return;
            }

            _waitingRunFolder = folder;
            _gateStartedAt = EditorApplication.timeSinceStartup;
            var timeoutMin = AutoProceedTimeoutMinutes;
            _autoProceedDeadline = timeoutMin > 0
                ? _gateStartedAt + timeoutMin * 60.0
                : 0;

            RegisterGateWithServer(folder);
            Debug.Log("[DemoRecorder] Resuming recap review gate after reload: " + folder);

            EditorApplication.update += PollResume;

            void PollResume()
            {
                if (!string.Equals(_waitingRunFolder, folder, StringComparison.Ordinal))
                {
                    EditorApplication.update -= PollResume;
                    return;
                }

                if (ShouldProceed(folder))
                {
                    EditorApplication.update -= PollResume;
                    _waitingRunFolder = null;
                    EditorPrefs.DeleteKey(PrefPendingGateFolder);
                    composeAction(folder);
                }
            }
        }

        static bool ShouldProceed(string runFolder)
        {
            if (SkipRecapReview)
                return true;

            if (_autoProceedDeadline > 0 &&
                EditorApplication.timeSinceStartup >= _autoProceedDeadline)
            {
                Debug.Log("[DemoRecorder] Recap review auto-proceed timeout elapsed.");
                return true;
            }

            return QueryGateProceed(runFolder);
        }

        static bool TryEnsureDashboardReady(string runFolder, out string dashboardUrl)
        {
            dashboardUrl = BuildDashboardUrl(runFolder);

            if (IsApiHealthy() && ApiHasReviewRoutes())
                return true;

            if (!TryEnsureDashboard(runFolder, openBrowser: true))
                return false;

            for (var i = 0; i < 20; i++)
            {
                if (IsApiHealthy() && ApiHasReviewRoutes())
                    return true;
                System.Threading.Thread.Sleep(250);
            }

            return IsApiHealthy() && ApiHasReviewRoutes();
        }

        static string BuildDashboardUrl(string runFolder)
        {
            var capture = Uri.EscapeDataString(runFolder);
            if (IsViteDevRunning())
                return $"http://127.0.0.1:{VitePort}/?capture={capture}";
            return $"http://127.0.0.1:{ResolveApiPort()}/?capture={capture}";
        }

        static int ResolveApiPort()
        {
            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var lexar = Path.Combine("/Volumes/Lexar/EnvironmentKit-Hub", PortFileRelative);
                var internalPath = Path.Combine(hub, "Library", "EnvironmentKit", PortFileRelative);
                foreach (var path in new[] { internalPath, lexar })
                {
                    if (!File.Exists(path))
                        continue;
                    var text = File.ReadAllText(path).Trim();
                    if (int.TryParse(text, out var port) && port > 0 && port < 65536)
                        return port;
                }
            }
            catch
            {
                // ignored
            }

            return DefaultApiPort;
        }

        static bool IsApiHealthy()
        {
            try
            {
                var json = HttpGet($"http://127.0.0.1:{ResolveApiPort()}/api/health", 1500);
                return !string.IsNullOrEmpty(json) &&
                       (json.Contains("\"ok\": true") || json.Contains("\"ok\":true"));
            }
            catch
            {
                return false;
            }
        }

        static bool ApiHasReviewRoutes()
        {
            try
            {
                var json = HttpGet($"http://127.0.0.1:{ResolveApiPort()}/api/approved/assets", 2000);
                return !string.IsNullOrEmpty(json) &&
                       !json.Contains("unknown api route") &&
                       json.Contains("approvedDir");
            }
            catch
            {
                return false;
            }
        }

        static bool IsViteDevRunning()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{VitePort}/");
                req.Method = "HEAD";
                req.Timeout = 800;
                using var resp = (HttpWebResponse)req.GetResponse();
                return resp.StatusCode == HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Kills stale API, starts detached server, registers gate. Optionally opens browser.
        /// </summary>
        static bool TryEnsureDashboard(string runFolder, bool openBrowser)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var ensurePy = Path.Combine(toolsDir, EnsureScript);
            if (!File.Exists(ensurePy))
            {
                var launcher = Path.Combine(toolsDir, LauncherScript);
                if (!File.Exists(launcher))
                    return false;
                try
                {
                    var bashArgs = openBrowser
                        ? $"\"{launcher}\" \"{runFolder}\" --open"
                        : $"\"{launcher}\" \"{runFolder}\" --no-open";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = bashArgs,
                        WorkingDirectory = toolsDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DemoRecorder] Could not run start-recap-dashboard.sh: " + ex.Message);
                    return false;
                }
            }

            try
            {
                var pyArgs = openBrowser
                    ? $"\"{ensurePy}\" \"{runFolder}\" --open"
                    : $"\"{ensurePy}\" \"{runFolder}\" --no-open";
                var psi = new ProcessStartInfo
                {
                    FileName = ResolvePython3(),
                    Arguments = pyArgs,
                    WorkingDirectory = toolsDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                    return false;
                proc.WaitForExit(45000);
                if (proc.ExitCode != 0)
                {
                    Debug.LogWarning(
                        "[DemoRecorder] ensure-recap-dashboard.py exited " + proc.ExitCode);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not run ensure-recap-dashboard.py: " + ex.Message);
                return false;
            }
        }

        static void RegisterGateWithServer(string runFolder)
        {
            try
            {
                var body = "{\"waiting\":true}";
                HttpPost(
                    $"http://127.0.0.1:{ResolveApiPort()}/api/capture/compose/gate?path={Uri.EscapeDataString(runFolder)}",
                    body,
                    "POST");
            }
            catch
            {
                // Server may still be starting; polling will retry.
            }
        }

        static bool QueryGateProceed(string runFolder)
        {
            try
            {
                var json = HttpGet(
                    $"http://127.0.0.1:{ResolveApiPort()}/api/capture/compose/gate?path={Uri.EscapeDataString(runFolder)}",
                    2500);
                if (string.IsNullOrEmpty(json))
                    return false;
                return json.Contains("\"proceed\": true") || json.Contains("\"proceed\":true");
            }
            catch
            {
                return false;
            }
        }

        static void TryOpenBrowser(string url)
        {
            if (Application.platform != RuntimePlatform.OSXEditor &&
                Application.platform != RuntimePlatform.OSXPlayer)
            {
                Application.OpenURL(url);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = "\"" + url + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not open browser: " + ex.Message);
                Application.OpenURL(url);
            }
        }

        static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using var resp = (HttpWebResponse)req.GetResponse();
            using var stream = resp.GetResponseStream();
            if (stream == null)
                return string.Empty;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        static string HttpPost(string url, string jsonBody, string method)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.ContentType = "application/json";
            req.Timeout = 5000;
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            req.ContentLength = bytes.Length;
            using (var stream = req.GetRequestStream())
                stream.Write(bytes, 0, bytes.Length);
            using var resp = (HttpWebResponse)req.GetResponse();
            using var reader = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        static string ResolvePython3()
        {
            foreach (var candidate in new[]
                     {
                         "/opt/homebrew/bin/python3",
                         "/usr/local/bin/python3",
                         "/usr/bin/python3",
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "python3";
        }
    }
}
#endif
