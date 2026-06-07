using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// 100-point commercial production checklist — ship readiness categories (not marketing “AAA”).
    /// </summary>
    public static class CaveBuildCommercialProductionGrader
    {
        public const string ManifestPath =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildCommercialProductionManifest.json";

        public const int ShipScore = CaveBuildQualityRubric.ShipScore;
        public const int TargetScore = ShipScore;

        public static readonly AaaFeatureDef[] Features =
        {
            new("asset_validation", "Asset validation & missing refs", 10),
            new("ui_params", "UI / seed / biome params", 8),
            new("dynamic_geometry", "Dynamic organic geometry", 12),
            new("spline_tunnel", "Spline tunnels & nav path", 8),
            new("water_fx", "Water / atmosphere FX", 8),
            new("collider_lod", "Colliders, LOD, occlusion", 8),
            new("breakables", "Breakables & gameplay tags", 8),
            new("navmesh_lighting", "NavMesh & cave lighting", 10),
            new("heatmap_stats", "Build stats export", 8),
            new("connectivity", "Connectivity / spawn reachability", 10),
            new("packaging", "Report & share readiness", 10),
        };

        public readonly struct AaaFeatureDef
        {
            public readonly string Id;
            public readonly string Name;
            public readonly int MaxPoints;

            public AaaFeatureDef(string id, string name, int maxPoints)
            {
                Id = id;
                Name = name;
                MaxPoints = maxPoints;
            }
        }

        public sealed class AaaFeatureScore
        {
            public string Id;
            public string Name;
            public int MaxPoints;
            public int Score;
            public bool Passed;
            public List<string> Issues = new();
        }

        public sealed class AaaFeatureReport
        {
            public string SceneName;
            public int Seed;
            public int OverallScore;
            public bool BuildAcceptable;
            public string LetterGrade;
            public string ExportPath = ManifestPath;
            public List<AaaFeatureScore> Features = new();
        }

        public static AaaFeatureReport Grade(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            CaveBuildQualityReport quality)
        {
            var report = new AaaFeatureReport
            {
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                Seed = request != null ? request.Seed : 0,
            };

            foreach (var def in Features)
            {
                var s = GradeFeature(def, caveRoot, ground, request, buildReport, quality);
                report.Features.Add(s);
                report.OverallScore += s.Score;
            }

            report.BuildAcceptable = report.OverallScore >= CaveBuildQualityRubric.BetaScore;
            report.LetterGrade = CaveBuildQualityRubric.ScoreToLetter(report.OverallScore);
            Write(report);
            return report;
        }

        static AaaFeatureScore GradeFeature(
            AaaFeatureDef def,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            CaveBuildQualityReport quality)
        {
            var s = new AaaFeatureScore
            {
                Id = def.Id,
                Name = def.Name,
                MaxPoints = def.MaxPoints,
                Score = def.MaxPoints,
            };

            var hubRoot = CaveBuildCursorSettings.ResolveHubRoot();

            switch (def.Id)
            {
                case "asset_validation":
                    if (!LavaTubePrefabCatalog.Load().IsValid)
                    {
                        s.Score = 0;
                        s.Issues.Add("Prefab catalog invalid.");
                    }

                    if (CaveBuildCompileGate.HasBlockingErrors())
                    {
                        s.Score = Mathf.Min(s.Score, 20);
                        s.Issues.Add("Compile errors remain.");
                    }

                    break;
                case "ui_params":
                    if (request == null)
                    {
                        s.Score = 40;
                        s.Issues.Add("Missing generation request.");
                    }
                    else if (request.Seed == 0)
                    {
                        s.Score -= 2;
                        s.Issues.Add("Seed is zero.");
                    }

                    break;
                case "dynamic_geometry":
                    if (caveRoot == null)
                    {
                        s.Score = 0;
                        s.Issues.Add("No cave root.");
                    }
                    else
                    {
                        var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
                        if (audit.HasAdventureShell || audit.LayeredSlabCount > 2)
                        {
                            s.Score = Mathf.Max(0, 40 - audit.LayeredSlabCount * 5);
                            s.Issues.Add("Onion / layered shells detected.");
                        }
                    }

                    break;
                case "spline_tunnel":
                    var authoring = caveRoot != null ? caveRoot.GetComponent<CaveSplinePathAuthoring>() : null;
                    if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                    {
                        s.Score = 20;
                        s.Issues.Add("Spline path missing or too short.");
                    }
                    else if (authoring.TotalLength < 40f)
                    {
                        s.Score = 50;
                        s.Issues.Add($"Path length {authoring.TotalLength:F0}m low.");
                    }

                    break;
                case "water_fx":
                    if (request != null && request.IncludeCaveWater)
                    {
                        var water = caveRoot != null ? caveRoot.GetComponentInChildren<CaveWaterBranchAnchor>(true) : null;
                        if (water == null)
                        {
                            s.Score = 40;
                            s.Issues.Add("Water requested but branch anchor missing.");
                        }
                    }

                    break;
                case "collider_lod":
                    if (caveRoot != null && caveRoot.GetComponent<CaveBlockTunnelCuller>() == null)
                    {
                        s.Score -= 2;
                        s.Issues.Add("Block culler missing on adventure cave.");
                    }

                    if (buildReport != null && buildReport.TriangleEstimate > 500_000)
                    {
                        s.Score -= 3;
                        s.Issues.Add("High triangle estimate.");
                    }

                    break;
                case "breakables":
                    var minables = caveRoot != null
                        ? caveRoot.GetComponentsInChildren<MinableRock>(true).Length
                        : 0;
                    if (minables < 3)
                    {
                        s.Score = Mathf.Max(0, minables * 15);
                        s.Issues.Add($"Only {minables} minable rocks.");
                    }

                    break;
                case "navmesh_lighting":
                    if (buildReport != null && !buildReport.NavMeshBuilt)
                    {
                        s.Score -= 4;
                        s.Issues.Add("NavMesh not built.");
                    }

                    if (!NavMesh.SamplePosition(
                            caveRoot != null ? caveRoot.position : Vector3.zero,
                            out _,
                            12f,
                            NavMesh.AllAreas))
                    {
                        s.Score -= 3;
                        s.Issues.Add("No NavMesh near cave root.");
                    }

                    var lights = caveRoot != null ? caveRoot.GetComponentsInChildren<Light>(true).Length : 0;
                    if (lights < 2)
                    {
                        s.Score -= 2;
                        s.Issues.Add("Few cave lights.");
                    }

                    break;
                case "heatmap_stats":
                    if (buildReport == null || buildReport.PieceCount <= 0)
                    {
                        s.Score = 30;
                        s.Issues.Add("Build report missing piece stats.");
                    }
                    else
                    {
                        if (!File.Exists(Path.Combine(hubRoot, CaveBuildQualitySystem.ManifestPath)))
                        {
                            s.Score -= 2;
                            s.Issues.Add("CaveBuildGradingManifest.json missing.");
                        }
                    }

                    break;
                case "connectivity":
                    if (caveRoot != null && !CaveAdventurePlayabilityPipeline.CheckSpawnReachability(caveRoot))
                    {
                        s.Score = 35;
                        s.Issues.Add("Spawn reachability check failed.");
                    }

                    if (quality != null)
                    {
                        var spawnStage = CaveBuildQualityRubric.GetStageScore(quality, "spawn_reachability");
                        if (spawnStage > 0 && spawnStage < 70)
                        {
                            s.Score = Mathf.Min(s.Score, 40);
                            s.Issues.Add("spawn_reachability stage low.");
                        }
                    }

                    break;
                case "packaging":
                    const string qualityReportRel = "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json";
                    if (!File.Exists(Path.Combine(hubRoot, qualityReportRel)))
                    {
                        s.Score = 20;
                        s.Issues.Add("CaveBuildQualityReport.json missing.");
                    }

                    if (!File.Exists(Path.Combine(hubRoot, CaveBuildResearchExporter.ResearchPath)))
                    {
                        s.Score -= 3;
                        s.Issues.Add("CaveBuildResearch.json missing.");
                    }

                    break;
            }

            s.Passed = s.Score >= Mathf.RoundToInt(def.MaxPoints * 0.92f);
            return s;
        }

        public static void Write(AaaFeatureReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ManifestPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"sceneName\": \"{Esc(report.SceneName)}\",");
            sb.AppendLine($"  \"seed\": {report.Seed},");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"gradingStandard\": \"{CaveBuildQualityRubric.GradingStandard}\",");
            sb.AppendLine($"  \"targetGrade\": \"{CaveBuildQualityRubric.ShipGrade}\",");
            sb.AppendLine($"  \"targetScore\": {ShipScore},");
            sb.AppendLine($"  \"betaScore\": {CaveBuildQualityRubric.BetaScore},");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"letterGrade\": \"{Esc(report.LetterGrade)}\",");
            sb.AppendLine($"  \"exportPath\": \"{Esc(report.ExportPath)}\",");
            sb.AppendLine("  \"features\": [");
            for (var i = 0; i < report.Features.Count; i++)
            {
                var f = report.Features[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{Esc(f.Id)}\",");
                sb.AppendLine($"      \"name\": \"{Esc(f.Name)}\",");
                sb.AppendLine($"      \"maxPoints\": {f.MaxPoints},");
                sb.AppendLine($"      \"score\": {f.Score},");
                sb.AppendLine($"      \"passed\": {(f.Passed ? "true" : "false")},");
                sb.AppendLine("      \"issues\": [");
                for (var j = 0; j < f.Issues.Count; j++)
                    sb.AppendLine($"        \"{Esc(f.Issues[j])}\"{(j < f.Issues.Count - 1 ? "," : "")}");
                sb.AppendLine("      ]");
                sb.Append("    }");
                sb.AppendLine(i < report.Features.Count - 1 ? "," : "");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            Debug.Log(
                $"[CaveBuild] Commercial production manifest: {report.LetterGrade} ({report.OverallScore}/100) — {report.ExportPath}");
        }

        static string Esc(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>Backward-compatible alias — prefer <see cref="CaveBuildCommercialProductionGrader"/>.</summary>
    public static class CaveBuildAaaFeatureGrader
    {
        public const string ManifestPath = CaveBuildCommercialProductionGrader.ManifestPath;
        public const int TargetScore = CaveBuildCommercialProductionGrader.TargetScore;

        public static CaveBuildCommercialProductionGrader.AaaFeatureReport Grade(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            CaveBuildQualityReport quality) =>
            CaveBuildCommercialProductionGrader.Grade(caveRoot, ground, request, buildReport, quality);
    }
}
