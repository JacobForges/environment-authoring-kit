using System.Collections.Generic;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Ordered rungs — remediation fixes one failing stage per pass, never a full rebuild.</summary>
    public static class CaveBuildQualityLadder
    {
        /// <summary>Stages that cannot be auto-fixed in-place (scene settings or full regen only).</summary>
        /// <summary>Scene-level only — cannot be fixed by mutating cave geometry.</summary>
        public static readonly HashSet<string> ManualOnlyStageIds = new()
        {
            "ground"
        };

        /// <summary>Priority when multiple rungs fail: critical geometry/play first.</summary>
        static readonly string[] FixPriority =
        {
            "visual_shell",
            "path",
            "ground_placement",
            "enclosure_policy",
            "mode_consistency",
            "geometry_integrity",
            "layout_integrity",
            "walkways",
            "playability",
            "spawn_reachability",
            "navmesh",
            "portal",
            "block_tunnel",
            "mob_spawns",
            "materials",
            "lighting",
            "water",
            "enclosure",
            "cave_mouth_seal",
            "packaging_readiness",
            "interior_ribs",
            "organic_mesh",
            "atmosphere",
            "performance",
            "export_artifacts"
        };

        public static CaveBuildStageGrade PickNextRung(
            CaveBuildQualityReport report,
            ISet<string> skipStageIds = null)
        {
            if (report == null)
                return null;

            CaveBuildStageGrade best = null;
            var bestPriority = int.MaxValue;
            var bestScore = int.MaxValue;

            foreach (var stage in report.Stages)
            {
                if (stage == null || ManualOnlyStageIds.Contains(stage.StageId))
                    continue;
                if (skipStageIds != null && skipStageIds.Contains(stage.StageId))
                    continue;
                if (!CaveBuildQualityStageFixer.CanAutoFix(stage.StageId))
                    continue;

                var threshold = CaveBuildQualityRubric.IsCritical(stage.StageId)
                    ? CaveBuildQualityRubric.StagePassScore
                    : CaveBuildQualityRubric.StageFloorScore;
                if (CaveBuildQualityRubric.IsAdventureRelaxedStage(report.AdventureMode, stage.StageId))
                    threshold = CaveBuildQualityRubric.StageFloorScore;
                if (stage.Score >= threshold)
                    continue;

                var priority = FixPriorityIndex(stage.StageId);
                var critical = CaveBuildQualityRubric.IsCritical(stage.StageId);
                var rank = priority + (critical ? 0 : 100);

                if (best == null || rank < bestPriority ||
                    (rank == bestPriority && stage.Score < bestScore))
                {
                    best = stage;
                    bestPriority = rank;
                    bestScore = stage.Score;
                }
            }

            return best;
        }

        /// <summary>Stage id for agent/other rung — first auto-fixable failure by ladder priority.</summary>
        public static string PickNextStageId(
            CaveBuildQualityReport report,
            ISet<string> skipStageIds = null) =>
            PickNextRung(report, skipStageIds)?.StageId;

        public static int FixPriorityIndex(string stageId)
        {
            var normalized = stageId == "ground" ? "ground_placement" : stageId;
            for (var i = 0; i < FixPriority.Length; i++)
            {
                if (FixPriority[i] == normalized || FixPriority[i] == stageId)
                    return i;
            }

            return FixPriority.Length + 50;
        }
    }
}
