using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.Editor.GaussianSplat;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public sealed class SurfaceWorldBuildReport
    {
        public bool Success;
        public string Message = string.Empty;
        public int TrailCount;
        public int WaterFeatureCount;
        public int CaveOpeningCount;
        public Transform SurfaceRoot;
    }

    /// <summary>
    /// Open-sky radial world from the tagged Ground anchor — does not modify UndergroundCaveSystem unless scope is FullWorld
    /// (cave rebuild is handled separately by <see cref="LavaTubeCaveBuilder"/>).
    /// </summary>
    public static class SurfaceWorldGenerator
    {
        public const int SurfacePhaseCount = 12;

        public static readonly string[] SurfacePhaseLabels =
        {
            "Setup root (additive if present)",
            "Ensure terrain extends from main land",
            "Florida LiDAR carve/rise sculpt + research brief",
            "Terrain dressing materials",
            "Terrain dressing + layout pre-plan (before grid terraform)",
            "Trails only",
            "Roads only",
            "Water only (non-walkable)",
            "Cave opening markers",
            "Mountain markers (non-walkable)",
            "Surface NavMesh bake",
            "Manifest export",
        };

        static void LogSurfacePhase(int index)
        {
            var label = $"Surface phase {index + 1}/{SurfacePhaseCount}: {SurfacePhaseLabels[index]}";
            CaveBuildPipelineLog.Info(label, "Surface");
            CaveBuildLiveSceneFeedback.NotifySurfacePhase(label);
        }

        sealed class BuildSession
        {
            public SurfaceWorldBuildReport Report = new();
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public bool ReplaceExistingSurface;
            public int UndoGroup;
            public Vector3 Center;
            public Vector3 PrimaryForward;
            public float Extent;
            public int Seed;
            public int TerrainPasses;
            public Terrain Terrain;
            public Transform RootTransform;
            public EnvironmentAuthoringKit.EnvironmentRoot EnvRoot;
            public float PreserveInner;
            public int ExtraTiles;
            public System.Action<SurfaceWorldBuildReport> OnComplete;
        }

        enum SurfaceFinishStep
        {
            NormalizePeak = 0,
            NeighborTiles,
            SurfaceOpenings,
            PrimaryMouthTerrain,
            SurfaceTrails,
            SurfaceRoads,
            SurfaceWater,
            SurfaceMountains,
            NavMesh,
            Finalize,
            Done,
        }

        /// <summary>Runs surface world build with terrain sculpt passes spread across editor queue steps (no freeze).</summary>
        public static void QueueBuild(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool replaceExistingSurface,
            System.Action<SurfaceWorldBuildReport> onComplete)
        {
            var session = new BuildSession
            {
                Ground = ground,
                Request = request,
                ReplaceExistingSurface = replaceExistingSurface,
                OnComplete = onComplete,
            };

            CaveBuildPlannerTerrainGuide.TryBindSession(request, ground);

            var fullWorldFlatGrid = session.Request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                session.Request.UseOuterRingMountains &&
                SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(session.Request, fullWorld: true);

            if (!TryBeginSurfaceBuild(session, stopAfterTerrainBind: fullWorldFlatGrid))
            {
                CaveBuildSurfaceCompletionGate.MarkSurfacePipelineFailed(session.Request);
                onComplete?.Invoke(session.Report);
                return;
            }

            if (fullWorldFlatGrid)
            {
                SurfaceTerrainTileExpansion.QueueFullWorldLayAllGroundTilesOnly(
                    session.Terrain,
                    session.Ground,
                    session.Request,
                    (count, tileMsg) =>
                    {
                        if (!string.IsNullOrEmpty(tileMsg))
                            CaveBuildEditorLog.LogSurface(tileMsg, forceUnityConsole: true);
                        ContinueFullWorldSurfaceAfterGroundLaid(session);
                    });
                return;
            }

            var demCenter = session.Request.SurfaceScope == SurfaceBuildScope.FullWorld
                ? SurfaceTerrainPlayRegion.TerrainTileCenter(session.Terrain)
                : session.Center;
            var demExtent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                session.Terrain,
                demCenter,
                session.Extent);

            if (CaveBuildPlannerTerrainGuide.UsesPlannerTerrainGenerator(session.Request))
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Planner terrain — LiDAR carve/rise sculpt (no heightmap stamp)…",
                    forceUnityConsole: true);
                SurfaceTerrainSculptPromptBridge.ExportBeforeSculpt(
                    session.Request,
                    session.Ground,
                    session.Seed,
                    session.Center,
                    session.Extent);
                LogSurfacePhase(2);
                CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                    "Planner LiDAR sculpt + brief height targets…");
                SurfaceWorldLayoutPrePlanner.QueueWritePlan(
                    session.Ground,
                    session.Request,
                    session.Center,
                    session.Extent,
                    () =>
                        CaveBuildPlannerTerrainGuide.QueueMainTerrainPipeline(
                            session.Terrain,
                            session.Ground,
                            session.Request,
                            demCenter,
                            demExtent,
                            session.TerrainPasses,
                            () =>
                            {
                                EditorUtility.ClearProgressBar();
                                var next = session.Request.SurfaceScope == SurfaceBuildScope.FullWorld
                                    ? SurfaceFinishStep.NeighborTiles
                                    : SurfaceFinishStep.NormalizePeak;
                                QueueFinishSurfaceBuild(session, next);
                            }));
                return;
            }

            CaveBuildEditorLog.LogSurface(
                "[Surface] Florida LiDAR — research-informed carve/rise sculpt (character-scale relief)…",
                forceUnityConsole: true);

            SurfaceTerrainSculptPromptBridge.ExportBeforeSculpt(
                session.Request,
                session.Ground,
                session.Seed,
                session.Center,
                session.Extent);

            LogSurfacePhase(2);
            CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                "Florida LiDAR guide — sculpting playable terrain…");

            SurfaceWorldLayoutPrePlanner.QueueWritePlan(
                session.Ground,
                session.Request,
                session.Center,
                session.Extent,
                () =>
                    SurfaceDemGeoreferenceAuthor.QueueApplyGeoreferencedStamp(
                        session.Terrain,
                        demCenter,
                        demExtent,
                        session.Seed,
                        demMsg =>
                {
                    if (!string.IsNullOrEmpty(demMsg))
                        CaveBuildEditorLog.LogSurface("[Surface] " + demMsg, forceUnityConsole: true);

                    var demStamped =
                        SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild;

                    LogSurfacePhase(4);
                    if (demStamped)
                    {
                        var sculptCenter = session.Request.SurfaceScope == SurfaceBuildScope.FullWorld
                            ? demCenter
                            : session.Center;
                        var sculptExtent = session.Request.SurfaceScope == SurfaceBuildScope.FullWorld
                            ? demExtent
                            : session.Extent;
                        var creativePasses = SurfaceTerrainCenteredAuthor.ResolvePassCountAfterFloridaDem(
                            session.TerrainPasses,
                            demStamped: true);

                        CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                            $"Carve/rise sculpt after LiDAR ({creativePasses} passes)…");
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] LiDAR macro carved — queueing {creativePasses} research-informed sculpt pass(es) " +
                            "(character-scale hills; not flat stamp + smooth).",
                            forceUnityConsole: true);

                        SurfaceTerrainCenteredAuthor.QueueCenteredPasses(
                            session.Terrain,
                            sculptCenter,
                            sculptExtent,
                            session.Seed,
                            session.Request.SurfaceIncludeMountains,
                            session.Request.SurfaceIncludeWater,
                            session.Request.SurfaceIncludeRoads,
                            session.PreserveInner,
                            creativePasses,
                            refinementAfterAuthoritativeDem: true,
                            onComplete: () =>
                            {
                                EditorUtility.ClearProgressBar();
                                var next = session.Request.SurfaceScope == SurfaceBuildScope.FullWorld
                                    ? SurfaceFinishStep.NeighborTiles
                                    : SurfaceFinishStep.NormalizePeak;
                                QueueFinishSurfaceBuild(session, next);
                            });
                        return;
                    }

                    var sculptPasses = SurfaceTerrainCenteredAuthor.ResolvePassCount(session.TerrainPasses);
                    CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                        $"Sculpt {sculptPasses} passes (no Florida DEM)…");
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Queueing {sculptPasses} centered sculpt passes (no authoritative DEM)…",
                        forceUnityConsole: true);

                    SurfaceTerrainCenteredAuthor.QueueCenteredPasses(
                        session.Terrain,
                        session.Center,
                        session.Extent,
                        session.Seed,
                        session.Request.SurfaceIncludeMountains,
                        session.Request.SurfaceIncludeWater,
                        session.Request.SurfaceIncludeRoads,
                        session.PreserveInner,
                        sculptPasses,
                        refinementAfterAuthoritativeDem: false,
                        onComplete: () =>
                        {
                            EditorUtility.ClearProgressBar();
                            CaveBuildEditorLog.LogSurface(
                                "[Surface] Florida DEM + sculpt refinement done — peak normalize next…",
                                forceUnityConsole: true);
                            QueueFinishSurfaceBuild(session, SurfaceFinishStep.NormalizePeak);
                        });
                }));
        }

        /// <summary>Synchronous build — prefer <see cref="QueueBuild"/> from the cave pipeline.</summary>
        public static SurfaceWorldBuildReport Build(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool replaceExistingSurface = true)
        {
            SurfaceWorldBuildReport result = null;
            QueueBuild(ground, request, replaceExistingSurface, r => result = r);
            return result ?? new SurfaceWorldBuildReport
            {
                Message =
                    "Surface build is running on the editor queue — wait for terrain passes to finish, then retry if this returned too early.",
            };
        }

        static void ContinueFullWorldSurfaceAfterGroundLaid(BuildSession session)
        {
            ApplyPostTerrainSurfaceSetup(session);
            SurfaceWorldLayoutPrePlanner.QueueWritePlan(
                session.Ground,
                session.Request,
                session.Center,
                session.Extent,
                () =>
                {
                    EditorUtility.ClearProgressBar();
                    var tileCount = SurfaceOpenWorldGridExpansion.BuildAaaExtendedPlaceOrder().Length;
                    if (session.Request.SurfaceScope == SurfaceBuildScope.FullWorld)
                    {
                        CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                            $"FullWorld grid terraform next (~{tileCount} tiles — banner updates per tile; this phase is slow on 16GB Macs)");
                        CaveBuildRunStatusPublisher.SetSubOperation(
                            "FullWorld grid",
                            $"handoff — weld + terraform (~{tileCount} tiles)");
                    }

                    QueueFinishSurfaceBuild(session, SurfaceFinishStep.NeighborTiles);
                });
        }

        static bool TryBeginSurfaceBuild(BuildSession session, bool stopAfterTerrainBind = false)
        {
            var report = session.Report;
            var ground = session.Ground;
            if (ground == null || !ground.HasAnchor)
            {
                report.Message = "No ground anchor — tag walkable floor as Ground or assign in Environment Kit.";
                return false;
            }

            var request = session.Request ??= new WorldGenerationRequest();
            FullWorldConceptLayoutCatalog.EnsureConceptOnRequest(request);
            SurfaceTerrainGridRegistry.ResetSession();
            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                SurfaceTerrainBlockoutLayoutAuthor.DemoteModularGridGroundTag();
                SurfaceTerrainBlockoutLayoutAuthor.BindMainTerrainToGroundInfo(ground);
                if (!request.PreserveBlockoutGridLayout)
                {
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] FullWorld terrain-first — 9 play + 16 foothill + 24 peak + mountain labyrinth " +
                        "(modular Grid blockout not authoritative).",
                        forceUnityConsole: true);
                }
            }
            session.Seed = request.Seed;
            session.Extent = EnvironmentKitHardwareBudget.ClampSurfaceExtent(
                Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f));
            session.Center = ground.Anchor.position;
            session.PrimaryForward = ResolvePrimaryForward(ground, request);
            session.TerrainPasses = SurfaceTerrainCenteredAuthor.ResolvePassCount(
                request.SurfaceTerrainBuildPasses);

            Undo.IncrementCurrentGroup();
            session.UndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Surface World");

            LogSurfacePhase(0);
            ApplyAtmosphereFromRequest(request);

            session.EnvRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            var surfaceRoot = session.EnvRoot.transform.Find(SurfaceWorldPaths.RootName);
            if (session.ReplaceExistingSurface &&
                surfaceRoot != null &&
                PipelineContentPreservePolicy.TryPreserveTransform(surfaceRoot, SurfaceWorldPaths.RootName))
            {
                session.ReplaceExistingSurface = false;
            }

            if (session.ReplaceExistingSurface && surfaceRoot != null)
                CaveEditorUndo.DestroyImmediate(surfaceRoot.gameObject);

            if (surfaceRoot != null && !session.ReplaceExistingSurface)
            {
                session.RootTransform = surfaceRoot;
                Debug.Log("[CaveBuild] Surface phase 1: reusing GeneratedSurfaceWorld (additive).");
            }
            else
            {
                var rootGo = new GameObject(SurfaceWorldPaths.RootName);
                CaveEditorUndo.RegisterCreated(rootGo, "Surface world root");
                rootGo.transform.SetParent(session.EnvRoot.transform, false);
                rootGo.transform.position = session.Center;
                session.RootTransform = rootGo.transform;
            }

            report.SurfaceRoot = session.RootTransform;

            EnvironmentSceneUtility.RepairKitRootTerrainState();
            if (ground != null && !EnvironmentSceneUtility.IsLiveTerrain(ground.Terrain))
                ground.Terrain = null;

            LogSurfacePhase(1);
            var allowTerrain = request.AllowCreateTerrain || request.SurfaceScope != SurfaceBuildScope.CaveOnly;
            var budget = EnvironmentKitHardwareBudget.Active;
            var terrain = EnvironmentSceneUtility.FindTerrainInActiveScene(
                session.EnvRoot.transform,
                ground,
                allowTerrain,
                budget.TerrainMaxSizeMeters,
                budget.TerrainMaxHeightMeters);
            if (terrain == null && allowTerrain)
            {
                terrain = CaveBuildTerrainEnsure.TryEnsure(ground, null, session.Seed, out var ensureMsg);
                if (!string.IsNullOrEmpty(ensureMsg))
                    Debug.Log("[CaveBuild] Surface terrain: " + ensureMsg);
            }

            if (terrain == null)
            {
                report.Message =
                    "No Terrain in scene. The build tried to create integration terrain at your Ground anchor but could not.\n\n" +
                    "• Tag walkable floor as Ground\n" +
                    "• Or add a Unity Terrain to the scene\n" +
                    "• Or turn off Environment Kit → \"Never create new terrain\" if you rely on auto-create";
                return false;
            }

            ground.Terrain = terrain;
            session.Terrain = terrain;

            if (request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                !request.PreserveBlockoutGridLayout)
            {
                SurfaceTerrainBlockoutLayoutAuthor.RebindFullWorldGroundToMainTerrain(ground, terrain);
                SurfaceTerrainTileExpansion.EnforcePlayDiskGridLayout(terrain, ground);
            }

            if (request.PreserveBlockoutGridLayout &&
                request.SurfaceScope == SurfaceBuildScope.FullWorld)
                SurfaceTerrainBlockoutLayoutAuthor.AlignPlayDiskToBlockoutGrid(terrain, ground);
            else if (terrain != null)
                session.Center = SurfaceTerrainTileExpansion.TileCenterWorld(terrain);

            if (stopAfterTerrainBind)
                return true;

            ApplyPostTerrainSurfaceSetup(session);
            return true;
        }

        static void ApplyPostTerrainSurfaceSetup(BuildSession session)
        {
            var request = session.Request;
            var ground = session.Ground;
            var terrain = session.Terrain;

            LogSurfacePhase(3);
            var floridaDemReady = SurfaceDemGeoreferenceAuthor.TryLoadGeorefForSeed(
                session.Seed, out _, out _);
            var heightStyle = request.SurfaceIncludeMountains
                ? TerrainHeightStyle.Mountains
                : TerrainHeightStyle.Hilly;
            if (session.ReplaceExistingSurface && !floridaDemReady)
            {
                TerrainDressingApplier.ApplyHeightStyle(terrain, heightStyle, null, session.Seed);
            }
            else if (session.ReplaceExistingSurface)
            {
                CaveBuildEditorLog.LogSurface(
                    "Phase 3: skipping procedural grid height — Florida LiDAR DEM is authoritative (phase 3).",
                    forceUnityConsole: true);
            }
            else
            {
                CaveBuildEditorLog.LogSurface(
                    "Phase 3: skipping grid height dressing — additive build keeps Ground disk; Florida DEM stamps outside.",
                    forceUnityConsole: true);
            }

            TerrainDressingApplier.Apply(terrain, CreateFallbackDressing(request));

            LogSurfacePhase(4);
            SurfaceTerrainLidarPromptBridge.ExportBeforeTerrainWork(request, ground, session.Seed);
            if (!floridaDemReady)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    "[Surface] No Florida hillshade/georef for this seed — terrain will look flat/repetitive until: " +
                    "cd Tools/cave-grader && npm run sync-florida-hillshades -- --elev-grid=128");
            }
            session.PreserveInner = !session.ReplaceExistingSurface
                ? Mathf.Max(
                    session.Extent * SurfaceLidarTerrainAuthor.MainLandPreserveRadiusFraction,
                    session.Extent * 0.12f)
                : Mathf.Max(
                    session.Extent * SurfaceLidarTerrainAuthor.MainLandPreserveRadiusFraction,
                    session.Extent * 0.30f);
            if (!session.ReplaceExistingSurface)
            {
                CaveBuildEditorLog.LogSurface(
                    $"Phase 4: additive sculpt outside {session.PreserveInner:F0}m (Ground center disk preserved).",
                    forceUnityConsole: true);
            }

            CaveBuildEditorLog.LogSurface(
                "Phase 5: Florida LiDAR macro + paced carve/rise sculpt (research brief) before trails/NavMesh.",
                forceUnityConsole: true);
        }

        static void ContinueAfterPlayDiskPolish(BuildSession session)
        {
            var request = session.Request;
            var fullWorldFlatGrid = request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                request.UseOuterRingMountains &&
                SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(request, fullWorld: true);

            if (fullWorldFlatGrid &&
                SurfaceFloridaDemBuildState.NineTilePlayDiskPolishCompletedThisBuild)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] FullWorld play disk — hometown stays flat (no carve/rise sculpt on 9-tile center).",
                    forceUnityConsole: true);
            }

            CaveBuildActionPacing.ScheduleLight(
                () => QueueFinishSurfaceBuild(session, SurfaceFinishStep.SurfaceOpenings));
        }

        static void QueueMountainPipelineAfterMouth(BuildSession session)
        {
            var request = session.Request;
            SurfaceMountainResearchPipeline.QueueAfterNineTilePolish(
                session.Terrain,
                session.Ground,
                request,
                session.RootTransform,
                session.Center,
                session.Extent,
                session.PrimaryForward,
                session.Seed,
                () => CaveBuildActionPacing.ScheduleLight(
                    () => QueueFinishSurfaceBuild(session, SurfaceFinishStep.SurfaceTrails)));
        }

        static void QueueFinishSurfaceBuild(BuildSession session, SurfaceFinishStep step)
        {
            if (step == SurfaceFinishStep.Done)
            {
                EditorUtility.ClearProgressBar();
                SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();
                CaveBuildSurfaceCompletionGate.MarkSurfaceWorldGeneratorFinished(
                    session.Request,
                    session.Report is { Success: true });
                session.OnComplete?.Invoke(session.Report);
                return;
            }

            var label = step switch
            {
                SurfaceFinishStep.NormalizePeak => "surface peak normalize",
                SurfaceFinishStep.NeighborTiles => "FullWorld weld + terraform (seams after sculpt)",
                SurfaceFinishStep.SurfaceTrails => "surface trails",
                SurfaceFinishStep.SurfaceRoads => "surface roads",
                SurfaceFinishStep.SurfaceWater => "surface water",
                SurfaceFinishStep.SurfaceOpenings => "surface cave openings",
                SurfaceFinishStep.SurfaceMountains => "surface mountain markers",
                SurfaceFinishStep.NavMesh => "surface NavMesh bake",
                SurfaceFinishStep.Finalize => "surface manifest + finish",
                _ => "surface finish",
            };

            CaveBuildActionPacing.ScheduleLight(() =>
            {
                try
                {
                    if (CaveBuildProgressBars.AllowModalProgressBar)
                        EditorUtility.DisplayProgressBar("Environment Kit", $"[Surface] {label}…", 0.88f);
                    var (next, chain) = RunFinishSurfaceStep(session, step);
                    if (chain)
                        QueueFinishSurfaceBuild(session, next);
                }
                catch (System.Exception ex)
                {
                    EditorUtility.ClearProgressBar();
                    session.Report.Message = "Surface build failed: " + ex.Message;
                    session.Report.Success = false;
                    Debug.LogException(ex);
                    CaveBuildSurfaceCompletionGate.MarkSurfacePipelineFailed(session.Request);
                    session.OnComplete?.Invoke(session.Report);
                }
            });
        }

        static (SurfaceFinishStep next, bool chain) RunFinishSurfaceStep(BuildSession session, SurfaceFinishStep step)
        {
            EnsureSessionSurfaceRoot(session);

            var report = session.Report;
            var ground = session.Ground;
            var request = session.Request;
            var terrain = session.Terrain;
            var center = session.Center;
            var extent = session.Extent;
            var seed = session.Seed;
            var replaceExistingSurface = session.ReplaceExistingSurface;
            var rootTransform = session.RootTransform;
            var envRoot = session.EnvRoot;
            var terrainPasses = session.TerrainPasses;
            var primaryForward = session.PrimaryForward;

            if (!rootTransform && step is not SurfaceFinishStep.NormalizePeak and not SurfaceFinishStep.NeighborTiles
                and not SurfaceFinishStep.Done)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[Surface] Skipping finish step {step} — could not bind or recreate GeneratedSurfaceWorld.",
                    forceUnityConsole: true);
                return (step + 1, true);
            }

            switch (step)
            {
                case SurfaceFinishStep.NormalizePeak:
                    if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
                    {
                        CaveBuildEditorLog.LogSurface(
                            "[Surface] FullWorld — peak normalize skipped (height already anchored at Ground).",
                            forceUnityConsole: true);
                        return (SurfaceFinishStep.NeighborTiles, true);
                    }

                    if (SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild)
                    {
                        CaveBuildEditorLog.LogSurface(
                            "[Surface] LiDAR authoritative — skipping peak normalize (no height shift before 9-tile).",
                            forceUnityConsole: true);
                        return (SurfaceFinishStep.NeighborTiles, true);
                    }

                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Peak evaluation first — height shift commits once after scan…",
                        forceUnityConsole: true);
                    SurfaceTerrainGroundLevelNormalizer.QueueNormalizePeakToGroundLevel(
                        terrain,
                        center,
                        extent,
                        ground,
                        groundNormMsg =>
                        {
                            if (!string.IsNullOrEmpty(groundNormMsg))
                                Debug.Log("[CaveBuild] " + groundNormMsg);
                            CaveBuildActionPacing.ScheduleLight(
                                () => QueueFinishSurfaceBuild(session, SurfaceFinishStep.NeighborTiles));
                        });
                    return (SurfaceFinishStep.Done, false);

                case SurfaceFinishStep.NeighborTiles:
                {
                    var fullWorldFlatGrid = request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                        request.UseOuterRingMountains &&
                        SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(request, fullWorld: true);
                    var minimalGridDemo = CaveBuildSessionConfig.IsFloatingIslandsDemo(request) ||
                                          CaveBuildSessionConfig.IsPlayDiskOnlyDemo(request);

                    if (fullWorldFlatGrid || minimalGridDemo)
                    {
                        SurfaceTerrainTileExpansion.QueueFullWorldDirectionalPipeline(
                            terrain,
                            ground,
                            request,
                            (count, tileMsg) =>
                            {
                                session.ExtraTiles = count;
                                if (!string.IsNullOrEmpty(tileMsg))
                                    CaveBuildEditorLog.LogSurface(tileMsg, forceUnityConsole: true);
                                if (request.PreserveBlockoutGridLayout && request.CarveTerrainToBlockoutFootprints)
                                {
                                    SurfaceTerrainBlockoutLayoutAuthor.QueueGeoCarvePlayDisk(
                                        terrain,
                                        request,
                                        () => ContinueAfterPlayDiskPolish(session));
                                }
                                else
                                    ContinueAfterPlayDiskPolish(session);
                            });
                    }
                    else
                    {
                        SurfaceTerrainTileExpansion.QueueAttachGameplayTiles(
                            terrain,
                            ground,
                            request,
                            (count, tileMsg) =>
                            {
                                session.ExtraTiles = count;
                                if (!string.IsNullOrEmpty(tileMsg))
                                    CaveBuildEditorLog.LogSurface(tileMsg, forceUnityConsole: true);
                                SurfaceTerrainTileExpansion.QueueUnifiedSurfaceWorldPolish(
                                    terrain,
                                    ground,
                                    request,
                                    () =>
                                    {
                                        if (request.PreserveBlockoutGridLayout && request.CarveTerrainToBlockoutFootprints)
                                        {
                                            SurfaceTerrainBlockoutLayoutAuthor.QueueGeoCarvePlayDisk(
                                                terrain,
                                                request,
                                                () => ContinueAfterPlayDiskPolish(session));
                                        }
                                        else
                                            ContinueAfterPlayDiskPolish(session);
                                    });
                            });
                    }

                    return (SurfaceFinishStep.Done, false);
                }

                case SurfaceFinishStep.SurfaceOpenings:
                    RunSurfaceOpeningsStep(session);
                    return (SurfaceFinishStep.PrimaryMouthTerrain, true);

                case SurfaceFinishStep.PrimaryMouthTerrain:
                    SurfacePrimaryMouthTerrainAuthor.TryApply(session.Ground, session.Request, out var mouthMsg);
                    if (!string.IsNullOrEmpty(mouthMsg))
                        CaveBuildEditorLog.LogSurface(mouthMsg, forceUnityConsole: true);
                    if (request.UseOuterRingMountains &&
                        SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(
                            request,
                            request.SurfaceScope == SurfaceBuildScope.FullWorld))
                    {
                        QueueMountainPipelineAfterMouth(session);
                        return (SurfaceFinishStep.Done, false);
                    }

                    return (SurfaceFinishStep.SurfaceTrails, true);

                case SurfaceFinishStep.SurfaceTrails:
                    RunSurfaceTrailsStep(session);
                    return (SurfaceFinishStep.SurfaceRoads, true);

                case SurfaceFinishStep.SurfaceRoads:
                    RunSurfaceRoadsStep(session);
                    return (SurfaceFinishStep.SurfaceWater, true);

                case SurfaceFinishStep.SurfaceWater:
                    RunSurfaceWaterStep(session);
                    return (SurfaceFinishStep.SurfaceMountains, true);

                case SurfaceFinishStep.SurfaceMountains:
                    RunSurfaceMountainsStep(session);
                    return (SurfaceFinishStep.NavMesh, true);

                case SurfaceFinishStep.NavMesh:
                    LogSurfacePhase(10);
                    SurfaceNavMeshBaker.BakePhase(envRoot.transform, terrain, rootTransform, out var navMsg);
                    if (!string.IsNullOrEmpty(navMsg))
                        Debug.Log("[CaveBuild] " + navMsg);
                    return (SurfaceFinishStep.Finalize, true);

                case SurfaceFinishStep.Finalize:
                {
                    WorldRuntimeResourcesAuthor.EnsureAllEnemyPrefabsInResources();
                    var plannedEnemies = SurfaceTerrainEnemySpawnerPlacement.EnsureOnSurface(
                        rootTransform, request, null);
                    if (plannedEnemies > 0)
                        Debug.Log(
                            $"[CaveBuild] Surface terrain enemies: {plannedEnemies} planned at play (NavMesh scatter).");

                    LogSurfacePhase(11);
                    WriteManifest(report, ground, request, center, 1, extent, terrainPasses, session.ExtraTiles);
                    EnvironmentKitGaussianSplatIntegration.ScheduleAfterSurfaceFinish();
                    EnvironmentSceneUtility.MarkSceneDirty();
                    Undo.CollapseUndoOperations(session.UndoGroup);

                    report.Success = true;
                    report.Message =
                        $"Surface world ({SurfacePhaseCount} phases) at Ground center — {report.TrailCount} trail axis, " +
                        $"{report.WaterFeatureCount} water, {report.CaveOpeningCount} cave mouth, " +
                        $"{terrainPasses} terrain passes, {session.ExtraTiles} extra tile(s), {extent:F0}m.";
                    CaveBuildEditorLog.LogSurface(report.Message, forceUnityConsole: true);
                    return (SurfaceFinishStep.Done, true);
                }

                default:
                    return (SurfaceFinishStep.Done, true);
            }
        }

        /// <summary>Re-bind when Plan v4 or another pass recreates Environment root while finish steps are still queued.</summary>
        static bool TryRefreshSessionSurfaceRoot(BuildSession session)
        {
            if (session == null)
                return false;

            if (session.RootTransform)
                return true;

            session.RootTransform = null;

            if (session.Ground.HasAnchor && !session.EnvRoot)
                session.EnvRoot = EnvironmentSceneUtility.GetOrCreateRoot(session.Ground);

            if (!session.EnvRoot)
                return false;

            var surfaceRoot = session.EnvRoot.transform.Find(SurfaceWorldPaths.RootName);
            if (surfaceRoot == null)
                return false;

            session.RootTransform = surfaceRoot;
            return true;
        }

        /// <summary>Guarantees a live GeneratedSurfaceWorld for deferred finish steps after Plan v4 or rebuild.</summary>
        static Transform EnsureSessionSurfaceRoot(BuildSession session)
        {
            if (TryRefreshSessionSurfaceRoot(session))
                return session.RootTransform;

            if (session == null)
                return null;

            if (session.Ground.HasAnchor && !session.EnvRoot)
                session.EnvRoot = EnvironmentSceneUtility.GetOrCreateRoot(session.Ground);

            if (!session.EnvRoot)
                return null;

            var rootGo = new GameObject(SurfaceWorldPaths.RootName);
            CaveEditorUndo.RegisterCreated(rootGo, "Recreate GeneratedSurfaceWorld");
            rootGo.transform.SetParent(session.EnvRoot.transform, false);
            rootGo.transform.position = session.Center;
            session.RootTransform = rootGo.transform;
            session.Report.SurfaceRoot = session.RootTransform;
            CaveBuildEditorLog.LogSurfaceWarning(
                "[Surface] Recreated GeneratedSurfaceWorld — prior root was destroyed; finish steps continue.",
                forceUnityConsole: true);
            return session.RootTransform;
        }

        static void RunSurfaceTrailsStep(BuildSession session)
        {
            var report = session.Report;
            var request = session.Request;
            var terrain = session.Terrain;
            var center = session.Center;
            var extent = session.Extent;
            var seed = session.Seed;
            var replaceExistingSurface = session.ReplaceExistingSurface;
            var rootTransform = EnsureSessionSurfaceRoot(session);
            var primaryForward = session.PrimaryForward;
            var rng = new System.Random(seed);

            if (!rootTransform)
                return;

            var trailsRoot = EnvironmentSceneUtility.GetOrCreateChild(rootTransform, SurfaceWorldPaths.TrailsName);
            if (trailsRoot == null)
                return;
            LogSurfacePhase(5);
            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] FullWorld — skipping play-disk trails (foothill/peak/labyrinth pipeline owns mountain edges).",
                    forceUnityConsole: false);
                report.TrailCount = trailsRoot.childCount;
                return;
            }

            if (replaceExistingSurface && trailsRoot.childCount > 0)
                Debug.Log($"[CaveBuild] Surface phase 5: keeping {trailsRoot.childCount} existing trail(s) (additive).");
            else if (!PipelineContentPreservePolicy.PreferMergeChildren(trailsRoot, "surface trails"))
                ClearChildren(trailsRoot);
            report.TrailCount = trailsRoot.childCount;
            if (request.SurfaceIncludeTrails && (!replaceExistingSurface || report.TrailCount == 0))
            {
                var trail = BuildTrail(trailsRoot, terrain, center, primaryForward, extent, seed, rng, mountain: true);
                if (trail != null)
                {
                    report.TrailCount++;
                    SurfaceTerrainRadialAuthor.FlattenTrailBench(terrain, trail, 2.5f);
                }
            }
        }

        static void RunSurfaceRoadsStep(BuildSession session)
        {
            var request = session.Request;
            var terrain = session.Terrain;
            var center = session.Center;
            var extent = session.Extent;
            var seed = session.Seed;
            var replaceExistingSurface = session.ReplaceExistingSurface;
            var rootTransform = session.RootTransform;
            var primaryForward = session.PrimaryForward;

            var roadsRoot = EnvironmentSceneUtility.GetOrCreateChild(rootTransform, SurfaceWorldPaths.RoadsName);
            LogSurfacePhase(6);
            if (replaceExistingSurface && roadsRoot.childCount > 0)
                Debug.Log($"[CaveBuild] Surface phase 6: keeping {roadsRoot.childCount} existing road(s) (additive).");
            else if (!PipelineContentPreservePolicy.PreferMergeChildren(roadsRoot, "surface roads"))
                ClearChildren(roadsRoot);
            if (request.SurfaceIncludeRoads && (!replaceExistingSurface || roadsRoot.childCount == 0))
                BuildRoad(roadsRoot, terrain, center, primaryForward, extent * 0.7f, seed + 100);
        }

        static void RunSurfaceWaterStep(BuildSession session)
        {
            var report = session.Report;
            var request = session.Request;
            var terrain = session.Terrain;
            var center = session.Center;
            var extent = session.Extent;
            var seed = session.Seed;
            var replaceExistingSurface = session.ReplaceExistingSurface;
            var rootTransform = session.RootTransform;
            var primaryForward = session.PrimaryForward;

            var waterRoot = EnvironmentSceneUtility.GetOrCreateChild(rootTransform, SurfaceWorldPaths.WaterName);
            LogSurfacePhase(7);
            if (replaceExistingSurface && waterRoot.childCount > 0)
                Debug.Log($"[CaveBuild] Surface phase 7: keeping {waterRoot.childCount} existing water feature(s) (additive).");
            else if (!PipelineContentPreservePolicy.PreferMergeChildren(waterRoot, "surface water"))
                ClearChildren(waterRoot);
            report.WaterFeatureCount = waterRoot.childCount;
            if (request.SurfaceIncludeWater && (!replaceExistingSurface || report.WaterFeatureCount == 0))
            {
                var waterForward = Quaternion.Euler(0f, 55f, 0f) * primaryForward;
                if (BuildWaterFeature(waterRoot, session.Ground, center, waterForward, extent * 0.45f, seed + 200))
                    report.WaterFeatureCount++;
            }

            SnapWaterFeaturesToTerrain(waterRoot, session.Ground);
        }

        /// <summary>Align surface water props to walkable terrain height (matches SurfacePlaytestValidator).</summary>
        public static int SnapWaterFeaturesToTerrain(Transform waterRoot, SceneGroundInfo ground)
        {
            if (waterRoot == null || ground?.Terrain == null)
                return 0;

            var snapped = 0;
            foreach (Transform child in waterRoot)
            {
                if (child == null)
                    continue;

                var pos = child.position;
                var surfaceY = CaveGroundPlacementUtility.SampleHeightmapWorldY(ground, pos);
                if (float.IsNaN(surfaceY) || pos.y <= surfaceY + 1.5f)
                    continue;

                Undo.RecordObject(child, "Snap water to terrain");
                child.position = new Vector3(pos.x, surfaceY + 0.08f, pos.z);
                EditorUtility.SetDirty(child);
                snapped++;
            }

            return snapped;
        }

        static void RunSurfaceOpeningsStep(BuildSession session)
        {
            var report = session.Report;
            var terrain = session.Terrain;
            var center = session.Center;
            var extent = session.Extent;
            var seed = session.Seed;
            var rootTransform = session.RootTransform;
            var primaryForward = session.PrimaryForward;
            var request = session.Request;
            var rng = new System.Random(seed);

            var openingsRoot = EnvironmentSceneUtility.GetOrCreateChild(rootTransform, SurfaceWorldPaths.CaveOpeningsName);
            LogSurfacePhase(8);
            if (!PipelineContentPreservePolicy.PreferMergeChildren(openingsRoot, "cave openings"))
                ClearChildren(openingsRoot);
            report.CaveOpeningCount = openingsRoot.childCount;

            if (request != null &&
                SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(
                    request,
                    request.SurfaceScope == SurfaceBuildScope.FullWorld))
            {
                if (request.UseMountainWildernessCaveMouth)
                {
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Play-disk cave openings skipped — south peak wilderness mouths only (no radial star layout).",
                        forceUnityConsole: true);
                    return;
                }

                report.CaveOpeningCount = PlaceNineTileCaveOpenings(
                    openingsRoot,
                    terrain,
                    center,
                    primaryForward,
                    extent,
                    request,
                    rng);
                return;
            }

            var openingDist = extent * (0.58f + (float)rng.NextDouble() * 0.18f);
            if (PlaceCaveOpening(openingsRoot, terrain, center, primaryForward, openingDist, 0, isPrimary: true))
                report.CaveOpeningCount++;
        }

        static int PlaceNineTileCaveOpenings(
            Transform openingsRoot,
            Terrain mainTerrain,
            Vector3 center,
            Vector3 primaryForward,
            float extent,
            WorldGenerationRequest request,
            System.Random rng)
        {
            var count = 0;
            var primaryDist = extent * 0.42f;
            if (PlaceCaveOpening(
                    openingsRoot,
                    mainTerrain,
                    center,
                    primaryForward,
                    primaryDist,
                    0,
                    isPrimary: true,
                    tileOffset: Vector2Int.zero))
                count++;

            var satelliteSlots = PickSatelliteTileOffsets(request, primaryForward);
            var sector = 1;
            foreach (var off in satelliteSlots)
            {
                var tileCenter = TileCenterWorld(mainTerrain, off);
                var outward = tileCenter - center;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1f)
                    outward = primaryForward;
                outward.Normalize();

                var dist = Vector3.Distance(center, tileCenter) * 0.92f;
                if (PlaceCaveOpening(
                        openingsRoot,
                        mainTerrain,
                        center,
                        outward,
                        dist,
                        sector,
                        isPrimary: false,
                        tileOffset: off))
                    count++;
                sector++;
            }

            return count;
        }

        static Vector2Int[] PickSatelliteTileOffsets(WorldGenerationRequest request, Vector3 primaryForward)
        {
            var want = Mathf.Clamp(request.SatelliteCaveCount, 0, 7);
            if (want == 0)
                return Array.Empty<Vector2Int>();
            var skip = 0;
            var bestDot = float.MaxValue;
            for (var i = 0; i < SurfaceTerrainTileExpansion.NineTileRingOffsets.Length; i++)
            {
                var off = SurfaceTerrainTileExpansion.NineTileRingOffsets[i];
                var dir = new Vector3(off.x, 0f, off.y);
                var dot = Vector3.Dot(dir.normalized, primaryForward.normalized);
                if (dot < bestDot)
                {
                    bestDot = dot;
                    skip = i;
                }
            }

            var list = new List<Vector2Int>(want);
            for (var i = 0; i < SurfaceTerrainTileExpansion.NineTileRingOffsets.Length && list.Count < want; i++)
            {
                if (i == skip)
                    continue;
                list.Add(SurfaceTerrainTileExpansion.NineTileRingOffsets[i]);
            }

            return list.ToArray();
        }

        static Vector3 TileCenterWorld(Terrain mainTerrain, Vector2Int offset)
        {
            var size = mainTerrain.terrainData.size;
            var origin = mainTerrain.transform.position;
            return origin + new Vector3(
                (offset.x + 0.5f) * size.x,
                0f,
                (offset.y + 0.5f) * size.z);
        }

        static void RunSurfaceMountainsStep(BuildSession session)
        {
            var rootTransform = session.RootTransform;
            var center = session.Center;
            var extent = session.Extent;
            var terrain = session.Terrain;
            var request = session.Request;

            var mountainsRoot = EnvironmentSceneUtility.GetOrCreateChild(rootTransform, SurfaceWorldPaths.MountainsName);
            LogSurfacePhase(9);
            if (!PipelineContentPreservePolicy.PreferMergeChildren(mountainsRoot, "surface mountains"))
                ClearChildren(mountainsRoot);

            if (request != null &&
                request.UseOuterRingMountains &&
                SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(
                    request,
                    request.SurfaceScope == SurfaceBuildScope.FullWorld))
            {
                PlaceOuterRingMountainMarkers(mountainsRoot, terrain, center);
                return;
            }

            PlaceMountainMarkers(mountainsRoot, center, 1, extent, terrain);
        }

        static void PlaceOuterRingMountainMarkers(Transform parent, Terrain mainTerrain, Vector3 center)
        {
            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (terrains.Count < 9)
            {
                PlaceMountainMarkers(parent, center, 8, 220f, mainTerrain);
                return;
            }

            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;
            foreach (var t in terrains)
            {
                if (t?.terrainData == null)
                    continue;
                var o = t.transform.position;
                var s = t.terrainData.size;
                minX = Mathf.Min(minX, o.x);
                maxX = Mathf.Max(maxX, o.x + s.x);
                minZ = Mathf.Min(minZ, o.z);
                maxZ = Mathf.Max(maxZ, o.z + s.z);
            }

            var cx = (minX + maxX) * 0.5f;
            var cz = (minZ + maxZ) * 0.5f;
            var halfW = (maxX - minX) * 0.5f;
            var halfH = (maxZ - minZ) * 0.5f;

            var dirs = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, 0f, 1f).normalized,
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, -1f).normalized,
                new Vector3(0f, 0f, -1f),
                new Vector3(-1f, 0f, -1f).normalized,
                new Vector3(-1f, 0f, 0f),
                new Vector3(-1f, 0f, 1f).normalized,
            };

            for (var i = 0; i < dirs.Length; i++)
            {
                var d = dirs[i];
                var p = new Vector3(
                    cx + d.x * halfW * 0.96f,
                    0f,
                    cz + d.z * halfH * 0.96f);
                p.y = SampleTerrainY(mainTerrain, p) + 1f;
                var m = new GameObject($"MountainPeak_Outer_{i}");
                CaveEditorUndo.RegisterCreated(m, "Outer ring mountain marker");
                m.transform.SetParent(parent, false);
                m.transform.position = p;
            }
        }

        public static IReadOnlyList<SurfaceCaveOpeningMarker> FindCaveOpenings()
        {
            var markers = UnityEngine.Object.FindObjectsByType<SurfaceCaveOpeningMarker>(FindObjectsInactive.Exclude);
            return markers ?? System.Array.Empty<SurfaceCaveOpeningMarker>();
        }

        static void ApplyAtmosphereFromRequest(WorldGenerationRequest request)
        {
            request ??= new WorldGenerationRequest();
            var rng = new System.Random(request.Seed + 12007);
            var foggy = request.Weather is WeatherKind.Foggy or WeatherKind.Stormy;
            RenderSettings.fog = foggy;
            if (foggy)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.0025f * request.FogDensityMultiplier;
                RenderSettings.fogColor = request.Weather == WeatherKind.Stormy
                    ? new Color(0.45f, 0.48f, 0.52f)
                    : new Color(0.62f, 0.68f, 0.72f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            var sky = request.Time switch
            {
                TimeOfDay.Dawn => new Color(0.82f, 0.62f, 0.52f),
                TimeOfDay.Dusk => new Color(0.75f, 0.45f, 0.38f),
                TimeOfDay.Night => new Color(0.18f, 0.22f, 0.35f),
                TimeOfDay.Overcast => new Color(0.58f, 0.6f, 0.62f),
                _ => new Color(0.72f, 0.82f, 0.95f),
            };
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.45f, 0.46f, 0.42f), sky, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.17f);

            RenderSettings.sun ??= UnityEngine.Object.FindAnyObjectByType<Light>();
            if (RenderSettings.sun != null)
            {
                RenderSettings.sun.type = LightType.Directional;
                RenderSettings.sun.intensity = request.Time == TimeOfDay.Night ? 0.35f : 0.95f + (float)rng.NextDouble() * 0.45f;
                var sunPitch = request.Time switch
                {
                    TimeOfDay.Dawn => 12f + (float)rng.NextDouble() * 10f,
                    TimeOfDay.Dusk => 8f + (float)rng.NextDouble() * 8f,
                    TimeOfDay.Night => -25f,
                    _ => 42f + (float)rng.NextDouble() * 18f,
                };
                var sunYaw = (float)(rng.NextDouble() * 360);
                RenderSettings.sun.transform.rotation = Quaternion.Euler(sunPitch, sunYaw, 0f);
            }
        }

        /// <summary>
        /// Creates radial walk trails when the surface root has none (ladder repair / trail walkability fix).
        /// </summary>
        /// <returns>Number of trail splines created this call (0 if terrain/root missing).</returns>
        public static int EnsureWalkTrails(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot)
        {
            if (ground?.Terrain == null || request == null || surfaceRoot == null)
                return 0;

            var trailsRoot = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (trailsRoot == null)
                trailsRoot = EnvironmentSceneUtility.GetOrCreateChild(surfaceRoot, SurfaceWorldPaths.TrailsName);
            if (trailsRoot.childCount > 0)
                return trailsRoot.childCount;

            var terrain = ground.Terrain;
            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var extent = request.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;
            var primaryForward = ResolvePrimaryForward(ground, request);
            var seed = request.Seed;
            var rng = new System.Random(seed);
            var trail = BuildTrail(trailsRoot, terrain, center, primaryForward, extent, seed, rng, mountain: true);
            var created = trail != null && trail.Length >= 2 ? 1 : 0;
            if (created > 0)
                SurfaceTerrainRadialAuthor.FlattenTrailBench(terrain, trail, 2.5f);

            SurfaceTerrainPlayRegion.FlushAllSurfaceTerrains(terrain);
            return created;
        }

        static Vector3[] BuildTrail(
            Transform parent,
            Terrain terrain,
            Vector3 center,
            Vector3 forward,
            float extent,
            int seed,
            System.Random rng,
            bool mountain)
        {
            var points = new List<Vector3>();
            var steps = mountain ? 5 + rng.Next(0, 4) : 3 + rng.Next(0, 4);
            var maxDist = SurfaceTerrainPlayRegion.ResolveMaxTrailDistance(terrain, center, forward);
            for (var s = 0; s <= steps; s++)
            {
                var t = s / (float)steps;
                var dist = Mathf.Lerp(extent * 0.12f, maxDist, t);
                var lateral = ((float)rng.NextDouble() - 0.5f) * 8f * (1f - t);
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                var p = center + forward * dist + right * lateral;
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(terrain, p.x, p.z, out _))
                    break;

                p.y = SampleTerrainY(terrain, p) + (mountain ? Mathf.Lerp(0.5f, 4f, t) : 0.2f);
                points.Add(p);
            }

            if (points.Count < 2)
                return null;

            var trailGo = new GameObject($"Trail_{seed % 1000}");
            CaveEditorUndo.RegisterCreated(trailGo, "Trail");
            trailGo.transform.SetParent(parent, false);
            for (var i = 0; i < points.Count; i++)
            {
                var wp = new GameObject($"Waypoint_{i}");
                CaveEditorUndo.RegisterCreated(wp, "Waypoint");
                wp.transform.SetParent(trailGo.transform, false);
                wp.transform.position = points[i];
            }

            return points.ToArray();
        }

        static void BuildRoad(Transform parent, Terrain terrain, Vector3 center, Vector3 forward, float length, int seed)
        {
            var road = new GameObject($"Road_{seed % 1000}");
            CaveEditorUndo.RegisterCreated(road, "Road");
            road.transform.SetParent(parent, false);
            var a = center + forward * 8f;
            var b = center + forward * length;
            a.y = SampleTerrainY(terrain, a);
            b.y = SampleTerrainY(terrain, b);
            var mid = (a + b) * 0.5f;
            road.transform.position = mid;
            road.transform.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            var scale = new Vector3(6f, 0.1f, Vector3.Distance(a, b));
            road.transform.localScale = scale;
        }

        static bool BuildWaterFeature(
            Transform parent,
            SceneGroundInfo ground,
            Vector3 center,
            Vector3 forward,
            float dist,
            int seed)
        {
            var p = center + forward * dist;
            var surfaceY = ground != null
                ? CaveGroundPlacementUtility.SampleHeightmapWorldY(ground, p)
                : float.NaN;
            if (float.IsNaN(surfaceY) && ground?.Terrain != null)
                surfaceY = SampleTerrainY(ground.Terrain, p);
            p.y = float.IsNaN(surfaceY) ? p.y : surfaceY + 0.08f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            CaveEditorUndo.RegisterCreated(go, "Water");
            go.name = $"Water_{seed % 1000}";
            go.transform.SetParent(parent, false);
            go.transform.position = p;
            go.transform.localScale = new Vector3(14f, 0.15f, 20f);
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var waterMat = WorldSurfaceWaterUtility.GetOrCreateSurfaceWater();
                if (waterMat != null)
                    mr.sharedMaterial = waterMat;
            }

            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return true;
        }

        static bool PlaceCaveOpening(
            Transform parent,
            Terrain terrain,
            Vector3 center,
            Vector3 forward,
            float dist,
            int sector,
            bool isPrimary = false,
            Vector2Int tileOffset = default)
        {
            var p = center + forward * dist;
            p.y = SampleTerrainY(terrain, p) + 0.05f;
            var go = new GameObject(isPrimary ? "CaveOpening_Primary" : $"CaveOpening_{sector}");
            CaveEditorUndo.RegisterCreated(go, "Cave opening");
            go.transform.SetParent(parent, false);
            go.transform.position = p;
            go.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            var marker = go.AddComponent<SurfaceCaveOpeningMarker>();
            marker.sectorIndex = sector;
            marker.distanceFromCenterMeters = dist;
            marker.isPrimaryEntrance = isPrimary;
            marker.terrainTileOffset = tileOffset;
            marker.suggestedDepthMeters = isPrimary ? 12f : 8f;
            return true;
        }

        static void PlaceMountainMarkers(Transform parent, Vector3 center, int dirs, float extent, Terrain terrain)
        {
            for (var i = 0; i < dirs; i += 2)
            {
                var angle = i / (float)dirs * Mathf.PI * 2f;
                var forward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var p = center + forward * (extent * 0.78f);
                p.y = SampleTerrainY(terrain, p) + 1f;
                var m = new GameObject($"MountainPeak_{i}");
                CaveEditorUndo.RegisterCreated(m, "Mountain marker");
                m.transform.SetParent(parent, false);
                m.transform.position = p;
            }
        }

        static float SampleTerrainY(Terrain terrain, Vector3 world)
        {
            if (terrain == null)
                return world.y;

            if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(terrain, world.x, world.z, out var tile))
                return tile.SampleHeight(world) + tile.transform.position.y;

            return terrain.SampleHeight(world) + terrain.transform.position.y;
        }

        static TerrainDressingPreset CreateFallbackDressing(WorldGenerationRequest request)
        {
            var preset = ScriptableObject.CreateInstance<TerrainDressingPreset>();
            preset.groundTint = new Color(0.32f, 0.42f, 0.28f);
            preset.secondaryTint = new Color(0.45f, 0.38f, 0.3f);
            return preset;
        }

        static void ClearChildren(Transform parent)
        {
            if (parent == null)
                return;

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null &&
                    !PipelineContentPreservePolicy.ShouldAllowDestroy(child.gameObject, "surface world clear"))
                    continue;

                CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        static Vector3 ResolvePrimaryForward(SceneGroundInfo ground, WorldGenerationRequest request)
        {
            var forward = ground.HasAnchor ? ground.HorizontalForward : Vector3.forward;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.y = 0f;
            forward.Normalize();

            if (request != null && Mathf.Abs(request.CaveEntranceYawDegrees) > 0.01f)
            {
                forward = Quaternion.Euler(0f, request.CaveEntranceYawDegrees, 0f) * forward;
                forward.y = 0f;
                forward.Normalize();
            }

            return forward;
        }

        static void WriteManifest(
            SurfaceWorldBuildReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Vector3 center,
            int dirs,
            float extent,
            int terrainPasses,
            int extraTiles)
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Generated"))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");

            var hub = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var path = Path.Combine(hub, SurfaceWorldPaths.ManifestRelativePath);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{SceneManager.GetActiveScene().name}\",");
            sb.AppendLine($"  \"seed\": {request.Seed},");
            sb.AppendLine($"  \"scope\": \"{request.SurfaceScope}\",");
            sb.AppendLine($"  \"center\": [{center.x:F2}, {center.y:F2}, {center.z:F2}],");
            sb.AppendLine($"  \"extentMeters\": {extent:F1},");
            sb.AppendLine($"  \"primaryTrailAxes\": {dirs},");
            sb.AppendLine($"  \"terrainBuildPasses\": {terrainPasses},");
            sb.AppendLine($"  \"extraTerrainTiles\": {extraTiles},");
            sb.AppendLine($"  \"trailCount\": {report.TrailCount},");
            sb.AppendLine($"  \"waterCount\": {report.WaterFeatureCount},");
            sb.AppendLine($"  \"caveOpeningCount\": {report.CaveOpeningCount},");
            sb.AppendLine($"  \"groundAnchor\": \"{ground.Anchor.name}\",");
            sb.AppendLine($"  \"surfaceRoot\": \"{SurfaceWorldPaths.RootName}\",");
            sb.AppendLine($"  \"hillshadeIndex\": \"{SurfaceLidarTerrainAuthor.HillshadesIndexRel}\",");
            sb.AppendLine($"  \"surfacePhaseCount\": {SurfacePhaseCount}");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
