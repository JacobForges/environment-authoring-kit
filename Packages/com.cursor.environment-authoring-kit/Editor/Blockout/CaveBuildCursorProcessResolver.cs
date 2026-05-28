using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Unity often lacks shell PATH — resolve node/tsx/npx with full paths.</summary>
    static class CaveBuildCursorProcessResolver
    {
        public static bool TryCreateGradeAndFixProcess(
            string hubRoot,
            string scriptPath,
            bool includeLiveFix,
            string rung,
            out ProcessStartInfo psi,
            out string error,
            string workflow = null)
        {
            psi = null;
            error = null;

            var toolsDir = Path.Combine(hubRoot, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var extraArgs = " --auto --stream --use-exported-prompt" +
                            (includeLiveFix ? " --live" : string.Empty);
            if (!string.IsNullOrWhiteSpace(workflow))
                extraArgs += " --workflow=" + workflow.Trim();
            if (!string.IsNullOrWhiteSpace(rung))
                extraArgs += " --rung=" + rung.Trim();

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (File.Exists(tsxCli))
            {
                if (!TryResolveNode(out var nodePath, out error))
                    return false;

                psi = new ProcessStartInfo
                {
                    FileName = nodePath,
                    Arguments = $"\"{tsxCli}\" \"{scriptPath}\"{extraArgs}",
                    WorkingDirectory = toolsDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                ApplyEnvironment(psi, hubRoot, rung, workflow);
                return true;
            }

            if (!TryResolveNpx(out var npxPath, out error))
                return false;

            psi = new ProcessStartInfo
            {
                FileName = npxPath,
                Arguments = $"--yes tsx \"{scriptPath}\"{extraArgs}",
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ApplyEnvironment(psi, hubRoot, rung, workflow);
            return true;
        }

        public static void ApplyEnvironment(ProcessStartInfo psi, string hubRoot, string rung, string workflow)
        {
            psi.EnvironmentVariables["CURSOR_API_KEY"] = CaveBuildCursorSettings.ResolveApiKey();
            psi.EnvironmentVariables["CAVE_AI_PROVIDER"] = CaveBuildCursorSettings.ResolveActiveProvider().ToString();
            psi.EnvironmentVariables["CAVE_ACTIVE_MODEL"] = CaveBuildCursorSettings.ResolveActiveModelId();
            psi.EnvironmentVariables["CAVE_ACTIVE_BASE_URL"] = CaveBuildCursorSettings.ResolveActiveBaseUrl();
            var activeKey = CaveBuildCursorSettings.ResolveActiveApiKey();
            if (!string.IsNullOrWhiteSpace(activeKey))
                psi.EnvironmentVariables["CAVE_ACTIVE_API_KEY"] = activeKey;
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            psi.EnvironmentVariables["CAVE_EXTERNAL_APPLY_EDITS"] =
                settings.allowExternalProviderEdits ? "1" : "0";
            psi.EnvironmentVariables["CAVE_EXTERNAL_APPLY_DRY_RUN"] =
                settings.externalProviderEditsDryRun ? "1" : "0";
            psi.EnvironmentVariables["GOOGLE_API_KEY"] = settings.GetApiKey(EnvironmentKitAiProvider.GoogleGemini);
            psi.EnvironmentVariables["ANTHROPIC_API_KEY"] = settings.GetApiKey(EnvironmentKitAiProvider.AnthropicClaude);
            psi.EnvironmentVariables["OPENAI_API_KEY"] = settings.GetApiKey(EnvironmentKitAiProvider.OpenAICompatible);
            psi.EnvironmentVariables["OPENROUTER_API_KEY"] = settings.GetApiKey(EnvironmentKitAiProvider.OpenRouter);
            psi.EnvironmentVariables["CUSTOM_API_KEY"] = settings.GetApiKey(EnvironmentKitAiProvider.CustomEndpoint);
            psi.EnvironmentVariables["HUB_ROOT"] = hubRoot;
            psi.EnvironmentVariables["CAVE_CURSOR_MODEL"] = CaveBuildCursorSettings.ResolveModelId();
            if (!string.IsNullOrWhiteSpace(rung))
                psi.EnvironmentVariables["CAVE_CURSOR_RUNG"] = rung.Trim();
            if (!string.IsNullOrWhiteSpace(workflow))
                psi.EnvironmentVariables["CAVE_WORKFLOW"] = workflow.Trim();

            psi.EnvironmentVariables["CAVE_HARDCODED_PROMPTS"] = "1";
            psi.EnvironmentVariables["CAVE_USE_EXPORTED_PROMPT"] = "1";
            psi.EnvironmentVariables["CAVE_CURSOR_WEB_RESEARCH"] = "0";
            var autoIter = System.Environment.GetEnvironmentVariable("CAVE_AUTONOMOUS_ITERATION");
            if (!string.IsNullOrWhiteSpace(autoIter))
                psi.EnvironmentVariables["CAVE_AUTONOMOUS_ITERATION"] = autoIter.Trim();
            // Avoid tsx IPC listen issues when spawned from Unity (macOS).
            psi.EnvironmentVariables["TSX_DISABLE_IPC"] = "1";

            var path = psi.EnvironmentVariables["PATH"] ?? string.Empty;
            foreach (var dir in GetPathCandidates())
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    continue;
                if (path.Contains(dir))
                    continue;
                path = string.IsNullOrEmpty(path) ? dir : dir + Path.PathSeparator + path;
            }

            psi.EnvironmentVariables["PATH"] = path;
        }

        static IEnumerable<string> GetPathCandidates()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!string.IsNullOrWhiteSpace(settings.nodeBinDirectory))
                yield return settings.nodeBinDirectory.TrimEnd('/');

            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
            yield return "/usr/bin";
            yield return "/bin";
        }

        public static bool TryResolveNode(out string nodePath, out string error)
        {
            error = null;
            nodePath = CaveBuildCursorSettings.LoadOrCreate().nodeExecutablePath;
            if (!string.IsNullOrWhiteSpace(nodePath) && File.Exists(nodePath))
                return true;

            foreach (var dir in GetPathCandidates())
            {
                var candidate = Path.Combine(dir, "node");
                if (File.Exists(candidate))
                {
                    nodePath = candidate;
                    return true;
                }
            }

            if (TryWhich("node", out nodePath))
                return true;

            nodePath = null;
            error =
                "Node.js not found. Install Node (brew install node) or set node executable in Cave Build Cursor Settings.";
            return false;
        }

        static bool TryResolveNpx(out string npxPath, out string error)
        {
            error = null;
            npxPath = null;
            foreach (var dir in GetPathCandidates())
            {
                var candidate = Path.Combine(dir, "npx");
                if (File.Exists(candidate))
                {
                    npxPath = candidate;
                    return true;
                }
            }

            if (TryWhich("npx", out npxPath))
                return true;

            error = "npx not found. Run npm install in Tools/cave-grader or set node path in Cursor Settings.";
            return false;
        }

        static bool TryWhich(string command, out string fullPath)
        {
            fullPath = null;
            try
            {
                var shell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
                var psi = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"-lc \"which {command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return false;

                fullPath = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                return proc.ExitCode == 0 && !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath);
            }
            catch
            {
                return false;
            }
        }
    }
}
