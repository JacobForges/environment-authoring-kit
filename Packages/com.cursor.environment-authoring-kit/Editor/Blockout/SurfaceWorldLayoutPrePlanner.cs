#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Research-backed layout plan (JSON only) before LiDAR stamp / terrain sculpt — no heightmap writes.
    /// </summary>
    public static class SurfaceWorldLayoutPrePlanner
    {
        public const string PlanFileName = "SurfaceWorldLayoutPrePlan.json";

        public static void QueueWritePlan(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Vector3 center,
            float extent,
            Action onComplete)
        {
            CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
            {
                CaveBuildRunStatusPublisher.SetSubOperation("FullWorld grid", "layout pre-plan (JSON only)");
                TryWritePlan(ground, request, center, extent, out var msg);
                CaveBuildEditorLog.LogSurface("[Surface] Pre-plan — " + msg, forceUnityConsole: true);
                onComplete?.Invoke();
            });
        }

        public static bool TryWritePlan(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Vector3 center,
            float extent,
            out string message)
        {
            message = string.Empty;
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var path = Path.Combine(CaveBuildAgentContextExporter.Folder, PlanFileName);
            var seed = request?.Seed ?? 0;
            var fullWorld = request?.SurfaceScope == SurfaceBuildScope.FullWorld;
            var nineTile = request != null &&
                           SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(request, fullWorld);
            var demAuthoritative = request != null && fullWorld;
            var skipPostDemSculpt = demAuthoritative;

            var sb = new StringBuilder(2048);
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"seed\": {seed},");
            sb.AppendLine($"  \"generationStyleId\": \"{request?.GenerationStyleId ?? string.Empty}\",");
            sb.AppendLine($"  \"surfaceScope\": \"{request?.SurfaceScope}\",");
            if (request != null && request.ConceptLayoutIndex == 0 && fullWorld)
            {
                var conceptRel = FullWorldConceptLayoutCatalog.GetConceptImageRel(0);
                sb.AppendLine("  \"idealLayoutTarget\": {");
                sb.AppendLine($"    \"conceptIndex\": 0,");
                sb.AppendLine($"    \"conceptImageRel\": \"{conceptRel}\",");
                sb.AppendLine("    \"conceptImagesRootRel\": \"Assets/EnvironmentKit/ResearchCache/images/concepts\",");
                sb.AppendLine("    \"researchCategory\": \"fullworld_layout_ideal\",");
                sb.AppendLine(
                    "    \"tileCounts\": { \"playDisk\": 9, \"mixedTransition\": 352, \"conceptPresets\": 864, \"presetEach\": \"86-87\" },");
                sb.AppendLine("    \"requireMountainLabyrinth\": true,");
                sb.AppendLine("    \"requireWildernessCaveMouth\": true,");
                sb.AppendLine("    \"requirePrimaryMouthSnap\": true,");
                sb.AppendLine("    \"similarityRule\": \"Same silhouette family as concept — labyrinth grid and mouth placement vary by buildSeed.\"");
                sb.AppendLine("  },");
            }

            sb.AppendLine($"  \"center\": [{center.x:F2}, {center.y:F2}, {center.z:F2}],");
            sb.AppendLine($"  \"extentMeters\": {extent:F1},");
            sb.AppendLine($"  \"nineTileSquare\": {(nineTile ? "true" : "false")},");
            if (fullWorld && request.UseExtendedOpenWorldGrid)
            {
                var slotCount = SurfaceOpenWorldGridExpansion.BuildAaaExtendedPlaceOrder().Length;
                sb.AppendLine("  \"openWorldGrid\": {");
                sb.AppendLine($"    \"targetTileCount\": {SurfaceOpenWorldGridExpansion.TargetTileCount},");
                sb.AppendLine($"    \"plannedSlotCount\": {slotCount},");
                sb.AppendLine($"    \"chebyshevRadius\": {SurfaceOpenWorldGridExpansion.MaxChebyshevRadius},");
                sb.AppendLine("    \"layout\": \"17x17 edge-to-edge flat grid — play disk center, rings 1–8 outward\"");
                sb.AppendLine("  },");
            }
            sb.AppendLine($"  \"skipPostDemCreativeSculpt\": {(skipPostDemSculpt ? "true" : "false")},");
            sb.AppendLine($"  \"skipPostDemSculptReason\": \"LiDAR authoritative heightfield — macro shape via outer ring + terrain phases (wlp-01,wlp-33,wlp-37)\",");
            sb.AppendLine("  \"mountainRing\": {");
            sb.AppendLine($"    \"cornerPeakRiseMeters\": {SurfaceOuterRingMountainsAuthor.CornerPeakRiseMeters:F1},");
            sb.AppendLine($"    \"edgePeakRiseMeters\": {SurfaceOuterRingMountainsAuthor.EdgePeakRiseMeters:F1},");
            sb.AppendLine($"    \"outerBandMeters\": {SurfaceOuterRingMountainsAuthor.OuterBandMeters:F1}");
            sb.AppendLine("  },");
            sb.AppendLine("  \"mouthBeforeProps\": true,");
            sb.AppendLine("  \"researchDoc\": \"docs/RESEARCH_WORLD_LAYOUT_PLACEMENT.md\",");
            sb.AppendLine("  \"researchCatalog\": \"Tools/cave-grader/research-catalog.seed.json\",");
            sb.AppendLine("  \"researchIds\": [");
            sb.AppendLine("    \"wlp-01-farcry-gdc-toolchain\",");
            sb.AppendLine("    \"wlp-33-sidefx-heightfield-creation\",");
            sb.AppendLine("    \"wlp-37-unity-setheights\",");
            sb.AppendLine("    \"wlp-38-unity-support-setheights-slow\",");
            sb.AppendLine("    \"wlp-31-lynch-image-of-city\",");
            sb.AppendLine("    \"wlp-35-kempke-game-world-size\"");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"layoutRandomness\": {");
            sb.AppendLine($"    \"buildSeed\": {seed},");
            sb.AppendLine("    \"caveMazeFlavorRoll\": \"CaveLayoutRoll — organic / vertical / sparse / walkway labyrinth\",");
            sb.AppendLine("    \"mountainLabyrinthGrid\": \"SurfaceMountainLabyrinthLayout.Generate(seed) — cell size + entrance vary per seed\",");
            sb.AppendLine("    \"fullWorldTileOrder\": \"ring-by-ring place+seam — deterministic from seed via TileDemSeed per offset\",");
            sb.AppendLine("    \"agentRule\": \"Do not hard-code a single layout — honor buildSeed so each run differs (maze flavor, labyrinth footprint, prop scatter).\"");
            sb.AppendLine("  },");
            sb.AppendLine("  \"pipelineOrder\": [");
            sb.AppendLine("    \"pre_placement_research_gate\",");
            sb.AppendLine("    \"layout_pre_plan_json\",");
            sb.AppendLine("    \"florida_lidar_dem_stamp\",");
            sb.AppendLine("    \"skip_post_dem_sculpt_when_authoritative\",");
            sb.AppendLine("    \"nine_tile_neighbors\",");
            sb.AppendLine("    \"extended_open_world_flat_grid\",");
            sb.AppendLine("    \"outer_ring_mountains\",");
            sb.AppendLine("    \"cave_openings_and_mouth_terrain\",");
            sb.AppendLine("    \"trails_roads_water\",");
            sb.AppendLine("    \"terrain_ai_phases\",");
            sb.AppendLine("    \"mouth_refresh_pre_props\",");
            sb.AppendLine("    \"surface_props\",");
            sb.AppendLine("    \"pre_build_gate\",");
            sb.AppendLine("    \"cave_geometry\"");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"agentPromptHint\": \"Read SurfaceWorldLayoutPrePlan.json + RESEARCH_WORLD_LAYOUT_PLACEMENT.md before editing terrain heightmaps. Honor layoutRandomness.buildSeed — each build should differ (maze flavor, mountain labyrinth grid, prop scatter). Do not add post-DEM sculpt passes when skipPostDemCreativeSculpt is true.\"");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            message = $"Wrote {path} (skipPostDemSculpt={skipPostDemSculpt}, nineTile={nineTile}).";
            return true;
        }
    }
}
#endif
