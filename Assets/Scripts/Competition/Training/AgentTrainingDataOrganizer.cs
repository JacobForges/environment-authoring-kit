using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Hub.Competition
{
    /// <summary>Scans agent dataset folders, builds training_catalog.json, exports reasoning_train.jsonl.</summary>
    public static class AgentTrainingDataOrganizer
    {
        public const int CatalogSchemaVersion = 1;

        [Serializable]
        public sealed class CatalogSnapshot
        {
            public int schemaVersion;
            public long utcUnix;
            public int gameplayRows;
            public int chatRows;
            public int reasoningRows;
            public int missionRows;
            public int researchRows;
            public Dictionary<string, int> gameplayActivities = new(StringComparer.OrdinalIgnoreCase);
            public List<string> files = new();
        }

        public static bool IsGameplayEpisodeFile(string fileName) =>
            !string.IsNullOrEmpty(fileName)
            && fileName.StartsWith(AgentTrainingDataKind.GameplayEpisodePrefix, StringComparison.OrdinalIgnoreCase);

        public static void RebuildCatalog(string agentId)
        {
            if (string.IsNullOrEmpty(agentId))
                return;

            var snapshot = Scan(agentId);
            WriteCatalog(agentId, snapshot);
        }

        public static void PrepareForTrain(string agentId)
        {
            if (string.IsNullOrEmpty(agentId))
                return;

            RebuildCatalog(agentId);
            ExportReasoningTrain(agentId);
            RebuildCatalog(agentId);
        }

        public static bool TryLoadCatalog(string agentId, out CatalogSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(agentId))
                return false;

            var path = CatalogPath(agentId);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                snapshot = ParseCatalog(json);
                return snapshot != null;
            }
            catch
            {
                return false;
            }
        }

        public static string FormatCatalogSummary(string agentId)
        {
            if (!TryLoadCatalog(agentId, out var catalog) || catalog == null)
                return "Dataset catalog: not built yet.";

            return
                $"Dataset catalog: gameplay {catalog.gameplayRows}, chat {catalog.chatRows}, reasoning {catalog.reasoningRows}, mission {catalog.missionRows}, research {catalog.researchRows}.";
        }

        static string CatalogPath(string agentId) =>
            Path.Combine(CompetitionPaths.AgentDatasetFolder(agentId), AgentTrainingDataKind.CatalogFile);

        static CatalogSnapshot Scan(string agentId)
        {
            var snapshot = new CatalogSnapshot
            {
                schemaVersion = CatalogSchemaVersion,
                utcUnix = AgentTime.UtcUnixSeconds(),
            };

            var datasetFolder = CompetitionPaths.AgentDatasetFolder(agentId);
            if (!string.IsNullOrEmpty(datasetFolder) && Directory.Exists(datasetFolder))
            {
                foreach (var path in Directory.GetFiles(datasetFolder))
                {
                    var fileName = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    snapshot.files.Add(fileName);

                    if (IsGameplayEpisodeFile(fileName))
                    {
                        CountGameplayFile(path, snapshot);
                        continue;
                    }

                    if (string.Equals(fileName, AgentTrainingDataKind.ChatFile, StringComparison.OrdinalIgnoreCase))
                        snapshot.chatRows += CountJsonlLines(path);
                    else if (string.Equals(fileName, AgentTrainingDataKind.ReasoningJournal, StringComparison.OrdinalIgnoreCase))
                        snapshot.reasoningRows += CountJsonlLines(path);
                }
            }

            var missionJournal = CompetitionPaths.AgentMissionDeliveryJournalPath(agentId);
            if (!string.IsNullOrEmpty(missionJournal) && File.Exists(missionJournal))
                snapshot.missionRows = CountJsonlLines(missionJournal);

            var researchArchive = CompetitionPaths.AgentResearchArchivePath(agentId);
            if (!string.IsNullOrEmpty(researchArchive) && File.Exists(researchArchive))
                snapshot.researchRows = CountJsonlLines(researchArchive);

            snapshot.files.Sort(StringComparer.OrdinalIgnoreCase);
            return snapshot;
        }

        static void CountGameplayFile(string path, CatalogSnapshot snapshot)
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"state\""))
                    continue;

                snapshot.gameplayRows++;

                var activity = ExtractJsonStringField(line, "activity");
                var tag = string.IsNullOrEmpty(activity) ? "untagged" : activity;
                snapshot.gameplayActivities.TryGetValue(tag, out var n);
                snapshot.gameplayActivities[tag] = n + 1;
            }
        }

        static int CountJsonlLines(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0;

            var count = 0;
            foreach (var line in File.ReadAllLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    count++;
            }

            return count;
        }

        static void WriteCatalog(string agentId, CatalogSnapshot snapshot)
        {
            var folder = CompetitionPaths.AgentDatasetFolder(agentId);
            if (string.IsNullOrEmpty(folder))
                return;

            Directory.CreateDirectory(folder);
            File.WriteAllText(CatalogPath(agentId), BuildCatalogJson(snapshot));
        }

        static string BuildCatalogJson(CatalogSnapshot snapshot)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"schemaVersion\":").Append(snapshot.schemaVersion).Append(',');
            sb.Append("\"utcUnix\":").Append(snapshot.utcUnix).Append(',');
            sb.Append("\"kinds\":{");
            AppendKindBlock(sb, AgentTrainingDataKind.Gameplay, snapshot.gameplayRows, snapshot.gameplayActivities);
            sb.Append(',');
            AppendKindBlock(sb, AgentTrainingDataKind.Chat, snapshot.chatRows, null);
            sb.Append(',');
            AppendKindBlock(sb, AgentTrainingDataKind.Reasoning, snapshot.reasoningRows, null);
            sb.Append(',');
            AppendKindBlock(sb, AgentTrainingDataKind.Mission, snapshot.missionRows, null);
            sb.Append(',');
            AppendKindBlock(sb, AgentTrainingDataKind.Research, snapshot.researchRows, null);
            sb.Append("},");
            sb.Append("\"files\":").Append(StringArrayJson(snapshot.files));
            sb.Append('}');
            return sb.ToString();
        }

        static void AppendKindBlock(
            StringBuilder sb,
            string kind,
            int rows,
            Dictionary<string, int> activities)
        {
            sb.Append('"').Append(EscapeJson(kind)).Append("\":{");
            sb.Append("\"rows\":").Append(rows);
            if (activities != null && activities.Count > 0)
            {
                sb.Append(",\"activities\":{");
                var first = true;
                foreach (var pair in activities)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    sb.Append('"').Append(EscapeJson(pair.Key)).Append("\":").Append(pair.Value);
                }

                sb.Append('}');
            }

            sb.Append('}');
        }

        static void ExportReasoningTrain(string agentId)
        {
            var journalPath = CompetitionPaths.AgentReasoningJournalPath(agentId);
            if (string.IsNullOrEmpty(journalPath) || !File.Exists(journalPath))
                return;

            var folder = CompetitionPaths.AgentDatasetFolder(agentId);
            if (string.IsNullOrEmpty(folder))
                return;

            Directory.CreateDirectory(folder);
            var exportPath = Path.Combine(folder, AgentTrainingDataKind.ReasoningTrainExport);

            var sb = new StringBuilder();
            foreach (var line in File.ReadAllLines(journalPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                AgentReasoningCycleLogEntry entry;
                try
                {
                    entry = JsonUtility.FromJson<AgentReasoningCycleLogEntry>(line);
                }
                catch
                {
                    continue;
                }

                if (entry == null)
                    continue;

                sb.Append(BuildReasoningTrainRow(entry)).Append('\n');
            }

            File.WriteAllText(exportPath, sb.ToString());
        }

        static string BuildReasoningTrainRow(AgentReasoningCycleLogEntry entry)
        {
            var context = new StringBuilder(256);
            AppendContextPart(context, "command", entry.command);
            AppendContextPart(context, "mode", entry.mode);
            AppendContextPart(context, "depth", entry.depth);
            AppendContextPart(context, "reason1", entry.reason1);
            AppendContextPart(context, "think1", entry.think1);
            AppendContextPart(context, "predict1", entry.predict1);
            AppendContextPart(context, "risk1", entry.risk1);

            var sb = new StringBuilder(384);
            sb.Append('{');
            sb.Append("\"kind\":\"").Append(AgentTrainingDataKind.Reasoning).Append("\",");
            sb.Append("\"stance\":\"").Append(EscapeJson(entry.stance ?? string.Empty)).Append("\",");
            sb.Append("\"plan\":\"").Append(EscapeJson(entry.plan ?? string.Empty)).Append("\",");
            sb.Append("\"outcomeTag\":\"").Append(EscapeJson(entry.outcomeTag ?? "routine")).Append("\",");
            sb.Append("\"context\":\"").Append(EscapeJson(context.ToString())).Append("\",");
            sb.Append("\"utcUnix\":").Append(entry.utcUnix);
            sb.Append('}');
            return sb.ToString();
        }

        static void AppendContextPart(StringBuilder sb, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (sb.Length > 0)
                sb.Append(' ');

            sb.Append(key).Append('=').Append(value.Trim());
        }

        static CatalogSnapshot ParseCatalog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var snapshot = new CatalogSnapshot();
            snapshot.schemaVersion = ReadIntField(json, "schemaVersion");
            snapshot.utcUnix = ReadLongField(json, "utcUnix");
            snapshot.gameplayRows = ReadKindRows(json, AgentTrainingDataKind.Gameplay);
            snapshot.chatRows = ReadKindRows(json, AgentTrainingDataKind.Chat);
            snapshot.reasoningRows = ReadKindRows(json, AgentTrainingDataKind.Reasoning);
            snapshot.missionRows = ReadKindRows(json, AgentTrainingDataKind.Mission);
            snapshot.researchRows = ReadKindRows(json, AgentTrainingDataKind.Research);
            return snapshot;
        }

        static int ReadKindRows(string json, string kind)
        {
            var marker = $"\"{kind}\":{{";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return 0;

            start += marker.Length;
            var rowsMarker = "\"rows\":";
            var rowsStart = json.IndexOf(rowsMarker, start, StringComparison.Ordinal);
            if (rowsStart < 0 || rowsStart > start + 64)
                return 0;

            rowsStart += rowsMarker.Length;
            var end = json.IndexOfAny(new[] { ',', '}' }, rowsStart);
            if (end < 0)
                return 0;

            return int.TryParse(json.Substring(rowsStart, end - rowsStart), out var rows) ? rows : 0;
        }

        static int ReadIntField(string json, string field)
        {
            var marker = $"\"{field}\":";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return 0;

            start += marker.Length;
            var end = json.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0)
                return 0;

            return int.TryParse(json.Substring(start, end - start), out var value) ? value : 0;
        }

        static long ReadLongField(string json, string field)
        {
            var marker = $"\"{field}\":";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return 0;

            start += marker.Length;
            var end = json.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0)
                return 0;

            return long.TryParse(json.Substring(start, end - start), out var value) ? value : 0;
        }

        static string ExtractJsonStringField(string line, string field)
        {
            var quoted = $"\"{field}\":\"";
            var start = line.IndexOf(quoted, StringComparison.Ordinal);
            if (start < 0)
                return null;

            start += quoted.Length;
            var end = line.IndexOf('"', start);
            if (end < 0)
                return null;

            return line.Substring(start, end - start);
        }

        static string StringArrayJson(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0)
                return "[]";

            var sb = new StringBuilder("[");
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append('"').Append(EscapeJson(items[i])).Append('"');
            }

            sb.Append(']');
            return sb.ToString();
        }

        static string EscapeJson(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
