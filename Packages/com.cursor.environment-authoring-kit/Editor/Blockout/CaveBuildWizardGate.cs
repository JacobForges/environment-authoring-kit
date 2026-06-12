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
        static string _orphanAutoStartConsumedUtc;

        static CaveBuildWizardGate()
        {
            EditorApplication.update += Tick;
        }

        public static void OpenPreview()
        {
            CaveBuildPlannerKitCatalogExporter.ExportIfStale(out _, force: false);
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
            CaveBuildDemoAutoRecorder.OnHubBuildStarting("AI build planner");

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

        public const string KitCatalogExportRequestRel =
            "Assets/EnvironmentKit/Generated/planner-kit-catalog-export.request";
        public const string KitCatalogExportDoneRel =
            "Assets/EnvironmentKit/Generated/planner-kit-catalog-export.done.json";

        public const string ConceptCardThumbRequestRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.request.json";
        public const string ConceptCardThumbDoneRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.done.json";

        static void Tick()
        {
            PollKitCatalogExportRequest();
            PollConceptCardThumbRequest();
            PollConceptCardMeshRequest();
            PollConceptCardSculptRequest();
            PollPendingFinalize();
            PollOrphanedPlannerFinalize();
        }

        public const string ConceptCardMeshRequestRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.request.json";
        public const string ConceptCardMeshDoneRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.done.json";

        public const string ConceptCardSculptRequestRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.request.json";
        public const string ConceptCardSculptDoneRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.done.json";

        static void PollConceptCardSculptRequest()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var requestPath = Path.Combine(hub, ConceptCardSculptRequestRel);
            if (!File.Exists(requestPath))
                return;

            string json;
            try
            {
                json = File.ReadAllText(requestPath);
                File.Delete(requestPath);
            }
            catch (IOException)
            {
                return;
            }

            CaveBuildPlannerGeneratedCharacters.CardSculptRequest req = null;
            try
            {
                req = JsonUtility.FromJson<CaveBuildPlannerGeneratedCharacters.CardSculptRequest>(json);
            }
            catch
            {
                req = null;
            }

            var ok = false;
            string prefabRel = null;
            var msg = "Invalid character sculpt request.";
            if (req != null && !string.IsNullOrWhiteSpace(req.sourcePrefabPath))
            {
                ok = CaveBuildPlannerGeneratedCharacters.TrySculptForCard(req, out prefabRel, out msg);
            }

            var donePath = Path.Combine(hub, ConceptCardSculptDoneRel);
            try
            {
                var doneDir = Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(doneDir))
                    Directory.CreateDirectory(doneDir);
                File.WriteAllText(
                    donePath,
                    $"{{\"ok\":{(ok ? "true" : "false")},\"cardId\":{JsonEscape(req?.cardId)},"
                    + $"\"prefabPath\":{JsonEscape(prefabRel)},\"message\":{JsonEscape(msg)}}}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlannerCharacters] Could not write sculpt done file: " + ex.Message);
            }

            if (ok)
                Debug.Log("[PlannerCharacters] " + msg);
            else
                Debug.LogWarning("[PlannerCharacters] Sculpt failed: " + msg);
        }

        static void PollConceptCardMeshRequest()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var requestPath = Path.Combine(hub, ConceptCardMeshRequestRel);
            if (!File.Exists(requestPath))
                return;

            string json;
            try
            {
                json = File.ReadAllText(requestPath);
                File.Delete(requestPath);
            }
            catch (IOException)
            {
                return;
            }

            CaveBuildPlannerGeneratedProps.CardMeshRequest req = null;
            try
            {
                req = JsonUtility.FromJson<CaveBuildPlannerGeneratedProps.CardMeshRequest>(json);
            }
            catch
            {
                req = null;
            }

            var ok = false;
            string prefabRel = null;
            string thumbRel = null;
            var msg = "Invalid card mesh request.";
            if (req != null && !string.IsNullOrWhiteSpace(req.cardId))
            {
                ok = CaveBuildPlannerGeneratedProps.TryGenerateForCard(
                    req.cardId,
                    req.categoryKey,
                    req.label,
                    req.seed,
                    req.regenIndex,
                    req.aiSpec,
                    out prefabRel,
                    out thumbRel,
                    out msg);
            }

            var donePath = Path.Combine(hub, ConceptCardMeshDoneRel);
            try
            {
                var doneDir = Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(doneDir))
                    Directory.CreateDirectory(doneDir);
                var thumbRelJson = JsonEscape(
                    ok ? (thumbRel ?? "") : "");
                File.WriteAllText(
                    donePath,
                    $"{{\"ok\":{(ok ? "true" : "false")},\"cardId\":{JsonEscape(req?.cardId)},"
                    + $"\"prefabPath\":{JsonEscape(prefabRel)},\"thumbRel\":{thumbRelJson},"
                    + $"\"message\":{JsonEscape(msg)}}}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlannerProps] Could not write card mesh done file: " + ex.Message);
            }

            if (ok)
                Debug.Log("[PlannerProps] " + msg);
            else
                Debug.LogWarning("[PlannerProps] Card mesh failed: " + msg);
        }

        [Serializable]
        sealed class CardThumbRequest
        {
            public string prefabPath;
            public string cardId;
        }

        static void PollConceptCardThumbRequest()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var requestPath = Path.Combine(hub, ConceptCardThumbRequestRel);
            if (!File.Exists(requestPath))
                return;

            string json;
            try
            {
                json = File.ReadAllText(requestPath);
                File.Delete(requestPath);
            }
            catch (IOException)
            {
                return;
            }

            CardThumbRequest req = null;
            try
            {
                req = JsonUtility.FromJson<CardThumbRequest>(json);
            }
            catch
            {
                req = null;
            }

            var ok = false;
            var thumbRel = string.Empty;
            var msg = "Invalid card thumb request.";
            if (req != null && !string.IsNullOrWhiteSpace(req.prefabPath))
            {
                thumbRel = CaveBuildPlannerKitCatalogExporter.ExportThumbnailForPrefab(req.prefabPath);
                ok = !string.IsNullOrEmpty(thumbRel);
                msg = ok
                    ? $"Exported preview for {Path.GetFileNameWithoutExtension(req.prefabPath)}"
                    : $"Could not render preview for {req.prefabPath}";
            }

            var donePath = Path.Combine(hub, ConceptCardThumbDoneRel);
            try
            {
                var doneDir = Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(doneDir))
                    Directory.CreateDirectory(doneDir);
                File.WriteAllText(
                    donePath,
                    $"{{\"ok\":{(ok ? "true" : "false")},\"thumbRel\":{JsonEscape(thumbRel)},"
                    + $"\"prefabPath\":{JsonEscape(req?.prefabPath)},\"message\":{JsonEscape(msg)}}}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlannerKitCatalog] Could not write card thumb done file: " + ex.Message);
            }

            if (ok)
                Debug.Log("[PlannerKitCatalog] " + msg);
            else
                Debug.LogWarning("[PlannerKitCatalog] " + msg);
        }

        /// <summary>Wizard server drops a request file when batchmode cannot run (editor already open).</summary>
        static void PollKitCatalogExportRequest()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var requestPath = Path.Combine(hub, KitCatalogExportRequestRel);
            if (!File.Exists(requestPath))
                return;

            try
            {
                File.Delete(requestPath);
            }
            catch (IOException)
            {
                return;
            }

            var ok = CaveBuildPlannerKitCatalogExporter.ExportIfStale(out var msg, force: true);
            var donePath = Path.Combine(hub, KitCatalogExportDoneRel);
            try
            {
                var doneDir = Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(doneDir))
                    Directory.CreateDirectory(doneDir);
                File.WriteAllText(
                    donePath,
                    $"{{\"ok\":{(ok ? "true" : "false")},\"message\":{JsonEscape(msg)},\"thumbCount\":{CountKitThumbs(hub)}}}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlannerKitCatalog] Could not write done file: " + ex.Message);
            }

            if (ok)
                Debug.Log("[PlannerKitCatalog] " + msg);
            else
                Debug.LogWarning("[PlannerKitCatalog] Export failed: " + msg);
        }

        static int CountKitThumbs(string hub)
        {
            var dir = Path.Combine(hub, CaveBuildPlannerKitCatalogExporter.ThumbsRel);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Length : 0;
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
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

            var active = CaveBuildSessionConfig.LoadActive();
            if (active == null)
                return;

            // One auto-start per approved plan — do not relaunch after the operator idles post-build.
            if (!string.IsNullOrEmpty(active.finalizedUtc) &&
                string.Equals(_orphanAutoStartConsumedUtc, active.finalizedUtc, StringComparison.Ordinal))
                return;

            if (!LavaTubeCaveBuilder.TryStartFullWorldFromPlannerFinalize())
                return;

            _orphanAutoStartConsumedUtc = active.finalizedUtc;
        }

        internal static void BeginFinalizedBuild(Action start)
        {
            CaveBuildSessionConfig.ReloadAndApplyPlannerSession(out _);
            FocusUnityEditor();
            EnvironmentKitHubWindow.EnsureOpenForBuild();
            CaveBuildDemoAutoRecorder.OnHubBuildStarting("Build pipeline");
            CaveBuildDemoAutoRecorder.AppendChapter("world_build", "World build pipeline", "build", "unity");
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
