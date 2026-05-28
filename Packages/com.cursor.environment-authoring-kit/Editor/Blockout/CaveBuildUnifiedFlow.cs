using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Documents and gates the single operator entry: Build Complete Cave Level.
    /// Other types in this folder are implementation details, not separate workflows.
    /// </summary>
    public static class CaveBuildUnifiedFlow
    {
        public const int QueuedPipelineStepCount = CaveBuildQueuedPipelineSchedule.Total;

        public const string PrimaryMenuPath = CaveBuildMenuPaths.BuildComplete;

        public static readonly string FlowSummary =
            "BUILD SCOPES (choose menu):\n" +
            "• Build Complete Cave Level — surface world + underground cave (FullWorld)\n" +
            "• Build Surface World Only — open sky, trails, water, mountains, cave markers (no cave rebuild)\n" +
            "• Build Cave Only — underground from existing surface opening markers\n\n" +
            "ONE MENU (FullWorld) — Build Complete Cave Level — everything below runs automatically:\n\n" +
            "• Automated bootstrap: reliable preset, preflight MD, enhancements (45 phases), DEM supersample\n" +
            "• Radial surface from tagged Ground (roads, trails, ponds, river basins, mouth markers)\n" +
            "• FullWorld: Florida LiDAR + terrain from tagged Ground first, then cave geometry (mouth aligns down)\n" +
            "• Surface: phased steps (LiDAR stamp, trails, NavMesh) — additive when world already exists\n" +
            "• Pre-build readiness ladder (reloop until 88+; Cursor after local retries when enabled)\n" +
            "• " + QueuedPipelineStepCount +
            "-step queued pipeline (validate+research → geo → playability → world → meat → finalize)\n" +
            "• End: CaveBuildCompletionReadout.md + CaveBuildCompletionContract.json\n" +
            "• Surface walk-in entrance at ground level (mouth pad + stepped descent into route)\n" +
            "• Validation bot: route probe + visual shell + invisible colliders + entrance + combat (paced sleeps)\n" +
            "• Mob spawns: colored capsule markers, mixed Aggressive/Defensive/Passive, CaveEnemy prefab + Animator\n" +
            "• Targeted fixes only — does not rebuild accurate geometry when checks pass\n" +
            "• World grounding: FinalizeGroundPlacement then auto-lock world XZ (meat loop never re-seats layout)\n" +
            "• Reports: CaveBuildQualityReport.json, CaveBuildCompletionReadout.md, route/combat probes\n\n" +
            "• One Start dialog, one Finish dialog with file:// links (no mid-build popups)\n\n" +
            "Optional (off by default in Cave Build Cursor Settings):\n" +
            "• Cursor after build, auto-rebuild, batch mode, research invoke\n\n" +
            "Repair menus under Cave Build/Repair Only are for broken scenes — not part of normal build.";

        public const string IntegratedStepsLog =
            "[CaveBuild] Integrated in Build Complete Cave: pre-build gate, queued pipeline, " +
            "surface walk-in entrance, validation bot (5 paced checks incl. combat), marker restore, " +
            "FinalizeGroundPlacement, ground lock for meat loop, surface spawn at mouth, " +
            "grading export, loop guard (no auto-rebuild after full pipeline).";

        public const string RepairMenusHint =
            "Repair-only menus (Reset Ground, Fix Spawn, etc.) are NOT required after a full build — " +
            "use only if you cancelled mid-build or hand-edited the scene.";

        /// <summary>Runs in-editor readiness export, then Cursor pre-build (like post-build). Returns true when geometry may run now.</summary>
        public static bool TryRunPreBuildPhase(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            int layoutSeed,
            bool layoutPrototype,
            bool skipPreBuild,
            bool skipDialogs,
            bool hideLegacyBlockout,
            out CaveBuildPreBuildReport report,
            CaveLayoutRoll layoutRoll = null)
        {
            report = null;
            if (layoutPrototype || skipPreBuild)
                return true;

            report = CaveBuildPreBuildLadder.Run(ground, request, layoutPrototype, layoutSeed);
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (!CaveBuildCursorSettings.HasCredentialsForActiveProvider() && !settings.enforcePreBuildGate)
            {
                Debug.Log(
                    "[CaveBuild] No AI credentials — pre-build advisory only; continuing procedural pipeline.");
                return true;
            }

            var useCursorPreBuild = settings.autoInvokePreBuildWorkflow && CaveBuildCursorAgentBridge.HasApiKey;
            var deferCursorToReloop = CaveBuildPreBuildReloop.ShouldReloop(settings);

            if (useCursorPreBuild && !deferCursorToReloop)
            {
                CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                    request, ground, CaveBuildSurfaceCompletionGate.IsSurfaceWorldGeneratorFinished ||
                    CaveBuildSurfaceCompletionGate.IsCompleteForSeed(request));

                if (CaveBuildSurfaceCompletionGate.MustFinishSurfaceBeforeCave(request) &&
                    !CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(request, ground))
                {
                    CaveBuildSurfaceCompletionGate.LogHandoffBlocker(request, ground, "pre-build Cursor");
                    Debug.LogWarning(
                        "[CaveBuild] Pre-build blocked — above-ground terrain must finish first (run surface step).");
                    return false;
                }

                CaveBuildPendingGeometryBuild.QueueFromBuilder(
                    ground,
                    request,
                    hideLegacyBlockout,
                    skipDialogs,
                    layoutRoll);
                if (!CaveBuildPreBuildWorkflow.Begin(report, ground, request))
                {
                    Debug.LogWarning(
                        "[CaveBuild] Pre-build Cursor workflow did not start — falling back to sync gate only.");
                    CaveBuildPendingGeometryBuild.Clear();
                    useCursorPreBuild = false;
                }
                else
                {
                    Debug.Log(
                        "[CaveBuild] Pre-build Cursor workflow started (same pattern as post-build). " +
                        "Geometry runs after workflow completes.");
                    if (!skipDialogs)
                    {
                        CaveBuildDialogPolicy.Notify(
                            "Pre-Build — Cursor Running",
                            "Cursor is running the pre-build workflow (research → plan → compile → readiness ladder).\n\n" +
                            "Cave geometry will start automatically when it finishes (if readiness passes).");
                    }
                    else
                    {
                        Debug.Log(
                            "[CaveBuild] Pre-build Cursor workflow started — geometry runs when workflow completes.");
                    }

                    return false;
                }
            }

            if (report.BuildAcceptable)
            {
                Debug.Log(
                    $"[CaveBuild] Pre-build sync PASS ({report.LetterGrade} {report.OverallScore}/100) — continuing.");
                return true;
            }

            if (CaveBuildPreBuildReloop.ShouldReloop(settings))
            {
                Debug.LogWarning(
                    $"[CaveBuild] Pre-build not acceptable ({report.LetterGrade} {report.OverallScore}/100, " +
                    $"target {CaveBuildPreBuildLadder.TargetOverallScore}+) — startup will reloop the gate.");
                return false;
            }

            if (!settings.enforcePreBuildGate)
            {
                Debug.LogWarning(
                    "[CaveBuild] Pre-build sync failed but enforcePreBuildGate is off — continuing build.");
                return true;
            }

            var gateMsg =
                $"Pre-build BLOCKED (sync grade {report.LetterGrade} {report.OverallScore}/100).\n\n" +
                $"Enable preBuildReloopUntilPass + autoInvokePreBuildWorkflow + CURSOR_API_KEY,\n" +
                $"or fix issues in {CaveBuildPreBuildLadder.ReportPath}.";
            if (!skipDialogs)
                CaveBuildCompletionSummary.ShowBlocked(gateMsg, CaveBuildPreBuildLadder.ReportPath);
            else
                Debug.LogWarning("[CaveBuild] " + gateMsg);
            return false;
        }

        /// <summary>World Generator path: Cursor pre-build then pipeline on root.</summary>
        public static bool TryRunPreBuildPhaseForWorldGen(
            Transform rootAnchor,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            int layoutSeed,
            XROptimizationProfile xrProfile,
            out CaveBuildPreBuildReport report)
        {
            report = CaveBuildPreBuildLadder.Run(ground, request, false, layoutSeed);
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                request, ground,
                CaveBuildSurfaceCompletionGate.IsSurfaceWorldGeneratorFinished ||
                CaveBuildSurfaceCompletionGate.IsCompleteForSeed(request));

            if (CaveBuildSurfaceCompletionGate.MustFinishSurfaceBeforeCave(request) &&
                !CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(request, ground))
            {
                CaveBuildSurfaceCompletionGate.LogHandoffBlocker(request, ground, "Generate World pre-build");
                Debug.LogWarning(
                    "[CaveBuild] Generate World: run surface build before cave (FullWorld surface not ready).");
                return false;
            }

            if (!settings.autoInvokePreBuildWorkflow || !CaveBuildCursorAgentBridge.HasApiKey)
            {
                if (!report.BuildAcceptable && settings.enforcePreBuildGate)
                {
                    Debug.LogWarning("[CaveBuild] Generate World cave blocked by pre-build sync gate.");
                    return false;
                }

                return true;
            }

            CaveBuildPendingGeometryBuild.QueueFromWorldGenerator(rootAnchor, ground, request, xrProfile);
            if (!CaveBuildPreBuildWorkflow.Begin(report, ground, request))
            {
                CaveBuildPendingGeometryBuild.Clear();
                return !settings.enforcePreBuildGate || report.BuildAcceptable;
            }

            Debug.Log("[CaveBuild] Generate World: deferred until pre-build Cursor workflow completes.");
            return false;
        }

        public static void LogFlowStart(string sceneName, bool layoutPrototype)
        {
            if (layoutPrototype)
                return;

            Debug.Log(
                $"[CaveBuild] ═══ Build Complete Cave — '{sceneName}' ═══\n" +
                IntegratedStepsLog + "\n" +
                RepairMenusHint);
        }

        public static void LogFlowComplete(LavaTubeCaveBuildReport report)
        {
            var grade = report != null ? $"{report.QualityLetter} ({report.QualityScore}/100)" : "n/a";
            Debug.Log(
                $"[CaveBuild] ═══ Build Complete Cave finished — grade {grade} ═══\n" +
                RepairMenusHint);
        }
    }
}
