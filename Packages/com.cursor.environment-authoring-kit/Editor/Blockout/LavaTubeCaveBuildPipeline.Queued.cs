using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static partial class LavaTubeCaveBuildPipeline
    {
        const int QueuedStepValidate = CaveBuildQueuedPipelineSchedule.Validate;

        const int ValidateSubBeginSession = 0;
        const int ValidateSubResearchFirst = 1;
        const int ValidateSubCatalogValidate = 10;
        const int ValidateSubRequestFlags = 11;
        const int ValidateSubAdventureInit = 12;
        const int ValidateSubCount = 13;
        const int ValidateSubCaveOnlyCount = 4;
        const int QueuedGeoFirst = CaveBuildQueuedPipelineSchedule.GeoFirst;
        const int QueuedGeoStepCount = CaveBuildQueuedPipelineSchedule.GeoCount;
        const int QueuedStepPlayabilityFirst = CaveBuildQueuedPipelineSchedule.PlayabilityFirst;
        const int QueuedStepValidationFirst = CaveBuildQueuedPipelineSchedule.ValidationFirst;
        const int QueuedStepValidationCount = CaveBuildQueuedPipelineSchedule.ValidationCount;
        const int QueuedStepGroundPolishFirst = CaveBuildQueuedPipelineSchedule.GroundPolishFirst;
        const int QueuedStepGroundPolishCount = CaveBuildQueuedPipelineSchedule.GroundPolishCount;
        const int QueuedStepWorldFirst = CaveBuildQueuedPipelineSchedule.WorldFirst;
        const int QueuedWorldStageCount = CaveBuildQueuedPipelineSchedule.WorldCount;
        const int QueuedStepMeatStart = CaveBuildQueuedPipelineSchedule.Meat;
        const int QueuedStepPostMeatFirst = CaveBuildQueuedPipelineSchedule.PostMeatFirst;
        const int QueuedPostMeatStepCount = CaveBuildQueuedPipelineSchedule.PostMeatCount;
        const int QueuedStepResearchFirst = CaveBuildQueuedPipelineSchedule.ResearchFirst;
        const int QueuedResearchStepCount = CaveBuildQueuedPipelineSchedule.ResearchCount;
        const int QueuedStepFinalizePolishFirst = CaveBuildQueuedPipelineSchedule.FinalizePolishFirst;
        const int QueuedStepFinalizePolishCount = CaveBuildQueuedPipelineSchedule.FinalizePolishCount;
        const int QueuedStepAaaManifest = CaveBuildQueuedPipelineSchedule.AaaManifest;
        const int QueuedStepFinishReport = CaveBuildQueuedPipelineSchedule.FinishReport;
        const int QueuedStepTotal = CaveBuildQueuedPipelineSchedule.Total;

        public const int ManifestQueuedStepIndex = CaveBuildQueuedPipelineSchedule.ManifestQueuedStepIndex;

        public static void ResetManifestFinalizeResumeArmed() => _manifestFinalizeResumeArmed = false;

        static string[] WorldStageLabels => CaveBuildQueuedPipelineSchedule.WorldStageLabels;

        static void StartQueuedPipeline()
        {
            if (CaveBuildPipelineScope.CaveOnlyContinuation)
            {
                CaveBuildPipelineDomains.LogCave(
                    $"Queued pipeline ({QueuedStepTotal} macro steps) — CaveOnly align continuation; " +
                    $"existing surface frozen; validate={ValidateSubCaveOnlyCount} paced actions (no re-research).",
                    forceUnityConsole: true);
            }
            else
            {
                var scope = _queued?.Request?.SurfaceScope ?? SurfaceBuildScope.FullWorld;
                CaveBuildPipelineDomains.LogCave(
                    $"Queued pipeline ({QueuedStepTotal} macro steps) — {scope}; " +
                    $"validate={ValidateSubCount} paced actions (research 9 steps: unified manifest, ONE research agent prompt, fast active prompt); " +
                    "geo 14, playability 18, validation 6, ground polish 10, world 15, meat, post-meat 24, research 12, finalize polish 15, manifest, finalize. " +
                    "FullWorld: surface/terrain should finish in startup before cave geo.",
                    forceUnityConsole: true);
                if (scope == SurfaceBuildScope.FullWorld)
                {
                    CaveBuildPipelineDomains.LogSurface(
                        "FullWorld: surface/terrain should already be complete (Ground up) — cave geometry aligns to terrain.");
                }
                else
                {
                    CaveBuildPipelineDomains.LogSurface(
                        "Surface terrain runs in pre-build / SurfaceWorldGenerator — not in cave macro steps.");
                }
            }

            EditorUtility.ClearProgressBar();
            CaveBuildEditorLog.LogCave(
                "Validate kickoff — EditorApplication.update tick chain (not delayCall, not action queue).",
                forceUnityConsole: true);

            CaveBuildActionPacing.PreparePipelineChainKickoff();
            CaveBuildValidateTickRunner.Enqueue(BeginQueuedPipelineOnNextFrame);
        }

        static void BeginQueuedPipelineOnNextFrame()
        {
            if (_queued == null)
                return;

            if (!CaveBuildPipelineScope.CaveOnlyContinuation)
                CaveBuildPhaseContractRegistry.ExportContractsCatalog();
            CaveBuildWorkflowCoordinator.BeginSession();

            if (!CaveBuildPipelineScope.CaveOnlyContinuation &&
                _queued?.Request?.SurfaceScope == SurfaceBuildScope.FullWorld &&
                !CaveBuildPhaseContractRegistry.HasPlayableCaveLayoutInScene())
            {
                CaveBuildPhaseContractRegistry.InvalidateCaveGeometryLadderRungs();
                CaveBuildEditorLog.LogCaveWarning(
                    "[CaveBuild] FullWorld: no substantive cave in scene — invalidated cave_layout ladder; " +
                    "queued geo steps 1–14 will run after validate (not ramp-only mouth patch).");
            }

            CaveBuildEditorLog.LogCave(
                "[Cave] Validate kickoff done — first validate tick next editor update.",
                forceUnityConsole: true);

            if (TryFastForwardValidateWhenResearchReady())
                return;

            _queued.ValidateSubStep = ValidateSubBeginSession;
            _queued.ValidateBeginUiPublished = false;
            CaveBuildValidateTickRunner.Enqueue(RunValidateSubStepDirect);
        }

        /// <summary>
        /// When pre-placement research already passed, skip research substeps (still run catalog/flags/adventure).
        /// </summary>
        static bool TryFastForwardValidateWhenResearchReady()
        {
            var ctx = _queued;
            if (ctx == null || ctx.CaveOnlyContinuation)
                return false;

            var gatePassed = CaveBuildPrePlacementResearch.IsGatePassedForSeed(ctx.Request.Seed);
            var promptsOnDisk = CaveBuildPromptExportSession.HasValidateResearchPromptArtifactsOnDisk();
            if (!gatePassed && !promptsOnDisk)
                return false;

            if (!gatePassed && promptsOnDisk)
            {
                CaveBuildPrePlacementResearch.WriteGateForValidateFastForward(
                    ctx.Request.Seed,
                    ctx.Request.SurfaceScope == SurfaceBuildScope.FullWorld);
            }

            CaveBuildEditorLog.LogCave(
                gatePassed
                    ? "Validate fast-forward — research gate already passed; skipping research substeps."
                    : "Validate fast-forward — prompt MD/JSON on disk; skipping blocking research tsx.",
                forceUnityConsole: true);
            ctx.ValidateSubStep = ValidateSubCatalogValidate;
            ctx.ValidateBeginUiPublished = true;
            CaveBuildValidateTickRunner.Enqueue(RunValidateSubStepDirect);
            return true;
        }

        /// <summary>
        /// One validate substep per <see cref="EditorApplication.update"/> — survives stuck action queue / delayCall.
        /// </summary>
        sealed class CaveBuildValidateTickRunner
        {
            static readonly System.Collections.Generic.Queue<System.Action> Pending = new();
            static bool _hooked;

            public static void Enqueue(System.Action work)
            {
                if (work == null)
                    return;
                Pending.Enqueue(work);
                if (_hooked)
                    return;
                _hooked = true;
                EditorApplication.update += OnEditorUpdate;
            }

            public static void EmergencyClear()
            {
                Pending.Clear();
                if (_hooked)
                    EditorApplication.update -= OnEditorUpdate;
                _hooked = false;
            }

            static void OnEditorUpdate()
            {
                if (Pending.Count == 0)
                {
                    EditorApplication.update -= OnEditorUpdate;
                    _hooked = false;
                    return;
                }

                var work = Pending.Dequeue();
                EditorUtility.ClearProgressBar();
                try
                {
                    work();
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                    Pending.Clear();
                    EditorApplication.update -= OnEditorUpdate;
                    _hooked = false;
                    FinishQueued(new LavaTubeCaveBuildReport
                    {
                        Message = "Validate tick failed: " + ex.Message,
                    });
                }
                finally
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                }
            }
        }

        static void ScheduleValidateOnNextEditorFrame() =>
            CaveBuildValidateTickRunner.Enqueue(RunValidateSubStepDirect);

        /// <summary>Resume queued step after non-blocking research-gate prompt tsx.</summary>
        internal static void CompleteQueuedResearchPromptTsx(bool ok, string msg, int resumeQueuedStep)
        {
            var ctx = _queued;
            if (ctx == null)
                return;

            ctx.QueuedAwaitingResearchPrompt = false;
            ctx.QueuedAwaitingResearchPromptStep = -1;

            if (!ok)
            {
                FinishQueued(new LavaTubeCaveBuildReport { Message = msg ?? "Research prompt tsx failed." });
                return;
            }

            if (!string.IsNullOrEmpty(msg))
                CaveBuildEditorLog.LogCave(msg, forceUnityConsole: true);

            ScheduleQueuedStep(resumeQueuedStep, GetStepCooldownWeight(resumeQueuedStep));
        }

        /// <summary>Resume validate after non-blocking prompt tsx (research sub-steps 5–8).</summary>
        internal static void CompleteValidateResearchTsx(
            bool ok,
            string msg,
            int completedResearchSub)
        {
            var ctx = _queued;
            if (ctx == null)
                return;

            ctx.ValidateAwaitingTsx = false;
            if (!string.IsNullOrEmpty(msg))
            {
                ctx.ValidateResearchAccumulated = string.IsNullOrEmpty(ctx.ValidateResearchAccumulated)
                    ? msg
                    : ctx.ValidateResearchAccumulated + " | " + msg;
            }

            if (!ok)
            {
                FinishQueued(new LavaTubeCaveBuildReport { Message = msg ?? "Research tsx failed." });
                return;
            }

            if (completedResearchSub == CaveBuildPrePlacementResearch.QueuedResearchActivePrompt)
            {
                CaveBuildEnhancementRunner.RunHook(CaveBuildEnhancementCatalog.Hook.AfterResearch);
                CaveBuildPhaseContractRegistry.MarkRungComplete(
                    CaveBuildPhaseContractRegistry.RungResearchSeed,
                    ctx.Request.Seed);
                CaveBuildPrePlacementResearch.WriteGateAfterActivePrompt(
                    ctx.Request,
                    ctx.Request.SurfaceScope == SurfaceBuildScope.FullWorld,
                    ref ctx.ValidateResearchAccumulated,
                    out var _);
                ScheduleValidateContinue(
                    ctx,
                    ValidateSubCatalogValidate,
                    "prefab catalog",
                    CaveBuildActionPacing.ActionWeight.Light);
                return;
            }

            var nextResearch = completedResearchSub + 1;
            if (nextResearch < CaveBuildPrePlacementResearch.QueuedResearchStepCount)
            {
                ScheduleValidateContinue(
                    ctx,
                    ValidateSubResearchFirst + nextResearch,
                    CaveBuildPrePlacementResearch.QueuedResearchStepLabels[nextResearch],
                    QueuedResearchScheduleWeight(nextResearch));
                return;
            }

            ScheduleValidateContinue(
                ctx,
                ValidateSubCatalogValidate,
                "prefab catalog",
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static void RunValidateSubStepDirect()
        {
            var ctx = _queued;
            if (ctx == null)
                return;

            if (ctx.ValidateAwaitingTsx)
                return;

            var label = QueuedValidateLiveLabel(ctx, ctx.ValidateSubStep);
            CaveBuildRunStatusPublisher.SetQueuedStep(QueuedStepValidate, QueuedStepTotal, label);
            ProgressQueuedValidateNow(ctx.ShowProgress, QueuedStepValidate, QueuedStepTotal, label);
            CaveBuildEditorLog.LogCave(
                $"Validate TICK — {label.Replace("[Cave] ", string.Empty)}",
                forceUnityConsole: true);

            if (!RunQueuedValidateChunk(ctx))
                return;

            QueueGeoAfterValidate(ctx);
        }

        static void QueueGeoAfterValidate(QueuedPipelineContext ctx)
        {
            if (_queued == null || ctx == null)
                return;

            var next = ComputeNextQueuedStep(QueuedStepValidate, ctx);
            if (next < 0 || next >= QueuedStepTotal)
                return;

            CaveBuildEditorLog.LogCave(
                "Validate finished — queuing cave geo (skipping surface NavMesh probe until geo complete).",
                forceUnityConsole: true);
            CaveBuildActionPacing.PreparePipelineChainKickoff();
            CaveBuildValidateTickRunner.Enqueue(() => RunQueuedBuildStep(next));
        }

        /// <summary>
        /// After a long Cursor agent run, the macro pipeline can stall on step 119/121 while auto-rebuild defers forever.
        /// Jump to manifest + finalize so the user is not stuck on a stale status line.
        /// </summary>
        static bool _resumeAfterAgentArmed;
        static bool _manifestFinalizeResumeArmed;
        static double _manifestStepEnteredAt;
        static bool _manifestWatchdogArmed;
        static bool _queuedStepWatchdogArmed;
        static int _queuedStepWatchdogStep = -1;
        static double _queuedStepWatchdogEnteredAt;
        static bool _queuedStepTimeoutTriggered;

        public static void ResetResumeAfterAgentArmed()
        {
            _resumeAfterAgentArmed = false;
            _manifestFinalizeResumeArmed = false;
            _manifestWatchdogArmed = false;
            _queuedStepWatchdogArmed = false;
            _queuedStepWatchdogStep = -1;
            _queuedStepWatchdogEnteredAt = 0;
            _queuedStepTimeoutTriggered = false;
            EditorApplication.update -= ManifestFinalizeWatchdog;
            EditorApplication.update -= QueuedStepWatchdog;
        }

        /// <summary>Once per Cursor agent wait — unstick research 88–99 or advance manifest → finalize (never re-queue 115 in a loop).</summary>
        public static void ResumePipelineAfterCursorAgent()
        {
            if (_queued == null || _resumeAfterAgentArmed)
                return;

            var step = CaveBuildRunStatusPublisher.CurrentQueuedStep;
            if (step >= QueuedStepFinishReport)
                return;

            if (step >= QueuedStepPostMeatFirst && step < QueuedStepResearchFirst)
            {
                _resumeAfterAgentArmed = true;
                _queued.QueuedAwaitingResearchPrompt = false;
                _queued.QueuedAwaitingResearchPromptStep = -1;
                CaveBuildActionPacing.PreparePipelineChainKickoff();
                var next = step + 1;
                if (next >= QueuedStepResearchFirst)
                    next = QueuedStepAaaManifest;
                CaveBuildEditorLog.LogCave(
                    $"Pipeline resume after Cursor agent — post-meat was stuck at step {step + 1}/{QueuedStepTotal}, " +
                    $"scheduling step {next + 1}.",
                    forceUnityConsole: true);
                ScheduleQueuedStep(next, CaveBuildActionPacing.ActionWeight.Normal);
                return;
            }

            if (step < QueuedStepResearchFirst)
                return;

            _resumeAfterAgentArmed = true;
            CaveBuildActionPacing.PreparePipelineChainKickoff();

            if (step >= QueuedStepAaaManifest)
            {
                CaveBuildEditorLog.LogCave(
                    $"Pipeline resume after Cursor agent — at step {step + 1}/{QueuedStepTotal}, " +
                    "scheduling finalize only (manifest already ran).",
                    forceUnityConsole: true);
                ScheduleQueuedStep(QueuedStepFinishReport, CaveBuildActionPacing.ActionWeight.Normal);
                return;
            }

            CaveBuildEditorLog.LogCave(
                $"Pipeline resume after Cursor agent — was step {step + 1}/{QueuedStepTotal}, " +
                "scheduling commercial manifest + finalize.",
                forceUnityConsole: true);
            ScheduleQueuedStep(QueuedStepAaaManifest, CaveBuildActionPacing.ActionWeight.Normal);
        }

        /// <summary>Recover when manifest ran but finalize was skipped (incremental ladder / stale step 115).</summary>
        public static void ResumePipelineAfterManifestIfStuck()
        {
            if (_queued == null)
                return;

            var step = CaveBuildRunStatusPublisher.CurrentQueuedStep;
            if (step != QueuedStepAaaManifest)
                return;

            if (_manifestFinalizeResumeArmed)
                return;

            _manifestFinalizeResumeArmed = true;
            _queued.QueuedAwaitingResearchPrompt = false;
            _queued.QueuedAwaitingResearchPromptStep = -1;
            CaveBuildActionPacing.PreparePipelineChainKickoff();
            CaveBuildEditorLog.LogCave(
                "Pipeline resume — manifest complete, scheduling finalize (step 121/121).",
                forceUnityConsole: true);
            ScheduleQueuedStep(QueuedStepFinishReport, CaveBuildActionPacing.ActionWeight.Normal);
        }

        static void ArmManifestFinalizeWatchdog()
        {
            _manifestStepEnteredAt = EditorApplication.timeSinceStartup;
            if (_manifestWatchdogArmed)
                return;
            _manifestWatchdogArmed = true;
            EditorApplication.update -= ManifestFinalizeWatchdog;
            EditorApplication.update += ManifestFinalizeWatchdog;
        }

        static void ManifestFinalizeWatchdog()
        {
            if (_queued == null || !IsPhasedBuildActive)
            {
                ResetResumeAfterAgentArmed();
                return;
            }

            var step = CaveBuildRunStatusPublisher.CurrentQueuedStep;
            if (step != QueuedStepAaaManifest)
            {
                ResetResumeAfterAgentArmed();
                return;
            }

            if (EditorApplication.timeSinceStartup - _manifestStepEnteredAt < 6.0)
                return;

            CaveBuildEditorLog.LogCaveWarning("Manifest step stalled >6s — forcing finalize (121/121).");
            _manifestFinalizeResumeArmed = false;
            ResumePipelineAfterManifestIfStuck();
        }

        static void ArmQueuedStepWatchdog(int step)
        {
            _queuedStepWatchdogStep = step;
            _queuedStepWatchdogEnteredAt = EditorApplication.timeSinceStartup;
            _queuedStepTimeoutTriggered = false;
            if (_queuedStepWatchdogArmed)
                return;
            _queuedStepWatchdogArmed = true;
            EditorApplication.update -= QueuedStepWatchdog;
            EditorApplication.update += QueuedStepWatchdog;
        }

        static void QueuedStepWatchdog()
        {
            if (_queued == null || !IsPhasedBuildActive)
            {
                ResetResumeAfterAgentArmed();
                return;
            }

            if (_queuedStepWatchdogStep < 0)
                return;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var timeout = settings.queuedStepTimeoutSeconds;
            if (timeout <= 0f)
                return;

            var current = CaveBuildRunStatusPublisher.CurrentQueuedStep;
            if (current != _queuedStepWatchdogStep)
            {
                _queuedStepWatchdogStep = current;
                _queuedStepWatchdogEnteredAt = EditorApplication.timeSinceStartup;
                _queuedStepTimeoutTriggered = false;
                return;
            }

            if (_queuedStepTimeoutTriggered)
                return;

            if (EditorApplication.timeSinceStartup - _queuedStepWatchdogEnteredAt < timeout)
                return;

            _queuedStepTimeoutTriggered = true;
            var stepHuman = current + 1;
            var msg =
                $"Queued step timeout at {stepHuman}/{QueuedStepTotal} after {timeout:F0}s. " +
                "Fail-fast abort to prevent freeze. Resume: Cave Build -> Emergency Unfreeze, then Build Complete Cave " +
                "(or Resume Pipeline if available).";
            CaveBuildEditorLog.LogCaveWarning("[CaveBuild] " + msg);
            FinishQueued(new LavaTubeCaveBuildReport { Message = msg });
        }

        static void ScheduleQueuedStep(
            int step,
            CaveBuildActionPacing.ActionWeight cooldownAfterPrevious = CaveBuildActionPacing.ActionWeight.Normal,
            bool isPipelineKickoff = false)
        {
            if (!isPipelineKickoff)
                CaveBuildActionPacing.ApplyCooldownTimers(cooldownAfterPrevious);

            var label = CaveBuildPipelineDomains.QueueLabel(QueuedStepLabel(step));
            var weight = QueuedStepScheduleWeight(step);
            if (isPipelineKickoff)
            {
                CaveBuildActionPacing.SchedulePriorityFirstStep(
                    () => RunQueuedBuildStep(step),
                    label,
                    weight);
                return;
            }

            if (step == QueuedStepFinishReport)
            {
                CaveBuildActionPacing.SchedulePriorityFirstStep(
                    () => RunQueuedBuildStep(step),
                    label,
                    CaveBuildActionPacing.ActionWeight.Light);
                return;
            }

            CaveBuildActionPacing.ScheduleBuildStep(
                () => RunQueuedBuildStep(step),
                label,
                weight);
        }

        static CaveBuildActionPacing.ActionWeight QueuedStepScheduleWeight(int step)
        {
            if (step == QueuedStepValidate)
                return CaveBuildActionPacing.ActionWeight.Light;
            if (step >= QueuedStepWorldFirst)
                return CaveBuildActionPacing.ActionWeight.Heavy;
            if (step == QueuedGeoFirst + 7 || step == QueuedGeoFirst + 8)
                return CaveBuildActionPacing.ActionWeight.Heavy;
            return CaveBuildActionPacing.ActionWeight.Normal;
        }

        static void ScheduleValidateContinue(
            QueuedPipelineContext ctx,
            int nextSubStep,
            string label,
            CaveBuildActionPacing.ActionWeight weight = CaveBuildActionPacing.ActionWeight.Light)
        {
            ctx.ValidateSubStep = nextSubStep;
            var total = ValidateSubTotal(ctx);
            CaveBuildEditorLog.LogCave(
                $"Validate continue → {ValidateSubDisplayIndex(ctx, nextSubStep)}/{total} — {label} (next editor tick)",
                forceUnityConsole: true);
            ScheduleValidateOnNextEditorFrame();
        }

        static int ValidateSubTotal(QueuedPipelineContext ctx) =>
            ctx.CaveOnlyContinuation ? ValidateSubCaveOnlyCount : ValidateSubCount;

        static int ValidateSubDisplayIndex(QueuedPipelineContext ctx, int subStep)
        {
            if (!ctx.CaveOnlyContinuation)
                return subStep + 1;
            return subStep switch
            {
                ValidateSubBeginSession => 1,
                ValidateSubCatalogValidate => 2,
                ValidateSubRequestFlags => 3,
                ValidateSubAdventureInit => 4,
                _ => subStep + 1,
            };
        }

        static string QueuedStepLabel(int step) => CaveBuildQueuedPipelineSchedule.StepLabel(step);

        static string QueuedGeoLabel(int step) => CaveBuildQueuedPipelineSchedule.GeoLabel(step);

        static bool UseQueuedAdventureGeometry(QueuedPipelineContext ctx) =>
            ctx.Request.UseSplineMesh &&
            ctx.Request.UseTrue3DCaveSystem &&
            ctx.Request.UseBlockTunnel &&
            !ctx.Request.UseLayoutPrototype;

        static void ApplyDefaultAdventureRequestFlags(WorldGenerationRequest request)
        {
            if (request == null || request.UseLayoutPrototype)
                return;

            request.UseSplineMesh = true;
            request.UseTrue3DCaveSystem = true;
            request.UseBlockTunnel = true;
            request.IncludeCaveWater = true;
            request.UseTerrainCarve = true;
            request.AllowCreateTerrain = true;
        }

        /// <summary>Recovers adventure state when incremental ladder skipped validate flags/init.</summary>
        static bool TryEnsureQueuedAdventureState(QueuedPipelineContext ctx, out string message)
        {
            message = string.Empty;
            if (ctx?.Request == null)
            {
                message = "Queued adventure state missing — no build request.";
                return false;
            }

            if (ctx.Adventure != null)
                return true;

            if (!UseQueuedAdventureGeometry(ctx))
                return true;

            ApplyDefaultAdventureRequestFlags(ctx.Request);
            if (!UseQueuedAdventureGeometry(ctx))
                return true;

            CaveBuildEditorLog.LogCaveWarning(
                "Queued adventure was missing (validate sub-steps may have been skipped) — calling BeginQueued now.");

            ctx.Adventure = CaveAdventureCaveGenerator.BeginQueued(
                ctx.GroundAnchor,
                ctx.Ground,
                ctx.Request,
                ctx.Catalog,
                (t, lbl) => StageProgress(ctx.ShowProgress, 4, lbl, 0.1f + t * 0.12f));

            if (ctx.Adventure != null)
                return true;

            message =
                "Queued adventure state missing — validate steps 9–10 (layout flags + BeginQueued) did not run. " +
                "Disable incremental ladder or run Build Complete — Full AAA Rebuild (invalidate ladder).";
            return false;
        }

        static void RunQueuedBuildStep(int step)
        {
            var ctx = _queued;
            if (ctx == null)
                return;

            if (ctx.QueuedAwaitingResearchPrompt)
            {
                var unblock =
                    step >= QueuedStepPostMeatFirst && step < QueuedStepResearchFirst ||
                    step >= QueuedStepValidationFirst &&
                    step < QueuedStepValidationFirst + QueuedStepValidationCount ||
                    step >= QueuedGeoFirst && step < QueuedGeoFirst + QueuedGeoStepCount;
                if (unblock)
                {
                    CaveBuildEditorLog.LogCaveWarning(
                        $"[CaveBuild] Clearing research prompt wait at step {step + 1} — advancing pipeline.");
                    ctx.QueuedAwaitingResearchPrompt = false;
                    ctx.QueuedAwaitingResearchPromptStep = -1;
                }
                else
                {
                    return;
                }
            }

            if (step == QueuedStepValidate)
            {
                RunValidateSubStepDirect();
                return;
            }

            var label = step == QueuedStepValidate
                ? QueuedValidateLiveLabel(ctx, ctx.ValidateSubStep)
                : $"[Cave] {QueuedStepLabel(step)}";
            ArmQueuedStepWatchdog(step);
            CaveBuildRunStatusPublisher.SetQueuedStep(step, QueuedStepTotal, label);
            ProgressQueued(ctx.ShowProgress, step, QueuedStepTotal, label);
            if (step == QueuedStepValidate)
            {
                CaveBuildEditorLog.LogCave(
                    $"Validate running now — {label.Replace("[Cave] ", string.Empty)}",
                    forceUnityConsole: true);
            }
            else if (step >= QueuedGeoFirst && step < QueuedGeoFirst + QueuedGeoStepCount)
            {
                CaveBuildEditorLog.LogCave(
                    $"Cave geo running — {QueuedGeoLabel(step)} ({step - QueuedGeoFirst + 1}/{QueuedGeoStepCount})",
                    forceUnityConsole: true);
            }

            var rungId = CaveBuildPhaseContractRegistry.MapQueuedStepToRung(step);
            if (CaveBuildIncrementalLadder.ShouldSkipQueuedStep(step, ctx.Request))
            {
                if (!string.IsNullOrEmpty(rungId))
                    CaveBuildPhaseContractRegistry.MarkRungComplete(rungId, ctx.Request.Seed);
                var skipNext = CaveBuildIncrementalLadder.AdvanceSkippingComplete(step + 1, QueuedStepTotal, ctx.Request);
                if (skipNext < QueuedStepTotal)
                {
                    ScheduleQueuedStep(skipNext, GetStepCooldownWeight(step));
                    return;
                }
            }

            if (!string.IsNullOrEmpty(rungId))
                CaveBuildLadderMetrics.BeginRung(rungId);

            try
            {
                // Validate step 0 runs paced pre-placement research substeps — do not also run full gate + RefreshForPhase here.
                var skipResearchGate = step == QueuedStepValidate ||
                                       step >= QueuedStepAaaManifest ||
                                       (step >= QueuedStepValidationFirst &&
                                        step < QueuedStepValidationFirst + QueuedStepValidationCount) ||
                                       (step >= QueuedGeoFirst && step < QueuedGeoFirst + QueuedGeoStepCount) ||
                                       (ctx.CaveOnlyContinuation &&
                                        ctx.Request?.SurfaceScope == SurfaceBuildScope.CaveOnly);
                var awaitingResearchPrompt = false;
                if (!skipResearchGate)
                {
                    CaveBuildEditorLog.LogCave(
                        $"Research gate before step {step} ({CaveBuildPhaseResearchGate.ResolvePhaseId(step)})…",
                        forceUnityConsole: true);
                }

                if (!skipResearchGate &&
                    !CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(
                        step,
                        ctx.Request,
                        out var researchGateMsg,
                        out awaitingResearchPrompt))
                {
                    if (!string.IsNullOrEmpty(rungId))
                    {
                        CaveBuildLadderMetrics.EndRung(rungId, skipped: false);
                        CaveBuildResearchConstrainedGate.OnRungFailed(
                            rungId,
                            CaveBuildPhaseResearchGate.ResolvePhaseId(step),
                            ctx.Request.Seed,
                            researchGateMsg);
                    }

                    FinishQueued(new LavaTubeCaveBuildReport { Message = researchGateMsg });
                    return;
                }

                if (awaitingResearchPrompt)
                {
                    ctx.QueuedAwaitingResearchPrompt = true;
                    ctx.QueuedAwaitingResearchPromptStep = step;
                    return;
                }

                if (step == QueuedStepValidate)
                {
                    if (!RunQueuedValidateChunk(ctx))
                        return;
                }
                else if (step >= QueuedGeoFirst && step < QueuedGeoFirst + QueuedGeoStepCount)
                    RunQueuedGeometryStep(ctx, step);
                else if (step >= QueuedStepPlayabilityFirst &&
                         step < QueuedStepPlayabilityFirst + CaveAdventurePlayabilityPipeline.StepCount)
                    RunQueuedPlayabilityStep(ctx, step - QueuedStepPlayabilityFirst);
                else if (step >= QueuedStepValidationFirst &&
                         step < QueuedStepValidationFirst + QueuedStepValidationCount)
                {
                    if (step == QueuedStepValidationFirst &&
                        !CaveBuildWorkflowGuardrails.PreValidationGuardrailCheck(
                            ctx.Request,
                            ctx.Ground,
                            ctx.CaveRoot,
                            out var guardrailMsg))
                    {
                        FinishQueued(new LavaTubeCaveBuildReport { Message = guardrailMsg });
                        return;
                    }

                    RunQueuedValidationStep(ctx, step - QueuedStepValidationFirst);
                }
                else if (step >= QueuedStepGroundPolishFirst &&
                         step < QueuedStepGroundPolishFirst + QueuedStepGroundPolishCount)
                    RunQueuedGroundPolishStep(ctx, step - QueuedStepGroundPolishFirst);
                else if (step >= QueuedStepWorldFirst && step < QueuedStepWorldFirst + QueuedWorldStageCount)
                    RunQueuedWorldStage(ctx, step - QueuedStepWorldFirst);
                else if (step == QueuedStepMeatStart)
                    StartQueuedMeatLoop(ctx);
                else if (step >= QueuedStepPostMeatFirst && step < QueuedStepPostMeatFirst + QueuedPostMeatStepCount)
                    RunQueuedPostMeatStep(ctx, step - QueuedStepPostMeatFirst);
                else if (step >= QueuedStepResearchFirst && step < QueuedStepResearchFirst + QueuedResearchStepCount)
                    RunQueuedResearchStep(ctx, step - QueuedStepResearchFirst);
                else if (step >= QueuedStepFinalizePolishFirst &&
                         step < QueuedStepFinalizePolishFirst + QueuedStepFinalizePolishCount)
                    RunQueuedFinalizePolishStep(ctx, step - QueuedStepFinalizePolishFirst);
                else if (step == QueuedStepAaaManifest)
                {
                    ArmManifestFinalizeWatchdog();
                    RunQueuedAaaManifest(ctx);
                }
                else if (step == QueuedStepFinishReport)
                    RunQueuedFinishReport(ctx);
                else
                    FinishQueued(new LavaTubeCaveBuildReport { Message = "Unknown queued build step." });
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                if (!string.IsNullOrEmpty(rungId))
                {
                    CaveBuildLadderMetrics.EndRung(rungId, skipped: false);
                    CaveBuildResearchConstrainedGate.OnRungFailed(
                        rungId,
                        CaveBuildPhaseResearchGate.ResolvePhaseId(step),
                        ctx.Request.Seed,
                        ex.Message);
                }

                FinishQueued(new LavaTubeCaveBuildReport { Message = label + " failed: " + ex.Message });
                return;
            }

            if (!string.IsNullOrEmpty(rungId))
            {
                CaveBuildLadderMetrics.EndRung(rungId, skipped: false);
                CaveBuildPhaseContractRegistry.MarkRungComplete(rungId, ctx.Request.Seed);
            }

            if (_queued == null)
                return;

            if (step == QueuedStepMeatStart)
                return;

            var next = ComputeNextQueuedStep(step, ctx);
            if (next < 0)
                return;

            if (next >= QueuedStepTotal)
            {
                if (step < QueuedStepFinishReport)
                {
                    CaveBuildEditorLog.LogCave(
                        "Queued pipeline end — scheduling finalize report.",
                        forceUnityConsole: true);
                    ScheduleQueuedStep(QueuedStepFinishReport, GetStepCooldownWeight(step));
                }

                return;
            }

            if (step != QueuedStepValidate)
            {
                ResolveQueuedCaveRoot(ctx);
                CaveBuildPhaseBotReport.RecordAfterQueuedStep(
                    step,
                    ctx.CaveRoot,
                    CaveBuildPhaseResearchGate.ResolvePhaseId(step));
            }
            ScheduleQueuedStep(next, GetStepCooldownWeight(step));
        }

        static CaveBuildActionPacing.ActionWeight GetStepCooldownWeight(int step)
        {
            if (step >= QueuedStepGroundPolishFirst && step < QueuedStepGroundPolishFirst + QueuedStepGroundPolishCount)
                return CaveBuildActionPacing.ActionWeight.Heavy;
            if (step >= QueuedStepWorldFirst)
                return CaveBuildActionPacing.ActionWeight.Heavy;
            if (step >= QueuedStepValidationFirst && step < QueuedStepValidationFirst + QueuedStepValidationCount)
                return CaveBuildActionPacing.ActionWeight.Normal;
            if (step == QueuedGeoFirst + 7 || step == QueuedGeoFirst + 8)
                return CaveBuildActionPacing.ActionWeight.Heavy;
            return CaveBuildActionPacing.ActionWeight.Normal;
        }

        static int ComputeNextQueuedStep(int step, QueuedPipelineContext ctx)
        {
            if (step == QueuedGeoFirst && !UseQueuedAdventureGeometry(ctx))
                return QueuedStepPlayabilityFirst;

            if (step == QueuedGeoFirst + 7)
            {
                var s = ctx.Adventure;
                if (s != null && s.BlockRingCount > 0 && s.BlockRingIndex < s.BlockRingCount)
                    return step;
            }

            var next = step + 1;
            return CaveBuildIncrementalLadder.AdvanceSkippingComplete(next, QueuedStepTotal, ctx.Request);
        }

        static CaveBuildActionPacing.ActionWeight QueuedResearchScheduleWeight(int researchSub) =>
            researchSub switch
            {
                CaveBuildPrePlacementResearch.QueuedResearchCache => CaveBuildActionPacing.ActionWeight.Heavy,
                CaveBuildPrePlacementResearch.QueuedResearchHillshades => CaveBuildActionPacing.ActionWeight.Heavy,
                CaveBuildPrePlacementResearch.QueuedResearchCatalog => CaveBuildActionPacing.ActionWeight.Normal,
                CaveBuildPrePlacementResearch.QueuedResearchBrief => CaveBuildActionPacing.ActionWeight.Normal,
                _ => CaveBuildActionPacing.ActionWeight.Light,
            };

        static string QueuedValidateLiveLabel(QueuedPipelineContext ctx, int subStep)
        {
            var total = ValidateSubTotal(ctx);
            var idx = ValidateSubDisplayIndex(ctx, subStep);

            if (ctx.CaveOnlyContinuation)
            {
                return subStep switch
                {
                    ValidateSubBeginSession => $"[Cave] validate {idx}/{total} — session (surface frozen)",
                    ValidateSubCatalogValidate => $"[Cave] validate {idx}/{total} — prefab catalog",
                    ValidateSubRequestFlags => $"[Cave] validate {idx}/{total} — layout seed & flags",
                    ValidateSubAdventureInit => $"[Cave] validate {idx}/{total} — adventure state init",
                    _ => $"[Cave] validate {idx}/{total}",
                };
            }

            if (subStep >= ValidateSubResearchFirst &&
                subStep < ValidateSubResearchFirst + CaveBuildPrePlacementResearch.QueuedResearchStepCount)
            {
                var r = subStep - ValidateSubResearchFirst;
                return
                    $"[Cave] validate {idx}/{total} — " +
                    CaveBuildPrePlacementResearch.QueuedResearchStepLabels[r];
            }

            return subStep switch
            {
                ValidateSubBeginSession => $"[Cave] validate {idx}/{total} — session & build mode",
                ValidateSubCatalogValidate => $"[Cave] validate {idx}/{total} — prefab catalog",
                ValidateSubRequestFlags => $"[Cave] validate {idx}/{total} — layout seed & flags",
                ValidateSubAdventureInit => $"[Cave] validate {idx}/{total} — adventure state init",
                _ => $"[Cave] validate {idx}/{total}",
            };
        }

        /// <summary>Validate in micro-queued slices (research, catalog, adventure init) — one editor action at a time.</summary>
        static bool RunQueuedValidateChunk(QueuedPipelineContext ctx)
        {
            switch (ctx.ValidateSubStep)
            {
                case ValidateSubBeginSession:
                {
                    if (!ctx.ValidateBeginUiPublished)
                    {
                        ctx.ValidateBeginUiPublished = true;
                        ProgressQueued(
                            ctx.ShowProgress,
                            QueuedStepValidate,
                            QueuedStepTotal,
                            "[Cave] validate 1/13 — session & build mode");
                        ScheduleValidateContinue(ctx, ValidateSubBeginSession, "session apply");
                        return false;
                    }

                    var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    if (!CaveBuildAaaProductionBootstrap.IsFullProductionBuild(
                            ctx.Request.SurfaceScope, ctx.Request.UseLayoutPrototype))
                    {
                        if (ctx.CaveOnlyContinuation)
                            CaveBuildRunStatusPublisher.BeginCaveContinuationSession(sceneName, ctx.Request.Seed);
                        else
                        {
                            var env = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
                            var hasSurface = env != null && env.transform.Find(SurfaceWorldPaths.RootName) != null;
                            var additive = ctx.Request.SurfaceScope == SurfaceBuildScope.FullWorld && hasSurface;
                            CaveBuildRunStatusPublisher.BeginSession(sceneName, ctx.Request.Seed, additive);
                        }
                    }

                    ctx.ValidateResearchAccumulated = string.Empty;

                    if (ctx.CaveOnlyContinuation)
                    {
                        ProgressQueued(
                            ctx.ShowProgress,
                            QueuedStepValidate,
                            QueuedStepTotal,
                            "[Cave] validate (surface frozen)");
                        ScheduleValidateContinue(
                            ctx,
                            ValidateSubCatalogValidate,
                            "prefab catalog",
                            CaveBuildActionPacing.ActionWeight.Light);
                        return false;
                    }

                    ProgressQueued(
                        ctx.ShowProgress,
                        QueuedStepValidate,
                        QueuedStepTotal,
                        "[Cave] pre-placement research (paced)");

                    if (CaveBuildPrePlacementResearch.IsGatePassedForSeed(ctx.Request.Seed))
                    {
                        ScheduleValidateContinue(
                            ctx,
                            ValidateSubCatalogValidate,
                            "prefab catalog",
                            CaveBuildActionPacing.ActionWeight.Light);
                        return false;
                    }

                    var researchStart = CaveBuildPrePlacementResearch.ResolveValidateResearchStartSubStep(
                        ctx.Request.Seed);
                    ScheduleValidateContinue(
                        ctx,
                        ValidateSubResearchFirst + researchStart,
                        CaveBuildPrePlacementResearch.QueuedResearchStepLabels[researchStart],
                        QueuedResearchScheduleWeight(researchStart));
                    return false;
                }

                case int sub when sub >= ValidateSubResearchFirst &&
                                  sub < ValidateSubResearchFirst + CaveBuildPrePlacementResearch.QueuedResearchStepCount:
                {
                    var researchSub = sub - ValidateSubResearchFirst;
                    var researchLabel =
                        CaveBuildPrePlacementResearch.QueuedResearchStepLabels[researchSub];
                    ProgressQueued(
                        ctx.ShowProgress,
                        QueuedStepValidate,
                        QueuedStepTotal,
                        $"validate {ValidateSubDisplayIndex(ctx, sub)}/{ValidateSubTotal(ctx)} — {researchLabel}");

                    if (!CaveBuildPrePlacementResearch.RunQueuedResearchStep(
                            researchSub,
                            ctx.Request,
                            ctx.Request.SurfaceScope == SurfaceBuildScope.FullWorld,
                            ref ctx.ValidateResearchAccumulated,
                            out var researchMsg,
                            out var awaitingResearchTsx))
                    {
                        FinishQueued(new LavaTubeCaveBuildReport { Message = researchMsg });
                        return true;
                    }

                    if (awaitingResearchTsx)
                    {
                        ctx.ValidateAwaitingTsx = true;
                        return false;
                    }

                    if (researchSub == CaveBuildPrePlacementResearch.QueuedResearchSkipCheck &&
                        CaveBuildPrePlacementResearch.IsGatePassedForSeed(ctx.Request.Seed))
                    {
                        ScheduleValidateContinue(
                            ctx,
                            ValidateSubCatalogValidate,
                            "prefab catalog",
                            CaveBuildActionPacing.ActionWeight.Light);
                        return false;
                    }

                    if (researchSub == CaveBuildPrePlacementResearch.QueuedResearchActivePrompt)
                    {
                        CaveBuildEnhancementRunner.RunHook(CaveBuildEnhancementCatalog.Hook.AfterResearch);
                        CaveBuildPhaseContractRegistry.MarkRungComplete(
                            CaveBuildPhaseContractRegistry.RungResearchSeed,
                            ctx.Request.Seed);
                    }

                    var nextResearch = researchSub + 1;
                    if (nextResearch < CaveBuildPrePlacementResearch.QueuedResearchStepCount)
                    {
                        ScheduleValidateContinue(
                            ctx,
                            ValidateSubResearchFirst + nextResearch,
                            CaveBuildPrePlacementResearch.QueuedResearchStepLabels[nextResearch],
                            QueuedResearchScheduleWeight(nextResearch));
                        return false;
                    }

                    ScheduleValidateContinue(
                        ctx,
                        ValidateSubCatalogValidate,
                        "prefab catalog",
                        CaveBuildActionPacing.ActionWeight.Light);
                    return false;
                }

                case ValidateSubCatalogValidate:
                {
                    ProgressQueued(
                        ctx.ShowProgress,
                        QueuedStepValidate,
                        QueuedStepTotal,
                        $"validate {ValidateSubDisplayIndex(ctx, ValidateSubCatalogValidate)}/{ValidateSubTotal(ctx)} — prefab catalog");
                    if (!ctx.Catalog.IsValid)
                    {
                        FinishQueued(new LavaTubeCaveBuildReport
                        {
                            Message = "Prefab catalog empty — check lava tube prefabs under Assets/."
                        });
                        return true;
                    }

                    ScheduleValidateContinue(
                        ctx,
                        ValidateSubRequestFlags,
                        "layout seed & flags",
                        CaveBuildActionPacing.ActionWeight.Light);
                    return false;
                }

                case ValidateSubRequestFlags:
                {
                    ApplyDefaultAdventureRequestFlags(ctx.Request);

                    ProgressQueued(
                        ctx.ShowProgress,
                        QueuedStepValidate,
                        QueuedStepTotal,
                        $"validate {ValidateSubDisplayIndex(ctx, ValidateSubRequestFlags)}/{ValidateSubTotal(ctx)} — layout seed {ctx.Request.Seed}");
                    ctx.Report = new LavaTubeCaveBuildReport
                    {
                        Message =
                            $"Layout seed {ctx.Request.Seed}, segments {ctx.Request.CaveTunnelSegments}, chambers {ctx.Request.CaveChamberCount}."
                    };

                    ScheduleValidateContinue(
                        ctx,
                        ValidateSubAdventureInit,
                        "adventure state init",
                        CaveBuildActionPacing.ActionWeight.Light);
                    return false;
                }

                case ValidateSubAdventureInit:
                {
                    Progress(ctx.ShowProgress, 3, "Prepare queued adventure geometry state…");
                    if (UseQueuedAdventureGeometry(ctx))
                    {
                        ctx.Adventure = CaveAdventureCaveGenerator.BeginQueued(
                            ctx.GroundAnchor,
                            ctx.Ground,
                            ctx.Request,
                            ctx.Catalog,
                            (t, lbl) => StageProgress(ctx.ShowProgress, 4, lbl, 0.1f + t * 0.12f));
                        CaveBuildEditorLog.LogCave(
                            "Adventure queue state ready (materials load on geo step 5 — not blocking validate).",
                            forceUnityConsole: true);
                    }
                    else
                    {
                        CaveBuildEditorLog.LogCave(
                            "Validate complete — monolithic geometry path (no adventure queue state).",
                            forceUnityConsole: true);
                    }

                    ctx.ValidateSubStep = ValidateSubBeginSession;
                    ctx.ValidateResearchAccumulated = null;
                    return true;
                }

                default:
                    FinishQueued(new LavaTubeCaveBuildReport { Message = "Unknown validate sub-step." });
                    return true;
            }
        }

        static bool RunQueuedBlocksBatchWithLiveStatus(CaveAdventureCaveGenerator.QueuedBuildState s)
        {
            if (s == null)
                return false;

            var total = Mathf.Max(1, s.BlockRingCount);
            var done = Mathf.Clamp(s.BlockRingIndex, 0, total);
            CaveBuildRunStatusPublisher.SetSubOperation(
                "cave geo — tunnel wall rings",
                $"ring batch {done + 1}/{total} ({s.BlockCount} blocks so far)");
            return CaveAdventureCaveGenerator.QueuedStepBlocksBatch(s);
        }

        static void RunQueuedGeometryStep(QueuedPipelineContext ctx, int step)
        {
            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.Geometry);
            if (step == QueuedGeoFirst && !UseQueuedAdventureGeometry(ctx))
            {
                RunQueuedMonolithicGeometry(ctx);
                return;
            }

            if (!TryEnsureQueuedAdventureState(ctx, out var adventureMsg))
            {
                FinishQueued(new LavaTubeCaveBuildReport { Message = adventureMsg });
                return;
            }

            var s = ctx.Adventure;
            var geoIdx = step - QueuedGeoFirst + 1;
            ProgressQueued(
                ctx.ShowProgress,
                step,
                QueuedStepTotal,
                $"cave geo {geoIdx}/{QueuedGeoStepCount} — {QueuedGeoLabel(step)}");

            if (step == QueuedGeoFirst + 9)
                EnvironmentKitScopedAssetRefresh.ImportStructurePrefabsNow(ctx.Catalog);

            if (step == QueuedGeoFirst + QueuedGeoStepCount - 1)
                EnvironmentKitScopedAssetRefresh.ImportScatterPropsNow(ctx.Catalog);

            bool cancelled;
            if (step == QueuedGeoFirst + 5)
            {
                cancelled = CaveAdventureCaveGenerator.QueuedStepShell(s);
                if (!cancelled)
                {
                    ctx.CaveRoot = s.CavesRoot;
                    CaveBuildEnhancementRunner.RunHook(
                        CaveBuildEnhancementCatalog.Hook.AfterCaveShell,
                        ctx.CaveRoot);
                }

            }
            else
            {
                cancelled = step switch
                {
                    var n when n == QueuedGeoFirst => CaveAdventureCaveGenerator.QueuedStepClear(s),
                    var n when n == QueuedGeoFirst + 1 => CaveAdventureCaveGenerator.QueuedStepEntrance(s),
                    var n when n == QueuedGeoFirst + 2 => CaveAdventureCaveGenerator.QueuedStepMaze(s),
                    var n when n == QueuedGeoFirst + 3 => CaveAdventureCaveGenerator.QueuedStepAddTerrain(s),
                    var n when n == QueuedGeoFirst + 4 => CaveAdventureCaveGenerator.QueuedStepPlatforms(s),
                    var n when n == QueuedGeoFirst + 6 => CaveAdventureCaveGenerator.QueuedStepLabyrinthAnnex(s),
                    var n when n == QueuedGeoFirst + 7 => CaveAdventureCaveGenerator.QueuedStepGrandCavern(s),
                    var n when n == QueuedGeoFirst + 8 => CaveAdventureCaveGenerator.QueuedStepBlocksPrepare(s),
                    var n when n == QueuedGeoFirst + 9 => RunQueuedBlocksBatchWithLiveStatus(s),
                    var n when n == QueuedGeoFirst + 10 => CaveAdventureCaveGenerator.QueuedStepBlocksFinish(s),
                    var n when n == QueuedGeoFirst + 11 => CaveAdventureCaveGenerator.QueuedStepFeatures(s),
                    var n when n == QueuedGeoFirst + 12 => CaveAdventureCaveGenerator.QueuedStepSurfaceWalkIn(s),
                    var n when n == QueuedGeoFirst + 13 => CaveAdventureCaveGenerator.QueuedStepSpawn(s),
                    var n when n == QueuedGeoFirst + 14 => CaveAdventureCaveGenerator.QueuedStepPropsAndWater(s),
                    _ => false,
                };
            }

            if (cancelled)
            {
                FinishQueued(new LavaTubeCaveBuildReport { Message = "Cave build cancelled." });
                return;
            }

            if (step == QueuedGeoFirst + QueuedGeoStepCount - 1)
            {
                ctx.Report = CaveAdventureCaveGenerator.FinishQueuedReport(s);
                ctx.CaveRoot = s.CavesRoot;
                CaveBuildPhaseContractRegistry.MarkRungComplete(
                    CaveBuildPhaseContractRegistry.RungCaveLayout,
                    ctx.Request.Seed);
                CaveBuildSurfacePipeline.AlignCaveAfterGenerate(ctx.CaveRoot, ctx.Ground, ctx.Request);
                if (ctx.Request?.SurfaceScope == SurfaceBuildScope.FullWorld &&
                    ctx.CaveRoot != null &&
                    ctx.Ground != null)
                {
                    CaveGroundPlacementUtility.ReseatCaveUnderTerrainAfterSurface(
                        ctx.CaveRoot, ctx.Ground, out var reseatMsg);
                    if (!string.IsNullOrEmpty(reseatMsg))
                        Debug.Log("[CaveBuild] Post-geo cave placement: " + reseatMsg);
                }

                if (ctx.CaveRoot != null)
                    CaveGenerationQualityPhaseRunner.RunRange(
                        ctx.CaveRoot, ctx.Ground, ctx.Request, 0, 20, out _);
                if (ctx.Request.UseLayoutPrototype)
                {
                    FinishQueued(FinalizeLayoutPrototype(
                        ctx.CaveRoot, ctx.Ground, ctx.Request, ctx.Report, ctx.ShowProgress));
                }
            }
        }

        static void RunQueuedMonolithicGeometry(QueuedPipelineContext ctx)
        {
            Progress(ctx.ShowProgress, 4, "Cave geometry (entrance + organic tube)…");
            if (ctx.Request.UseSplineMesh)
            {
                ctx.Report = SplineLavaTubeCaveGenerator.Generate(
                    ctx.GroundAnchor,
                    ctx.Ground,
                    ctx.Request,
                    (t, lbl) => StageProgress(ctx.ShowProgress, 4, lbl, 0.1f + t * 0.12f));
            }
            else
            {
                ctx.Report = LavaTubeCaveGenerator.Generate(ctx.GroundAnchor, ctx.Ground, ctx.Request);
            }

            ResolveQueuedCaveRoot(ctx);
            if (ctx.CaveRoot == null)
            {
                FinishQueued(ctx.Report);
                return;
            }

            CaveBuildSurfacePipeline.AlignCaveAfterGenerate(ctx.CaveRoot, ctx.Ground, ctx.Request);
            if (ctx.Request?.SurfaceScope == SurfaceBuildScope.FullWorld && ctx.CaveRoot != null && ctx.Ground != null)
            {
                CaveGroundPlacementUtility.ReseatCaveUnderTerrainAfterSurface(
                    ctx.CaveRoot, ctx.Ground, out var reseatMsg);
                if (!string.IsNullOrEmpty(reseatMsg))
                    Debug.Log("[CaveBuild] Post-generate cave placement: " + reseatMsg);
            }

            if (ctx.Request.UseLayoutPrototype)
            {
                FinishQueued(FinalizeLayoutPrototype(
                    ctx.CaveRoot, ctx.Ground, ctx.Request, ctx.Report, ctx.ShowProgress));
            }
        }

        static void ResolveQueuedCaveRoot(QueuedPipelineContext ctx)
        {
            ctx.CaveRoot = ctx.GroundAnchor.Find(CaveGeometryPaths.CaveSystemRootName);
            if (ctx.CaveRoot == null)
                ctx.CaveRoot = ctx.GroundAnchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
        }

        static void RunQueuedPlayabilityStep(QueuedPipelineContext ctx, int playStep)
        {
            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.Playability);
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);
            if (ctx.CaveRoot == null)
            {
                FinishQueued(ctx.Report);
                return;
            }

            if (playStep == 0)
            {
                if (!CavePlayabilityValidator.IsAdventureCave(ctx.CaveRoot))
                    EnsureBlockTunnelShell(ctx.CaveRoot, ctx.Request);
            }

            if (!CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                return;

            if (playStep == 0)
            {
                var geometry = ctx.CaveRoot.Find(CaveGeometryPaths.GeometryRoot);
                if (geometry != null)
                    CaveEnclosureShellBuilder.PurgeLayerOffenders(geometry);
            }

            Progress(ctx.ShowProgress, PlayabilityStageStart + playStep,
                CaveAdventurePlayabilityPipeline.StepLabels[playStep]);
            CaveAdventurePlayabilityPipeline.RunStep(playStep, ctx.CaveRoot, ctx.Request, ctx.Ground);

            if (playStep != CaveAdventurePlayabilityPipeline.StepCount - 1)
                return;

            if (!CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                return;

            CaveCompactLayerPurge.PurgeShellLayersOnly(ctx.CaveRoot);
            var meta = ctx.CaveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                var layout = CaveThirdPersonLayoutUtility.GenerateForCave(meta, ctx.Request.UseLayoutPrototype);
                CaveMobSpawnerPlacement.PlaceAlongRoute(ctx.CaveRoot, layout);
                CaveAdventureCaveLighting.Apply(ctx.CaveRoot, layout);
            }

            CaveGenerationQualityPhaseRunner.RunRange(
                ctx.CaveRoot, ctx.Ground, ctx.Request, 20, 10, out _);
        }

        static void RunQueuedValidationStep(QueuedPipelineContext ctx, int validationStep)
        {
            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.Playability);
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);
            if (ctx.CaveRoot == null || !CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                return;

            Progress(
                ctx.ShowProgress,
                22 + validationStep,
                CaveBuildAutomatedValidation.StepLabels[validationStep]);
            CaveBuildAutomatedValidation.RunStep(
                validationStep,
                ctx.CaveRoot,
                ctx.Ground,
                ctx.Request);
        }

        static void RunQueuedWorldStage(QueuedPipelineContext ctx, int worldStage)
        {
            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.World);
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);
            if (ctx.CaveRoot == null)
            {
                FinishQueued(new LavaTubeCaveBuildReport { Message = "Cave root missing for world build." });
                return;
            }

            var progressStage = 27 + worldStage;
            Progress(ctx.ShowProgress, progressStage, WorldStageLabels[worldStage]);

            switch (worldStage)
            {
                case 0:
                    ctx.Report.SeamlessQuality = ValidateOrganicMesh(ctx.CaveRoot);
                    break;
                case 1:
                    EnvironmentKitScopedAssetRefresh.ImportScatterPropsNow(ctx.Catalog);
                    if (CaveBuildWorkflowCoordinator.TryConsumeWorldPropScatter())
                        ScatterExtraProps(ctx.CaveRoot, ctx.Catalog, ctx.Rng, ctx.Request);
                    break;
                case 2:
                    if (ctx.Request.UseTrue3DCaveSystem)
                    {
                        var shell = ctx.CaveRoot.Find("OcclusionShell");
                        if (shell != null)
                            CaveBuildSceneUtility.ClearChildrenFast(shell);
                        ctx.Report.ShellPieceCount = 0;
                    }
                    else
                    {
                        ctx.Report.ShellPieceCount = LavaTubeCaveEnclosureBuilder.Build(
                            ctx.CaveRoot, ctx.Catalog, ctx.Rng, ctx.Report.PathNodes);
                    }

                    break;
                case 3:
                    EnvironmentKitScopedAssetRefresh.ImportMaterialsPackNow();
                    CaveSceneMaterialRepair.RepairCaveRoot(ctx.CaveRoot);
                    if (ctx.Request.IncludeCaveWater)
                        BuildCaveWater(ctx.CaveRoot);
                    else
                        CaveWaterUtility.ClearAllWater(ctx.CaveRoot);
                    break;
                case 4:
                    CaveCinematicLightingPass.Apply(
                        ctx.CaveRoot,
                        ctx.Ground,
                        ctx.Request,
                        out var cinematicMsg);
                    if (!string.IsNullOrEmpty(cinematicMsg))
                        Debug.Log("[CaveBuild] " + cinematicMsg);
                    break;
                case 5:
                    BuildFx(ctx.CaveRoot, ctx.Rng);
                    BuildFogMist(ctx.CaveRoot);
                    break;
                case 6:
                    CaveBlockTunnelRuntimeSetup.EnsureOnCaveRoot(ctx.CaveRoot);
                    var postPhysics = LavaTubeCavePostProcess.ApplyPhysicsAndLod(
                        ctx.CaveRoot, ctx.XrProfile, bakeGiHints: true);
                    RestoreBlockVisibilityForEditor(ctx.CaveRoot);
                    if (ctx.Request.UseTrue3DCaveSystem)
                        CaveAdventureVisualPass.Apply(ctx.CaveRoot);
                    ctx.Report.DrawCallEstimate = postPhysics.DrawCallEstimate;
                    ctx.Report.TriangleEstimate = postPhysics.TriangleEstimate;
                    ctx.Report.PieceCount = postPhysics.PieceCount;
                    break;
                case 7:
                    ctx.Report.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(ctx.CaveRoot);
                    break;
                case 8:
                    FinalizeGameplay(ctx.CaveRoot, ctx.Ground, ctx.Report, ctx.Request);
                    break;
                case 9:
                    CaveSceneMaterialRepair.RepairCaveRoot(ctx.CaveRoot);
                    if (!CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        CaveFloorSafetyUtility.EnsureVisibleWalkways(ctx.CaveRoot);
                    CaveColliderUtility.EnsureMazeVolumeColliders(ctx.CaveRoot);
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                    {
                        CaveBuildWorkflowCoordinator.InvalidateNavMesh();
                        CaveAdventureVisualPass.Apply(ctx.CaveRoot);
                    }

                    if (ctx.Request.UseTrue3DCaveSystem)
                        ctx.Report.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(ctx.CaveRoot, force: true);
                    if (ctx.Request.IncludeCaveWater)
                        CaveWaterBuilder.RebuildForCave(ctx.CaveRoot);
                    else
                        CaveWaterUtility.ClearAllWater(ctx.CaveRoot);
                    break;
                case 10:
                    if (!CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        LavaTubeCaveEnclosureBuilder.EnsureAtmosphereZone(ctx.CaveRoot, ctx.Report.PathNodes);
                    ValidatePlayabilityGate(ctx.CaveRoot, ctx.Request);
                    ValidateEnclosure(ctx.CaveRoot, ctx.Report, ctx.Request.UseSplineMesh);
                    if (ctx.Request.IncludeCaveWater)
                        ValidateCaveWater(ctx.CaveRoot);
                    if (ctx.Ground != null && ctx.Ground.HasAnchor)
                    {
                        var entrance = ctx.CaveRoot.Find("Entrance");
                        if (entrance != null)
                            SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);

                        string groundMsg;
                        if (CaveBuildMetadata.ShouldPreserveRootXZ(ctx.CaveRoot))
                            CaveGroundPlacementUtility.TryRepairLockedGroundPlacement(
                                ctx.CaveRoot, ctx.Ground, out groundMsg);
                        else
                            CaveGroundPlacementUtility.FinalizeGroundPlacement(
                                ctx.CaveRoot, ctx.Ground, out groundMsg, ctx.Request.Seed);
                        CaveBuildWorkflowCoordinator.MarkMouthGrounded();
                        var deferGroundLock = ctx.Request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                                              !CaveBuildSurfaceCompletionGate.IsTerrainGradingComplete;
                        if (!deferGroundLock)
                            CaveBuildWorkflowCoordinator.LockGroundPlacement(ctx.CaveRoot);
                        else
                            Debug.Log(
                                "[CaveBuild] FullWorld: deferring ground lock until terrain grading completes " +
                                "(cave will re-seat under LiDAR terrain before meat loop).");
                        if (!string.IsNullOrEmpty(groundMsg))
                            Debug.Log("[CaveBuild] World grounding: " + groundMsg);
                    }

                    break;
                case 11:
                    if (ctx.Ground != null && ctx.Ground.HasAnchor)
                        CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(
                            ctx.CaveRoot, ctx.Ground, out var buryWorld);
                    break;
                case 12:
                    EnvironmentKitScopedAssetRefresh.ImportScatterPropsNow(ctx.Catalog);
                    if (CaveBuildWorkflowCoordinator.TryConsumeWorldPropScatter())
                        ScatterExtraProps(ctx.CaveRoot, ctx.Catalog, ctx.Rng, ctx.Request);
                    break;
                case 13:
                    if (ctx.Request.UseTrue3DCaveSystem && CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        CaveEnclosureShellBuilder.HideRoutePlatformSlabs(ctx.CaveRoot);
                    break;
                case 14:
                    CaveGroundPlacementUtility.EnsureRootWorldXZLock(ctx.CaveRoot);
                    if (ctx.Ground != null && ctx.Ground.HasAnchor)
                        CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                            ctx.CaveRoot, ctx.Ground, allowRaise: false, out _);
                    break;
            }
        }

        static void RunQueuedGroundPolishStep(QueuedPipelineContext ctx, int sub)
        {
            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.World);
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);
            if (ctx.CaveRoot == null || ctx.Ground == null || !ctx.Ground.HasAnchor)
                return;

            var cave = ctx.CaveRoot;
            var ground = ctx.Ground;

            switch (sub)
            {
                case 0:
                    CaveGroundPlacementUtility.ReseatCaveUnderTerrainAfterSurface(cave, ground, out var reseat);
                    if (!string.IsNullOrEmpty(reseat))
                        Debug.Log("[CaveBuild] Ground polish: " + reseat);
                    break;
                case 1:
                    CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(cave, ground, out var bury1);
                    if (!string.IsNullOrEmpty(bury1))
                        Debug.Log("[CaveBuild] Ground polish: " + bury1);
                    break;
                case 2:
                    SurfaceCaveRoofAuditor.AuditAndStrip(cave, ground, out var roof1);
                    if (!string.IsNullOrEmpty(roof1))
                        Debug.Log("[CaveBuild] Ground polish: " + roof1);
                    break;
                case 3:
                    CaveBuildWorkflowGuardrails.EnsureCaveBurialEnvelope(cave, ground, out var envelope);
                    Debug.Log("[CaveBuild] Ground polish: " + envelope);
                    break;
                case 4:
                    CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                        cave, ground, allowRaise: false, out var snap);
                    if (!string.IsNullOrEmpty(snap))
                        Debug.Log("[CaveBuild] Ground polish: " + snap);
                    break;
                case 5:
                    var meta = cave.GetComponent<CaveBuildMetadata>();
                    var seed = meta != null ? meta.seed : ctx.Request?.Seed ?? 0;
                    if (ground.Terrain != null)
                        CaveTerrainUtility.ApplyCaveEntranceMouth(ground.Terrain, seed, cave);
                    break;
                case 6:
                    CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(cave, ground, out var bury2);
                    if (!string.IsNullOrEmpty(bury2))
                        Debug.Log("[CaveBuild] Ground polish: " + bury2);
                    break;
                case 7:
                    SurfaceCaveRoofAuditor.AuditAndStrip(cave, ground, out var roof2);
                    if (!string.IsNullOrEmpty(roof2))
                        Debug.Log("[CaveBuild] Ground polish: " + roof2);
                    break;
                case 8:
                    CaveGroundPlacementUtility.EnsureRootWorldXZLock(cave);
                    break;
                case 9:
                    if (!CaveBuildWorkflowGuardrails.PreValidationGuardrailCheck(
                            ctx.Request, ground, cave, out var gateMsg))
                        Debug.LogWarning("[CaveBuild] Ground polish gate: " + gateMsg);
                    break;
            }
        }

        static bool TryResolveQueuedEnvironmentRoots(
            out EnvironmentAuthoringKit.EnvironmentRoot envRoot,
            out Transform surfaceRoot)
        {
            envRoot = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            surfaceRoot = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
            return envRoot != null;
        }

        static void RunQueuedFinalizePolishStep(QueuedPipelineContext ctx, int sub)
        {
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);

            switch (sub)
            {
                case 0:
                    if (ctx.CaveRoot != null && ctx.Ground != null && ctx.Ground.HasAnchor)
                        CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(
                            ctx.CaveRoot, ctx.Ground, out var bury);
                    break;
                case 1:
                    if (ctx.CaveRoot != null && ctx.Ground != null)
                        SurfaceCaveRoofAuditor.AuditAndStrip(ctx.CaveRoot, ctx.Ground, out _);
                    break;
                case 2:
                    if (CaveBuildWorkflowGuardrails.AuditSurfacePropCoverage(out var coverage))
                        Debug.Log("[CaveBuild] " + coverage);
                    else
                        Debug.LogWarning("[CaveBuild] " + coverage);
                    break;
                case 3:
                {
                    var surfaceTransform = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
                    var veg = surfaceTransform != null
                        ? surfaceTransform.Find(SurfaceIntelligentPropPlacer.VegetationLayerName)
                        : null;
                    if (veg != null && ctx.Ground?.Terrain != null &&
                        !SurfaceTerrainPropPlacementRegion.IsNineTileVegetationSufficient(
                            veg, ctx.Ground.Terrain))
                    {
                        Debug.LogWarning(
                            "[CaveBuild] Nine-tile vegetation below contract — surface props may need another pass.");
                    }

                    if (surfaceTransform != null && ctx.Ground?.Terrain != null && ctx.Request != null)
                    {
                        var center = ctx.Ground.HasAnchor
                            ? ctx.Ground.Anchor.position
                            : new Vector3(
                                ctx.Ground.Bounds.center.x,
                                ctx.Ground.SurfaceY,
                                ctx.Ground.Bounds.center.z);
                        var extent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                            ctx.Ground.Terrain, center, ctx.Request.SurfaceExtentMeters);
                        if (SurfaceIntelligentPropPlacer.TryPlacePostCaveSurfaceRocks(
                                surfaceTransform,
                                ctx.Ground.Terrain,
                                center,
                                extent,
                                ctx.Request.Seed,
                                out var rockMsg))
                            Debug.Log("[CaveBuild] " + rockMsg);
                    }

                    break;
                }
                case 4:
                    if (ctx.CaveRoot != null && ctx.Ground != null && ctx.Request != null)
                    {
                        var regraded = RegradeAndExportForCursorInvoke(
                            ctx.CaveRoot,
                            ctx.Ground,
                            ctx.Request,
                            ctx.Report,
                            ctx.Meat?.Quality,
                            "finalize_polish");
                        if (ctx.Meat != null)
                            ctx.Meat.Quality = regraded;
                    }

                    break;
                case 5:
                    if (ctx.Ground != null &&
                        TryResolveQueuedEnvironmentRoots(out var envRoot, out var surfaceRoot) &&
                        envRoot != null)
                    {
                        CaveBuildWorkflowGuardrails.TryFinalSurfaceNavMeshCommit(
                            ctx.Ground, surfaceRoot, envRoot, out var nav);
                        Debug.Log("[CaveBuild] " + nav);
                    }

                    break;
                case 6:
                    if (ctx.CaveRoot != null && ctx.Ground != null)
                    {
                        CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(ctx.CaveRoot);
                        CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                            ctx.CaveRoot, ctx.Ground, allowRaise: false, out _);
                    }

                    break;
                case 7:
                    if (ctx.CaveRoot != null)
                        CaveAdventurePlayabilityPipeline.CheckSpawnReachability(ctx.CaveRoot);
                    break;
                case 8:
                    if (ctx.CaveRoot != null && ctx.Request != null)
                        CaveGenerationQualityPhaseRunner.RunRange(
                            ctx.CaveRoot, ctx.Ground, ctx.Request, 0, 12, out _);
                    break;
                case 9:
                    if (ctx.CaveRoot != null && ctx.Ground != null && ctx.Request != null)
                        CaveBuildCommercialProductionGrader.Grade(
                            ctx.CaveRoot, ctx.Ground, ctx.Request, ctx.Report, ctx.Meat?.Quality);
                    break;
                case 10:
                    if (ctx.Request != null)
                        CaveBuildCompletionContract.Evaluate(ctx.Request, ctx.Ground, ctx.Report);
                    break;
                case 11:
                    CaveBuildPhaseContractRegistry.ExportContractsCatalog();
                    break;
                case 12:
                    if (ctx.Request != null)
                    {
                        CaveBuildHelperScriptOrchestrator.Queue(
                            CaveBuildHelperScriptOrchestrator.Moment.BuildComplete,
                            CaveBuildHelperScriptOrchestrator.MakeContext(ctx.Request));
                    }

                    break;
                case 13:
                    break;
                case 14:
                    EditorUtility.UnloadUnusedAssetsImmediate();
                    break;
                case 15:
                    if (ctx.CaveRoot != null && ctx.Ground != null && ctx.Ground.HasAnchor)
                    {
                        var openings = CaveGroundPlacementUtility.GetEntranceMouthWorld(ctx.CaveRoot, ctx.Ground);
                        var list = new System.Collections.Generic.List<Vector3>();
                        if (openings.sqrMagnitude > 0.01f)
                            list.Add(openings);
                        var protrusion = CaveGroundPlacementUtility.MeasureMaxCaveProtrusionAboveHeightmap(
                            ctx.CaveRoot, ctx.Ground, list, out _);
                        if (protrusion > 0.85f)
                            Debug.LogWarning(
                                $"[CaveBuild] Finalize: cave still protrudes {protrusion:F2}m above heightmap.");
                    }

                    break;
                case 16:
                    CaveBuildWorkflowGuardrails.AuditSurfacePropCoverage(out _);
                    break;
                case 17:
                    CaveBuildPhaseContractRegistry.ExportContractsCatalog();
                    break;
            }
        }

        static void StartQueuedMeatLoop(QueuedPipelineContext ctx)
        {
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);
            if (ctx.CaveRoot == null && !CaveBuildPhaseContractRegistry.HasPlayableCaveLayoutInScene())
            {
                Debug.LogError(
                    "[CaveBuild] No cave geometry in scene — incremental ladder may have skipped geo steps. " +
                    "Re-run Build Complete Cave or disable incremental ladder / invalidate ladder cache.");
                FinishQueued(new LavaTubeCaveBuildReport
                {
                    Message = "Cave geometry missing — cannot run meat loop. Run Full AAA Rebuild.",
                });
                return;
            }

            if (!CaveTerrainPipelineOrchestrator.CanStartCaveMeatLoop(ctx.Request, ctx.Ground))
            {
                Progress(
                    ctx.ShowProgress,
                    48,
                    "[Cave] Waiting for Florida LiDAR + terrain grade→fix (underground meat loop next)…");
                CaveTerrainPipelineOrchestrator.ScheduleCaveMeatWhenTerrainReady(
                    ctx.Request,
                    ctx.Ground,
                    () => StartQueuedMeatLoop(ctx));
                return;
            }

            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.MeatLoop);
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);
            ctx.CaveRoot ??= CaveRouteProbeRunner.FindCaveRoot();
            if (ctx.CaveRoot != null && ctx.Ground != null && ctx.Ground.HasAnchor)
            {
                CaveGroundPlacementUtility.ReseatCaveUnderTerrainAfterSurface(
                    ctx.CaveRoot, ctx.Ground, out var reseatMsg);
                if (!string.IsNullOrEmpty(reseatMsg))
                    Debug.Log("[CaveBuild] Post-terrain cave placement: " + reseatMsg);

                if (CaveBuildWorkflowGuardrails.EnsureCaveBurialEnvelope(
                        ctx.CaveRoot,
                        ctx.Ground,
                        out var burialMsg))
                    Debug.Log("[CaveBuild] " + burialMsg);
                else
                    Debug.LogWarning("[CaveBuild] " + burialMsg);
            }

            if (!CaveBuildWorkflowCoordinator.IsGroundPlacementLocked)
                CaveBuildWorkflowCoordinator.LockGroundPlacement(ctx.CaveRoot);
            if (ctx.CaveRoot == null)
            {
                FinishQueued(ctx.Report);
                return;
            }

            Progress(ctx.ShowProgress, 38,
                $"{CaveBuildQualityRubric.TargetGrade} quality ladder (grade → fix → re-grade)…");
            ctx.Meat = CaveBuildQualityMeatLoop.BeginQueued(
                ctx.CaveRoot, ctx.Ground, ctx.Request, ctx.Report, ctx.ShowProgress);
            ScheduleQueuedMeatPhase(ctx);
        }

        static void ScheduleQueuedMeatPhase(QueuedPipelineContext ctx)
        {
            var pass = ctx.Meat?.Pass ?? 0;
            var phase = ctx.Meat?.Phase ?? CaveBuildQualityMeatLoop.QueuedMeatPhase.Purge;
            CaveBuildActionPacing.ApplyCooldownTimers(CaveBuildActionPacing.ActionWeight.Normal);
            CaveBuildActionPacing.ScheduleHeavy(
                () => RunQueuedMeatPhase(ctx),
                CaveBuildPipelineDomains.QueueLabel($"meat pass {pass} {phase}"));
        }

        static void RunQueuedMeatPhase(QueuedPipelineContext ctx)
        {
            if (ctx?.Meat == null)
            {
                ScheduleQueuedStep(QueuedStepPostMeatFirst);
                return;
            }

            var label = $"meat pass {ctx.Meat.Pass} {ctx.Meat.Phase}";

            try
            {
                var done = CaveBuildQualityMeatLoop.RunQueuedPhase(
                    ctx.Meat,
                    (n, lbl) => Progress(ctx.ShowProgress, 38, lbl));
                if (done)
                {
                    ctx.RemediationPasses = ctx.Meat.FixesApplied;
                    ScheduleQueuedStep(QueuedStepPostMeatFirst, CaveBuildActionPacing.ActionWeight.Heavy);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                FinishQueued(new LavaTubeCaveBuildReport { Message = "Meat loop failed: " + ex.Message });
                return;
            }

            ScheduleQueuedMeatPhase(ctx);
        }

        static void RunQueuedPostMeatStep(QueuedPipelineContext ctx, int sub)
        {
            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.PostMeat);
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);
            if (ctx.CaveRoot == null)
            {
                FinishQueued(ctx.Report);
                return;
            }

            var quality = ctx.Meat?.Quality;
            if (sub >= 8)
            {
                if (sub < 12 && ctx.Ground != null && ctx.Ground.HasAnchor)
                {
                    CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(ctx.CaveRoot, ctx.Ground, out _);
                    if (sub % 2 == 1)
                        SurfaceCaveRoofAuditor.AuditAndStrip(ctx.CaveRoot, ctx.Ground, out _);
                }
                else if (sub < 16)
                {
                    quality = RegradeAndExportForCursorInvoke(
                        ctx.CaveRoot,
                        ctx.Ground,
                        ctx.Request,
                        ctx.Report,
                        quality,
                        $"post_meat_expanded_{sub}");
                    if (ctx.Meat != null)
                        ctx.Meat.Quality = quality;
                }
                else if (sub < 20 && CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                {
                    switch (sub)
                    {
                        case 16:
                            CaveFloorSafetyUtility.EnsureRouteTerrainPlayCollider(ctx.CaveRoot);
                            break;
                        case 17:
                            CaveSpawnTeleportAuthority.ApplyMainAreaTeleportSpawn(ctx.CaveRoot, ctx.Request);
                            break;
                        case 18:
                            CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(ctx.CaveRoot);
                            break;
                        case 19:
                            SurfaceCaveRoofAuditor.AuditAndStrip(ctx.CaveRoot, ctx.Ground, out _);
                            break;
                    }
                }
                else if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                {
                    if (sub % 2 == 0)
                        CaveBuildAutomatedValidation.RunFinalProbe(ctx.CaveRoot);
                    else if (ctx.Ground != null)
                        SurfaceCaveRoofAuditor.AuditAndStrip(ctx.CaveRoot, ctx.Ground, out _);
                }

                return;
            }

            switch (sub)
            {
                case 0:
                    quality = RegradeAndExportForCursorInvoke(
                        ctx.CaveRoot, ctx.Ground, ctx.Request, ctx.Report, quality, "post_meat_loop");
                    break;
                case 1:
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        CaveCompactLayerPurge.PurgeShellLayersOnly(ctx.CaveRoot);
                    break;
                case 2:
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        CaveEnclosureShellBuilder.HideRoutePlatformSlabs(ctx.CaveRoot);
                    break;
                case 3:
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        CaveAdventureVisualPass.Apply(ctx.CaveRoot);
                    break;
                case 4:
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        EnsureGameplaySpawns(ctx.CaveRoot, ctx.Ground);
                    break;
                case 5:
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                    {
                        CaveFloorSafetyUtility.EnsureRouteTerrainPlayCollider(ctx.CaveRoot);
                        CaveSpawnTeleportAuthority.ApplyMainAreaTeleportSpawn(ctx.CaveRoot, ctx.Request);
                    }

                    break;
                case 6:
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                    {
                        CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(ctx.CaveRoot);
                        if (!CaveAdventurePlayabilityPipeline.CheckSpawnReachability(ctx.CaveRoot))
                        {
                            Debug.LogWarning(
                                "[CaveBuild] Spawn reachability still low after quality ladder.");
                        }

                        quality = RegradeAndExportForCursorInvoke(
                            ctx.CaveRoot, ctx.Ground, ctx.Request, ctx.Report, quality, "post_adventure_passes");
                    }

                    if (ctx.Meat != null)
                        ctx.Meat.Quality = quality;
                    break;
                case 7:
                    if (CaveGeometryPaths.IsAdventureCave(ctx.CaveRoot))
                        CaveBuildAutomatedValidation.RunFinalProbe(ctx.CaveRoot);
                    break;
            }
        }

        static void RunQueuedResearchStep(QueuedPipelineContext ctx, int sub)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.runPostBuildResearchPhase)
                return;

            CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.Research);
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);

            var baseSub = Mathf.Min(sub / 3, 3);
            var quality = ctx.Meat?.Quality;
            switch (baseSub)
            {
                case 0:
                    if (sub % 3 == 0)
                        CaveBuildResearchPhase.RunAnalyzeNeeds(quality, ctx.CaveRoot, ctx.Ground);
                    break;
                case 1:
                    if (sub % 3 == 0 && quality != null)
                        CaveBuildResearchPhase.RunCatalogRefresh(quality, ctx.CaveRoot, ctx.Ground);
                    break;
                case 2:
                    if (sub % 3 == 0)
                        CaveBuildResearchPhase.RunOnlineEnrichment(quality, ctx.CaveRoot, ctx.Ground);
                    break;
                case 3:
                    if (sub % 3 == 0)
                    {
                        CaveBuildResearchPhase.RunPersistPhaseSummary(quality);
                        if (ctx.Request != null)
                        {
                            CaveBuildHelperScriptOrchestrator.Queue(
                                CaveBuildHelperScriptOrchestrator.Moment.CavePostBuildResearch,
                                CaveBuildHelperScriptOrchestrator.MakeContext(ctx.Request));
                        }
                    }

                    break;
            }
        }

        static void RunQueuedAaaManifest(QueuedPipelineContext ctx)
        {
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);

            Progress(ctx.ShowProgress, 39, "Commercial production manifest (100pt)…");
            var quality = ctx.Meat?.Quality;
            var production = CaveBuildCommercialProductionGrader.Grade(
                ctx.CaveRoot, ctx.Ground, ctx.Request, ctx.Report, quality);
            if (ctx.Report != null)
            {
                ctx.Report.Message +=
                    $" Production checklist: {production.LetterGrade} ({production.OverallScore}/100).";
            }
        }

        static void RunQueuedFinishReport(QueuedPipelineContext ctx)
        {
            if (ctx.CaveRoot == null)
                ResolveQueuedCaveRoot(ctx);

            var quality = ctx.Meat?.Quality;
            Progress(ctx.ShowProgress, 40, "Finalize build report…");
            if (ctx.CaveRoot != null && ctx.Ground != null && ctx.Ground.HasAnchor)
            {
                CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(
                    ctx.CaveRoot, ctx.Ground, out var burialMsg);
                if (!string.IsNullOrEmpty(burialMsg))
                    Debug.Log("[CaveBuild] Final burial: " + burialMsg);
                SurfaceCaveRoofAuditor.AuditAndStrip(ctx.CaveRoot, ctx.Ground, out var roofMsg);
                if (!string.IsNullOrEmpty(roofMsg))
                    Debug.Log("[CaveBuild] Final roof audit: " + roofMsg);
            }

            if (ctx.CaveRoot != null)
                ctx.Report.MinableCount = ctx.CaveRoot.GetComponentsInChildren<MinableRock>(true).Length;

            if (quality != null)
            {
                if (CaveBuildAaaProductionBootstrap.IsFullProductionBuild(
                        ctx.Request.SurfaceScope, ctx.Request.UseLayoutPrototype))
                {
                    CaveBuildAaaProductionBootstrap.OnFullProductionBuildFinished(
                        ctx.Request.Seed, quality.OverallScore, quality.LetterGrade);
                }
                else
                {
                    CaveBuildLadderMetrics.RecordBuildGrade(quality.OverallScore, quality.LetterGrade);
                    CaveBuildPhaseContractRegistry.MarkRungComplete(
                        CaveBuildPhaseContractRegistry.RungPolish,
                        ctx.Request.Seed);
                }

                ctx.Report.QualityScore = quality.OverallScore;
                ctx.Report.QualityLetter = quality.LetterGrade;
                ctx.Report.QualityAcceptable = quality.BuildAcceptable;
                var targetNote = quality.BuildAcceptable
                    ? "TARGET MET"
                    : $"below {CaveBuildQualityRubric.TargetGrade} ({CaveBuildQualityRubric.TargetOverallScore}+)";
                ctx.Report.Message =
                    $"Complete cave in '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'. " +
                    $"Grade {quality.LetterGrade} ({quality.OverallScore}/100) {targetNote}. Seed {ctx.Request.Seed}. " +
                    ctx.Report.SeamlessQuality + " " + ctx.Report.Message;

                if (!quality.BuildAcceptable)
                {
                    Debug.LogWarning(
                        $"[CaveBuild] Build finished below {CaveBuildQualityRubric.TargetGrade}. " +
                        $"Open {quality.ExportPath} for failing stages, then rebuild.");
                }

                if (ctx.CaveRoot != null)
                {
                    var settings = CaveBuildCursorSettings.LoadOrCreate();
                    settings.LoadFromPrefs();
                    if (!settings.enableAutonomousUntilShip)
                    {
                        CaveBuildCursorAgentBridge.TryAutoInvokeAfterBuildComplete(
                            quality, ctx.CaveRoot, ctx.Ground, afterPostBuildPasses: true);
                    }
                    else
                    {
                        Debug.Log(
                            "[CaveBuild] Post-build Cursor workflow deferred — autonomous-until-Ship loop runs at finish dialog.");
                    }
                }
            }

            FinishQueued(ctx.Report);
        }
    }
}
