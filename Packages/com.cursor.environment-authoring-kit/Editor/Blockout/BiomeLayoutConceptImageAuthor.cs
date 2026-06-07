#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Generates biome layout concept guide PNGs (master + presets 0–9).</summary>
    public static class BiomeLayoutConceptImageAuthor
    {
        const string ImageScriptRel =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/generate-biome-layout-concept-images.py";

        public static bool GenerateAll()
        {
            var ok = RunPython(ImageScriptRel, "Biome layout concept images");
            if (ok)
                ExportManifest();
            AssetDatabase.Refresh();
            return ok;
        }

        static bool RunPython(string scriptRel, string label)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var scriptPath = Path.Combine(hub, scriptRel);
            if (!File.Exists(scriptPath))
            {
                Debug.LogError($"[BiomeLayout] Script missing: {scriptPath}");
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
                Debug.LogError($"[BiomeLayout] Failed to start python3 for {label}.");
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (!string.IsNullOrEmpty(stdout))
                Debug.Log("[BiomeLayout] " + stdout.TrimEnd());
            if (!string.IsNullOrEmpty(stderr))
                Debug.LogWarning("[BiomeLayout] " + stderr.TrimEnd());
            if (proc.ExitCode != 0)
            {
                Debug.LogError($"[BiomeLayout] {label} failed (exit {proc.ExitCode}).");
                return false;
            }

            Debug.Log($"[BiomeLayout] Generated {label}.");
            return true;
        }

        static void ExportManifest()
        {
            BiomeLayoutConceptCatalog.ExportPhasePromptManifest(out var msg);
            if (!string.IsNullOrEmpty(msg))
                Debug.Log("[BiomeLayout] " + msg);
        }

        [MenuItem("Environment Kit/Generate Biome Layout Concept Images")]
        public static void MenuGenerateAll() => GenerateAll();
    }
}
#endif
