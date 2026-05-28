#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Ring buffer of build pipeline messages for the Pipeline Console window.</summary>
    [InitializeOnLoad]
    public static class CaveBuildPipelineLog
    {
        public const int MaxEntries = 400;
        public const string ExportRelativePath = CaveBuildAgentContextExporter.Folder + "/CaveBuildPipelineLog.json";

        public sealed class Entry
        {
            public string utc;
            public string level;
            public string source;
            public string message;
        }

        static readonly List<Entry> Buffer = new();
        static readonly List<Entry> PendingFromUnity = new();
        static bool _hooked;
        static bool _flushScheduled;

        static CaveBuildPipelineLog()
        {
            EnsureHook();
        }

        public static void EnsureHook()
        {
            if (_hooked)
                return;
            Application.logMessageReceivedThreaded += OnLogMessageThreaded;
            _hooked = true;
        }

        static void OnLogMessageThreaded(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition))
                return;
            if (!condition.Contains("[Cave]") &&
                !condition.Contains("[Cave|") &&
                !condition.Contains("[Surface]") &&
                !condition.Contains("[CaveBuild]") &&
                !condition.Contains("[CaveCursor]") &&
                !condition.Contains("[CaveProbe]") &&
                !condition.Contains("[CavePlaytestBot]"))
                return;

            var level = type switch
            {
                LogType.Error => "error",
                LogType.Exception => "error",
                LogType.Warning => "warn",
                _ => "info",
            };

            lock (PendingFromUnity)
            {
                PendingFromUnity.Add(new Entry
                {
                    utc = DateTime.UtcNow.ToString("o"),
                    level = level,
                    source = "Unity",
                    message = condition,
                });
            }

            if (_flushScheduled)
                return;
            _flushScheduled = true;
            EditorApplication.delayCall += FlushPendingFromUnity;
        }

        static void FlushPendingFromUnity()
        {
            _flushScheduled = false;
            Entry[] batch;
            lock (PendingFromUnity)
            {
                if (PendingFromUnity.Count == 0)
                    return;
                batch = PendingFromUnity.ToArray();
                PendingFromUnity.Clear();
            }

            for (var i = 0; i < batch.Length; i++)
                AddEntry(batch[i]);
        }

        public static void Info(string message, string source = "CaveBuild") => Add("info", source, message);

        public static void Warn(string message, string source = "CaveBuild") => Add("warn", source, message);

        public static void Error(string message, string source = "CaveBuild") => Add("error", source, message);

        public static void Add(string level, string source, string message) =>
            AddEntry(new Entry
            {
                utc = DateTime.UtcNow.ToString("o"),
                level = level,
                source = source ?? "CaveBuild",
                message = message ?? string.Empty,
            });

        static void AddEntry(Entry entry)
        {
            Buffer.Add(entry);
            while (Buffer.Count > MaxEntries)
                Buffer.RemoveAt(0);
        }

        public static IReadOnlyList<Entry> GetEntries() => Buffer;

        public static string GetRecentText(int maxLines = 20)
        {
            if (Buffer.Count == 0)
                return string.Empty;

            var start = Mathf.Max(0, Buffer.Count - maxLines);
            var sb = new System.Text.StringBuilder();
            for (var i = start; i < Buffer.Count; i++)
            {
                var e = Buffer[i];
                sb.Append('[').Append(e.source).Append("] ").AppendLine(e.message);
            }

            return sb.ToString().TrimEnd();
        }

        public static void Clear() => Buffer.Clear();

        public static void ExportJson()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ExportRelativePath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"exportedUtc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine("  \"entries\": [");
            for (var i = 0; i < Buffer.Count; i++)
            {
                var e = Buffer[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"utc\": \"{Escape(e.utc)}\",");
                sb.AppendLine($"      \"level\": \"{Escape(e.level)}\",");
                sb.AppendLine($"      \"source\": \"{Escape(e.source)}\",");
                sb.AppendLine($"      \"message\": \"{Escape(e.message)}\"");
                sb.AppendLine(i < Buffer.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
