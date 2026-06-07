#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Optional end-of-recording AI rewrite of demo captions (Hub AI provider).</summary>
    static class CaveBuildDemoNarrationAi
    {
        const string ScriptName = "demo-recap-narrate.ts";
        const int TimeoutMs = 180_000;

        public const string PrefAiNarration = "EnvironmentKit_DemoRecorder_AiNarration";

        public static bool AiNarrationEnabled
        {
            get => EditorPrefs.GetBool(PrefAiNarration, true);
            set => EditorPrefs.SetBool(PrefAiNarration, value);
        }

        public static bool CanRun =>
            AiNarrationEnabled && CaveBuildCursorSettings.HasCredentialsForActiveProvider();

        public static void TryEnhanceFrames(
            IList<CaveBuildDemoAutoRecorder.FrameInfo> frames,
            string buildMode,
            string runFolder)
        {
            if (frames == null || frames.Count == 0 || !CanRun)
                return;

            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var script = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath, ScriptName);
                if (!File.Exists(script))
                {
                    Debug.LogWarning("[DemoRecorder] Missing " + ScriptName + " — using rule-based captions only.");
                    return;
                }

                var requestPath = Path.Combine(runFolder, "DemoRecapNarrateRequest.json");
                WriteRequest(requestPath, frames, buildMode);

                if (!CaveBuildCursorProcessResolver.TryCreateTsxScript(
                        hub,
                        script,
                        $"\"{requestPath}\"",
                        out var psi,
                        out var resolveError))
                {
                    Debug.LogWarning("[DemoRecorder] AI narration skipped — " + resolveError);
                    return;
                }

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Debug.LogWarning("[DemoRecorder] AI narration skipped — could not start tsx.");
                    return;
                }

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

                    Debug.LogWarning("[DemoRecorder] AI narration timed out — using rule-based captions.");
                    return;
                }

                if (proc.ExitCode != 0)
                {
                    Debug.LogWarning(
                        "[DemoRecorder] AI narration skipped — " +
                        (string.IsNullOrEmpty(stderr) ? $"exit {proc.ExitCode}" : stderr.Trim()));
                    return;
                }

                File.WriteAllText(Path.Combine(runFolder, "DemoRecapNarrateResponse.json"), stdout);
                ApplyResponse(stdout, frames);
                Debug.Log(
                    $"[DemoRecorder] AI narration applied to {frames.Count} slide(s) via " +
                    CaveBuildCursorSettings.ResolveActiveProvider() + ".");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] AI narration failed — using rule-based captions. " + ex.Message);
            }
        }

        static void WriteRequest(string path, IList<CaveBuildDemoAutoRecorder.FrameInfo> frames, string buildMode)
        {
            var sb = new StringBuilder();
            sb.Append("{\"buildMode\":").Append(JsonString(buildMode ?? "")).Append(",\"frames\":[");
            for (var i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                if (i > 0)
                    sb.Append(',');
                sb.Append("{\"i\":").Append(i);
                sb.Append(",\"phase\":").Append(JsonString(f.phase ?? ""));
                sb.Append(",\"sub\":").Append(JsonString(f.sub ?? ""));
                sb.Append(",\"line1\":").Append(JsonString(f.line1 ?? ""));
                sb.Append(",\"line2\":").Append(JsonString(f.line2 ?? ""));
                sb.Append(",\"line3\":").Append(JsonString(f.line3 ?? ""));
                sb.Append('}');
            }

            sb.Append("]}");
            File.WriteAllText(path, sb.ToString());
        }

        static void ApplyResponse(string stdout, IList<CaveBuildDemoAutoRecorder.FrameInfo> frames)
        {
            var json = stdout.Trim();
            if (json.IndexOf("\"captions\"", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("AI response missing captions array.");

            var byIndex = new Dictionary<int, CaveBuildDemoNarration.Lines>();
            var matches = System.Text.RegularExpressions.Regex.Matches(
                json,
                @"\{[^{}]*""i""\s*:\s*(\d+)[^{}]*""line1""\s*:\s*""((?:\\.|[^""\\])*)""[^{}]*""line2""\s*:\s*""((?:\\.|[^""\\])*)""[^{}]*""line3""\s*:\s*""((?:\\.|[^""\\])*)""[^{}]*\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (!m.Success)
                    continue;

                var idx = int.Parse(m.Groups[1].Value);
                byIndex[idx] = new CaveBuildDemoNarration.Lines(
                    UnescapeJson(m.Groups[2].Value),
                    UnescapeJson(m.Groups[3].Value),
                    UnescapeJson(m.Groups[4].Value));
            }

            for (var i = 0; i < frames.Count; i++)
            {
                if (!byIndex.TryGetValue(i, out var lines) || !lines.HasContent)
                    continue;

                frames[i].ApplyLines(lines);
            }
        }

        static string JsonString(string s) =>
            "\"" + (s ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", " ")
                .Replace("\r", " ") + "\"";

        static string UnescapeJson(string s) =>
            s.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
#endif
