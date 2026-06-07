#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Producer-grade recap: emerald cards, presentation compose, OpenCV callouts (via run-producer-recap.py).
    /// </summary>
    static class CaveBuildDemoProducerCompose
    {
        const string ProducerScript = "run-producer-recap.py";
        const string ApplyCardsScript = "apply-approved-cards.py";
        const int TimeoutMs = 1_800_000; // 30 min full encode

        public static string BrandAssetsFolder(string hubRoot = null) =>
            EnvironmentKitDataRoot.ResolvePath("DemoRecapApproved");

        /// <summary>Copy global approved intro/outro into a capture run if missing.</summary>
        public static void TryCopyBrandAssets(string runFolder, string hubRoot)
        {
            if (string.IsNullOrEmpty(runFolder) || !Directory.Exists(runFolder))
                return;

            var brand = BrandAssetsFolder(hubRoot);
            if (!Directory.Exists(brand))
                return;

            foreach (var name in new[]
                     {
                         "ApprovedIntro.png", "ApprovedOutro.png", "ApprovedCards.json",
                         "_approval_portrait.png", "DemoRecapPortrait.png", "DemoRecapSignature.png",
                     })
            {
                var src = Path.Combine(brand, name);
                var dst = Path.Combine(runFolder, name);
                if (File.Exists(src) && !File.Exists(dst))
                    File.Copy(src, dst);
            }
        }

        public static bool ShouldUseTerminalPersonalVoice() =>
            CaveBuildDemoAutoRecorder.ProducerNarratorEnabled &&
            (Application.platform == RuntimePlatform.OSXEditor ||
             Application.platform == RuntimePlatform.OSXPlayer);

        public static bool TryComposeProducer(string runFolder, bool previewOnly, out string outputPath)
        {
            outputPath = ResolveOutputPath(runFolder, previewOnly);

            if (string.IsNullOrEmpty(runFolder) || !Directory.Exists(runFolder))
                return false;

            var timelapse = Path.Combine(runFolder, "timelapse");
            if (!Directory.Exists(timelapse) || Directory.GetFiles(timelapse, "tl_*.png").Length < 8)
            {
                Debug.LogWarning("[DemoRecorder] Producer compose needs timelapse/tl_*.png (8+ frames).");
                return false;
            }

            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                TryCopyBrandAssets(runFolder, hub);

                if (!CaveBuildCursorProcessResolver.TryRunPythonScript(
                        hub,
                        ApplyCardsScript,
                        $"\"{runFolder}\"",
                        120_000,
                        out _,
                        out var applyErr,
                        out var applyFail,
                        "demo_recap_cards"))
                {
                    Debug.LogWarning("[DemoRecorder] apply-approved-cards: " +
                                     (applyFail ?? applyErr ?? "failed"));
                }

                var useTerminalNarration = ShouldUseTerminalPersonalVoice();
                var args = $"\"{runFolder}\"";
                if (previewOnly)
                    args += " --preview";
                if (useTerminalNarration)
                    args += " --no-narrator";

                var headlessOk = CaveBuildCursorProcessResolver.TryRunPythonScript(
                    hub,
                    ProducerScript,
                    args,
                    TimeoutMs,
                    out var stdout,
                    out var stderr,
                    out var err,
                    "demo_producer_recap");

                if (!string.IsNullOrEmpty(stdout))
                    Debug.Log("[DemoRecorder] " + stdout.Trim());

                if (!headlessOk)
                {
                    Debug.LogWarning("[DemoRecorder] Producer recap headless pass failed — " +
                                     (err ?? stderr ?? "unknown"));
                }

                var silentVideoReady = SilentComposeVideoExists(runFolder, previewOnly);
                var outputReady = File.Exists(outputPath);

                if (useTerminalNarration)
                {
                    var narrationOnly = silentVideoReady || outputReady;
                    EnsureTerminalPersonalVoiceNarration(hub, runFolder, previewOnly, narrationOnly);
                    Debug.Log(
                        narrationOnly
                            ? "[DemoRecorder] Graded video shell ready; Terminal opened for Personal Voice narration (--narration-only)."
                            : "[DemoRecorder] Terminal opened for full producer recap with Personal Voice narration.");
                    return true;
                }

                if (!headlessOk)
                    return false;

                if (!outputReady)
                {
                    var alt = Path.Combine(runFolder, "DemoRecapPresentation.mp4");
                    if (File.Exists(alt))
                        outputPath = alt;
                    else
                    {
                        Debug.LogWarning("[DemoRecorder] Producer finished but output missing: " + outputPath);
                        return false;
                    }
                }

                Debug.Log("[DemoRecorder] Producer recap: " + outputPath);
                TryOpenRecapVideo(outputPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Producer compose error: " + ex.Message);
                if (ShouldUseTerminalPersonalVoice())
                {
                    var hub = CaveBuildCursorSettings.ResolveHubRoot();
                    var narrationOnly = SilentComposeVideoExists(runFolder, previewOnly) ||
                                        File.Exists(ResolveOutputPath(runFolder, previewOnly));
                    EnsureTerminalPersonalVoiceNarration(hub, runFolder, previewOnly, narrationOnly);
                }

                return false;
            }
        }

        /// <summary>
        /// After any recap video shell exists, launch Terminal for Jacob Adkins Personal Voice narration.
        /// </summary>
        public static void EnsurePersonalVoiceNarration(string runFolder, bool previewOnly = false)
        {
            if (!ShouldUseTerminalPersonalVoice() || string.IsNullOrEmpty(runFolder) ||
                !Directory.Exists(runFolder))
                return;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var narrationOnly = SilentComposeVideoExists(runFolder, previewOnly) ||
                                File.Exists(ResolveOutputPath(runFolder, previewOnly));
            EnsureTerminalPersonalVoiceNarration(hub, runFolder, previewOnly, narrationOnly);
        }

        public static bool NeedsPersonalVoiceNarration(string runFolder)
        {
            if (!ShouldUseTerminalPersonalVoice() || string.IsNullOrEmpty(runFolder) ||
                !Directory.Exists(runFolder))
                return false;

            var timelapse = Path.Combine(runFolder, "timelapse");
            if (!Directory.Exists(timelapse) || Directory.GetFiles(timelapse, "tl_*.png").Length < 8)
                return false;

            var output = Path.Combine(runFolder, "DemoRecapPresentation.mp4");
            if (!File.Exists(output))
                return true;

            return !Mp4HasAudioTrack(output);
        }

        static string ResolveOutputPath(string runFolder, bool previewOnly) =>
            previewOnly
                ? Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
                    "DemoRecap-Card-Preview",
                    "DirectorPreview.mp4")
                : Path.Combine(runFolder, "DemoRecapPresentation.mp4");

        static bool SilentComposeVideoExists(string runFolder, bool previewOnly)
        {
            var workDir = previewOnly ? "_presentation_compose_preview120" : "_presentation_compose";
            var graded = Path.Combine(runFolder, workDir, "_final_video.mp4");
            return File.Exists(graded) && new FileInfo(graded).Length > 1024;
        }

        static bool Mp4HasAudioTrack(string mp4Path)
        {
            if (string.IsNullOrEmpty(mp4Path) || !File.Exists(mp4Path))
                return false;
            if (!CaveBuildDemoAutoRecorder.IsFfmpegAvailable)
                return false;

            var ffprobe = ResolveFfprobePath();
            if (string.IsNullOrEmpty(ffprobe))
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobe,
                    Arguments =
                        $"-v error -select_streams a -show_entries stream=codec_type -of csv=p=0 \"{mp4Path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                    return false;
                var stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(8000);
                return proc.ExitCode == 0 && stdout.Contains("audio");
            }
            catch
            {
                return false;
            }
        }

        static string ResolveFfprobePath()
        {
            var ffmpeg = CaveBuildDemoAutoRecorder.FfmpegPath;
            if (string.IsNullOrEmpty(ffmpeg))
                return null;
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? string.Empty, "ffprobe");
            if (File.Exists(sibling))
                return sibling;
            return null;
        }

        /// <summary>
        /// macOS Personal Voice works in Terminal.app, not in Unity's headless python child.
        /// </summary>
        static void EnsureTerminalPersonalVoiceNarration(
            string hubRoot,
            string runFolder,
            bool previewOnly,
            bool narrationOnly)
        {
            var toolsDir = Path.Combine(hubRoot, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var wrapper = Path.Combine(toolsDir, "run-recap-terminal.sh");
            if (!File.Exists(wrapper))
            {
                Debug.LogWarning("[DemoRecorder] Missing run-recap-terminal.sh — run narration manually.");
                return;
            }

            var previewArg = previewOnly ? " --preview" : string.Empty;
            var narrationArg = narrationOnly ? " --narration-only" : string.Empty;
            var shellCmd =
                $"cd {BashQuote(toolsDir)} && bash ./run-recap-terminal.sh {BashQuote(runFolder)}{previewArg}{narrationArg}";
            var escaped = shellCmd.Replace("\\", "\\\\").Replace("\"", "\\\"");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments =
                        "-e 'tell application \"Terminal\"' " +
                        "-e 'activate' " +
                        $"-e 'do script \"{escaped}\"' " +
                        "-e 'end tell'",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
                Debug.Log(
                    "[DemoRecorder] Terminal opened for Personal Voice (Jacob Adkins) narration. " +
                    (narrationOnly
                        ? "Reusing graded video shell (--narration-only). "
                        : "Running full producer recap. ") +
                    "Keep that window open until it finishes (often 30–60 minutes). " +
                    "The recap MP4 will open automatically when narration completes.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[DemoRecorder] Could not open Terminal for narration: " + ex.Message +
                    "\nRun manually:\n  bash " + wrapper + " \"" + runFolder + "\"" +
                    previewArg + narrationArg);
            }
        }

        static string BashQuote(string path) => "'" + (path ?? string.Empty).Replace("'", "'\\''") + "'";

        static void TryOpenRecapVideo(string mp4Path)
        {
            if (string.IsNullOrEmpty(mp4Path) || !File.Exists(mp4Path))
                return;

            if (Application.platform != RuntimePlatform.OSXEditor &&
                Application.platform != RuntimePlatform.OSXPlayer)
            {
                EditorUtility.RevealInFinder(mp4Path);
                return;
            }

            try
            {
                Debug.Log("[DemoRecorder] Opening recap video…");
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = "\"" + mp4Path + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DemoRecorder] Could not open recap video: " + ex.Message);
                EditorUtility.RevealInFinder(mp4Path);
            }
        }
    }
}
#endif
