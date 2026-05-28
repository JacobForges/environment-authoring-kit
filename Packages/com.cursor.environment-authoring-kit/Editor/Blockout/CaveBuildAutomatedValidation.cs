#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Paced validation bot + three complementary checks with targeted fixes only (no full cave rebuild).
    /// </summary>
    public static class CaveBuildAutomatedValidation
    {
        public const int StepCount = 6;

        public static readonly string[] StepLabels =
        {
            "Surface route (terrain pipeline — skipped in cave queue for FullWorld)",
            "Cave route probe bot (underground)",
            "Visual shell audit",
            "Geometry integrity (invisible colliders)",
            "Surface walk-in (terrain pipeline — skipped in cave queue for FullWorld)",
            "Combat probe (mobs, attack, defend)",
        };

        public static void RunStep(
            int step,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (caveRoot == null || !CaveGeometryPaths.IsAdventureCave(caveRoot))
                return;

            switch (step)
            {
                case 0:
                    RunSurfaceRouteProbe(caveRoot);
                    break;
                case 1:
                    RunRouteProbe(caveRoot, request);
                    break;
                case 2:
                    RunVisualShellCheck(caveRoot, request);
                    break;
                case 3:
                    RunGeometryIntegrityCheck(caveRoot, request);
                    break;
                case 4:
                    RunSurfaceEntranceCheck(caveRoot, ground, request);
                    break;
                case 5:
                    RunCombatCheck(caveRoot, request);
                    break;
            }

        }

        /// <summary>Final lightweight re-probe after meat loop (no fixes unless critical).</summary>
        public static void RunFinalProbe(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var report = CaveRouteProbeRunner.Run(caveRoot);
            CaveRouteProbeRunner.Export(report, caveRoot);
            if (!report.Passed)
            {
                Debug.LogWarning(
                    $"[CaveBuild] Final route probe: {report.Issues.Count} issue(s) — see {CaveRouteProbeRunner.ReportPath}");
            }
        }

        static void RunSurfaceRouteProbe(Transform caveRoot)
        {
            // Do not run CavePlaytestPreBuildPipeline here — it executes all 60 PlaytestPolishPhaseRunner
            // phases synchronously (terrain smooth, props, meat snapshot, etc.) and freezes the editor at
            // pipeline stage 22. Full polish remains on Play Mode bot schedule (CavePlaytestRouteBotBridge).

            var report = SurfaceRouteProbeRunner.Run(caveRoot, lightweightBuildProbe: true);
            SurfaceRouteProbeRunner.Export(report);
            if (report.Passed)
            {
                Debug.Log("[CaveBuild] Surface route bot: PASS (trails → mouth).");
                return;
            }

            var messages = new List<string>();
            foreach (var issue in report.Issues)
                messages.Add(issue);
            CaveLiveCodegenRequest.Write(caveRoot, messages, "surface_route_probe_build");
            Debug.LogWarning(
                $"[CaveBuild] Surface route bot: {report.Issues.Count} issue(s) — see {SurfaceRouteProbeRunner.ReportPath}");
        }

        static void RunRouteProbe(Transform caveRoot, WorldGenerationRequest request)
        {
            var report = CaveRouteProbeRunner.Run(caveRoot);
            CaveRouteProbeRunner.Export(report, caveRoot);

            if (report.Passed)
            {
                Debug.Log("[CaveBuild] Route probe bot: PASS.");
                return;
            }

            var messages = new List<string>();
            foreach (var issue in report.Issues)
                messages.Add($"[{issue.SuggestedStageId}] {issue.Message}");

            CaveLiveCodegenRequest.Write(caveRoot, messages, "route_probe_build");

            CaveInvisibleColliderUtility.StripForAdventure(caveRoot);

            if (request == null)
                return;

            foreach (var issue in report.Issues)
            {
                if (string.IsNullOrEmpty(issue.SuggestedStageId))
                    continue;
                if (!CaveBuildQualityStageFixer.CanAutoFix(issue.SuggestedStageId))
                    continue;
                if (CaveBuildQualityStageFixer.TryFix(
                        issue.SuggestedStageId,
                        caveRoot,
                        request,
                        default,
                        request.Seed,
                        out var action))
                {
                    Debug.Log($"[CaveBuild] Targeted fix ({issue.SuggestedStageId}): {action}");
                }
            }
        }

        static void RunVisualShellCheck(Transform caveRoot, WorldGenerationRequest request)
        {
            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
                return;

            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            audit.CollectIssues(compactRoute: true, layoutPrototype: false);
            var score = audit.ComputeScore(compactRoute: true, layoutPrototype: false);
            if (score >= CaveBuildQualityRubric.StagePassScore && audit.Issues.Count == 0)
            {
                Debug.Log($"[CaveBuild] Visual shell check: PASS ({score}).");
                return;
            }

            Debug.LogWarning($"[CaveBuild] Visual shell check: {score} — {audit.Issues.Count} issue(s).");
            if (!CaveBuildWorkflowCoordinator.ShouldPreserveWalkways)
                CaveBuildWorkflowCoordinator.MarkWalkFloorsCommitted();

            CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);

            if (request != null &&
                CaveBuildQualityStageFixer.TryFix("visual_shell", caveRoot, request, default, request.Seed, out var action))
            {
                Debug.Log("[CaveBuild] Targeted visual_shell fix: " + action);
            }
        }

        static void RunGeometryIntegrityCheck(Transform caveRoot, WorldGenerationRequest request)
        {
            var before = CavePlayabilityValidator.CountInvisibleSolidColliders(caveRoot);
            var stripped = CaveInvisibleColliderUtility.StripForAdventure(caveRoot);
            var after = CavePlayabilityValidator.CountInvisibleSolidColliders(caveRoot);

            if (after == 0 && before == 0)
            {
                Debug.Log("[CaveBuild] Geometry integrity: PASS (no invisible solids).");
                return;
            }

            Debug.LogWarning(
                $"[CaveBuild] Geometry integrity: invisible solids before={before}, stripped={stripped}, after={after}.");

            if (after > 0 && request != null &&
                CaveBuildQualityStageFixer.TryFix(
                    "geometry_integrity",
                    caveRoot,
                    request,
                    default,
                    request.Seed,
                    out var action))
            {
                Debug.Log("[CaveBuild] Targeted geometry_integrity fix: " + action);
            }

            CaveAdventurePlayabilityPipeline.RunStep(10, caveRoot, request, default);
        }

        public static void RunSurfaceEntranceCheckPublic(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request) =>
            RunSurfaceEntranceCheck(caveRoot, ground, request);

        static void RunSurfaceEntranceCheck(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (caveRoot == null || request == null)
                return;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var seed = meta != null ? meta.seed : request.Seed;
            var segments = meta != null ? meta.tunnelSegments : Mathf.Max(6, request.CaveTunnelSegments);
            var chambers = meta != null ? meta.chamberCount : Mathf.Max(2, request.CaveChamberCount);
            var layout = CaveMazeLayoutGenerator.Generate(seed, segments, chambers);
            if (layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
            {
                Debug.LogWarning("[CaveBuild] Surface walk-in skipped — maze layout has no solution path.");
                return;
            }

            var geometry = CaveAdventureCaveGenerator.EnsureGeometryRoot(caveRoot);
            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

            CaveEntranceVolumeBuilder.StripEntranceOnionSlabs(caveRoot, ground);

            var needsBuild = !CaveSurfaceEntranceBuilder.HasDescentWalk(caveRoot);
            if (!needsBuild && CaveSurfaceEntranceBuilder.ValidateMouthOnGround(caveRoot, ground, out _))
            {
                var onion = SurfacePlaytestValidator.Run(caveRoot).EntranceOnionSlabCount;
                if (onion < 3)
                {
                    Debug.Log("[CaveBuild] Surface walk-in entrance: PASS.");
                    return;
                }

                Debug.LogWarning($"[CaveBuild] Entrance has {onion} stacked slab(s) — rebuilding professional mouth.");
            }

            var built = CaveSurfaceEntranceBuilder.Build(
                caveRoot, geometry, layout, floorMat, rockMat, ground, null, request.Seed);
            Debug.Log($"[CaveBuild] Surface walk-in entrance built/refreshed ({built} piece(s)).");

            if (!CaveBuildWorkflowCoordinator.IsGroundPlacementLocked && ground != null && ground.HasAnchor)
            {
                CaveGroundPlacementUtility.FinalizeGroundPlacement(
                    caveRoot, ground, out var msg, request.Seed);
                if (!string.IsNullOrEmpty(msg))
                    Debug.Log("[CaveBuild] Surface entrance grounding: " + msg);
                CaveBuildWorkflowCoordinator.TryAutoLockIfPlacementReady(caveRoot, ground);
            }

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
            {
                var spline = new CaveSplinePath();
                spline.SetKnots(layout.PathKnots);
                SplineCaveSpawnAligner.AlignEntranceSpawn(
                    caveRoot, entrance, spline, keepAtSurfaceMouth: true, mazeLayout: null);
            }

            if (!CaveSurfaceEntranceBuilder.ValidateMouthOnGround(caveRoot, ground, out var issue))
                Debug.LogWarning("[CaveBuild] Surface entrance: " + issue);
        }

        static void RunCombatCheck(Transform caveRoot, WorldGenerationRequest request)
        {
            CaveCombatSetupUtility.WireSceneCombat(caveRoot);

            var spawners = caveRoot.GetComponentsInChildren<CaveMobSpawner>(true);
            if (spawners.Length < 3)
            {
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                if (meta != null)
                {
                    var layout = CaveMazeLayoutGenerator.Generate(
                        meta.seed, meta.tunnelSegments, meta.chamberCount);
                    CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
                }
            }

            var report = CaveCombatProbeRunner.Run(caveRoot);
            CaveCombatProbeRunner.Export(report, caveRoot);

            if (report.Passed)
            {
                Debug.Log(
                    "[CaveBuild] Combat probe: PASS — spawners, player attack/defend, enemy damage sim OK.");
                return;
            }

            Debug.LogWarning(
                $"[CaveBuild] Combat probe: {report.Issues.Count} issue(s) — see {CaveCombatProbeRunner.ReportPath}");

            if (request == null)
                return;

            foreach (var issue in report.Issues)
            {
                if (string.IsNullOrEmpty(issue.SuggestedStageId))
                    continue;
                if (!CaveBuildQualityStageFixer.CanAutoFix(issue.SuggestedStageId))
                    continue;
                if (CaveBuildQualityStageFixer.TryFix(
                        issue.SuggestedStageId,
                        caveRoot,
                        request,
                        default,
                        request.Seed,
                        out var action))
                {
                    Debug.Log($"[CaveBuild] Targeted fix ({issue.SuggestedStageId}): {action}");
                }
            }

            report = CaveCombatProbeRunner.Run(caveRoot);
            CaveCombatProbeRunner.Export(report, caveRoot);
        }
    }
}
#endif
