using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Pre-build Cursor workflow JSON (research → compile → readiness ladder → proceed).</summary>
    public static class CaveBuildPreBuildWorkflowExporter
    {
        public const string WorkflowPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildPreBuildWorkflowContext.json";

        public static readonly string[] PhaseOrder =
        {
            "research",
            "plan",
            "compile_gate",
            "readiness_ladder",
            "proceed_build",
        };

        public static void WriteInitial(CaveBuildPreBuildReport preReport, string nextPhase = "research")
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildCompileGate.ExportDiagnostics(hub);
            CaveBuildAgentMemoryExporter.SyncToDisk();
            CaveBuildResearchExporter.WriteMinimal(hub, preReport);

            var compile = CaveBuildCompileGate.Capture(hub);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"workflow\": \"pre_build\",");
            sb.AppendLine($"  \"scene\": \"{Escape(preReport?.SceneName ?? string.Empty)}\",");
            sb.AppendLine($"  \"preBuildScore\": {preReport?.OverallScore ?? 0},");
            sb.AppendLine($"  \"preBuildGrade\": \"{Escape(preReport?.LetterGrade ?? "F")}\",");
            sb.AppendLine($"  \"preBuildAcceptable\": {(preReport != null && preReport.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"currentPhase\": \"{Escape(nextPhase)}\",");
            sb.AppendLine($"  \"nextRequiredPhase\": \"{Escape(nextPhase)}\",");
            sb.AppendLine(
                "  \"policy\": \"Before cave geometry: research → plan → compile_gate (zero CS errors) → weighted readiness ladder → proceed with Build Complete Cave. Use research + plan for every fix. Record failures in CaveBuildAgentMemory.json.\",");
            sb.AppendLine("  \"phases\": [");
            for (var i = 0; i < PhaseOrder.Length; i++)
            {
                var id = PhaseOrder[i];
                var status = id == nextPhase ? "in_progress" : "pending";
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{id}\",");
                sb.AppendLine($"      \"status\": \"{status}\",");
                sb.AppendLine("      \"required\": true");
                sb.Append("    }");
                sb.AppendLine(i < PhaseOrder.Length - 1 ? "," : "");
            }

            sb.AppendLine("  ],");
            sb.AppendLine("  \"checklist\": [");
            WriteChecklist(sb, compile.HasCompileErrors);
            sb.AppendLine("  ],");
            sb.AppendLine($"  \"preBuildLadderReportPath\": \"{CaveBuildPreBuildLadder.ReportPath}\",");
            sb.AppendLine($"  \"preBuildLadderContextPath\": \"{CaveBuildPreBuildLadder.ContextPath}\",");
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
                if (!string.IsNullOrEmpty(status) && IsWorkflowPhaseId(phaseId))
                    text = RegexReplacePhaseStatus(text, phaseId, status);
                CaveBuildCompileGate.ExportDiagnostics(hub);
                WriteFile(hub, text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Pre-build workflow update failed: " + ex.Message);
            }
        }

        /// <summary>Marks a workflow phase done/failed. Does not advance currentPhase (SetCurrentPhase does that per invoke).</summary>
        public static void RecordPhaseResult(string phaseId, bool success, string note = null)
        {
            if (!IsWorkflowPhaseId(phaseId))
                return;

            if (!success && !string.IsNullOrEmpty(note))
                CaveBuildAgentMemoryExporter.RecordFailure("pre_" + phaseId, phaseId, note);

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, WorkflowPath);
            if (!File.Exists(path))
                return;

            try
            {
                var text = File.ReadAllText(path);
                text = RegexReplacePhaseStatus(text, phaseId, success ? "done" : "failed");
                CaveBuildCompileGate.ExportDiagnostics(hub);
                WriteFile(hub, text);
                Debug.Log(
                    $"[CaveBuild] Pre-build workflow '{phaseId}' → {(success ? "done" : "failed")}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Pre-build workflow record failed: " + ex.Message);
            }
        }

        public static bool IsWorkflowPhaseId(string phaseId)
        {
            for (var i = 0; i < PhaseOrder.Length; i++)
            {
                if (PhaseOrder[i] == phaseId)
                    return true;
            }

            return false;
        }

        static string GetNextPhase(string completed)
        {
            for (var i = 0; i < PhaseOrder.Length; i++)
            {
                if (PhaseOrder[i] != completed)
                    continue;
                if (i + 1 < PhaseOrder.Length)
                    return PhaseOrder[i + 1];
                return "proceed_build";
            }

            return "research";
        }

        static void WriteChecklist(StringBuilder sb, bool hasCompileErrors)
        {
            var items = new[]
            {
                ("research_read_manifest", "Read CaveBuildResearch.json + prestige papers", "research"),
                ("research_plan_table", "Plan table tied to pre-build ladder stages", "plan"),
                ("compile_zero", "Zero CS errors in package (see diagnostics)", "compile_gate"),
                ("compile_use_plan", "Each compile fix cites plan step + research source", "compile_gate"),
                ("ladder_lowest_rung", "Fix lowest-scoring pre-build ladder rung from report", "readiness_ladder"),
                ("memory_no_repeat", "Check CaveBuildAgentMemory.json — do not repeat failures", "readiness_ladder"),
                ("proceed_build", "Only after acceptable pre-build score: user runs Build Complete Cave", "proceed_build"),
            };

            for (var i = 0; i < items.Length; i++)
            {
                var (id, label, phase) = items[i];
                var blocked = phase == "readiness_ladder" && hasCompileErrors;
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{id}\",");
                sb.AppendLine($"      \"label\": \"{Escape(label)}\",");
                sb.AppendLine($"      \"phase\": \"{phase}\",");
                sb.AppendLine("      \"checked\": false,");
                sb.AppendLine($"      \"blockedUntilCompileClean\": {(blocked ? "true" : "false")}");
                sb.Append("    }");
                sb.AppendLine(i < items.Length - 1 ? "," : "");
            }
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
