#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static partial class LavaTubeCaveGenerator
    {
        internal sealed class LavaTubeMonolithicRuntime
        {
            public LavaTubePrefabCatalog Catalog;
            public System.Random Rng;
            public Transform CavesRoot;
            public Transform TunnelRoot;
            public Transform ChamberRoot;
            public Transform WaterRoot;
            public Transform DetailRoot;
            public Transform PropsRoot;
            public Transform Entrance;
            public CaveSplinePath Spline;
            public List<CavePathKnot> Knots;
            public List<Vector3> Nodes = new();
            public List<Vector3> ChamberCenters = new();
            public int PieceCount;
            public int MinablePlaced;
            public int TargetMinables;
            public float WaterDist;
            public float StepLen;
            public float WaterAtSeg;
            public int TunnelDistIndex;
            public int ChamberIndex;
        }

        internal static bool TryAdvanceMonolithicChunk(CaveMonolithicGeneratePacing.Session session)
        {
            var rt = session.LavaTubeRuntime ??= new LavaTubeMonolithicRuntime();
            switch (session.Chunk)
            {
                case 0:
                    rt.Catalog = LavaTubePrefabCatalog.Load();
                    if (!rt.Catalog.IsValid)
                    {
                        session.Report = new LavaTubeCaveBuildReport { Message = "Prefab catalog empty." };
                        return true;
                    }

                    CaveBuildActionPacing.TouchQueueActivity();
                    rt.Rng = new System.Random(session.Request.Seed);
                    rt.CavesRoot = EnvironmentSceneUtility.GetOrCreateChild(
                        session.EnvironmentRoot, "LavaTubeCaveSystem");
                    CaveBuildSceneUtility.ClearChildrenFast(rt.CavesRoot);
                    var entranceForward = session.Ground.HasAnchor
                        ? session.Ground.HorizontalForward
                        : Vector3.forward;
                    var origin = SplineLavaTubeCaveGenerator.GetEntranceWorldPosition(session.Ground);
                    rt.CavesRoot.position = origin;
                    rt.CavesRoot.rotation = Quaternion.LookRotation(entranceForward, Vector3.up);
                    rt.TunnelRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Tunnels");
                    rt.ChamberRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Chambers");
                    rt.WaterRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Water");
                    rt.DetailRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Details");
                    rt.Entrance = EnvironmentSceneUtility.GetOrCreateChild(rt.CavesRoot, "Entrance");
                    BuildEntrance(rt.Entrance, rt.Catalog, rt.Rng, Vector3.down);
                    rt.StepLen = session.Request.CavePathStepLength > 0f
                        ? session.Request.CavePathStepLength
                        : 11f;
                    var drop = session.Request.CavePathDropPerStep > 0f ? session.Request.CavePathDropPerStep : 0.32f;
                    var yawVar = session.Request.CavePathYawVariance > 0f ? session.Request.CavePathYawVariance : 22f;
                    var chamberMul = session.Request.CaveChamberSizeMultiplier > 0f
                        ? session.Request.CaveChamberSizeMultiplier
                        : 2.35f;
                    rt.Knots = CavePathFactory.BuildDescendingPath(
                        session.Request.CaveTunnelSegments,
                        session.Request.CaveChamberCount,
                        session.Request.Seed,
                        rt.StepLen,
                        drop,
                        yawVar,
                        chamberMul,
                        session.Request.CaveEntranceYawDegrees);
                    rt.Knots[0] = new CavePathKnot(
                        new Vector3(2f, 0.45f, 6f), rt.Knots[0].RadiusX, rt.Knots[0].RadiusY, false);
                    rt.Spline = new CaveSplinePath();
                    rt.Spline.SetKnots(rt.Knots);
                    rt.TargetMinables = session.Request.CaveMinableTarget > 0
                        ? session.Request.CaveMinableTarget
                        : 12;
                    rt.WaterAtSeg = session.Request.CaveWaterBranchSegment >= 0
                        ? session.Request.CaveWaterBranchSegment
                        : session.Request.CaveTunnelSegments / 2;
                    rt.PropsRoot = EnvironmentSceneUtility.GetOrCreateChild(rt.DetailRoot, "Props");
                    rt.ChamberCenters.Clear();
                    foreach (var knot in rt.Knots)
                    {
                        if (knot.IsChamber)
                            rt.ChamberCenters.Add(knot.Position);
                    }

                    var tunnelDims = new CaveSeamlessTunnelBuilder.TunnelDimensions
                    {
                        Width = TunnelWidth,
                        Height = TunnelHeight,
                        NavClearance = NavClearance
                    };
                    var pathStart = rt.Spline.SampleAtDistance(0f);
                    rt.PieceCount = CaveSeamlessTunnelBuilder.BridgeEntranceToPath(
                        rt.TunnelRoot, rt.Catalog, rt.Rng, new Vector3(1f, 0f, 4f), pathStart, tunnelDims);
                    rt.PieceCount += CaveSeamlessTunnelBuilder.BuildAlongSpline(
                        rt.TunnelRoot, rt.Catalog, rt.Rng, rt.Spline, tunnelDims, rt.ChamberCenters,
                        ChamberSize * 0.55f, placeLights: true);
                    rt.WaterDist = rt.WaterAtSeg * rt.StepLen;
                    rt.TunnelDistIndex = 0;
                    rt.MinablePlaced = 0;
                    break;
                case 1:
                    CaveBuildActionPacing.TouchQueueActivity();
                    const float distStep = 5.5f;
                    var dist = rt.TunnelDistIndex * distStep;
                    var processed = 0;
                    while (dist < rt.Spline.TotalLength && processed < 4)
                    {
                        var sample = rt.Spline.SampleAtDistance(dist);
                        rt.Nodes.Add(sample.Position);
                        ScatterSegmentProps(
                            rt.PropsRoot, rt.Catalog, rt.Rng, sample.Position,
                            sample.Position + sample.Tangent * 2f, 1);
                        if (Mathf.Abs(dist - rt.WaterDist) < rt.StepLen * 0.6f)
                        {
                            rt.PieceCount += BuildWaterBranch(
                                rt.WaterRoot, rt.TunnelRoot, rt.Catalog, rt.Rng,
                                sample.Position, sample.Tangent, session.Request.CaveWaterBranchYaw);
                        }

                        var ringIndex = Mathf.FloorToInt(dist / distStep);
                        if (rt.MinablePlaced < rt.TargetMinables && ringIndex % 2 == 0)
                        {
                            PlaceMinableRock(
                                rt.DetailRoot, rt.Catalog, rt.Rng,
                                sample.Position + sample.Right * (TunnelWidth * 0.35f) + Vector3.up * 0.5f);
                            rt.MinablePlaced++;
                        }

                        dist += distStep;
                        rt.TunnelDistIndex++;
                        processed++;
                    }

                    if (dist < rt.Spline.TotalLength)
                        return false;
                    break;
                case 2:
                    CaveBuildActionPacing.TouchQueueActivity();
                    while (rt.ChamberIndex < rt.Knots.Count)
                    {
                        var knot = rt.Knots[rt.ChamberIndex];
                        rt.ChamberIndex++;
                        if (!knot.IsChamber)
                            continue;

                        var sample = rt.Spline.SampleAtDistance(FindClosestDistance(rt.Spline, knot.Position));
                        rt.PieceCount += BuildNaturalChamber(
                            rt.ChamberRoot, rt.Catalog, rt.Rng, rt.PropsRoot,
                            sample.Position, sample.Tangent, rt.ChamberIndex);
                        rt.MinablePlaced += PlaceMinableCluster(
                            rt.ChamberRoot, rt.Catalog, rt.Rng, sample.Position, 2);
                        ScatterChamberProps(rt.PropsRoot, rt.Catalog, rt.Rng, sample.Position, sample.Tangent, 5);
                        rt.Nodes.Add(sample.Position);
                        break;
                    }

                    if (rt.ChamberIndex < rt.Knots.Count)
                        return false;

                    while (rt.MinablePlaced < rt.TargetMinables && rt.Nodes.Count > 0)
                    {
                        var node = rt.Nodes[rt.Rng.Next(rt.Nodes.Count)];
                        PlaceMinableRock(
                            rt.DetailRoot, rt.Catalog, rt.Rng,
                            node + new Vector3(0f, 0.5f, (float)(rt.Rng.NextDouble() * 2 - 1) * 2f));
                        rt.MinablePlaced++;
                        if (rt.MinablePlaced >= rt.TargetMinables)
                            break;
                    }

                    ScatterArtifacts(rt.DetailRoot, rt.Catalog, rt.Rng, rt.Nodes);
                    PlaceEntranceMarker(rt.CavesRoot, rt.Spline.SampleAtDistance(Mathf.Min(4f, rt.Spline.TotalLength)).Tangent);
                    break;
                case 3:
                    CaveBuildActionPacing.TouchQueueActivity();
                    var authoring = rt.CavesRoot.gameObject.GetComponent<CaveSplinePathAuthoring>();
                    if (authoring == null)
                        authoring = Undo.AddComponent<CaveSplinePathAuthoring>(rt.CavesRoot.gameObject);
                    authoring.SetPath(rt.Knots, rt.Spline.TotalLength);
                    SplineCaveSpawnAligner.AlignEntranceSpawn(rt.CavesRoot, rt.Entrance, rt.Spline);
                    EnvironmentSceneUtility.MarkSceneDirty();
                    session.Report = new LavaTubeCaveBuildReport
                    {
                        PieceCount = rt.PieceCount,
                        PathNodes = rt.Nodes,
                        Message = $"Seamless tunnel rings along spline, seed {session.Request.Seed}."
                    };
                    return true;
            }

            session.Chunk++;
            return session.Chunk >= CaveMonolithicGeneratePacing.LavaTubeChunkCount;
        }
    }
}
#endif
