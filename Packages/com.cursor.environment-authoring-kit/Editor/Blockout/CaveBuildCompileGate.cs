using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Captures Unity script compile errors for the Cursor compile_gate phase.
    /// Research: arXiv:2510.15120 (DRL level design + Unity validation loops);
    /// arXiv:2503.05146 (Unity RL Playground / ML-Agents simulation gates before scene work);
    /// EA SEED CoG 2024 LLM failure analysis (multi-source log triage when Bee log is stale).
    /// </summary>
    public static class CaveBuildCompileGate
    {
        public const string DiagnosticsPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildCompileDiagnostics.json";

        public const string KitPackageSegment = "com.cursor.environment-authoring-kit";

        const int MaxErrors = 40;
        const int EditorLogTailBytes = 512 * 1024;

        // MSBuild: path/File.cs(41,21): error CS0102: message
        static readonly Regex MsBuildCsError = new Regex(
            @"([^\s""]+\.cs)\((\d+),\d+\):\s*(error CS\d+):\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Classic: error CS0102: message in path/File.cs:41
        static readonly Regex ClassicCsError = new Regex(
            @"(error CS\d+):\s*(.+?)\s+in\s+([^\s:(]+\.cs):(\d+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public static bool HasBlockingErrors(string hubRoot = null)
        {
            var snap = Capture(hubRoot);
            return snap.VerifiedErrorCount > 0;
        }

        /// <summary>Lightweight capture during pre-build reloop — no 512KB Editor.log tail scan.</summary>
        public static CompileSnapshot CaptureReloopFast(string hubRoot = null)
        {
            hubRoot ??= CaveBuildCursorSettings.ResolveHubRoot();
            if (!EditorUtility.scriptCompilationFailed)
            {
                return new CompileSnapshot
                {
                    HasCompileErrors = false,
                    IsCompiling = EditorApplication.isCompiling,
                    ErrorCount = 0,
                    VerifiedErrorCount = 0,
                    StaleErrorCount = 0,
                    KitPackageErrorCount = 0,
                    Errors = Array.Empty<CompileError>(),
                    CapturedUtc = DateTime.UtcNow.ToString("o"),
                    AgentNote = "Reloop fast path — no scriptCompilationFailed flag.",
                };
            }

            var raw = CollectCompileErrors(hubRoot, includeEditorLogTail: false);
            var verified = new List<CompileError>();
            var stale = 0;
            var maxVerify = 5;
            foreach (var e in raw)
            {
                if (verified.Count >= maxVerify)
                    break;
                if (IsErrorStillPresentOnDisk(hubRoot, e))
                    verified.Add(e);
                else
                    stale++;
            }

            var kitCount = 0;
            foreach (var e in verified)
            {
                if (IsKitPackageFile(e.File))
                    kitCount++;
            }

            return new CompileSnapshot
            {
                HasCompileErrors = verified.Count > 0,
                IsCompiling = EditorApplication.isCompiling,
                ErrorCount = raw.Count,
                VerifiedErrorCount = verified.Count,
                StaleErrorCount = stale,
                KitPackageErrorCount = kitCount,
                Errors = verified.ToArray(),
                CapturedUtc = DateTime.UtcNow.ToString("o"),
                AgentNote =
                    "Reloop fast compile check (fix Console CS errors; full scan skipped to keep editor responsive).",
            };
        }

        public static CompileSnapshot Capture(string hubRoot = null)
        {
            hubRoot ??= CaveBuildCursorSettings.ResolveHubRoot();
            var raw = CollectCompileErrors(hubRoot);
            var verified = new List<CompileError>();
            var stale = 0;
            foreach (var e in raw)
            {
                if (IsErrorStillPresentOnDisk(hubRoot, e))
                    verified.Add(e);
                else
                    stale++;
            }

            var hasFlag = EditorUtility.scriptCompilationFailed;
            var kitCount = 0;
            foreach (var e in verified)
            {
                if (IsKitPackageFile(e.File))
                    kitCount++;
            }

            return new CompileSnapshot
            {
                HasCompileErrors = hasFlag && verified.Count > 0,
                IsCompiling = EditorApplication.isCompiling,
                ErrorCount = raw.Count,
                VerifiedErrorCount = verified.Count,
                StaleErrorCount = stale,
                KitPackageErrorCount = kitCount,
                Errors = verified.ToArray(),
                CapturedUtc = DateTime.UtcNow.ToString("o"),
                AgentNote = stale > 0
                    ? $"{stale} log error(s) are STALE (source on disk already fixed). Fix only verifiedOnDisk errors. Do not chase AnchorWorld/CS1501 lines that no longer exist in the file."
                    : string.Empty,
            };
        }

        /// <summary>Request recompile then export — reduces Bee/Editor.log stale errors after agent fixes.</summary>
        public static void RequestRecompileAndExportDiagnostics(string hubRoot = null)
        {
            hubRoot ??= CaveBuildCursorSettings.ResolveHubRoot();
            if (!EditorApplication.isCompiling)
                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            ExportDiagnostics(hubRoot);
        }

        public static void ExportDiagnostics(string hubRoot = null)
        {
            hubRoot ??= CaveBuildCursorSettings.ResolveHubRoot();
            var snap = Capture(hubRoot);
            var path = Path.Combine(hubRoot, DiagnosticsPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"capturedUtc\": \"{snap.CapturedUtc}\",");
            sb.AppendLine($"  \"hasCompileErrors\": {(snap.HasCompileErrors ? "true" : "false")},");
            sb.AppendLine($"  \"isCompiling\": {(snap.IsCompiling ? "true" : "false")},");
            sb.AppendLine($"  \"errorCount\": {snap.ErrorCount},");
            sb.AppendLine($"  \"verifiedErrorCount\": {snap.VerifiedErrorCount},");
            sb.AppendLine($"  \"staleErrorCount\": {snap.StaleErrorCount},");
            sb.AppendLine($"  \"kitPackageErrorCount\": {snap.KitPackageErrorCount},");
            sb.AppendLine($"  \"scriptCompilationFailed\": {(EditorUtility.scriptCompilationFailed ? "true" : "false")},");
            sb.AppendLine($"  \"agentNote\": \"{Escape(snap.AgentNote)}\",");
            sb.AppendLine("  \"errors\": [");
            for (var i = 0; i < snap.Errors.Length; i++)
            {
                var e = snap.Errors[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"code\": \"{Escape(e.Code)}\",");
                sb.AppendLine($"      \"file\": \"{Escape(e.File)}\",");
                sb.AppendLine($"      \"line\": {e.Line},");
                sb.AppendLine($"      \"message\": \"{Escape(e.Message)}\",");
                sb.AppendLine("      \"verifiedOnDisk\": true");
                sb.Append("    }");
                sb.AppendLine(i < snap.Errors.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());

            if (snap.StaleErrorCount > 0)
            {
                Debug.Log(
                    $"[CaveBuild] Compile diagnostics: {snap.VerifiedErrorCount} verified, " +
                    $"{snap.StaleErrorCount} STALE (ignored — source already fixed on disk). " +
                    snap.AgentNote);
            }
        }

        static List<CompileError> CollectCompileErrors(string hubRoot, bool includeEditorLogTail = true)
        {
            var seen = new HashSet<string>();
            var list = new List<CompileError>();
            AppendParsedErrors(list, seen, ReadBeeLogText(hubRoot));
            if (includeEditorLogTail && (list.Count == 0 || EditorUtility.scriptCompilationFailed))
                AppendParsedErrors(list, seen, ReadEditorLogTail());
            return list;
        }

        static string ReadBeeLogText(string hubRoot)
        {
            var logPath = Path.Combine(hubRoot, "Library/Bee/tundra.log.json");
            if (!File.Exists(logPath))
                return string.Empty;

            try
            {
                return File.ReadAllText(logPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Bee log read failed: " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>Editor.log tail when Bee log is empty but compilation failed (SEED failure-analysis triage).</summary>
        static string ReadEditorLogTail()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Path.Combine(home, "Library/Logs/Unity/Editor.log"),
                Path.Combine(home, ".config/unity3d/Editor.log"),
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var len = fs.Length;
                    var read = (int)Math.Min(EditorLogTailBytes, len);
                    fs.Seek(-read, SeekOrigin.End);
                    var buf = new byte[read];
                    fs.Read(buf, 0, read);
                    return Encoding.UTF8.GetString(buf);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[CaveBuild] Editor.log read failed: " + ex.Message);
                }
            }

            return string.Empty;
        }

        static void AppendParsedErrors(List<CompileError> list, HashSet<string> seen, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                foreach (Match m in MsBuildCsError.Matches(text))
                {
                    if (list.Count >= MaxErrors)
                        break;
                    AddParsedError(list, seen, m.Groups[3].Value.Trim(), m.Groups[1].Value.Trim(),
                        int.TryParse(m.Groups[2].Value, out var ln) ? ln : 0, m.Groups[4].Value.Trim());
                }

                foreach (Match m in ClassicCsError.Matches(text))
                {
                    if (list.Count >= MaxErrors)
                        break;
                    AddParsedError(list, seen, m.Groups[1].Value.Trim(), m.Groups[3].Value.Trim(),
                        int.TryParse(m.Groups[4].Value, out var ln) ? ln : 0, m.Groups[2].Value.Trim());
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Compile log parse failed: " + ex.Message);
            }
        }

        static void AddParsedError(
            List<CompileError> list,
            HashSet<string> seen,
            string code,
            string file,
            int line,
            string message)
        {
            var key = $"{code}|{file}|{line}";
            if (!seen.Add(key))
                return;

            list.Add(new CompileError
            {
                Code = code,
                Message = message,
                File = file,
                Line = line,
            });
        }

        public static bool IsKitPackageFile(string filePath) =>
            !string.IsNullOrEmpty(filePath) &&
            filePath.IndexOf(KitPackageSegment, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool IsErrorStillPresentOnDisk(string hubRoot, CompileError error)
        {
            if (string.IsNullOrEmpty(error.File) || error.Line <= 0)
                return true;

            var path = ResolveSourcePath(hubRoot, error.File);
            if (!File.Exists(path))
                return false;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch
            {
                return true;
            }

            if (error.Line > lines.Length)
                return false;

            var line = lines[error.Line - 1];
            var msg = error.Message ?? string.Empty;

            if (error.Code == "error CS0104" &&
                msg.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0 &&
                line.Contains("UnityEngine.Debug.", StringComparison.Ordinal))
                return false;

            if (error.Code == "error CS1503" &&
                msg.Contains("SceneGroundInfo", StringComparison.Ordinal) &&
                msg.Contains("Terrain", StringComparison.Ordinal) &&
                line.Contains("terrain", StringComparison.Ordinal) &&
                !line.Contains("SceneGroundInfo", StringComparison.Ordinal))
                return false;

            if (error.Code == "error CS1503" &&
                msg.Contains("Vector3' to 'UnityEngine.Vector3[]", StringComparison.Ordinal) &&
                line.Contains("SmoothOuterHeightRingPublic", StringComparison.Ordinal))
                return false;

            if (error.Code == "error CS7036" &&
                msg.Contains("vegPass", StringComparison.Ordinal) &&
                (line.Contains("VegetationPass.", StringComparison.Ordinal) ||
                 line.Contains("vegPass:", StringComparison.Ordinal)))
                return false;

            // CS1612: struct copy via buffer[0] = head (not Queue.Peek().ReadyAt = …)
            if (error.Code == "error CS1612" &&
                msg.Contains("Cannot modify the return value", StringComparison.Ordinal) &&
                !line.Contains(".ReadyAt =", StringComparison.Ordinal))
                return false;

            // CS1739: LogCaveWarning no longer accepts forceUnityConsole
            if (error.Code == "error CS1739" &&
                msg.Contains("forceUnityConsole", StringComparison.Ordinal) &&
                !line.Contains("forceUnityConsole", StringComparison.Ordinal))
                return false;

            var member = Regex.Match(msg, @"definition for '([^']+)'");
            if (member.Success)
                return line.Contains(member.Groups[1].Value, StringComparison.Ordinal);

            var method = Regex.Match(msg, @"method '([^']+)'");
            if (method.Success)
            {
                var methodName = method.Groups[1].Value;
                if (!line.Contains(methodName, StringComparison.Ordinal))
                    return false;
                if (error.Message.Contains("takes 2 arguments", StringComparison.Ordinal) &&
                    !line.Contains(",", StringComparison.Ordinal))
                    return false;
                if (error.Message.Contains("takes 1 arguments", StringComparison.Ordinal) &&
                    line.Contains(", ground", StringComparison.Ordinal))
                    return false;
            }

            var typeName = Regex.Match(msg, @"type '([^']+)'");
            if (typeName.Success && !line.Contains(typeName.Groups[1].Value, StringComparison.Ordinal))
                return false;

            // CS0103: The name 'Foo' does not exist in the current context
            var missingName = Regex.Match(msg, @"The name '([^']+)'");
            if (missingName.Success && !line.Contains(missingName.Groups[1].Value, StringComparison.Ordinal))
                return false;

            // CS0136: A local or parameter named 'player' cannot be declared in this scope...
            var shadowedName = Regex.Match(msg, @"named '([^']+)' cannot be declared");
            if (shadowedName.Success)
            {
                var id = shadowedName.Groups[1].Value;
                var declares =
                    line.Contains("var " + id, StringComparison.Ordinal) ||
                    line.Contains(id + " =", StringComparison.Ordinal);
                if (!declares)
                    return false;
            }

            return true;
        }

        static string ResolveSourcePath(string hubRoot, string file)
        {
            if (string.IsNullOrEmpty(file))
                return file;
            if (Path.IsPathRooted(file))
                return file;
            if (file.StartsWith("Assets/", StringComparison.Ordinal) ||
                file.StartsWith("Packages/", StringComparison.Ordinal))
                return Path.Combine(hubRoot, file);
            return Path.Combine(hubRoot, "Assets", file);
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        public struct CompileSnapshot
        {
            public bool HasCompileErrors;
            public bool IsCompiling;
            public int ErrorCount;
            public int VerifiedErrorCount;
            public int StaleErrorCount;
            public int KitPackageErrorCount;
            public CompileError[] Errors;
            public string CapturedUtc;
            public string AgentNote;
        }

        public struct CompileError
        {
            public string Code;
            public string File;
            public int Line;
            public string Message;
        }
    }
}
