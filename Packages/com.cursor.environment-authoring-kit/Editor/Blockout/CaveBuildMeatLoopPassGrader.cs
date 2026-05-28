using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveBuildMeatLoopPassGrader
    {
        public static void MergePassGrades(
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            int pass)
        {
            if (report == null)
                return;

            var mission = CaveBuildMeatLoopPassPlan.GetMission(pass);
            for (var i = 0; i < mission.GradeStageIds.Length; i++)
            {
                var stageId = mission.GradeStageIds[i];
                var graded = CaveBuildQualityGrader.GradeStageById(
                    stageId, caveRoot, ground, request, buildReport, report);
                if (graded == null)
                    continue;

                if (mission.GradeWeights != null && i < mission.GradeWeights.Length)
                    graded.Weight = mission.GradeWeights[i];

                UpsertStage(report, graded);
            }

            var slug = mission.Title.Replace(' ', '_').ToLowerInvariant();
            report.GradingMode = $"meat_loop_pass_{pass}_{slug}";
        }

        static void UpsertStage(CaveBuildQualityReport report, CaveBuildStageGrade graded)
        {
            for (var i = 0; i < report.Stages.Count; i++)
            {
                if (report.Stages[i]?.StageId != graded.StageId)
                    continue;
                report.Stages[i] = graded;
                return;
            }

            report.Stages.Add(graded);
        }
    }
}
