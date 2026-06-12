using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Runs open-sky surface generation without disturbing existing cave systems unless scope is FullWorld.</summary>
    public static class CaveBuildSurfacePipeline
    {
        /// <summary>Runs full surface build on the editor queue (legacy name — same as <see cref="QueueSurfaceWorldAndTerrainPhases"/>).</summary>
        public static void QueueBeforeCaveBuild(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            System.Action<SurfaceWorldBuildReport> onComplete) =>
            QueueSurfaceWorldAndTerrainPhases(ground, request, onComplete);

        /// <summary>Florida LiDAR surface world + terrain AI phases (intended after cave geometry is queued).</summary>
        public static void QueueSurfaceWorldAndTerrainPhases(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            System.Action<SurfaceWorldBuildReport> onComplete)
        {
            if (request == null || request.SurfaceScope == SurfaceBuildScope.CaveOnly)
            {
                onComplete?.Invoke(null);
                return;
            }

            // Single orchestrator ownership: FullWorld terrain must be kicked from startup,
            // not from side-path cave callbacks while a phased cave pipeline is already active.
            if (request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                LavaTubeCaveBuildPipeline.IsPhasedBuildActive &&
                !CaveBuildStartupCoordinator.IsActive)
            {
                var msg =
                    "Surface pipeline start blocked: FullWorld terrain must be started by StartupCoordinator. " +
                    "Use Build Complete Cave (terrain-first).";
                CaveBuildEditorLog.LogSurfaceWarning("[Surface] " + msg);
                onComplete?.Invoke(new SurfaceWorldBuildReport { Success = false, Message = msg });
                return;
            }

            SurfaceTerrainGridRegistry.ResetSession();
            CaveBuildSurfaceCompletionGate.MarkSurfaceBuildStarted();
            var scopeLabel = request.SurfaceScope switch
            {
                SurfaceBuildScope.FullWorld => "FullWorld (surface world + terrain phases; cave runs separately when scoped)",
                SurfaceBuildScope.SurfaceOnly => "SurfaceOnly (terrain + surface world only — no cave geometry)",
                _ => request.SurfaceScope.ToString(),
            };
            CaveBuildEditorLog.LogSurface(
                $"Surface pipeline starting [{scopeLabel}] — micro-paced steps (full quality).",
                forceUnityConsole: true);

            CaveBuildActionPacing.ScheduleHeavy(
                () => QueueSurfaceWorldThenTerrainPhases(ground, request, onComplete),
                CaveBuildPipelineDomains.SurfaceQueueLabel("surface world + terrain phases"));
        }

        static void QueueSurfaceWorldThenTerrainPhases(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            System.Action<SurfaceWorldBuildReport> onComplete)
        {
            CaveBuildActionPacing.ScheduleHeavy(
                () => RunSurfaceResearchThenQueueBuild(ground, request, onComplete),
                CaveBuildPipelineDomains.SurfaceQueueLabel("surface research + hillshade"));
        }

        static void RunSurfaceResearchThenQueueBuild(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            System.Action<SurfaceWorldBuildReport> onComplete)
        {
            if (!TryBeginSurfaceWorldSetup(ground, request, out var replaceSurface, out var preFailReport))
            {
                CaveBuildSurfaceCompletionGate.MarkSurfacePipelineFailed(request);
                onComplete?.Invoke(preFailReport);
                return;
            }

            void QueueResearchThenBuild()
            {
                CaveBuildPrePlacementResearch.QueueRunBeforeAnyPlacement(
                    ground,
                    request,
                    !replaceSurface,
                    (researchOk, researchMsg) =>
                    {
                        if (!researchOk)
                        {
                            var fail = new SurfaceWorldBuildReport { Success = false, Message = researchMsg };
                            CaveBuildSurfaceCompletionGate.MarkSurfacePipelineFailed(request);
                            onComplete?.Invoke(fail);
                            return;
                        }

                        if (!string.IsNullOrEmpty(researchMsg))
                            Debug.Log("[CaveBuild] " + researchMsg);

                        QueueSurfaceWorldBuildAfterResearch(ground, request, replaceSurface, onComplete);
                    });
            }

            var skipNetworkResearchHelpers =
                CaveBuildSessionPreset.AllowProceduralTerrainWithoutResearch &&
                !CaveBuildResearchCacheBridge.HasUsableLocalResearchCache();
            var skipPlannerSurfaceHelpers =
                CaveBuildSessionConfig.HasFinalizedActive &&
                CaveBuildPlannerLayoutBridge.TryLoad(out _, out _);
            if (skipNetworkResearchHelpers || skipPlannerSurfaceHelpers)
            {
                CaveBuildEditorLog.LogSurface(
                    skipPlannerSurfaceHelpers
                        ? "[Surface] Planner session — skipping tsx pre-placement helpers; Unity planner terrain pipeline owns sculpt."
                        : "[Startup] No API / no ResearchCache — skipping tsx research sync; procedural terrain.",
                    forceUnityConsole: true);
                QueueResearchThenBuild();
                return;
            }

            var helperCtx = CaveBuildHelperScriptOrchestrator.MakeContext(request);
            helperCtx.PhaseId = "research";
            helperCtx.Rung = "terrain_integration";
            helperCtx.AdditiveSurface = !replaceSurface;
            CaveBuildHelperScriptOrchestrator.Queue(
                CaveBuildHelperScriptOrchestrator.Moment.SurfacePrePlacement,
                helperCtx,
                (helperOk, helperMsg) =>
                {
                    if (!helperOk)
                    {
                        var fail = new SurfaceWorldBuildReport
                        {
                            Success = false,
                            Message = helperMsg,
                        };
                        CaveBuildSurfaceCompletionGate.MarkSurfacePipelineFailed(request);
                        onComplete?.Invoke(fail);
                        return;
                    }

                    if (!string.IsNullOrEmpty(helperMsg))
                        Debug.Log("[CaveBuild] " + helperMsg);

                    QueueResearchThenBuild();
                });
        }

        /// <summary>Playtest resume — research already done or being resumed separately.</summary>
        public static void ResumeSurfaceBuildAfterResearch(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            System.Action<SurfaceWorldBuildReport> onComplete)
        {
            if (!TryBeginSurfaceWorldSetup(ground, request, out var replaceSurface, out var failReport))
            {
                CaveBuildSurfaceCompletionGate.MarkSurfacePipelineFailed(request);
                onComplete?.Invoke(failReport);
                return;
            }

            QueueSurfaceWorldBuildAfterResearch(ground, request, replaceSurface, onComplete);
        }

        static void QueueSurfaceWorldBuildAfterResearch(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool replaceSurface,
            System.Action<SurfaceWorldBuildReport> onComplete)
        {
            CaveBuildActionPacing.SchedulePipelineFirstStep(
                () => SurfaceWorldGenerator.QueueBuild(
                ground,
                request,
                replaceSurface,
                report =>
                {
                    if (report is not { Success: true })
                    {
                        CaveBuildSurfaceCompletionGate.MarkSurfacePipelineFailed(request);
                        if (report != null && !string.IsNullOrEmpty(report.Message))
                            EditorUtility.DisplayDialog("Surface World", report.Message, "OK");
                        onComplete?.Invoke(report);
                        return;
                    }

                    CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                        request, ground, true);

                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Surface world phases complete — terrain AI phases + grading next.",
                        forceUnityConsole: true);

                    SurfaceTerrainAiPhases.QueueAllPhasesAndLadder(
                        ground,
                        request,
                        (phasesOk, phasesMsg) =>
                        {
                            try
                            {
                                FinishSurfacePipelineAfterTerrainPhases(ground, request, report, phasesOk, phasesMsg);
                            }
                            finally
                            {
                                var success = report is { Success: true } && phasesOk;
                                CaveBuildSurfaceCompletionGate.MarkSurfaceBuildFinished(request, success);
                            }

                            if (report is { Success: true } && phasesOk)
                            {
                                CaveBuildEditorLog.LogSurface(
                                    "Above-ground terrain complete — pre-build / cave may proceed.",
                                    forceUnityConsole: true);
                            }

                            onComplete?.Invoke(report);
                        });
                }),
                CaveBuildPipelineDomains.SurfaceQueueLabel("surface world (12 phases)"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static bool TryBeginSurfaceWorldBuild(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out bool replaceSurface,
            out SurfaceWorldBuildReport failReport) =>
            TryBeginSurfaceWorldSetup(ground, request, out replaceSurface, out failReport);

        /// <summary>Scene/request setup only — research runs via <see cref="CaveBuildPrePlacementResearch.QueueRunBeforeAnyPlacement"/>.</summary>
        static bool TryBeginSurfaceWorldSetup(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out bool replaceSurface,
            out SurfaceWorldBuildReport failReport)
        {
            failReport = null;
            replaceSurface = false;
            if (request == null)
                return true;

            if (request.SurfaceScope is not (SurfaceBuildScope.SurfaceOnly or SurfaceBuildScope.FullWorld))
                return true;

            EnvironmentSceneUtility.RepairKitRootTerrainState();
            ground = SceneGroundResolver.EnforceFullWorldGround(
                SceneGroundResolver.ResolveForFullWorld(ground?.Anchor));
            if (!ground.HasAnchor)
            {
                failReport = new SurfaceWorldBuildReport
                {
                    Success = false,
                    Message = "No Ground anchor — tag walkable floor as Ground before building.",
                };
                return false;
            }

            if (!TryValidateSurfaceTerrainReady(ground, out var terrainFail))
            {
                failReport = terrainFail;
                return false;
            }

            request.AllowCreateTerrain = true;
            request.HeightStyle = request.SurfaceIncludeMountains
                ? TerrainHeightStyle.Mountains
                : TerrainHeightStyle.Hilly;
            request.Time = TimeOfDay.Day;
            request.Weather = WeatherKind.Clear;

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            var hasSurface = envRoot != null &&
                             envRoot.transform.Find(SurfaceWorldPaths.RootName) != null;
            var blockoutAnchoredSurface = hasSurface && IsPriorSurfaceBlockoutAnchored(envRoot?.transform.Find(SurfaceWorldPaths.RootName));
            replaceSurface = request.SurfaceScope == SurfaceBuildScope.SurfaceOnly ||
                             !hasSurface ||
                             (request.SurfaceScope == SurfaceBuildScope.FullWorld && blockoutAnchoredSurface);

            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                if (SurfaceTerrainTileExpansion.RequiresFullWorldTerrainFirstRebuild(ground, request))
                {
                    if (SurfaceTerrainTileExpansion.TryRepairFullWorldGridInPlace(
                            SurfaceTerrainTileExpansion.FindMainTerrainInScene(),
                            ground,
                            request))
                    {
                        replaceSurface = false;
                    }
                    else if (!SurfaceTerrainTileExpansion.FullWorldTerrainPurgedThisBuild)
                    {
                        SurfaceTerrainTileExpansion.PurgeFullWorldTerrainForRebuild();
                        replaceSurface = true;
                        SceneGroundResolver.ClearStoredGround();
                        SurfaceTerrainBlockoutLayoutAuthor.DemoteModularGridGroundTag();
                        if (ground != null)
                            SceneGroundResolver.ApplyGround(ground, SceneGroundResolver.EnforceFullWorldGround(ground));
                        var tilePlan = CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                            ? SurfaceOpenWorldGridExpansion.BuildAaaExtendedPlaceOrder().Length
                            : SurfaceTerrainTileExpansion.FullWorldTerrainTileCount;
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] FullWorld terrain-first — could not repair in place; rebuilding flat {tilePlan}-tile grid.",
                            forceUnityConsole: true);
                    }
                }
            }

            replaceSurface = PipelineContentPreservePolicy.ShouldReplaceGeneratedSurfaceWorld(replaceSurface, request);

            if (request.SurfaceScope == SurfaceBuildScope.FullWorld && hasSurface && !replaceSurface)
            {
                Debug.Log(
                    "[CaveBuild] FullWorld: updating surface in place (additive) — not deleting GeneratedSurfaceWorld.");
            }
            else if (request.SurfaceScope == SurfaceBuildScope.FullWorld && replaceSurface && hasSurface)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] FullWorld terrain-first — replacing GeneratedSurfaceWorld (prior build was Grid/blockout anchored).",
                    forceUnityConsole: true);
            }

            return true;
        }

        static bool TryValidateSurfaceTerrainReady(SceneGroundInfo ground, out SurfaceWorldBuildReport fail)
        {
            fail = null;
            if (ground == null || !ground.HasAnchor)
                return true;

            if (EnvironmentSceneUtility.IsLiveTerrain(ground.Terrain))
                return true;

            var found = EnvironmentSceneUtility.FindTerrainInActiveScene(
                null,
                ground,
                allowCreate: true,
                EnvironmentKitHardwareBudget.Active.TerrainMaxSizeMeters,
                EnvironmentKitHardwareBudget.Active.TerrainMaxHeightMeters);
            if (found == null)
                found = CaveBuildTerrainEnsure.TryEnsure(ground, null, WorldGenerationRequest.LoadOrDefault()?.Seed ?? 0, out _);

            if (EnvironmentSceneUtility.IsLiveTerrain(found))
            {
                ground.Terrain = found;
                return true;
            }

            fail = new SurfaceWorldBuildReport
            {
                Success = false,
                Message =
                    "Surface build stopped early — no usable Terrain for FullWorld.\n\n" +
                    "• Tag your walkable terrain as Ground\n" +
                    "• Or add a Terrain to the scene\n" +
                    "• If EnvironmentRoot has a broken TerrainCollider, delete it and retry",
            };
            return false;
        }

        static bool IsPriorSurfaceBlockoutAnchored(Transform surfaceRoot)
        {
            if (surfaceRoot == null)
                return false;

            if (!SurfaceTerrainBlockoutLayoutAuthor.TryFindBlockoutGrid(out var grid, out _))
                return false;

            if (surfaceRoot.IsChildOf(grid))
                return true;

            var env = surfaceRoot.parent;
            while (env != null)
            {
                if (env == grid)
                    return true;
                env = env.parent;
            }

            foreach (var terrain in surfaceRoot.GetComponentsInChildren<Terrain>(true))
            {
                if (terrain != null && terrain.transform.IsChildOf(grid))
                    return true;
            }

            return false;
        }

        static void FinishSurfacePipelineAfterTerrainPhases(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            SurfaceWorldBuildReport surfaceReport,
            bool phasesOk,
            string phasesMsg)
        {
            if (!phasesOk)
            {
                Debug.LogWarning("[CaveBuild] " + phasesMsg);
                surfaceReport.Success = false;
                if (!string.IsNullOrEmpty(phasesMsg))
                    surfaceReport.Message = $"{surfaceReport.Message} | Terrain phases: {phasesMsg}";
            }
            else if (!string.IsNullOrEmpty(phasesMsg))
                Debug.Log("[CaveBuild] " + phasesMsg);

            if (surfaceReport == null || !surfaceReport.Success)
                return;

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            if (envRoot == null)
                return;

            WorldRuntimeResourcesAuthor.EnsureAllEnemyPrefabsInResources();
            var surfaceRoot = envRoot.transform.Find(SurfaceWorldPaths.RootName);
            SurfaceTerrainEnemySpawnerPlacement.EnsureOnSurface(surfaceRoot, request, null);

            var terrainReport = SurfaceTerrainQualityGrader.Run(ground, request, surfaceRoot);
            CaveBuildHealOrchestrator.CaptureIfEnabled(
                terrainReport?.BuildAcceptable == true ? "surface_graded" : "surface_graded_attempt",
                request.Seed,
                ground?.Terrain,
                UnityEngine.Object.FindAnyObjectByType<CaveBuildMetadata>()?.transform);
            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
                CaveBuildTerrainCursorDeferred.MarkAfterSurface(terrainReport, ground, request);
            else
                InvokeTerrainCursorWorkflow(terrainReport, ground);

            CaveBuildPipelineLog.Info(surfaceReport.Message, "Surface");
            if (surfaceReport.Success)
            {
                var seed = request.Seed;
                CaveBuildPhaseContractRegistry.MarkRungComplete(
                    CaveBuildPhaseContractRegistry.RungMacroTerrain, seed);
                CaveBuildPhaseContractRegistry.MarkRungComplete(
                    CaveBuildPhaseContractRegistry.RungHydrologyMasks, seed);
                CaveBuildPhaseContractRegistry.MarkRungComplete(
                    CaveBuildPhaseContractRegistry.RungTrailsNav, seed);
                CaveBuildPhaseContractRegistry.MarkRungComplete(
                    CaveBuildPhaseContractRegistry.RungSurfaceProps, seed);
                CaveBuildWorldLayoutAudit.Run(ground, request);
            }
        }

        static bool TryRunSurfaceWorldBuild(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out SurfaceWorldBuildReport surfaceReport)
        {
            surfaceReport = null;
            if (!TryBeginSurfaceWorldSetup(ground, request, out var replaceSurface, out var failReport))
            {
                surfaceReport = failReport;
                return false;
            }

            if (request != null &&
                request.SurfaceScope is SurfaceBuildScope.SurfaceOnly or SurfaceBuildScope.FullWorld &&
                !CaveBuildPrePlacementResearch.RunBeforeAnyPlacement(ground, request, !replaceSurface, out var preMsg))
            {
                surfaceReport = new SurfaceWorldBuildReport { Message = preMsg };
                return false;
            }

            if (request == null ||
                request.SurfaceScope is not (SurfaceBuildScope.SurfaceOnly or SurfaceBuildScope.FullWorld))
                return true;

            surfaceReport = SurfaceWorldGenerator.Build(ground, request, replaceExistingSurface: replaceSurface);
            if (!surfaceReport.Success)
            {
                EditorUtility.DisplayDialog("Surface World", surfaceReport.Message, "OK");
                return false;
            }

            return true;
        }

        public static bool TryRunBeforeCaveBuild(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out SurfaceWorldBuildReport surfaceReport,
            bool trackCompletionGate = true)
        {
            surfaceReport = null;
            if (request == null)
                return true;

            var needsSurface =
                request.SurfaceScope is SurfaceBuildScope.FullWorld or SurfaceBuildScope.SurfaceOnly;
            if (needsSurface && trackCompletionGate)
                SurfaceTerrainGridRegistry.ResetSession();
            CaveBuildSurfaceCompletionGate.MarkSurfaceBuildStarted();

            try
            {
                return TryRunBeforeCaveBuildCore(ground, request, out surfaceReport);
            }
            finally
            {
                if (needsSurface && trackCompletionGate)
                {
                    CaveBuildSurfaceCompletionGate.MarkSurfaceBuildFinished(
                        request,
                        surfaceReport is { Success: true });
                }
            }
        }

        static bool TryRunBeforeCaveBuildCore(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out SurfaceWorldBuildReport surfaceReport)
        {
            surfaceReport = null;
            if (request == null)
                return true;

            switch (request.SurfaceScope)
            {
                case SurfaceBuildScope.CaveOnly:
                    return true;
                case SurfaceBuildScope.SurfaceOnly:
                case SurfaceBuildScope.FullWorld:
                {
                    if (!TryRunSurfaceWorldBuild(ground, request, out surfaceReport))
                        return false;

                    var phasesOk = SurfaceTerrainAiPhases.RunAllPhasesBlocking(ground, request, out var phasesMsg);
                    if (!phasesOk)
                        Debug.LogWarning("[CaveBuild] " + phasesMsg);
                    else if (!string.IsNullOrEmpty(phasesMsg))
                        Debug.Log("[CaveBuild] " + phasesMsg);

                    FinishSurfacePipelineAfterTerrainPhases(
                        ground,
                        request,
                        surfaceReport,
                        phasesOk,
                        phasesMsg);

                    return surfaceReport.Success && phasesOk;
                }
                default:
                    return true;
            }
        }

        public static void InvokeTerrainCursorWorkflow(
            SurfaceTerrainLadderReport terrainReport,
            SceneGroundInfo ground)
        {
            if (terrainReport == null || terrainReport.BuildAcceptable)
                return;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.autoInvokeTerrainAfterSurfaceBuild)
                return;

            if (!CaveBuildCursorAgentBridge.HasApiKey)
            {
                Debug.Log(
                    "[CaveBuild] Terrain below target — configure Hub → Active provider + API key to auto-invoke terrain grader.");
                return;
            }

            if (CaveBuildPendingGeometryBuild.HasPending)
            {
                Debug.Log(
                    "[CaveBuild] Terrain Cursor skipped — cave geometry is queued (pre-build). Runs after cave build.");
                CaveBuildTerrainCursorDeferred.MarkAfterSurface(terrainReport, ground, null);
                return;
            }

            if (TerrainBuildCursorAgentBridge.IsAgentRunning)
            {
                Debug.Log("[CaveBuild] Terrain Cursor deferred — agent already running.");
                return;
            }

            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    if (TerrainBuildCursorAgentBridge.TryBeginTerrainWorkflow(
                            terrainReport,
                            ground,
                            out var msg))
                        Debug.Log("[CaveBuild] " + msg);
                    else
                        Debug.LogWarning("[CaveBuild] Terrain workflow: " + msg);
                },
                "terrain Cursor workflow after surface build",
                CaveBuildActionPacing.ActionWeight.Normal);

            if (settings.suggestTerrainGradeWatcher)
            {
                Debug.Log(
                    "[CaveBuild] CLI watcher: cd Tools/cave-grader && npm run watch-terrain-grade");
            }
        }

        public static bool ShouldSkipCaveGeometry(WorldGenerationRequest request) =>
            request != null && request.SurfaceScope == SurfaceBuildScope.SurfaceOnly;

        public static void AlignCaveAfterGenerate(Transform caveRoot, SceneGroundInfo ground, WorldGenerationRequest request)
        {
            if (request == null || caveRoot == null)
                return;

            if (CaveBuildWorkflowCoordinator.IsGroundPlacementLocked)
            {
                Debug.Log("[CaveBuild] Skipping cave re-align — ground placement already locked.");
                return;
            }

            if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
            {
                Debug.Log("[CaveBuild] Skipping cave re-align — root XZ locked to surface opening.");
                return;
            }

            if (request.SurfaceScope is SurfaceBuildScope.FullWorld or SurfaceBuildScope.CaveOnly)
            {
                var sector = request.PreferredCaveOpeningSector;
                if (sector < 0)
                {
                    var openings = SurfaceWorldGenerator.FindCaveOpenings();
                    if (openings.Count > 0)
                        sector = new System.Random(request.Seed + 3319).Next(openings.Count);
                }

                SurfaceCaveOpeningAligner.TryAlignCaveRootToOpening(caveRoot, ground, preferredSector: sector);
            }
        }
    }
}
