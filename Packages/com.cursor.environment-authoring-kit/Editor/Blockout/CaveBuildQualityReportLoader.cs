#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Loads CaveBuildQualityReport.json (JsonUtility cannot parse nested stage arrays).</summary>
    public static class CaveBuildQualityReportLoader
    {
        [Serializable]
        sealed class StageDto
        {
            public string id;
            public string name;
            public int score;
            public int weight;
            public bool passed;
            public string[] issues;
            public string[] fixes;
        }

        [Serializable]
        sealed class ReportDto
        {
            public string scene;
            public int seed;
            public int overallScore;
            public string letterGrade;
            public bool buildAcceptable;
            public bool isDud;
            public bool hardFailure;
            public string recommendedAction;
            public string gradingMode;
            public int remediationPasses;
            public int ladderGradePasses;
            public bool adventureMode;
            public StageDto[] stages;
        }

        public static bool TryLoad(string hubRoot, out CaveBuildQualityReport report)
        {
            report = null;
            var path = Path.Combine(
                hubRoot ?? CaveBuildCursorSettings.ResolveHubRoot(),
                CaveBuildQualityReport.DefaultExportPath);
            if (!File.Exists(path))
                return false;

            try
            {
                var dto = JsonUtility.FromJson<ReportDto>(File.ReadAllText(path));
                if (dto == null)
                    return false;

                report = new CaveBuildQualityReport
                {
                    SceneName = dto.scene ?? string.Empty,
                    Seed = dto.seed,
                    OverallScore = dto.overallScore,
                    LetterGrade = string.IsNullOrEmpty(dto.letterGrade) ? "F" : dto.letterGrade,
                    BuildAcceptable = dto.buildAcceptable,
                    IsDud = dto.isDud,
                    HardFailure = dto.hardFailure,
                    GradingMode = dto.gradingMode ?? "loaded_json",
                    RemediationPasses = dto.remediationPasses,
                    LadderGradePasses = dto.ladderGradePasses,
                    AdventureMode = dto.adventureMode,
                };

                if (!string.IsNullOrEmpty(dto.recommendedAction) &&
                    Enum.TryParse(dto.recommendedAction, out CaveBuildRecommendedAction action))
                    report.RecommendedAction = action;

                if (dto.stages != null)
                {
                    foreach (var s in dto.stages)
                    {
                        if (s == null || string.IsNullOrEmpty(s.id))
                            continue;

                        var g = new CaveBuildStageGrade
                        {
                            StageId = s.id,
                            StageName = s.name ?? s.id,
                            Score = s.score,
                            Weight = s.weight > 0 ? s.weight : 10,
                            Passed = s.passed,
                        };
                        if (s.issues != null)
                            foreach (var i in s.issues)
                                g.AddIssue(i);
                        if (s.fixes != null)
                            foreach (var f in s.fixes)
                                g.AddFix(f);
                        report.Stages.Add(g);
                    }
                }

                report.RecalculateOverall();
                return report.Stages.Count > 0 || report.OverallScore > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Quality report load failed: " + ex.Message);
                return false;
            }
        }

        public static CaveBuildQualityReport LoadOrNull(string hubRoot = null) =>
            TryLoad(hubRoot, out var r) ? r : null;
    }
}
#endif
