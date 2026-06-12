#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Queues build startup so clicking Build does not freeze the editor on one long main-thread block.</summary>
    public static class CaveBuildStartupCoordinator
    {
        enum StartupStep
        {
            PrepareScene = 0,
            LayoutRoll = 1,
            SurfacePipeline = 2,
            PreBuildGate = 3,
            CaveGeometry = 4,
        }

        struct PendingBuild
        {
            public bool OpenMainSceneFirst;
            public bool HideLegacyBlockout;
            public bool SkipDialogs;
            public bool LayoutPrototype;
            public bool SkipPreBuildGate;
            public SurfaceBuildScope SurfaceScope;
            public System.Action<bool> OnDeferRelease;
        }

        static PendingBuild _pending;
        static StartupStep _step;
        static bool _active;
        static SceneGroundInfo _ground;
        static CaveLayoutRoll _roll;
        static WorldGenerationRequest _request;
        static string _sceneName;
        static SurfaceWorldBuildReport _surfaceReport;
        static CaveBuildPreBuildReport _cachedPreBuildReport;
        static int _preBuildCompileWaitTicks;
        static double _preBuildGateWatchdogAt;

        public static bool IsActive => _active;

        /// <summary>
        /// Called when terrain AI phases + post-terrain helper scripts finish — unblocks pre-build if the editor queue was backed up.
        /// </summary>
        public static void OnTerrainPipelineHelpersComplete(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (request == null)
                return;

            CaveBuildSurfaceCompletionGate.ReleaseStuckHandoffForStartup(request, ground, true);

            if (!LavaTubeCaveBuilder.IsBuildInProgress)
                return;

            if (_active && _step == StartupStep.PreBuildGate)
            {
                CaveBuildEditorLog.LogCave(
                    "[Startup] Terrain helpers done — priority pre-build gate retry.",
                    forceUnityConsole: true);
                CaveBuildActionPacing.SchedulePriorityFirstStep(
                    RunPreBuildGateEvaluate,
                    CaveBuildPipelineDomains.QueueLabel("startup — pre-build after terrain"));
                return;
            }

            if (!_active && CaveBuildPendingGeometryBuild.HasPending)
            {
                CaveBuildEditorLog.LogCave(
                    "[Startup] Terrain done — resuming pending cave geometry.",
                    forceUnityConsole: true);
                CaveBuildPendingGeometryBuild.TryRunPending(out _);
            }
        }

        /// <summary>Clears startup coordinator state after <see cref="CaveBuildEmergencyRecovery"/>.</summary>
        public static void EmergencyResetStartup()
        {
            CaveBuildPreBuildReloop.ResetSession();
            _active = false;
            _pending = default;
            _ground = default;
            _roll = default;
            _request = null;
            _sceneName = null;
            _surfaceReport = null;
            _step = StartupStep.PrepareScene;
            _preBuildGateWatchdogAt = 0;
        }

        public static void QueueBuild(
            bool openMainSceneFirst,
            bool hideLegacyBlockout,
            bool skipDialogs,
            bool layoutPrototype,
            bool skipPreBuildGate,
            SurfaceBuildScope surfaceScope,
            System.Action<bool> onDeferRelease)
        {
            if (_active)
            {
                CaveBuildEditorLog.LogCaveWarning("Build startup already queued.");
                return;
            }

            _pending = new PendingBuild
            {
                OpenMainSceneFirst = openMainSceneFirst,
                HideLegacyBlockout = hideLegacyBlockout,
                SkipDialogs = skipDialogs,
                LayoutPrototype = layoutPrototype,
                SkipPreBuildGate = skipPreBuildGate,
                SurfaceScope = surfaceScope,
                OnDeferRelease = onDeferRelease,
            };
            _step = StartupStep.PrepareScene;
            _active = true;
            _surfaceReport = null;
            CaveBuildPreBuildReloop.ResetSession();
            CaveBuildHealOrchestrator.ResetSession();

            CaveBuildPipelinePhaseTracker.OnBuildSessionStart(_pending.SurfaceScope);
            CaveBuildRunStatusPublisher.SetPhase("startup", "Queued build startup — preparing scene…");
            CaveBuildEditorLog.LogCave(
                "Build startup queued — watch Environment Kit Hub (Build tab) and the editor progress bar.",
                forceUnityConsole: true);

            EnvironmentKitHubWindow.EnsureOpenForBuild();
            CaveBuildHelperScriptOrchestrator.Queue(
                CaveBuildHelperScriptOrchestrator.Moment.BuildSessionStart,
                CaveBuildHelperScriptOrchestrator.MakeContext(null),
                OnBuildSessionHelpersComplete);
        }

        static void OnBuildSessionHelpersComplete(bool ok, string message)
        {
            if (!ok)
            {
                CaveBuildEditorLog.LogCaveWarning("[Startup] Helper scripts failed: " + message);
                Complete(false);
                return;
            }

            CaveBuildEditorLog.LogCave(
                "[Startup] Tooling check done — continuing scene prep (watch Hub Build tab + progress bar).",
                forceUnityConsole: true);
            CaveBuildRunStatusPublisher.SetPhase("startup", "Scene prep & layout roll");

            EditorApplication.delayCall += () =>
            {
                if (!_active)
                    return;
                ScheduleStep();
            };
        }

        static void ScheduleStep()
        {
            var label = _step switch
            {
                StartupStep.PrepareScene => "prepare scene & Ground tag",
                StartupStep.LayoutRoll => "layout roll & production recipe",
                StartupStep.PreBuildGate => "pre-build readiness gate",
                StartupStep.CaveGeometry => "cave geometry pipeline (last)",
                StartupStep.SurfacePipeline => "Florida LiDAR + terrain from Ground (before cave)",
                _ => "finish",
            };
            var queueLabel = CaveBuildPipelineDomains.QueueLabel($"startup — {label}");
            if (_step == StartupStep.PreBuildGate)
            {
                CaveBuildActionPacing.SchedulePriorityFirstStep(RunPreBuildGate, queueLabel);
                return;
            }

            var weight = _step == StartupStep.PrepareScene || _step == StartupStep.LayoutRoll
                ? CaveBuildActionPacing.ActionWeight.Light
                : CaveBuildActionPacing.ActionWeight.Heavy;
            CaveBuildActionPacing.SchedulePipelineFirstStep(RunStep, queueLabel, weight);
        }

        static void RunStep()
        {
            if (!_active)
                return;

            try
            {
                switch (_step)
                {
                    case StartupStep.PrepareScene:
                        RunPrepareScene();
                        break;
                    case StartupStep.LayoutRoll:
                        RunLayoutRoll();
                        break;
                    case StartupStep.SurfacePipeline:
                        RunSurfacePipeline();
                        break;
                    case StartupStep.PreBuildGate:
                        RunPreBuildGate();
                        break;
                    case StartupStep.CaveGeometry:
                        RunCaveGeometry();
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                Complete(false);
            }
        }

        static void RunPrepareScene()
        {
            SetProgress(0.08f, "[Startup] Presets & Ground tag…");
            if (_pending.OpenMainSceneFirst)
            {
                EnvironmentKitSceneSafeguards.GuardActiveSceneForBuild();
                LavaTubeCaveBuilder.StartupTryOpenMainScene();
            }

            if (!LavaTubeCaveBuilder.StartupEnsurePresets())
            {
                Complete(false);
                return;
            }

            LavaTubeCaveBuilder.StartupClearInvalidGround();
            CaveBuildProjectSetup.EnsureMinimalSceneDefaults(out var createdDefaults);
            if (createdDefaults)
            {
                CaveBuildEditorLog.LogCave(
                    "[Startup] Empty scene — created default Ground, PortalFive, and grid anchors.",
                    forceUnityConsole: true);
            }

            _ground = _pending.SurfaceScope == SurfaceBuildScope.FullWorld
                ? SceneGroundResolver.EnforceFullWorldGround(
                    SceneGroundResolver.ResolveForFullWorld(LavaTubeCaveBuilder.StartupLoadUserGround()))
                : SceneGroundResolver.Resolve(LavaTubeCaveBuilder.StartupLoadUserGround());
            if (_pending.SurfaceScope == SurfaceBuildScope.FullWorld &&
                _ground.HasAnchor &&
                (SceneGroundResolver.IsModularKitBlockoutGrid(_ground.Anchor) ||
                 SceneGroundResolver.IsInvalidFullWorldSurfaceGround(_ground.Anchor)))
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    "[Startup] Ground resolved to Grid/RouteTerrainFloor/tilted mesh — rebinding to SurfaceTerrainMain or PortalFive.");
            }

            if (!_ground.HasAnchor)
            {
                CaveBuildCompletionSummary.ShowBlocked(
                    "No ground found. Tag walkable floor as 'Ground' or assign in Environment Kit.");
                Complete(false);
                return;
            }

            _sceneName = SceneManager.GetActiveScene().name;
            // Never block startup on a context menu prompt; pick the best portal automatically.
            CaveBuildPortalSettings.PromptIfNeeded(showDialog: false);

            if (!CaveBuildWorkflowGuardrails.TryArtifactPreflight(out var preflightMsg))
            {
                CaveBuildEditorLog.LogCaveWarning("[Startup] " + preflightMsg);
                CaveBuildCompletionSummary.ShowBlocked(preflightMsg);
                Complete(false);
                return;
            }

            if (preflightMsg.StartsWith("artifact_preflight advisory", System.StringComparison.Ordinal))
                CaveBuildEditorLog.LogCaveWarning("[Startup] " + preflightMsg);
            else
                CaveBuildEditorLog.LogCave("[Startup] " + preflightMsg, forceUnityConsole: true);

            CaveBuildPipelineScope.BeginFullPipeline();
            if (_pending.SurfaceScope == SurfaceBuildScope.FullWorld && !_pending.LayoutPrototype)
            {
                CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungCaveLayout);
                CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungPolish);
            }

            CaveBuildUnifiedFlow.LogFlowStart(_sceneName, _pending.LayoutPrototype);
            _step = StartupStep.LayoutRoll;
            ScheduleStep();
        }

        static void RunLayoutRoll()
        {
            SetProgress(0.2f, "[Startup] Layout roll & recipe…");
            var randomizeSeed = _pending.SurfaceScope == SurfaceBuildScope.FullWorld
                ? FullWorldConceptLayoutCatalog.RandomOnBuildEnabled
                : EditorPrefs.GetBool("CaveBuild_RandomizeEachTime", true);
            if (_pending.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                if (!CaveBuildSessionConfig.HasFinalizedActive)
                    CaveBuildFreshBuildSession.PrepareForNewBuild(
                        randomizeSeed,
                        CaveBuildSpeedDemoPolicy.IsActiveHubSelection());
                else
                    CaveBuildSessionConfig.ApplyToEditorSettings();
            }
            else if (randomizeSeed)
            {
                CaveBuildSeedDefaults.ForceNewSeedBeforeLayoutRoll();
            }

            _roll = LavaTubeCaveBuilder.StartupCreateLayoutRoll();
            CaveBuildLayoutRollSession.Record(_roll);
            LavaTubeCaveBuilder.StartupLogLayoutRoll(_roll);

            var productionRecipe = CaveBuildAaaProductionBootstrap.PrepareFullProductionBuild(
                _roll,
                _pending.SurfaceScope,
                _pending.LayoutPrototype);

            _request = new WorldGenerationRequest
            {
                Biome = BiomeId.Cave,
                CaveMode = CaveGenerationMode.FullSystem,
                UseLayoutPrototype = _pending.LayoutPrototype,
                UseSplineMesh = !_pending.LayoutPrototype,
                UseTrue3DCaveSystem = !_pending.LayoutPrototype,
                UseBlockTunnel = !_pending.LayoutPrototype,
                UseTerrainCarve = !_pending.LayoutPrototype,
                AllowCreateTerrain = !_pending.LayoutPrototype,
                IncludeCaveWater = false,
                SurfaceScope = _pending.LayoutPrototype ? SurfaceBuildScope.CaveOnly : _pending.SurfaceScope,
                SurfaceTerrainBuildPasses = SurfaceTerrainCenteredAuthor.DefaultPassCount,
            };
            CaveBuildAaaProductionBootstrap.MergeRecipeIntoRequest(productionRecipe, _request, _roll);
            CaveBuildAutomatedFullWorldBootstrap.ApplyToRequest(_request);
            if (LavaTubeCaveBuilder.OverrideContentTier.HasValue)
            {
                _request.ContentTier = LavaTubeCaveBuilder.OverrideContentTier.Value;
                LavaTubeCaveBuilder.OverrideContentTier = null;
            }
            else if (_request.SurfaceScope == SurfaceBuildScope.SurfaceOnly)
                _request.ContentTier = WorldBuildContentTier.Light;

            if (_request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                _request.DemSupersampleTargetDim <= 0)
            {
                _request.DemSupersampleTargetDim =
                    CaveBuildCursorSettings.LoadOrCreate().demSupersampleTargetDim;
            }

            if (_request.SurfaceScope == SurfaceBuildScope.FullWorld)
                CaveBuildSessionConfig.BindFullWorldRequest(_request);
            else
                _request.EnsureFullWorldSurfaceContract();
            CaveBuildAaaSessionPolicy.BindActiveRequest(_request);
            CaveBuildStepCounter.ConfigureForRequest(_request);
            if (!CaveBuildSessionConfig.IsSessionRequest(_request))
                CaveBuildConceptSession.LockForBuild(_request.ConceptLayoutIndex, _request.Seed, syncHubDropdown: false);
            CaveBuildPipelinePhaseTracker.RefreshProvisionalExtended(_request);
            CaveBuildEnhancementRunner.BeginSession(_request);
            CaveBuildEnhancementRunner.RunHook(CaveBuildEnhancementCatalog.Hook.OnRequestPrepared);
            SurfaceDemGeoreferenceAuthor.SetSupersampleTargetDim(_request.DemSupersampleTargetDim);

            if (_pending.LayoutPrototype || _request.SurfaceScope == SurfaceBuildScope.CaveOnly)
                _step = StartupStep.PreBuildGate;
            else if (_request.SurfaceScope == SurfaceBuildScope.FullWorld)
                _step = StartupStep.SurfacePipeline;
            else
                _step = StartupStep.PreBuildGate;

            var nextLabel = _pending.LayoutPrototype || _request.SurfaceScope == SurfaceBuildScope.CaveOnly
                ? "pre-build gate → cave geometry"
                : _request.SurfaceScope == SurfaceBuildScope.FullWorld
                    ? "surface/terrain from Ground → pre-build → cave geometry last"
                    : "pre-build gate → cave / surface per scope";
            CaveBuildEditorLog.LogCave(
                $"[Startup] Layout roll done (seed={_roll.Seed}) — next: {nextLabel}.",
                forceUnityConsole: true);

            ScheduleStep();
        }

        static void RunSurfacePipeline()
        {
            SetProgress(0.32f, "[Startup] Florida LiDAR + terrain from Ground anchor (before cave)…");

            if (!CaveEditorUndo.IsBulkBuild)
                CaveEditorUndo.BeginBulkBuild();

            if (_pending.LayoutPrototype || _request.SurfaceScope == SurfaceBuildScope.CaveOnly)
            {
                FinishStartupAfterSurface(null);
                return;
            }

            CaveBuildEditorLog.LogSurface(
                "[Startup] Building surface world + terrain phases first — cave geometry runs after grading.",
                forceUnityConsole: true);

            CaveBuildSurfacePipeline.QueueSurfaceWorldAndTerrainPhases(
                _ground,
                _request,
                FinishStartupAfterSurface);
        }

        static void FinishStartupAfterSurface(SurfaceWorldBuildReport surfaceReport)
        {
            if (!_active)
                return;

            _surfaceReport = surfaceReport;

            if (surfaceReport != null && !surfaceReport.Success)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    "Surface/terrain pipeline had issues — cave geometry will still be attempted after pre-build.");
            }

            var surfaceOk = surfaceReport == null || surfaceReport.Success;
            CaveBuildSurfaceCompletionGate.MarkSurfaceBuildFinished(_request, surfaceOk);
            if (surfaceOk && _request != null)
                CaveBuildEnhancementRunner.RunHook(CaveBuildEnhancementCatalog.Hook.AfterTerrainPhases);

            if (CaveBuildSurfacePipeline.ShouldSkipCaveGeometry(_request))
            {
                var doneMsg = surfaceReport?.Message ?? "Surface build completed.";
                var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                var quality = CaveBuildQualitySystem.LastGradedReport ??
                              CaveBuildRungPromptExporter.TryLoadQualityReport();
                var stubReport = new LavaTubeCaveBuildReport { Message = doneMsg, QualityAcceptable = surfaceReport?.Success ?? true };
                if (CaveBuildPostBuildFinalizeGate.TryOfferPlayModeRecording(
                        sceneName,
                        stubReport,
                        _roll,
                        quality,
                        skipDialogs: EnvironmentKitHubWindow.IsOpen))
                {
                    Complete(false);
                    return;
                }

                if (EnvironmentKitHubWindow.IsOpen)
                    EnvironmentKitHubWindow.NotifyBuildCompleted("Surface World — Finished", doneMsg);
                else
                    Debug.Log("[CaveBuild] Surface-only completion (non-modal): " + doneMsg);

                EnvironmentSceneUtility.MarkSceneDirty();
                Complete(false);
                return;
            }

            if (_request.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                CaveBuildEditorLog.LogCave(
                    "[Startup] Surface/terrain complete — pre-build gate, then cave geometry (mouth aligns to terrain).",
                    forceUnityConsole: true);
                _step = StartupStep.PreBuildGate;
                ScheduleStep();
                return;
            }

            Complete(false);
        }

        /// <summary>Next-frame quick fixes then pre-build gate — avoids blocking tsx/Refresh on the queue thread.</summary>
        public static void SchedulePreBuildReloopWork(string status)
        {
            if (!LavaTubeCaveBuilder.IsBuildInProgress && !_active)
            {
                Debug.LogWarning(
                    "[CaveBuild] Pre-build reloop not scheduled — no active build session.");
                return;
            }

            if (!_active)
            {
                _active = true;
                _step = StartupStep.PreBuildGate;
                CaveBuildEditorLog.LogCaveWarning(
                    "[Startup] Re-armed startup coordinator for pre-build reloop.");
            }

            _step = StartupStep.PreBuildGate;
            SetProgress(0.38f, $"[Startup] Pre-build reloop — {status}");
            CaveBuildActionPacing.SchedulePriorityFirstStep(
                () =>
                {
                    try
                    {
                        CaveBuildPreBuildReloop.RunDeferredReloopFixes();
                    }
                    finally
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                    }
                },
                CaveBuildPipelineDomains.QueueLabel("startup — pre-build reloop fixes"));
        }

        /// <summary>After deferred reloop fixes — re-queues the gate on the light pacing queue.</summary>
        public static void InvokePreBuildGateFromReloop()
        {
            if (!_active && LavaTubeCaveBuilder.IsBuildInProgress)
            {
                _active = true;
                _step = StartupStep.PreBuildGate;
            }

            CaveBuildActionPacing.SchedulePriorityFirstStep(
                RunPreBuildGate,
                CaveBuildPipelineDomains.QueueLabel("startup — pre-build from reloop"));
        }

        static void RunPreBuildGate()
        {
            if (!_active && LavaTubeCaveBuilder.IsBuildInProgress)
            {
                _active = true;
                _step = StartupStep.PreBuildGate;
            }

            if (!_active)
                return;

            if (EditorApplication.isCompiling)
            {
                _preBuildCompileWaitTicks++;
                if (_preBuildCompileWaitTicks > 120)
                {
                    CaveBuildEditorLog.LogCaveWarning(
                        "[Startup] Compile wait exceeded — running pre-build gate anyway (fix CS errors in Console).");
                    _preBuildCompileWaitTicks = 0;
                }
                else
                {
                    CaveBuildEditorLog.LogCave(
                        "[Startup] Pre-build gate waiting for script compile to finish…",
                        forceUnityConsole: true);
                    EditorApplication.delayCall += () =>
                    {
                        RunPreBuildGate();
                        EditorApplication.QueuePlayerLoopUpdate();
                    };
                    return;
                }
            }
            else
            {
                _preBuildCompileWaitTicks = 0;
            }

            SetProgress(0.38f, "[Startup] Pre-build readiness gate…");
            CaveBuildRunStatusPublisher.ClearSubOperation();
            _preBuildGateWatchdogAt = EditorApplication.timeSinceStartup;
            EditorApplication.QueuePlayerLoopUpdate();
            CaveBuildActionPacing.SchedulePriorityFirstStep(
                RunPreBuildGateEvaluate,
                CaveBuildPipelineDomains.QueueLabel("startup — pre-build ladder"));
            SchedulePreBuildGateWatchdog();
        }

        static void SchedulePreBuildGateWatchdog()
        {
            EditorApplication.delayCall += () =>
            {
                if (!_active || _step != StartupStep.PreBuildGate)
                    return;

                if (EditorApplication.timeSinceStartup - _preBuildGateWatchdogAt < 90.0)
                {
                    SchedulePreBuildGateWatchdog();
                    return;
                }

                if (!CaveBuildSurfaceCompletionGate.IsTerrainGradingComplete)
                    return;

                CaveBuildEditorLog.LogCaveWarning(
                    "[Startup] Pre-build gate watchdog — forcing evaluate (queue may have been backed up).");
                CaveBuildActionPacing.SchedulePriorityFirstStep(
                    RunPreBuildGateEvaluate,
                    CaveBuildPipelineDomains.QueueLabel("startup — pre-build watchdog"));
            };
        }

        static void RunPreBuildGateEvaluate()
        {
            if (!_active)
                return;

            if (EditorApplication.isCompiling)
            {
                RunPreBuildGate();
                return;
            }

            CaveBuildSurfaceCompletionGate.ReleaseStuckHandoffForStartup(_request, _ground, true);

            if (!CaveBuildUnifiedFlow.TryRunPreBuildPhase(
                    _ground,
                    _request,
                    _roll.Seed,
                    _pending.LayoutPrototype,
                    _pending.SkipPreBuildGate,
                    _pending.SkipDialogs,
                    _pending.HideLegacyBlockout,
                    out _cachedPreBuildReport,
                    _roll))
            {
                if (CaveBuildCursorAgentBridge.IsPreBuildWorkflowActive)
                {
                    Complete(true);
                    return;
                }

                if (CaveBuildPendingGeometryBuild.HasPending)
                {
                    if (CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(_request, _ground))
                    {
                        CaveBuildEditorLog.LogCave(
                            "[Startup] Pre-build deferred cave cleared — terrain handoff ready, continuing.",
                            forceUnityConsole: true);
                        CaveBuildPendingGeometryBuild.Clear();
                        AdvancePastPreBuildGate();
                        return;
                    }

                    Complete(true);
                    return;
                }

                CaveBuildActionPacing.SchedulePriorityFirstStep(
                    RunPreBuildGateReloopPlan,
                    CaveBuildPipelineDomains.QueueLabel("startup — pre-build reloop plan"));
                return;
            }

            AdvancePastPreBuildGate();
        }

        static void RunPreBuildGateReloopPlan()
        {
            if (!_active)
                return;

            var preBuildReport = _cachedPreBuildReport;
            if (!_pending.SkipPreBuildGate && !_pending.LayoutPrototype && preBuildReport != null)
            {
                var reloop = CaveBuildPreBuildReloop.TryPlanStartupFailure(
                    preBuildReport,
                    _ground,
                    _request,
                    _roll.Seed,
                    _pending.LayoutPrototype,
                    _roll,
                    _pending.SkipDialogs,
                    _pending.HideLegacyBlockout,
                    out var reloopStatus,
                    out _);

                switch (reloop)
                {
                    case CaveBuildPreBuildReloop.PreBuildReloopResult.PassedAfterLocalFix:
                    case CaveBuildPreBuildReloop.PreBuildReloopResult.ProductionContinue:
                        SetProgress(0.42f, $"[Startup] {reloopStatus}");
                        CaveBuildEditorLog.LogCave(
                            $"[Startup] {reloopStatus}",
                            forceUnityConsole: true);
                        AdvancePastPreBuildGate();
                        return;
                    case CaveBuildPreBuildReloop.PreBuildReloopResult.RetryScheduled:
                        CaveBuildEditorLog.LogCave(
                            "[Startup] Pre-build reloop — quick fixes on next editor tick, then gate retry.",
                            forceUnityConsole: true);
                        EditorApplication.QueuePlayerLoopUpdate();
                        return;
                    case CaveBuildPreBuildReloop.PreBuildReloopResult.CursorDeferred:
                        Complete(true);
                        return;
                }
            }

            if (!_pending.LayoutPrototype && preBuildReport != null && !preBuildReport.BuildAcceptable)
            {
                var blockDetail = CaveBuildPreBuildReloop.ShouldReloop()
                    ? "reloop exhausted or could not schedule"
                    : "preBuildReloopUntilPass off";
                CaveBuildCompletionSummary.ShowBlocked(
                    $"Pre-build BLOCKED ({preBuildReport.LetterGrade} {preBuildReport.OverallScore}/100) — {blockDetail}.",
                    CaveBuildPreBuildLadder.ReportPath);
            }

            Complete(false);
        }

        static void AdvancePastPreBuildGate()
        {
            if (CaveBuildSessionPreset.HasUsableAiProvider)
            {
                CaveBuildHelperScriptOrchestrator.Queue(
                    CaveBuildHelperScriptOrchestrator.Moment.PreBuildGateComplete,
                    CaveBuildHelperScriptOrchestrator.MakeContext(_request));
            }
            else
            {
                CaveBuildEditorLog.LogCave(
                    "[Startup] No API — skipping pre-build prompt exports, continuing to cave pipeline.",
                    forceUnityConsole: true);
            }

            if (CaveBuildAaaProductionBootstrap.IsFullProductionBuild(_pending.SurfaceScope, _pending.LayoutPrototype))
                CaveBuildAaaProductionBootstrap.OnPreBuildGatePassed(_roll.Seed);

            if (_request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                !CaveBuildSessionConfig.SkipTerrainHelperScripts(_request))
            {
                var layoutAudit = CaveBuildWorldLayoutAudit.Run(_ground, _request);
                var blocksStartup = layoutAudit.blocksSurfaceContinue ||
                                    (layoutAudit.blocksCaveQueue && _request.UseTrue3DCaveSystem);
                if (blocksStartup &&
                    System.Environment.GetEnvironmentVariable("CAVE_LAYOUT_PLAN_FORCE") != "1")
                {
                    var blockReason = layoutAudit.blocksSurfaceContinue
                        ? "surface continue (play-disk seams)"
                        : "cave queue";
                    CaveBuildCompletionSummary.ShowBlocked(
                        $"World layout audit blocks {blockReason} — fix seams/props/overlap or run Surface Only first. " +
                        $"Suggested fix: {layoutAudit.suggestedFix}. See {CaveBuildWorldLayoutAudit.ReportRel}. " +
                        "Override: set CAVE_LAYOUT_PLAN_FORCE=1 for headless farm only.",
                        CaveBuildWorldLayoutAudit.ReportRel);
                    Complete(false);
                    return;
                }
            }

            _step = StartupStep.CaveGeometry;
            ScheduleStep();
        }

        static void RunCaveGeometry()
        {
            CaveBuildSurfaceTerrainLock.LockForUndergroundCave("startup cave geometry");
            SetProgress(0.58f, "[Startup] Queuing cave geometry (after surface terrain)…");

            if (CaveBuildSurfaceCompletionGate.MustFinishSurfaceBeforeCave(_request) &&
                !CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(_request, _ground))
            {
                CaveBuildSurfaceCompletionGate.LogHandoffBlocker(_request, _ground, "startup cave geometry");
                CaveBuildActionPacing.QueueWhenIdle(
                    () =>
                    {
                        if (!_active)
                            return;
                        RunCaveGeometry();
                    },
                    CaveBuildPipelineDomains.QueueLabel("startup — cave after surface idle"));
                return;
            }

            CaveBuildEditorLog.LogCave(
                "[Startup] Cave geometry last — aligns mouth to finished terrain and descends underground.",
                forceUnityConsole: true);

            var defer = LavaTubeCaveBuilder.StartupQueueCaveGeometry(
                _sceneName,
                _ground,
                _request,
                _pending.HideLegacyBlockout,
                _pending.SkipDialogs,
                _pending.LayoutPrototype,
                _roll);

            if (defer)
            {
                SetProgress(
                    0.6f,
                    "[Startup] Cave pipeline queued — step 1/122 in Console (validate + research)…");
                CaveBuildEditorLog.LogCave(
                    "[Startup] Phased cave pipeline queued — if nothing advances in 30s, use Cave Build → Emergency Unfreeze.",
                    forceUnityConsole: true);
            }
            else
            {
                CaveBuildEditorLog.LogCave(
                    "[Startup] Cave pipeline running synchronously on main thread…",
                    forceUnityConsole: true);
            }

            if (_pending.LayoutPrototype || _request.SurfaceScope == SurfaceBuildScope.CaveOnly)
            {
                Complete(defer);
                return;
            }

            if (_request.SurfaceScope == SurfaceBuildScope.SurfaceOnly)
            {
                _step = StartupStep.SurfacePipeline;
                ScheduleStep();
                return;
            }

            Complete(defer);
        }

        static void Complete(bool deferRelease)
        {
            EditorUtility.ClearProgressBar();
            _active = false;
            _pending.OnDeferRelease?.Invoke(deferRelease);
            if (!deferRelease)
            {
                EnvironmentKitHardwareBudget.EndEditorSession();
                if (!LavaTubeCaveBuildPipeline.IsPhasedBuildActive &&
                    !LavaTubeCaveBuilder.IsBuildInProgress)
                    CaveBuildRunStatusPublisher.EndSession();
            }
        }

        static void SetProgress(float t, string detail)
        {
            if (CaveBuildProgressBars.AllowModalProgressBar)
                CaveBuildProgressUI.ShowThrottled("Environment Kit", detail, t);
            CaveBuildPipelinePhaseTracker.OnStartupDetail(detail);
            CaveBuildRunStatusPublisher.PulseSubOperation("startup", detail);
            CaveBuildPipelineLog.Info(detail, "Startup");
        }

        public static void QueueResumeAtResearch(
            SceneGroundInfo ground,
            CaveLayoutRoll roll,
            int researchSubStep,
            System.Action<bool> onDeferRelease)
        {
            if (_active)
                return;

            ArmResumeSession(ground, roll, onDeferRelease);
            _step = StartupStep.SurfacePipeline;
            CaveBuildRunStatusPublisher.BeginSession(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                roll.Seed,
                additiveSurface: true);
            CaveBuildRunStatusPublisher.SetPhase("startup", "Resuming pre-placement research after playtest");

            CaveBuildPrePlacementResearch.QueueResumeFromSubStep(
                ground,
                _request,
                additiveSurface: true,
                researchSubStep,
                (ok, researchMsg) =>
                {
                    if (!ok)
                    {
                        CaveBuildEditorLog.LogSurfaceWarning("[Startup] Research resume failed: " + researchMsg);
                        Complete(false);
                        return;
                    }

                    if (!string.IsNullOrEmpty(researchMsg))
                        Debug.Log("[CaveBuild] " + researchMsg);

                    CaveBuildSurfacePipeline.ResumeSurfaceBuildAfterResearch(
                        ground,
                        _request,
                        FinishStartupAfterSurface);
                });
        }

        public static void QueueResumeAtSurfacePipeline(
            SceneGroundInfo ground,
            CaveLayoutRoll roll,
            System.Action<bool> onDeferRelease)
        {
            if (_active)
                return;

            if (CaveBuildPrePlacementResearch.IsGatePassedForSeed(roll.Seed))
            {
                ArmResumeSession(ground, roll, onDeferRelease);
                _step = StartupStep.SurfacePipeline;
                CaveBuildRunStatusPublisher.BeginSession(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    roll.Seed,
                    additiveSurface: true);
                CaveBuildRunStatusPublisher.SetPhase("startup", "Resuming surface pipeline after playtest");
                CaveBuildSurfacePipeline.ResumeSurfaceBuildAfterResearch(
                    ground,
                    _request,
                    FinishStartupAfterSurface);
                return;
            }

            var subStep = CaveBuildPrePlacementResearch.LastActiveSubStep;
            if (subStep < 0)
                subStep = 0;
            QueueResumeAtResearch(ground, roll, subStep, onDeferRelease);
        }

        static void ArmResumeSession(
            SceneGroundInfo ground,
            CaveLayoutRoll roll,
            System.Action<bool> onDeferRelease)
        {
            _pending = new PendingBuild
            {
                OpenMainSceneFirst = false,
                HideLegacyBlockout = true,
                SkipDialogs = true,
                LayoutPrototype = false,
                SkipPreBuildGate = false,
                SurfaceScope = SurfaceBuildScope.FullWorld,
                OnDeferRelease = onDeferRelease,
            };
            _ground = ground;
            _roll = roll;
            _active = true;
            _surfaceReport = null;
            _sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            _request = new WorldGenerationRequest
            {
                Biome = BiomeId.Cave,
                CaveMode = CaveGenerationMode.FullSystem,
                UseLayoutPrototype = false,
                UseSplineMesh = true,
                UseTrue3DCaveSystem = true,
                UseBlockTunnel = true,
                UseTerrainCarve = true,
                AllowCreateTerrain = false,
                IncludeCaveWater = false,
                SurfaceScope = SurfaceBuildScope.FullWorld,
                SurfaceTerrainBuildPasses = SurfaceTerrainCenteredAuthor.DefaultPassCount,
            };

            var productionRecipe = CaveBuildAaaProductionBootstrap.PrepareFullProductionBuild(
                roll,
                SurfaceBuildScope.FullWorld,
                layoutPrototype: false);
            CaveBuildAaaProductionBootstrap.MergeRecipeIntoRequest(productionRecipe, _request, roll);
            CaveBuildAutomatedFullWorldBootstrap.ApplyToRequest(_request);
            CaveBuildSessionConfig.BindFullWorldRequest(_request);
            if (!CaveBuildSessionConfig.IsSessionRequest(_request))
                CaveBuildConceptSession.ApplyAndBind(_request, syncHubDropdown: false);
            CaveBuildEnhancementRunner.BeginSession(_request);
            SurfaceDemGeoreferenceAuthor.SetSupersampleTargetDim(_request.DemSupersampleTargetDim);
            CaveBuildPipelinePhaseTracker.OnBuildSessionStart(SurfaceBuildScope.FullWorld, _request);
        }
    }
}
#endif
