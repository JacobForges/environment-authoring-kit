#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class TerrainBuildCursorGraderDiagnostics
    {
        public const string LastRunPath =
            CaveBuildAgentContextExporter.Folder + "/TerrainBuildCursorLastRun.json";

        [Serializable]
        class LastRunDisk
        {
            public int exitCode;
            public string error;
            public string message;
            public string workflow;
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

                if (disk.error == "missing_api_key")
                    return "CURSOR_API_KEY missing — Sync API Key from .env";
                if (disk.error == "missing_terrain_report")
                    return "SurfaceTerrainQualityReport.json missing — Re-grade terrain";
                if (disk.error == "cursor_agent_startup")
                    return "Cursor startup failed: " + (disk.message ?? "see Console");
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
#endif
