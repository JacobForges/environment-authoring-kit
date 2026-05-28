#if UNITY_EDITOR
using System;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Automated FullWorld: re-run pre-build gate after local fixes until BuildAcceptable (88+),
    /// then Cursor pre-build workflow when local attempts stall.
    /// </summary>
    public static class CaveBuildPreBuildReloop
    {
        public const int DefaultMaxLocalAttempts = 12;

        /// <summary>Automated production may continue cave pipeline at this score when reloop cannot reach 88.</summary>
        public const int ProductionContinueMinScore = 80;

        public enum PreBuildReloopResult
        {
            NotHandled,
            RetryScheduled,
            PassedAfterLocalFix,
            CursorDeferred,
            ProductionContinue,
            Blocked,
        }

        struct PendingReloopWork
        {
            public CaveBuildPreBuildReport Report;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public int LayoutSeed;
            public bool LayoutPrototype;
        }

        static int _attemptCount;
        static bool _cursorWorkflowQueued;
        static int _lastOverallScore = -1;
        static int _stagnantAttempts;
        static PendingReloopWork? _pendingReloop;
        static double _lastCompileDiagnosticsExportAt;

        public static int AttemptCount => _attemptCount;

        /// <summary>After the first reloop, skip heavy Editor.log parsing during ladder grading.</summary>
        public static bool UseFastCompileCapture => _attemptCount > 0;

        public static void ResetSession()
        {
            _attemptCount = 0;
            _cursorWorkflowQueued = false;
            _lastOverallScore = -1;
            _stagnantAttempts = 0;
            _pendingReloop = null;
        }

        public static bool IsEnabled()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            return ShouldReloop(settings);
        }

        public static bool ShouldReloop(CaveBuildCursorSettings settings = null)
        {
            settings ??= CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (settings.preBuildReloopUntilPass)
                return true;
            if (CaveBuildAutomatedFullWorldBootstrap.SessionActive)
                return true;
            return CaveBuildStartupCoordinator.IsActive;
        }

        public static int ResolveMaxLocalAttempts(CaveBuildCursorSettings settings)
        {
            if (settings == null)
                return DefaultMaxLocalAttempts;
            return settings.maxPreBuildReloopAttempts > 0
                ? settings.maxPreBuildReloopAttempts
                : DefaultMaxLocalAttempts;
        }

        /// <summary>
        /// Light-weight planning only — never blocks on tsx, AssetDatabase.Refresh, or shell rebuild here.
        /// </summary>
        public static PreBuildReloopResult TryPlanStartupFailure(
            CaveBuildPreBuildReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            int layoutSeed,
            bool layoutPrototype,
            CaveLayoutRoll roll,
            bool skipDialogs,
            bool hideLegacyBlockout,
            out string status,
            out CaveBuildPreBuildReport refreshedReport)
        {
            status = string.Empty;
            refreshedReport = report;

            if (report == null || layoutPrototype)
                return PreBuildReloopResult.NotHandled;

            if (report.BuildAcceptable)
            {
                status = "Pre-build already acceptable.";
                return PreBuildReloopResult.PassedAfterLocalFix;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!ShouldReloop(settings))
            {
                status = "Pre-build reloop disabled.";
                return PreBuildReloopResult.NotHandled;
            }

            if (CaveBuildPreBuildLadder.HasCriticalFailure(report))
            {
                Debug.LogWarning(
                    $"[CaveBuild] Pre-build critical rung below floor ({report.LetterGrade} {report.OverallScore}/100) — " +
                    "quick fixes run next editor tick (not on this frame).");
            }

            refreshedReport = report;
            TrackScoreStagnation(report.OverallScore);

            if (report.OverallScore >= ProductionContinueMinScore &&
                TryProductionContinue(report, out status))
                return PreBuildReloopResult.ProductionContinue;

            var max = ResolveMaxLocalAttempts(settings);
            var tryCursorEarly = _stagnantAttempts >= 2 &&
                settings.autoInvokePreBuildWorkflow &&
                CaveBuildCursorAgentBridge.HasApiKey;

            if (!tryCursorEarly && _attemptCount < max)
            {
                _attemptCount++;
                var grade = $"{report.LetterGrade} {report.OverallScore}/100";
                status =
                    $"Pre-build reloop {_attemptCount}/{max} — {grade} (target {CaveBuildPreBuildLadder.TargetOverallScore}+) — " +
                    "applying quick fixes next frame.";
                _pendingReloop = new PendingReloopWork
                {
                    Report = report,
                    Ground = ground,
                    Request = request,
                    LayoutSeed = layoutSeed,
                    LayoutPrototype = layoutPrototype,
                };
                CaveBuildStartupCoordinator.SchedulePreBuildReloopWork(status);
                Debug.LogWarning("[CaveBuild] " + status);
                return PreBuildReloopResult.RetryScheduled;
            }

            if (!_cursorWorkflowQueued &&
                settings.autoInvokePreBuildWorkflow &&
                CaveBuildCursorAgentBridge.HasApiKey)
            {
                if (TryStartCursorPreBuild(
                        report,
                        ground,
                        request,
                        roll,
                        skipDialogs,
                        hideLegacyBlockout,
                        max,
                        out status))
                    return PreBuildReloopResult.CursorDeferred;
            }

            if (TryProductionContinue(report, out status))
                return PreBuildReloopResult.ProductionContinue;

            status =
                $"Pre-build reloop exhausted ({_attemptCount} local attempts, " +
                $"{report.LetterGrade} {report.OverallScore}/100). " +
                $"Enable autoInvokePreBuildWorkflow + CURSOR_API_KEY or fix {CaveBuildPreBuildLadder.ReportPath}.";
            return PreBuildReloopResult.Blocked;
        }

        /// <summary>Runs on the next editor frame — quick fixes only, then re-queues the gate.</summary>
        public static void RunDeferredReloopFixes()
        {
            if (!_pendingReloop.HasValue)
            {
                CaveBuildStartupCoordinator.InvokePreBuildGateFromReloop();
                return;
            }

            var ctx = _pendingReloop.Value;
            _pendingReloop = null;

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    "[Startup] Pre-build quick fixes (non-blocking)…",
                    0.39f);
                TryApplyLocalFixes(ctx.Report, ctx.Ground, ctx.Request, ctx.LayoutSeed, quickOnly: true, out _);
                CaveBuildDeferredAssetRefresh.RequestRefresh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Pre-build deferred fixes failed: " + ex.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            CaveBuildStartupCoordinator.InvokePreBuildGateFromReloop();
        }

        public static bool TryProductionContinue(CaveBuildPreBuildReport report, out string status)
        {
            status = string.Empty;
            if (report == null || report.BuildAcceptable)
                return false;
            if (CaveBuildPreBuildLadder.HasCriticalFailure(report) &&
                !IsCompileOnlyCriticalFailure(report))
                return false;
            if (!CaveBuildAutomatedFullWorldBootstrap.SessionActive &&
                !CaveBuildStartupCoordinator.IsActive &&
                !LavaTubeCaveBuilder.IsBuildInProgress)
                return false;
            if (report.OverallScore < ProductionContinueMinScore)
                return false;

            status =
                $"Pre-build advisory continue ({report.LetterGrade} {report.OverallScore}/100, " +
                $"target {CaveBuildPreBuildLadder.TargetOverallScore}+) — automated FullWorld advances to cave pipeline. " +
                $"See {CaveBuildPreBuildLadder.ReportPath}.";
            Debug.LogWarning("[CaveBuild] " + status);
            return true;
        }

        static void TrackScoreStagnation(int overallScore)
        {
            if (_lastOverallScore < 0)
            {
                _lastOverallScore = overallScore;
                return;
            }

            if (overallScore <= _lastOverallScore + 1)
                _stagnantAttempts++;
            else
                _stagnantAttempts = 0;

            _lastOverallScore = overallScore;
        }

        static bool TryStartCursorPreBuild(
            CaveBuildPreBuildReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            CaveLayoutRoll roll,
            bool skipDialogs,
            bool hideLegacyBlockout,
            int maxLocalAttempts,
            out string status)
        {
            status = string.Empty;
            CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                request,
                ground,
                CaveBuildSurfaceCompletionGate.IsSurfaceWorldGeneratorFinished ||
                CaveBuildSurfaceCompletionGate.IsCompleteForSeed(request));

            if (CaveBuildSurfaceCompletionGate.MustFinishSurfaceBeforeCave(request) &&
                !CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(request, ground))
            {
                status = "Pre-build Cursor deferred — surface world must finish first.";
                Debug.LogWarning("[CaveBuild] " + status);
                return false;
            }

            CaveBuildPendingGeometryBuild.QueueFromBuilder(
                ground,
                request,
                hideLegacyBlockout,
                skipDialogs,
                roll);
            if (!CaveBuildPreBuildWorkflow.Begin(report, ground, request))
            {
                CaveBuildPendingGeometryBuild.Clear();
                status = "Pre-build Cursor workflow did not start.";
                return false;
            }

            _cursorWorkflowQueued = true;
            status =
                $"Pre-build score stalled ({report.LetterGrade} {report.OverallScore}/100 after {_attemptCount}/{maxLocalAttempts} local tries) — " +
                "Cursor pre-build workflow started; cave geometry runs when it completes.";
            CaveBuildRunStatusPublisher.SetPhase("startup", status);
            Debug.Log("[CaveBuild] " + status);
            if (!skipDialogs)
            {
                CaveBuildDialogPolicy.Notify(
                    "Pre-Build — Cursor Running",
                    "Local pre-build retries did not reach 88+. Cursor is running the pre-build workflow.\n\n" +
                    "Cave geometry will start automatically when readiness passes.");
            }

            return true;
        }

        static bool TryApplyLocalFixes(
            CaveBuildPreBuildReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            int layoutSeed,
            bool quickOnly,
            out string notes)
        {
            var sb = new StringBuilder();
            var applied = false;
            if (report == null)
            {
                notes = string.Empty;
                return false;
            }

            foreach (var rung in CaveBuildPreBuildLadder.GetFailingRungs(report))
            {
                switch (rung)
                {
                    case "prior_cave_state":
                        if (!quickOnly && TryFixPriorCaveState(ground, sb))
                            applied = true;
                        else if (quickOnly)
                            sb.AppendLine("prior_cave_state: deferred (heavy) — runs at cave geometry if needed.");
                        break;
                    case "research_manifest":
                        if (TryFixResearchManifest(report, sb, syncCatalog: !quickOnly))
                            applied = true;
                        break;
                    case "scene_portal":
                        if (TryFixScenePortal(sb))
                            applied = true;
                        break;
                    case "compile_gate":
                        if (TryFixCompileGate(sb))
                            applied = true;
                        break;
                }
            }

            notes = sb.Length > 0 ? sb.ToString().TrimEnd() : string.Empty;
            if (!string.IsNullOrEmpty(notes))
                Debug.Log("[CaveBuild] Pre-build reloop local fixes:\n" + notes);
            return applied;
        }

        static bool TryFixPriorCaveState(SceneGroundInfo ground, StringBuilder sb)
        {
            if (!ground.HasAnchor)
                return false;

            var cave = ground.Anchor.Find(CaveGeometryPaths.CaveSystemRootName);
            if (cave == null)
                cave = ground.Anchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            if (cave == null)
                return false;

            var removed = CaveCompactLayerPurge.Purge(cave);
            var rebuilt = 0;
            var meta = cave.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
                rebuilt = CaveCompactRouteUtility.RebuildCompactRouteShell(cave, layout, meta.seed);
            }

            CaveAdventureVisualPass.Apply(cave);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(cave);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            sb.AppendLine(
                $"prior_cave_state: purged {removed} shell(s), rebuilt {rebuilt} route surface(s), visual pass + hide slabs.");
            return true;
        }

        static bool TryFixResearchManifest(CaveBuildPreBuildReport report, StringBuilder sb, bool syncCatalog)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var wrote = false;
            try
            {
                CaveBuildResearchExporter.WriteMinimal(hub, report);
                wrote = true;
            }
            catch (Exception ex)
            {
                sb.AppendLine("research_manifest: WriteMinimal failed — " + ex.Message);
            }

            if (syncCatalog && !CaveBuildResearchCacheBridge.ShouldSkipCatalogTsxSync())
            {
                if (CaveBuildResearchCacheBridge.SyncResearchCatalog(out var catMsg))
                    sb.AppendLine("research_manifest: " + catMsg);
                else if (CaveBuildResearchCacheBridge.HasUsableLocalResearchCache())
                    sb.AppendLine("research_manifest: catalog sync failed — using existing ResearchCache.");
                else
                    sb.AppendLine("research_manifest: catalog sync failed — " + catMsg);
            }
            else
            {
                sb.AppendLine("research_manifest: minimal JSON only (no catalog tsx during reloop).");
            }

            if (wrote)
                sb.AppendLine("research_manifest: wrote CaveBuildResearch.json.");
            return wrote;
        }

        static bool TryFixScenePortal(StringBuilder sb)
        {
            var portal = CaveBuildPortalSettings.PortalForBuild;
            if (portal != null)
            {
                sb.AppendLine($"scene_portal: using assigned portal '{portal.name}'.");
                return true;
            }

            var candidates = CaveBuildPortalSettings.FindPortalCandidates();
            if (candidates.Length == 0)
            {
                sb.AppendLine("scene_portal: no portal candidates — will auto-detect at build.");
                return false;
            }

            CaveBuildPortalSettings.PortalForBuild = candidates[0];
            sb.AppendLine($"scene_portal: auto-assigned '{candidates[0].name}'.");
            return true;
        }

        static bool TryFixCompileGate(StringBuilder sb)
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastCompileDiagnosticsExportAt < 8.0)
            {
                sb.AppendLine("compile_gate: skipped duplicate diagnostics export (fix Console CS errors).");
                return false;
            }

            _lastCompileDiagnosticsExportAt = now;
            CaveBuildCompileGate.ExportDiagnostics();
            sb.AppendLine("compile_gate: exported diagnostics (no forced recompile during reloop).");
            return true;
        }

        /// <summary>True when the only sub-70 critical rung is compile_gate (verified CS errors).</summary>
        static bool IsCompileOnlyCriticalFailure(CaveBuildPreBuildReport report)
        {
            if (report?.Stages == null)
                return false;

            var compileCritical = false;
            foreach (var s in report.Stages)
            {
                if (!s.Critical || s.Score >= CaveBuildPreBuildLadder.StageFloorScore)
                    continue;
                if (s.StageId == "compile_gate")
                    compileCritical = true;
                else
                    return false;
            }

            return compileCritical;
        }
    }
}
#endif
