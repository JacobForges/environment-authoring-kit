#if UNITY_EDITOR
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Thirty post-geometry phases: third-person headroom, shell, playability, QA.</summary>
    public static class CaveGenerationQualityPhaseRunner
    {
        public const int PhaseCount = 30;
        public const int QueuedStepBase = 2000;
        public const string LogRel = CaveBuildAgentContextExporter.Folder + "/CaveGenerationQualityPhaseLog.json";

        public static readonly string[] PhaseIds =
        {
            "gen_01_tps_layout_resync",
            "gen_02_route_ceiling_rebuild",
            "gen_03_headroom_intruder_clear",
            "gen_04_visual_shell_repair",
            "gen_05_single_ceiling_enforce",
            "gen_06_walkway_mark",
            "gen_07_spawn_pad",
            "gen_08_floor_collision",
            "gen_09_block_tunnel_audit",
            "gen_10_invisible_collider_strip",
            "gen_11_cavern_finish_open",
            "gen_12_material_repair",
            "gen_13_adventure_lighting",
            "gen_14_atmosphere_fog",
            "gen_15_navmesh_bake",
            "gen_16_route_headroom_probe",
            "gen_17_cave_route_export",
            "gen_18_compact_layer_purge",
            "gen_19_onion_shell_purge",
            "gen_20_platform_gap_audit",
            "gen_21_mob_spawner_spacing",
            "gen_22_water_pool_safety",
            "gen_23_performance_budget",
            "gen_24_adventure_visual_pass",
            "gen_25_ground_xz_lock",
            "gen_26_mouth_depth_snap",
            "gen_27_navmesh_rebake_force",
            "gen_28_playable_world_gate",
            "gen_29_quality_grade_export",
            "gen_30_generation_complete",
        };

        public static bool RunAll(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string summary)
        {
            return RunRange(caveRoot, ground, request, 0, PhaseCount, out summary);
        }

        public static bool RunRange(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            int startInclusive,
            int count,
            out string summary)
        {
            summary = string.Empty;
            if (caveRoot == null || request == null)
            {
                summary = "Cave gen phases skipped — no cave.";
                return false;
            }

            var end = Mathf.Min(startInclusive + count, PhaseCount);
            var log = new StringBuilder();
            log.AppendLine("{");
            log.AppendLine($"  \"generatedUtc\": \"{System.DateTime.UtcNow:o}\",");
            log.AppendLine("  \"phases\": [");

            var ok = true;
            var passed = 0;
            for (var i = startInclusive; i < end; i++)
            {
                var step = QueuedStepBase + i;
                CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(step, request, out var gateMsg);
                CaveBuildPhasePromptBridge.ExportResearchActionPlan(PhaseIds[i], step, request.Seed, out _);

                var phaseOk = RunPhase(i, caveRoot, ground, request, out var phaseMsg);
                if (phaseOk)
                    passed++;
                ok &= phaseOk;
                log.AppendLine("    {");
                log.AppendLine($"      \"id\": \"{PhaseIds[i]}\",");
                log.AppendLine($"      \"passed\": {(phaseOk ? "true" : "false")},");
                log.AppendLine($"      \"message\": \"{Escape(phaseMsg)}\",");
                log.AppendLine($"      \"gate\": \"{Escape(gateMsg)}\"");
                log.AppendLine(i < end - 1 ? "    }," : "    }");
            }

            log.AppendLine("  ],");
            log.AppendLine($"  \"passedCount\": {passed},");
            log.AppendLine($"  \"range\": \"{startInclusive}-{end - 1}\"");
            log.AppendLine("}");
            AppendLog(log.ToString());

            summary = $"Cave gen quality {passed}/{end - startInclusive} OK (TPS headroom pipeline).";
            if (!ok)
                Debug.LogWarning("[CaveBuild] " + summary);
            else
                Debug.Log("[CaveBuild] " + summary);
            return ok;
        }

        static bool RunPhase(
            int index,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string message)
        {
            message = string.Empty;
            if (!TryLayout(caveRoot, request, out var layout))
            {
                message = "No layout metadata.";
                return index > 5;
            }

            switch (index)
            {
                case 0:
                    message =
                        $"TPS layout H={layout.CorridorHeight:F1}m ceiling={layout.CeilingClearanceAboveFloor:F1}m walk={CaveMazeLayout.MinWalkClearanceMeters:F1}m.";
                    return layout.CeilingClearanceAboveFloor >= CaveThirdPersonClearance.ResolveMinCeilingAboveFloor() * 0.9f;
                case 1:
                    return RebuildRouteShell(caveRoot, ground, request, layout, out message);
                case 2:
                    var cleared = CaveVisualShellRouteRepair.ClearRouteHeadroomIntruders(caveRoot, layout);
                    message = $"Cleared {cleared} headroom intruder(s).";
                    return true;
                case 3:
                    if (CaveVisualShellRouteRepair.TryRepair(caveRoot, ground, request, out message))
                        return true;
                    message = "Visual shell OK or skipped.";
                    return true;
                case 4:
                    return EnsureCeiling(caveRoot, layout, out message);
                case 5:
                    return CaveBuildQualityStageFixer.TryFix(
                        "walkways", caveRoot, request, ground, request.Seed, out message);
                case 6:
                    return CaveBuildQualityStageFixer.TryFix(
                        "spawn_reachability", caveRoot, request, ground, request.Seed, out message);
                case 7:
                    return CaveBuildQualityStageFixer.TryFix(
                        "player_floor", caveRoot, request, ground, request.Seed, out message);
                case 8:
                    return CaveBuildQualityStageFixer.TryFix(
                        "block_tunnel", caveRoot, request, ground, request.Seed, out message);
                case 9:
                    return CaveBuildQualityStageFixer.TryFix(
                        "geometry_integrity", caveRoot, request, ground, request.Seed, out message);
                case 10:
                    layout.CavernRadiusCells = Mathf.Max(layout.CavernRadiusCells, 2);
                    message = $"Cavern radius cells={layout.CavernRadiusCells}.";
                    return true;
                case 11:
                    CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                    message = "Materials repaired.";
                    return true;
                case 12:
                    var lights = CaveAdventureCaveLighting.Apply(caveRoot, layout);
                    message = $"Lighting — {lights} light(s).";
                    return true;
                case 13:
                    return CaveBuildQualityStageFixer.TryFix(
                        "atmosphere", caveRoot, request, ground, request.Seed, out message);
                case 14:
                    LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, force: true);
                    message = "NavMesh baked.";
                    return true;
                case 15:
                    var low = CountLowHeadroomCells(layout);
                    message = $"Route headroom audit — {low} low cell(s).";
                    return low <= Mathf.Max(2, layout.SolutionPath.Count / 8);
                case 16:
                    CaveRouteProbeRunner.ExportAndNotifyCursor(caveRoot, invokeAgent: false);
                    message = "Cave route probe exported.";
                    return true;
                case 17:
                    var purged = CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
                    message = $"Compact purge {purged} shell layer(s).";
                    return true;
                case 18:
                    var onion = CaveEnclosureShellBuilder.PurgeLayerOffenders(
                        caveRoot.Find(CaveGeometryPaths.GeometryRoot));
                    message = $"Onion purge {onion} piece(s).";
                    return true;
                case 19:
                    var gaps = layout.JumpGapCells?.Count ?? 0;
                    message = $"Jump gaps on route: {gaps}.";
                    return gaps <= 8;
                case 20:
                    CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
                    message = "Mob spawners along route.";
                    return true;
                case 21:
                    return CaveBuildQualityStageFixer.TryFix("water", caveRoot, request, ground, request.Seed, out message);
                case 22:
                    var perf = CavePerformanceBudget.Apply(caveRoot);
                    message = $"Performance budget — {perf.RenderersDisabled} renderer(s) trimmed.";
                    return true;
                case 23:
                    CaveAdventureVisualPass.Apply(caveRoot);
                    message = "Adventure visual pass.";
                    return true;
                case 24:
                    CaveBuildWorkflowCoordinator.LockGroundPlacement(caveRoot);
                    message = "Cave XZ locked.";
                    return true;
                case 25:
                    return CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, out message);
                case 26:
                    LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, force: true);
                    message = "NavMesh force rebake.";
                    return true;
                case 27:
                    return PlayableWorldGate.EvaluateAndWrite(ground, request, 16, out message);
                case 28:
                    var q = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
                    CaveBuildQualityReportWriter.Write(q, gradingMode: "cave_generation_tps");
                    message = $"Quality {q.OverallScore} ({q.LetterGrade}).";
                    return q.OverallScore >= 80;
                case 29:
                    message =
                        $"Generation TPS complete — clearance {layout.CeilingClearanceAboveFloor:F0}m, corridor {layout.CorridorHeight:F1}m.";
                    return true;
                default:
                    return false;
            }
        }

        static bool TryLayout(Transform caveRoot, WorldGenerationRequest request, out CaveMazeLayout layout)
        {
            layout = null;
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return false;

            layout = CaveThirdPersonLayoutUtility.GenerateForCave(
                meta,
                request != null && request.UseLayoutPrototype);
            return layout != null;
        }

        static bool RebuildRouteShell(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            CaveMazeLayout layout,
            out string message)
        {
            message = string.Empty;
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
            {
                message = "No geometry root.";
                return false;
            }

            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (floorMat == null || rockMat == null)
            {
                message = "Missing cave materials.";
                return false;
            }

            CaveEnclosureShellBuilder.PurgeLayerOffenders(geometry);
            var n = CaveEnclosureShellBuilder.Build(geometry, layout, floorMat, rockMat, request.Seed);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            message = $"Rebuilt route floor+ceiling ({n} surface(s)) for TPS headroom.";
            return n > 0;
        }

        static bool EnsureCeiling(Transform caveRoot, CaveMazeLayout layout, out string message)
        {
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (geometry == null || rockMat == null)
            {
                message = "Ceiling skip — no geometry/material.";
                return true;
            }

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var n = CaveEnclosureShellBuilder.EnsureSingleCeiling(
                geometry, layout, rockMat, meta != null ? meta.seed : 0);
            message = $"Single route ceiling — {n} mesh(es).";
            return true;
        }

        static int CountLowHeadroomCells(CaveMazeLayout layout)
        {
            if (layout?.SolutionPath == null)
                return 0;

            var min = CaveMazeLayout.MinWalkClearanceMeters;
            var low = 0;
            foreach (var cell in layout.SolutionPath)
            {
                if (layout.GetCeilingClearanceAt(cell.x, cell.y) < min * 1.05f)
                    low++;
            }

            return low;
        }

        static void AppendLog(string json)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, LogRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            if (File.Exists(path))
                File.AppendAllText(path, "\n" + json);
            else
                File.WriteAllText(path, json);
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
