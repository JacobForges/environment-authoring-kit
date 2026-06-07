using System;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Parsed from grade-and-fix / agent stdout — triggers immediate workflow advance.</summary>
    public readonly struct CaveBuildPhaseCompleteSignal
    {
        public const string Prefix = "[CaveCursor:phase-complete]";

        public string Workflow { get; }
        public string Rung { get; }
        public string Reason { get; }

        public CaveBuildPhaseCompleteSignal(string workflow, string rung, string reason)
        {
            Workflow = workflow ?? string.Empty;
            Rung = rung ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public static bool TryParse(string line, out CaveBuildPhaseCompleteSignal signal)
        {
            signal = default;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var trimmed = line.Trim();
            if (trimmed.Contains(Prefix, StringComparison.Ordinal))
            {
                return TryParseKeyValueLine(trimmed, out signal);
            }

            if (trimmed.Contains("PREBUILD_COMPILE_CLEAN", StringComparison.Ordinal))
            {
                signal = new CaveBuildPhaseCompleteSignal("pre_build", "compile_gate", "no_errors");
                return true;
            }

            const string legacy = "PREBUILD_RUNG_COMPLETE:";
            var idx = trimmed.IndexOf(legacy, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var rung = trimmed.Substring(idx + legacy.Length).Trim();
                var end = rung.IndexOfAny(new[] { ' ', '\t', '|', '\r', '\n' });
                if (end > 0)
                    rung = rung.Substring(0, end);
                signal = new CaveBuildPhaseCompleteSignal("pre_build", rung, "done");
                return true;
            }

            return false;
        }

        static bool TryParseKeyValueLine(string line, out CaveBuildPhaseCompleteSignal signal)
        {
            signal = default;
            var workflow = ExtractToken(line, "workflow=");
            var rung = ExtractToken(line, "rung=");
            var reason = ExtractToken(line, "reason=");
            if (string.IsNullOrEmpty(rung))
                return false;

            signal = new CaveBuildPhaseCompleteSignal(workflow ?? "pre_build", rung, reason ?? "done");
            return true;
        }

        static string ExtractToken(string line, string key)
        {
            var idx = line.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return null;

            var start = idx + key.Length;
            var end = line.IndexOf(' ', start);
            if (end < 0)
                end = line.Length;
            return line.Substring(start, end - start).Trim();
        }
    }
}
