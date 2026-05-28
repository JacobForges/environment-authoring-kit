#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
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

        public static bool IsActive => _active;

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

            CaveBuildRunStatusPublisher.SetPhase("startup", "Queued build startup — preparing scene…");
            CaveBuildEditorLog.LogCave(
                "Build startup queued — watch progress bar & Pipeline Console.",
                forceUnityConsole: true);

            CaveBuildPipelineConsoleWindow.Open();
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
                "[Startup] Tooling check done — continuing scene prep (watch progress bar + Pipeline Console).",
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
                CaveBuildActionPacing.SchedulePipelineFirstStep(RunPreBuildGate, queueLabel);
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
                LavaTubeCaveBuilder.StartupTryOpenMainScene();

            if (!LavaTubeCaveBuilder.StartupEnsurePresets())
            {
                Complete(false);
                return;
            }

            LavaTubeCaveBuilder.StartupClearInvalidGround();
            _ground = SceneGroundResolver.Resolve(LavaTubeCaveBuilder.StartupLoadUserGround());
            if (!_ground.HasAnchor)
            {
                if (!_pending.SkipDialogs)
                    CaveBuildCompletionSummary.ShowBlocked(
                        "No ground found. Tag walkable floor as 'Ground' or assign in Environment Kit.");
                Complete(false);
                return;
            }

            _sceneName = SceneManager.GetActiveScene().name;
            if (!_pending.SkipDialogs)
                CaveBuildPortalSettings.PromptIfNeeded(showDialog: true);

            if (!CaveBuildWorkflowGuardrails.TryArtifactPreflight(out var preflightMsg))
            {
                CaveBuildEditorLog.LogCaveWarning("[Startup] " + preflightMsg);
                if (!_pending.SkipDialogs)
                    CaveBuildCompletionSummary.ShowBlocked(preflightMsg);
                Complete(false);
                return;
            }

            if (preflightMsg.StartsWith("artifact_preflight advisory", System.StringComparison.Ordinal))
                CaveBuildEditorLog.LogCaveWarning("[Startup] " + preflightMsg);
            else
                CaveBuildEditorLog.LogCave("[Startup] " + preflightMsg, forceUnityConsole: true);

            CaveBuildPipelineScope.BeginFullPipeline();
            CaveBuildFullWorldSurfaceDeferral.ResetForNewBuildSession();
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
                SurfaceDirectionCount = 1,
                SurfaceTerrainBuildPasses = SurfaceTerrainCenteredAuthor.DefaultPassCount,
            };
            CaveBuildAaaProductionBootstrap.MergeRecipeIntoRequest(productionRecipe, _request, _roll);
            CaveBuildAutomatedFullWorldBootstrap.ApplyToRequest(_request);
            if (_request.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                _request.RunEnhancementPhases = true;
                if (_request.DemSupersampleTargetDim <= 0)
                    _request.DemSupersampleTargetDim =
                        CaveBuildCursorSettings.LoadOrCreate().demSupersampleTargetDim;
            }

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
                if (!_pending.SkipDialogs)
                {
                    EditorUtility.DisplayDialog(
                        "Surface World — Finished",
                        surfaceReport?.Message ?? "Surface build completed.",
                        "OK");
                }

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
            CaveBuildActionPacing.PreparePipelineChainKickoff();
            EditorApplication.delayCall += () =>
            {
                try
                {
                    CaveBuildPreBuildReloop.RunDeferredReloopFixes();
                }
                finally
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                }
            };
        }

        /// <summary>After deferred reloop fixes — re-queues the gate on the light pacing queue.</summary>
        public static void InvokePreBuildGateFromReloop()
        {
            if (!_active && LavaTubeCaveBuilder.IsBuildInProgress)
            {
                _active = true;
                _step = StartupStep.PreBuildGate;
            }

            CaveBuildActionPacing.PreparePipelineChainKickoff();
            EditorApplication.delayCall += () =>
            {
                try
                {
                    RunPreBuildGate();
                }
                finally
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                }
            };
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
            EditorApplication.QueuePlayerLoopUpdate();
            CaveBuildActionPacing.SchedulePipelineFirstStep(
                RunPreBuildGateEvaluate,
                CaveBuildPipelineDomains.QueueLabel("startup — pre-build ladder"));
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
                if (CaveBuildPendingGeometryBuild.HasPending ||
                    CaveBuildCursorAgentBridge.IsPreBuildWorkflowActive)
                {
                    Complete(true);
                    return;
                }

                CaveBuildActionPacing.SchedulePipelineFirstStep(
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

            _step = StartupStep.CaveGeometry;
            ScheduleStep();
        }

        static void RunCaveGeometry()
        {
            SetProgress(0.58f, "[Startup] Queuing cave geometry (after surface terrain)…");
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
                    "[Startup] Cave pipeline queued — step 1/120 in Console (validate + research)…");
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
            }
        }

        static void SetProgress(float t, string detail)
        {
            CaveBuildProgressUI.ShowThrottled("Environment Kit", detail, t);
            CaveBuildRunStatusPublisher.SetPhase("startup", detail);
            CaveBuildPipelineLog.Info(detail, "Startup");
        }
    }
}
#endif
