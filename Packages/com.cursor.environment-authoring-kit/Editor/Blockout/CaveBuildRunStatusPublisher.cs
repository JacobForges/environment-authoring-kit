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

        /// <summary>Live updates during builds — outside Assets so Unity does not re-import every pulse.</summary>
        public const string LiveStatusLibraryRel = "Library/EnvironmentKit/CaveBuildLiveRunStatus.md";

        const int MaxActivityLines = 64;
        const double MinPublishIntervalSeconds = 0.35;
        const double HeartbeatPublishIntervalSeconds = 0.2;
        const double MinAssetsMirrorIntervalSeconds = 8.0;
        const double MinForcedAssetsMirrorIntervalSeconds = 3.0;

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
        static double _lastAssetsMirrorTime;
        static bool _caveOnlySession;
        static bool _heartbeatHooked;
        static string _lastPulsedSubSummary = string.Empty;

        /// <summary>Prefer Library snapshot (live); fall back to Generated copy for older runs.</summary>
        public static string GetLiveStatusReadRel()
        {
            var library = EnvironmentKitDataRoot.ResolvePath("CaveBuildLiveRunStatus.md");
            return File.Exists(library) ? LiveStatusLibraryRel : LiveStatusRel;
        }

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
            if (CaveBuildStepCounter.HasSession && CaveBuildStepCounter.Current > 0)
            {
                var phaseLine = CaveBuildStepCounter.FormatHubPhaseLine();
                return string.IsNullOrEmpty(phaseLine)
                    ? $"Paced step {CaveBuildStepCounter.FormatHubStepLine()} — {_detail}"
                    : $"Paced step {CaveBuildStepCounter.FormatHubStepLine()} · {phaseLine}";
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

        /// <summary>In-memory live document (Hub + Pipeline Console). Always current elapsed.</summary>
        public static string BuildLiveStatusMarkdown()
        {
            var now = EditorApplication.timeSinceStartup;
            var elapsed = _startedAt > 0 ? now - _startedAt : 0;
            var sb = new StringBuilder();
            sb.AppendLine("# Cave build — live status");
            sb.AppendLine();
            sb.AppendLine($"**Updated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Elapsed:** {elapsed:F0}s");
            sb.AppendLine($"**Paced steps:** {CaveBuildStepCounter.FormatHubStepLine()}");
            sb.AppendLine($"**ETC (paced):** {CaveBuildStepCounter.FormatEtc()}");
            sb.AppendLine($"**Phase:** `{_phase}`");
            if (!string.IsNullOrEmpty(CaveBuildPipelinePhaseTracker.CurrentPhaseLabel))
                sb.AppendLine($"**Phase detail:** {CaveBuildPipelinePhaseTracker.CurrentPhaseLabel}");
            sb.AppendLine($"**Detail:** {_detail}");
            sb.AppendLine(
                $"**RAM (editor):** {Mathf.RoundToInt(CaveBuildHardwareMonitor.LastUsage01 * 100f)}% of ~{EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb():F0} GB budget");
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
            sb.AppendLine("Open **Environment Kit Hub → Build** for live activity (optional pop-out Pipeline Console).");
            return sb.ToString();
        }

        public static bool TryGetLiveStatusPreview(int maxChars, out string preview)
        {
            preview = BuildLiveStatusMarkdown();
            if (preview.Length > maxChars)
                preview = preview.Substring(0, maxChars) + "\n…";
            return HasActiveSession || !string.IsNullOrEmpty(_detail);
        }

        static void EnsureHeartbeat()
        {
            if (_heartbeatHooked)
                return;
            _heartbeatHooked = true;
            EditorApplication.update += OnHeartbeat;
        }

        static void StopHeartbeat()
        {
            if (!_heartbeatHooked)
                return;
            EditorApplication.update -= OnHeartbeat;
            _heartbeatHooked = false;
        }

        static void OnHeartbeat()
        {
            if (!HasActiveSession)
            {
                StopHeartbeat();
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastPublishTime < HeartbeatPublishIntervalSeconds)
                return;
            Publish(force: true);
        }

        public static void EndSession()
        {
            StopHeartbeat();
            _phase = "idle";
            _queuedStep = -1;
            ClearSubOperation();
            _lastPulsedSubSummary = string.Empty;
            CaveBuildStepCounter.EndSession();
            Publish(force: true);
            _startedAt = 0;
            CaveBuildDemoAutoRecorder.TryFinalizeOnBuildSessionEnd();
        }

        public static void BeginSession(string sceneName, int seed, bool additiveSurface)
        {
            ClearActivityFeed();
            if (!CaveBuildStepCounter.HasSession)
                CaveBuildStepCounter.BeginSession();
            CaveBuildHardwareMonitor.EnsureSampling();
            _caveOnlySession = false;
            _lastPulsedSubSummary = string.Empty;
            _startedAt = EditorApplication.timeSinceStartup;
            EnsureHeartbeat();
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
            if (!CaveBuildStepCounter.HasSession)
                CaveBuildStepCounter.BeginSession();
            _caveOnlySession = true;
            _startedAt = EditorApplication.timeSinceStartup;
            _phase = "cave_continuation";
            _detail = $"Scene {sceneName}, seed {seed}";
            _researchNote = "Skipped — pre-build + surface already complete.";
            _buildMode = "[Cave] only — surface frozen (no terrain edits)";
            _queuedStep = 0;
            _lastPulsedSubSummary = string.Empty;
            RecordActivity("session", $"Cave continuation — seed {seed}");
            EnsureHeartbeat();
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
            if (!string.IsNullOrEmpty(operation) &&
                (operation.Contains("surface", StringComparison.OrdinalIgnoreCase) ||
                 operation.Contains("terrain", StringComparison.OrdinalIgnoreCase) ||
                 operation.Contains("FullWorld", StringComparison.OrdinalIgnoreCase) ||
                 operation.Contains("play perimeter", StringComparison.OrdinalIgnoreCase) ||
                 operation.Contains("mountain", StringComparison.OrdinalIgnoreCase)))
            {
                _phase = "surface_build";
            }

            _subOperation = operation ?? string.Empty;
            _subOperationDetail = detail ?? string.Empty;
            var summary = FormatSubActionSummary();
            RecordActivity("sub", summary);
            var changed = !string.Equals(summary, _lastPulsedSubSummary, StringComparison.Ordinal);
            _lastPulsedSubSummary = summary;
            var now = EditorApplication.timeSinceStartup;
            if (!changed && now - _lastPublishTime < 0.25)
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
            CaveBuildPipelinePhaseTracker.OnCaveMacroStep(step, total, label);
            _phase = "pipeline";
            _detail = label;
            ClearSubOperation();
            RecordActivity("pipeline", label);
            Publish(force: true);
            CaveBuildPipelineLog.Info($"Pipeline segment {step + 1}/{total}: {label}", _caveOnlySession ? "Cave-Pipeline" : "Pipeline");

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

            var liveLabel = $"{CaveBuildPipelineDomains.CaveLive} Step {CaveBuildStepCounter.Current}: {label}";
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
            if (!force && now - _lastPublishTime < MinPublishIntervalSeconds)
                return;
            _lastPublishTime = now;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var text = BuildLiveStatusMarkdown();

            var libraryPath = EnvironmentKitDataRoot.ResolvePath("CaveBuildLiveRunStatus.md");
            Directory.CreateDirectory(Path.GetDirectoryName(libraryPath) ?? EnvironmentKitDataRoot.ResolveRoot());
            File.WriteAllText(libraryPath, text);

            var mirrorAssets = false;
            if (!mirrorAssets)
                return;

            _lastAssetsMirrorTime = now;
            var assetsPath = Path.Combine(hub, LiveStatusRel);
            Directory.CreateDirectory(Path.GetDirectoryName(assetsPath) ?? hub);
            try
            {
                File.WriteAllText(assetsPath, text);
            }
            catch (IOException)
            {
                // Unity may have the asset open for import — Library copy remains authoritative.
            }
        }
    }
}
#endif
