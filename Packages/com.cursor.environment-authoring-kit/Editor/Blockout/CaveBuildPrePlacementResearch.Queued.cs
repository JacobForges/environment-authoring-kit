#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static partial class CaveBuildPrePlacementResearch
    {
        /// <summary>
        /// Paced pre-placement research (6 queue steps) — never blocks startup after layout roll in one frame.
        /// Uses <see cref="CaveBuildResearchExecutionBrief"/> + ResearchCache per research-workflow.md.
        /// </summary>
        public static void QueueRunBeforeAnyPlacement(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool additiveSurface,
            Action<bool, string> onComplete)
        {
            if (onComplete == null)
                return;

            if (request != null && IsGatePassedForSeed(request.Seed))
            {
                onComplete(true, "Pre-placement research already completed for this seed.");
                return;
            }

            if (request != null &&
                CaveBuildSessionPreset.AllowProceduralTerrainWithoutResearch &&
                !CaveBuildResearchCacheBridge.HasUsableLocalResearchCache() &&
                TryPassProceduralResearchFallback(
                    additiveSurface,
                    request.Seed,
                    "skipped network research — fresh clone",
                    out var proceduralMsg))
            {
                onComplete(true, proceduralMsg);
                return;
            }

            var state = new QueuedResearchState
            {
                Request = request,
                AdditiveSurface = additiveSurface,
                SubStep = 0,
                OnComplete = onComplete,
            };

            CaveBuildEditorLog.LogSurface(
                "[Startup] Pre-placement research queued (9 paced steps — cache, hillshades, brief, unified manifest, research agent prompt, action plan, active prompt).",
                forceUnityConsole: true);

            ScheduleQueuedResearchSubStep(state);
        }

        sealed class QueuedResearchState
        {
            public WorldGenerationRequest Request;
            public bool AdditiveSurface;
            public int SubStep;
            public string AccumulatedMessage = string.Empty;
            public Action<bool, string> OnComplete;
        }

        static void ScheduleQueuedResearchSubStep(QueuedResearchState state)
        {
            var label = state.SubStep < QueuedResearchStepLabels.Length
                ? QueuedResearchStepLabels[state.SubStep]
                : "research";
            CaveBuildActionPacing.ScheduleHeavyChain(
                () => RunQueuedResearchSubStep(state),
                CaveBuildPipelineDomains.SurfaceQueueLabel(
                    $"pre-placement research {state.SubStep + 1}/{QueuedResearchStepCount} — {label}"));
        }

        static void RunQueuedResearchSubStep(QueuedResearchState state)
        {
            CaveBuildProgressUI.ShowThrottled(
                "Environment Kit",
                $"[Startup] Research {state.SubStep + 1}/{QueuedResearchStepCount}: {QueuedResearchStepLabels[state.SubStep]}…",
                0.32f + 0.02f * (state.SubStep / (float)QueuedResearchStepCount));

            var ok = RunQueuedResearchStep(
                state.SubStep,
                state.Request,
                state.AdditiveSurface,
                ref state.AccumulatedMessage,
                out var stepMsg,
                out var awaitingTsx);

            if (awaitingTsx)
                return;

            if (!ok)
            {
                EditorUtility.ClearProgressBar();
                state.OnComplete(false, stepMsg);
                return;
            }

            state.SubStep++;
            if (state.SubStep < QueuedResearchStepCount)
            {
                ScheduleQueuedResearchSubStep(state);
                return;
            }

            EditorUtility.ClearProgressBar();
            state.OnComplete(true, stepMsg);
        }

        public const int QueuedResearchStepCount = 9;

        public const int QueuedResearchSkipCheck = 0;
        public const int QueuedResearchCache = 1;
        public const int QueuedResearchHillshades = 2;
        public const int QueuedResearchCatalog = 3;
        public const int QueuedResearchBrief = 4;
        public const int QueuedResearchUnifiedPrompt = 5;
        public const int QueuedResearchAgentPrompt = 6;
        public const int QueuedResearchActionPlan = 7;
        public const int QueuedResearchActivePrompt = 8;

        /// <summary>
        /// When ResearchCache is on disk, skip blocking tsx sync steps and run prompt exports only during validate.
        /// </summary>
        public static int ResolveValidateResearchStartSubStep(int seed)
        {
            if (IsGatePassedForSeed(seed))
                return QueuedResearchSkipCheck;

            if (!CaveBuildResearchCacheBridge.ShouldUseOnDiskResearchFastPath())
                return QueuedResearchSkipCheck;

            if (CaveBuildPromptExportSession.HasValidateResearchPromptArtifactsOnDisk())
            {
                Debug.Log(
                    "[CaveBuild] Validate research fast path — ResearchCache + prompt MD/JSON on disk; " +
                    "only gate write (no tsx).");
                return QueuedResearchActivePrompt;
            }

            Debug.Log(
                "[CaveBuild] Validate research fast path — on-disk ResearchCache OK; " +
                "prompt exports only (tsx skipped when outputs exist).");
            return QueuedResearchUnifiedPrompt;
        }

        public static readonly string[] QueuedResearchStepLabels =
        {
            "research gate check",
            "research cache sync",
            "Florida hillshade sync",
            "research catalog export",
            "execution brief export",
            "unified JSON manifest (light)",
            "consolidated research agent prompt",
            "research action plan",
            "active phase prompt (fast)",
        };

        /// <summary>One paced editor-queue slice of pre-placement research (never blocks the whole gate in one frame).</summary>
        public static bool RunQueuedResearchStep(
            int subStep,
            WorldGenerationRequest request,
            bool additiveSurface,
            ref string accumulatedMessage,
            out string message,
            out bool awaitingTsx)
        {
            message = string.Empty;
            awaitingTsx = false;
            var seed = request?.Seed ?? 0;
            const string rung = "terrain_integration";

            switch (subStep)
            {
                case QueuedResearchSkipCheck:
                    if (IsGatePassedForSeed(seed))
                    {
                        message = "Pre-placement research already completed for this seed.";
                        return true;
                    }

                    var syncHint = CaveBuildResearchCacheBridge.ShouldUseOnDiskResearchFastPath()
                        ? "Research validate — on-disk cache fast path (no network tsx)…"
                        : "Syncing ResearchCache + Florida hillshades + execution brief…";
                    CaveBuildRunStatusPublisher.SetResearchPhase(
                        syncHint,
                        CaveBuildRunStatusPublisher.ResearchGateState.InProgress);
                    return true;

                case QueuedResearchCache:
                {
                    if (LavaTubeCaveBuildPipeline.IsPhasedBuildActive &&
                        CaveBuildResearchCacheBridge.HasUsableLocalResearchCache())
                    {
                        message =
                            "Skipped research-cache-sync during validate — on-disk ResearchCache OK " +
                            "(no blocking tsx on main thread).";
                        CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                        Append(ref accumulatedMessage, message);
                        return true;
                    }

                    if (CaveBuildResearchCacheBridge.ShouldSkipCacheTsxSync())
                    {
                        message =
                            "Skipped research-cache-sync.ts — usable ResearchCache/index.json on disk " +
                            "(set CAVE_FORCE_RESEARCH_SYNC=1 to force network sync).";
                        CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                        Append(ref accumulatedMessage, message);
                        return true;
                    }

                    var pullImages = !CaveBuildResearchCacheBridge.HasUsableLocalResearchCache();
                    CaveBuildRunStatusPublisher.SetSubOperation(
                        "research cache sync",
                        pullImages
                            ? "metadata + missing preview images (HTTP)…"
                            : "metadata only (reusing on-disk cache + images)…");
                    if (!CaveBuildResearchCacheBridge.SyncCache(rung, pullImages, out message))
                    {
                        if (!CaveBuildResearchCacheBridge.HasUsableLocalResearchCache())
                        {
                            if (TryPassProceduralResearchFallback(additiveSurface, seed, message, out message))
                                return true;

                            WriteGate(false, message, additiveSurface, seed);
                            CaveBuildRunStatusPublisher.SetResearchPhase(
                                message,
                                CaveBuildRunStatusPublisher.ResearchGateState.Failed);
                            return false;
                        }

                        message += " | Using existing ResearchCache on disk (offline/degraded mode).";
                        Debug.LogWarning("[CaveBuild] Pre-placement research: " + message);
                    }

                    Append(ref accumulatedMessage, message);
                    CaveBuildRunStatusPublisher.ClearSubOperation();
                    return true;
                }

                case QueuedResearchHillshades:
                    if (CaveBuildResearchCacheBridge.ShouldSkipHillshadeTsxSync())
                    {
                        message = "Skipped Florida hillshade tsx — on-disk hillshade PNG(s) present.";
                        CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                        Append(ref accumulatedMessage, message);
                        return true;
                    }

                    CaveBuildRunStatusPublisher.SetSubOperation(
                        "Florida hillshade sync",
                        "county bare-earth PNGs (Bay, Washington, Jackson, Calhoun)…");
                    if (!CaveBuildResearchCacheBridge.SyncFloridaHillshades(out message))
                    {
                        if (!CaveBuildResearchCacheBridge.HasLocalFloridaHillshades())
                        {
                            if (TryPassProceduralResearchFallback(
                                    additiveSurface,
                                    seed,
                                    accumulatedMessage + " | hillshades: " + message,
                                    out message))
                                return true;

                            WriteGate(false, accumulatedMessage + " | hillshades: " + message, additiveSurface, seed);
                            CaveBuildRunStatusPublisher.SetResearchPhase(
                                message,
                                CaveBuildRunStatusPublisher.ResearchGateState.Failed);
                            return false;
                        }

                        message = "Florida hillshade sync failed — reusing on-disk hillshade PNG(s).";
                        Debug.LogWarning("[CaveBuild] Pre-placement research: " + message);
                    }

                    Append(ref accumulatedMessage, message);
                    return true;

                case QueuedResearchCatalog:
                    if (CaveBuildResearchCacheBridge.ShouldSkipCatalogTsxSync())
                    {
                        message = "Skipped export-research-catalog.ts — seed catalog on disk + ResearchCache OK.";
                        CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                        Append(ref accumulatedMessage, message);
                        return true;
                    }

                    if (!CaveBuildResearchCacheBridge.SyncResearchCatalog(out message))
                    {
                        if (!CaveBuildResearchCacheBridge.HasUsableLocalResearchCache())
                        {
                            if (TryPassProceduralResearchFallback(
                                    additiveSurface,
                                    seed,
                                    accumulatedMessage + " | catalog: " + message,
                                    out message))
                                return true;

                            WriteGate(false, accumulatedMessage + " | catalog: " + message, additiveSurface, seed);
                            CaveBuildRunStatusPublisher.SetResearchPhase(
                                message,
                                CaveBuildRunStatusPublisher.ResearchGateState.Failed);
                            return false;
                        }

                        message = "Research catalog export failed — index.json still usable.";
                        Debug.LogWarning("[CaveBuild] Pre-placement research: " + message);
                    }

                    Append(ref accumulatedMessage, message);
                    return true;

                case QueuedResearchBrief:
                    if (CaveBuildResearchCacheBridge.ShouldSkipBriefTsxSync())
                    {
                        message = "Skipped execution-brief tsx — brief JSON already on disk.";
                        CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                    }
                    else if (!CaveBuildResearchCacheBridge.SyncResearchExecutionBrief(rung, out message))
                    {
                        Debug.LogWarning("[CaveBuild] Research execution brief: " + message);
                    }

                    Append(ref accumulatedMessage, message);
                    return true;

                case QueuedResearchUnifiedPrompt:
                    if (TrySkipValidatePromptTsx(subStep, ref accumulatedMessage, out message))
                        return true;
                    if (TryBeginValidatePromptTsx(
                            subStep,
                            ref accumulatedMessage,
                            () =>
                            {
                                CaveBuildRunStatusPublisher.SetSubOperation(
                                    "unified JSON manifest",
                                    "light catalog (skip rebuild if fresh)…");
                                CaveBuildEditorLog.LogCave(
                                    "Unified JSON manifest (light — skip rebuild if fresh)…",
                                    forceUnityConsole: true);
                                return CaveBuildPhasePromptBridge.TryBeginRunTsx(
                                    "generate-unified-agent-prompt.ts",
                                    null,
                                    null,
                                    120_000,
                                    (ok, msg) =>
                                    {
                                        if (!ok)
                                            Debug.LogWarning("[CaveBuild] Unified prompt export: " + msg);
                                        LavaTubeCaveBuildPipeline.CompleteValidateResearchTsx(ok, msg, subStep);
                                    },
                                    "CAVE_ACTIVE_PHASE=research",
                                    CaveBuildPromptExportSession.ShouldSkipManifestRebuild(seed)
                                        ? "CAVE_SKIP_MANIFEST_REBUILD=1"
                                        : null);
                            },
                            out awaitingTsx))
                        return true;

                    if (!CaveBuildPhasePromptBridge.ExportUnifiedAgentContext("research", out message))
                        Debug.LogWarning("[CaveBuild] Unified prompt export: " + message);
                    Append(ref accumulatedMessage, message);
                    return true;

                case QueuedResearchAgentPrompt:
                    if (TrySkipValidatePromptTsx(subStep, ref accumulatedMessage, out message))
                        return true;
                    if (TryBeginValidatePromptTsx(
                            subStep,
                            ref accumulatedMessage,
                            () =>
                            {
                                CaveBuildRunStatusPublisher.SetSubOperation(
                                    "research agent prompt",
                                    "ONE consolidated MD (max 5 images)…");
                                CaveBuildEditorLog.LogCave(
                                    "Consolidated research agent prompt (ONE file, max 5 images)…",
                                    forceUnityConsole: true);
                                return CaveBuildPhasePromptBridge.TryBeginRunTsx(
                                    "generate-research-agent-prompt.ts",
                                    $"--rung={rung}",
                                    null,
                                    90_000,
                                    (ok, msg) =>
                                    {
                                        if (!ok)
                                            Debug.LogWarning("[CaveBuild] Research agent prompt: " + msg);
                                        LavaTubeCaveBuildPipeline.CompleteValidateResearchTsx(ok, msg, subStep);
                                    },
                                    "CAVE_ACTIVE_PHASE=research",
                                    $"CAVE_ACTIVE_RUNG={rung}");
                            },
                            out awaitingTsx))
                        return true;

                    if (!CaveBuildUnifiedPromptBridge.ExportResearchAgentPrompt("research", rung, out message))
                        Debug.LogWarning("[CaveBuild] Research agent prompt: " + message);
                    Append(ref accumulatedMessage, message);
                    return true;

                case QueuedResearchActionPlan:
                    if (TrySkipValidatePromptTsx(subStep, ref accumulatedMessage, out message))
                        return true;
                    if (TryBeginValidatePromptTsx(
                            subStep,
                            ref accumulatedMessage,
                            () =>
                            {
                                CaveBuildRunStatusPublisher.SetSubOperation("research action plan", "tsx export…");
                                CaveBuildEditorLog.LogCave("Research action plan export…", forceUnityConsole: true);
                                return CaveBuildPhasePromptBridge.TryBeginRunTsx(
                                    "generate-research-action-plan.ts",
                                    null,
                                    null,
                                    90_000,
                                    (ok, msg) =>
                                    {
                                        if (!ok)
                                            Debug.LogWarning("[CaveBuild] Research action plan: " + msg);
                                        LavaTubeCaveBuildPipeline.CompleteValidateResearchTsx(ok, msg, subStep);
                                    },
                                    $"CAVE_ACTIVE_PHASE=research",
                                    $"CAVE_QUEUED_STEP=0",
                                    $"CAVE_BUILD_SEED={seed}");
                            },
                            out awaitingTsx))
                        return true;

                    if (!CaveBuildPhasePromptBridge.ExportResearchActionPlan("research", 0, seed, out message))
                        Debug.LogWarning("[CaveBuild] Research action plan: " + message);
                    Append(ref accumulatedMessage, message);
                    return true;

                case QueuedResearchActivePrompt:
                {
                    if (CaveBuildPromptExportSession.HasValidateResearchPromptArtifactsOnDisk() &&
                        !IsGatePassedForSeed(seed))
                    {
                        message =
                            "Research gate marked passed — prompt artifacts on disk (validate fast path).";
                        WriteGate(true, message, additiveSurface, seed);
                        CaveBuildRunStatusPublisher.SetResearchPhase(
                            message,
                            CaveBuildRunStatusPublisher.ResearchGateState.Passed);
                        CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                        Append(ref accumulatedMessage, message);
                        return true;
                    }

                    if (CaveBuildPromptExportSession.IsFresh(seed))
                    {
                        message = "Active phase prompt already fresh — skipped duplicate tsx.";
                        CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                    }
                    else
                    {
                        CaveBuildRunStatusPublisher.SetSubOperation(
                            "active phase prompt",
                            "fast tsx (links research MD, ≤3 JSON excerpts)…");
                        CaveBuildEditorLog.LogCave(
                            "Active phase prompt (fast — links research agent MD, small excerpts)…",
                            forceUnityConsole: true);
                        if (!CaveBuildPhasePromptBridge.ExportPhasePrompt(
                                "research",
                                rung,
                                -1,
                                0,
                                out message,
                                skipManifestRebuild: CaveBuildPromptExportSession.ShouldSkipManifestRebuild(seed)))
                        {
                            Debug.LogWarning("[CaveBuild] Active phase prompt: " + message);
                        }
                        CaveBuildPhasePromptBridge.ExportNextStepsAndDoNot("research", out _);
                        CaveBuildPromptExportSession.MarkFresh(seed);
                        CaveBuildEditorLog.LogCave("Active phase prompt export done.", forceUnityConsole: true);
                    }

                    WriteGateAfterActivePrompt(request, additiveSurface, ref accumulatedMessage, out message);
                    return true;
                }

                default:
                    message = "Unknown pre-placement research sub-step.";
                    return false;
            }
        }

        static void Append(ref string accumulated, string part)
        {
            if (string.IsNullOrEmpty(part))
                return;
            accumulated = string.IsNullOrEmpty(accumulated) ? part : accumulated + " | " + part;
        }

        static bool TrySkipValidatePromptTsx(int subStep, ref string accumulated, out string message)
        {
            message = string.Empty;
            if (!LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
                !CaveBuildPromptExportSession.HasValidateResearchPromptArtifactsOnDisk())
                return false;

            message =
                $"Skipped {QueuedResearchStepLabels[subStep]} tsx — prompt MD/JSON already on disk.";
            CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
            Append(ref accumulated, message);
            return true;
        }

        /// <summary>Called when validate fast-forwards past research tsx but gate JSON still needs passing bit.</summary>
        public static void WriteGateForValidateFastForward(int seed, bool additiveSurface)
        {
            var message =
                "Pre-placement research gate passed (validate fast-forward — prompt artifacts on disk).";
            WriteGate(true, message, additiveSurface, seed);
            CaveBuildRunStatusPublisher.SetResearchPhase(
                message,
                CaveBuildRunStatusPublisher.ResearchGateState.Passed);
            Debug.Log("[CaveBuild] " + message);
        }

        /// <summary>During validate, never block inside <see cref="EditorApplication.update"/> with sync node/tsx.</summary>
        static bool TryBeginValidatePromptTsx(
            int subStep,
            ref string accumulatedMessage,
            Func<bool> tryBegin,
            out bool awaitingTsx)
        {
            awaitingTsx = false;
            if (!LavaTubeCaveBuildPipeline.IsPhasedBuildActive)
                return false;

            if (!tryBegin())
                return false;

            awaitingTsx = true;
            return true;
        }
    }
}
#endif
