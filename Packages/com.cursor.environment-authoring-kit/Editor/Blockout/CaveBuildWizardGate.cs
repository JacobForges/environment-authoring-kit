#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Opens the React build planner in the browser; starts the Hub build after Finalize.
    /// </summary>
    [InitializeOnLoad]
    public static class CaveBuildWizardGate
    {
        const string EnsureScript = "ensure-build-wizard.py";
        const int ApiPort = 8766;

        static Action _pendingStart;
        static double _gateStartedAt;
        static double _lastOrphanCheckAt;

        static CaveBuildWizardGate()
        {
            EditorApplication.update += Tick;
        }

        public static void OpenPreview()
        {
            var hub = Uri.EscapeDataString(CaveBuildCursorSettings.ResolveHubRoot());
            TryEnsureWizard($"http://127.0.0.1:{ApiPort}/?hub={hub}");
        }

        /// <summary>Hub button when planner finalized JSON exists but pipeline never started.</summary>
        public static void StartFinalizedPlanNow()
        {
            if (LavaTubeCaveBuilder.TryStartFullWorldFromPlannerFinalize())
                return;

            Debug.LogWarning(
                "[CaveBuild] Could not start from finalized plan — is Ground tagged? Is a build already running?");
        }

        public static void RequestFullWorldBuild(Action startBuild)
        {
            if (startBuild == null)
                return;

            // Plan already approved in browser — start immediately (don't wipe active JSON).
            if (CaveBuildSessionConfig.TryReadWizardPhase(out var existing) &&
                string.Equals(existing, "finalized", StringComparison.OrdinalIgnoreCase) &&
                CaveBuildSessionConfig.HasFinalizedActive)
            {
                _pendingStart = startBuild;
                if (!CaveBuildSessionConfig.ReloadAndApplyPlannerSession(out var active))
                {
                    _pendingStart = null;
                    return;
                }

                CaveBuildSessionConfig.PrepareFreshBuild(active);
                Debug.Log(
                    $"[CaveBuild] Finalized planner session found — starting {active?.label ?? "plan"} without reopening browser.");
                BeginFinalizedBuild(startBuild);
                _pendingStart = null;
                CaveBuildSessionConfig.WriteWizardState("started");
                return;
            }

            _pendingStart = startBuild;
            _gateStartedAt = EditorApplication.timeSinceStartup;
            CaveBuildSessionConfig.ClearActive();
            CaveBuildSessionConfig.WriteWizardState("awaiting_finalize");

            var hub = Uri.EscapeDataString(CaveBuildCursorSettings.ResolveHubRoot());
            if (!TryEnsureWizard($"http://127.0.0.1:{ApiPort}/?hub={hub}"))
            {
                Debug.LogWarning(
                    "[CaveBuild] Build wizard server not ready — using Hub defaults. " +
                    "Run: python3 Packages/.../Tools/cave-grader/ensure-build-wizard.py");
                var fallback = CaveBuildSessionConfig.CreateDefaults();
                CaveBuildSessionConfig.PrepareFreshBuild(fallback);
                _pendingStart = null;
                startBuild();
            }
        }

        static void Tick()
        {
            PollPendingFinalize();
            PollOrphanedPlannerFinalize();
        }

        static void PollPendingFinalize()
        {
            if (_pendingStart == null)
                return;

            var active = CaveBuildSessionConfig.LoadActive();
            if (active != null && CaveBuildSessionConfig.TryReadWizardPhase(out var phase) &&
                string.Equals(phase, "finalized", StringComparison.OrdinalIgnoreCase))
            {
                var start = _pendingStart;
                _pendingStart = null;
                CaveBuildSessionConfig.ReloadAndApplyPlannerSession(out active);
                if (active == null)
                    return;

                CaveBuildSessionConfig.PrepareFreshBuild(active);
                Debug.Log(
                    $"[CaveBuild] Build planner finalized — {active.label}, {CaveBuildSessionConfig.DescribeActiveTilePlan()}, " +
                    $"props={(active.playDiskProps ? "on" : "off")}, seed mode={(active.randomSeedEachBuild ? "random" : "fixed")}.");
                BeginFinalizedBuild(start);
                CaveBuildSessionConfig.WriteWizardState("started");
                return;
            }

            if (CaveBuildSessionConfig.TryReadWizardPhase(out var p) &&
                string.Equals(p, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                _pendingStart = null;
                Debug.Log("[CaveBuild] Build wizard cancelled.");
                return;
            }

            if (EditorApplication.timeSinceStartup - _gateStartedAt > 3600.0)
            {
                _pendingStart = null;
                Debug.LogWarning("[CaveBuild] AI build planner timed out after 60 minutes.");
            }
        }

        static void PollOrphanedPlannerFinalize()
        {
            if (_pendingStart != null)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastOrphanCheckAt < 0.35)
                return;

            _lastOrphanCheckAt = now;

            if (!CaveBuildSessionConfig.HasFinalizedActive ||
                !CaveBuildSessionConfig.TryReadWizardPhase(out var phase) ||
                !string.Equals(phase, "finalized", StringComparison.OrdinalIgnoreCase))
                return;

            if (CaveBuildHubSessionReconcile.IsCoreBuildRunning())
                return;

            LavaTubeCaveBuilder.TryStartFullWorldFromPlannerFinalize();
        }

        internal static void BeginFinalizedBuild(Action start)
        {
            CaveBuildSessionConfig.ReloadAndApplyPlannerSession(out _);
            FocusUnityEditor();
            EnvironmentKitHubWindow.EnsureOpenForBuild();
            CaveBuildDemoAutoRecorder.OnHubBuildStarting("AI planner finalized");
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "hub",
                "Planner finalized — starting build + recording…");

            // Defer one editor tick so macOS focus + Hub window can settle before the pipeline queues.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    start?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            };
        }

        static void FocusUnityEditor()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/osascript",
                        Arguments = "-e 'tell application \"Unity\" to activate'",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[CaveBuild] Could not activate Unity Editor: " + ex.Message);
                }
            }
        }

        static bool TryEnsureWizard(string url)
        {
            var tools = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader");
            var script = Path.Combine(tools, EnsureScript);
            if (!File.Exists(script))
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/python3",
                    Arguments = $"\"{script}\" --no-open --restart",
                    WorkingDirectory = tools,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(8000);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] ensure-build-wizard: " + ex.Message);
                return false;
            }

            Application.OpenURL(url);
            return true;
        }
    }
}
#endif
