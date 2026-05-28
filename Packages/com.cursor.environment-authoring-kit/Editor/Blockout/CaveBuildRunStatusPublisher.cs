#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Human-readable live status during builds (open in Pipeline Console or Generated MD).</summary>
    public static class CaveBuildRunStatusPublisher
    {
        public const string LiveStatusRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildLiveRunStatus.md";

        static string _phase = "idle";
        static string _detail = string.Empty;
        static string _subOperation = string.Empty;
        static string _subOperationDetail = string.Empty;
        static string _researchNote = string.Empty;
        static string _buildMode = string.Empty;
        static int _queuedStep = -1;
        static int _queuedTotal = CaveBuildQueuedPipelineSchedule.Total;

        public static int CurrentQueuedStep => _queuedStep;

        public static int QueuedStepTotal => _queuedTotal;
        static double _startedAt;
        static double _lastPublishTime;
        static bool _caveOnlySession;

        public static void BeginSession(string sceneName, int seed, bool additiveSurface)
        {
            _caveOnlySession = false;
            _startedAt = EditorApplication.timeSinceStartup;
            _phase = "starting";
            _detail = $"Scene {sceneName}, seed {seed}";
            _researchNote = "Pending pre-placement research…";
            _buildMode = additiveSurface ? "[Surface] additive on existing land" : "[Surface] full world replace";
            _queuedStep = 0;
            Publish();
            CaveBuildLiveSceneFeedback.BeginBuildSession();
            CaveBuildPipelineLog.Info(
                $"═══ Build session started — {_buildMode} — seed {seed} ═══",
                "RunStatus");
            CaveBuildPipelineDomains.LogSurface($"Build session — {_buildMode} — seed {seed}");
        }

        public static void BeginCaveContinuationSession(string sceneName, int seed)
        {
            _caveOnlySession = true;
            _startedAt = EditorApplication.timeSinceStartup;
            _phase = "cave_continuation";
            _detail = $"Scene {sceneName}, seed {seed}";
            _researchNote = "Skipped — pre-build + surface already complete.";
            _buildMode = "[Cave] only — surface frozen (no terrain edits)";
            _queuedStep = 0;
            Publish();
            CaveBuildLiveSceneFeedback.BeginBuildSession();
            CaveBuildPipelineLog.Info(
                $"═══ Cave continuation — surface frozen — seed {seed} ═══",
                "Cave-RunStatus");
            CaveBuildPipelineDomains.LogCave(
                $"Continuation session — seed {seed} — surface frozen",
                forceUnityConsole: true);
        }

        public enum ResearchGateState
        {
            InProgress,
            Passed,
            Failed,
        }

        public static void SetResearchPhase(string detail, bool passed) =>
            SetResearchPhase(detail, passed ? ResearchGateState.Passed : ResearchGateState.Failed);

        public static void SetResearchPhase(string detail, ResearchGateState state)
        {
            _phase = "pre_placement_research";
            _detail = detail;
            _researchNote = state switch
            {
                ResearchGateState.Passed =>
                    "Research cache + execution brief ready — safe to place geometry.",
                ResearchGateState.InProgress =>
                    "Research sync in progress (reusing on-disk cache when possible)…",
                _ =>
                    "Research sync failed — check Console for node/tsx/npm errors.",
            };
            Publish();
            if (_caveOnlySession)
                return;

            var level = state == ResearchGateState.Failed ? LogType.Warning : LogType.Log;
            Debug.LogFormat(level, LogOption.None, null, "{0} Research gate: {1} {2}",
                CaveBuildPipelineDomains.Cave, _researchNote, detail);
        }

        public static void SetSubOperation(string operation, string detail)
        {
            _subOperation = operation ?? string.Empty;
            _subOperationDetail = detail ?? string.Empty;
            Publish(force: true);
        }

        public static void PulseSubOperation(string operation, string detail)
        {
            _subOperation = operation ?? string.Empty;
            _subOperationDetail = detail ?? string.Empty;
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastPublishTime < 0.4)
                return;
            Publish(force: true);
        }

        public static void ClearSubOperation()
        {
            _subOperation = string.Empty;
            _subOperationDetail = string.Empty;
        }

        public static void SetQueuedStep(int step, int total, string label)
        {
            _queuedStep = step;
            _queuedTotal = total;
            _phase = "pipeline";
            _detail = label;
            ClearSubOperation();
            Publish(force: true);
            CaveBuildPipelineLog.Info($"Step {step + 1}/{total}: {label}", _caveOnlySession ? "Cave-Pipeline" : "Pipeline");

            var env = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            Transform focus = null;
            if (env != null)
            {
                if (_caveOnlySession)
                {
                    focus = env.transform.Find(CaveGeometryPaths.CaveSystemRootName);
                    if (focus == null)
                        focus = env.transform.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                }
                else
                {
                    focus = env.transform.Find(SurfaceWorldPaths.RootName);
                    if (focus == null)
                        focus = env.transform.Find(CaveGeometryPaths.CaveSystemRootName);
                    if (focus == null)
                        focus = env.transform.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                }
            }

            var liveLabel = $"{CaveBuildPipelineDomains.CaveLive} Step {step + 1}/{total}: {label}";
            CaveBuildLiveSceneFeedback.NotifyStep(liveLabel, focus, frameScene: step % 3 == 0);
        }

        public static void SetPhase(string phase, string detail)
        {
            _phase = phase;
            _detail = detail;
            Publish();
        }

        public static void Publish(bool force = false)
        {
            var now = EditorApplication.timeSinceStartup;
            if (!force && now - _lastPublishTime < 0.35)
                return;
            _lastPublishTime = now;

            var elapsed = now - _startedAt;
            var sb = new StringBuilder();
            sb.AppendLine("# Cave build — live status");
            sb.AppendLine();
            sb.AppendLine($"**Updated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Elapsed:** {elapsed:F0}s");
            sb.AppendLine($"**Phase:** `{_phase}`");
            sb.AppendLine($"**Detail:** {_detail}");
            if (!string.IsNullOrEmpty(_subOperation))
            {
                sb.AppendLine($"**Working now:** {_subOperation}");
                if (!string.IsNullOrEmpty(_subOperationDetail))
                    sb.AppendLine($"**Last activity:** {_subOperationDetail}");
            }

            sb.AppendLine($"**Build mode:** {_buildMode}");
            sb.AppendLine($"**Research:** {_researchNote}");
            if (_queuedStep >= 0)
                sb.AppendLine($"**Pipeline step:** {_queuedStep + 1} / {_queuedTotal}");
            sb.AppendLine();
            sb.AppendLine("## Rules (active this run)");
            if (_caveOnlySession)
            {
                sb.AppendLine("- **[Cave] only** — do not edit surface terrain, LiDAR, or SurfaceWorld roots");
                sb.AppendLine("- Surface was built in pre-build; mouth anchor only for cave alignment");
            }
            else
            {
                sb.AppendLine("- **[Surface]** runs in pre-build / SurfaceWorldGenerator (not cave macro steps)");
                sb.AppendLine("- **[Cave]** research before placement (see CaveBuildResearchExecutionBrief.md)");
                sb.AppendLine("- **Additive** — extend existing Ground/Terrain; no radial overwrite of center disk");
            }

            sb.AppendLine("- compile_gate: ignore stale errors in CaveBuildCompileDiagnostics.json");
            sb.AppendLine();
            sb.AppendLine("## Recent log (tail)");
            sb.AppendLine("```");
            var tail = CaveBuildPipelineLog.GetRecentText(24);
            sb.AppendLine(string.IsNullOrEmpty(tail) ? "(no log yet)" : tail);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("Open **Cave Build → Diagnostics → Pipeline Console** for full stream.");

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, LiveStatusRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, sb.ToString());
        }
    }
}
#endif
