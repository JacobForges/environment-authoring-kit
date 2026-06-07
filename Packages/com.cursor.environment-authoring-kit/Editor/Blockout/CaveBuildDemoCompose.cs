#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Pillow + ffmpeg compose path (Ken Burns, crossfades, branded lower-thirds).</summary>
    static class CaveBuildDemoCompose
    {
        const string ComposeScript = "compose-demo-recap.py";
        const int TimeoutMs = 600_000;

        public static bool TryComposeVideo(
            string runFolder,
            IList<CaveBuildDemoAutoRecorder.FrameInfo> frames,
            string buildMode)
        {
            if (frames == null || frames.Count == 0 || string.IsNullOrEmpty(runFolder))
                return false;

            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var script = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath, ComposeScript);
                if (!File.Exists(script))
                {
                    Debug.LogWarning("[DemoRecorder] Missing " + ComposeScript + " — cannot compose recap video.");
                    return false;
                }

                WriteComposeSpec(runFolder, frames, buildMode);

                if (!CaveBuildCursorProcessResolver.TryResolveNode(out var nodePath, out var resolveError))
                {
                    Debug.LogWarning("[DemoRecorder] Compose skipped — " + resolveError);
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = nodePath,
                    Arguments = $"\"{script}\" \"{Path.Combine(runFolder, "DemoRecapCompose.json")}\"",
                    WorkingDirectory = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                CaveBuildCursorProcessResolver.ApplyEnvironment(psi, hub, null, "demo_recap_compose");
                psi.EnvironmentVariables["HUB_ROOT"] = hub;

                using var proc = Process.Start(psi);
                if (proc == null)
                    return false;

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(TimeoutMs))
                {
                    try
                    {
                        proc.Kill();
                    }
                    catch
                    {
                        // ignore
                    }

                    Debug.LogWarning("[DemoRecorder] Compose timed out.");
                    return false;
                }

                if (proc.ExitCode != 0)
                {
                    Debug.LogWarning(
                        "[DemoRecorder] Compose failed — " +
                        (string.IsNullOrEmpty(stderr) ? $"exit {proc.ExitCode}" : stderr.Trim()));
                    return false;
                }

                var output = Path.Combine(runFolder, "DemoRecap.mp4");
                if (!File.Exists(output))
                {
                    Debug.LogWarning("[DemoRecorder] Compose finished but DemoRecap.mp4 is missing.");
                    return false;
                }

                Debug.Log("[DemoRecorder] Composed recap with motion + transitions: " + output);
                if (!string.IsNullOrEmpty(stdout))
                    Debug.Log("[DemoRecorder] " + stdout.Trim());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Compose error: " + ex.Message);
                return false;
            }
        }

        static void WriteComposeSpec(
            string runFolder,
            IList<CaveBuildDemoAutoRecorder.FrameInfo> frames,
            string buildMode)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"slideDuration\": 3.0,");
            sb.AppendLine("  \"xfadeDuration\": 0.45,");
            sb.AppendLine("  \"introTitle\": \"World Build Recap\",");
            sb.Append("  \"introSubtitle\": ");
            sb.Append(JsonString($"Narrated {buildMode} build — Scene view checkpoints with context"));
            sb.AppendLine(",");
            sb.AppendLine("  \"outroTitle\": \"Recap complete\",");
            sb.AppendLine("  \"outroSubtitle\": \"Re-open Scene view to keep iterating on terrain, mountains, and caves\",");
            sb.AppendLine("  \"frames\": [");
            for (var i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                if (i > 0)
                    sb.AppendLine(",");
                var chapter = string.IsNullOrEmpty(f.phase) ? $"Milestone {i + 1}" : f.phase;
                sb.Append("    {");
                sb.Append("\"image\":").Append(JsonString(f.imagePath ?? ""));
                sb.Append(",\"line1\":").Append(JsonString(f.line1 ?? ""));
                sb.Append(",\"line2\":").Append(JsonString(f.line2 ?? ""));
                sb.Append(",\"line3\":").Append(JsonString(f.line3 ?? ""));
                sb.Append(",\"chapter\":").Append(JsonString(chapter));
                sb.Append(",\"phase\":").Append(JsonString(f.phase ?? ""));
                sb.Append(",\"sub\":").Append(JsonString(f.sub ?? ""));
                sb.Append('}');
            }

            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(runFolder, "DemoRecapCompose.json"), sb.ToString());
        }

        static string JsonString(string s) =>
            "\"" + (s ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", " ")
                .Replace("\r", " ") + "\"";
    }
}
#endif
