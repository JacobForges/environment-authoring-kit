#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Per-rung timings, grades, skip counts — feeds metrics dashboard.</summary>
    public static class CaveBuildLadderMetrics
    {
        public const string MetricsRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildLadderMetrics.json";

        [Serializable]
        public class RungMetric
        {
            public string rungId;
            public double lastDurationMs;
            public bool lastSkipped;
            public int runCount;
            public int skipCount;
            public string lastCompletedUtc;
        }

        [Serializable]
        public class MetricsFile
        {
            public string updatedUtc;
            public int lastSeed;
            public string lastScene;
            public float lastOverallScore;
            public string lastLetterGrade;
            public RungMetric[] rungs = Array.Empty<RungMetric>();
            public string showcaseRecipeId = string.Empty;
        }

        static readonly Dictionary<string, double> _activeStart = new();
        static MetricsFile _cache;

        public static void BeginSession(string sceneName, int seed, string recipeId = null)
        {
            _cache = Load();
            _cache.lastSeed = seed;
            _cache.lastScene = sceneName ?? string.Empty;
            if (!string.IsNullOrEmpty(recipeId))
                _cache.showcaseRecipeId = recipeId;
            _activeStart.Clear();
            Save();
        }

        public static void BeginRung(string rungId)
        {
            _activeStart[rungId] = EditorApplication.timeSinceStartup;
        }

        public static void EndRung(string rungId, bool skipped = false)
        {
            if (!_activeStart.TryGetValue(rungId, out var start))
                return;
            _activeStart.Remove(rungId);
            var ms = (EditorApplication.timeSinceStartup - start) * 1000.0;
            var file = Load();
            var list = new List<RungMetric>(file.rungs ?? Array.Empty<RungMetric>());
            var idx = list.FindIndex(r => r.rungId == rungId);
            if (idx < 0)
            {
                list.Add(new RungMetric { rungId = rungId });
                idx = list.Count - 1;
            }

            var m = list[idx];
            m.lastDurationMs = ms;
            m.lastSkipped = skipped;
            m.runCount++;
            if (skipped)
                m.skipCount++;
            if (!skipped)
                m.lastCompletedUtc = DateTime.UtcNow.ToString("o");
            list[idx] = m;
            file.rungs = list.ToArray();
            _cache = file;
            Save();
        }

        public static void RecordRungSkipped(string rungId, bool skipped) =>
            EndRung(rungId, skipped);

        public static void RecordBuildGrade(float score, string letter)
        {
            var file = Load();
            file.lastOverallScore = score;
            file.lastLetterGrade = letter ?? "?";
            _cache = file;
            Save();
        }

        public static MetricsFile Load()
        {
            if (_cache != null)
                return _cache;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, MetricsRel);
            if (!File.Exists(path))
                return new MetricsFile();
            try
            {
                _cache = JsonUtility.FromJson<MetricsFile>(File.ReadAllText(path)) ?? new MetricsFile();
            }
            catch
            {
                _cache = new MetricsFile();
            }

            return _cache;
        }

        public static void Save()
        {
            var file = _cache ?? new MetricsFile();
            file.updatedUtc = DateTime.UtcNow.ToString("o");
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, MetricsRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
            WriteMarkdownSummary(file, hub);
        }

        static void WriteMarkdownSummary(MetricsFile file, string hub)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Cave build — ladder metrics");
            sb.AppendLine();
            sb.AppendLine($"**Updated:** {file.updatedUtc}");
            sb.AppendLine($"**Scene:** {file.lastScene} | **Seed:** {file.lastSeed}");
            sb.AppendLine($"**Grade:** {file.lastLetterGrade} ({file.lastOverallScore:F0}/100)");
            if (!string.IsNullOrEmpty(file.showcaseRecipeId))
                sb.AppendLine($"**Recipe:** `{file.showcaseRecipeId}`");
            sb.AppendLine();
            sb.AppendLine("| Rung | Last ms | Runs | Skips | Skipped last |");
            sb.AppendLine("|------|---------|------|-------|--------------|");
            foreach (var r in file.rungs ?? Array.Empty<RungMetric>())
            {
                sb.AppendLine(
                    $"| `{r.rungId}` | {r.lastDurationMs:F0} | {r.runCount} | {r.skipCount} | {(r.lastSkipped ? "yes" : "no")} |");
            }

            var mdPath = Path.Combine(hub, CaveBuildAgentContextExporter.Folder + "/CaveBuildMetricsDashboard.md");
            Directory.CreateDirectory(Path.GetDirectoryName(mdPath) ?? hub);
            File.WriteAllText(mdPath, sb.ToString());
        }
    }
}
#endif
