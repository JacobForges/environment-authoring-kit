using System;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Reads grade-and-fix.ts last-run diagnostics for fast-failure hints in Unity.</summary>
    static class CaveBuildCursorGraderDiagnostics
    {
        public const string LastRunPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildCursorLastRun.json";

        [Serializable]
        class LastRunDisk
        {
            public int exitCode;
            public string error;
            public string message;
            public string path;
            public string rung;
            public string status;
        }

        public static string TryReadLastRunSummary()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, LastRunPath);
            if (!File.Exists(path))
                return null;

            try
            {
                var disk = JsonUtility.FromJson<LastRunDisk>(File.ReadAllText(path));
                if (disk == null)
                    return null;

                if (!string.IsNullOrEmpty(disk.error) && disk.error == "missing_api_key")
                    return "CURSOR_API_KEY missing — Sync API Key from .env";
                if (!string.IsNullOrEmpty(disk.error) && disk.error == "missing_quality_report")
                    return "CaveBuildQualityReport.json missing — Re-grade in Cave Build Grader";
                if (!string.IsNullOrEmpty(disk.error) && disk.error == "cursor_agent_startup")
                    return "Cursor agent startup failed: " + (disk.message ?? "see Console");
                if (!string.IsNullOrEmpty(disk.message))
                    return disk.message;
                if (!string.IsNullOrEmpty(disk.error))
                    return disk.error + (disk.exitCode != 0 ? $" (exit {disk.exitCode})" : "");
            }
            catch
            {
                // ignored
            }

            return null;
        }
    }
}
