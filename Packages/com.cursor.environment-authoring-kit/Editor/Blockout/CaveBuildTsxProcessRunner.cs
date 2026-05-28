#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Runs node/tsx without blocking <see cref="EditorApplication.update"/> (Unity UI stays responsive).
    /// Research: research-workflow.md (no blocking sync during validate); Ubisoft FC5 64m incremental bakes.
    /// </summary>
    public static class CaveBuildTsxProcessRunner
    {
        const double HeartbeatIntervalSeconds = 1.25;

        sealed class ActiveRun
        {
            public Process Proc;
            public Options Options;
            public StringBuilder Stdout = new();
            public StringBuilder Stderr = new();
            public string LastLine = string.Empty;
            public double Started;
            public double LastHeartbeat;
            public Action<bool, string> OnComplete;
        }

        static ActiveRun _active;

        public static bool IsRunning => _active != null;

        /// <summary>Non-blocking run — completion on <paramref name="onComplete"/> (may be same frame if process exits instantly).</summary>
        public static void BeginRun(Options options, Action<bool, string> onComplete)
        {
            if (onComplete == null)
                throw new ArgumentNullException(nameof(onComplete));

            if (_active != null)
            {
                onComplete(false, "Another tsx process is already running.");
                return;
            }

            if (options == null || string.IsNullOrEmpty(options.NodePath))
            {
                onComplete(false, "Missing node executable.");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = options.NodePath,
                Arguments = options.Arguments,
                WorkingDirectory = options.ToolsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["HUB_ROOT"] = options.HubRoot;
            psi.EnvironmentVariables["TSX_DISABLE_IPC"] = "1";

            Process proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                onComplete(false, "Failed to start process: " + ex.Message);
                return;
            }

            if (proc == null)
            {
                onComplete(false, "Failed to start process.");
                return;
            }

            var run = new ActiveRun
            {
                Proc = proc,
                Options = options,
                Started = EditorApplication.timeSinceStartup,
                LastHeartbeat = EditorApplication.timeSinceStartup,
                OnComplete = onComplete,
            };

            void OnLine(string line, bool isErr)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;

                var trimmed = line.TrimEnd();
                if (isErr)
                    run.Stderr.AppendLine(trimmed);
                else
                    run.Stdout.AppendLine(trimmed);

                run.LastLine = trimmed;
                if (trimmed.Contains("[CaveCursor:", StringComparison.Ordinal) ||
                    trimmed.Contains("[ResearchCache]", StringComparison.Ordinal) ||
                    trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    UnityEngine.Debug.Log(trimmed);
                    CaveBuildPipelineLog.Info(trimmed, "Tsx");
                }

                CaveBuildRunStatusPublisher.SetSubOperation(
                    options.LiveOperationLabel ?? options.SuccessLabel,
                    trimmed);
            }

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    OnLine(e.Data, false);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    OnLine(e.Data, true);
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            _active = run;
            EditorApplication.update += PollActiveRun;
            CaveBuildRunStatusPublisher.SetSubOperation(
                options.LiveOperationLabel ?? options.SuccessLabel,
                "starting node/tsx…");
        }

        /// <summary>Legacy synchronous API — only for callers outside active builds; prefer <see cref="BeginRun"/>.</summary>
        public static bool Run(Options options, out string message)
        {
            message = null;
            if (CaveBuildStartupCoordinator.IsActive ||
                LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
                CaveBuildActionPacing.IsBusy)
            {
                message =
                    "CaveBuildTsxProcessRunner.Run blocked during build — use BeginRun (main-thread WaitForExit freezes Unity).";
                return false;
            }

            var done = false;
            var ok = false;
            string resultMsg = null;
            BeginRun(options, (success, msg) =>
            {
                ok = success;
                resultMsg = msg;
                done = true;
            });

            while (!done)
            {
                if (!PollOnce())
                    break;
            }

            message = resultMsg;
            return ok;
        }

        static void PollActiveRun()
        {
            if (_active == null)
            {
                EditorApplication.update -= PollActiveRun;
                return;
            }

            if (PollOnce())
                FinishActiveRun();
        }

        static bool PollOnce()
        {
            var run = _active;
            if (run == null)
                return true;

            var proc = run.Proc;
            if (proc == null)
                return true;

            var now = EditorApplication.timeSinceStartup;
            var elapsedMs = (int)((now - run.Started) * 1000.0);
            var deadlineMs = Math.Max(5_000, run.Options.WaitMs);

            if (!proc.HasExited)
            {
                if (elapsedMs >= deadlineMs)
                {
                    try
                    {
                        proc.Kill();
                    }
                    catch
                    {
                        // ignored
                    }

                    run.OnComplete?.Invoke(
                        false,
                        $"{run.Options.SuccessLabel} timed out after {deadlineMs}ms");
                    return true;
                }

                if (now - run.LastHeartbeat >= HeartbeatIntervalSeconds)
                {
                    run.LastHeartbeat = now;
                    var pulse = string.IsNullOrEmpty(run.LastLine)
                        ? $"running… {elapsedMs / 1000}s"
                        : $"{run.LastLine} ({elapsedMs / 1000}s)";
                    CaveBuildRunStatusPublisher.PulseSubOperation(
                        run.Options.LiveOperationLabel ?? run.Options.SuccessLabel,
                        pulse);
                }

                return false;
            }

            return true;
        }

        static void FinishActiveRun()
        {
            EditorApplication.update -= PollActiveRun;
            var run = _active;
            _active = null;
            if (run == null)
                return;

            var proc = run.Proc;
            try
            {
                if (proc != null && !proc.HasExited)
                    proc.WaitForExit(500);
            }
            catch
            {
                // ignored
            }

            var options = run.Options;
            var outText = run.Stdout.ToString().TrimEnd();
            var errText = run.Stderr.ToString().TrimEnd();
            if (!string.IsNullOrWhiteSpace(outText))
                UnityEngine.Debug.Log(outText);
            if (!string.IsNullOrWhiteSpace(errText))
                UnityEngine.Debug.LogWarning(errText);

            string message;
            bool ok;
            if (proc == null || proc.ExitCode != 0)
            {
                message = $"{options.SuccessLabel} exited {proc?.ExitCode ?? -1}.";
                ok = false;
            }
            else
            {
                message = $"{options.SuccessLabel} → OK";
                ok = true;
            }

            CaveBuildRunStatusPublisher.SetSubOperation(options.SuccessLabel, ok ? "done" : message);
            run.OnComplete?.Invoke(ok, message);
            EditorApplication.QueuePlayerLoopUpdate();

            try
            {
                proc?.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        public static void CancelActive(string reason = "cancelled")
        {
            if (_active == null)
                return;

            try
            {
                _active.Proc?.Kill();
            }
            catch
            {
                // ignored
            }

            var label = _active.Options.SuccessLabel;
            _active.OnComplete?.Invoke(false, $"{label}: {reason}");
            FinishActiveRun();
        }

        public sealed class Options
        {
            public string HubRoot;
            public string ToolsDir;
            public string NodePath;
            public string Arguments;
            public int WaitMs = 120_000;
            public string SuccessLabel = "tsx";
            public string LiveOperationLabel;
            public string[] ExtraEnvs;
        }
    }
}
#endif
