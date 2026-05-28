using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Weighted readiness ladder run before cave geometry is generated.</summary>
    public static class CaveBuildPreBuildLadder
    {
        public const string ReportPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildPreBuildLadderReport.json";
        public const string ContextPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildPreBuildLadderContext.json";

        public const int TargetOverallScore = 88;
        public const int StagePassScore = 92;
        public const int StageFloorScore = 70;
        public const string TargetGrade = "B+";

        public static readonly PreBuildRungDef[] RungOrder =
        {
            new("compile_gate", 25, critical: true),
            new("package_tooling", 18, critical: true),
            new("scene_ground", 16, critical: true),
            new("prefab_catalog", 14, critical: true),
            new("ai_provider", 10, critical: false),
            new("research_manifest", 10, critical: false),
            new("scene_portal", 7, critical: false),
            new("prior_cave_state", 5, critical: false),
        };

        public struct PreBuildRungDef
        {
            public string Id;
            public int Weight;
            public bool Critical;

            public PreBuildRungDef(string id, int weight, bool critical)
            {
                Id = id;
                Weight = weight;
                Critical = critical;
            }
        }

        public static CaveBuildPreBuildReport Run(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool layoutPrototype,
            int layoutSeed)
        {
            var report = new CaveBuildPreBuildReport
            {
                SceneName = SceneManager.GetActiveScene().name,
                LayoutSeed = layoutSeed,
                LayoutPrototype = layoutPrototype,
                GradingMode = "pre_build_ladder",
            };

            foreach (var def in RungOrder)
            {
                var stage = GradeRung(def, ground, request, layoutPrototype, layoutSeed);
                report.Stages.Add(stage);
            }

            report.RecalculateOverall();
            CaveBuildPreBuildLadderWriter.Write(report);
            WriteLadderContext(report);
            return report;
        }

        public static bool AllRungsPassing(CaveBuildPreBuildReport report)
        {
            if (report?.Stages == null || report.Stages.Count == 0)
                return false;

            foreach (var s in report.Stages)
            {
                if (!s.Passed || s.Score < StagePassScore)
                    return false;
            }

            return true;
        }

        public static string PickActiveRung(CaveBuildPreBuildReport report, ISet<string> skip = null)
        {
            if (report == null)
                return null;

            if (AllRungsPassing(report))
                return null;

            PreBuildStageGrade worstCritical = null;
            PreBuildStageGrade worst = null;

            foreach (var s in report.Stages)
            {
                if (skip != null && skip.Contains(s.StageId))
                    continue;
                if (s.Passed && s.Score >= StagePassScore)
                    continue;

                if (s.Critical)
                {
                    if (worstCritical == null || s.Score < worstCritical.Score ||
                        (s.Score == worstCritical.Score && s.Weight > worstCritical.Weight))
                        worstCritical = s;
                }
                else if (worst == null || s.Score < worst.Score)
                {
                    worst = s;
                }
            }

            if (worstCritical != null)
                return worstCritical.StageId;
            if (worst != null)
                return worst.StageId;

            return null;
        }

        public static List<string> GetFailingRungs(CaveBuildPreBuildReport report)
        {
            var list = new List<string>();
            if (report == null)
                return list;

            foreach (var s in report.Stages)
            {
                if (!s.Passed || s.Score < StagePassScore)
                    list.Add(s.StageId);
            }

            return list;
        }

        /// <summary>True when a critical rung is below the hard floor (must fix before any build).</summary>
        public static bool HasCriticalFailure(CaveBuildPreBuildReport report)
        {
            if (report?.Stages == null)
                return true;

            foreach (var s in report.Stages)
            {
                if (s.Critical && s.Score < StageFloorScore)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Obsolete — automated FullWorld uses <see cref="CaveBuildPreBuildReloop"/> until BuildAcceptable (88+).
        /// </summary>
        [Obsolete("Replaced by CaveBuildPreBuildReloop — advisory bypass removed.")]
        public static bool ShouldAllowAutomatedFullWorldContinue(CaveBuildPreBuildReport report) => false;

        static PreBuildStageGrade GradeRung(
            PreBuildRungDef def,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool layoutPrototype,
            int layoutSeed)
        {
            var g = new PreBuildStageGrade
            {
                StageId = def.Id,
                StageName = PreBuildRungDisplayName(def.Id),
                Weight = def.Weight,
                Critical = def.Critical,
                Score = 100,
            };

            switch (def.Id)
            {
                case "compile_gate":
                    GradeCompileGate(g);
                    break;
                case "package_tooling":
                    GradePackageTooling(g);
                    break;
                case "scene_ground":
                    GradeSceneGround(g, ground);
                    break;
                case "prefab_catalog":
                    GradePrefabCatalog(g, layoutPrototype);
                    break;
                case "ai_provider":
                case "cursor_api":
                    GradeAiProvider(g);
                    break;
                case "research_manifest":
                    GradeResearchManifest(g);
                    break;
                case "scene_portal":
                    GradeScenePortal(g);
                    break;
                case "prior_cave_state":
                    GradePriorCaveState(g, ground);
                    break;
            }

            g.Passed = g.Score >= StagePassScore;
            if (def.Critical && g.Score < StageFloorScore)
                g.Passed = false;

            return g;
        }

        static void GradeCompileGate(PreBuildStageGrade g)
        {
            var snap = CaveBuildPreBuildReloop.UseFastCompileCapture
                ? CaveBuildCompileGate.CaptureReloopFast()
                : CaveBuildCompileGate.Capture();
            if (snap.VerifiedErrorCount > 0)
            {
                g.Score = Mathf.Max(0, 40 - snap.VerifiedErrorCount * 8);
                g.Issues.Add($"{snap.VerifiedErrorCount} compile error(s) — fix before building.");
                foreach (var e in snap.Errors)
                {
                    if (g.Issues.Count >= 6)
                        break;
                    g.Issues.Add($"{e.Code} {e.File}:{e.Line}");
                }

                g.Fixes.Add("Fix all errors in Console / CaveBuildCompileDiagnostics.json.");
                g.Fixes.Add("Run Window → Environment Kit → Cave Build → Sync API Key (if unrelated).");
            }
        }

        static void GradePackageTooling(PreBuildStageGrade g)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var seed = Path.Combine(
                hub,
                "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/research-catalog.seed.json");
            var script = Path.Combine(
                hub,
                "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/grade-and-fix.ts");

            if (!File.Exists(seed))
            {
                g.Score = 50;
                g.Issues.Add("Missing research-catalog.seed.json");
                g.Fixes.Add("cd Tools/cave-grader && npm run sync-research-catalog");
            }

            if (!File.Exists(script))
            {
                g.Score = Mathf.Min(g.Score, 40);
                g.Issues.Add("Missing grade-and-fix.ts");
            }

            var nodeModules = Path.Combine(
                hub,
                "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/node_modules");
            if (!Directory.Exists(nodeModules))
            {
                g.Score = Mathf.Min(g.Score, 55);
                g.Issues.Add("cave-grader node_modules missing");
                g.Fixes.Add("cd Tools/cave-grader && npm install");
            }
        }

        static void GradeSceneGround(PreBuildStageGrade g, SceneGroundInfo ground)
        {
            if (!ground.HasAnchor)
            {
                g.Score = 0;
                g.Issues.Add("No Ground-tagged anchor in scene.");
                g.Fixes.Add("Tag walkable floor as Ground or assign Environment Kit ground.");
                return;
            }

            if (ground.SurfaceY == 0f && ground.Bounds.size.sqrMagnitude < 0.01f)
            {
                g.Score = 75;
                g.Issues.Add("Ground bounds may be unset — verify SceneGroundResolver.");
            }
        }

        static void GradePrefabCatalog(PreBuildStageGrade g, bool layoutPrototype)
        {
            if (layoutPrototype)
            {
                g.Score = 100;
                return;
            }

            var catalog = LavaTubePrefabCatalog.Load();
            if (!catalog.IsValid)
            {
                g.Score = 0;
                g.Issues.Add("Lava tube prefab catalog empty.");
                g.Fixes.Add("Add prefabs under Assets/ per kit README.");
            }
        }

        static void GradeAiProvider(PreBuildStageGrade g)
        {
            if (CaveBuildCursorSettings.HasCredentialsForActiveProvider())
                return;

            g.Score = 60;
            g.Issues.Add(CaveBuildCursorSettings.GraderCredentialHint());
            g.Fixes.Add("Hub → Settings → pick provider + key, or Build → Apply Offline (No API) for procedural-only.");
        }

        static void GradeResearchManifest(PreBuildStageGrade g)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CaveBuildResearchExporter.ResearchPath);
            if (!File.Exists(path))
            {
                g.Score = 70;
                g.Issues.Add("CaveBuildResearch.json not generated yet.");
                g.Fixes.Add("Will be created on first pre-build export.");
            }
        }

        static void GradeScenePortal(PreBuildStageGrade g)
        {
            var portal = CaveBuildPortalSettings.PortalForBuild;
            if (portal == null)
            {
                var candidates = CaveBuildPortalSettings.FindPortalCandidates();
                if (candidates.Length > 0)
                {
                    g.Score = 95;
                    g.Issues.Add($"Portal auto-assign available ({candidates[0].name}) — not yet pinned.");
                    g.Fixes.Add("Pre-build reloop assigns portal automatically.");
                    return;
                }

                var found = GameObject.Find("PortalFive") ?? GameObject.Find("MainScene_CavePortal");
                if (found == null)
                {
                    g.Score = 80;
                    g.Issues.Add("No portal assigned — will auto-detect at build.");
                    return;
                }
            }

            g.Score = 100;
        }

        static void GradePriorCaveState(PreBuildStageGrade g, SceneGroundInfo ground)
        {
            if (!ground.HasAnchor)
                return;

            var cave = ground.Anchor.Find(CaveGeometryPaths.CaveSystemRootName);
            if (cave == null)
                cave = ground.Anchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            if (cave == null)
                return;

            var audit = CaveBuildVisualShellAuditor.Audit(cave);
            if (audit.LayeredSlabCount > 2 || audit.HasAdventureShell)
            {
                g.Score = 55;
                g.Issues.Add($"Existing cave has onion/layer issues (slabs={audit.LayeredSlabCount}).");
                g.Fixes.Add("Window → Environment Kit → Remove Cave Layered Shells before rebuild.");
            }
        }

        static string PreBuildRungDisplayName(string id) =>
            id switch
            {
                "compile_gate" => "Compile gate (zero CS errors)",
                "package_tooling" => "Package & grader tooling",
                "scene_ground" => "Scene ground anchor",
                "prefab_catalog" => "Prefab catalog",
                "ai_provider" => "AI provider readiness",
                "cursor_api" => "AI provider readiness",
                "research_manifest" => "Research manifest on disk",
                "scene_portal" => "Cave portal assignment",
                "prior_cave_state" => "Prior cave state (cleanup)",
                _ => id,
            };

        static void WriteLadderContext(CaveBuildPreBuildReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var failing = GetFailingRungs(report);
            var active = PickActiveRung(report);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{Escape(report.SceneName)}\",");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"letterGrade\": \"{Escape(report.LetterGrade)}\",");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"activeRung\": \"{Escape(active)}\",");
            sb.AppendLine($"  \"targetScore\": {TargetOverallScore},");
            sb.AppendLine("  \"rungOrder\": [");
            for (var i = 0; i < RungOrder.Length; i++)
            {
                var id = RungOrder[i].Id;
                sb.AppendLine($"    \"{id}\"{(i < RungOrder.Length - 1 ? "," : "")}");
            }

            sb.AppendLine("  ],");
            sb.AppendLine("  \"failingRungs\": [");
            for (var f = 0; f < failing.Count; f++)
                sb.AppendLine($"    \"{Escape(failing[f])}\"{(f < failing.Count - 1 ? "," : "")}");
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            var path = Path.Combine(hub, ContextPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString());
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    [Serializable]
    public class CaveBuildPreBuildReport
    {
        public string SceneName = string.Empty;
        public int LayoutSeed;
        public bool LayoutPrototype;
        public string GradingMode = "pre_build_ladder";
        public int OverallScore;
        public string LetterGrade = "F";
        public bool BuildAcceptable;
        public string ExportPath = CaveBuildPreBuildLadder.ReportPath;
        public List<PreBuildStageGrade> Stages = new();

        public void RecalculateOverall()
        {
            var totalWeight = 0;
            var weighted = 0;
            var criticalFail = false;

            foreach (var s in Stages)
            {
                totalWeight += s.Weight;
                weighted += s.Score * s.Weight;
                if (s.Critical && s.Score < CaveBuildPreBuildLadder.StageFloorScore)
                    criticalFail = true;
            }

            OverallScore = totalWeight > 0 ? Mathf.RoundToInt(weighted / (float)totalWeight) : 0;
            LetterGrade = ScoreToLetter(OverallScore);
            BuildAcceptable = !criticalFail && OverallScore >= CaveBuildPreBuildLadder.TargetOverallScore;
        }

        static string ScoreToLetter(int score)
        {
            if (score >= 97) return "A+";
            if (score >= 93) return "A";
            if (score >= 90) return "A-";
            if (score >= 87) return "B+";
            if (score >= 83) return "B";
            if (score >= 80) return "B-";
            if (score >= 77) return "C+";
            if (score >= 73) return "C";
            if (score >= 70) return "C-";
            if (score >= 60) return "D";
            return "F";
        }
    }

    [Serializable]
    public class PreBuildStageGrade
    {
        public string StageId;
        public string StageName;
        public int Weight;
        public bool Critical;
        public int Score;
        public bool Passed;
        public List<string> Issues = new();
        public List<string> Fixes = new();
    }

    static class CaveBuildPreBuildLadderWriter
    {
        public static void Write(CaveBuildPreBuildReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CaveBuildPreBuildLadder.ReportPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{E(report.SceneName)}\",");
            sb.AppendLine($"  \"layoutSeed\": {report.LayoutSeed},");
            sb.AppendLine($"  \"layoutPrototype\": {(report.LayoutPrototype ? "true" : "false")},");
            sb.AppendLine($"  \"gradingMode\": \"{E(report.GradingMode)}\",");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"letterGrade\": \"{E(report.LetterGrade)}\",");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"targetScore\": {CaveBuildPreBuildLadder.TargetOverallScore},");
            sb.AppendLine($"  \"targetGrade\": \"{CaveBuildPreBuildLadder.TargetGrade}\",");
            sb.AppendLine("  \"stages\": [");
            for (var i = 0; i < report.Stages.Count; i++)
            {
                var s = report.Stages[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{E(s.StageId)}\",");
                sb.AppendLine($"      \"name\": \"{E(s.StageName)}\",");
                sb.AppendLine($"      \"score\": {s.Score},");
                sb.AppendLine($"      \"weight\": {s.Weight},");
                sb.AppendLine($"      \"critical\": {(s.Critical ? "true" : "false")},");
                sb.AppendLine($"      \"passed\": {(s.Passed ? "true" : "false")},");
                sb.AppendLine("      \"issues\": [");
                WriteArr(sb, s.Issues, "        ");
                sb.AppendLine("      ],");
                sb.AppendLine("      \"fixes\": [");
                WriteArr(sb, s.Fixes, "        ");
                sb.AppendLine("      ]");
                sb.Append("    }");
                sb.AppendLine(i < report.Stages.Count - 1 ? "," : "");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
        }

        static void WriteArr(StringBuilder sb, List<string> items, string indent)
        {
            if (items == null || items.Count == 0)
                return;
            for (var i = 0; i < items.Count; i++)
            {
                sb.Append($"{indent}\"{E(items[i])}\"");
                if (i < items.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
        }

        static string E(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
