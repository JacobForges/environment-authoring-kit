#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        const int MaxActivityLines = 64;

        public readonly struct ActivityEntry
        {
            public readonly float ElapsedSeconds;
            public readonly string Category;
            public readonly string Text;

            public ActivityEntry(float elapsedSeconds, string category, string text)
            {
                ElapsedSeconds = elapsedSeconds;
                Category = category ?? string.Empty;
                Text = text ?? string.Empty;
            }
        }

        static readonly List<ActivityEntry> Activity = new();

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

        public static string Phase => _phase;

        public static string Detail => _detail;

        public static string SubOperation => _subOperation;

        public static string SubOperationDetail => _subOperationDetail;

        public static string BuildMode => _buildMode;

        public static string ResearchNote => _researchNote;

        public static float ElapsedSeconds =>
            _startedAt > 0 ? (float)(EditorApplication.timeSinceStartup - _startedAt) : 0f;

        public static bool HasActiveSession =>
            _startedAt > 0 && !string.Equals(_phase, "idle", StringComparison.Ordinal);

        public static IReadOnlyList<ActivityEntry> RecentActivity => Activity;

        static double _startedAt;
        static double _lastPublishTime;
        static bool _caveOnlySession;

        public static void ClearActivityFeed() => Activity.Clear();

        public static void RecordActivity(string category, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var elapsed = _startedAt > 0 ? (float)(EditorApplication.timeSinceStartup - _startedAt) : 0f;
            Activity.Add(new ActivityEntry(elapsed, category, text.Trim()));
            while (Activity.Count > MaxActivityLines)
                Activity.RemoveAt(0);
        }

        public static string FormatActivityFeedForHub(int maxLines = 36)
        {
            if (Activity.Count == 0)
                return "(waiting for first activity — build is starting or queue is idle)";

            var start = Mathf.Max(0, Activity.Count - maxLines);
            var sb = new StringBuilder();
            for (var i = start; i < Activity.Count; i++)
            {
                var e = Activity[i];
                sb.Append('[').Append(e.ElapsedSeconds.ToString("F0")).Append("s] ");
                if (!string.IsNullOrEmpty(e.Category))
                    sb.Append('(').Append(e.Category).Append(") ");
                sb.AppendLine(e.Text);
            }

            return sb.ToString().TrimEnd();
        }

        public static string FormatMainActionSummary()
        {
            if (_queuedStep >= 0)
            {
                return $"Build {_queuedStep + 1}/{_queuedTotal} — {_detail}";
            }

            return string.IsNullOrEmpty(_detail) ? _phase : $"{_phase} — {_detail}";
        }

        public static string FormatSubActionSummary()
        {
            if (string.IsNullOrEmpty(_subOperation) && string.IsNullOrEmpty(_subOperationDetail))
                return string.Empty;

            return string.IsNullOrEmpty(_subOperationDetail)
                ? _subOperation
                : $"{_subOperation} — {_subOperationDetail}";
        }

        public static void BeginSession(string sceneName, int seed, bool additiveSurface)
        {
            ClearActivityFeed();
            _caveOnlySession = false;
            _startedAt = EditorApplication.timeSinceStartup;
            _phase = "starting";
            _detail = $"Scene {sceneName}, seed {seed}";
            _researchNote = "Pending pre-placement research…";
            _buildMode = additiveSurface ? "[Surface] additive on existing land" : "[Surface] full world replace";
            _queuedStep = 0;
            RecordActivity("session", $"Build started — {_buildMode}, seed {seed}");
            Publish();
            CaveBuildLiveSceneFeedback.BeginBuildSession();
            CaveBuildPipelineLog.Info(
                $"═══ Build session started — {_buildMode} — seed {seed} ═══",
                "RunStatus");
            CaveBuildPipelineDomains.LogSurface($"Build session — {_buildMode} — seed {seed}");
        }

        public static void BeginCaveContinuationSession(string sceneName, int seed)
        {
            ClearActivityFeed();
            _caveOnlySession = true;
            _startedAt = EditorApplication.timeSinceStartup;
            _phase = "cave_continuation";
            _detail = $"Scene {sceneName}, seed {seed}";
            _researchNote = "Skipped — pre-build + surface already complete.";
            _buildMode = "[Cave] only — surface frozen (no terrain edits)";
            _queuedStep = 0;
            RecordActivity("session", $"Cave continuation — seed {seed}");
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
            RecordActivity("research", _detail);
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
            RecordActivity("sub", FormatSubActionSummary());
            Publish(force: true);
        }

        public static void PulseSubOperation(string operation, string detail)
        {
            _subOperation = operation ?? string.Empty;
            _subOperationDetail = detail ?? string.Empty;
            RecordActivity("sub", FormatSubActionSummary());
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastPublishTime < 0.25)
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
            RecordActivity("step", $"Build {step + 1}/{total}: {label}");
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
            CaveBuildLiveSceneFeedback.NotifyStep(liveLabel, focus, frameScene: true);
        }

        public static void SetPhase(string phase, string detail)
        {
            _phase = phase;
            _detail = detail;
            RecordActivity("main", FormatMainActionSummary());
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
            sb.AppendLine("## Activity feed (sub-steps)");
            sb.AppendLine("```");
            sb.AppendLine(FormatActivityFeedForHub(40));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## Recent log (tail)");
            sb.AppendLine("```");
            var tail = CaveBuildPipelineLog.GetRecentText(32);
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
