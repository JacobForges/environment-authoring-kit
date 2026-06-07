#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveBuildDemoSmartCompose
    {
        const string ScriptName = "compose-smart-recap.py";
        const int TimeoutMs = 900_000;

        public static bool TryComposeSmartVideo(string runFolder)
        {
            if (string.IsNullOrEmpty(runFolder))
                return false;

            var timeline = Path.Combine(runFolder, "DemoRecapTimeline.json");
            var timelapse = Path.Combine(runFolder, "timelapse");
            if (!File.Exists(timeline) || !Directory.Exists(timelapse))
                return false;

            var pngs = Directory.GetFiles(timelapse, "tl_*.png");
            if (pngs.Length < 8)
            {
                Debug.LogWarning($"[DemoRecorder] Smart edit needs timelapse frames (found {pngs.Length}).");
                return false;
            }

            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var script = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath, ScriptName);
                if (!File.Exists(script))
                {
                    Debug.LogWarning("[DemoRecorder] Missing " + ScriptName);
                    return false;
                }

                var output = Path.Combine(runFolder, "DemoRecap.mp4");
                if (!CaveBuildCursorProcessResolver.TryRunPythonScript(
                        hub,
                        ScriptName,
                        $"\"{runFolder}\" \"{output}\"",
                        TimeoutMs,
                        out var stdout,
                        out var stderr,
                        out var err,
                        "demo_smart_recap"))
                {
                    Debug.LogWarning("[DemoRecorder] Smart compose failed — " + (err ?? stderr ?? "unknown"));
                    return false;
                }

                Debug.Log("[DemoRecorder] Smart timelapse recap: " + output + " (" + pngs.Length + " source frames)");
                if (!string.IsNullOrEmpty(stdout))
                    Debug.Log("[DemoRecorder] " + stdout.Trim());
                return File.Exists(output);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Smart compose error: " + ex.Message);
                return false;
            }
        }
    }
}
#endif
