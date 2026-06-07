using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Parses grade-and-fix.ts stderr/stdout into readable Unity Console lines.</summary>
    sealed class CaveCursorStreamLog
    {
        const int ThinkingFlushChars = 160;
        const int MaxErrorTail = 8;
        const string Prefix = "[CaveCursor]";

        static readonly Queue<string> s_recentErrors = new();

        readonly string _rung;
        readonly int _chainIndex;
        readonly int _chainTotal;
        readonly StringBuilder _thinkingBuffer = new();

        public CaveCursorStreamLog(string rung, int chainIndex, int chainTotal)
        {
            _rung = string.IsNullOrWhiteSpace(rung) ? "?" : rung.Trim();
            _chainIndex = chainIndex;
            _chainTotal = chainTotal;
        }

        public void OnStdout(string line) => HandleLine(line, isError: false);

        public void OnStderr(string line) => HandleLine(line, isError: true);

        public void Flush()
        {
            FlushThinking(force: true);
        }

        void HandleLine(string line, bool isError)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var trimmed = line.Trim();

            if (CaveBuildPhaseCompleteSignal.TryParse(trimmed, out var phaseComplete))
            {
                CaveBuildCursorAgentBridge.NotifyStreamPhaseComplete(phaseComplete);
                Log($"Phase-complete flag: workflow={phaseComplete.Workflow} rung={phaseComplete.Rung} ({phaseComplete.Reason})");
                return;
            }

            if (trimmed.StartsWith("[CaveCursor:info]"))
            {
                Log(trimmed.Substring("[CaveCursor:info]".Length).Trim());
                return;
            }

            if (trimmed.StartsWith("[CaveCursor:status]"))
            {
                var body = trimmed.Substring("[CaveCursor:status]".Length).Trim();
                Log($"Status ({_rung}): {body}");
                return;
            }

            if (trimmed.StartsWith("[CaveCursor:tool]"))
            {
                var body = trimmed.Substring("[CaveCursor:tool]".Length).Trim();
                var parts = body.Split('|');
                var status = parts.Length > 0 ? parts[0] : "?";
                var name = parts.Length > 1 ? parts[1] : "?";
                var detail = parts.Length > 2 ? parts[2] : "";
                if (status == "error")
                    LogWarning($"Tool FAILED ({_rung}) {name}: {Truncate(detail, 240)}");
                else
                    Log($"Tool OK ({_rung}) {name}");
                return;
            }

            if (trimmed.StartsWith("[CaveCursor:thinking]"))
            {
                AppendThinking(trimmed.Substring("[CaveCursor:thinking]".Length));
                return;
            }

            if (trimmed.StartsWith("[CaveCursor:assistant]"))
            {
                FlushThinking(force: true);
                Log($"Assistant ({_rung}): {Truncate(trimmed.Substring("[CaveCursor:assistant]".Length).Trim(), 400)}");
                return;
            }

            if (trimmed.StartsWith("[status]"))
            {
                Log($"Status ({_rung}): {trimmed.Substring("[status]".Length).Trim()}");
                return;
            }

            if (trimmed.StartsWith("[tool error]"))
            {
                FlushThinking(force: true);
                LogWarning($"Tool FAILED ({_rung}): {trimmed.Substring("[tool error]".Length).Trim()}");
                return;
            }

            if (trimmed.StartsWith("[tool ok]"))
            {
                Log($"Tool OK ({_rung}): {trimmed.Substring("[tool ok]".Length).Trim()}");
                return;
            }

            if (trimmed.StartsWith("[thinking]"))
            {
                AppendThinking(trimmed.Substring("[thinking]".Length));
                return;
            }

            FlushThinking(force: false);

            if (isError &&
                (trimmed.StartsWith("Run status:") ||
                 trimmed.StartsWith("Agent run") ||
                 trimmed.StartsWith("Startup failed") ||
                 trimmed.StartsWith("Invalid JSON")))
                LogWarning(trimmed);
            else if (!isError &&
                     (trimmed.StartsWith("Prompt ladder") ||
                      trimmed.StartsWith("Prompt saved") ||
                      trimmed.StartsWith("Invoking Cursor") ||
                      trimmed.StartsWith("Run started") ||
                      trimmed.StartsWith("Agent finished")))
                Log(trimmed);
            else if (isError)
            {
                PushError(trimmed);
                LogWarning(trimmed);
            }
            else
                Log(trimmed);
        }

        static void PushError(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            s_recentErrors.Enqueue(line.Trim());
            while (s_recentErrors.Count > MaxErrorTail)
                s_recentErrors.Dequeue();
        }

        public static string GetRecentErrorSummary()
        {
            if (s_recentErrors.Count == 0)
                return CaveBuildCursorGraderDiagnostics.TryReadLastRunSummary();

            var parts = new List<string>();
            foreach (var line in s_recentErrors)
                parts.Add(Truncate(line, 120));
            return string.Join(" | ", parts);
        }

        public static void ClearRecentErrors() => s_recentErrors.Clear();

        void AppendThinking(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk))
                return;

            var text = chunk.Trim();
            if (_thinkingBuffer.Length > 0 && !_thinkingBuffer.ToString().EndsWith(" "))
                _thinkingBuffer.Append(' ');
            _thinkingBuffer.Append(text);
            FlushThinking(force: false);
        }

        void FlushThinking(bool force)
        {
            if (_thinkingBuffer.Length == 0)
                return;

            var text = _thinkingBuffer.ToString().Trim();
            var endsSentence = text.EndsWith(".") || text.EndsWith("?") || text.EndsWith("!");
            if (!force && !endsSentence && text.Length < ThinkingFlushChars)
                return;

            Log($"Thinking ({_rung}, pass {_chainIndex}/{_chainTotal}): {Truncate(text, 500)}");
            _thinkingBuffer.Clear();
        }

        static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max) + "…";
        }

        static void Log(string message) =>
            Debug.Log($"{Prefix} {message}");

        static void LogWarning(string message) =>
            Debug.LogWarning($"{Prefix} {message}");
    }
}
