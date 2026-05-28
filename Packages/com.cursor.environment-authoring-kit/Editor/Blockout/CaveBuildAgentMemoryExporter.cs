using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Persistent failure memory so Cursor does not repeat the same mistakes.</summary>
    public static class CaveBuildAgentMemoryExporter
    {
        public const string MemoryPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildAgentMemory.json";

        const int MaxEntries = 48;

        [Serializable]
        public class MemoryFile
        {
            public MemoryEntry[] entries = Array.Empty<MemoryEntry>();
        }

        [Serializable]
        public class MemoryEntry
        {
            public string utc;
            public string phase;
            public string rung;
            public string fingerprint;
            public string message;
            public bool resolved;
        }

        public static void RecordFailure(
            string phase,
            string rung,
            string message,
            bool resolved = false)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var file = Load(hub);
            var fp = BuildFingerprint(phase, rung, message);

            foreach (var e in file.entries)
            {
                if (e.fingerprint == fp && !e.resolved)
                    return;
            }

            var list = new List<MemoryEntry>(file.entries);
            list.Insert(0, new MemoryEntry
            {
                utc = DateTime.UtcNow.ToString("o"),
                phase = phase ?? string.Empty,
                rung = rung ?? string.Empty,
                fingerprint = fp,
                message = Truncate(message, 400),
                resolved = resolved,
            });

            while (list.Count > MaxEntries)
                list.RemoveAt(list.Count - 1);

            var arr = new MemoryEntry[list.Count];
            list.CopyTo(arr);
            Write(hub, new MemoryFile { entries = arr });
        }

        public static void MarkResolved(string fingerprint)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var file = Load(hub);
            var changed = false;
            foreach (var e in file.entries)
            {
                if (e.fingerprint != fingerprint)
                    continue;
                e.resolved = true;
                changed = true;
            }

            if (changed)
                Write(hub, file);
        }

        public static void SyncToDisk()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            if (!File.Exists(Path.Combine(hub, MemoryPath)))
                Write(hub, new MemoryFile { entries = Array.Empty<MemoryEntry>() });
        }

        public static string FormatPromptBlock()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var file = Load(hub);
            var open = new List<MemoryEntry>();
            foreach (var e in file.entries)
            {
                if (!e.resolved)
                    open.Add(e);
            }

            if (open.Count == 0)
                return "## Recorded failures (none — do not reintroduce resolved issues)";

            var sb = new StringBuilder();
            sb.AppendLine("## Recorded failures — DO NOT REPEAT (check off when fixed)");
            var n = 0;
            foreach (var e in open)
            {
                if (n >= 12)
                    break;
                n++;
                sb.AppendLine(
                    $"- [ ] **{e.phase}/{e.rung}** `{e.fingerprint}` — {e.message} (since {e.utc})");
            }

            sb.AppendLine();
            sb.AppendLine(
                "When you fix an item, add `CaveBuildAgentMemory.json` entry `resolved: true` for that fingerprint in your plan (Unity will merge on next export).");
            return sb.ToString();
        }

        static MemoryFile Load(string hub)
        {
            var path = Path.Combine(hub, MemoryPath);
            if (!File.Exists(path))
                return new MemoryFile { entries = Array.Empty<MemoryEntry>() };

            try
            {
                return JsonUtility.FromJson<MemoryFile>(File.ReadAllText(path))
                       ?? new MemoryFile { entries = Array.Empty<MemoryEntry>() };
            }
            catch
            {
                return new MemoryFile { entries = Array.Empty<MemoryEntry>() };
            }
        }

        static void Write(string hub, MemoryFile file)
        {
            var path = Path.Combine(hub, MemoryPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }

        static string BuildFingerprint(string phase, string rung, string message)
        {
            var norm = (message ?? string.Empty).ToLowerInvariant();
            norm = norm.Replace(" ", "_");
            if (norm.Length > 80)
                norm = norm.Substring(0, 80);
            return $"{phase}:{rung}:{norm}";
        }

        static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
