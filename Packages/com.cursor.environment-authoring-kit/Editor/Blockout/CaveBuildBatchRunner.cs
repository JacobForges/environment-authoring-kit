using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Queues N complete cave builds with paced delay between jobs (auto-incrementing seed).
    /// </summary>
    public static class CaveBuildBatchRunner
    {
        public const string LogPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildBatchLog.json";

        sealed class BatchState
        {
            public Transform GroundAnchor;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest RequestTemplate;
            public XROptimizationProfile XrProfile;
            public int JobCount;
            public int SeedIncrement;
            public float DelaySeconds;
            public bool HideLegacy;
            public bool SkipDialogs;
            public int Index;
            public int StartSeed;
            public List<BatchJobEntry> Entries = new();
            public Action<BatchSummary> OnBatchComplete;
        }

        public sealed class BatchJobEntry
        {
            public int Index;
            public int Seed;
            public int QualityScore;
            public string QualityLetter;
            public bool QualityAcceptable;
            public string Message;
        }

        public sealed class BatchSummary
        {
            public int JobsRun;
            public int JobsPassed;
            public List<BatchJobEntry> Entries = new();
            public string LogPath = CaveBuildBatchRunner.LogPath;
        }

        static BatchState _active;

        public static bool IsActive => _active != null;

        public static void CancelActive(string reason = "user abort")
        {
            if (_active == null)
                return;

            Debug.Log($"[CaveBuild] Batch cancelled — {reason}.");
            _active = null;
        }

        public static void Start(
            Transform groundAnchor,
            SceneGroundInfo ground,
            WorldGenerationRequest requestTemplate,
            XROptimizationProfile xrProfile,
            int jobCount,
            int seedIncrement,
            float delaySeconds,
            bool hideLegacy,
            bool skipDialogs,
            Action<BatchSummary> onBatchComplete = null)
        {
            if (_active != null)
            {
                Debug.LogWarning("[CaveBuild] Batch already running — wait for completion.");
                return;
            }

            if (jobCount < 1)
            {
                Debug.LogWarning("[CaveBuild] Batch job count must be >= 1.");
                return;
            }

            if (requestTemplate?.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                CaveBuildPhaseContractRegistry.ExportContractsCatalog();
                CaveBuildAaaProductionBootstrap.EnsureBatchProductionSettings();
            }

            _active = new BatchState
            {
                GroundAnchor = groundAnchor,
                Ground = ground,
                RequestTemplate = requestTemplate,
                XrProfile = xrProfile,
                JobCount = jobCount,
                SeedIncrement = Mathf.Max(1, seedIncrement),
                DelaySeconds = Mathf.Max(0.05f, delaySeconds),
                HideLegacy = hideLegacy,
                SkipDialogs = skipDialogs,
                StartSeed = requestTemplate?.Seed ?? 0,
                OnBatchComplete = onBatchComplete,
            };

            Debug.Log(
                $"[CaveBuild] Batch started — {jobCount} jobs, seed start {_active.StartSeed}, " +
                $"increment {_active.SeedIncrement}, delay {_active.DelaySeconds:F2}s.");
            RunNextJob();
        }

        public static void StartFromSettings(
            Transform groundAnchor,
            SceneGroundInfo ground,
            WorldGenerationRequest requestTemplate,
            XROptimizationProfile xrProfile,
            bool hideLegacy,
            bool skipDialogs,
            Action<BatchSummary> onBatchComplete = null)
        {
            var s = CaveBuildCursorSettings.LoadOrCreate();
            s.LoadFromPrefs();
            Start(
                groundAnchor,
                ground,
                requestTemplate,
                xrProfile,
                Mathf.Max(1, s.batchJobCount),
                Mathf.Max(1, s.batchSeedIncrement),
                Mathf.Max(0.05f, s.batchDelaySeconds),
                hideLegacy,
                skipDialogs,
                onBatchComplete);
        }

        /// <summary>
        /// Called from queued FinishQueued — returns true when the user callback was deferred (batch continuing).
        /// </summary>
        public static bool TryHandleBuildComplete(
            LavaTubeCaveBuildReport report,
            Action<LavaTubeCaveBuildReport> userCallback)
        {
            if (_active == null)
            {
                userCallback?.Invoke(report);
                return false;
            }

            RecordJob(report);

            _active.Index++;
            if (_active.Index >= _active.JobCount)
            {
                CompleteBatch(userCallback, report);
                return true;
            }

            Debug.Log(
                $"[CaveBuild] Batch job {_active.Index}/{_active.JobCount} done — " +
                $"next in {_active.DelaySeconds:F2}s.");
            CaveBuildActionPacing.PostponeNextRun(_active.DelaySeconds);
            CaveBuildActionPacing.ScheduleHeavy(
                RunNextJob,
                $"batch job {_active.Index + 1}/{_active.JobCount}");
            return true;
        }

        static void RunNextJob()
        {
            if (_active == null)
                return;

            var seed = unchecked(_active.StartSeed + _active.Index * _active.SeedIncrement);
            var request = CloneRequest(_active.RequestTemplate, seed);
            if (_active.Index == 0)
                Debug.Log($"[CaveBuild] Batch job 1/{_active.JobCount} seed={seed}");
            else
                Debug.Log($"[CaveBuild] Batch job {_active.Index + 1}/{_active.JobCount} seed={seed}");

            LavaTubeCaveBuildPipeline.QueueRun(
                _active.GroundAnchor,
                _active.Ground,
                request,
                _active.XrProfile,
                showProgress: true,
                report => TryHandleBuildComplete(report, null));
        }

        static WorldGenerationRequest CloneRequest(WorldGenerationRequest template, int seed)
        {
            if (template == null)
                return new WorldGenerationRequest { Seed = seed };

            var clone = template.Clone();
            clone.Seed = seed;
            return clone;
        }

        static void RecordJob(LavaTubeCaveBuildReport report)
        {
            if (_active == null)
                return;

            _active.Entries.Add(new BatchJobEntry
            {
                Index = _active.Index,
                Seed = _active.StartSeed + _active.Index * _active.SeedIncrement,
                QualityScore = report?.QualityScore ?? 0,
                QualityLetter = report?.QualityLetter ?? "?",
                QualityAcceptable = report?.QualityAcceptable ?? false,
                Message = report?.Message ?? string.Empty,
            });
        }

        static void CompleteBatch(Action<LavaTubeCaveBuildReport> userCallback, LavaTubeCaveBuildReport lastReport)
        {
            var summary = new BatchSummary
            {
                JobsRun = _active.Entries.Count,
                Entries = new List<BatchJobEntry>(_active.Entries),
            };
            foreach (var e in _active.Entries)
            {
                if (e.QualityAcceptable)
                    summary.JobsPassed++;
            }

            WriteBatchLog(summary);
            var batch = _active;
            _active = null;

            Debug.Log(
                $"[CaveBuild] Batch complete — {summary.JobsPassed}/{summary.JobsRun} passed. Log: {LogPath}");

            userCallback?.Invoke(lastReport);

            batch.OnBatchComplete?.Invoke(summary);
        }

        static void WriteBatchLog(BatchSummary summary)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, LogPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"completedUtc\": \"{DateTime.UtcNow:O}\",");
            sb.AppendLine($"  \"jobsRun\": {summary.JobsRun},");
            sb.AppendLine($"  \"jobsPassed\": {summary.JobsPassed},");
            sb.AppendLine("  \"jobs\": [");
            for (var i = 0; i < summary.Entries.Count; i++)
            {
                var j = summary.Entries[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"index\": {j.Index},");
                sb.AppendLine($"      \"seed\": {j.Seed},");
                sb.AppendLine($"      \"qualityScore\": {j.QualityScore},");
                sb.AppendLine($"      \"qualityLetter\": \"{Esc(j.QualityLetter)}\",");
                sb.AppendLine($"      \"qualityAcceptable\": {(j.QualityAcceptable ? "true" : "false")},");
                sb.AppendLine($"      \"message\": \"{Esc(j.Message)}\"");
                sb.Append("    }");
                sb.AppendLine(i < summary.Entries.Count - 1 ? "," : "");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
        }

        static string Esc(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
