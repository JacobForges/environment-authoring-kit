#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Planner brief drives terrain heightmaps (plateau targets + relief) — not post-hoc GO mass.
    /// LiDAR runs as carve/rise sculpt; planner specs bias the result.
    /// </summary>
    public static class CaveBuildPlannerTerrainGuide
    {
        public struct TileSculptProfile
        {
            public float TargetSurfaceWorldY;
            public float MaxReliefMeters;
            public float MacroAmplitude;
        }

        static bool _active;
        static CaveBuildPlannerLayoutBridge.TechnicalSpecs _specs;
        static CaveBuildPlannerLayoutBridge.LayoutPlan _plan;
        static string _title;

        public static bool IsActive => _active;

        public static float ActiveMacroAmplitude { get; private set; } = -1f;

        const float WallHeightM = 2.1f;
        const float WallThicknessM = 1.35f;
        const float HopPadRadiusM = 1.15f;
        const float HopPadRiseM = 0.55f;
        const float BridgePadWidthM = 3.6f;
        const float BridgePadRiseM = 0.45f;

        static CaveBuildPlannerMazeTopology.Topology _mazeTopology;

        enum TileEdge
        {
            North,
            East,
            South,
            West,
        }

        public static bool UsesPlannerTerrainGenerator(WorldGenerationRequest request) =>
            CaveBuildSessionConfig.HasFinalizedActive &&
            (request == null || CaveBuildSessionConfig.IsSessionRequest(request)) &&
            CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out _) &&
            brief?.layoutPlan != null;

        public static bool TryBindSession(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            _active = false;
            _specs = null;
            _plan = null;
            _title = null;
            ActiveMacroAmplitude = -1f;

            if (!UsesPlannerTerrainGenerator(request))
                return false;

            if (!CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out _))
                return false;

            _specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(brief.layoutPlan);
            _plan = brief.layoutPlan;
            _mazeTopology = CaveBuildPlannerMazeTopology.Resolve(_plan);
            _title = brief.title;
            _active = true;
            ActiveMacroAmplitude = 0.06f;
            CaveBuildPlannerConceptGuide.TryBind(request);
            CaveBuildPlannerFidelityGate.ResetRetryState();

            SurfaceLidarGuidedSculptPolicy.PreferSculptOverStamp = true;
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            settings.lidarGuidedSculptOnly = true;
            settings.SaveToPrefs();

            var lidarNote = SurfaceDemGeoreferenceAuthor.TryLoadGeorefForSeed(request.Seed, out _, out _)
                ? string.Empty
                : " LiDAR hillshade thin — run: cd Tools/cave-grader && npm run sync-florida-hillshades";

            CaveBuildEditorLog.LogSurface(
                $"[Planner] Terrain generator — \"{_title}\" LiDAR sculpt + additive brief features only " +
                $"(maze walls / hop pads — no per-tile heightmap wipe; maze={_mazeTopology.Source})." +
                lidarNote,
                forceUnityConsole: true);
            return true;
        }

        public static void ClearSession()
        {
            _active = false;
            _specs = null;
            _plan = null;
            _mazeTopology = null;
            _title = null;
            ActiveMacroAmplitude = -1f;
            CaveBuildPlannerConceptGuide.ClearSession();
            CaveBuildPlannerFidelityGate.ResetRetryState();
        }

        public static bool TryGetTileProfile(
            Vector2Int gridOff,
            Terrain main,
            SceneGroundInfo ground,
            out TileSculptProfile profile)
        {
            profile = default;
            if (!_active || _specs == null || main == null)
                return false;

            var hubY = ground != null && ground.HasAnchor
                ? SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y
                : main.transform.position.y;

            if (IsCardinalIslandOffset(gridOff))
            {
                profile = new TileSculptProfile
                {
                    TargetSurfaceWorldY = hubY + _specs.plateauHeightM,
                    MaxReliefMeters = 2.5f,
                    MacroAmplitude = 0.14f,
                };
                return true;
            }

            if (!SurfaceTerrainTileExpansion.IsNineTileGameplayOffset(gridOff))
                return false;

            var isCorner = IsPlayCornerOffset(gridOff);
            profile = new TileSculptProfile
            {
                TargetSurfaceWorldY = hubY + (isCorner ? _specs.plateauHeightM : _specs.platformHeightM),
                MaxReliefMeters = isCorner ? 1.2f : 0.35f,
                MacroAmplitude = isCorner ? 0.1f : 0.04f,
            };
            return true;
        }

        static bool IsCardinalIslandOffset(Vector2Int off) =>
            off == new Vector2Int(0, 2) ||
            off == new Vector2Int(2, 0) ||
            off == new Vector2Int(0, -2) ||
            off == new Vector2Int(-2, 0);

        static bool IsPlayCornerOffset(Vector2Int off)
        {
            if (_plan?.playDisk != null && _plan.playDisk.labyrinth)
            {
                var row = 1 - off.y;
                var col = off.x + 1;
                return CaveBuildPlannerLayoutBridge.IsCornerCell(row, col);
            }

            return off.x != 0 && off.y != 0;
        }

        public static void ApplyPlateauToHeightmap(
            Terrain terrain,
            Vector2Int gridOff,
            SceneGroundInfo ground,
            Terrain main,
            int seed) =>
            QueueApplyPlateauToHeightmap(terrain, gridOff, ground, main, seed, null);

        public static void QueueApplyPlateauToHeightmap(
            Terrain terrain,
            Vector2Int gridOff,
            SceneGroundInfo ground,
            Terrain main,
            int seed,
            Action onComplete)
        {
            if (terrain?.terrainData == null || !TryGetTileProfile(gridOff, main, ground, out var profile))
            {
                onComplete?.Invoke();
                return;
            }

            var session = new PlateauHeightSession
            {
                Terrain = terrain,
                GridOff = gridOff,
                Ground = ground,
                Main = main,
                Seed = seed,
                Profile = profile,
                RowZ = 0,
                OnComplete = onComplete,
            };

            session.FloorNorm = Mathf.Clamp01(
                (profile.TargetSurfaceWorldY - terrain.transform.position.y) /
                Mathf.Max(terrain.terrainData.size.y, 0.01f));
            session.ReliefNorm = profile.MaxReliefMeters / Mathf.Max(terrain.terrainData.size.y, 0.01f);
            session.WallRiseNorm = WallHeightM / Mathf.Max(terrain.terrainData.size.y, 0.01f);
            session.HopRiseNorm = HopPadRiseM / Mathf.Max(terrain.terrainData.size.y, 0.01f);
            session.BridgeRiseNorm = BridgePadRiseM / Mathf.Max(terrain.terrainData.size.y, 0.01f);
            session.Res = terrain.terrainData.heightmapResolution;

            CaveBuildActionPacing.ScheduleLightChain(
                () => BeginPlateauHeightSession(session),
                CaveBuildPipelineDomains.SurfaceQueueLabel("planner brief sculpt (preserve heights)"));
        }

        static void BeginPlateauHeightSession(PlateauHeightSession session)
        {
            if (session?.Terrain?.terrainData == null)
            {
                session?.OnComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.TouchQueueActivity();
            var res = session.Res;
            // Preserve LiDAR / grid-lay heights — never replace the full heightmap per tile (causes seam cliffs).
            session.Heights = session.Terrain.terrainData.GetHeights(0, 0, res, res);
            session.RowZ = res;
            QueueFinalizePlateauHeightmap(session);
        }

        sealed class PlateauHeightSession
        {
            public Terrain Terrain;
            public Vector2Int GridOff;
            public SceneGroundInfo Ground;
            public Terrain Main;
            public int Seed;
            public TileSculptProfile Profile;
            public float[,] Heights;
            public int Res;
            public int RowZ;
            public float FloorNorm;
            public float ReliefNorm;
            public float WallRiseNorm;
            public float HopRiseNorm;
            public float BridgeRiseNorm;
            public Action OnComplete;
            public List<WallSculptOp> FinalizeWalls;
            public List<CaveBuildPlannerLayoutBridge.Marker> FinalizeMarkers;
            public int FinalizeWallIndex;
            public int FinalizeMarkerIndex;
            public bool FinalizeIslandBridgeDone;
            public int MazeWallCount;
            public int HopPadCount;
        }

        struct WallSculptOp
        {
            public TileEdge Edge;
            public bool GapCenter;
        }

        static void QueueFinalizePlateauHeightmap(PlateauHeightSession session)
        {
            session.FinalizeWalls = BuildMazeWallOps(session.GridOff);
            session.FinalizeMarkers = CollectHopMarkersInFootprint(session.Terrain, session.Main, session.Ground);
            session.FinalizeWallIndex = 0;
            session.FinalizeMarkerIndex = 0;
            session.FinalizeIslandBridgeDone = !IsCardinalIslandOffset(session.GridOff);
            session.MazeWallCount = 0;
            session.HopPadCount = 0;

            CaveBuildActionPacing.ScheduleLightChain(
                () => RunFinalizePlateauStep(session),
                CaveBuildPipelineDomains.SurfaceQueueLabel("planner brief sculpt finalize"));
        }

        static void RunFinalizePlateauStep(PlateauHeightSession session)
        {
            if (session?.Terrain?.terrainData == null || session.Heights == null)
            {
                session?.OnComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.TouchQueueActivity();
            var terrain = session.Terrain;
            var heights = session.Heights;

            if (session.FinalizeWalls != null && session.FinalizeWallIndex < session.FinalizeWalls.Count)
            {
                var op = session.FinalizeWalls[session.FinalizeWallIndex++];
                session.MazeWallCount += SculptWallEdge(
                    heights, terrain, op.Edge, session.FloorNorm, session.WallRiseNorm, op.GapCenter);
                CaveBuildActionPacing.ScheduleLight(
                    () => RunFinalizePlateauStep(session),
                    CaveBuildPipelineDomains.SurfaceQueueLabel("planner brief maze wall"));
                return;
            }

            if (session.FinalizeMarkers != null && session.FinalizeMarkerIndex < session.FinalizeMarkers.Count)
            {
                var m = session.FinalizeMarkers[session.FinalizeMarkerIndex++];
                session.HopPadCount += SculptHopMarker(
                    heights, terrain, session.Main, session.Ground, m,
                    session.FloorNorm, session.HopRiseNorm, session.BridgeRiseNorm);
                CaveBuildActionPacing.ScheduleLight(
                    () => RunFinalizePlateauStep(session),
                    CaveBuildPipelineDomains.SurfaceQueueLabel("planner brief hop pad"));
                return;
            }

            if (!session.FinalizeIslandBridgeDone)
            {
                session.FinalizeIslandBridgeDone = true;
                session.HopPadCount += ApplyIslandBridgeLanding(
                    heights, terrain, session.Main, session.Ground, session.GridOff,
                    session.FloorNorm, session.BridgeRiseNorm);
                CaveBuildActionPacing.ScheduleLight(
                    () => RunFinalizePlateauStep(session),
                    CaveBuildPipelineDomains.SurfaceQueueLabel("planner brief island bridge"));
                return;
            }

            CaveEditorUndo.RecordObject(terrain.terrainData, "Planner terrain sculpt");
            CaveBuildMicroTerrainHeightmap.QueueWriteFull(
                terrain,
                heights,
                "planner terrain",
                "commit",
                () => CompletePlateauHeightmap(session));
        }

        static void CompletePlateauHeightmap(PlateauHeightSession session)
        {
            if (session.MazeWallCount > 0 || session.HopPadCount > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Planner] {session.Terrain.name} — {session.MazeWallCount} maze wall strip(s), " +
                    $"{session.HopPadCount} hop/bridge pad(s) sculpted.",
                    forceUnityConsole: false);
            }

            session.OnComplete?.Invoke();
        }

        static List<WallSculptOp> BuildMazeWallOps(Vector2Int gridOff)
        {
            var ops = new List<WallSculptOp>(12);
            if (_plan?.playDisk == null || !_plan.playDisk.labyrinth ||
                !SurfaceTerrainTileExpansion.IsNineTileGameplayOffset(gridOff))
                return ops;

            var row = 1 - gridOff.y;
            var col = gridOff.x + 1;
            var topo = _mazeTopology ?? CaveBuildPlannerMazeTopology.Resolve(_plan);

            if (row > 0 && CaveBuildPlannerMazeTopology.HasHorizontalWall(topo, row - 1, col))
                ops.Add(new WallSculptOp { Edge = TileEdge.North, GapCenter = false });
            if (row < 2 && CaveBuildPlannerMazeTopology.HasHorizontalWall(topo, row, col))
                ops.Add(new WallSculptOp { Edge = TileEdge.South, GapCenter = false });
            if (col > 0 && CaveBuildPlannerMazeTopology.HasVerticalWall(topo, row, col - 1))
                ops.Add(new WallSculptOp { Edge = TileEdge.West, GapCenter = false });
            if (col < 2 && CaveBuildPlannerMazeTopology.HasVerticalWall(topo, row, col))
                ops.Add(new WallSculptOp { Edge = TileEdge.East, GapCenter = false });

            if (row == 0)
                ops.Add(new WallSculptOp { Edge = TileEdge.North, GapCenter = col == 1 });
            if (row == 2)
                ops.Add(new WallSculptOp { Edge = TileEdge.South, GapCenter = col == 1 });
            if (col == 0)
                ops.Add(new WallSculptOp { Edge = TileEdge.West, GapCenter = row == 1 });
            if (col == 2)
                ops.Add(new WallSculptOp { Edge = TileEdge.East, GapCenter = row == 1 });

            return ops;
        }

        static List<CaveBuildPlannerLayoutBridge.Marker> CollectHopMarkersInFootprint(
            Terrain terrain,
            Terrain main,
            SceneGroundInfo ground)
        {
            var markers = new List<CaveBuildPlannerLayoutBridge.Marker>();
            if (_plan?.markers == null || main?.terrainData == null)
                return markers;

            var tileSize = main.terrainData.size;
            var hubY = ground != null && ground.HasAnchor
                ? SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y
                : main.transform.position.y;

            foreach (var m in _plan.markers)
            {
                if (m == null || !CaveBuildPlannerLayoutBridge.IsPlatformMarker(m))
                    continue;
                if (!TryResolveHopMarkerWorld(main, ground, m, hubY, tileSize, out var world))
                    continue;
                if (!WorldPointOnTerrain(terrain, world))
                    continue;
                markers.Add(m);
            }

            return markers;
        }

        static int SculptHopMarker(
            float[,] heights,
            Terrain terrain,
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.Marker m,
            float floorNorm,
            float hopRiseNorm,
            float bridgeRiseNorm)
        {
            if (m == null || main?.terrainData == null)
                return 0;

            var tileSize = main.terrainData.size;
            var hubY = ground != null && ground.HasAnchor
                ? SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y
                : main.transform.position.y;
            if (!TryResolveHopMarkerWorld(main, ground, m, hubY, tileSize, out var world))
                return 0;

            var label = m.label ?? string.Empty;
            var isBridge = label.IndexOf("bridge", StringComparison.OrdinalIgnoreCase) >= 0;
            return isBridge
                ? SculptBridgePad(heights, terrain, world, ResolveGapLeg(label), floorNorm, bridgeRiseNorm)
                : SculptRaiseDisc(heights, terrain, world, HopPadRadiusM, floorNorm, hopRiseNorm);
        }

        static int SculptWallEdge(
            float[,] heights,
            Terrain terrain,
            TileEdge edge,
            float floorNorm,
            float wallRiseNorm,
            bool gapCenter)
        {
            var data = terrain.terrainData;
            var res = heights.GetLength(0);
            var size = data.size;
            var thickU = WallThicknessM / Mathf.Max(size.x, 0.01f);
            var gapHalfU = gapCenter ? 0.18f : 0f;

            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var u = x / (float)(res - 1);
                    var v = z / (float)(res - 1);
                    if (!IsOnEdgeStrip(edge, u, v, thickU, gapHalfU))
                        continue;

                    heights[z, x] = Mathf.Clamp01(heights[z, x] + wallRiseNorm);
                }
            }

            return 1;
        }

        static bool IsOnEdgeStrip(TileEdge edge, float u, float v, float thickU, float gapHalfU)
        {
            bool InGap(float t) => gapHalfU > 0f && Mathf.Abs(t - 0.5f) < gapHalfU;

            return edge switch
            {
                TileEdge.North => v >= 1f - thickU && !InGap(u),
                TileEdge.South => v <= thickU && !InGap(u),
                TileEdge.East => u >= 1f - thickU && !InGap(v),
                TileEdge.West => u <= thickU && !InGap(v),
                _ => false,
            };
        }

        static bool WorldPointOnTerrain(Terrain terrain, Vector3 world)
        {
            if (terrain?.terrainData == null)
                return false;

            var pos = terrain.transform.position;
            var size = terrain.terrainData.size;
            return world.x >= pos.x && world.x <= pos.x + size.x &&
                   world.z >= pos.z && world.z <= pos.z + size.z;
        }

        static int ApplyIslandBridgeLanding(
            float[,] heights,
            Terrain terrain,
            Terrain main,
            SceneGroundInfo ground,
            Vector2Int islandOff,
            float floorNorm,
            float bridgeRiseNorm)
        {
            if (_plan?.markers == null)
                return 0;

            var leg = islandOff.y > 0 ? "N" : islandOff.y < 0 ? "S" : islandOff.x > 0 ? "E" : "W";
            var tileSize = main.terrainData.size;
            var hubY = ground != null && ground.HasAnchor
                ? SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y
                : main.transform.position.y;
            var center = terrain.transform.position + new Vector3(tileSize.x * 0.5f, 0f, tileSize.z * 0.5f);
            var y = terrain.transform.position.y + (floorNorm * terrain.terrainData.size.y) + 0.35f;
            var world = new Vector3(center.x, y, center.z);
            world += EdgeOffset(Vector2Int.zero, tileSize, $"-{leg}-bridge") * -1.15f;

            return SculptBridgePad(heights, terrain, world, leg, floorNorm, bridgeRiseNorm);
        }

        static int SculptRaiseDisc(
            float[,] heights,
            Terrain terrain,
            Vector3 world,
            float radiusM,
            float floorNorm,
            float riseNorm)
        {
            var data = terrain.terrainData;
            var res = heights.GetLength(0);
            var size = data.size;
            var pos = terrain.transform.position;
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = pos.x + x / (float)(res - 1) * size.x;
                    var wz = pos.z + z / (float)(res - 1) * size.z;
                    var dx = wx - world.x;
                    var dz = wz - world.z;
                    var dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > radiusM)
                        continue;

                    var t = 1f - dist / radiusM;
                    heights[z, x] = Mathf.Clamp01(heights[z, x] + riseNorm * (t * t));
                }
            }

            return 1;
        }

        static int SculptBridgePad(
            float[,] heights,
            Terrain terrain,
            Vector3 world,
            string leg,
            float floorNorm,
            float riseNorm)
        {
            var data = terrain.terrainData;
            var res = heights.GetLength(0);
            var size = data.size;
            var pos = terrain.transform.position;
            var halfW = BridgePadWidthM * 0.5f;
            var halfL = Mathf.Max(_specs?.platformHopM ?? 2.5f, 2f) * 0.55f;
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = pos.x + x / (float)(res - 1) * size.x;
                    var wz = pos.z + z / (float)(res - 1) * size.z;
                    var dx = wx - world.x;
                    var dz = wz - world.z;
                    var inside = leg switch
                    {
                        "N" or "S" => Mathf.Abs(dx) <= halfW && Mathf.Abs(dz) <= halfL,
                        "E" or "W" => Mathf.Abs(dz) <= halfW && Mathf.Abs(dx) <= halfL,
                        _ => Mathf.Abs(dx) <= halfW && Mathf.Abs(dz) <= halfL,
                    };
                    if (!inside)
                        continue;

                    heights[z, x] = Mathf.Clamp01(heights[z, x] + riseNorm);
                }
            }

            return 1;
        }

        static bool TryResolveHopMarkerWorld(
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.Marker m,
            float hubY,
            Vector3 tileSize,
            out Vector3 world)
        {
            world = Vector3.zero;
            if (m == null || !string.Equals(m.zone, "play", StringComparison.OrdinalIgnoreCase))
                return false;

            var off = CaveBuildPlannerLayoutBridge.PlayRowColToOffset(m.row, m.col);
            var y = hubY + (CaveBuildPlannerLayoutBridge.IsCornerCell(m.row, m.col)
                ? _specs.plateauHeightM
                : _specs.platformHeightM);
            var tileCenter = TileCenterXZ(main, off, y);
            var label = m.label ?? string.Empty;
            var slot = Mathf.Max(0, m.slot);
            Vector3 offset;

            if (label.IndexOf("-gap-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var leg = ResolveGapLeg(label);
                var steps = Mathf.Max(2, slot + 1);
                var t = steps <= 1 ? 0.5f : slot / (float)(steps - 1);
                offset = LegOffset(leg, tileSize, t);
                if (_specs != null && _specs.hubGapM > 0.1f)
                {
                    var outward = EdgeOffset(off, tileSize, $"-{leg}-");
                    offset += outward.normalized * (_specs.hubGapM * 0.22f * slot);
                }
            }
            else
            {
                offset = EdgeOffset(off, tileSize, label);
                if (_specs != null && label.IndexOf("bridge", StringComparison.OrdinalIgnoreCase) >= 0)
                    offset += EdgeOffset(off, tileSize, label).normalized * (_specs.hubGapM * 0.35f);
            }

            world = new Vector3(tileCenter.x + offset.x, y + 0.35f, tileCenter.z + offset.z);
            return true;
        }

        static Vector3 TileCenterXZ(Terrain main, Vector2Int off, float y)
        {
            var origin = SurfaceTerrainGridRegistry.ExpectedOrigin(main, off);
            var size = main.terrainData.size;
            return new Vector3(origin.x + size.x * 0.5f, y, origin.z + size.z * 0.5f);
        }

        static Vector3 LegOffset(string leg, Vector3 tileSize, float t)
        {
            t = Mathf.Clamp01(t);
            return leg switch
            {
                "N" => new Vector3(Mathf.Lerp(-tileSize.x * 0.45f, tileSize.x * 0.45f, t), 0f, tileSize.z * 0.42f),
                "S" => new Vector3(Mathf.Lerp(tileSize.x * 0.45f, -tileSize.x * 0.45f, t), 0f, -tileSize.z * 0.42f),
                "E" => new Vector3(tileSize.x * 0.42f, 0f, Mathf.Lerp(-tileSize.z * 0.45f, tileSize.z * 0.45f, t)),
                "W" => new Vector3(-tileSize.x * 0.42f, 0f, Mathf.Lerp(tileSize.z * 0.45f, -tileSize.z * 0.45f, t)),
                _ => Vector3.zero,
            };
        }

        static string ResolveGapLeg(string label)
        {
            if (label.IndexOf("-N-", StringComparison.OrdinalIgnoreCase) >= 0) return "N";
            if (label.IndexOf("-S-", StringComparison.OrdinalIgnoreCase) >= 0) return "S";
            if (label.IndexOf("-E-", StringComparison.OrdinalIgnoreCase) >= 0) return "E";
            if (label.IndexOf("-W-", StringComparison.OrdinalIgnoreCase) >= 0) return "W";
            return "N";
        }

        static Vector3 EdgeOffset(Vector2Int tileOff, Vector3 tileSize, string label)
        {
            if (label.IndexOf("-N-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(0f, 0f, tileSize.z * 0.45f);
            if (label.IndexOf("-S-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(0f, 0f, -tileSize.z * 0.45f);
            if (label.IndexOf("-E-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(tileSize.x * 0.45f, 0f, 0f);
            if (label.IndexOf("-W-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(-tileSize.x * 0.45f, 0f, 0f);
            return Vector3.zero;
        }

        public static void QueueTilePipeline(
            Terrain terrain,
            Vector2Int gridOff,
            SceneGroundInfo ground,
            Terrain main,
            WorldGenerationRequest request,
            Action onComplete)
        {
            if (terrain == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!_active)
                TryBindSession(request, ground);

            var center = SurfaceTerrainPlayRegion.TerrainTileCenter(terrain);
            var extent = terrain.terrainData != null
                ? Mathf.Max(terrain.terrainData.size.x, terrain.terrainData.size.z)
                : request.SurfaceExtentMeters;

            ActiveMacroAmplitude = TryGetTileProfile(gridOff, main, ground, out var profile)
                ? profile.MacroAmplitude
                : 0.08f;

            SurfaceDemGeoreferenceAuthor.QueueApplyLidarGuidedLandscapeSculpt(
                terrain,
                center,
                extent,
                request.Seed + gridOff.x * 131 + gridOff.y * 313,
                _ =>
                {
                    QueueApplyPlateauToHeightmap(terrain, gridOff, ground, main, request.Seed, () =>
                    {
                        ActiveMacroAmplitude = -1f;
                        onComplete?.Invoke();
                    });
                });
        }

        public static void QueueMainTerrainPipeline(
            Terrain terrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Vector3 center,
            float extent,
            int terrainPasses,
            Action onComplete)
        {
            if (terrain == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            TryBindSession(request, ground);
            ActiveMacroAmplitude = TryGetTileProfile(Vector2Int.zero, terrain, ground, out var profile)
                ? profile.MacroAmplitude
                : 0.08f;

            SurfaceDemGeoreferenceAuthor.QueueApplyLidarGuidedLandscapeSculpt(
                terrain,
                center,
                extent,
                request.Seed,
                _ =>
                {
                    QueueApplyPlateauToHeightmap(terrain, Vector2Int.zero, ground, terrain, request.Seed, () =>
                    {
                        var passes = SurfaceTerrainCenteredAuthor.ResolvePassCount(terrainPasses);
                        ActiveMacroAmplitude = profile.MacroAmplitude;
                        SurfaceTerrainCenteredAuthor.QueueCenteredPasses(
                            terrain,
                            center,
                            extent,
                            request.Seed,
                            mountains: false,
                            water: false,
                            roads: false,
                            preserveInnerRadiusMeters: extent * 0.35f,
                            passCount: passes,
                            refinementAfterAuthoritativeDem: false,
                            onComplete: () =>
                            {
                                ActiveMacroAmplitude = -1f;
                                onComplete?.Invoke();
                            });
                    });
                });
        }
    }
}
#endif
