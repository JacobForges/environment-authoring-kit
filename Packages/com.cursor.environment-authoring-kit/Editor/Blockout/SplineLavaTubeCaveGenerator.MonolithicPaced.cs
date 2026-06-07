#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static partial class SplineLavaTubeCaveGenerator
    {
        internal sealed class SplineMonolithicRuntime
        {
            public LavaTubePrefabCatalog Catalog;
            public System.Random Rng;
            public Transform CavesRoot;
            public Transform Entrance;
            public Transform MeshRoot;
            public Transform SeamlessRoot;
            public Transform DetailRoot;
            public Transform PropsRoot;
            public Transform WaterRoot;
            public CaveSplinePath Spline;
            public List<CavePathKnot> Knots;
            public CaveMazeLayout MazeLayout;
            public List<Vector3> ChamberCenters = new();
            public Material RockMat;
            public bool UseTrue3D;
            public bool UseAdventureHybrid;
            public bool UseBlocks;
            public int BlockCount;
            public int PieceCount;
            public List<Vector3> PathNodes;
            public CaveSplinePath BranchSpline;
        }

        internal static bool TryAdvanceMonolithicChunk(CaveMonolithicGeneratePacing.Session session)
        {
            if (session.Cancelled)
            {
                session.Report = CancelledReport();
                return true;
            }

            if (session.Request.UseLayoutPrototype ||
                (session.Request.UseTrue3DCaveSystem && session.Request.UseBlockTunnel))
            {
                session.Report = Generate(
                    session.EnvironmentRoot,
                    session.Ground,
                    session.Request,
                    session.ReportProgress);
                return true;
            }

            var rt = session.SplineRuntime ??= new SplineMonolithicRuntime();
            bool Cancelled(float t, string label)
            {
                if (session.ReportProgress != null && session.ReportProgress(t, label))
                {
                    session.Cancelled = true;
                    return true;
                }

                return false;
            }

            switch (session.Chunk)
            {
                case 0:
                    rt.Catalog = LavaTubePrefabCatalog.Load();
                    if (!rt.Catalog.IsValid)
                    {
                        session.Report = new LavaTubeCaveBuildReport { Message = "Prefab catalog empty." };
                        return true;
                    }

                    rt.Rng = new System.Random(session.Request.Seed);
                    rt.CavesRoot = EnvironmentSceneUtility.GetOrCreateChild(
                        session.EnvironmentRoot, "LavaTubeCaveSystem");
                    if (Cancelled(0.02f, "Clearing previous cave…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    CaveBuildSceneUtility.ClearChildrenFast(rt.CavesRoot);
                    CaveLegacyGeometryPurge.Purge(rt.CavesRoot);
                    var entranceForward = session.Ground.HasAnchor
                        ? session.Ground.HorizontalForward
                        : Vector3.forward;
                    rt.CavesRoot.position = GetEntranceWorldPosition(session.Ground, rt.CavesRoot);
                    rt.CavesRoot.rotation = Quaternion.LookRotation(entranceForward, Vector3.up);
                    rt.Entrance = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Entrance");
                    rt.Entrance.localPosition = new Vector3(0f, CaveGeometryPaths.UndergroundDepthMeters, 0f);
                    rt.MeshRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "SplineMesh");
                    rt.SeamlessRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "SeamlessTunnel");
                    rt.DetailRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Details");
                    rt.PropsRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.DetailRoot, "Props");
                    rt.WaterRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Water");
                    break;
                case 1:
                    if (Cancelled(0.08f, "Above-ground entrance…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    LavaTubeCaveGenerator.BuildEntranceForPipeline(rt.Entrance, rt.Catalog, rt.Rng);
                    LavaTubeCaveGenerator.EnsureEntranceMarker(rt.CavesRoot);
                    if (Cancelled(0.15f, "Descending path…"))
                        return true;
                    rt.UseTrue3D = session.Request.UseTrue3DCaveSystem;
                    rt.UseAdventureHybrid = rt.UseTrue3D && session.Request.UseBlockTunnel;
                    rt.UseBlocks = session.Request.UseBlockTunnel;
                    rt.RockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
                    if (rt.UseTrue3D)
                    {
                        rt.MazeLayout = CaveMazeLayoutGenerator.Generate(
                            session.Request.Seed,
                            session.Request.CaveTunnelSegments,
                            session.Request.CaveChamberCount,
                            session.Request.MazeGenFlavor);
                        rt.Knots = rt.MazeLayout.PathKnots;
                    }
                    else
                    {
                        rt.Knots = CavePathFactory.BuildDescendingPath(
                            session.Request.CaveTunnelSegments,
                            session.Request.CaveChamberCount,
                            session.Request.Seed,
                            session.Request.CavePathStepLength > 0 ? session.Request.CavePathStepLength : 11f,
                            session.Request.CavePathDropPerStep > 0 ? session.Request.CavePathDropPerStep : 0.32f,
                            session.Request.CavePathYawVariance > 0 ? session.Request.CavePathYawVariance : 22f,
                            session.Request.CaveChamberSizeMultiplier > 0
                                ? session.Request.CaveChamberSizeMultiplier
                                : 2.35f,
                            session.Request.CaveEntranceYawDegrees);
                        var tunnelRx = rt.Knots[0].RadiusX;
                        var tunnelRy = rt.Knots[0].RadiusY;
                        rt.Knots[0] = new CavePathKnot(
                            new Vector3(1f, -0.15f, 3.5f), tunnelRx, tunnelRy, false);
                    }

                    rt.Spline = new CaveSplinePath();
                    rt.Spline.SetKnots(rt.Knots);
                    rt.ChamberCenters.Clear();
                    foreach (var knot in rt.Knots)
                    {
                        if (knot.IsChamber)
                            rt.ChamberCenters.Add(knot.Position);
                    }

                    break;
                case 2:
                    if (Cancelled(0.24f, "Block tunnel shell…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    rt.BlockCount = 0;
                    if (rt.UseBlocks)
                    {
                        var blockSettings = CaveBlockTunnelBuilder.Settings.Default;
                        if (rt.UseAdventureHybrid)
                        {
                            blockSettings.RingSpacing = 2.1f;
                            blockSettings.AngularSteps = 16;
                            blockSettings.FloorLayers = 0;
                            blockSettings.CeilingLayers = 1;
                            blockSettings.WallThickness = 3;
                            blockSettings.InteriorHollow = 0.42f;
                            blockSettings.OuterWallMinable = true;
                        }

                        rt.BlockCount = CaveBlockTunnelBuilder.Build(
                            rt.CavesRoot, rt.Spline, rt.RockMat, session.Request.Seed, blockSettings);
                    }

                    break;
                case 3:
                    if (Cancelled(0.28f, rt.UseTrue3D ? "Maze cave volume…" : "Organic tube liner…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    rt.PieceCount = 0;
                    if (rt.UseTrue3D && rt.MazeLayout != null)
                    {
                        ClearLegacyTubeMeshes(rt.MeshRoot);
                        rt.PieceCount = CaveMazeVolumeBuilder.Build(
                            rt.MeshRoot, rt.MazeLayout, rt.RockMat, rt.UseAdventureHybrid);
                        var mazeVol = rt.MeshRoot.Find(CaveMazeVolumeBuilder.MazeVolumeRootName);
                        if (mazeVol != null)
                        {
                            rt.PieceCount += CaveMazeCeilingCoverBuilder.Build(
                                mazeVol, rt.MazeLayout, rt.RockMat);
                        }

                        PlaceMazeTorches(rt.MeshRoot, rt.MazeLayout, rt.Rng);
                        if (rt.UseAdventureHybrid)
                            CaveAdventureVisualPass.Apply(rt.CavesRoot);
                        CaveBuildSceneUtility.ClearChildrenFast(rt.SeamlessRoot);
                    }
                    else
                    {
                        var settings = CaveTubeMeshSettings.DefaultOrganic;
                        settings.Seed = session.Request.Seed;
                        settings.InteriorView = true;
                        settings.RingSpacing = 2.1f;
                        settings.SidesPerRing = 16;
                        settings.VerticalWalls = false;
                        settings.FloorFlatten = 0.12f;
                        settings.HeightMultiplier = 1f;
                        var mesh = CaveTubeMeshBuilder.Build(rt.Spline, rt.Knots, settings);
                        rt.PieceCount = CreateMeshObject(rt.MeshRoot, "MainCaveTube", mesh, deferSave: true);
                        SetTubeRendererEnabled(rt.MeshRoot, "MainCaveTube", true);
                        rt.PieceCount += BuildSeamlessClosure(
                            rt.SeamlessRoot, rt.Catalog, rt.Rng, rt.Spline, rt.ChamberCenters);
                        rt.PieceCount += CaveCeilingSealUtility.BuildAlongSpline(
                            rt.SeamlessRoot, rt.Spline, rt.RockMat, mazeMode: false);
                    }

                    rt.PathNodes = SamplePathNodes(rt.Spline, 24);
                    var authoring = rt.CavesRoot.GetComponent<CaveSplinePathAuthoring>();
                    if (authoring == null)
                        authoring = rt.CavesRoot.gameObject.AddComponent<CaveSplinePathAuthoring>();
                    authoring.SetPath(rt.Knots, rt.Spline.TotalLength);
                    var meta = rt.CavesRoot.GetComponent<CaveBuildMetadata>();
                    if (meta == null)
                        meta = rt.CavesRoot.gameObject.AddComponent<CaveBuildMetadata>();
                    meta.Set(
                        session.Request.Seed,
                        session.Request.CaveTunnelSegments,
                        session.Request.CaveChamberCount,
                        rt.UseAdventureHybrid);
                    break;
                case 4:
                    if (Cancelled(0.42f, "Walkways + spawn at surface mouth…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    if (rt.UseTrue3D && rt.MazeLayout != null)
                    {
                        CaveMazeWalkwayBuilder.Build(rt.CavesRoot, rt.MazeLayout);
                        if (rt.UseAdventureHybrid)
                        {
                            CaveAdventureFeaturesBuilder.Build(
                                rt.CavesRoot,
                                rt.MazeLayout,
                                rt.RockMat,
                                CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial(),
                                session.Request.Seed);
                        }
                    }
                    else
                    {
                        CaveWalkwayBuilder.Build(rt.CavesRoot, rt.Spline);
                    }

                    CaveFloorSafetyUtility.Apply(rt.CavesRoot);
                    SplineCaveSpawnAligner.AlignEntranceSpawn(
                        rt.CavesRoot, rt.Entrance, rt.Spline, keepAtSurfaceMouth: false, rt.MazeLayout);
                    CaveColliderUtility.EnsureMazeVolumeColliders(rt.CavesRoot);
                    CaveOrganicInteriorPass.Build(rt.CavesRoot, rt.Spline, rt.Catalog, rt.Rng);
                    break;
                case 5:
                    if (!rt.UseBlocks && Cancelled(0.55f, "Minable wall blocks…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    if (!rt.UseBlocks)
                        CaveMinableWallBuilder.Build(rt.CavesRoot, rt.Spline, rt.Catalog, rt.Rng, spacingMeters: 16f);
                    break;
                case 6:
                    if (Cancelled(0.68f, "Props and lights…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    if (!rt.UseTrue3D)
                        PlaceLightsAlongSpline(rt.MeshRoot, rt.Spline, rt.Rng);
                    ScatterPropsAlongSpline(
                        rt.PropsRoot, rt.Catalog, rt.Rng, rt.Spline,
                        session.Request.CavePropScatterCount > 0 ? session.Request.CavePropScatterCount : 14);
                    PlaceAdventureLoreBeats(rt.DetailRoot, rt.Spline, rt.Catalog, rt.Rng);
                    PlaceChamberSpawners(rt.CavesRoot, rt.Knots);
                    PlaceMinablesNearSpline(rt.DetailRoot, rt.Catalog, rt.Rng, rt.Spline, 10);
                    break;
                case 7:
                    rt.BranchSpline = null;
                    if (session.Request.IncludeCaveWater)
                    {
                        if (Cancelled(0.82f, "Water branch geometry…"))
                            return true;
                        CaveBuildActionPacing.TouchQueueActivity();
                        var branchKnots = BuildDescendingWaterBranchKnots(
                            rt.Knots[rt.Knots.Count - 1], session.Request.Seed + 77);
                        rt.BranchSpline = new CaveSplinePath();
                        rt.BranchSpline.SetKnots(branchKnots);
                        var branchSettings = CaveTubeMeshSettings.DefaultOrganic;
                        branchSettings.Seed = session.Request.Seed + 77;
                        branchSettings.InteriorView = true;
                        branchSettings.VerticalWalls = rt.UseTrue3D;
                        branchSettings.FloorFlatten = rt.UseTrue3D ? 0.55f : 0.12f;
                        branchSettings.HeightMultiplier = rt.UseTrue3D ? 2.2f : 1f;
                        var branchMesh = CaveTubeMeshBuilder.Build(rt.BranchSpline, branchKnots, branchSettings);
                        rt.PieceCount += CreateMeshObject(rt.WaterRoot, "WaterBranchTube", branchMesh, deferSave: true);
                        SetTubeRendererEnabled(rt.WaterRoot, "WaterBranchTube", true);
                        if (rt.UseBlocks)
                        {
                            rt.BlockCount += CaveBlockTunnelBuilder.Build(
                                rt.CavesRoot,
                                rt.BranchSpline,
                                rt.RockMat,
                                session.Request.Seed + 313,
                                new CaveBlockTunnelBuilder.Settings
                                {
                                    BlockSize = CaveBlockTunnelBuilder.Settings.Default.BlockSize,
                                    RingSpacing = 3.2f,
                                    InteriorHollow = CaveBlockTunnelBuilder.Settings.Default.InteriorHollow,
                                    AngularSteps = 10,
                                    FloorLayers = 1,
                                    CeilingLayers = 1,
                                    WallThickness = 1,
                                    MorphPosition = CaveBlockTunnelBuilder.Settings.Default.MorphPosition,
                                    MorphRotation = CaveBlockTunnelBuilder.Settings.Default.MorphRotation,
                                    MorphScaleMin = CaveBlockTunnelBuilder.Settings.Default.MorphScaleMin,
                                    MorphScaleMax = CaveBlockTunnelBuilder.Settings.Default.MorphScaleMax,
                                    OuterWallMinable = false
                                },
                                sectionName: "WaterBranch");
                            SetTubeRendererEnabled(rt.WaterRoot, "WaterBranchTube", true);
                        }

                        var endSample = rt.BranchSpline.SampleAtDistance(rt.BranchSpline.TotalLength);
                        var poolFloor = endSample.Position - endSample.Up * (endSample.RadiusY * 0.74f);
                        var fallSample = rt.BranchSpline.SampleAtDistance(rt.BranchSpline.TotalLength * 0.45f);
                        var waterAnchor = rt.CavesRoot.GetComponent<CaveWaterBranchAnchor>();
                        if (waterAnchor == null)
                            waterAnchor = rt.CavesRoot.gameObject.AddComponent<CaveWaterBranchAnchor>();
                        waterAnchor.SetBranchPositions(poolFloor, fallSample.Position);
                    }
                    else
                    {
                        CaveWaterUtility.ClearAllWater(rt.CavesRoot);
                    }

                    break;
                case 8:
                    if (session.Request.UseTerrainCarve)
                    {
                        CaveBuildActionPacing.TouchQueueActivity();
                        CaveTerrainIntegrationUtility.EnsureForGroundPlacement(
                            session.Ground, rt.CavesRoot, session.Request.Seed, out _);
                        CaveTerrainCarveUtility.CarveForCaveSystem(rt.CavesRoot, rt.Spline, rt.BranchSpline);
                        var terrain = ActiveSceneUtility.FindInActiveScene<Terrain>();
                        CaveTerrainUtility.ApplyCaveEntranceMouth(terrain, session.Request.Seed, rt.CavesRoot);
                        if (SurfaceWorldGenerator.FindCaveOpenings().Count > 0)
                        {
                            CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                                rt.CavesRoot, session.Ground, out _);
                        }
                        else
                        {
                            CaveGroundPlacementUtility.FinalizeGroundPlacement(
                                rt.CavesRoot, session.Ground, out _, session.Request.Seed);
                        }
                    }

                    FlushDeferredMeshAssets();
                    if (Cancelled(0.95f, "Finishing geometry…"))
                        return true;
                    CaveBuildActionPacing.TouchQueueActivity();
                    EnvironmentSceneUtility.MarkSceneDirty();
                    session.Report = new LavaTubeCaveBuildReport
                    {
                        PieceCount = rt.PieceCount + rt.BlockCount,
                        PathNodes = rt.PathNodes,
                        Message = rt.UseTrue3D
                            ? rt.UseAdventureHybrid
                                ? $"Adventure maze: flat floor/ceiling + {rt.BlockCount} minable block walls, {rt.MazeLayout?.JumpGapCells?.Count ?? 0} jump gaps."
                                : $"Maze volume cave ({rt.PieceCount} wall pieces) + grand cavern."
                            : rt.UseBlocks
                                ? $"Block tunnel ({rt.BlockCount} cubes) + terrain carve."
                                : "Organic spline mesh cave."
                    };
                    return true;
            }

            session.Chunk++;
            return session.Chunk >= CaveMonolithicGeneratePacing.SplineChunkCount;
        }
    }
}
#endif
