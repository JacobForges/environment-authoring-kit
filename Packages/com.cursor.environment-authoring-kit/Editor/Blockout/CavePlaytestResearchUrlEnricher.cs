#if UNITY_EDITOR
using System.IO;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Runs enrich-research-urls.mjs — merges online digests into CaveBuildResearch.json.</summary>
    public static class CavePlaytestResearchUrlEnricher
    {
        public const string DigestRel =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchUrlDigest.json";

        public const string ScriptRel =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/enrich-research-urls.mjs";

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Enrich Research URLs (online)")]
        public static void RunMenu()
        {
            if (Run(out var msg))
                EditorUtility.DisplayDialog("Research URL Enrichment", msg, "OK");
            else
                EditorUtility.DisplayDialog("Research URL Enrichment", msg, "OK");
        }

        public static bool Run(out string message)
        {
            message = string.Empty;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var script = Path.Combine(hub, ScriptRel);
            if (!File.Exists(script))
            {
                message = $"Missing script: {ScriptRel}";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = node,
                Arguments = $"\"{script}\"",
                WorkingDirectory = hub,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.Environment["HUB_ROOT"] = hub;

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                message = "Failed to start Node process.";
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(600_000);

            if (proc.ExitCode != 0)
            {
                message = $"Enrichment failed (exit {proc.ExitCode}): {stderr}\n{stdout}";
                UnityEngine.Debug.LogWarning("[CaveBuild] " + message);
                return false;
            }

            message = string.IsNullOrWhiteSpace(stdout)
                ? $"Wrote {DigestRel}"
                : stdout.Trim();
            if (!string.IsNullOrWhiteSpace(stderr))
                UnityEngine.Debug.Log("[CaveBuild] URL enrichment log:\n" + stderr);
            UnityEngine.Debug.Log("[CaveBuild] Research URL enrichment OK — " + message);
            return true;
        }

        public static bool DigestIsFresh(int maxAgeHours = 168)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, DigestRel);
            if (!File.Exists(path))
                return false;
            try
            {
                var age = System.DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                return age.TotalHours <= maxAgeHours;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
