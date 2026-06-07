using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Single active Cursor rung — mirrors Tools/cave-grader/prompt-ladder.ts priority.</summary>
    public static class CaveBuildPromptLadder
    {
        public const string RungResearch = "research";
        public const string RungCompileGate = "compile_gate";
        public const string RungVisualShell = "visual_shell";
        public const string RungGroundPlacement = "ground_placement";
        public const string RungFloorCollision = "floor_collision";
        public const string RungNavmesh = "navmesh";
        public const string RungMaterials = "materials";
        public const string RungPerformance = "performance";
        public const string RungOther = "other";

        public const int MaxChainPerMeatPass = 3;
        public const int MaxPreBuildChainPerPass = 3;

        /// <summary>Mandatory Cursor phases before scene ladder rungs (after every cave build).</summary>
        public static readonly string[] PostBuildMandatoryPhases =
        {
            RungResearch,
            RungCompileGate,
        };

        /// <summary>Mandatory Cursor phases before cave geometry (matches pre-build workflow exporter).</summary>
        public static readonly string[] PreBuildMandatoryPhases =
        {
            RungResearch,
            "plan",
            RungCompileGate,
        };

        public const string PhaseReadinessLadder = "readiness_ladder";
        public const string PhaseProceedBuild = "proceed_build";

        /// <summary>Weighted readiness rungs graded before cave generation.</summary>
        public static readonly string[] PreBuildLadderRungs =
        {
            RungCompileGate,
            "package_tooling",
            "scene_ground",
            "prefab_catalog",
            "cursor_api",
            "research_manifest",
            "scene_portal",
            "prior_cave_state",
        };

        public static readonly string[] CursorRungOrder =
        {
            RungVisualShell,
            RungGroundPlacement,
            RungFloorCollision,
            RungNavmesh,
            RungMaterials,
            RungPerformance,
            RungOther
        };

        public static readonly string[] AllRungsIncludingWorkflow =
        {
            RungResearch,
            RungCompileGate,
            RungVisualShell,
            RungGroundPlacement,
            RungFloorCollision,
            RungNavmesh,
            RungMaterials,
            RungPerformance,
            RungOther
        };

        static readonly HashSet<string> VisualShellStageIds = new()
        {
            "visual_shell",
            "enclosure_policy",
            "mode_consistency",
            "geometry_integrity",
            "layout_integrity",
            "organic_mesh",
            "block_tunnel",
            "enclosure",
            "interior_ribs",
            "walkways"
        };

        /// <summary>Graded stages only — unlisted ids (e.g. terrain_carve fixer alias) must not fail the rung at score 0.</summary>
        static readonly HashSet<string> GroundStageIds = new()
        {
            "ground",
            "cave_mouth_seal",
            "spawn_reachability",
            "portal"
        };

        static readonly HashSet<string> FloorCollisionStageIds = new()
        {
            "player_floor",
            "walkways",
            "spawn_reachability",
            "portal"
        };

        public static List<string> GetFailingRungs(
            CaveBuildQualityReport report,
            Transform caveRoot = null,
            SceneGroundInfo ground = null,
            CaveBuildVisualShellAuditor.AuditResult? visualAudit = null)
        {
            var list = new List<string>();
            if (report == null)
                return list;

            var audit = visualAudit ?? (caveRoot != null
                ? CaveBuildVisualShellAuditor.Audit(caveRoot)
                : default);

            foreach (var rung in CursorRungOrder)
            {
                if (IsRungFailing(rung, report, caveRoot, ground, audit))
                    list.Add(rung);
            }

            return list;
        }

        public static string PickActiveRung(
            CaveBuildQualityReport report,
            Transform caveRoot = null,
            SceneGroundInfo ground = null,
            CaveBuildVisualShellAuditor.AuditResult? visualAudit = null,
            ISet<string> skipRungs = null)
        {
            if (report == null)
                return RungOther;

            var audit = visualAudit ?? (caveRoot != null
                ? CaveBuildVisualShellAuditor.Audit(caveRoot)
                : default);

            foreach (var rung in CursorRungOrder)
            {
                if (skipRungs != null && skipRungs.Contains(rung))
                    continue;
                if (IsRungFailing(rung, report, caveRoot, ground, audit))
                    return rung;
            }

            return RungOther;
        }

        public static bool IsRungFailing(
            string rung,
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground,
            CaveBuildVisualShellAuditor.AuditResult audit)
        {
            switch (rung)
            {
                case RungVisualShell:
                    if (report.IsDud && report.DudReasons.Exists(r =>
                            r.Contains("onion", System.StringComparison.OrdinalIgnoreCase) ||
                            r.Contains("AdventureShell", System.StringComparison.OrdinalIgnoreCase)))
                        return true;

                    var rubricVisual = CaveBuildQualityRubric.GetStageScore(report, "visual_shell");
                    if (caveRoot != null &&
                        rubricVisual >= CaveBuildQualityRubric.VisualShellStrictPassScore &&
                        CaveCompactRouteUtility.MeetsAuditCompactRoute(audit))
                        return false;

                    if (audit.StrayBlockCount > 0)
                        return true;

                    if (audit.HasAdventureShell || audit.StackedCeilingSlabCount > 0 ||
                        audit.VisibleFlatPlatformCount > 0 && audit.HasRouteTerrainFloor)
                        return true;
                    if (audit.BlockRingCount > 0)
                    {
                        var pathSteps = audit.SolutionPathSteps > 0
                            ? audit.SolutionPathSteps
                            : EstimatePathSteps(caveRoot);
                        if (audit.BlockRingCount > pathSteps + 2 && audit.BlocksPerRingAvg > 36f)
                            return true;
                    }

                    return StageBelowPass(report, VisualShellStageIds) ||
                           rubricVisual < CaveBuildQualityRubric.VisualShellStrictPassScore;

                case RungGroundPlacement:
                    if (caveRoot != null && ground != null &&
                        !CaveGroundPlacementUtility.IsRootPlacementAcceptable(caveRoot, ground))
                        return true;
                    return StageBelowPass(report, GroundStageIds);

                case RungFloorCollision:
                    if (CaveBuildQualityRubric.GetStageScore(report, "player_floor") <
                        CaveBuildQualityRubric.StagePassScore)
                        return true;
                    foreach (var reason in report.DudReasons)
                    {
                        if (reason != null &&
                            (reason.Contains("fall") || reason.Contains("walkable") ||
                             reason.Contains("fall-through")))
                            return true;
                    }

                    return StageBelowPass(report, FloorCollisionStageIds);

                case RungNavmesh:
                    return CaveBuildQualityRubric.GetStageScore(report, "navmesh") <
                           CaveBuildQualityRubric.StagePassScore;

                case RungMaterials:
                    return CaveBuildQualityRubric.GetStageScore(report, "materials") <
                               CaveBuildQualityRubric.StagePassScore ||
                           CaveBuildQualityRubric.GetStageScore(report, "lighting") <
                               CaveBuildQualityRubric.StagePassScore;

                case RungPerformance:
                    return CaveBuildQualityRubric.GetStageScore(report, "performance") <
                           CaveBuildQualityRubric.StageFloorScore;

                case RungOther:
                    return !report.BuildAcceptable || report.IsDud;

                default:
                    return false;
            }
        }

        static bool StageBelowPass(CaveBuildQualityReport report, IEnumerable<string> stageIds)
        {
            foreach (var id in stageIds)
            {
                var score = CaveBuildQualityRubric.GetStageScore(report, id);
                if (score < CaveBuildQualityRubric.StagePassScore)
                    return true;
            }

            return false;
        }

        static int EstimatePathSteps(Transform caveRoot)
        {
            var meta = caveRoot != null ? caveRoot.GetComponent<CaveBuildMetadata>() : null;
            return meta != null ? meta.tunnelSegments + meta.chamberCount * 2 : 24;
        }
    }
}
