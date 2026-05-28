using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Targeted per-stage modifiers — each fix touches only what that rung measures.</summary>
    public static class CaveBuildQualityStageFixer
    {
        public static bool CanAutoFix(string stageId) =>
            !string.IsNullOrEmpty(stageId) && !CaveBuildQualityLadder.ManualOnlyStageIds.Contains(stageId);

        public static bool TryFix(
            string stageId,
            Transform caveRoot,
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            int seed,
            out string actionTaken)
        {
            actionTaken = string.Empty;
            if (caveRoot == null || string.IsNullOrEmpty(stageId))
                return false;

            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot) &&
                stageId is not ("visual_shell" or "materials" or "lighting" or "navmesh" or "mob_spawns"))
            {
                actionTaken = "Layout prototype — skipped rebuild fix (purge-only via visual_shell).";
                return false;
            }

            var adventure = CaveGeometryPaths.IsAdventureCave(caveRoot);
            var changed = false;

            switch (stageId)
            {
                case "path":
                    changed = FixPath(caveRoot, request, adventure, out actionTaken);
                    break;
                case "geometry_integrity":
                    changed = FixGeometryIntegrity(caveRoot, request, adventure, out actionTaken);
                    break;
                case "walkways":
                    changed = FixWalkways(caveRoot, request, adventure, out actionTaken);
                    break;
                case "player_floor":
                    changed = FixPlayerFloor(caveRoot, request, out actionTaken);
                    break;
                case "spawn_reachability":
                    changed = FixSpawnReachability(caveRoot, request, adventure, out actionTaken);
                    break;
                case "navmesh":
                    changed = FixNavMesh(caveRoot, out actionTaken);
                    break;
                case "portal":
                    changed = FixPortal(caveRoot, ground, request, out actionTaken);
                    break;
                case "block_tunnel":
                    changed = FixBlockTunnel(caveRoot, seed, adventure, out actionTaken);
                    break;
                case "mob_spawns":
                    changed = FixMobSpawns(caveRoot, out actionTaken);
                    break;
                case "materials":
                    CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                    actionTaken = "Repaired cave materials on existing meshes.";
                    changed = true;
                    break;
                case "lighting":
                    LavaTubeCavePostProcess.ApplyLightingOnly(caveRoot);
                    actionTaken = "Reapplied cave lighting pass.";
                    changed = true;
                    break;
                case "water":
                    if (request != null && request.IncludeCaveWater)
                    {
                        CaveWaterBuilder.RebuildForCave(caveRoot);
                        actionTaken = "Rebuilt water branch only.";
                    }
                    else
                    {
                        CaveWaterUtility.ClearAllWater(caveRoot);
                        actionTaken = "Cleared water (cave build uses dry mode).";
                    }

                    changed = true;
                    break;
                case "enclosure":
                    changed = FixEnclosure(caveRoot, seed, adventure, out actionTaken);
                    break;
                case "ground_placement":
                case "cave_mouth_seal":
                case "terrain_integration":
                case "terrain_carve":
                    if (stageId == "ground_placement")
                        CaveBuildResearchExecutionBrief.TryLogGroundPlacementRefs();

                    if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
                    {
                        if (ground != null &&
                            !CaveGroundPlacementUtility.IsRootPlacementAcceptable(caveRoot, ground) &&
                            CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                                caveRoot, ground, out actionTaken))
                        {
                            changed = true;
                            break;
                        }

                        actionTaken = "Cave root XZ locked — skipped auto ground/terrain align.";
                        return false;
                    }

                    if (CaveBuildWorkflowCoordinator.IsGroundPlacementLocked)
                    {
                        if (ground != null &&
                            !CaveGroundPlacementUtility.IsRootPlacementAcceptable(caveRoot, ground) &&
                            CaveGroundPlacementUtility.TryRepairLockedGroundPlacement(
                                caveRoot, ground, out actionTaken))
                        {
                            changed = true;
                            break;
                        }

                        actionTaken = "Ground placement locked — skipped (mouth already grounded).";
                        return false;
                    }

                    if (ground != null &&
                        CaveGroundPlacementUtility.TryRepairLockedGroundPlacement(caveRoot, ground, out actionTaken))
                    {
                        changed = true;
                        break;
                    }

                    if (ground != null &&
                        CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, out actionTaken))
                    {
                        changed = true;
                        break;
                    }

                    if (CaveGroundPlacementUtility.IsGroundPlacementAcceptable(caveRoot, ground))
                    {
                        CaveBuildWorkflowCoordinator.TryAutoLockIfPlacementReady(caveRoot, ground);
                        actionTaken = "Ground placement acceptable — auto-locked cave world XZ.";
                        return false;
                    }

                    if (stageId == "terrain_carve" && FixTerrainCarve(caveRoot, out var carveOnly))
                    {
                        actionTaken = carveOnly;
                        changed = true;
                    }

                    break;
                case "interior_ribs":
                    changed = FixInteriorRibs(caveRoot, seed, adventure, out actionTaken);
                    break;
                case "visual_shell":
                case "enclosure_policy":
                case "mode_consistency":
                    changed = FixVisualShell(caveRoot, ground, request, adventure, out actionTaken);
                    break;
                case "playability":
                    if (FixWalkways(caveRoot, request, adventure, out var walkAction))
                    {
                        actionTaken = walkAction;
                        changed = true;
                    }

                    if (FixSpawnReachability(caveRoot, request, adventure, out var spawnAction))
                    {
                        actionTaken = string.IsNullOrEmpty(actionTaken)
                            ? spawnAction
                            : actionTaken + "; " + spawnAction;
                        changed = true;
                    }

                    break;
                case "organic_mesh":
                    changed = FixOrganicMesh(caveRoot, seed, adventure, out actionTaken);
                    break;
                case "atmosphere":
                    if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                    {
                        CavePlayabilityFix.RunSilent(caveRoot);
                        actionTaken = "Simplified single cave atmosphere (adventure).";
                        changed = true;
                    }
                    else
                    {
                        changed = FixAtmosphere(caveRoot, out actionTaken);
                    }

                    break;
                default:
                    return false;
            }

            if (changed)
                EditorUtility.SetDirty(caveRoot.gameObject);

            return changed;
        }

        static bool FixPath(
            Transform caveRoot,
            WorldGenerationRequest request,
            bool adventure,
            out string action)
        {
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                action = "Missing CaveSplinePathAuthoring or path knots.";
                return false;
            }

            if (adventure && request != null)
            {
                var layout = request.UseLayoutPrototype
                    ? CaveMazeLayoutGenerator.GeneratePrototype(
                        request.Seed, request.CaveTunnelSegments, request.CaveChamberCount)
                    : CaveMazeLayoutGenerator.Generate(
                        request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);
                var spline = new CaveSplinePath();
                spline.SetKnots(layout.PathKnots);
                CaveEditorUndo.RecordObject(authoring, "Refresh Maze Path");
                authoring.SetPath(layout.PathKnots, spline.TotalLength);
                action = $"Refreshed {layout.PathKnots.Count} maze path knots (seed {request.Seed}).";
                return true;
            }

            var list = new List<CavePathKnot>(authoring.Knots);
            for (var i = 1; i < list.Count; i++)
            {
                var prev = list[i - 1].Position;
                var cur = list[i].Position;
                if (cur.y < prev.y - 0.2f)
                    continue;

                var k = list[i];
                list[i] = new CavePathKnot(
                    new Vector3(cur.x, prev.y - 0.45f, cur.z),
                    k.RadiusX,
                    k.RadiusY,
                    k.IsChamber);
            }

            var pathSpline = new CaveSplinePath();
            pathSpline.SetKnots(list);
            CaveEditorUndo.RecordObject(authoring, "Enforce Path Descent");
            authoring.SetPath(list, pathSpline.TotalLength);
            action = "Enforced monotonic descent on spline path knots.";
            return true;
        }

        static bool FixGeometryIntegrity(
            Transform caveRoot,
            WorldGenerationRequest request,
            bool adventure,
            out string action)
        {
            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
            {
                action = "Layout prototype — geometry fix skipped (no ceiling/shell rebuild).";
                return false;
            }

            if (adventure)
            {
                CaveAdventureVisualPass.Apply(caveRoot);
                CaveInvisibleColliderUtility.StripForAdventure(caveRoot);
                CaveAdventurePlayabilityPipeline.RunStep(2, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(10, caveRoot, request, default);
                if (CavePlayabilityValidator.CountOpenCeilingSamples(caveRoot, samples: 16) > 2)
                    CaveAdventurePlayabilityPipeline.RunStep(11, caveRoot, request, default);
                action = "Adventure: marked walkables, stripped bad colliders, sealed path ceilings.";
                return true;
            }

            // Pass 0 "Shell + geometry" depends on spline authoring knots existing.
            // If knots are missing/too short, open-ceiling sampling falls back to "all open"
            // (geometry_integrity hard-fails). We can regenerate knots from locked build metadata
            // without moving the cave root or recomputing placement.
            var meta = caveRoot != null ? caveRoot.GetComponent<CaveBuildMetadata>() : null;
            var authoring = caveRoot != null ? caveRoot.GetComponent<CaveSplinePathAuthoring>() : null;
            var ensuredKnots = false;
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                if (meta != null && request != null)
                {
                    if (authoring == null)
                    {
                        authoring = caveRoot.gameObject.AddComponent<CaveSplinePathAuthoring>();
                        CaveEditorUndo.RegisterCreated(authoring, "Cave Spline Path Authoring");
                    }

                    var layout = CaveMazeLayoutGenerator.Generate(
                        meta.seed,
                        meta.tunnelSegments,
                        meta.chamberCount,
                        meta.mazeGenFlavor);
                    if (layout?.PathKnots != null && layout.PathKnots.Count >= 2)
                    {
                        var spline = new CaveSplinePath();
                        spline.SetKnots(layout.PathKnots);
                        CaveEditorUndo.RecordObject(authoring, "Refresh Spline Path (geometry_integrity)");
                        authoring.SetPath(layout.PathKnots, spline.TotalLength);
                        ensuredKnots = true;
                    }
                }
            }

            // geometry_integrity also hard-fails when the true-3D enclosure meshes are missing.
            // In visual_shell passes we rebuild only these meshes (not full cave placement).
            var builtMainTubeAndOuterShell = false;
            if (caveRoot != null)
            {
                var meshRoot = caveRoot.Find("SplineMesh");
                if (meshRoot == null)
                {
                    var go = new GameObject("SplineMesh");
                    CaveEditorUndo.RegisterCreated(go, "SplineMesh root");
                    go.transform.SetParent(caveRoot, false);
                    meshRoot = go.transform;
                }

                var seed = meta != null ? meta.seed : request != null ? request.Seed : 0;
                var knotsOk = authoring != null && authoring.Knots != null && authoring.Knots.Count >= 2;

                if (knotsOk)
                {
                    var spline = new CaveSplinePath();
                    spline.SetKnots(authoring.Knots);

                    var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

                    bool EnsureMainCaveTube()
                    {
                        try
                        {
                            var settings = CaveTubeMeshSettings.DefaultOrganic;
                            settings.Seed = seed + 911;
                            settings.RingSpacing = 1.55f;
                            settings.SidesPerRing = 24;
                            settings.InteriorView = true;

                            var mesh = CaveTubeMeshBuilder.Build(spline, authoring.Knots, settings);
                            return CaveSplineTubeMeshUtility.EnsureTubeMesh(
                                meshRoot,
                                "MainCaveTube",
                                mesh,
                                rockMat,
                                "CaveSplineMainMesh.asset");
                        }
                        catch (MissingComponentException ex)
                        {
                            Debug.LogWarning(
                                "[CaveBuild] MainCaveTube repair hit MissingComponentException — purging and retrying once. " +
                                ex.Message);
                            return RetryEnsureMainCaveTube(meshRoot, spline, authoring, rockMat, seed);
                        }
                    }

                    bool RetryEnsureMainCaveTube(
                        Transform tubeMeshRoot,
                        CaveSplinePath pathSpline,
                        CaveSplinePathAuthoring pathAuthoring,
                        Material rockMaterial,
                        int layoutSeed)
                    {
                        for (var i = tubeMeshRoot.childCount - 1; i >= 0; i--)
                        {
                            var child = tubeMeshRoot.GetChild(i);
                            if (child != null && child.name == "MainCaveTube")
                                CaveEditorUndo.DestroyImmediate(child.gameObject);
                        }

                        var settings = CaveTubeMeshSettings.DefaultOrganic;
                        settings.Seed = layoutSeed + 911;
                        settings.RingSpacing = 1.55f;
                        settings.SidesPerRing = 24;
                        settings.InteriorView = true;

                        var mesh = CaveTubeMeshBuilder.Build(pathSpline, pathAuthoring.Knots, settings);
                        return CaveSplineTubeMeshUtility.EnsureTubeMesh(
                            tubeMeshRoot,
                            "MainCaveTube",
                            mesh,
                            rockMaterial,
                            "CaveSplineMainMesh.asset");
                    }

                    bool EnsureMainCaveOuterShell()
                    {
                        try
                        {
                            var outerKnots = new List<CavePathKnot>(authoring.Knots.Count);
                            foreach (var k in authoring.Knots)
                                outerKnots.Add(new CavePathKnot(k.Position, k.RadiusX + 1.35f, k.RadiusY + 1.15f, k.IsChamber));

                            var outerSettings = CaveTubeMeshSettings.DefaultOrganic;
                            outerSettings.Seed = seed + 901;
                            outerSettings.InteriorView = false;
                            outerSettings.RingSpacing = 1.9f;
                            outerSettings.SidesPerRing = 18;
                            outerSettings.NoiseAmplitude = 0.42f;
                            outerSettings.FloorFlatten = 0.55f;
                            outerSettings.VerticalWalls = true;
                            outerSettings.HeightMultiplier = 2.8f;

                            var outerMesh = CaveTubeMeshBuilder.Build(spline, outerKnots, outerSettings);
                            return CaveSplineTubeMeshUtility.EnsureTubeMesh(
                                meshRoot,
                                "MainCaveOuterShell",
                                outerMesh,
                                rockMat,
                                "CaveSplineOuterShellMesh.asset");
                        }
                        catch (MissingComponentException ex)
                        {
                            Debug.LogWarning(
                                "[CaveBuild] MainCaveOuterShell repair hit MissingComponentException — purging and retrying once. " +
                                ex.Message);
                            for (var i = meshRoot.childCount - 1; i >= 0; i--)
                            {
                                var child = meshRoot.GetChild(i);
                                if (child != null && child.name == "MainCaveOuterShell")
                                    CaveEditorUndo.DestroyImmediate(child.gameObject);
                            }

                            var outerKnots = new List<CavePathKnot>(authoring.Knots.Count);
                            foreach (var k in authoring.Knots)
                                outerKnots.Add(new CavePathKnot(k.Position, k.RadiusX + 1.35f, k.RadiusY + 1.15f, k.IsChamber));

                            var outerSettings = CaveTubeMeshSettings.DefaultOrganic;
                            outerSettings.Seed = seed + 901;
                            outerSettings.InteriorView = false;
                            outerSettings.RingSpacing = 1.9f;
                            outerSettings.SidesPerRing = 18;
                            outerSettings.NoiseAmplitude = 0.42f;
                            outerSettings.FloorFlatten = 0.55f;
                            outerSettings.VerticalWalls = true;
                            outerSettings.HeightMultiplier = 2.8f;

                            var outerMesh = CaveTubeMeshBuilder.Build(spline, outerKnots, outerSettings);
                            return CaveSplineTubeMeshUtility.EnsureTubeMesh(
                                meshRoot,
                                "MainCaveOuterShell",
                                outerMesh,
                                rockMat,
                                "CaveSplineOuterShellMesh.asset");
                        }
                    }

                    builtMainTubeAndOuterShell = EnsureMainCaveTube() || EnsureMainCaveOuterShell();
                }
            }

            if (CavePlayabilityValidator.RemoveLayeredFallbackGeometry(caveRoot) > 0)
            {
                action = "Removed layered fallback closure geometry.";
                return true;
            }

            if (CavePlayabilityValidator.CountOpenCeilingSamples(caveRoot, samples: 16) > 2 &&
                ReinforceCeilingNonAdventure(caveRoot))
            {
                action = "Added ceiling cover along spline (no walkway rebuild).";
                return true;
            }

            if (CavePlayabilityValidator.RemoveDecorativeColliders(caveRoot) > 0)
            {
                action = "Removed decorative colliders only.";
                return true;
            }

            if (ensuredKnots || builtMainTubeAndOuterShell)
            {
                if (ensuredKnots && builtMainTubeAndOuterShell)
                    action = "Generated missing spline knots and rebuilt MainCaveTube + MainCaveOuterShell for geometry_integrity.";
                else if (ensuredKnots)
                    action = "Generated missing spline knots for geometry_integrity sampling.";
                else
                    action = "Rebuilt MainCaveTube + MainCaveOuterShell for geometry_integrity.";
                return true;
            }

            action = "No geometry_integrity modifier applied.";
            return false;
        }

        static bool ReinforceCeilingNonAdventure(Transform caveRoot)
        {
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return false;

            var rock = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (rock == null)
                return false;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot == null)
                return false;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var mazeVol = meshRoot.Find(CaveMazeVolumeBuilder.MazeVolumeRootName);
            if (meta != null && mazeVol != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
                CaveMazeCeilingCoverBuilder.Build(mazeVol, layout, rock);
                return true;
            }

            CaveCeilingSealUtility.BuildAlongSpline(meshRoot, spline, rock, mazeMode: true);
            return true;
        }

        static bool FixWalkways(Transform caveRoot, WorldGenerationRequest request, bool adventure, out string action)
        {
            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
            {
                action = "Layout prototype — flat LayoutWalkFloor only.";
                return false;
            }

            if (adventure)
            {
                CaveAdventurePlayabilityPipeline.RunStep(2, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(6, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(7, caveRoot, request, default);
                CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                action = "Kept RouteTerrainFloor visible; PathPlatforms slabs stay hidden.";
                return true;
            }

            var placed = CaveWalkwayBuilder.RebuildFromAuthoring(caveRoot);
            CaveFloorSafetyUtility.EnsureVisibleWalkways(caveRoot);
            action = placed > 0
                ? $"Rebuilt {placed} spline walk floors only."
                : "Ensured visible walk floor renderers.";
            return true;
        }

        static bool FixPlayerFloor(Transform caveRoot, WorldGenerationRequest request, out string action)
        {
            var n = CaveFloorSafetyUtility.EnsureRouteTerrainPlayCollider(caveRoot);
            n += CaveFloorSafetyUtility.Apply(caveRoot);
            var snapped = CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(caveRoot);
            if (request != null)
            {
                var layout = CaveSpawnAlignmentUtility.TryResolveLayout(caveRoot, request);
                if (layout != null)
                    CaveSpawnAlignmentUtility.AlignSpawnToMazeStart(caveRoot, layout);
            }

            action =
                $"Ensured walk colliders ({n}), spawn pad, snap={snapped}. Re-test Play Mode teleport.";
            return n > 0 || snapped;
        }

        static bool FixSpawnReachability(
            Transform caveRoot,
            WorldGenerationRequest request,
            bool adventure,
            out string action)
        {
            if (adventure && request != null)
            {
                CaveAdventurePlayabilityPipeline.RunStep(3, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(4, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(15, caveRoot, request, default);
                action = "Aligned spawn pad to maze floor and repaired reachability.";
                return true;
            }

            CavePlayabilityValidator.AutoFix(caveRoot);
            action = "Aligned spawn and nearest walk floor.";
            return true;
        }

        static bool FixNavMesh(Transform caveRoot, out string action)
        {
            var ok = LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);
            action = ok ? "Rebaked NavMesh on walk surfaces only." : "NavMesh bake attempted.";
            return true;
        }

        static bool FixPortal(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string action)
        {
            var entrance = caveRoot.Find("Entrance");
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || entrance == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                action = "Missing entrance or path authoring.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            CaveMazeLayout mazeLayout = null;
            if (request != null && request.UseTrue3DCaveSystem)
            {
                mazeLayout = CaveMazeLayoutGenerator.Generate(
                    request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);
            }

            var spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(
                caveRoot, entrance, spline, keepAtSurfaceMouth: false, mazeLayout);
            CaveEntrancePortalPreserver.Apply(caveRoot, ground, spawn);
            action = "Realigned entrance spawn + portal link only.";
            return true;
        }

        static bool FixBlockTunnel(Transform caveRoot, int seed, bool adventure, out string action)
        {
            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
            {
                action = "Layout prototype — block tunnel skipped.";
                return false;
            }

            if (adventure)
            {
                CaveAdventurePlayabilityPipeline.RunStep(9, caveRoot, null, default);
                var trimmed = CaveCompactRouteUtility.PrepareCompactRouteBlockBudgetForGrading(caveRoot);
                CaveAdventureVisualPass.RestoreBlockWallsForGrading(caveRoot);
                CaveInvisibleColliderUtility.StripForAdventure(caveRoot);
                action = trimmed > 0
                    ? $"Compact block_tunnel: trimmed {trimmed} to grader band + capped minable renderers."
                    : "Compact block_tunnel: capped minable renderers + invisible collider strip.";
                return true;
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                action = "No path to place blocks.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var rock = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (rock == null)
            {
                action = "Missing rock material.";
                return false;
            }

            var settings = CaveBlockTunnelBuilder.Settings.Default;
            settings.RingSpacing = 2.6f;
            settings.WallThickness = 2;
            settings.OuterWallMinable = true;
            var placed = CaveBlockTunnelBuilder.Build(caveRoot, spline, rock, seed + 404, settings, "Main");
            action = $"Added {placed} block-tunnel cubes along spline.";
            return placed > 0;
        }

        static bool FixMobSpawns(Transform caveRoot, out string action)
        {
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var placed = 0;
            if (meta != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
                placed = CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
            }
            else
            {
                var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
                if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                {
                    action = "No path for mob spawns.";
                    return false;
                }

                placed = CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, authoring.Knots);
            }

            var wired = CaveCombatSetupUtility.WireSceneCombat(caveRoot);
            action = placed > 0
                ? $"Placed {placed} mob spawner(s); wired combat on {wired} object(s)."
                : $"Mob spawners present; combat wiring refreshed ({wired}).";
            return placed > 0 || wired > 0;
        }

        static bool FixEnclosure(Transform caveRoot, int seed, bool adventure, out string action)
        {
            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
            {
                action = "Layout prototype — enclosure/ceiling fix skipped.";
                return false;
            }

            if (adventure)
            {
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                if (meta != null)
                {
                    var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
                    var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
                    var rock = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
                    if (geometry != null && rock != null)
                    {
                        var stripped = CaveEnclosureShellBuilder.PurgeLayerOffenders(geometry);
                        var ceilingPieces = CaveEnclosureShellBuilder.EnsureSingleCeiling(
                            geometry, layout, rock, seed);
                        if (stripped > 0 || ceilingPieces > 0)
                        {
                            action = $"Purge {stripped} onion piece(s), ensured {ceilingPieces} route ceiling mesh.";
                            return true;
                        }
                    }
                }

                action = "Route enclosure already present.";
                return false;
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null)
            {
                action = "No path for enclosure.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var nodes = new System.Collections.Generic.List<Vector3>();
            for (var i = 0; i < 24; i++)
            {
                var t = i / 23f;
                nodes.Add(spline.SampleAtNormalized(t).Position);
            }

            var catalog = LavaTubePrefabCatalog.Load();
            if (!catalog.IsValid)
            {
                action = "Catalog invalid for occlusion shell.";
                return false;
            }

            var built = LavaTubeCaveEnclosureBuilder.Build(caveRoot, catalog, new System.Random(seed + 77), nodes);
            action = $"Built {built} occlusion shell piece(s).";
            return built > 0;
        }

        static bool FixTerrainCarve(Transform caveRoot, out string action)
        {
            if (Object.FindFirstObjectByType<Terrain>() == null)
            {
                action = "No terrain in scene.";
                return false;
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                action = "No path for terrain carve.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var ok = CaveTerrainCarveUtility.CarveForCaveSystem(caveRoot, spline, null);
            action = ok ? "Re-carved terrain along cave path only." : "Terrain carve failed.";
            return ok;
        }

        static bool FixInteriorRibs(Transform caveRoot, int seed, bool adventure, out string action)
        {
            if (adventure)
            {
                action = "Adventure uses block tunnel for interior read.";
                return false;
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null)
            {
                action = "No path for interior ribs.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var catalog = LavaTubePrefabCatalog.Load();
            var n = CaveOrganicInteriorPass.Build(caveRoot, spline, catalog, new System.Random(seed + 313));
            action = $"Placed {n} interior rib piece(s).";
            return n > 0;
        }

        static bool IsTrue3DShellExpected(Transform caveRoot, WorldGenerationRequest request)
        {
            if (request != null)
                return request.UseTrue3DCaveSystem;

            // Partial builds keep CaveBuildMetadata before adventure shells exist — still expect true-3D maze.
            if (caveRoot != null && caveRoot.GetComponent<CaveBuildMetadata>() != null)
                return true;

            var seamlessRoot = caveRoot != null ? caveRoot.Find("SeamlessTunnel") : null;
            return seamlessRoot != null && seamlessRoot.childCount == 0;
        }

        static bool NeedsTrue3DShellBootstrap(Transform caveRoot, WorldGenerationRequest request)
        {
            if (caveRoot == null || !IsTrue3DShellExpected(caveRoot, request))
                return false;

            var maze = caveRoot.Find($"SplineMesh/{CaveMazeVolumeBuilder.MazeVolumeRootName}");
            if (maze != null && maze.GetComponentsInChildren<MeshRenderer>(true).Length >= 12)
                return false;

            var main = caveRoot.Find("SplineMesh/MainCaveTube");
            if (main != null && main.GetComponent<MeshFilter>()?.sharedMesh != null)
                return false;

            return caveRoot.GetComponent<CaveBuildMetadata>() != null || request != null;
        }

        /// <summary>
        /// visual_shell rung: route terrain may exist while SplineMesh maze/tube shell was never built.
        /// Regenerate from locked metadata — no root XZ move, no full Build Complete Cave.
        /// </summary>
        static bool TryBootstrapMissingTrue3DShell(
            Transform caveRoot,
            WorldGenerationRequest request,
            out string action)
        {
            action = string.Empty;
            if (!NeedsTrue3DShellBootstrap(caveRoot, request))
                return false;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var metaSeed = meta != null ? meta.seed : request != null ? request.Seed : 0;
            var tunnelSegs = meta != null ? meta.tunnelSegments : request?.CaveTunnelSegments ?? 8;
            var chambers = meta != null ? meta.chamberCount : request?.CaveChamberCount ?? 2;
            var flavor = meta != null ? meta.mazeGenFlavor : 0;

            var layout = CaveMazeLayoutGenerator.Generate(metaSeed, tunnelSegs, chambers, flavor);
            if (layout?.PathKnots == null || layout.PathKnots.Count < 2)
            {
                action = "Could not generate maze layout for true-3D shell bootstrap.";
                return false;
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null)
            {
                authoring = caveRoot.gameObject.AddComponent<CaveSplinePathAuthoring>();
                CaveEditorUndo.RegisterCreated(authoring, "Cave Spline Path Authoring");
            }

            if (authoring.Knots == null || authoring.Knots.Count < 4)
            {
                var pathSpline = new CaveSplinePath();
                pathSpline.SetKnots(layout.PathKnots);
                CaveEditorUndo.RecordObject(authoring, "Bootstrap Spline Path (visual_shell)");
                authoring.SetPath(layout.PathKnots, pathSpline.TotalLength);
            }

            var geometry = CaveAdventureCaveGenerator.EnsureGeometryRoot(caveRoot);

            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot == null)
            {
                var go = new GameObject("SplineMesh");
                CaveEditorUndo.RegisterCreated(go, "SplineMesh root");
                go.transform.SetParent(caveRoot, false);
                meshRoot = go.transform;
            }

            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (rockMat == null)
            {
                action = "Missing cave rock material for true-3D shell bootstrap.";
                return false;
            }

            var useAdventureHybrid = request != null && request.UseBlockTunnel;
            var placed = CaveMazeVolumeBuilder.Build(meshRoot, layout, rockMat, useAdventureHybrid);
            var mazeVol = meshRoot.Find(CaveMazeVolumeBuilder.MazeVolumeRootName);
            if (mazeVol != null)
                placed += CaveMazeCeilingCoverBuilder.Build(mazeVol, layout, rockMat);

            CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot);

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var catalog = LavaTubePrefabCatalog.Load();
            var ribs = CaveOrganicInteriorPass.Build(caveRoot, spline, catalog, new System.Random(metaSeed + 313));

            var shellPieces = 0;
            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            if (geometry != null && floorMat != null)
            {
                var needFloor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName) == null;
                var needCeiling = geometry.Find(CaveEnclosureShellBuilder.CeilingRootName) == null;
                if (needFloor || needCeiling)
                    shellPieces = CaveEnclosureShellBuilder.Build(geometry, layout, floorMat, rockMat, metaSeed);
            }

            action =
                $"Bootstrapped true-3D shell from metadata (seed {metaSeed}): {placed} maze piece(s), {ribs} rib(s), {shellPieces} route shell piece(s).";
            return placed > 0 || ribs > 0 || shellPieces > 0;
        }

        static bool FixVisualShell(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool adventure,
            out string action)
        {
            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
            {
                var layoutStripped = CaveCompactLayerPurge.Purge(caveRoot);
                action = $"Layout prototype — purged {layoutStripped} legacy layer(s) only (no mesh rebuild).";
                return layoutStripped > 0;
            }

            if (TryBootstrapMissingTrue3DShell(caveRoot, request, out action))
            {
                CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                return true;
            }

            var phase = CaveBuildWorkflowCoordinator.CurrentPhase;
            var additiveOnly = phase is CaveBuildWorkflowCoordinator.Phase.MeatLoop
                or CaveBuildWorkflowCoordinator.Phase.PostMeat;

            if (additiveOnly && request != null)
            {
                if (CaveVisualShellRouteRepair.TryRepair(caveRoot, ground, request, out action))
                {
                    // Additive visual-shell route repair can succeed while legacy spline/tube layers
                    // are still present; the visual_shell grader expects "no onion layers".
                    // PurgeShellLayersOnly preserves walkways (committed floor colliders), but removes
                    // the legacy spline/tube objects (MainCaveTube/CaveMazeVolume/SeamlessTunnel/SkySeal).
                    CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
                    CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                    CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                    return true;
                }

                // CaveBuildQualityGrader::GradeEnclosurePolicy counts floor/ceiling roots as
                // direct children under CaveGeometry. In some additive runs the meshes exist as
                // descendants, so TryRepair may report "no rebuild" while grader still finds 0.
                // If the direct roots are missing, force a minimal route shell rebuild.
                if (request.UseLayoutPrototype == false)
                {
                    var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
                    if (geometry != null)
                    {
                        var floorRoots = 0;
                        var ceilingRoots = 0;
                        foreach (Transform child in geometry)
                        {
                            if (child == null)
                                continue;
                            if (child.name == CaveEnclosureShellBuilder.FloorRootName)
                                floorRoots++;
                            else if (child.name == CaveEnclosureShellBuilder.CeilingRootName)
                                ceilingRoots++;
                        }

                        if (floorRoots != 1 || ceilingRoots != 1)
                        {
                            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                            if (meta != null)
                            {
                                var layout = CaveMazeLayoutGenerator.Generate(
                                    meta.seed,
                                    meta.tunnelSegments,
                                    meta.chamberCount,
                                    meta.mazeGenFlavor);
                                var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
                                var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
                                if (floorMat != null && rockMat != null)
                                {
                                    var shellPieces = CaveEnclosureShellBuilder.Build(geometry, layout, floorMat, rockMat, meta.seed);
                                    if (shellPieces > 0)
                                    {
                                        CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                                        CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                                        action = "Additive visual_shell — forced route shell rebuild (direct floor/ceiling roots missing).";
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                action = "Additive visual_shell — no full rebuild (route shell already valid).";
                CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                return false;
            }

            var stripped = CaveBuildWorkflowCoordinator.ShouldPreserveWalkways
                ? CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot)
                : CaveCompactLayerPurge.Purge(caveRoot);
            var rebuilt = 0;

            if (request != null && request.UseLayoutPrototype == false)
            {
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                if (meta != null)
                {
                    var layout = CaveMazeLayoutGenerator.Generate(
                        meta.seed, meta.tunnelSegments, meta.chamberCount);
                    var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
                    var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
                    var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
                    if (geometry != null && floorMat != null && rockMat != null)
                    {
                        rebuilt += CaveEnclosureShellBuilder.Build(
                            geometry, layout, floorMat, rockMat, meta.seed);

                        if (adventure)
                        {
                            var blockMain = geometry.Find($"{CaveAdventureBlockBuilder.RootName}/Main");
                            if (blockMain != null)
                            {
                                for (var i = blockMain.childCount - 1; i >= 0; i--)
                                {
                                    var child = blockMain.GetChild(i);
                                    if (child != null && child.name.StartsWith("BlockRingMid_"))
                                        CaveEditorUndo.DestroyImmediate(child.gameObject);
                                }
                            }
                            
                            var blockSettings = CaveBlockTunnelBuilder.Settings.Default;
                            blockSettings.RingSpacing = 1.1f;
                            blockSettings.WallThickness = 1;
                            blockSettings.CeilingLayers = 0;
                            blockSettings.FloorLayers = 0;
                            blockSettings.BlockSize = 1f;
                            CaveAdventureBlockBuilder.Build(geometry, layout, rockMat, request.Seed, blockSettings);
                            rebuilt++;
                        }
                    }

                    if (adventure)
                    {
                        CaveAdventureCaveLighting.Apply(caveRoot, layout);
                        CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
                    }
                }
            }

            CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            CaveBuildWorkflowCoordinator.InvalidateNavMesh();
            LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, force: true);

            action = $"Purge {stripped} layered piece(s), rebuilt {rebuilt} route surface(s).";
            return stripped > 0 || rebuilt > 0;
        }

        static bool FixOrganicMesh(Transform caveRoot, int seed, bool adventure, out string action)
        {
            if (CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
            {
                action = "Layout prototype — organic/shell pass skipped.";
                return false;
            }

            if (adventure)
            {
                var n = CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
                CaveAdventureVisualPass.Apply(caveRoot);
                action = $"Purged {n} layered piece(s) and restored block visibility.";
                return true;
            }

            if (caveRoot.Find("SplineMesh/CaveMazeVolume") != null)
            {
                action = "Maze volume present — organic tube skipped.";
                return false;
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                action = "No path for tube mesh.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var settings = CaveTubeMeshSettings.DefaultOrganic;
            settings.Seed = seed + 911;
            settings.RingSpacing = 1.55f;
            settings.SidesPerRing = 24;
            settings.InteriorView = true;

            var mesh = CaveTubeMeshBuilder.Build(spline, authoring.Knots, settings);
            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot == null || mesh == null)
            {
                action = "MainCaveTube missing.";
                return false;
            }

            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (!CaveSplineTubeMeshUtility.EnsureTubeMesh(
                    meshRoot,
                    "MainCaveTube",
                    mesh,
                    rockMat,
                    "CaveSplineMainMesh.asset"))
            {
                action = "MainCaveTube mesh rebuild failed.";
                return false;
            }

            action = "Rebuilt MainCaveTube mesh asset only.";
            return true;
        }

        static bool FixAtmosphere(Transform caveRoot, out string action)
        {
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                action = "No path for atmosphere zone.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var nodes = new System.Collections.Generic.List<Vector3>();
            for (var i = 0; i < 24; i++)
            {
                var t = i / 23f;
                nodes.Add(spline.SampleAtNormalized(t).Position);
            }

            LavaTubeCaveEnclosureBuilder.EnsureAtmosphereZone(caveRoot, nodes);
            action = "Ensured underground atmosphere trigger zone.";
            return true;
        }
    }
}
