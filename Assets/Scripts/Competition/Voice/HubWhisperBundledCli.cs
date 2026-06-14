using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Hub.Competition
{
    /// <summary>Resolves whisper.cpp CLI bundled under StreamingAssets for standalone .app / .exe.</summary>
    static class HubWhisperBundledCli
    {
        const string VoiceRoot = "Voice";
        const string BinRoot = "bin";

        public static string ResolveExecutablePath()
        {
            var folder = PlatformFolder();
            if (string.IsNullOrEmpty(folder))
                return null;

            var fileName = folder.StartsWith("win", StringComparison.OrdinalIgnoreCase)
                ? "whisper-cli.exe"
                : "whisper-cli";

            var extracted = ExtractedCliPath(folder, fileName);
            if (IsRunnable(extracted))
                return extracted;

            var bundled = BundledCliPath(folder, fileName);
            if (!File.Exists(bundled))
                return null;

            if (TryMaterializeCli(bundled, extracted))
                return extracted;

            // Windows can sometimes run directly from StreamingAssets.
            if (folder.StartsWith("win", StringComparison.OrdinalIgnoreCase) && File.Exists(bundled))
                return bundled;

            return IsRunnable(extracted) ? extracted : null;
        }

        public static bool HasBundledAssets()
        {
            var folder = PlatformFolder();
            if (string.IsNullOrEmpty(folder))
                return false;

            var fileName = folder.StartsWith("win", StringComparison.OrdinalIgnoreCase)
                ? "whisper-cli.exe"
                : "whisper-cli";

            return File.Exists(BundledCliPath(folder, fileName))
                   || IsRunnable(ExtractedCliPath(folder, fileName));
        }

        static string PlatformFolder()
        {
            if (Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.WindowsEditor)
            {
                return "win_x64";
            }

            if (Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.OSXEditor)
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    || RuntimeInformation.ProcessArchitecture == Architecture.Arm
                    ? "osx_arm64"
                    : "osx_x64";
            }

            return null;
        }

        static string StreamingVoiceRoot() =>
            Path.Combine(Application.streamingAssetsPath, CompetitionPaths.RootFolder, VoiceRoot);

        static string BundledCliPath(string platformFolder, string fileName) =>
            Path.Combine(StreamingVoiceRoot(), BinRoot, platformFolder, fileName);

        static string ExtractedCliPath(string platformFolder, string fileName) =>
            Path.Combine(
                Application.persistentDataPath,
                CompetitionPaths.RootFolder,
                VoiceRoot,
                BinRoot,
                platformFolder,
                fileName);

        static bool IsRunnable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            if (Application.platform is RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor)
            {
                try
                {
                    using var chmod = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "/bin/chmod",
                            Arguments = $"+x \"{path}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        },
                    };
                    chmod.Start();
                    chmod.WaitForExit(5000);
                }
                catch
                {
                    // ignore
                }
            }

            return true;
        }

        static bool TryMaterializeCli(string bundledPath, string extractedPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(extractedPath);
                if (string.IsNullOrEmpty(dir))
                    return false;

                Directory.CreateDirectory(dir);

                var stamp = extractedPath + ".stamp";
                var bundledTime = File.GetLastWriteTimeUtc(bundledPath);
                if (File.Exists(extractedPath)
                    && File.Exists(stamp)
                    && File.ReadAllText(stamp).Trim() == bundledTime.Ticks.ToString())
                {
                    return IsRunnable(extractedPath);
                }

                File.Copy(bundledPath, extractedPath, overwrite: true);
                File.WriteAllText(stamp, bundledTime.Ticks.ToString());
                return IsRunnable(extractedPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Voice] Could not extract bundled whisper-cli — " + ex.Message);
                return false;
            }
        }
    }
}
