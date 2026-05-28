using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public sealed class CaveBuildStageGrade
    {
        public string StageId;
        public string StageName;
        public int Score;
        public int Weight = 10;
        public bool Passed;
        public readonly List<string> Issues = new();
        public readonly List<string> Fixes = new();

        public void AddIssue(string issue)
        {
            if (!string.IsNullOrEmpty(issue))
                Issues.Add(issue);
        }

        public void AddFix(string fix)
        {
            if (!string.IsNullOrEmpty(fix))
                Fixes.Add(fix);
        }

        public void ApplyPassThreshold()
        {
            var threshold = CaveBuildQualityRubric.IsCritical(StageId)
                ? CaveBuildQualityRubric.StagePassScore
                : CaveBuildQualityRubric.StageFloorScore;
            Passed = Score >= threshold;
        }
    }

    public sealed class CaveBuildQualityReport
    {
        public const string DefaultExportPath = "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json";

        /// <summary>Weighted mean of stage scores (can be high while ship gates fail).</summary>
        public int WeightedOverallScore;

        /// <summary>Displayed overall — min(weighted, worst critical stage) so letter matches blockers.</summary>
        public int OverallScore;

        public string LetterGrade = "F";
        public bool BuildAcceptable;
        public bool MeetsBetaTarget;
        public bool MeetsShipTarget;
        public bool IsDud;
        public bool HardFailure;
        public CaveBuildRecommendedAction RecommendedAction = CaveBuildRecommendedAction.None;
        public string GradingVersion = CaveBuildQualitySystem.GradingVersion;
        public string GradingMode = "full_build";
        public int RemediationPasses;
        public int Seed;
        public string SceneName;
        public string ExportPath = DefaultExportPath;
        public bool AdventureMode;
        /// <summary>Stages blocking Ship/Beta despite high weighted average.</summary>
        public readonly List<string> ShipBlockers = new();
        public bool LayoutPrototypeMode;
        /// <summary>How many times grading ran inside the ladder (initial + after each fix).</summary>
        public int LadderGradePasses;
        public readonly List<string> DudReasons = new();
        public readonly List<CaveBuildStageGrade> Stages = new();

        public void RecalculateOverall()
        {
            ShipBlockers.Clear();
            var totalWeight = 0;
            var weighted = 0;
            foreach (var s in Stages)
            {
                s.ApplyPassThreshold();
                totalWeight += s.Weight;
                weighted += s.Score * s.Weight;
            }

            WeightedOverallScore = totalWeight > 0 ? weighted / totalWeight : 0;
            var gateCap = CaveBuildQualityRubric.ComputeGateCapScore(this);
            OverallScore = Mathf.Min(WeightedOverallScore, gateCap);
            LetterGrade = CaveBuildQualityRubric.ScoreToLetter(OverallScore);
            HardFailure = CaveBuildQualityRubric.HasHardFailure(this, AdventureMode);
            MeetsBetaTarget = CaveBuildQualityRubric.MeetsBetaTarget(this);
            MeetsShipTarget = CaveBuildQualityRubric.MeetsShipTarget(this);
            BuildAcceptable = LayoutPrototypeMode
                ? !IsDud && !HardFailure && MeetsBetaTarget &&
                  CaveBuildQualityRubric.GetStageScore(this, "visual_shell") >=
                  CaveBuildQualityRubric.VisualShellStrictPassScore &&
                  CaveBuildQualityRubric.GetStageScore(this, "mode_consistency") >=
                  CaveBuildQualityRubric.StageFloorScore
                : !IsDud && MeetsBetaTarget;

            CaveBuildQualityRubric.CollectShipBlockers(this, ShipBlockers);
        }
    }
}
