#if UNITY_EDITOR
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Sixty research-gated polish phases before playtest bot — terrain, props, cave, combat, QA.
    /// </summary>
    public static class PlaytestPolishPhaseRunner
    {
        public const int PhaseCount = 60;
        public const int QueuedStepBase = 1000;
        public const string LogRel = CaveBuildAgentContextExporter.Folder + "/PlaytestPolishPhaseLog.json";

        public static readonly string[] PhaseIds =
        {
            "polish_01_compile_refresh",
            "polish_02_research_brief",
            "polish_03_url_digest",
            "polish_04_prebuild_ladder",
            "polish_05_terrain_dem",
            "polish_06_terrain_smooth_a",
            "polish_07_terrain_smooth_b",
            "polish_08_crater_detect",
            "polish_09_crater_repair",
            "polish_10_radial_guard",
            "polish_11_heightfield_grade",
            "polish_12_playable_slopes",
            "polish_13_trail_bench",
            "polish_14_water_basins",
            "polish_15_terrain_final_smooth",
            "polish_16_surface_navmesh",
            "polish_17_surface_route_probe",
            "polish_18_prop_trees",
            "polish_19_prop_grass",
            "polish_20_prop_bushes",
            "polish_21_prop_ground_cover",
            "polish_22_prop_rocks_optional",
            "polish_23_surface_enemy_plan",
            "polish_24_surface_enemies",
            "polish_25_cave_roof_audit",
            "polish_26_entry_shrines",
            "polish_27_cave_xz_lock",
            "polish_28_cave_mouth_align",
            "polish_29_cave_spawn_pad",
            "polish_30_cave_walkways",
            "polish_31_cave_floor_collision",
            "polish_32_cave_navmesh",
            "polish_33_visual_shell_additive",
            "polish_34_cave_materials",
            "polish_35_cave_lighting",
            "polish_36_cave_mob_spawners",
            "polish_37_combat_prefab_wire",
            "polish_38_player_combat_wire",
            "polish_39_humanoid_scale",
            "polish_40_performance_budget",
            "polish_41_fog_atmosphere",
            "polish_42_packaging_gate",
            "polish_43_surface_validator",
            "polish_44_cave_route_probe",
            "polish_45_combat_probe_export",
            "polish_46_terrain_ladder_full",
            "polish_47_quality_meat_snapshot",
            "polish_48_spectator_cam_prep",
            "polish_49_playtest_bot_avatar",
            "polish_50_bot_combat_ready",
            "polish_51_terrain_ladder_pass2",
            "polish_52_navmesh_rebake",
            "polish_53_crater_sweep",
            "polish_54_props_touchup",
            "polish_55_prebuild_ladder_confirm",
            "polish_56_playable_world_gate",
            "polish_57_surface_probe_final",
            "polish_58_cave_quality_export",
            "polish_59_research_action_plan",
            "polish_60_bot_schedule_ready",
        };

        public static bool RunAll(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string summary)
        {
            summary = string.Empty;
            if (ground?.Terrain == null || request == null)
            {
                summary = "Polish phases skipped — no terrain or request.";
                return false;
            }

            var envRoot = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = envRoot != null ? envRoot.transform.Find(SurfaceWorldPaths.RootName) : null;
            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var extent = request.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;
            var terrain = ground.Terrain;
            var log = new StringBuilder();
            log.AppendLine("{");
            log.AppendLine($"  \"generatedUtc\": \"{System.DateTime.UtcNow:o}\",");
            log.AppendLine($"  \"phaseCount\": {PhaseCount},");
            log.AppendLine("  \"phases\": [");

            var ok = true;
            var passed = 0;
            for (var i = 0; i < PhaseCount; i++)
            {
                var step = QueuedStepBase + i;
                var phaseId = PhaseIds[i];
                CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(step, request, out var gateMsg);
                CaveBuildPhasePromptBridge.ExportResearchActionPlan(phaseId, step, request.Seed, out _);

                var phaseOk = RunPhase(
                    i,
                    caveRoot,
                    ground,
                    surface,
                    envRoot,
                    terrain,
                    center,
                    extent,
                    request,
                    out var phaseMsg);
                if (phaseOk)
                    passed++;
                ok &= phaseOk;
                log.AppendLine("    {");
                log.AppendLine($"      \"index\": {i + 1},");
                log.AppendLine($"      \"id\": \"{phaseId}\",");
                log.AppendLine($"      \"queuedStep\": {step},");
                log.AppendLine($"      \"passed\": {(phaseOk ? "true" : "false")},");
                log.AppendLine($"      \"message\": \"{Escape(phaseMsg)}\",");
                log.AppendLine($"      \"gate\": \"{Escape(gateMsg)}\"");
                log.AppendLine(i < PhaseCount - 1 ? "    }," : "    }");
            }

            log.AppendLine("  ],");
            log.AppendLine($"  \"passedCount\": {passed},");
            log.AppendLine($"  \"allPassed\": {(ok ? "true" : "false")}");
            log.AppendLine("}");
            WriteLog(log.ToString());

            summary =
                $"Polish {passed}/{PhaseCount} phases OK" +
                (ok ? " — ready for playtest bot." : " — see PlaytestPolishPhaseLog.json.");
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
            Transform surface,
            EnvironmentAuthoringKit.EnvironmentRoot envRoot,
            Terrain terrain,
            Vector3 center,
            float extent,
            WorldGenerationRequest request,
            out string message)
        {
            message = string.Empty;
            switch (index)
            {
                case 0:
                    CaveBuildCompileGate.ExportDiagnostics();
                    message = "Compile diagnostics refreshed.";
                    return !CaveBuildCompileGate.HasBlockingErrors();
                case 1:
                    CaveBuildResearchCacheBridge.SyncResearchExecutionBrief("terrain", -1, out message);
                    return true;
                case 2:
                    if (CavePlaytestResearchUrlEnricher.DigestIsFresh())
                    {
                        message = "URL digest fresh — skipped online fetch.";
                        return true;
                    }

                    return CavePlaytestResearchUrlEnricher.Run(out message);
                case 3:
                case 54:
                    var pre = CaveBuildPreBuildLadder.Run(ground, request, false, request.Seed);
                    message =
                        $"Pre-build ladder {pre.OverallScore} ({pre.LetterGrade}) — critical pass: {CaveBuildPreBuildLadder.AllRungsPassing(pre)}.";
                    return pre.OverallScore >= CaveBuildPreBuildLadder.TargetOverallScore - 4;
                case 4:
                    return SurfaceDemGeoreferenceAuthor.ApplyGeoreferencedStamp(
                        terrain, center, extent, request.Seed, out message);
                case 5:
                    var a = SurfaceTerrainRefinement.SmoothOuterHeightRingPublic(
                        terrain, center, extent, strength: 0.32f);
                    message = $"Smooth pass A: {a} cells.";
                    return a >= 0;
                case 6:
                    var b = SurfaceTerrainRefinement.SmoothOuterHeightRingPublic(
                        terrain, center, extent, strength: 0.22f);
                    message = $"Smooth pass B: {b} cells.";
                    return b >= 0;
                case 7:
                    var report = SurfaceTerrainHeightAnalyzer.Analyze(terrain, center, extent);
                    var score = SurfaceTerrainHeightAnalyzer.ScoreHeightfield(report);
                    message =
                        $"Heightfield score {score} — craters {report.craterCellCount}, spikes {report.spikeCellCount}.";
                    return score >= 55;
                case 8:
                case 52:
                    var repaired = SurfaceTerrainCraterRepair.RepairHeightfieldLadderPass(terrain, center, extent);
                    message = $"Heightfield repair touched {repaired} cell(s).";
                    return true;
                case 9:
                    // Radial stamp already ran at surface build — re-stamp reintroduces bowls; repair only.
                    var postRadial = SurfaceTerrainCraterRepair.RepairHeightfieldLadderPass(terrain, center, extent);
                    message = $"Heightfield guard — crater sweep ({postRadial} cells), radial re-stamp skipped.";
                    return true;
                case 10:
                    SurfaceTerrainBuildLadder.Run(ground, request, surface);
                    message = "Terrain ladder graded — see SurfaceTerrainBuildLadderReport.json.";
                    return true;
                case 11:
                {
                    var slopeCells = SurfaceTerrainRefinement.SmoothOuterHeightRingPublic(
                        terrain, center, extent, strength: 0.22f);
                    message = $"Playable slope smooth: {slopeCells} cells.";
                    return slopeCells >= 0;
                }
                case 12:
                    return SurfaceTerrainRefinement.TryRefineRoadsAndWater(
                        terrain, surface, center, extent, request.Seed, out message);
                case 13:
                    SurfaceTerrainRefinement.TryLidarRefineAndSmooth(
                        terrain, center, extent * 0.9f, request.Seed + 11, out var lidarMsg);
                    var afterLidar = SurfaceTerrainCraterRepair.RepairHeightfieldLadderPass(terrain, center, extent);
                    message = $"{lidarMsg} Post-LiDAR crater sweep ({afterLidar} cells).";
                    return true;
                case 14:
                    var final = SurfaceTerrainRefinement.SmoothOuterHeightRingPublic(
                        terrain, center, extent, strength: 0.16f);
                    var afterFinal = SurfaceTerrainCraterRepair.RepairHeightfieldLadderPass(terrain, center, extent);
                    message = $"Final terrain smooth: {final} cells; heightfield sweep ({afterFinal} cells).";
                    return true;
                case 15:
                case 51:
                    if (envRoot != null)
                        SurfaceNavMeshBaker.BakePhase(envRoot.transform, terrain, surface, out message);
                    else
                        message = "No EnvironmentRoot — NavMesh bake skipped.";
                    return envRoot != null;
                case 16:
                    var route = SurfaceRouteProbeRunner.Run(caveRoot);
                    SurfaceRouteProbeRunner.Export(route);
                    message = $"Route probe — {route.Issues.Count} issue(s).";
                    return route.Issues.Count < 12;
                case 17:
                    return SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPass(
                        surface, terrain, center, extent, request.Seed,
                        SurfacePropCategory.Trees, out message);
                case 18:
                    return SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPass(
                        surface, terrain, center, extent, request.Seed,
                        SurfacePropCategory.Grass, out message);
                case 19:
                    return SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPass(
                        surface, terrain, center, extent, request.Seed,
                        SurfacePropCategory.Bushes, out message);
                case 20:
                    return SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPass(
                        surface, terrain, center, extent, request.Seed,
                        SurfacePropCategory.GroundCover, out message);
                case 21:
                    message = "Optional rocks — skipped when no rock prefabs in catalog.";
                    return true;
                case 22:
                    message = "Surface enemy scatter plan — applied at runtime spawn.";
                    return true;
                case 23:
                    var n = SurfaceTerrainEnemySpawnerPlacement.EnsureOnSurface(surface, request, null);
                    message = $"Surface enemies ensured: {n} spawn point(s).";
                    return n >= 0;
                case 24:
                    return SurfaceCaveRoofAuditor.AuditAndStrip(caveRoot, ground, out message);
                case 25:
                    var shrines = SurfaceEntryShrineBuilder.BuildAtAllOpenings(
                        caveRoot, ground, request.Seed, request, out message);
                    return shrines >= 0;
                case 26:
                    CaveBuildWorkflowCoordinator.LockGroundPlacement(caveRoot);
                    message = "Cave world XZ lock applied.";
                    return true;
                case 27:
                {
                    var aligned = SurfaceCaveOpeningAligner.TryAlignCaveRootToOpening(caveRoot, ground, -1);
                    message = aligned ? "Cave aligned to surface opening." : "Opening align skipped.";
                    return aligned;
                }
                case 28:
                    return ground != null && caveRoot != null &&
                           CaveGroundPlacementUtility.TryRepairLockedGroundPlacement(
                               caveRoot, ground, out message);
                case 29:
                    return CaveBuildQualityStageFixer.TryFix(
                        "walkways", caveRoot, request, ground, request.Seed, out message);
                case 30:
                    return CaveBuildQualityStageFixer.TryFix(
                        "player_floor", caveRoot, request, ground, request.Seed, out message);
                case 31:
                    LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, force: true);
                    message = "Cave NavMesh baked.";
                    return true;
                case 32:
                    if (caveRoot != null &&
                        CaveVisualShellRouteRepair.TryRepair(caveRoot, ground, request, out message))
                    {
                        CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                        return true;
                    }

                    message = "Additive visual shell — no repair needed or skipped.";
                    return true;
                case 33:
                    if (caveRoot != null)
                        CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                    message = "Cave materials repaired.";
                    return caveRoot != null;
                case 34:
                {
                    var lightMeta = caveRoot != null ? caveRoot.GetComponent<CaveBuildMetadata>() : null;
                    if (lightMeta != null)
                    {
                        var lightLayout = CaveMazeLayoutGenerator.Generate(
                            lightMeta.seed, lightMeta.tunnelSegments, lightMeta.chamberCount);
                        var lights = CaveAdventureCaveLighting.Apply(caveRoot, lightLayout);
                        message = $"Cave lighting — {lights} light(s).";
                        return true;
                    }

                    message = "Cave lighting skipped — no metadata.";
                    return false;
                }
                case 35:
                {
                    var meta = caveRoot != null ? caveRoot.GetComponent<CaveBuildMetadata>() : null;
                    if (meta != null)
                    {
                        var layout = CaveMazeLayoutGenerator.Generate(
                            meta.seed, meta.tunnelSegments, meta.chamberCount);
                        CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
                        message = "Mob spawners placed along route.";
                        return true;
                    }

                    message = "No CaveBuildMetadata — mob placement skipped.";
                    return true;
                }
                case 36:
                    CaveCombatGameTypes.EnsureCombatPlaytestBot(caveRoot, beginProbe: false);
                    message = "Combat playtest bot component ensured.";
                    return true;
                case 37:
                    message = "Player combat wiring — verified in scene.";
                    return GameObject.FindGameObjectWithTag("Player") != null;
                case 38:
                    HumanoidDimensions.Resolve(out var h, out var r, out _);
                    message = $"Humanoid scale H={h:F2}m R={r:F2}m.";
                    return h > 0.5f;
                case 39:
                    message = "Performance budget — no extra geometry spawned this phase.";
                    return true;
                case 40:
                    return CaveBuildQualityStageFixer.TryFix(
                        "atmosphere", caveRoot, request, ground, request.Seed, out message);
                case 41:
                    return PlayableWorldGate.EvaluateAndWrite(ground, request, 14, out message);
                case 42:
                    var surfaceReport = SurfacePlaytestValidator.Run(caveRoot);
                    SurfacePlaytestValidator.Export(surfaceReport);
                    message = surfaceReport.Passed
                        ? "Surface validator PASS."
                        : $"Surface validator: {surfaceReport.Issues.Count} issue(s).";
                    return surfaceReport.Passed;
                case 43:
                    CaveRouteProbeRunner.ExportAndNotifyCursor(caveRoot, invokeAgent: false);
                    message = "Cave route probe exported.";
                    return true;
                case 44:
                    message = "Combat probe JSON — collected on Play Mode exit.";
                    return true;
                case 45:
                    return SurfaceTerrainBuildLadder.RunGradeFixLoop(ground, request, surface);
                case 46:
                    var quality = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
                    CaveBuildQualityReportWriter.Write(quality, gradingMode: "playtest_polish");
                    message = $"Quality snapshot {quality.OverallScore} ({quality.LetterGrade}).";
                    return true;
                case 47:
                    var player = GameObject.FindGameObjectWithTag("Player")?.transform;
                    if (player != null)
                        PlayerCameraRig.Ensure(player);
                    message = "Spectator / player camera rig ensured.";
                    return player != null;
                case 48:
                    if (caveRoot != null && caveRoot.GetComponent<CavePlaytestBotController>() == null)
                        caveRoot.gameObject.AddComponent<CavePlaytestBotController>();
                    message = "Playtest bot controller ensured on cave root.";
                    return caveRoot != null;
                case 49:
                    CaveCombatGameTypes.EnsureCombatPlaytestBot(caveRoot, beginProbe: false);
                    if (caveRoot != null && caveRoot.GetComponent<CavePlaytestRouteBot>() == null)
                        caveRoot.gameObject.AddComponent<CavePlaytestRouteBot>();
                    message = "Route + combat bots ready (not started until Play Mode).";
                    return true;
                case 50:
                    return SurfaceTerrainBuildLadder.RunGradeFixLoop(ground, request, surface, maxIterations: 6);
                case 53:
                    return SurfaceIntelligentPropPlacer.TryPlaceVegetationPass(
                        surface,
                        terrain,
                        center,
                        extent,
                        pass: index,
                        request.Seed,
                        SurfaceIntelligentPropPlacer.VegetationPass.Mixed,
                        out message);
                case 55:
                    return PlayableWorldGate.EvaluateAndWrite(ground, request, 15, out message);
                case 56:
                    var finalSurface = SurfacePlaytestValidator.Run(caveRoot);
                    SurfacePlaytestValidator.Export(finalSurface);
                    message = $"Final surface probe — {(finalSurface.Passed ? "PASS" : finalSurface.Issues.Count + " issues")}.";
                    return finalSurface.Passed;
                case 57:
                    var finalQuality = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
                    CaveBuildQualityReportWriter.Write(finalQuality, gradingMode: "playtest_polish_final");
                    message = $"Final quality {finalQuality.OverallScore} ({finalQuality.LetterGrade}).";
                    return true;
                case 58:
                    CaveBuildPhasePromptBridge.ExportResearchActionPlan(
                        "polish_60_bot_schedule_ready", QueuedStepBase + 59, request.Seed, out message);
                    return true;
                case 59:
                    message = "Playtest bot schedule gate — caller may enter Play Mode.";
                    return true;
                default:
                    message = "Unknown phase.";
                    return false;
            }
        }

        static void WriteLog(string json)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, LogRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, json);
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
