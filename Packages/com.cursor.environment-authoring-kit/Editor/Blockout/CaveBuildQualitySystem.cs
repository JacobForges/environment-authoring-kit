using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Central cave build grading: stage rubric, dud detection, manifest export, and Cursor agent hooks.
    /// </summary>
    public static class CaveBuildQualitySystem
    {
        public const string ManifestPath = "Assets/EnvironmentKit/Generated/CaveBuildGradingManifest.json";
        public const string GradingVersion = "3.0.0-commercial";

        public static CaveBuildQualityReport LastGradedReport { get; private set; }

        public static void SetLastGradedReport(CaveBuildQualityReport report) =>
            LastGradedReport = report;

        public static CaveBuildQualityReport Grade(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            string gradingMode = "full_build",
            bool invokeCursorAgent = true,
            int meatLoopPass = -1,
            bool refreshNavMeshBeforeGrade = true,
            bool runPostGradeVisualCleanup = true)
        {
            if (refreshNavMeshBeforeGrade)
                TryRefreshNavMeshBeforeGrade(caveRoot, request, buildReport);

            var report = CaveBuildQualityGrader.BuildStageReport(caveRoot, ground, request, buildReport);
            report.GradingVersion = GradingVersion;
            report.GradingMode = gradingMode;
            ApplyDudDetection(report, caveRoot, request);
            report.RecalculateOverall();
            ApplyRecommendedAction(report, request);
            CaveBuildGradingManifestWriter.Write();
            CaveBuildQualityReportWriter.Write(report, gradingMode);
            LastGradedReport = report;
            if (runPostGradeVisualCleanup &&
                CaveBuildWorkflowCoordinator.ShouldRunPostGradePurge &&
                caveRoot != null &&
                CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
                CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            }

            CaveBuildAgentContextExporter.Export(report, caveRoot, meatLoopPass, ground);
            CaveBuildQualityAgentBridge.WriteStructuredPrompt(report, includeLiveSection: false);
            if (invokeCursorAgent)
                CaveBuildCursorAgentBridge.TryAutoInvokeAfterGrade(report);
            return report;
        }

        public static void ApplyDudDetection(
            CaveBuildQualityReport report,
            Transform caveRoot,
            WorldGenerationRequest request)
        {
            if (report == null)
                return;

            report.DudReasons.Clear();
            var layoutProto = report.LayoutPrototypeMode;
            var visual = CaveBuildQualityRubric.GetStageScore(report, "visual_shell");
            var pathLen = MeasurePathLength(caveRoot);
            var minPath = layoutProto
                ? CaveBuildQualityRubric.MinPathLengthLayoutPrototype
                : CaveBuildQualityRubric.MinPathLengthFullBuild;

            if (DetectHorizontalOnionBands(caveRoot, out var bandReason))
                report.DudReasons.Add(bandReason);

            var audit = caveRoot != null ? CaveBuildVisualShellAuditor.Audit(caveRoot) : default;
            if (audit.HasAdventureShell || audit.StackedCeilingSlabCount > 0 || audit.LayeredSlabCount > 2)
            {
                if (audit.HasAdventureShell)
                    report.DudReasons.Add("Horizontal onion: AdventureShell slab stack detected.");
                if (audit.StackedCeilingSlabCount > 0)
                    report.DudReasons.Add($"Horizontal onion: {audit.StackedCeilingSlabCount} stacked ceiling slab(s).");
            }

            if (visual < CaveBuildQualityRubric.VisualShellHardFailScore)
                report.DudReasons.Add($"visual_shell score {visual} < {CaveBuildQualityRubric.VisualShellHardFailScore} (hard fail).");
            else if (!layoutProto && visual < CaveBuildQualityRubric.VisualShellStrictPassScore)
                report.DudReasons.Add($"visual_shell score {visual} < {CaveBuildQualityRubric.VisualShellStrictPassScore} (strict pass).");

            if (pathLen > 0f && pathLen < minPath)
                report.DudReasons.Add($"Path length {pathLen:F0}m below minimum {minPath:F0}m for {(layoutProto ? "layout prototype" : "full build")}.");

            var playerFloor = CaveBuildQualityRubric.GetStageScore(report, "player_floor");
            if (playerFloor > 0 && playerFloor < CaveBuildQualityRubric.CriticalDudFloorScore)
                report.DudReasons.Add(
                    $"player_floor score {playerFloor} < {CaveBuildQualityRubric.CriticalDudFloorScore} — entrance spawn may fall through ground.");

            if (!layoutProto && caveRoot != null)
            {
                var ground = SceneGroundResolver.Resolve();
                if (ground.HasAnchor)
                {
                    var depthErr = CaveGroundPlacementUtility.MeasureRootDepthError(caveRoot, ground);
                    if (Mathf.Abs(depthErr) > CaveGroundPlacementUtility.MaxVerticalErrorMeters)
                        report.DudReasons.Add(
                            $"Cave root placement error {depthErr:F1}m — must sit below surface (SurfaceY − {CaveGeometryPaths.UndergroundDepthMeters}m).");
                }
            }

            foreach (var stage in report.Stages)
            {
                if (stage == null)
                    continue;
                if (CaveBuildQualityRubric.IsWaivedForLayoutPrototype(layoutProto, stage.StageId))
                    continue;
                if (CountsAsStructuralDud(stage.StageId) &&
                    stage.Score < CaveBuildQualityRubric.CriticalDudFloorScore)
                    report.DudReasons.Add($"Critical stage '{stage.StageId}' score {stage.Score} < {CaveBuildQualityRubric.CriticalDudFloorScore}.");
            }

            var modeScore = CaveBuildQualityRubric.GetStageScore(report, "mode_consistency");
            if (modeScore < CaveBuildQualityRubric.StageFloorScore)
                report.DudReasons.Add("mode_consistency failed — prototype/full build geometry mismatch.");

            report.IsDud = report.DudReasons.Count > 0;
        }

        public static void ApplyRecommendedAction(CaveBuildQualityReport report, WorldGenerationRequest request)
        {
            if (report == null)
                return;

            var layoutProto = report.LayoutPrototypeMode ||
                              (request != null && request.UseLayoutPrototype);

            if (report.IsDud)
            {
                report.RecommendedAction = PickRecommendedAction(report, layoutProto);
                return;
            }

            if (report.BuildAcceptable)
            {
                report.RecommendedAction = CaveBuildRecommendedAction.None;
                return;
            }

            report.RecommendedAction = layoutProto
                ? CaveBuildRecommendedAction.RebuildPrototype
                : CaveBuildRecommendedAction.RunMeatLoop;
        }

        static void TryRefreshNavMeshBeforeGrade(
            Transform caveRoot,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport)
        {
            if (caveRoot == null || buildReport == null || request == null)
                return;

            if (request.UseLayoutPrototype || CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
                return;

            buildReport.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, request);
        }

        /// <summary>NavMesh/performance failures are remediable — not structural ship-stoppers.</summary>
        static bool CountsAsStructuralDud(string stageId) =>
            CaveBuildQualityRubric.IsStructuralCritical(stageId);

        public static float MeasurePathLength(Transform caveRoot)
        {
            var authoring = caveRoot != null ? caveRoot.GetComponent<CaveSplinePathAuthoring>() : null;
            return authoring != null ? authoring.TotalLength : 0f;
        }

        public static bool DetectHorizontalOnionBands(Transform caveRoot, out string reason)
        {
            reason = null;
            if (caveRoot == null)
                return false;

            var bands = new Dictionary<int, int>();
            const float bandHeight = 1.25f;
            foreach (var r in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r.bounds.size.magnitude < 0.2f)
                    continue;
                var n = r.gameObject.name;
                if (n.Contains("SpawnMarker") || n.Contains("Marker_"))
                    continue;
                if (!LooksLikeShellRenderer(n))
                    continue;

                var y = Mathf.RoundToInt(r.bounds.center.y / bandHeight);
                bands.TryGetValue(y, out var count);
                bands[y] = count + 1;
            }

            var stackedBands = 0;
            foreach (var kv in bands)
            {
                if (kv.Value >= 4)
                    stackedBands++;
            }

            var visiblePlatforms = CountEnabledPathPlatformSlabs(caveRoot);
            if (visiblePlatforms > 0)
            {
                reason =
                    $"Horizontal onion: {visiblePlatforms} visible PathPlatform slab renderer(s) (use RouteTerrainFloor only).";
                return true;
            }

            if (stackedBands < 5)
                return false;

            reason = $"Horizontal onion: {stackedBands} Y-bands with ≥4 shell renderers (stacked slabs).";
            return true;
        }

        static int CountEnabledPathPlatformSlabs(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return 0;

            var platforms = geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName);
            if (platforms == null)
                return 0;

            var visible = 0;
            foreach (var mr in platforms.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null && mr.enabled)
                    visible++;
            }

            return visible;
        }

        static bool LooksLikeShellRenderer(string name)
        {
            if (name.StartsWith("CaveBlock_") || name.StartsWith("BlockRing_"))
                return false;
            if (name == CaveEnclosureShellBuilder.FloorRootName ||
                name == CaveEnclosureShellBuilder.CeilingRootName)
                return false;
            if (name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                return false;

            return name.Contains("PathCeiling") || name.StartsWith("Floor_") ||
                   name.StartsWith("Ceiling_") || name.Contains("Outer_") || name.Contains("SkySeal") ||
                   name.Contains(CaveAdventureShellBuilder.ShellRootName);
        }

        static string CapLetterGrade(string grade, string cap)
        {
            var order = new[]
            {
                CaveBuildQualityRubric.BlockedGrade,
                CaveBuildQualityRubric.PrototypeGrade,
                CaveBuildQualityRubric.AlphaGrade,
                CaveBuildQualityRubric.BetaGrade,
                CaveBuildQualityRubric.ShipGrade,
            };
            var gi = IndexOf(order, grade);
            var ci = IndexOf(order, cap);
            if (gi < 0 || ci < 0)
                return cap;
            return gi <= ci ? grade : cap;
        }

        static int IndexOf(string[] order, string g)
        {
            for (var i = 0; i < order.Length; i++)
            {
                if (order[i] == g)
                    return i;
            }

            return -1;
        }

        static CaveBuildRecommendedAction PickRecommendedAction(CaveBuildQualityReport report, bool layoutProto)
        {
            if (layoutProto)
                return CaveBuildRecommendedAction.RebuildPrototype;

            var visual = CaveBuildQualityRubric.GetStageScore(report, "visual_shell");
            if (visual < 50 || report.DudReasons.Exists(r => r.Contains("onion")))
                return CaveBuildRecommendedAction.InvokeCursorAgent;

            if (CaveBuildQualityRubric.GetStageScore(report, "mode_consistency") < 80)
                return CaveBuildRecommendedAction.ManualSculpt;

            return CaveBuildRecommendedAction.RunMeatLoop;
        }
    }
}
