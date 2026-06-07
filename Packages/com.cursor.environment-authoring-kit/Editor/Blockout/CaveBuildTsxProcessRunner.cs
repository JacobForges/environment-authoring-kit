#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Runs node/tsx without blocking <see cref="EditorApplication.update"/> (Unity UI stays responsive).
    /// Research: research-workflow.md (no blocking sync during validate); Ubisoft FC5 64m incremental bakes.
    /// </summary>
    [InitializeOnLoad]
    public static class CaveBuildTsxProcessRunner
    {
        const double HeartbeatIntervalSeconds = 1.25;
        const int MaxCapturedLogChars = 48_000;

        static readonly string ActivePidFilePath = Path.Combine(
            Path.GetTempPath(),
            "cursor-cave-build-tsx.pid");

        static CaveBuildTsxProcessRunner() => TryKillPidFileProcess("editor startup");

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

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    AppendCapturedLine(run, run.Stdout, e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    AppendCapturedLine(run, run.Stderr, e.Data, isErr: true);
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            _active = run;
            TryWritePidFile(proc.Id);
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

            if (PollOnce() && _active != null)
                CompleteActiveRunFromProcessExit();
        }

        static void AppendCapturedLine(ActiveRun run, StringBuilder buffer, string line, bool isErr = false)
        {
            if (run == null || string.IsNullOrWhiteSpace(line))
                return;

            var trimmed = line.TrimEnd();
            if (buffer.Length < MaxCapturedLogChars)
            {
                buffer.AppendLine(trimmed);
                if (buffer.Length > MaxCapturedLogChars)
                    buffer.Length = MaxCapturedLogChars;
            }

            run.LastLine = trimmed;
            var options = run.Options;
            if (trimmed.Contains("[CaveCursor:", StringComparison.Ordinal) ||
                trimmed.Contains("[ResearchCache]", StringComparison.Ordinal) ||
                trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                UnityEngine.Debug.Log(trimmed);
                CaveBuildPipelineLog.Info(trimmed, "Tsx");
            }

            CaveBuildRunStatusPublisher.SetSubOperation(
                options?.LiveOperationLabel ?? options?.SuccessLabel ?? "tsx",
                trimmed);
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
                    KillProcessTree(proc);
                    CompleteActiveRun(
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

        static void CompleteActiveRunFromProcessExit()
        {
            var run = _active;
            if (run?.Proc == null)
            {
                CompleteActiveRun(false, "tsx process missing.");
                return;
            }

            var proc = run.Proc;
            try
            {
                if (!proc.HasExited)
                    proc.WaitForExit(500);
            }
            catch
            {
                // ignored
            }

            var options = run.Options;
            var ok = proc.ExitCode == 0;
            var message = ok
                ? $"{options.SuccessLabel} → OK"
                : $"{options.SuccessLabel} exited {proc.ExitCode}.";
            CompleteActiveRun(ok, message);
        }

        static void CompleteActiveRun(bool ok, string message)
        {
            EditorApplication.update -= PollActiveRun;
            var run = _active;
            _active = null;
            ClearPidFile();

            if (run == null)
                return;

            var proc = run.Proc;
            var options = run.Options;
            var outText = run.Stdout.ToString().TrimEnd();
            var errText = run.Stderr.ToString().TrimEnd();
            run.Stdout.Clear();
            run.Stderr.Clear();

            if (!string.IsNullOrWhiteSpace(outText))
                UnityEngine.Debug.Log(outText);
            if (!string.IsNullOrWhiteSpace(errText))
                UnityEngine.Debug.LogWarning(errText);

            CaveBuildRunStatusPublisher.SetSubOperation(options.SuccessLabel, ok ? "done" : message);
            var onComplete = run.OnComplete;
            CaveBuildActionPacing.QueueWhenIdle(
                () => onComplete?.Invoke(ok, message),
                CaveBuildPipelineDomains.QueueLabel($"tsx complete — {options.SuccessLabel}"));
            EditorApplication.QueuePlayerLoopUpdate();
            DisposeProcess(proc);
        }

        static void DisposeProcess(Process proc)
        {
            if (proc == null)
                return;

            try
            {
                if (!proc.HasExited)
                    KillProcessTree(proc);
            }
            catch
            {
                // ignored
            }

            try
            {
                proc.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        static void KillProcessTree(Process proc)
        {
            if (proc == null)
                return;

            try
            {
                if (proc.HasExited)
                    return;

                var pid = proc.Id;
                KillChildProcessesUnix(pid);

                proc.Kill();
                try
                {
                    proc.WaitForExit(2000);
                }
                catch
                {
                    // ignored
                }
            }
            catch
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill();
                }
                catch
                {
                    // ignored
                }
            }
        }

        /// <summary>Kill direct child processes (node/tsx workers) — Unity's runtime lacks Kill(entireProcessTree).</summary>
        static void KillChildProcessesUnix(int parentPid)
        {
            if (parentPid <= 0)
                return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/pkill",
                    Arguments = $"-P {parentPid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var childKill = Process.Start(psi);
                childKill?.WaitForExit(1500);
            }
            catch
            {
                // ignored — Windows/editor without pkill falls back to parent Kill only
            }
        }

        static void TryWritePidFile(int pid)
        {
            try
            {
                File.WriteAllText(ActivePidFilePath, pid.ToString());
            }
            catch
            {
                // ignored
            }
        }

        static void ClearPidFile()
        {
            try
            {
                if (File.Exists(ActivePidFilePath))
                    File.Delete(ActivePidFilePath);
            }
            catch
            {
                // ignored
            }
        }

        static void TryKillPidFileProcess(string reason)
        {
            try
            {
                if (!File.Exists(ActivePidFilePath))
                    return;

                if (!int.TryParse(File.ReadAllText(ActivePidFilePath).Trim(), out var pid) || pid <= 0)
                {
                    ClearPidFile();
                    return;
                }

                Process proc = null;
                try
                {
                    proc = Process.GetProcessById(pid);
                }
                catch
                {
                    ClearPidFile();
                    return;
                }

                if (proc != null && !proc.HasExited)
                {
                    KillProcessTree(proc);
                    UnityEngine.Debug.Log(
                        $"[CaveBuild] Killed orphaned tsx pid {pid} ({reason}).");
                }

                DisposeProcess(proc);
            }
            catch
            {
                // ignored
            }
            finally
            {
                ClearPidFile();
            }
        }

        /// <summary>
        /// Kills stray node/tsx children from Tools/cave-grader (research sync, prompts, etc.).
        /// Safe to run when Unity is idle — does not kill unrelated node apps unless they match cave-grader paths.
        /// </summary>
        public static int KillOrphanResearchNodeProcesses()
        {
            CancelActive("kill orphans");
            var killed = 0;
            killed += RunPkillPattern("tsx.*cave-grader");
            killed += RunPkillPattern("node.*cave-grader");
            killed += RunPkillPattern("research-cache-sync");
            killed += RunPkillPattern("export-research-catalog");
            killed += RunPkillPattern("generate-research-action-plan");
            TryKillPidFileProcess("orphan sweep");
            if (killed > 0)
            {
                GC.Collect();
                EditorUtility.UnloadUnusedAssetsImmediate();
            }

            return killed;
        }

        static int RunPkillPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return 0;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/pkill",
                    Arguments = $"-f \"{pattern}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                    return 0;

                proc.WaitForExit(2000);
                return proc.ExitCode == 0 ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static void CancelActive(string reason = "cancelled")
        {
            if (_active == null)
                return;

            var label = _active.Options?.SuccessLabel ?? "tsx";
            KillProcessTree(_active.Proc);
            CompleteActiveRun(false, $"{label}: {reason}");
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
