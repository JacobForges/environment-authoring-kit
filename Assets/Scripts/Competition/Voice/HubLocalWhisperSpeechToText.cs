using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Hub.Competition
{
    /// <summary>
    /// Offline STT via whisper.cpp CLI + ggml-tiny.en (see Tools/competition-voice/setup-whisper-local.sh).
    /// </summary>
    public static class HubLocalWhisperSpeechToText
    {
        const string EnvCli = "HUB_WHISPER_CLI";
        const string EnvModel = "HUB_WHISPER_MODEL";
        const int ProcessTimeoutMs = 45000;

        public static bool IsAvailable
        {
            get
            {
                if (!string.IsNullOrEmpty(ResolveCliPath()) && File.Exists(ResolveModelPath()))
                    return true;

                return File.Exists(ResolvePythonScriptPath());
            }
        }

        public static async Task<string> TryTranscribeWavAsync(byte[] wavBytes)
        {
            if (wavBytes == null || wavBytes.Length < 44 || !IsAvailable)
                return null;

            var cli = ResolveCliPath();
            var model = ResolveModelPath();
            var tempDir = Path.Combine(Application.temporaryCachePath, "hub_whisper");
            Directory.CreateDirectory(tempDir);
            var wavPath = Path.Combine(tempDir, $"cmd_{Guid.NewGuid():N}.wav");

            try
            {
                await File.WriteAllBytesAsync(wavPath, wavBytes);
                if (!string.IsNullOrEmpty(cli))
                    return await Task.Run(() => RunWhisperCli(cli, model, wavPath));

                return await Task.Run(() => RunPythonWhisper(wavPath));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Voice] Local Whisper — " + ex.Message);
                return null;
            }
            finally
            {
                TryDelete(wavPath);
            }
        }

        static string RunPythonWhisper(string wavPath)
        {
            var script = ResolvePythonScriptPath();
            if (string.IsNullOrEmpty(script))
                return null;

            var python = ResolvePythonPath();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = $"\"{script}\" \"{wavPath}\" --model tiny.en",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
                return null;

            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignore
                }

                return null;
            }

            if (process.ExitCode != 0 && process.ExitCode != 1)
            {
                var err = process.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(err))
                    UnityEngine.Debug.LogWarning("[Voice] Python Whisper — " + err.Trim());
                return null;
            }

            return NormalizeTranscript(stdout);
        }

        static string ResolvePythonScriptPath()
        {
            var hubRoot = HubRepoPaths.RepoRoot;
            if (string.IsNullOrEmpty(hubRoot))
                return null;

            var script = Path.Combine(hubRoot, "Tools/competition-voice/transcribe_wav.py");
            return File.Exists(script) ? script : null;
        }

        static string ResolvePythonPath()
        {
            var env = Environment.GetEnvironmentVariable("HUB_WHISPER_PYTHON");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            var hubRoot = HubRepoPaths.RepoRoot;
            if (!string.IsNullOrEmpty(hubRoot))
            {
                var venv = Path.Combine(hubRoot, ".venv-competition-onnx/bin/python3");
                if (File.Exists(venv))
                    return venv;
            }

            return "python3";
        }

        static string RunWhisperCli(string cli, string model, string wavPath)
        {
            var args = $"-m \"{model}\" -f \"{wavPath}\" -l en -nt -np";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
                return null;

            var stdout = new StringBuilder();
            while (!process.StandardOutput.EndOfStream)
                stdout.AppendLine(process.StandardOutput.ReadLine());

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignore
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                var err = process.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(err))
                    UnityEngine.Debug.LogWarning("[Voice] Local Whisper — " + err.Trim());
                return null;
            }

            return NormalizeTranscript(stdout.ToString());
        }

        static string NormalizeTranscript(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var sb = new StringBuilder();
            foreach (var line in raw.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("[", StringComparison.Ordinal))
                    continue;

                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(t);
            }

            var text = sb.ToString().Trim();
            return text.Length < 2 ? null : text;
        }

        public static string ResolveCliPath()
        {
            var env = Environment.GetEnvironmentVariable(EnvCli);
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env.Trim()))
                return env.Trim();

            var bundled = HubWhisperBundledCli.ResolveExecutablePath();
            if (!string.IsNullOrEmpty(bundled))
                return bundled;

            var hubRoot = HubRepoPaths.RepoRoot;
            if (string.IsNullOrEmpty(hubRoot))
                return null;

            var candidates = new[]
            {
                Path.Combine(hubRoot, "Tools/competition-voice/bin/whisper-cli"),
                Path.Combine(hubRoot, "Tools/competition-voice/bin/main"),
                Path.Combine(hubRoot, "Tools/competition-voice/whisper-cli"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        public static string ResolveModelPath()
        {
            var env = Environment.GetEnvironmentVariable(EnvModel);
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env.Trim()))
                return env.Trim();

            var streaming = Path.Combine(
                Application.streamingAssetsPath,
                CompetitionPaths.RootFolder,
                "Voice",
                "ggml-tiny.en.bin");
            if (File.Exists(streaming))
                return streaming;

            var hubRoot = HubRepoPaths.RepoRoot;
            if (string.IsNullOrEmpty(hubRoot))
                return null;

            var dev = Path.Combine(hubRoot, "Tools/competition-voice/models/ggml-tiny.en.bin");
            return File.Exists(dev) ? dev : null;
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }

    static class HubRepoPaths
    {
        public static string RepoRoot
        {
            get
            {
                try
                {
                    var assets = Application.dataPath;
                    if (string.IsNullOrEmpty(assets))
                        return null;

                    var dir = new DirectoryInfo(assets);
                    return dir.Parent?.FullName;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
