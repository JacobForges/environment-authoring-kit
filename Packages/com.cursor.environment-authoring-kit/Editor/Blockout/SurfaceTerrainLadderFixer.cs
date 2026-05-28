#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Automated fixes for <see cref="SurfaceTerrainBuildLadder"/> rungs.</summary>
    public static class SurfaceTerrainLadderFixer
    {
        /// <summary>Paced ladder fix — heightfield/slopes upload row bands on surface terrains only.</summary>
        public static void QueueTryFix(
            string rungId,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Action<bool, string> onComplete)
        {
            if (onComplete == null)
                return;

            if (string.IsNullOrEmpty(rungId) || ground?.Terrain == null)
            {
                onComplete(false, string.Empty);
                return;
            }

            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var requestExtent = SurfaceTerrainPlayRegion.ResolveRequestExtentMeters(request);
            var repairExtent = SurfaceTerrainPlayRegion.ResolveRepairExtentMeters(
                ground.Terrain,
                center,
                request);

            switch (rungId)
            {
                case "heightfield_no_craters":
                    QueueFixCraters(ground, center, repairExtent, onComplete);
                    return;
                case "playable_slopes":
                    QueueFixSlopes(ground, center, requestExtent, onComplete);
                    return;
                case "trail_walkability":
                    QueueFixTrails(ground, request, surfaceRoot, onComplete);
                    return;
                default:
                    onComplete(TryFix(rungId, ground, request, surfaceRoot, out var action), action);
                    return;
            }
        }

        static void QueueFixTrails(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Action<bool, string> onComplete)
        {
            if (onComplete == null)
                return;

            var cave = CaveRouteProbeRunner.FindCaveRoot();
            SurfaceTrailWalkabilityRepair.QueueTryRepair(ground, request, surfaceRoot, cave, onComplete);
        }

        public static bool TryFix(
            string rungId,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            out string action)
        {
            action = string.Empty;
            if (string.IsNullOrEmpty(rungId))
                return false;

            var center = ground != null && ground.HasAnchor
                ? ground.Anchor.position
                : ground != null
                    ? new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z)
                    : Vector3.zero;
            var requestExtent = SurfaceTerrainPlayRegion.ResolveRequestExtentMeters(request);
            var repairExtent = ground?.Terrain != null
                ? SurfaceTerrainPlayRegion.ResolveRepairExtentMeters(ground.Terrain, center, request)
                : requestExtent;
            var seed = request?.Seed ?? 1;

            switch (rungId)
            {
                case "heightfield_no_craters":
                    return FixCraters(ground, center, repairExtent, out action);
                case "playable_slopes":
                    return FixSlopes(ground, center, requestExtent, out action);
                case "trail_walkability":
                    return FixTrails(ground, request, surfaceRoot, out action);
                case "surface_navmesh":
                    return FixNav(surfaceRoot, ground, out action);
                case "prop_trees":
                    return PlaceProps(surfaceRoot, ground, center, requestExtent, seed, SurfacePropCategory.Trees, out action);
                case "prop_grass":
                    return PlaceProps(surfaceRoot, ground, center, requestExtent, seed, SurfacePropCategory.Grass, out action);
                case "prop_bushes":
                    return PlaceProps(surfaceRoot, ground, center, requestExtent, seed, SurfacePropCategory.Bushes, out action);
                case "prop_ground_cover":
                    return PlaceProps(
                        surfaceRoot, ground, center, requestExtent, seed, SurfacePropCategory.GroundCover, out action);
                case "surface_playtest":
                    return FixSurfacePlaytest(surfaceRoot, ground, request, out action);
                case "cave_mouth_grounding":
                    return FixCaveMouth(ground, request, out action);
                default:
                    return false;
            }
        }

        static IEnumerable<Terrain> EnumerateSurfaceTerrains(Terrain main)
        {
            foreach (var terrain in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(main))
                yield return terrain;
        }

        const int MaxQueuedCraterPassesPerTile = 18;

        public static void QueueFixCraters(
            SceneGroundInfo ground,
            Vector3 center,
            float extent,
            Action<bool, string> onComplete)
        {
            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain);
            var totalCrater = 0;
            var tileIndex = 0;
            var multiTile = terrains.Count > 1;
            var stitched = multiTile
                ? SurfaceTerrainTileExpansion.StitchAllNeighborSeamsSync(ground.Terrain)
                : 0;

            void RunNextTile()
            {
                if (tileIndex >= terrains.Count)
                {
                    onComplete(
                        totalCrater > 0 || stitched > 0,
                        $"Seam-stitched {stitched} tile(s); repaired {totalCrater} heightfield cell operation(s) (all terrains).");
                    return;
                }

                var current = terrains[tileIndex];
                var currentIdx = tileIndex + 1;
                tileIndex++;

                CaveBuildActionPacing.ScheduleHeavyChain(
                    () =>
                    {
                        EditorUtility.DisplayProgressBar(
                            "Environment Kit",
                            $"[Surface] Terrain ladder — crater repair tile {currentIdx}/{terrains.Count}…",
                            0.56f + 0.02f * (currentIdx / (float)Mathf.Max(1, terrains.Count)));

                        if (current != null)
                        {
                            totalCrater += SurfaceTerrainCraterRepair.RepairHeightfieldPlayable(
                                current,
                                center,
                                extent,
                                maxPasses: MaxQueuedCraterPassesPerTile);
                            // Outer-ring smooth is playable_slopes — it reintroduces grader bowl/spike clusters here.
                            current.Flush();
                        }

                        CaveBuildActionPacing.ScheduleNextEditorFrame(RunNextTile);
                    },
                    CaveBuildPipelineDomains.SurfaceQueueLabel(
                        $"terrain ladder craters tile {currentIdx}/{terrains.Count}"));
            }

            RunNextTile();
        }

        static void QueueFixSlopes(
            SceneGroundInfo ground,
            Vector3 center,
            float extent,
            Action<bool, string> onComplete)
        {
            CaveBuildActionPacing.ScheduleHeavyChain(
                () =>
                {
                    var totalCells = 0;
                    foreach (var terrain in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain))
                    {
                        if (terrain == null)
                            continue;
                        totalCells += ApplyPlayableSlopeOuterSmooth(terrain, center, extent);
                        terrain.Flush();
                    }

                    onComplete(
                        totalCells > 0,
                        $"Outer-ring smoothed {totalCells} cells for playable slopes (all terrains).");
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel("terrain ladder slopes all"));
        }

        /// <summary>
        /// Three-pass outer-ring laplacian — grader samples 8%–105% annulus; first pass uses a
        /// shallower preserve (18%) so mid-ring cells above the LiDAR core still get smoothed.
        /// </summary>
        static int ApplyPlayableSlopeOuterSmooth(Terrain terrain, Vector3 center, float extent)
        {
            if (terrain == null)
                return 0;

            const float midAnnulusPreserve = 0.18f;
            var cells = SurfaceTerrainRefinement.SmoothOuterHeightRingPublic(
                terrain, center, extent, strength: 0.32f, preserveRadiusFraction: midAnnulusPreserve);
            cells += SurfaceTerrainRefinement.SmoothOuterHeightRingPublic(
                terrain, center, extent, strength: 0.26f);
            cells += SurfaceTerrainRefinement.SmoothOuterHeightRingPublic(
                terrain, center, extent, strength: 0.16f);
            return cells;
        }

        static bool FixCraters(SceneGroundInfo ground, Vector3 center, float extent, out string action)
        {
            action = string.Empty;
            if (ground?.Terrain == null)
                return false;

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain);
            var multiTile = terrains.Count > 1;
            var stitched = multiTile
                ? SurfaceTerrainTileExpansion.StitchAllNeighborSeamsSync(ground.Terrain)
                : 0;
            var n = 0;
            foreach (var terrain in terrains)
            {
                n += SurfaceTerrainCraterRepair.RepairHeightfieldPlayable(
                    terrain, center, extent, maxPasses: 22);
            }

            action =
                $"Seam-stitched {stitched} tile(s); repaired {n} heightfield cell operation(s) (all terrains).";
            return n > 0 || stitched > 0;
        }

        static bool FixSlopes(SceneGroundInfo ground, Vector3 center, float extent, out string action)
        {
            action = string.Empty;
            if (ground?.Terrain == null)
                return false;

            var cells = 0;
            foreach (var terrain in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain))
                cells += ApplyPlayableSlopeOuterSmooth(terrain, center, extent);

            action = $"Outer-ring smoothed {cells} cells for playable slopes (all terrains).";
            return cells > 0;
        }

        static bool FixTrails(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            out string action)
        {
            action = string.Empty;
            if (ground == null || request == null)
                return false;

            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (SurfaceTrailWalkabilityRepair.TryRepair(ground, request, surfaceRoot, cave, out action))
                return true;

            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            SurfaceTerrainRefinement.TryRefineRoadsAndWater(
                ground.Terrain, surfaceRoot, center,
                request.SurfaceExtentMeters, request.Seed, out var msg);
            FixNav(surfaceRoot, ground, out var navMsg);
            var waterSnapped = SurfaceWorldGenerator.SnapWaterFeaturesToTerrain(
                surfaceRoot != null ? surfaceRoot.Find(SurfaceWorldPaths.WaterName) : null,
                ground);
            var waterMsg = waterSnapped > 0
                ? $"Snapped {waterSnapped} water feature(s) to terrain height."
                : string.Empty;
            action = string.Join(" ", new[] { action, msg, navMsg, waterMsg });
            return !string.IsNullOrEmpty(msg) || !string.IsNullOrEmpty(navMsg) || waterSnapped > 0;
        }

        static bool FixNav(Transform surfaceRoot, SceneGroundInfo ground, out string action)
        {
            action = string.Empty;
            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            if (envRoot == null || ground?.Terrain == null)
                return false;

            var ok = SurfaceNavMeshBaker.BakePhase(envRoot.transform, ground.Terrain, surfaceRoot, out action);
            return ok;
        }

        static bool PlaceProps(
            Transform surfaceRoot,
            SceneGroundInfo ground,
            Vector3 center,
            float extent,
            int seed,
            SurfacePropCategory category,
            out string action) =>
            SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPass(
                surfaceRoot,
                ground?.Terrain,
                center,
                extent,
                seed,
                category,
                out action);

        static bool FixSurfacePlaytest(
            Transform surfaceRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string action)
        {
            action = string.Empty;
            var parts = new List<string>();
            var changed = false;

            var waterSnapped = SurfaceWorldGenerator.SnapWaterFeaturesToTerrain(
                surfaceRoot != null ? surfaceRoot.Find(SurfaceWorldPaths.WaterName) : null,
                ground);
            if (waterSnapped > 0)
            {
                parts.Add($"Snapped {waterSnapped} water feature(s) to terrain height.");
                changed = true;
            }

            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (cave != null && request != null)
            {
                var hadDescent = CaveSurfaceEntranceBuilder.HasDescentWalk(cave);
                var onionBefore = SurfacePlaytestValidator.Run(cave).EntranceOnionSlabCount;
                CaveBuildAutomatedValidation.RunSurfaceEntranceCheckPublic(cave, ground, request);
                if (!hadDescent && CaveSurfaceEntranceBuilder.HasDescentWalk(cave))
                {
                    parts.Add("Built professional entrance descent at mouth.");
                    changed = true;
                }
                else if (onionBefore >= 3)
                {
                    parts.Add("Rebuilt surface walk-in (stripped stacked mouth slabs).");
                    changed = true;
                }
            }
            else if (cave != null && TryBuildMouthDescentIfMissing(cave, ground, request, out var fallbackDescent))
            {
                parts.Add(fallbackDescent);
                changed = true;
            }

            if (!changed)
            {
                if (surfaceRoot == null && ground != null)
                {
                    action = "Surface root missing — run Build Surface World.";
                    return false;
                }

                action = "Surface playtest: no automated fix applied.";
                return false;
            }

            action = string.Join(" ", parts);
            return true;
        }

        static bool FixCaveMouth(SceneGroundInfo ground, WorldGenerationRequest request, out string action)
        {
            action = string.Empty;
            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (cave == null || ground == null)
                return false;

            if (request == null)
            {
                action = "Missing generation request for cave mouth grounding.";
                return false;
            }

            var parts = new List<string>();
            var changed = false;

            if (SurfaceCaveOpeningAligner.TryAlignCaveRootToOpening(cave, ground, preferredSector: -1))
            {
                parts.Add("Aligned cave root to surface opening (XZ lock preserved).");
                changed = true;
            }
            else if (CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(cave, ground, out var snapMsg))
            {
                parts.Add(snapMsg);
                changed = true;
            }

            var hadDescent = CaveSurfaceEntranceBuilder.HasDescentWalk(cave);
            CaveBuildAutomatedValidation.RunSurfaceEntranceCheckPublic(cave, ground, request);
            if (!hadDescent && CaveSurfaceEntranceBuilder.HasDescentWalk(cave))
            {
                parts.Add("Built professional entrance descent at mouth.");
                changed = true;
            }
            else if (!CaveSurfaceEntranceBuilder.HasDescentWalk(cave) &&
                     TryBuildMouthDescentIfMissing(cave, ground, request, out var descentMsg))
            {
                parts.Add(descentMsg);
                changed = true;
            }

            if (CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(cave, ground, out var depthMsg) &&
                !string.IsNullOrEmpty(depthMsg))
            {
                parts.Add(depthMsg);
                changed = true;
            }

            if (!changed)
            {
                action = CaveSurfaceEntranceBuilder.HasDescentWalk(cave)
                    ? "Cave mouth already grounded (descent present)."
                    : "Could not align cave mouth — check surface openings and CaveGeometry.";
                return CaveSurfaceEntranceBuilder.HasDescentWalk(cave);
            }

            action = string.Join(" ", parts);
            return true;
        }

        static bool TryBuildMouthDescentIfMissing(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string message)
        {
            message = string.Empty;
            if (CaveSurfaceEntranceBuilder.HasDescentWalk(caveRoot))
                return false;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var seed = meta != null ? meta.seed : request?.Seed ?? 1;
            var segments = meta != null ? meta.tunnelSegments : 8;
            var chambers = meta != null ? meta.chamberCount : 4;

            var geometry = CaveAdventureCaveGenerator.EnsureGeometryRoot(caveRoot);

            var layout = CaveMazeLayoutGenerator.Generate(seed, segments, chambers);
            if (layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
            {
                message = "Cave layout has no solution path — cannot build descent walk-in.";
                return false;
            }

            var routeFloor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
            if (!HasRouteMesh(routeFloor))
            {
                message =
                    "Full cave geometry is missing (no RouteTerrainFloor) — run Build Complete Cave so queued " +
                    "geo steps 1–13 build shell + blocks before terrain mouth fixes.";
                return false;
            }

            CaveEntranceVolumeBuilder.StripEntranceOnionSlabs(caveRoot, ground);

            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            var built = CaveSurfaceEntranceBuilder.Build(
                caveRoot, geometry, layout, floorMat, rockMat, ground, null, seed);

            if (built <= 0 || !CaveSurfaceEntranceBuilder.HasDescentWalk(caveRoot))
            {
                message = "Descent walk-in build failed.";
                return false;
            }

            CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, out _);
            message = $"Built professional entrance descent ({built} piece(s)).";
            return true;
        }

        static bool HasRouteMesh(Transform root) =>
            root != null && root.GetComponent<MeshFilter>()?.sharedMesh != null;
    }
}
#endif
