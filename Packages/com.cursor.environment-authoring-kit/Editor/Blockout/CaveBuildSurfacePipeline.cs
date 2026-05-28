using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
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

            CaveBuildSurfaceCompletionGate.MarkSurfaceBuildStarted();
            CaveBuildEditorLog.LogSurface(
                "Surface + terrain pipeline starting (Ground anchor up; LiDAR prompts + DEM authoritative).",
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
            if (skipNetworkResearchHelpers)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Startup] No API / no ResearchCache — skipping tsx research sync; procedural terrain.",
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

            request.AllowCreateTerrain = true;
            request.HeightStyle = request.SurfaceIncludeMountains
                ? TerrainHeightStyle.Mountains
                : TerrainHeightStyle.Hilly;
            request.Time = TimeOfDay.Day;
            request.Weather = WeatherKind.Clear;

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            var hasSurface = envRoot != null &&
                             envRoot.transform.Find(SurfaceWorldPaths.RootName) != null;
            replaceSurface = request.SurfaceScope == SurfaceBuildScope.SurfaceOnly || !hasSurface;
            if (request.SurfaceScope == SurfaceBuildScope.FullWorld && hasSurface)
            {
                Debug.Log(
                    "[CaveBuild] FullWorld: updating surface in place (additive) — not deleting GeneratedSurfaceWorld.");
            }

            return true;
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

            var surfaceRoot = envRoot.transform.Find(SurfaceWorldPaths.RootName);
            var enemyPrefab = CaveCombatSetupUtility.EnsureEnemyPrefab();
            SurfaceTerrainEnemySpawnerPlacement.EnsureOnSurface(surfaceRoot, request, enemyPrefab);

            var terrainReport = SurfaceTerrainQualityGrader.Run(ground, request, surfaceRoot);
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
