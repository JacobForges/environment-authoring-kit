#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Generates Hollow Titan concept guide PNGs (master + phases 00–11).</summary>
    public static class HollowTitanConceptImageAuthor
    {
        const string ImageScriptRel =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/generate-hollow-titan-concept-images.py";
        public static bool GenerateAll()
        {
            var ok = RunPython(ImageScriptRel, "Hollow Titan concept images");
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
                Debug.LogError($"[HollowTitan] Script missing: {scriptPath}");
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
                Debug.LogError($"[HollowTitan] Failed to start python3 for {label}.");
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (!string.IsNullOrEmpty(stdout))
                Debug.Log("[HollowTitan] " + stdout.TrimEnd());
            if (!string.IsNullOrEmpty(stderr))
                Debug.LogWarning("[HollowTitan] " + stderr.TrimEnd());
            if (proc.ExitCode != 0)
            {
                Debug.LogError($"[HollowTitan] {label} failed (exit {proc.ExitCode}).");
                return false;
            }

            Debug.Log($"[HollowTitan] Generated {label}.");
            return true;
        }

        static void ExportManifest()
        {
            CaveBuildPhasePromptBridge.ExportHollowTitanPhasePromptManifest(out var msg);
            if (!string.IsNullOrEmpty(msg))
                Debug.Log("[HollowTitan] " + msg);
        }

        [MenuItem("Environment Kit/Generate Hollow Titan Concept Images")]
        public static void MenuGenerateAll() => GenerateAll();
    }
}
#endif
