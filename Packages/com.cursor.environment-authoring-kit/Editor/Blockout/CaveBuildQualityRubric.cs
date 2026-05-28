using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public readonly struct CaveBuildStageDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int Weight;
        public readonly int PassScore;
        public readonly int FloorScore;
        public readonly bool Critical;
        public readonly bool WaivedLayoutPrototype;
        public readonly bool FullBuildOnly;

        public CaveBuildStageDefinition(
            string id,
            string name,
            int weight,
            int passScore = 90,
            int floorScore = 80,
            bool critical = false,
            bool waivedLayoutPrototype = false,
            bool fullBuildOnly = false)
        {
            Id = id;
            Name = name;
            Weight = weight;
            PassScore = passScore;
            FloorScore = floorScore;
            Critical = critical;
            WaivedLayoutPrototype = waivedLayoutPrototype;
            FullBuildOnly = fullBuildOnly;
        }
    }

    /// <summary>
    /// Commercial production grades (industry-style ship gates), not consumer “AAA game” marketing tiers.
    /// Ship = release-ready slice; Beta = playable internal milestone; Alpha/Prototype = earlier R&amp;D.
    /// </summary>
    public static class CaveBuildQualityRubric
    {
        public const string GradingStandard = "commercial_production";

        public const string ShipGrade = "Ship";
        public const string BetaGrade = "Beta";
        public const string AlphaGrade = "Alpha";
        public const string PrototypeGrade = "Prototype";
        public const string BlockedGrade = "Blocked";

        /// <summary>Release-ready on target platform (stops meat loop / optional Cursor).</summary>
        public const string TargetGrade = ShipGrade;

        public const int ShipScore = 95;
        public const int BetaScore = 85;
        public const int AlphaScore = 70;
        public const int PrototypeScore = 50;

        /// <summary>Legacy alias — was 99 for internal “AAA+”.</summary>
        public const int TargetOverallScore = ShipScore;

        public const int StagePassScore = 90;
        public const int StageFloorScore = 80;
        public const int VisualShellStrictPassScore = 90;
        public const int VisualShellHardFailScore = 75;
        public const int CriticalDudFloorScore = 65;
        public const float MinPathLengthFullBuild = 40f;
        public const float MinPathLengthLayoutPrototype = 28f;
        public const int MaxRemediationLoops = 8;

        public static readonly CaveBuildStageDefinition[] StageDefinitions =
        {
            new("ground", "Scene ground anchor", 6, critical: true),
            new("path", "Descending spline path", 10, critical: true),
            new("layout_integrity", "Layout solution path", 10, critical: true),
            new("visual_shell", "Visual shell (no onion)", 22, critical: true),
            new("enclosure_policy", "Floor+ceiling policy", 10, critical: true),
            new("geometry_integrity", "Geometry integrity", 14, critical: true),
            new("walkways", "Walk colliders", 10, critical: true),
            new("player_floor", "Player floor (no fall-through)", 16, critical: true),
            new("block_tunnel", "Block tunnel shell", 12, critical: true, waivedLayoutPrototype: true, fullBuildOnly: true),
            new("navmesh", "NavMesh bake", 6, critical: true),
            new("portal", "Portal + spawn", 10, critical: true),
            new("spawn_reachability", "Spawn reachability", 10, critical: true),
            new("mob_spawns", "Enemy spawn coverage", 6, critical: true),
            new("playability", "Jump gaps traversable", 8),
            new("performance", "Renderer / triangle budget (XR)", 8),
            new("lighting", "Cave lighting", 5),
            new("atmosphere", "Underground atmosphere", 5),
            new("cave_mouth_seal", "Underground mouth seal (no surface terrain)", 8, waivedLayoutPrototype: false),
            new("export_artifacts", "Blueprint + quality JSON", 4),
            new("mode_consistency", "Prototype vs full build", 8, critical: true),
            new("organic_mesh", "Organic / adventure shell", 8, waivedLayoutPrototype: true, fullBuildOnly: true),
            new("enclosure", "Occlusion shell (legacy)", 4, waivedLayoutPrototype: true, fullBuildOnly: true),
            new("materials", "URP materials", 4, fullBuildOnly: true),
            new("water", "Underground water", 4, fullBuildOnly: true),
            new("interior_ribs", "Interior rock ribs", 4, waivedLayoutPrototype: true, fullBuildOnly: true),
            new("packaging_readiness", "Packaging / first-minute play", 6, critical: true, fullBuildOnly: true),
        };

        public static readonly string[] CriticalStageIds =
        {
            "ground",
            "path",
            "layout_integrity",
            "visual_shell",
            "enclosure_policy",
            "geometry_integrity",
            "block_tunnel",
            "walkways",
            "player_floor",
            "portal",
            "navmesh",
            "mob_spawns",
            "spawn_reachability",
            "mode_consistency",
            "packaging_readiness",
        };

        public static readonly string[] AdventureRelaxedStageIds =
        {
            "interior_ribs",
            "cave_mouth_seal",
            "performance",
            "water",
        };

        public static string ScoreToLetter(int score) => score switch
        {
            >= ShipScore => ShipGrade,
            >= BetaScore => BetaGrade,
            >= AlphaScore => AlphaGrade,
            >= PrototypeScore => PrototypeGrade,
            _ => BlockedGrade,
        };

        public static string DescribeGrade(string letterGrade) => letterGrade switch
        {
            ShipGrade =>
                "Commercial production: release-ready for target platform (playable, stable slice, export artifacts).",
            BetaGrade =>
                "Commercial production: feature-complete enough for structured playtest; polish and perf pass before ship.",
            AlphaGrade =>
                "Commercial production: playable vertical slice; blockers acceptable for internal QA only.",
            PrototypeGrade =>
                "Commercial production: R&D / demo only — not player-facing ship candidate.",
            _ => "Commercial production: blocked — structural or dud failure; do not ship.",
        };

        /// <summary>Beta+ with stage floors — iteration / playtest milestone (Ship also qualifies).</summary>
        public static bool MeetsBetaTarget(CaveBuildQualityReport report) =>
            MeetsProductionTier(report, BetaScore, requiredLetter: null, requireCriticalPass: false);

        /// <summary>Ship tier — commercial release-ready per kit gates.</summary>
        public static bool MeetsShipTarget(CaveBuildQualityReport report) =>
            MeetsProductionTier(report, ShipScore, ShipGrade, requireCriticalPass: true);

        /// <summary>Legacy name — maps to <see cref="MeetsShipTarget"/>.</summary>
        public static bool MeetsStrictTarget(CaveBuildQualityReport report) => MeetsShipTarget(report);

        /// <summary>Legacy — same as <see cref="MeetsShipTarget"/>.</summary>
        public static bool MeetsTarget(CaveBuildQualityReport report) => MeetsShipTarget(report);

        static bool MeetsProductionTier(
            CaveBuildQualityReport report,
            int minScore,
            string requiredLetter,
            bool requireCriticalPass)
        {
            if (report == null || report.IsDud)
                return false;

            if (report.OverallScore < minScore)
                return false;

            if (!string.IsNullOrEmpty(requiredLetter) && report.LetterGrade != requiredLetter)
                return false;

            foreach (var stage in report.Stages)
            {
                if (stage == null)
                    continue;

                if (IsWaivedForLayoutPrototype(report.LayoutPrototypeMode, stage.StageId))
                    continue;

                if (IsAdventureRelaxedStage(report.AdventureMode, stage.StageId))
                {
                    if (stage.Score < StageFloorScore)
                        return false;
                    continue;
                }

                if (stage.Score < StageFloorScore)
                    return false;

                if (requireCriticalPass && IsCritical(stage.StageId) && stage.Score < StagePassScore)
                    return false;
            }

            if (report.AdventureMode || report.LayoutPrototypeMode)
            {
                if (GetStageScore(report, "visual_shell") < VisualShellStrictPassScore)
                    return false;

                if (GetStageScore(report, "enclosure_policy") < VisualShellStrictPassScore)
                    return false;
            }

            if (!report.LayoutPrototypeMode && report.AdventureMode)
            {
                if (GetStageScore(report, "block_tunnel") < StagePassScore)
                    return false;
            }

            return !HasHardFailure(report, report.AdventureMode);
        }

        public static int GetStageScore(CaveBuildQualityReport report, string stageId)
        {
            if (report?.Stages == null)
                return 0;

            foreach (var stage in report.Stages)
            {
                if (stage != null && stage.StageId == stageId)
                    return stage.Score;
            }

            return 0;
        }

        public static bool IsWaivedForLayoutPrototype(bool layoutPrototype, string stageId)
        {
            if (!layoutPrototype || string.IsNullOrEmpty(stageId))
                return false;

            foreach (var d in StageDefinitions)
            {
                if (d.Id == stageId)
                    return d.WaivedLayoutPrototype;
            }

            return stageId == "block_tunnel" || stageId == "organic_mesh" || stageId == "enclosure";
        }

        public static bool IsAdventureRelaxedStage(bool adventureMode, string stageId)
        {
            if (!adventureMode || string.IsNullOrEmpty(stageId))
                return false;

            foreach (var id in AdventureRelaxedStageIds)
            {
                if (id == stageId)
                    return true;
            }

            return false;
        }

        public static bool IsCritical(string stageId)
        {
            foreach (var id in CriticalStageIds)
            {
                if (id == stageId)
                    return true;
            }

            return false;
        }

        public static bool IsStructuralCritical(string stageId) =>
            IsCritical(stageId) &&
            stageId != "navmesh" &&
            stageId != "portal" &&
            stageId != "mob_spawns" &&
            stageId != "packaging_readiness";

        public static IReadOnlyList<CaveBuildStageGrade> GetFailingStages(CaveBuildQualityReport report)
        {
            var list = new List<CaveBuildStageGrade>();
            if (report == null)
                return list;

            foreach (var stage in report.Stages)
            {
                if (IsWaivedForLayoutPrototype(report.LayoutPrototypeMode, stage.StageId))
                    continue;

                if (IsAdventureRelaxedStage(report.AdventureMode, stage.StageId))
                {
                    if (stage.Score < StageFloorScore)
                        list.Add(stage);
                    continue;
                }

                if (stage.Score < StagePassScore || (IsCritical(stage.StageId) && !stage.Passed))
                    list.Add(stage);
            }

            return list;
        }

        public static bool HasHardFailure(CaveBuildQualityReport report, bool adventureMode = false)
        {
            if (report == null || report.IsDud)
                return true;

            if (report.LayoutPrototypeMode)
            {
                var visual = GetStageScore(report, "visual_shell");
                return visual < VisualShellHardFailScore;
            }

            if (adventureMode)
            {
                foreach (var stage in report.Stages)
                {
                    if (stage == null)
                        continue;

                    if (IsAdventureRelaxedStage(true, stage.StageId))
                        continue;

                    if (stage.StageId == "visual_shell" && stage.Score < VisualShellHardFailScore)
                        return true;

                    if (IsStructuralCritical(stage.StageId) && stage.Score < CriticalDudFloorScore)
                        return true;
                }

                return false;
            }

            foreach (var stage in report.Stages)
            {
                if (stage == null)
                    continue;
                if (IsCritical(stage.StageId) && stage.Score < CriticalDudFloorScore)
                    return true;
            }

            return false;
        }

        /// <summary>Caps displayed overall so Ship letter is not shown when critical stages fail.</summary>
        public static int ComputeGateCapScore(CaveBuildQualityReport report)
        {
            if (report?.Stages == null || report.Stages.Count == 0)
                return 0;

            var cap = 100;
            foreach (var stage in report.Stages)
            {
                if (stage == null || string.IsNullOrEmpty(stage.StageId))
                    continue;
                if (IsWaivedForLayoutPrototype(report.LayoutPrototypeMode, stage.StageId))
                    continue;

                if (IsCritical(stage.StageId) && stage.Score < StagePassScore)
                    cap = Mathf.Min(cap, stage.Score);

                if (IsAdventureRelaxedStage(report.AdventureMode, stage.StageId) &&
                    stage.Score < StageFloorScore)
                    cap = Mathf.Min(cap, stage.Score);
                else if (!IsAdventureRelaxedStage(report.AdventureMode, stage.StageId) &&
                         stage.Score < StageFloorScore)
                    cap = Mathf.Min(cap, stage.Score);
            }

            if (report.AdventureMode || report.LayoutPrototypeMode)
            {
                cap = Mathf.Min(cap, GetStageScore(report, "visual_shell"));
                cap = Mathf.Min(cap, GetStageScore(report, "enclosure_policy"));
            }

            return cap;
        }

        public static void CollectShipBlockers(CaveBuildQualityReport report, List<string> blockers)
        {
            blockers?.Clear();
            if (report == null || blockers == null)
                return;

            if (report.IsDud)
            {
                foreach (var r in report.DudReasons)
                    blockers.Add("dud: " + r);
                return;
            }

            if (report.WeightedOverallScore >= report.OverallScore + 3)
                blockers.Add(
                    $"weighted {report.WeightedOverallScore}/100 vs gate {report.OverallScore}/100 (critical stage floors)");

            if (!MeetsShipTarget(report))
            {
                foreach (var stage in GetFailingStages(report))
                {
                    if (stage == null)
                        continue;
                    var threshold = IsCritical(stage.StageId) ? StagePassScore : StageFloorScore;
                    if (stage.Score < threshold)
                        blockers.Add($"{stage.StageId}: {stage.Score}/100 (need {threshold}+)");
                }
            }
        }
    }
}
