using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Writes CaveBuildResearch.json from the shared prestige-lab catalog seed.</summary>
    public static class CaveBuildResearchExporter
    {
        public const string ResearchPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildResearch.json";

        const string SeedRelativePath =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/research-catalog.seed.json";

        public static void Write(
            CaveBuildQualityReport report,
            string activeRung,
            int meatLoopPass)
        {
            EnsureFolder();
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var seedPath = Path.Combine(hub, SeedRelativePath);
            var json = BuildMergedManifest(
                hub,
                seedPath,
                report?.SceneName ?? string.Empty,
                activeRung ?? "visual_shell",
                meatLoopPass);
            File.WriteAllText(Path.Combine(hub, ResearchPath), json);
        }

        /// <summary>Pre-build workflow research manifest (no quality report yet).</summary>
        public static void WriteMinimal(string hub, CaveBuildPreBuildReport preReport)
        {
            EnsureFolder();
            hub ??= CaveBuildCursorSettings.ResolveHubRoot();
            var activeRung = CaveBuildPreBuildLadder.PickActiveRung(preReport);
            var seedPath = Path.Combine(hub, SeedRelativePath);
            var json = BuildMergedManifest(
                hub,
                seedPath,
                preReport?.SceneName ?? string.Empty,
                activeRung,
                meatLoopPass: 0);
            File.WriteAllText(Path.Combine(hub, ResearchPath), json);
        }

        public static string[] AllUrlsFromSeed()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var seedPath = Path.Combine(hub, SeedRelativePath);
            if (!File.Exists(seedPath))
                return Array.Empty<string>();

            var text = File.ReadAllText(seedPath);
            var matches = Regex.Matches(text, "https?://[^\"\\s]+");
            var set = new HashSet<string>();
            foreach (Match m in matches)
                set.Add(m.Value);
            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        static string BuildMergedManifest(
            string hub,
            string seedPath,
            string scene,
            string activeRung,
            int meatLoopPass)
        {
            if (!File.Exists(seedPath))
            {
                UnityEngine.Debug.LogWarning(
                    $"[CaveBuild] Missing {SeedRelativePath}. Run: cd Tools/cave-grader && npx tsx export-research-catalog.ts");
                return MinimalFallback(scene, activeRung, meatLoopPass);
            }

            var inner = File.ReadAllText(seedPath).Trim();
            if (inner.StartsWith("{"))
                inner = inner.Substring(1);
            if (inner.EndsWith("}"))
                inner = inner.Substring(0, inner.Length - 1).Trim();

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"scene\": \"{Escape(scene)}\",");
            sb.AppendLine($"  \"meatLoopPass\": {meatLoopPass},");
            sb.AppendLine($"  \"activeRung\": \"{Escape(activeRung)}\",");
            sb.AppendLine("  \"promptBudget\": {");
            sb.AppendLine("    \"maxPapersInPrompt\": 5,");
            sb.AppendLine("    \"maxLabIndicesInPrompt\": 3,");
            sb.AppendLine("    \"maxSearchQueries\": 6,");
            sb.AppendLine("    \"note\": \"Read ResearchCache local files first; web only on cache miss.\"");
            sb.AppendLine("  },");
            sb.AppendLine("  \"researchCache\": {");
            sb.AppendLine($"    \"indexPath\": \"{CaveBuildResearchCacheBridge.CacheIndexPath}\",");
            sb.AppendLine($"    \"generatedPointer\": \"{CaveBuildResearchCacheBridge.GeneratedPointerPath}\",");
            sb.AppendLine(
                "    \"layout\": \"entries/{id}/content.md, categories/{category}/index.json, images/{id}/manifest.json, images/fl-{county}-hillshade/hillshade.png\"");
            sb.AppendLine("  },");
            AppendFloridaTerrainFromPointer(hub, sb);
            sb.AppendLine(
                "  \"dataAttribution\": \"Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md\",");
            sb.AppendLine(inner);
            sb.AppendLine("}");
            return sb.ToString();
        }

        static string MinimalFallback(string scene, string activeRung, int meatLoopPass)
        {
            return "{\n  \"error\": \"Run npx tsx export-research-catalog.ts in Tools/cave-grader\",\n" +
                   $"  \"scene\": \"{Escape(scene)}\",\n" +
                   $"  \"activeRung\": \"{Escape(activeRung)}\",\n" +
                   $"  \"meatLoopPass\": {meatLoopPass}\n}}";
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder(CaveBuildAgentContextExporter.Folder))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
        }

        static void AppendFloridaTerrainFromPointer(string hub, StringBuilder sb)
        {
            var pointerPath = Path.Combine(hub, CaveBuildResearchCacheBridge.GeneratedPointerPath);
            if (!File.Exists(pointerPath))
                return;

            try
            {
                var json = ExtractJsonObjectProperty(File.ReadAllText(pointerPath), "floridaTerrain");
                if (string.IsNullOrEmpty(json))
                    return;
                sb.AppendLine($"  \"floridaTerrain\": {json},");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    "[CaveBuild] Could not merge floridaTerrain from research cache pointer: " + ex.Message);
            }
        }

        /// <summary>Brace-balanced object value for a top-level property (Unity-safe; no System.Text.Json).</summary>
        static string ExtractJsonObjectProperty(string json, string propertyName)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var key = "\"" + propertyName + "\"";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return null;

            idx = json.IndexOf('{', idx + key.Length);
            if (idx < 0)
                return null;

            var depth = 0;
            var inString = false;
            var escape = false;
            for (var i = idx; i < json.Length; i++)
            {
                var c = json[i];
                if (inString)
                {
                    if (escape)
                        escape = false;
                    else if (c == '\\')
                        escape = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return json.Substring(idx, i - idx + 1);
                }
            }

            return null;
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Syncs categorized research cache (serialized URLs + image manifests) under Assets/EnvironmentKit/ResearchCache.
    /// </summary>
    public static class CaveBuildResearchCacheBridge
    {
        public const string CacheIndexPath = "Assets/EnvironmentKit/ResearchCache/index.json";
        public const string GeneratedPointerPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchCache.json";

        public const string ExecutionBriefPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchExecutionBrief.json";

        public const string TerrainExecutionBriefPath =
            CaveBuildAgentContextExporter.Folder + "/TerrainResearchExecutionBrief.json";

        public const string CaveExecutionBriefPath =
            CaveBuildAgentContextExporter.Folder + "/CaveResearchExecutionBrief.json";

        const string CatalogSeedRelativePath =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/research-catalog.seed.json";

        /// <summary>Env CAVE_FORCE_RESEARCH_SYNC=1 forces network/tsx research steps during validate.</summary>
        public static bool ForceNetworkResearchSync()
        {
            var force = string.Equals(
                System.Environment.GetEnvironmentVariable("CAVE_FORCE_RESEARCH_SYNC"),
                "1",
                StringComparison.Ordinal);
            return force || EditorPrefs.GetBool("CaveBuild_ForceResearchSync", false);
        }

        /// <summary>Skip blocking research-cache-sync.ts when ResearchCache/index.json is already usable.</summary>
        public static bool ShouldUseOnDiskResearchFastPath()
        {
            if (ForceNetworkResearchSync())
                return false;

            if (!HasUsableLocalResearchCache())
                return false;

            if (CaveBuildAutomatedFullWorldBootstrap.SessionActive ||
                CaveBuildStartupCoordinator.IsActive)
                return true;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            return settings.skipResearchNetworkSyncWhenCachePresent;
        }

        public static bool ShouldSkipCacheTsxSync() => ShouldUseOnDiskResearchFastPath();

        public static bool ShouldSkipHillshadeTsxSync()
        {
            if (ForceNetworkResearchSync())
                return false;
            return HasLocalFloridaHillshades() && ShouldUseOnDiskResearchFastPath();
        }

        public static bool ShouldSkipCatalogTsxSync()
        {
            if (ForceNetworkResearchSync())
                return false;
            if (!ShouldUseOnDiskResearchFastPath())
                return false;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return File.Exists(Path.Combine(hub, CatalogSeedRelativePath));
        }

        public static bool ShouldSkipBriefTsxSync()
        {
            if (ForceNetworkResearchSync())
                return false;
            if (!ShouldUseOnDiskResearchFastPath())
                return false;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return File.Exists(Path.Combine(hub, ExecutionBriefPath));
        }

        /// <param name="pullImages">When true, reuse valid cached PNGs and HTTP-fetch missing/invalid previews.</param>
        /// <param name="forceAllImages">When true, re-download every preview image.</param>
        public static bool SyncCache(
            string activeRung,
            bool pullImages,
            out string message) =>
            SyncCache(activeRung, pullImages, forceAllImages: false, out message);

        /// <summary>
        /// Sync ResearchCache metadata + reuse on-disk images + fetch missing previews (HTTP).
        /// </summary>
        public static bool SyncCache(
            string activeRung,
            bool pullImages,
            bool forceAllImages,
            out string message)
        {
            message = null;
            if (ShouldSkipCacheTsxSync())
            {
                message =
                    "Skipped research-cache-sync.ts — usable ResearchCache on disk " +
                    "(CAVE_FORCE_RESEARCH_SYNC=1 to force).";
                UnityEngine.Debug.Log("[CaveBuild] " + message);
                return true;
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, "research-cache-sync.ts");
            if (!File.Exists(script))
            {
                message = "Missing research-cache-sync.ts — run npm install in Tools/cave-grader.";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx in Tools/cave-grader — run npm install.";
                return false;
            }

            var args = $"\"{tsxCli}\" \"{script}\"";
            if (!string.IsNullOrWhiteSpace(activeRung))
                args += $" --rung={activeRung.Trim()}";
            if (forceAllImages)
                args += " --fetch-images";
            else if (!pullImages)
                args += " --no-fetch-images";

            var waitMs = pullImages || forceAllImages ? 300_000 : 90_000;
            return RunTsxProcess(
                hub,
                toolsDir,
                node,
                args,
                waitMs: waitMs,
                successLabel: "Research cache synced",
                out message);
        }

        /// <summary>
        /// Mandatory research pull: cache entries + missing images + FL hillshades + catalog seed.
        /// Reuses all valid files already under ResearchCache/.
        /// </summary>
        public static bool SyncFullResearchPull(string activeRung, out string message)
        {
            message = null;
            if (TryFastPathResearchPull(activeRung, out message))
                return true;

            var warnings = new System.Collections.Generic.List<string>();

            if (!SyncCache(activeRung, pullImages: true, forceAllImages: false, out var cacheMsg))
            {
                if (HasUsableLocalResearchCache())
                {
                    cacheMsg = "Research cache sync failed — reusing " + CacheIndexPath + ".";
                    warnings.Add(cacheMsg);
                }
                else
                {
                    message = cacheMsg;
                    return false;
                }
            }

            if (!SyncFloridaHillshades(out var hillMsg))
            {
                if (HasLocalFloridaHillshades())
                {
                    hillMsg = "Florida hillshade sync failed — reusing on-disk hillshade PNG(s).";
                    warnings.Add(hillMsg);
                }
                else
                {
                    message = cacheMsg + " | hillshades failed: " + hillMsg;
                    return false;
                }
            }

            if (!SyncResearchCatalog(out var catMsg))
            {
                if (HasUsableLocalResearchCache())
                {
                    catMsg = "Research catalog export failed — index.json still usable.";
                    warnings.Add(catMsg);
                }
                else
                {
                    message = cacheMsg + " | " + hillMsg + " | catalog failed: " + catMsg;
                    return false;
                }
            }

            if (!SyncResearchExecutionBrief(activeRung, out var briefMsg))
                UnityEngine.Debug.LogWarning("[CaveBuild] Research execution brief: " + briefMsg);
            SyncTerrainResearchExecutionBrief("terrain_integration", -1, out _);
            SyncCaveResearchExecutionBrief("terrain_integration", -1, out _);
            CaveBuildPhasePromptBridge.ExportUnifiedAgentContext("research", out _);
            SyncCaveResearchExecutionBrief("research", -1, out _);

            message = cacheMsg + " | " + hillMsg + " | " + catMsg;
            if (warnings.Count > 0)
                message += " | " + string.Join(" | ", warnings);
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            return true;
        }

        /// <summary>
        /// Non-blocking path when ResearchCache + hillshades + catalog seed already exist (validate / phase gates).
        /// </summary>
        public static bool TryFastPathResearchPull(string activeRung, out string message)
        {
            message = null;
            if (!ShouldUseOnDiskResearchFastPath())
                return false;

            var parts = new System.Collections.Generic.List<string>
            {
                "Fast research pull — skipped blocking cache/hillshade/catalog tsx.",
            };

            // During active builds, never attempt the legacy sync brief export path — it uses a blocking runner.
            // The brief is optional once cache/catalog are on disk; missing brief should not stall placement.
            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                parts.Add("execution brief deferred — active build (non-blocking only).");
            }
            else if (!ShouldSkipBriefTsxSync())
            {
                if (!SyncResearchExecutionBrief(activeRung, out var briefMsg))
                    UnityEngine.Debug.LogWarning("[CaveBuild] Research execution brief: " + briefMsg);
                parts.Add(briefMsg);
            }
            else
            {
                parts.Add("execution brief on disk — skipped tsx.");
            }

            message = string.Join(" | ", parts);
            UnityEngine.Debug.Log("[CaveBuild] " + message);
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            return true;
        }

        /// <summary>True when ResearchCache/index.json exists and looks populated (offline build OK).</summary>
        public static bool HasUsableLocalResearchCache()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var indexPath = Path.Combine(hub, CacheIndexPath);
            if (!File.Exists(indexPath))
                return false;
            try
            {
                var text = File.ReadAllText(indexPath);
                return text.Length > 400 &&
                       text.Contains("\"categories\"", System.StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when at least one Florida county hillshade PNG is already on disk.</summary>
        public static bool HasLocalFloridaHillshades()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var indexPath = Path.Combine(
                hub,
                "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json");
            if (File.Exists(indexPath))
                return true;

            var imagesDir = Path.Combine(hub, "Assets/EnvironmentKit/ResearchCache/images");
            if (!Directory.Exists(imagesDir))
                return false;

            foreach (var dir in Directory.GetDirectories(imagesDir, "fl-*-hillshade"))
            {
                if (File.Exists(Path.Combine(dir, "hillshade.png")))
                    return true;
            }

            return false;
        }

        public static bool SyncResearchExecutionBrief(string activeRung, out string message) =>
            SyncResearchExecutionBrief(activeRung, meatPass: -1, out message);

        public static bool SyncResearchExecutionBrief(string activeRung, int meatPass, out string message) =>
            SyncResearchExecutionBriefScoped(activeRung, meatPass, "combined", out message);

        public static bool SyncTerrainResearchExecutionBrief(string activeRung, int meatPass, out string message) =>
            SyncResearchExecutionBriefScoped(activeRung, meatPass, "terrain", out message);

        public static bool SyncCaveResearchExecutionBrief(string activeRung, int meatPass, out string message) =>
            SyncResearchExecutionBriefScoped(activeRung, meatPass, "cave", out message);

        static bool SyncResearchExecutionBriefScoped(
            string activeRung,
            int meatPass,
            string scope,
            out string message)
        {
            message = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, "export-research-execution-brief.ts");
            if (!File.Exists(script))
            {
                message = "Missing export-research-execution-brief.ts";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx";
                return false;
            }

            var args = $"\"{tsxCli}\" \"{script}\" --scope={scope}";
            if (!string.IsNullOrWhiteSpace(activeRung))
                args += $" --rung={activeRung.Trim()}";
            if (meatPass >= 0)
            {
                System.Environment.SetEnvironmentVariable("CAVE_MEAT_PASS", meatPass.ToString());
                args += $" --meat-pass={meatPass}";
            }

            return RunTsxProcess(
                hub,
                toolsDir,
                node,
                args,
                waitMs: 60_000,
                successLabel: $"Research execution brief ({scope})",
                out message);
        }

        public static bool SyncResearchCatalog(out string message)
        {
            message = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, "export-research-catalog.ts");
            if (!File.Exists(script))
            {
                message = "Missing export-research-catalog.ts";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx";
                return false;
            }

            var args = $"\"{tsxCli}\" \"{script}\"";
            return RunTsxProcess(hub, toolsDir, node, args, 90_000, "Research catalog seed", out message);
        }

        public static bool TryRunVerifyPackageTooling(out string message)
        {
            message = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, "verify-package-tooling.ts");
            if (!File.Exists(script))
            {
                message = "Missing verify-package-tooling.ts";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx";
                return false;
            }

            var args = $"\"{tsxCli}\" \"{script}\"";
            return RunTsxProcess(hub, toolsDir, node, args, 30_000, "verify package tooling", out message);
        }

        static bool RunTsxProcess(
            string hub,
            string toolsDir,
            string node,
            string args,
            int waitMs,
            string successLabel,
            out string message)
        {
            CaveBuildRunStatusPublisher.SetSubOperation(successLabel, "starting node/tsx…");
            var ok = CaveBuildTsxProcessRunner.Run(
                new CaveBuildTsxProcessRunner.Options
                {
                    HubRoot = hub,
                    ToolsDir = toolsDir,
                    NodePath = node,
                    Arguments = args,
                    WaitMs = waitMs,
                    SuccessLabel = successLabel,
                    LiveOperationLabel = successLabel,
                },
                out message);
            if (ok)
                CaveBuildDeferredAssetRefresh.RequestRefresh();
            return ok;
        }

        /// <summary>Non-blocking tsx for validate/startup — required so Unity can repaint between HTTP fetches.</summary>
        public static bool TryBeginRunTsxProcess(
            string hub,
            string toolsDir,
            string node,
            string args,
            int waitMs,
            string successLabel,
            Action<bool, string> onComplete)
        {
            if (onComplete == null)
                return false;

            CaveBuildRunStatusPublisher.SetSubOperation(successLabel, "starting node/tsx…");
            CaveBuildTsxProcessRunner.BeginRun(
                new CaveBuildTsxProcessRunner.Options
                {
                    HubRoot = hub,
                    ToolsDir = toolsDir,
                    NodePath = node,
                    Arguments = args,
                    WaitMs = waitMs,
                    SuccessLabel = successLabel,
                    LiveOperationLabel = successLabel,
                },
                (ok, msg) =>
                {
                    if (ok)
                        CaveBuildDeferredAssetRefresh.RequestRefresh();
                    onComplete(ok, msg);
                });
            return true;
        }

        public static bool CacheExists()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return File.Exists(Path.Combine(hub, CacheIndexPath));
        }

        /// <summary>
        /// Renders county hillshade PNGs into ResearchCache (USGS public elevation). Set env CAVE_SYNC_FL_HILLSHADES=1.
        /// </summary>
        public static bool SyncFloridaHillshades(out string message)
        {
            message = null;
            var forceSync = string.Equals(
                System.Environment.GetEnvironmentVariable("CAVE_SYNC_FL_HILLSHADES"),
                "1",
                System.StringComparison.Ordinal);
            if (!forceSync && HasLocalFloridaHillshades())
            {
                message =
                    "Using on-disk Florida hillshades (set CAVE_SYNC_FL_HILLSHADES=1 to re-download).";
                return true;
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var toolsDir = Path.Combine(hub, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var script = Path.Combine(toolsDir, "florida-lidar-hillshade.ts");
            if (!File.Exists(script))
            {
                message = "Missing florida-lidar-hillshade.ts";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out message))
                return false;

            var tsxCli = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            if (!File.Exists(tsxCli))
            {
                message = "Missing tsx in Tools/cave-grader — run npm install.";
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = node,
                Arguments =
                    $"\"{tsxCli}\" \"{script}\" --elev-grid=128 --pixels=1024",
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["HUB_ROOT"] = hub;

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                message = "Failed to start florida-lidar-hillshade.";
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(600_000);

            if (!string.IsNullOrWhiteSpace(stdout))
                UnityEngine.Debug.Log(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
                UnityEngine.Debug.LogWarning(stderr.TrimEnd());

            if (proc.ExitCode != 0)
            {
                message = $"florida-lidar-hillshade exited {proc.ExitCode}.";
                return false;
            }

            message = "Florida county hillshades synced.";
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            return true;
        }
    }
}
