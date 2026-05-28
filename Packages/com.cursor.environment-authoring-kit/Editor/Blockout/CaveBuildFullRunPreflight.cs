#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Pre-flight checks before a FullWorld 120-step pipeline run.</summary>
    public static class CaveBuildFullRunPreflight
    {
        public const string ReportRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildPreflightReport.md";

        public enum Severity
        {
            Pass,
            Warn,
            Block,
        }

        public struct CheckResult
        {
            public string id;
            public string label;
            public Severity severity;
            public string detail;
        }

        public struct Report
        {
            public CheckResult[] checks;
            public bool CanStartFullWorld;
            public int blockCount;
            public int warnCount;
        }

        public static Report Run(
            SceneGroundInfo ground,
            WorldGenerationRequest request = null,
            bool writeMarkdown = true)
        {
            var list = new List<CheckResult>();
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? hub;
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            Add(list, "ground_anchor", "Ground tag / anchor", ground.HasAnchor
                ? Severity.Pass
                : Severity.Block, ground.HasAnchor
                ? $"Anchor: {ground.Anchor?.name ?? "ok"}"
                : "Tag walkable floor as Ground or assign on Environment Root.");

            var catalog = LavaTubePrefabCatalog.Load(forceRefresh: true);
            Add(list, "prefab_catalog", "Environment module prefab catalog", catalog.IsValid ? Severity.Pass : Severity.Block,
                catalog.IsValid
                    ? $"Auto-discovered {catalog.Floors.Count} floor, {catalog.Walls.Count} wall, {catalog.Ceilings.Count} ceiling prefab(s) under Assets/."
                    : "No 3D floor+wall+ceiling mesh prefabs found in Assets/. Import a modular cave/dungeon pack (mesh prefabs, not texture-only or 2D sprites).");

            Add(list, "phased_build", "Phased cave build enabled",
                settings.usePhasedCaveBuild ? Severity.Pass : Severity.Block,
                settings.usePhasedCaveBuild ? "usePhasedCaveBuild=true" : "Enable usePhasedCaveBuild in Cursor Settings.");

            Add(list, "build_not_active", "No build already running",
                !LavaTubeCaveBuilder.IsBuildInProgress && !LavaTubeCaveBuildPipeline.IsPhasedBuildActive
                    ? Severity.Pass
                    : Severity.Block,
                LavaTubeCaveBuilder.IsBuildInProgress ? "Build in progress — wait or Emergency Unfreeze." : "OK");

            Add(list, "research_cache", "ResearchCache index",
                CaveBuildResearchCacheBridge.HasUsableLocalResearchCache() ? Severity.Pass : Severity.Warn,
                CaveBuildResearchCacheBridge.HasUsableLocalResearchCache()
                    ? CaveBuildResearchCacheBridge.CacheIndexPath
                    : "Run sync-research-pull or let validate sync (slower first run).");

            Add(list, "florida_hillshades", "Florida county hillshades",
                CaveBuildResearchCacheBridge.HasLocalFloridaHillshades() ? Severity.Pass : Severity.Warn,
                CaveBuildResearchCacheBridge.HasLocalFloridaHillshades()
                    ? "At least one fl-*-hillshade on disk."
                    : "npm run sync-florida-hillshades in Tools/cave-grader recommended.");

            var nodeOk = CaveBuildCursorProcessResolver.TryResolveNode(out _, out var nodeMsg);
            Add(list, "node_tsx", "Node + tsx (cave-grader)",
                nodeOk ? Severity.Pass : Severity.Block, nodeOk ? "Node resolved." : nodeMsg ?? "Missing node");

            var toolsDir = Path.Combine(projectRoot, CaveBuildCursorAgentBridge.ToolsRelativePath);
            var tsx = Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs");
            Add(list, "tsx_installed", "tsx in Tools/cave-grader",
                File.Exists(tsx) ? Severity.Pass : Severity.Block,
                File.Exists(tsx)
                    ? tsx
                    : $"Run: cd \"{toolsDir}\" && npm install");

            Add(list, "incremental_ladder", "Incremental ladder",
                !settings.useIncrementalLadder ? Severity.Pass : Severity.Warn,
                settings.useIncrementalLadder
                    ? "OFF recommended for first full pass (can skip validate/adventure)."
                    : "Incremental off — good for first full completion.");

            Add(list, "auto_invoke", "Auto-invoke Cursor",
                !settings.autoInvokeAfterEveryBuild &&
                !settings.autoInvokeEachMeatLoopPass &&
                !settings.autoInvokePreBuildWorkflow
                    ? Severity.Pass
                    : Severity.Warn,
                "OFF recommended so the 120-step queue runs unattended.");

            var seed = request?.Seed ?? EditorPrefs.GetInt("CaveBuild_LastSeed", 12345);
            var ladderComplete = request != null &&
                                 CaveBuildPhaseContractRegistry.IsRungComplete(
                                     CaveBuildPhaseContractRegistry.RungCaveLayout,
                                     seed);
            Add(list, "ladder_cave_layout", $"Ladder cave_layout for seed {seed}",
                ladderComplete ? Severity.Pass : Severity.Warn,
                ladderComplete
                    ? "Prior completion exists — incremental ON is OK after one full pass."
                    : "No prior cave_layout — use Full AAA Rebuild + incremental OFF.");

            Add(list, "research_agent_prompt", "Consolidated research prompt file",
                File.Exists(Path.Combine(hub, CaveBuildPhasePromptBridge.ResearchAgentPromptPath))
                    ? Severity.Pass
                    : Severity.Warn,
                CaveBuildPhasePromptBridge.ResearchAgentPromptPath);

            Add(list, "enhancement_phases", "Enhancement phases catalog",
                settings.enableEnhancementPhases ? Severity.Pass : Severity.Warn,
                settings.enableEnhancementPhases
                    ? $"{CaveBuildEnhancementCatalog.PhaseCount} phases (speed/quality/creative)."
                    : "Enable for supersampling + creative passes.");

            var blocks = 0;
            var warns = 0;
            foreach (var c in list)
            {
                if (c.severity == Severity.Block)
                    blocks++;
                if (c.severity == Severity.Warn)
                    warns++;
            }

            var report = new Report
            {
                checks = list.ToArray(),
                CanStartFullWorld = blocks == 0,
                blockCount = blocks,
                warnCount = warns,
            };

            if (writeMarkdown)
                WriteMarkdown(hub, report, seed);

            CaveBuildEnhancementRunner.ExportCatalogJson();
            return report;
        }

        static void Add(List<CheckResult> list, string id, string label, Severity sev, string detail) =>
            list.Add(new CheckResult { id = id, label = label, severity = sev, detail = detail });

        static void WriteMarkdown(string hub, Report report, int seed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Full run preflight checklist");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Seed:** {seed}");
            sb.AppendLine($"**Can start FullWorld:** {(report.CanStartFullWorld ? "YES" : "NO")} ({report.blockCount} blockers, {report.warnCount} warnings)");
            sb.AppendLine();
            sb.AppendLine("## Recommended before Build Complete Cave (FullWorld)");
            sb.AppendLine("1. **Cave Build → Diagnostics → Apply Reliable FullWorld Preset**");
            sb.AppendLine("2. **Advanced → Build Complete Cave — Full AAA Rebuild (invalidate ladder)**");
            sb.AppendLine("3. **Diagnostics → Pipeline Console** — watch until **Build 120/120**");
            sb.AppendLine("4. Confirm log: **Cave shell ready — starting Florida LiDAR**");
            sb.AppendLine("5. Read **CaveBuildCompletionReadout.md** + **CaveBuildCompletionContract.json**");
            sb.AppendLine();
            sb.AppendLine("## Checks");
            sb.AppendLine("| Status | Check | Detail |");
            sb.AppendLine("|--------|-------|--------|");
            foreach (var c in report.checks)
            {
                var icon = c.severity switch
                {
                    Severity.Pass => "PASS",
                    Severity.Warn => "WARN",
                    _ => "BLOCK",
                };
                sb.AppendLine($"| {icon} | {c.label} | {c.detail.Replace("|", "/")} |");
            }

            sb.AppendLine();
            sb.AppendLine($"Enhancement catalog: `{CaveBuildEnhancementCatalog.PhaseCount}` phases — see `CaveBuildEnhancementPhases.json`.");
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[CaveBuild] Preflight report → {path} (blocks={report.blockCount}, warns={report.warnCount})");
        }
    }
}
#endif
