#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Generates FullWorld concept guide PNGs (0–9) under ResearchCache.</summary>
    public static class FullWorldConceptImageAuthor
    {
        const string ScriptRel =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/generate-fullworld-concept-images.py";

        public static bool GenerateAll()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var scriptPath = Path.Combine(hub, ScriptRel);
            if (!File.Exists(scriptPath))
            {
                Debug.LogError($"[CaveBuild] Concept image script missing: {scriptPath}");
                return false;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory = hub,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                Debug.LogError("[CaveBuild] Failed to start python3 for concept image generation.");
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (!string.IsNullOrEmpty(stdout))
                Debug.Log("[CaveBuild] " + stdout.TrimEnd());
            if (!string.IsNullOrEmpty(stderr))
                Debug.LogWarning("[CaveBuild] " + stderr.TrimEnd());

            if (proc.ExitCode != 0)
            {
                Debug.LogError($"[CaveBuild] Concept image generation failed (exit {proc.ExitCode}).");
                return false;
            }

            AssetDatabase.Refresh();
            Debug.Log("[CaveBuild] Generated FullWorld concept images 0–9.");
            return true;
        }

        [MenuItem("Environment Kit/Generate FullWorld Concept Images (0–9)")]
        public static void MenuGenerateAll() => GenerateAll();
    }
}
#endif
