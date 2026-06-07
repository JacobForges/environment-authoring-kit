#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>32-rung rubric for paced Hollow Titan exterior stump sculpt phases.</summary>
    public static class HollowTitanStumpSculptLadder
    {
        public const string ReportPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanStumpSculptLadderReport.json";

        public const int TargetOverallScore = 85;
        public const int StagePassScore = 82;
        public const int StageFloorScore = 60;

        public static void GradeAndLogPhase(
            int phaseIndex,
            Transform landmarkRoot,
            Terrain mainTerrain,
            HollowTitanStumpSculptLadderReport report)
        {
            if (report == null || landmarkRoot == null)
                return;

            var grade = GradePhase(phaseIndex, landmarkRoot, mainTerrain);
            UpsertStage(report, grade);

            var status = grade.Passed ? "PASS" : "FAIL";
            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Stump rung {phaseIndex} — {status} score {grade.Score}.",
                forceUnityConsole: !grade.Passed);
        }

        public static TerrainStageGrade GradePhase(int phaseIndex, Transform root, Terrain mainTerrain)
        {
            var g = new TerrainStageGrade
            {
                StageId = $"hollow_titan_stump_{phaseIndex:D2}",
                StageName = phaseIndex >= 0 && phaseIndex < HollowTitanStumpSculptPhases.PhaseLabels.Length
                    ? HollowTitanStumpSculptPhases.PhaseLabels[phaseIndex]
                    : $"stump phase {phaseIndex}",
                Weight = phaseIndex is >= 9 and <= 23 ? 4 : 2,
                Critical = phaseIndex is 0 or 13 or 30,
                Score = 100,
            };

            if (root == null)
            {
                g.Score = 0;
                g.Issues.Add("HollowTitanLandmark root missing.");
                g.Passed = false;
                return g;
            }

            var data = root.GetComponent<HollowTitanLandmarkBuildData>();
            var shell = root.Find("Exterior/TrunkShell");
            var hasMarker = shell != null &&
                            shell.Find(HollowTitanExteriorTerraform.SculptMarkerName) != null;

            if (phaseIndex >= 5 && !hasMarker && phaseIndex < 30)
            {
                g.Score -= 18;
                g.Issues.Add("StumpTerraformDone marker missing mid-sculpt.");
            }

            if (data != null)
            {
                var expected = HollowTitanBuildLadder.ConceptTrunkRadiusMeters *
                               HollowTitanBuildLadder.TitanDisplayScaleMultiplier;
                if (data.TrunkRadius < expected * 0.8f)
                {
                    g.Score -= 22;
                    g.Issues.Add($"Trunk radius {data.TrunkRadius:F0}m below titan target ~{expected:F0}m.");
                }
            }

            if (mainTerrain != null && phaseIndex == 30)
            {
                var pos = root.position;
                var groundY = mainTerrain.SampleHeight(pos) + mainTerrain.transform.position.y;
                if (Mathf.Abs(pos.y - groundY) > 3f)
                {
                    g.Score -= 20;
                    g.Issues.Add("Root not re-snapped to terrain after sculpt.");
                    g.Fixes.Add("Re-run resnap phase or Window → Resnap Hollow Titan.");
                }
            }

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            g.Passed = g.Score >= StagePassScore;
            if (g.Critical && g.Score < StageFloorScore)
                g.Passed = false;

            return g;
        }

        public static void FinalizeReport(HollowTitanStumpSculptLadderReport report)
        {
            if (report == null)
                return;

            report.SceneName = SceneManager.GetActiveScene().name;
            report.RecalculateOverall();
            WriteReport(report);
        }

        static void UpsertStage(HollowTitanStumpSculptLadderReport report, TerrainStageGrade stage)
        {
            if (report?.Stages == null || stage == null)
                return;

            for (var i = 0; i < report.Stages.Count; i++)
            {
                if (report.Stages[i].StageId == stage.StageId)
                {
                    report.Stages[i] = stage;
                    return;
                }
            }

            report.Stages.Add(stage);
        }

        static void WriteReport(HollowTitanStumpSculptLadderReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{Escape(report.SceneName)}\",");
            sb.AppendLine($"  \"seed\": {report.Seed},");
            sb.AppendLine($"  \"gradingMode\": \"hollow_titan_stump_sculpt_ladder\",");
            sb.AppendLine($"  \"phaseCount\": {HollowTitanStumpSculptPhases.PhaseCount},");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"exportPath\": \"{ReportPath}\",");
            sb.AppendLine("  \"stages\": [");
            for (var i = 0; i < report.Stages.Count; i++)
            {
                var s = report.Stages[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{s.StageId}\",");
                sb.AppendLine($"      \"score\": {s.Score},");
                sb.AppendLine($"      \"passed\": {(s.Passed ? "true" : "false")}");
                sb.AppendLine(i < report.Stages.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            report.ExportPath = ReportPath;
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public class HollowTitanStumpSculptLadderReport
    {
        public string SceneName = string.Empty;
        public int Seed;
        public int OverallScore;
        public bool BuildAcceptable;
        public string ExportPath = HollowTitanStumpSculptLadder.ReportPath;
        public List<TerrainStageGrade> Stages = new();

        public void RecalculateOverall()
        {
            var totalWeight = 0;
            var weighted = 0;
            var criticalFail = false;

            foreach (var s in Stages)
            {
                totalWeight += s.Weight;
                weighted += s.Score * s.Weight;
                if (s.Critical && s.Score < HollowTitanStumpSculptLadder.StageFloorScore)
                    criticalFail = true;
            }

            OverallScore = totalWeight > 0 ? Mathf.RoundToInt(weighted / (float)totalWeight) : 0;
            BuildAcceptable = !criticalFail &&
                              OverallScore >= HollowTitanStumpSculptLadder.TargetOverallScore;
        }
    }
}
#endif
