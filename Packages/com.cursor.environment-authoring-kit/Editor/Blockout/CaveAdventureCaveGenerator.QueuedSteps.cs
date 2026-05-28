using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Incremental adventure cave geometry — one heavy editor-queue step per call.</summary>
    public static partial class CaveAdventureCaveGenerator
    {
        public sealed class QueuedBuildState
        {
            public Transform EnvironmentRoot;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public LavaTubePrefabCatalog Catalog;
            public System.Random Rng;
            public System.Func<float, string, bool> ReportProgress;
            public Transform CavesRoot;
            public Transform Geometry;
            public CaveMazeLayout MazeLayout;
            public CaveSplinePath Spline;
            public Material RockMat;
            public Material FloorMat;
            public int PlatformCount;
            public int BlockCount;
            public int FeatureCount;
            public int TerrainPieces;
            public Transform BlockSection;
            public int BlockRingIndex;
            public int BlockRingCount;
            public CaveBlockTunnelBuilder.Settings BlockSettings;
        }

        public const int BlockRingBatchSize = CaveAdventureBlockBuilder.DefaultRingBatchSize;

        public static QueuedBuildState BeginQueued(
            Transform environmentRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubePrefabCatalog catalog,
            System.Func<float, string, bool> reportProgress = null) =>
            new QueuedBuildState
            {
                EnvironmentRoot = environmentRoot,
                Ground = ground,
                Request = request,
                Catalog = catalog,
                Rng = new System.Random(request.Seed),
                ReportProgress = reportProgress,
            };

        /// <summary>Materials are deferred from validate init so adventure state init does not scan/upgrade the whole pack on one frame.</summary>
        public static void EnsureQueuedMaterials(QueuedBuildState s)
        {
            if (s == null || s.Request == null)
                return;

            if (s.RockMat == null)
                s.RockMat = CreateStyledRockMaterial(s.Request);
            if (s.FloorMat == null)
                s.FloorMat = CreateStyledFloorMaterial(s.Request);
        }

        static Material CreateStyledRockMaterial(WorldGenerationRequest request)
        {
            var mat = new Material(CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial());
            var rng = new System.Random(request.Seed + 901);
            var style = string.IsNullOrEmpty(request.BuildVisualStyle)
                ? CaveBuildStylePalette.Classic
                : request.BuildVisualStyle;
            CaveBuildStylePalette.ApplyRockTint(mat, style, rng);
            return mat;
        }

        static Material CreateStyledFloorMaterial(WorldGenerationRequest request)
        {
            var mat = new Material(CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial());
            var rng = new System.Random(request.Seed + 902);
            var style = string.IsNullOrEmpty(request.BuildVisualStyle)
                ? CaveBuildStylePalette.Classic
                : request.BuildVisualStyle;
            CaveBuildStylePalette.ApplyFloorTint(mat, style, rng);
            return mat;
        }

        static bool Cancelled(QueuedBuildState s, float t, string label) =>
            s.ReportProgress != null && s.ReportProgress(t, label);

        public static bool QueuedStepClear(QueuedBuildState s)
        {
            if (Cancelled(s, 0.02f, "Clearing previous cave…"))
                return true;

            s.CavesRoot = GetOrCreateCaveSystemRoot(s.EnvironmentRoot);
            CaveBuildSceneUtility.ClearChildrenFast(s.CavesRoot);
            CaveLegacyGeometryPurge.Purge(s.CavesRoot);

            s.CavesRoot.position = SplineLavaTubeCaveGenerator.GetEntranceWorldPosition(s.Ground);
            var entranceForward = s.Ground.HasAnchor ? s.Ground.HorizontalForward : Vector3.forward;
            if (entranceForward.sqrMagnitude < 0.01f)
                entranceForward = Vector3.forward;
            entranceForward.y = 0f;
            entranceForward.Normalize();
            var baseYaw = Quaternion.LookRotation(entranceForward, Vector3.up);
            var rollYaw = Quaternion.Euler(0f, s.Request.CaveEntranceYawDegrees, 0f);
            s.CavesRoot.rotation = baseYaw * rollYaw;
            s.CavesRoot.localScale = Vector3.one;

            s.Geometry = EnsureGeometryRoot(s.CavesRoot);
            CaveEnclosureShellBuilder.PurgeLayerOffenders(s.Geometry);

            var meshRoot = EnvironmentSceneUtility.GetOrCreateChild(s.CavesRoot, "SplineMesh");
            meshRoot.localPosition = Vector3.zero;
            meshRoot.localRotation = Quaternion.identity;
            meshRoot.localScale = Vector3.one;
            SplineLavaTubeCaveGenerator.ClearLegacyTubeMeshes(meshRoot);
            EnvironmentSceneUtility.GetOrCreateChild(s.CavesRoot, "Details");
            return false;
        }

        public static bool QueuedStepEntrance(QueuedBuildState s)
        {
            if (Cancelled(s, 0.1f, "Entrance…"))
                return true;

            var entrance = EnvironmentSceneUtility.GetOrCreateChild(s.CavesRoot, "Entrance");
            entrance.localPosition = new Vector3(0f, CaveGeometryPaths.UndergroundDepthMeters, 0f);
            LavaTubeCaveGenerator.BuildEntranceForPipeline(entrance, s.Catalog, s.Rng);
            CavePrefabInstanceUtility.RestoreMissingPrefabVisuals(entrance);
            LavaTubeCaveGenerator.EnsureEntranceMarker(s.CavesRoot);
            return false;
        }

        public static bool QueuedStepMaze(QueuedBuildState s)
        {
            if (Cancelled(s, 0.2f, "Maze layout (3D grid)…"))
                return true;

            s.MazeLayout = CaveMazeLayoutGenerator.Generate(
                s.Request.Seed,
                s.Request.CaveTunnelSegments,
                s.Request.CaveChamberCount,
                s.Request.MazeGenFlavor);

            var meta = s.CavesRoot != null ? s.CavesRoot.GetComponent<CaveBuildMetadata>() : null;
            if (meta == null && s.CavesRoot != null)
                meta = s.CavesRoot.gameObject.AddComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                meta.Set(s.Request.Seed, s.Request.CaveTunnelSegments, s.Request.CaveChamberCount, true);
                meta.buildVisualStyle = s.Request.BuildVisualStyle;
                meta.mazeGenFlavor = s.Request.MazeGenFlavor;
            }
            s.Spline = new CaveSplinePath();
            s.Spline.SetKnots(s.MazeLayout.PathKnots);
            return false;
        }

        /// <summary>Creates scene Terrain when missing so carve, mouth, and ground_placement can run.</summary>
        public static bool QueuedStepAddTerrain(QueuedBuildState s)
        {
            if (Cancelled(s, 0.28f, "Phase: ensure terrain (cave carve)…"))
                return true;

            if (s.Request == null || !s.Request.UseTerrainCarve)
                return false;

            if (s.CavesRoot == null)
                s.CavesRoot = GetOrCreateCaveSystemRoot(s.EnvironmentRoot);

            CaveBuildPipelineLog.Info("Cave geo phase 4: ensure terrain extends from main land.", "Cave-Geo");

            var terrain = CaveBuildTerrainEnsure.TryEnsure(
                s.Ground,
                s.CavesRoot,
                s.Request.Seed,
                out var ensureMsg);

            if (terrain != null)
            {
                var extent = Mathf.Clamp(s.Request.SurfaceExtentMeters, 80f, 512f);
                if (s.Ground.HasAnchor)
                {
                    CaveBuildPipelineLog.Info("Cave geo phase 4b: LiDAR stamp at mouth (additive outward).", "Cave-Geo");
                    SurfaceDemGeoreferenceAuthor.ApplyGeoreferencedStamp(
                        terrain,
                        s.Ground.Anchor.position,
                        extent,
                        s.Request.Seed,
                        out var lidarMsg);
                    if (!string.IsNullOrEmpty(lidarMsg))
                        Debug.Log("[CaveBuild] " + lidarMsg);
                }

                Debug.Log(
                    $"[CaveBuild] Scene terrain ready: {terrain.name}" +
                    (string.IsNullOrEmpty(ensureMsg) ? " (existing)." : $" — {ensureMsg}"));
            }
            else
            {
                Debug.LogWarning(
                    "[CaveBuild] Add terrain step could not create Terrain — " +
                    (ensureMsg ?? "see Environment Kit Ground tag and Console."));
            }

            return false;
        }

        public static bool QueuedStepPlatforms(QueuedBuildState s)
        {
            if (Cancelled(s, 0.35f, "Walk platforms (route only)…"))
                return true;

            EnsureQueuedMaterials(s);
            s.PlatformCount = CaveAdventureBlockBuilder.BuildWalkPlatforms(s.Geometry, s.MazeLayout, s.FloorMat);
            return false;
        }

        public static bool QueuedStepShell(QueuedBuildState s)
        {
            if (Cancelled(s, 0.44f, "Route floor + single ceiling mesh…"))
                return true;

            CaveTerrainRouteAlignment.IntegrateSceneTerrain(s.CavesRoot, s.MazeLayout, s.Spline, s.Request);

            s.TerrainPieces = CaveEnclosureShellBuilder.Build(
                s.Geometry, s.MazeLayout, s.FloorMat, s.RockMat, s.Request.Seed);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(s.CavesRoot);
            return false;
        }

        public static bool QueuedStepBlocksPrepare(QueuedBuildState s)
        {
            if (Cancelled(s, 0.46f, "Tunnel walls — prepare…"))
                return true;

            s.BlockSettings = CaveAdventureBlockBuilder.CompactRouteSettings(s.MazeLayout);

            s.BlockSection = CaveAdventureBlockBuilder.PrepareBlockSection(s.Geometry);
            s.BlockRingIndex = 0;
            s.BlockRingCount = s.MazeLayout?.SolutionPath?.Count ?? 0;
            s.BlockCount = 0;
            return false;
        }

        /// <summary>Returns true only when the user cancelled the progress bar.</summary>
        public static bool QueuedStepBlocksBatch(QueuedBuildState s)
        {
            if (Cancelled(s, 0.48f, "Tunnel walls around route…"))
                return true;

            if (s.BlockSection == null || s.BlockRingCount <= 0)
                return false;

            var batch = Mathf.Min(BlockRingBatchSize, s.BlockRingCount - s.BlockRingIndex);
            s.BlockCount += CaveAdventureBlockBuilder.BuildRingCells(
                s.BlockSection,
                s.MazeLayout,
                s.BlockRingIndex,
                batch,
                s.RockMat,
                s.Request.Seed,
                s.BlockSettings);
            s.BlockRingIndex += batch;
            return false;
        }

        public static bool QueuedStepBlocksFinish(QueuedBuildState s)
        {
            ScatterHybridWallDetails(s.Geometry, s.MazeLayout, s.Catalog, s.Rng);
            var stripped = CaveInvisibleColliderUtility.StripForAdventure(s.CavesRoot);
            if (stripped > 0)
                Debug.Log($"[CaveBuild] Stripped {stripped} invisible/shell collider(s) after block walls.");
            return false;
        }

        public static bool QueuedStepFeatures(QueuedBuildState s)
        {
            if (Cancelled(s, 0.6f, "Jump crevices + finish goal…"))
                return true;

            PlaceTorchesOnGeometry(s.Geometry, s.MazeLayout, s.Rng);
            s.FeatureCount = CaveAdventureFeaturesBuilder.Build(
                s.CavesRoot, s.MazeLayout, s.RockMat, s.FloorMat, s.Request.Seed);
            PlaceFinishGoal(s.CavesRoot, s.MazeLayout);
            return false;
        }

        public static bool QueuedStepSurfaceWalkIn(QueuedBuildState s)
        {
            if (Cancelled(s, 0.68f, "Surface walk-in entrance (ground mouth + descent)…"))
                return true;

            var placed = CaveSurfaceEntranceBuilder.Build(
                s.CavesRoot,
                s.Geometry,
                s.MazeLayout,
                s.FloorMat,
                s.RockMat,
                s.Ground,
                s.Catalog,
                s.Request?.Seed ?? 0);
            Debug.Log($"[CaveBuild] Surface walk-in entrance phase: {placed} piece(s).");
            return false;
        }

        public static bool QueuedStepSpawn(QueuedBuildState s)
        {
            if (Cancelled(s, 0.75f, "Spawn alignment…"))
                return true;

            var authoring = s.CavesRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null)
                authoring = s.CavesRoot.gameObject.AddComponent<CaveSplinePathAuthoring>();
            authoring.SetPath(s.MazeLayout.PathKnots, s.Spline.TotalLength);

            var meta = s.CavesRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                meta = s.CavesRoot.gameObject.AddComponent<CaveBuildMetadata>();
            meta.Set(
                s.Request.Seed,
                s.Request.CaveTunnelSegments,
                s.Request.CaveChamberCount,
                hybrid: true,
                cellSize: s.MazeLayout != null ? s.MazeLayout.CellSize : 3f);

            SplineCaveSpawnAligner.AlignEntranceSpawn(
                s.CavesRoot,
                s.CavesRoot.Find("Entrance"),
                s.Spline,
                keepAtSurfaceMouth: true,
                mazeLayout: null);
            CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(s.CavesRoot);
            LavaTubeCaveBuildPipeline.EnsureGameplaySpawns(s.CavesRoot, s.Ground);
            EnsureSpawnGroundPad(s.CavesRoot, s.MazeLayout);
            EnsureMovementGuard(s.CavesRoot);
            CavePlayabilityFix.EnsureSingleUndergroundAtmosphere(s.CavesRoot);
            CaveCompactLayerPurge.Purge(s.CavesRoot);
            CaveAdventureVisualPass.Apply(s.CavesRoot);
            EnsureHybridRuntime(s.CavesRoot);
            return false;
        }

        public static bool QueuedStepPropsAndWater(QueuedBuildState s)
        {
            if (Cancelled(s, 0.85f, "Props + mobs…"))
                return true;

            var detailRoot = s.CavesRoot.Find("Details");
            var propsRoot = detailRoot != null
                ? EnvironmentSceneUtility.GetOrCreateChild(detailRoot, "Props")
                : null;
            var propCount = Mathf.Clamp(s.MazeLayout.SolutionPath.Count / 3, 4, 12);
            if (propsRoot != null)
            {
                SplineLavaTubeCaveGenerator.ScatterPropsAlongSpline(
                    propsRoot, s.Catalog, s.Rng, s.Spline, propCount);
            }

            CaveMobSpawnerPlacement.PlaceAlongRoute(s.CavesRoot, s.MazeLayout);
            if (detailRoot != null)
            {
                SplineLavaTubeCaveGenerator.PlaceMinablesNearSpline(
                    detailRoot, s.Catalog, s.Rng, s.Spline, 10);
            }

            LavaTubeCaveEnclosureBuilder.EnsureAtmosphereZone(
                s.CavesRoot, SplineLavaTubeCaveGenerator.SamplePathNodes(s.Spline, 24));
            CaveAdventureCaveLighting.Apply(s.CavesRoot, s.MazeLayout);

            if (s.Request.UseTerrainCarve)
            {
                CaveTerrainCarveUtility.CarveForCaveSystem(s.CavesRoot, s.Spline, null);
                CaveTerrainUtility.ApplyCaveEntranceMouth(
                    Object.FindAnyObjectByType<Terrain>(),
                    s.Request.Seed,
                    s.CavesRoot);
            }

            if (s.Request.IncludeCaveWater)
                BuildHybridWater(s.CavesRoot, s.MazeLayout);
            else
                CaveWaterUtility.ClearAllWater(s.CavesRoot);

            EnvironmentSceneUtility.MarkSceneDirty();
            return false;
        }

        public static LavaTubeCaveBuildReport FinishQueuedReport(QueuedBuildState s) =>
            new LavaTubeCaveBuildReport
            {
                PieceCount = s.PlatformCount + s.BlockCount + s.FeatureCount,
                PathNodes = SplineLavaTubeCaveGenerator.SamplePathNodes(s.Spline, 24),
                Message =
                    $"Compact cave route: {s.TerrainPieces} terrain surfaces, {s.PlatformCount} walk colliders, " +
                    $"{s.BlockCount} wall blocks, {s.MazeLayout.SolutionPath.Count} steps."
            };
    }
}
