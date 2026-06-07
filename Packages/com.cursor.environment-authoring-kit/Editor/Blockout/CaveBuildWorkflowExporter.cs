using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Post-build Cursor workflow checklist (research → compile → ladder → verify).</summary>
    public static class CaveBuildWorkflowExporter
    {
        public const string WorkflowPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildWorkflowContext.json";

        public static readonly string[] PhaseOrder =
        {
            "research",
            "plan",
            "compile_gate",
            "ladder_fixes",
            "verify_rebuild",
        };

        public static void WriteInitial(
            CaveBuildQualityReport report,
            Transform caveRoot,
            string nextPhase = "research")
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildCompileGate.ExportDiagnostics(hub);
            CaveBuildAgentMemoryExporter.SyncToDisk();

            var compile = CaveBuildCompileGate.Capture(hub);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"scene\": \"{Escape(report?.SceneName ?? string.Empty)}\",");
            sb.AppendLine($"  \"letterGrade\": \"{Escape(report?.LetterGrade ?? string.Empty)}\",");
            sb.AppendLine($"  \"overallScore\": {report?.OverallScore ?? 0},");
            sb.AppendLine($"  \"currentPhase\": \"{Escape(nextPhase)}\",");
            sb.AppendLine($"  \"nextRequiredPhase\": \"{Escape(nextPhase)}\",");
            sb.AppendLine("  \"policy\": \"Mandatory order: research → plan (in prompt) → compile_gate (zero CS errors) → ladder rungs → verify rebuild. Record failures in CaveBuildAgentMemory.json.\",");
            sb.AppendLine("  \"phases\": [");
            for (var i = 0; i < PhaseOrder.Length; i++)
            {
                var id = PhaseOrder[i];
                var status = id == nextPhase ? "in_progress" : "pending";
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{id}\",");
                sb.AppendLine($"      \"status\": \"{status}\",");
                sb.AppendLine($"      \"required\": true");
                sb.Append("    }");
                sb.AppendLine(i < PhaseOrder.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"checklist\": [");
            WriteChecklist(sb, compile.HasCompileErrors);
            sb.AppendLine("  ],");
            sb.AppendLine($"  \"compileDiagnosticsPath\": \"{CaveBuildCompileGate.DiagnosticsPath}\",");
            sb.AppendLine($"  \"researchManifestPath\": \"{CaveBuildResearchExporter.ResearchPath}\",");
            sb.AppendLine($"  \"agentMemoryPath\": \"{CaveBuildAgentMemoryExporter.MemoryPath}\",");
            sb.AppendLine($"  \"hasBlockingCompileErrors\": {(compile.HasCompileErrors ? "true" : "false")}");
            sb.AppendLine("}");
            WriteFile(hub, sb.ToString());
        }

        public static void SetCurrentPhase(string phaseId, string status = "in_progress")
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, WorkflowPath);
            if (!File.Exists(path))
                return;

            try
            {
                var text = File.ReadAllText(path);
                text = ReplaceJsonStringField(text, "currentPhase", phaseId);
                text = ReplaceJsonStringField(text, "nextRequiredPhase", phaseId);
                if (!string.IsNullOrEmpty(status))
                    text = RegexReplacePhaseStatus(text, phaseId, status);
                CaveBuildCompileGate.ExportDiagnostics(hub);
                WriteFile(hub, text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Workflow phase update failed: " + ex.Message);
            }
        }

        public static void RecordPhaseResult(string phaseId, bool success, string note = null)
        {
            if (!success && !string.IsNullOrEmpty(note))
                CaveBuildAgentMemoryExporter.RecordFailure(phaseId, phaseId, note);

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, WorkflowPath);
            if (!File.Exists(path))
                return;

            try
            {
                var text = File.ReadAllText(path);
                text = RegexReplacePhaseStatus(text, phaseId, success ? "done" : "failed");
                if (success)
                    text = MarkPhaseChecklistChecked(text, phaseId);
                var next = success ? GetNextPhase(phaseId, true) : phaseId;
                text = ReplaceJsonStringField(text, "currentPhase", next);
                text = ReplaceJsonStringField(text, "nextRequiredPhase", next);
                if (phaseId == CaveBuildPromptLadder.RungCompileGate && success)
                    text = ReplaceJsonBoolField(text, "hasBlockingCompileErrors", false);
                CaveBuildCompileGate.ExportDiagnostics(hub);
                WriteFile(hub, text);
                UnityEngine.Debug.Log(
                    $"[CaveBuild] Workflow phase '{phaseId}' → {(success ? "done" : "failed")}; next='{next}'");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Workflow update failed: " + ex.Message);
            }
        }

        /// <summary>Marks a workflow checklist row checked (arXiv:2510.15120 — gate before ladder edits).</summary>
        public static void MarkChecklistChecked(string checklistId)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, WorkflowPath);
            if (!File.Exists(path) || string.IsNullOrEmpty(checklistId))
                return;

            try
            {
                var text = File.ReadAllText(path);
                text = MarkChecklistChecked(text, checklistId);
                WriteFile(hub, text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Checklist update failed: " + ex.Message);
            }
        }

        static string MarkPhaseChecklistChecked(string json, string phaseId)
        {
            if (string.IsNullOrEmpty(phaseId))
                return json;

            var items = new[]
            {
                ("research_fetch", "research"),
                ("research_plan_table", "plan"),
                ("compile_zero_errors", "compile_gate"),
                ("compile_consider_research", "compile_gate"),
                ("ladder_active_rung_only", "ladder_fixes"),
                ("memory_check", "ladder_fixes"),
                ("verify_rebuild", "verify_rebuild"),
            };

            foreach (var (id, phase) in items)
            {
                if (phase == phaseId)
                    json = MarkChecklistChecked(json, id);
            }

            return json;
        }

        static string MarkChecklistChecked(string json, string checklistId)
        {
            var marker = $"\"id\": \"{checklistId}\"";
            var idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                return json;
            var checkedKey = "\"checked\":";
            var cidx = json.IndexOf(checkedKey, idx, StringComparison.Ordinal);
            if (cidx < 0)
                return json;
            var start = cidx + checkedKey.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;
            if (start + 4 >= json.Length)
                return json;
            if (json.AsSpan(start, 4).SequenceEqual("true".AsSpan()))
                return json;
            return json.Remove(start, 5).Insert(start, "true");
        }

        static string ReplaceJsonBoolField(string json, string field, bool value)
        {
            var key = $"\"{field}\":";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return json;
            var start = idx + key.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;
            var end = start;
            while (end < json.Length && (char.IsLetter(json[end]) || json[end] == '_'))
                end++;
            if (end <= start)
                return json;
            return json.Remove(start, end - start).Insert(start, value ? "true" : "false");
        }

        static string ReplaceJsonStringField(string json, string field, string value)
        {
            var key = $"\"{field}\":";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return json;
            var start = idx + key.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;
            if (start >= json.Length || json[start] != '"')
                return json;
            var end = json.IndexOf('"', start + 1);
            if (end < 0)
                return json;
            return json.Remove(start, end - start + 1).Insert(start, $"\"{Escape(value)}\"");
        }

        static string GetNextPhase(string completed, bool success)
        {
            if (!success)
                return completed;

            for (var i = 0; i < PhaseOrder.Length; i++)
            {
                if (PhaseOrder[i] != completed)
                    continue;
                if (i + 1 < PhaseOrder.Length)
                    return PhaseOrder[i + 1];
                return "verify_rebuild";
            }

            return "research";
        }

        static string RegexReplacePhaseStatus(string json, string phaseId, string status)
        {
            var marker = $"\"id\": \"{phaseId}\"";
            var idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                return json;
            var statusKey = "\"status\":";
            var sidx = json.IndexOf(statusKey, idx, StringComparison.Ordinal);
            if (sidx < 0)
                return json;
            var start = sidx + statusKey.Length;
            var end = json.IndexOf('"', start + 2);
            if (end < 0)
                return json;
            return json.Remove(start, end - start + 1).Insert(start, $" \"{status}\"");
        }

        static void WriteChecklist(StringBuilder sb, bool hasCompileErrors)
        {
            var items = new[]
            {
                ("research_fetch", "Fetch prestige-lab papers from CaveBuildResearch.json for active rung", "research"),
                ("research_plan_table", "Write plan table: step | JSON metric | paper | file | expected grade", "plan"),
                ("compile_zero_errors", "Fix ALL C# compile errors in package before scene/cave edits", "compile_gate"),
                ("compile_consider_research", "Compile fixes must reference research/plan (comment which paper/step)", "compile_gate"),
                ("ladder_active_rung_only", "Edit kit C# for active ladder rung only", "ladder_fixes"),
                ("memory_check", "Read CaveBuildAgentMemory.json and avoid listed fingerprints", "ladder_fixes"),
                ("verify_rebuild", "User: Remove Layered Shells → Build Complete Cave → Re-grade", "verify_rebuild"),
            };

            for (var i = 0; i < items.Length; i++)
            {
                var (id, label, phase) = items[i];
                var blocked = phase == "ladder_fixes" && hasCompileErrors;
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{id}\",");
                sb.AppendLine($"      \"label\": \"{Escape(label)}\",");
                sb.AppendLine($"      \"phase\": \"{phase}\",");
                sb.AppendLine($"      \"checked\": false,");
                sb.AppendLine($"      \"blockedUntilCompileClean\": {(blocked ? "true" : "false")}");
                sb.Append("    }");
                sb.AppendLine(i < items.Length - 1 ? "," : "");
            }
        }

        static void WriteFile(string hub, string json)
        {
            var path = Path.Combine(hub, WorkflowPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, json);
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
