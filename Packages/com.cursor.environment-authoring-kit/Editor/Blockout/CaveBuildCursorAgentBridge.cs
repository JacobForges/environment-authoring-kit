using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Runs TypeScript Cursor SDK helpers (grade-and-fix.ts) against Hub + quality JSON.</summary>
    public static class CaveBuildCursorAgentBridge
    {
        public const string ToolsRelativePath =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader";

        static Process _backgroundAgent;
        static CaveCursorStreamLog _streamLog;
        static double _backgroundStartedAt;
        static int _ladderChainRemaining;
        static int _ladderChainTotal;
        static string _pendingLadderRung;
        static readonly HashSet<string> _ladderChainCompletedRungs = new();
        static bool _postBuildWorkflowActive;
        static bool _postBuildWorkflowLadderPhase;
        static bool _preBuildWorkflowActive;
        static bool _preBuildWorkflowLadderPhase;
        static int _mandatoryPhaseIndex;
        static string _currentWorkflowRung;
        static int _compileGateAttempts;
        static CaveBuildQualityReport _workflowReport;
        static CaveBuildPreBuildReport _preBuildReport;
        static WorldGenerationRequest _preBuildPendingRequest;
        static Transform _workflowCaveRoot;
        static SceneGroundInfo _workflowGround;
        static bool _streamPhaseCompleteAcknowledged;
        static bool _completingFromStreamFlag;
        static bool _workflowAdvancedFromStreamFlag;
        const string WorkflowPreBuild = "pre_build";
        const string WorkflowPostBuild = "post_build";
        const string WorkflowTerrain = "terrain";
        static bool _terrainWorkflowOverride;
        static bool _autoRebuildPending;
        static double _autoRebuildNextAttemptAt;
        static int _autoRebuildDeferLogCount;

        public static string GradeAndFixScript =>
            Path.Combine(ResolveHubRoot(), ToolsRelativePath, "grade-and-fix.ts");

        public static bool HasApiKey => CaveBuildCursorSettings.HasCredentialsForActiveProvider();

        public static bool IsPreBuildWorkflowActive => _preBuildWorkflowActive;

        public static bool IsAgentRunning => _backgroundAgent != null && !_backgroundAgent.HasExited;

        public static string ResolveHubRoot() => CaveBuildCursorSettings.ResolveHubRoot();

        public static void SetTerrainWorkflowOverride(bool active) => _terrainWorkflowOverride = active;

        /// <summary>Background invoke for terrain grader (prepare prompts before calling).</summary>
        public static bool TryInvokeTerrainAgentBackground(out string message, string rung = null)
        {
            _terrainWorkflowOverride = true;
            if (string.IsNullOrWhiteSpace(rung))
            {
                var ground = SceneGroundResolver.Resolve();
                var surface = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
                var req = new WorldGenerationRequest { SurfaceIncludeTrails = true };
                var report = SurfaceTerrainQualityGrader.Run(ground, req, surface);
                rung = SurfaceTerrainBuildLadder.PickActiveRung(report);
            }

            return TryInvokeGradeAndFixBackground(
                out message,
                includeLiveFix: false,
                rung: rung,
                startLadderChain: false,
                skipCavePromptExport: true);
        }

        public static bool TryInvokeGradeAndFix(out string message, bool includeLiveFix = false)
        {
            message = null;
            try
            {
                if (!HasApiKey)
                {
                    message = CaveBuildCursorSettings.CursorWorkflowCredentialHint();
                    return false;
                }

                var hub = ResolveHubRoot();
                var script = GradeAndFixScript;
                if (!File.Exists(script))
                {
                    message = $"Missing script: {script}\nRun npm install in Tools/cave-grader.";
                    return false;
                }

                if (!CaveBuildCursorProcessResolver.TryCreateGradeAndFixProcess(
                        hub,
                        script,
                        includeLiveFix,
                        ResolveActiveRungFromDisk(),
                        out var psi,
                        out message,
                        ResolveWorkflowMode()))
                    return false;

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    message = "Failed to start Cursor grader process.";
                    return false;
                }

                AttachLiveConsoleLogging(proc, ResolveActiveRungFromDisk(), 1, 1);
                proc.WaitForExit(600_000);

                if (proc.ExitCode != 0)
                {
                    message =
                        $"Cursor agent exited {proc.ExitCode}. See Console [CaveCursor] lines above.";
                    return false;
                }

                message = "Agent finished OK. See Console [CaveCursor] lines above.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        /// <summary>Starts Cursor agent without blocking the Unity editor (used after every build when enabled).</summary>
        public static bool TryInvokeGradeAndFixBackground(
            out string message,
            bool includeLiveFix = false,
            string rung = null,
            bool startLadderChain = false,
            bool skipCavePromptExport = false)
        {
            message = null;
            try
            {
                if (IsAgentRunning)
                {
                    message = "Cursor agent already running — wait for completion in Console.";
                    return false;
                }

                if (!HasApiKey)
                {
                    message = CaveBuildCursorSettings.CursorWorkflowCredentialHint();
                    return false;
                }

                if (startLadderChain)
                {
                    _ladderChainTotal = CaveBuildPromptLadder.MaxChainPerMeatPass;
                    _ladderChainRemaining = _ladderChainTotal - 1;
                    _ladderChainCompletedRungs.Clear();
                }
                else if (_ladderChainTotal <= 0)
                    _ladderChainTotal = 1;

                if (string.IsNullOrWhiteSpace(rung))
                    rung = ResolveActiveRungFromDisk();

                if (!skipCavePromptExport)
                {
                    var caveRoot = GameObject.Find(CaveGeometryPaths.CaveSystemRootName)?.transform;
                    var ground = SceneGroundResolver.Resolve();
                    CaveBuildRungPromptExporter.PrepareAgentInvoke(rung, caveRoot, ground);
                }
                else if (!string.IsNullOrWhiteSpace(rung))
                {
                    TerrainBuildRungPromptExporter.PrepareAgentInvoke(rung, out _);
                }

                var hub = ResolveHubRoot();
                var script = GradeAndFixScript;
                if (!File.Exists(script))
                {
                    message = $"Missing script: {script}\nRun npm install in Tools/cave-grader.";
                    return false;
                }

                if (!CaveBuildCursorProcessResolver.TryCreateGradeAndFixProcess(
                        hub,
                        script,
                        includeLiveFix,
                        rung,
                        out _,
                        out message,
                        ResolveWorkflowMode()))
                    return false;

                var capturedRung = rung;
                var capturedLiveFix = includeLiveFix;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () =>
                    {
                        if (!TryInvokeGradeAndFixBackgroundNow(
                                out var startMsg,
                                capturedLiveFix,
                                capturedRung))
                            UnityEngine.Debug.LogWarning("[CaveCursor] Delayed invoke failed: " + startMsg);
                    },
                    $"invoke Cursor agent (rung={rung})");

                var pacingDelay = CaveBuildCursorSettings.ResolvePacingSeconds().normalDelay;
                message =
                    $"Cursor agent scheduled in ~{pacingDelay:F1}s " +
                    $"(rung={rung}, model={CaveBuildCursorSettings.ResolveModelId()}). " +
                    "Watch Console for [CaveCursor] stream lines.";
                UnityEngine.Debug.Log("[CaveCursor] " + message);
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                UnityEngine.Debug.LogWarning("[CaveCursor] " + ex);
                return false;
            }
        }

        static bool TryInvokeGradeAndFixBackgroundNow(
            out string message,
            bool includeLiveFix,
            string rung)
        {
            message = null;
            try
            {
                if (IsAgentRunning)
                {
                    message = "Cursor agent already running.";
                    return false;
                }

                var hub = ResolveHubRoot();
                var script = GradeAndFixScript;
                if (!CaveBuildCursorProcessResolver.TryCreateGradeAndFixProcess(
                        hub,
                        script,
                        includeLiveFix,
                        rung,
                        out var psi,
                        out message,
                        ResolveWorkflowMode()))
                    return false;

                _backgroundAgent = Process.Start(psi);
                if (_backgroundAgent == null)
                {
                    message = "Failed to start Cursor grader process.";
                    return false;
                }

                var chainIndex = _ladderChainCompletedRungs.Count + 1;
                LogLadderInvokeBanner(rung, chainIndex, _ladderChainTotal);
                CaveCursorStreamLog.ClearRecentErrors();
                AttachLiveConsoleLogging(_backgroundAgent, rung, chainIndex, _ladderChainTotal);

                _backgroundStartedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update -= PollBackgroundAgent;
                EditorApplication.update += PollBackgroundAgent;
                message =
                    $"Cursor agent started (rung={rung}, pass {chainIndex}/{_ladderChainTotal}, {psi.FileName}, model={CaveBuildCursorSettings.ResolveModelId()}).";
                UnityEngine.Debug.Log("[CaveCursor] " + message);
                if (!string.IsNullOrEmpty(rung))
                    _ladderChainCompletedRungs.Add(rung);
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                UnityEngine.Debug.LogWarning("[CaveCursor] " + ex);
                return false;
            }
        }

        /// <summary>Called when grade-and-fix or the agent prints a phase-complete flag line.</summary>
        public static void NotifyStreamPhaseComplete(CaveBuildPhaseCompleteSignal signal)
        {
            _streamPhaseCompleteAcknowledged = true;
            UnityEngine.Debug.Log(
                $"[CaveCursor] Phase-complete flag received: workflow={signal.Workflow} rung={signal.Rung} reason={signal.Reason}");

            if (signal.Workflow == WorkflowTerrain || _terrainWorkflowOverride)
            {
                TerrainBuildCursorAgentBridge.OnStreamPhaseComplete(signal);
                return;
            }

            CaveBuildActionPacing.ScheduleBuildStep(
                CompleteWorkflowPhaseFromStreamFlag,
                $"advance workflow after phase-complete (rung={signal.Rung})");
        }

        static void CompleteWorkflowPhaseFromStreamFlag()
        {
            if (!_streamPhaseCompleteAcknowledged || _completingFromStreamFlag)
                return;

            if (TerrainBuildCursorAgentBridge.IsTerrainWorkflowActive)
                return;

            if (!_preBuildWorkflowActive && !_postBuildWorkflowActive)
                return;

            _completingFromStreamFlag = true;
            try
            {
            TerminateBackgroundAgentIfRunning();
            _streamPhaseCompleteAcknowledged = false;
            EditorApplication.update -= PollBackgroundAgent;
            _workflowAdvancedFromStreamFlag = true;

            if (_preBuildWorkflowActive)
            {
                CaveBuildCompileGate.ExportDiagnostics();
                RefreshPreBuildReportFromPendingRequest();
                AdvancePreBuildWorkflow(true, 0);
                return;
            }

            if (_postBuildWorkflowActive)
            {
                CaveBuildCompileGate.ExportDiagnostics();
                AdvancePostBuildWorkflow(true, 0);
            }
            }
            finally
            {
                _completingFromStreamFlag = false;
            }
        }

        public static void TerminateBackgroundAgentIfRunningPublic() => TerminateBackgroundAgentIfRunning();

        static void TerminateBackgroundAgentIfRunning()
        {
            if (_backgroundAgent == null || _backgroundAgent.HasExited)
                return;

            try
            {
                _backgroundAgent.Kill();
                _backgroundAgent.WaitForExit(8000);
            }
            catch
            {
                // ignored
            }

            try
            {
                _backgroundAgent.Dispose();
            }
            catch
            {
                // ignored
            }

            _backgroundAgent = null;
            _streamLog?.Flush();
            _streamLog = null;
        }

        static void RefreshPreBuildReportFromPendingRequest()
        {
            if (_workflowGround == null || !_workflowGround.HasAnchor || _preBuildPendingRequest == null)
                return;

            _preBuildReport = CaveBuildPreBuildLadder.Run(
                _workflowGround,
                _preBuildPendingRequest,
                layoutPrototype: false,
                layoutSeed: _preBuildPendingRequest.Seed);
        }

        static bool IsPreBuildRungAlreadyPassing(string rungId)
        {
            if (_preBuildReport?.Stages == null || string.IsNullOrEmpty(rungId))
                return false;

            foreach (var s in _preBuildReport.Stages)
            {
                if (s.StageId != rungId)
                    continue;
                return s.Passed && s.Score >= CaveBuildPreBuildLadder.StagePassScore;
            }

            return false;
        }

        static void AttachLiveConsoleLogging(Process proc, string rung, int chainIndex, int chainTotal)
        {
            _streamPhaseCompleteAcknowledged = false;
            _streamLog = new CaveCursorStreamLog(rung, chainIndex, chainTotal);
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    _streamLog.OnStdout(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    _streamLog.OnStderr(e.Data);
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        static void PollBackgroundAgent()
        {
            if (_backgroundAgent == null)
            {
                EditorApplication.update -= PollBackgroundAgent;
                return;
            }

            if (!_backgroundAgent.HasExited)
                return;

            var code = _backgroundAgent.ExitCode;
            _backgroundAgent.Dispose();
            _backgroundAgent = null;
            _streamLog?.Flush();
            _streamLog = null;
            EditorApplication.update -= PollBackgroundAgent;

            var elapsed = EditorApplication.timeSinceStartup - _backgroundStartedAt;
            var success = code == 0 || _streamPhaseCompleteAcknowledged;
            _streamPhaseCompleteAcknowledged = false;

            if (!success)
            {
                var hint = CaveCursorStreamLog.GetRecentErrorSummary();
                UnityEngine.Debug.LogWarning(
                    string.IsNullOrEmpty(hint)
                        ? $"[CaveCursor] Agent finished with exit {code} after {elapsed:F0}s. See Console and {CaveBuildCursorGraderDiagnostics.LastRunPath}."
                        : $"[CaveCursor] Agent finished with exit {code} after {elapsed:F0}s — {hint}");
            }
            else
                UnityEngine.Debug.Log(
                    $"[CaveCursor] Agent finished OK after {elapsed:F0}s. Re-build cave, then Re-grade.");

            var advancedFromStream = _workflowAdvancedFromStreamFlag;
            _workflowAdvancedFromStreamFlag = false;

            if (_preBuildWorkflowActive)
            {
                CaveBuildCompileGate.ExportDiagnostics();
                RefreshPreBuildReportFromPendingRequest();
                if (!advancedFromStream && AdvancePreBuildWorkflow(success, elapsed))
                    return;
                if (advancedFromStream && _preBuildWorkflowActive)
                    return;
            }

            if (_postBuildWorkflowActive)
            {
                CaveBuildCompileGate.ExportDiagnostics();
                if (!advancedFromStream && AdvancePostBuildWorkflow(success, elapsed))
                    return;
                if (advancedFromStream && _postBuildWorkflowActive)
                    return;
            }

            if (TerrainBuildCursorAgentBridge.IsTerrainWorkflowActive)
            {
                if (!advancedFromStream)
                    TerrainBuildCursorAgentBridge.OnBackgroundAgentFinished(success, elapsed);
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (code == 0 && (_preBuildWorkflowLadderPhase || _postBuildWorkflowLadderPhase) &&
                ScheduleNextLadderRungWithDelay())
                return;

            if (code == 0 && settings.autoRebuildAfterAgentSuccess && !_preBuildWorkflowActive &&
                !_skipAutoRebuildAfterPreBuildGeometry && !CaveBuildPendingGeometryBuild.HasPending &&
                !CaveBuildPipelineCompletion.ShouldBlockAutoRebuildAfterAgent())
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Scheduling auto-rebuild (Build Complete Cave) after script compile…");
                ScheduleAutoRebuildAfterCompile();
                CaveBuildDialogPolicy.Notify(
                    "Cursor Agent",
                    $"Agent finished ({elapsed:F0}s). Unity will re-build the cave automatically once scripts compile.");
            }
            else
            {
                CaveBuildDialogPolicy.Notify(
                    "Cursor Agent",
                    code == 0
                        ? $"Agent finished ({elapsed:F0}s). Run Build Complete Cave Level to apply code fixes to the scene."
                        : $"Agent failed (exit {code}). See Console for [CaveCursor] output.");
            }
        }

        static bool ScheduleNextLadderRungWithDelay()
        {
            if (_ladderChainRemaining <= 0 || IsAgentRunning)
                return false;

            var next = PickNextFailingRung();
            if (string.IsNullOrEmpty(next))
            {
                _ladderChainRemaining = 0;
                UnityEngine.Debug.Log("[CaveCursor] Ladder chain complete — no more failing rungs.");
                return false;
            }

            _pendingLadderRung = next;
            CaveBuildActionPacing.ScheduleBuildStep(
                InvokePendingLadderRung,
                $"ladder rung '{next}' ({_ladderChainRemaining} slot(s) left)",
                CaveBuildActionPacing.ActionWeight.Normal);
            return true;
        }

        static void InvokePendingLadderRung()
        {
            if (_ladderChainRemaining <= 0 || string.IsNullOrEmpty(_pendingLadderRung))
                return;

            _ladderChainRemaining--;
            var rung = _pendingLadderRung;
            _pendingLadderRung = null;
            _currentWorkflowRung = rung;

            if (!TryInvokeGradeAndFixBackground(out var msg, rung: rung))
            {
                UnityEngine.Debug.LogWarning("[CaveCursor] Ladder chain stopped: " + msg);
                return;
            }

            UnityEngine.Debug.Log(
                $"[CaveCursor] Ladder chain: started rung '{rung}' ({_ladderChainRemaining} slot(s) left).");
        }

        static string PickNextFailingRung()
        {
            if (_preBuildWorkflowLadderPhase)
                return PickNextFailingPreBuildRung();

            var failing = LoadFailingRungsFromDisk();
            foreach (var rung in failing)
            {
                if (!_ladderChainCompletedRungs.Contains(rung))
                    return rung;
            }

            foreach (var rung in CaveBuildPromptLadder.CursorRungOrder)
            {
                if (!_ladderChainCompletedRungs.Contains(rung))
                    return rung;
            }

            return null;
        }

        static string PickNextFailingPreBuildRung()
        {
            RefreshPreBuildReportFromPendingRequest();

            var failing = LoadFailingPreBuildRungsFromDisk();
            foreach (var rung in failing)
            {
                if (_ladderChainCompletedRungs.Contains(rung))
                    continue;
                if (IsPreBuildRungAlreadyPassing(rung))
                {
                    _ladderChainCompletedRungs.Add(rung);
                    UnityEngine.Debug.Log(
                        $"[CaveCursor] Pre-build rung '{rung}' already passing on disk — auto-advance.");
                    continue;
                }

                return rung;
            }

            foreach (var rung in CaveBuildPromptLadder.PreBuildLadderRungs)
            {
                if (_ladderChainCompletedRungs.Contains(rung))
                    continue;
                if (IsPreBuildRungAlreadyPassing(rung))
                {
                    _ladderChainCompletedRungs.Add(rung);
                    continue;
                }

                return rung;
            }

            return null;
        }

        static IReadOnlyList<string> LoadFailingPreBuildRungsFromDisk()
        {
            var path = Path.Combine(ResolveHubRoot(), CaveBuildPreBuildLadder.ContextPath);
            if (!File.Exists(path))
                return CaveBuildPromptLadder.PreBuildLadderRungs;

            try
            {
                var ctx = JsonUtility.FromJson<PreBuildLadderContextDisk>(File.ReadAllText(path));
                if (ctx?.failingRungs != null && ctx.failingRungs.Length > 0)
                    return ctx.failingRungs;
            }
            catch
            {
                // ignored
            }

            return CaveBuildPromptLadder.PreBuildLadderRungs;
        }

        static string ResolveWorkflowMode()
        {
            if (_terrainWorkflowOverride || TerrainBuildCursorAgentBridge.IsTerrainWorkflowActive)
                return WorkflowTerrain;
            if (_preBuildWorkflowActive)
                return WorkflowPreBuild;
            if (_postBuildWorkflowActive)
                return WorkflowPostBuild;
            return null;
        }

        /// <summary>Starts pre-build workflow: research → plan → compile_gate → readiness ladder.</summary>
        public static bool TryBeginPreBuildWorkflow(
            CaveBuildPreBuildReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request = null)
        {
            if (IsAgentRunning)
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Pre-build workflow deferred — Cursor agent already running.");
                return false;
            }

            if (request != null)
                _preBuildPendingRequest = request;

            _preBuildReport = report;
            _workflowGround = ground;
            _preBuildWorkflowActive = true;
            _preBuildWorkflowLadderPhase = false;
            _postBuildWorkflowActive = false;
            _postBuildWorkflowLadderPhase = false;
            _mandatoryPhaseIndex = 0;
            _compileGateAttempts = 0;
            _ladderChainCompletedRungs.Clear();
            return StartPreBuildWorkflowStep(report, ground);
        }

        static bool StartPreBuildWorkflowStep(CaveBuildPreBuildReport report, SceneGroundInfo ground)
        {
            report ??= _preBuildReport;
            ground ??= _workflowGround;

            if (_mandatoryPhaseIndex < CaveBuildPromptLadder.PreBuildMandatoryPhases.Length)
            {
                var phase = CaveBuildPromptLadder.PreBuildMandatoryPhases[_mandatoryPhaseIndex];
                _currentWorkflowRung = phase;
                CaveBuildPreBuildWorkflowExporter.SetCurrentPhase(phase);
                _ladderChainTotal =
                    CaveBuildPromptLadder.PreBuildMandatoryPhases.Length +
                    CaveBuildPromptLadder.MaxPreBuildChainPerPass;
                var chainIndex = _mandatoryPhaseIndex + 1;

                UnityEngine.Debug.Log(
                    $"[CaveCursor] Pre-build workflow step {chainIndex}/{_ladderChainTotal}: phase={phase}");

                if (phase == CaveBuildPromptLadder.RungCompileGate &&
                    !CaveBuildCompileGate.HasBlockingErrors())
                {
                    UnityEngine.Debug.Log(
                        "[CaveCursor] compile_gate: zero CS errors — skipping Cursor invoke, advancing to readiness ladder.");
                    CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(phase, true);
                    _compileGateAttempts = 0;
                    _mandatoryPhaseIndex = CaveBuildPromptLadder.PreBuildMandatoryPhases.Length;
                    CaveBuildActionPacing.ScheduleBuildStep(
                        () => BeginPreBuildReadinessLadder(report),
                        "pre-build readiness ladder after compile_gate skip",
                        CaveBuildActionPacing.ActionWeight.Normal);
                    return true;
                }

                if (!TryInvokeGradeAndFixBackground(
                        out var msg,
                        rung: phase,
                        startLadderChain: _mandatoryPhaseIndex == 0))
                {
                    UnityEngine.Debug.LogWarning("[CaveCursor] Pre-build workflow failed to start: " + msg);
                    _preBuildWorkflowActive = false;
                    return false;
                }

                return true;
            }

            return BeginPreBuildReadinessLadder(report);
        }

        static bool BeginPreBuildReadinessLadder(CaveBuildPreBuildReport report)
        {
            if (CaveBuildCompileGate.HasBlockingErrors())
            {
                UnityEngine.Debug.LogWarning(
                    "[CaveCursor] Pre-build readiness blocked — compile errors remain. Re-running compile_gate.");
                _mandatoryPhaseIndex = 2;
                _preBuildWorkflowLadderPhase = false;
                return StartPreBuildWorkflowStep(report, _workflowGround);
            }

            _preBuildWorkflowLadderPhase = true;
            _mandatoryPhaseIndex = CaveBuildPromptLadder.PreBuildMandatoryPhases.Length;
            RefreshPreBuildReportFromPendingRequest();
            report = _preBuildReport ?? report;

            if (CaveBuildPreBuildLadder.AllRungsPassing(report))
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Pre-build readiness: all rungs already passing — skipping ladder invokes.");
                return FinishPreBuildReadinessLadderPhase(0);
            }

            for (var i = 0; i < CaveBuildPromptLadder.PreBuildLadderRungs.Length + 2; i++)
            {
                var rung = CaveBuildPreBuildLadder.PickActiveRung(report, _ladderChainCompletedRungs);
                if (string.IsNullOrEmpty(rung))
                    return FinishPreBuildReadinessLadderPhase(0);

                if (!IsPreBuildRungAlreadyPassing(rung))
                    break;

                _ladderChainCompletedRungs.Add(rung);
                UnityEngine.Debug.Log(
                    $"[CaveCursor] Pre-build rung '{rung}' already {CaveBuildPreBuildLadder.StagePassScore}+ — skipping Cursor (no phase 5 stall).");
            }

            var activeRung = CaveBuildPreBuildLadder.PickActiveRung(report, _ladderChainCompletedRungs);
            if (string.IsNullOrEmpty(activeRung))
                return FinishPreBuildReadinessLadderPhase(0);

            CaveBuildPreBuildWorkflowExporter.SetCurrentPhase(CaveBuildPromptLadder.PhaseReadinessLadder);
            _ladderChainRemaining = CaveBuildPromptLadder.MaxPreBuildChainPerPass - 1;
            _currentWorkflowRung = activeRung;

            if (!TryInvokeGradeAndFixBackground(out var msg, rung: activeRung, startLadderChain: false))
            {
                UnityEngine.Debug.LogWarning("[CaveCursor] Pre-build readiness ladder failed to start: " + msg);
                _preBuildWorkflowActive = false;
                return false;
            }

            UnityEngine.Debug.Log($"[CaveCursor] Pre-build readiness ladder started: rung={activeRung}");
            return true;
        }

        static bool FinishPreBuildReadinessLadderPhase(double elapsedSeconds)
        {
            CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(CaveBuildPromptLadder.PhaseReadinessLadder, true);
            CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(CaveBuildPromptLadder.PhaseProceedBuild, true);
            _preBuildWorkflowActive = false;
            _preBuildWorkflowLadderPhase = false;
            UnityEngine.Debug.Log("[CaveCursor] Pre-build readiness ladder complete (all rungs passing).");

            if (!TryFinishPreBuildAndContinueGeometry(elapsedSeconds))
            {
                CaveBuildDialogPolicy.Notify(
                    "Cursor Agent",
                    "Pre-build readiness passed on disk. If geometry did not start, run Build Complete Cave.");
                return true;
            }

            return true;
        }

        static bool AdvancePreBuildWorkflow(bool success, double elapsedSeconds)
        {
            var phase = _currentWorkflowRung ?? "unknown";
            if (!success)
            {
                if (phase == CaveBuildPromptLadder.RungResearch)
                    return ContinueAfterResearchAgentFailure(isPreBuild: true, elapsedSeconds);

                if (CaveBuildPreBuildWorkflowExporter.IsWorkflowPhaseId(phase) ||
                    phase == CaveBuildPromptLadder.PhaseReadinessLadder)
                    CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(phase, false, BuildAgentFailureNote(elapsedSeconds));
                _preBuildWorkflowActive = false;
                CaveBuildDialogPolicy.Notify(
                    "Cursor Agent",
                    $"Pre-build workflow phase '{phase}' failed. See Console and CaveBuildAgentMemory.json.");
                return false;
            }

            if (phase == CaveBuildPromptLadder.RungCompileGate && !_preBuildWorkflowLadderPhase)
            {
                CaveBuildCompileGate.ExportDiagnostics();
                if (CaveBuildCompileGate.HasBlockingErrors())
                {
                    _compileGateAttempts++;
                    var errCount = CaveBuildCompileGate.Capture().ErrorCount;
                    CaveBuildAgentMemoryExporter.RecordFailure(
                        "pre_" + phase,
                        phase,
                        $"Compile still failing ({errCount} errors) after agent pass #{_compileGateAttempts}");

                    if (_compileGateAttempts < 3)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[CaveCursor] pre-build compile_gate: {errCount} errors remain — retry {_compileGateAttempts}/3");
                        _mandatoryPhaseIndex = 2;
                        CaveBuildActionPacing.ScheduleBuildStep(
                            () => StartPreBuildWorkflowStep(null, null),
                            "pre-build compile_gate retry");
                        return true;
                    }

                    UnityEngine.Debug.LogError(
                        "[CaveCursor] pre-build compile_gate exhausted retries — fix compile errors manually, then Run Pre-Build Gate Only.");
                    _preBuildWorkflowActive = false;
                    CaveBuildDialogPolicy.Notify(
                        "Cursor Agent",
                        "Compile errors remain after 3 compile_gate passes. Fix errors in Console, then re-run pre-build gate.");
                    return false;
                }

                _compileGateAttempts = 0;
                CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(phase, true);
                _mandatoryPhaseIndex = CaveBuildPromptLadder.PreBuildMandatoryPhases.Length;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () => BeginPreBuildReadinessLadder(_preBuildReport),
                    "pre-build readiness ladder after compile_gate");
                return true;
            }

            if (phase == CaveBuildPromptLadder.RungResearch)
            {
                CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(phase, true);
                _mandatoryPhaseIndex = 1;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () => StartPreBuildWorkflowStep(null, null),
                    "pre-build plan phase",
                    CaveBuildActionPacing.ActionWeight.Normal);
                return true;
            }

            if (phase == "plan")
            {
                CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(phase, true);
                _mandatoryPhaseIndex = 2;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () => StartPreBuildWorkflowStep(null, null),
                    "pre-build compile_gate phase",
                    CaveBuildActionPacing.ActionWeight.Normal);
                return true;
            }

            if (_preBuildWorkflowLadderPhase)
            {
                _ladderChainCompletedRungs.Add(phase);
                RefreshPreBuildReportFromPendingRequest();

                if (ScheduleNextLadderRungWithDelay())
                    return true;

                return FinishPreBuildReadinessLadderPhase(elapsedSeconds);
            }

            _preBuildWorkflowActive = false;
            return false;
        }

        static bool TryFinishPreBuildAndContinueGeometry(double elapsedSeconds)
        {
            var ground = _workflowGround;
            var request = _preBuildPendingRequest;
            if (ground == null || !ground.HasAnchor || request == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[CaveCursor] Pre-build complete but no pending geometry build was queued.");
                CaveBuildPendingGeometryBuild.Clear();
                return false;
            }

            var report = CaveBuildPreBuildLadder.Run(
                ground,
                request,
                layoutPrototype: false,
                layoutSeed: request.Seed);
            _preBuildReport = report;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (settings.enforcePreBuildGate && !report.BuildAcceptable)
            {
                UnityEngine.Debug.LogWarning(
                    $"[CaveCursor] Pre-build still not acceptable after Cursor ({report.LetterGrade} {report.OverallScore}/100).");
                return false;
            }

            if (!CaveBuildPendingGeometryBuild.HasPending)
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Pre-build Cursor done — no pending build (run Build Complete Cave manually).");
                return true;
            }

            if (!settings.autoContinueAfterPreBuildCursor)
            {
                CaveBuildDialogPolicy.Notify(
                    "Cursor Agent",
                    $"Pre-build workflow finished ({elapsedSeconds:F0}s). Run Build Complete Cave to generate geometry.");
                return true;
            }

            ScheduleContinueGeometryAfterPreBuild();
            return true;
        }

        static bool _skipAutoRebuildAfterPreBuildGeometry;

        static void ScheduleContinueGeometryAfterPreBuild()
        {
            _skipAutoRebuildAfterPreBuildGeometry = true;
            var attempts = 0;
            const int maxAttempts = 360;

            void TryContinue()
            {
                attempts++;
                if (attempts > maxAttempts)
                {
                    _skipAutoRebuildAfterPreBuildGeometry = false;
                    UnityEngine.Debug.LogWarning(
                        "[CaveCursor] Continue after pre-build timed out. Run Build Complete Cave manually.");
                    return;
                }

                if (ShouldDeferGeometryAfterPreBuild())
                {
                    if (attempts == 1 || attempts % 40 == 0)
                    {
                        UnityEngine.Debug.Log(
                            "[CaveCursor] Waiting to continue geometry " +
                            $"(attempt {attempts}, agent={IsAgentRunning}, compile={EditorApplication.isCompiling}, " +
                            $"phased={LavaTubeCaveBuildPipeline.IsPhasedBuildActive}, " +
                            $"build={LavaTubeCaveBuilder.IsBuildInProgress}, " +
                            $"heavy={CaveBuildActionPacing.IsHeavyRunning})…");
                    }

                    CaveBuildActionPacing.ScheduleLight(TryContinue, "wait before geometry");
                    return;
                }

                var pendingRequest = _preBuildPendingRequest;
                CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                    pendingRequest,
                    _workflowGround,
                    pendingRequest != null && (
                        CaveBuildSurfaceCompletionGate.IsSurfaceWorldGeneratorFinished ||
                        CaveBuildSurfaceCompletionGate.IsCompleteForSeed(pendingRequest)));

                UnityEngine.Debug.Log("[CaveCursor] Pre-build done — starting cave geometry…");
                try
                {
                    if (!CaveBuildPendingGeometryBuild.TryRunPending(out var msg))
                    {
                        UnityEngine.Debug.LogWarning("[CaveCursor] " + msg);
                        CaveBuildSurfaceCompletionGate.LogHandoffBlocker(
                            pendingRequest, _workflowGround, "TryRunPending failed");
                    }
                    else
                        UnityEngine.Debug.Log("[CaveCursor] " + msg);
                }
                finally
                {
                    _skipAutoRebuildAfterPreBuildGeometry = false;
                }
            }

            CaveBuildActionPacing.ScheduleBuildStep(
                TryContinue,
                "continue cave geometry after pre-build");
        }

        static bool ShouldDeferGeometryAfterPreBuild()
        {
            if (EditorApplication.isCompiling)
                return true;

            if (SurfaceTerrainCenteredAuthor.IsQueuedPassesActive)
                return true;

            var pendingRequest = _preBuildPendingRequest;
            var ground = _workflowGround;

            if (CaveBuildPendingGeometryBuild.HasPending && pendingRequest != null)
            {
                CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                    pendingRequest,
                    ground,
                    CaveBuildSurfaceCompletionGate.IsSurfaceWorldGeneratorFinished ||
                    CaveBuildSurfaceCompletionGate.IsCompleteForSeed(pendingRequest));

                if (CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(pendingRequest, ground))
                {
                    if (TerrainBuildCursorAgentBridge.IsTerrainWorkflowActive)
                        TerrainBuildCursorAgentBridge.YieldForPendingCaveBuild();
                    if (!IsAgentRunning)
                        return false;
                }
                else if (CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive)
                {
                    return true;
                }
            }

            if (pendingRequest != null &&
                CaveBuildSurfaceCompletionGate.MustFinishSurfaceBeforeCave(pendingRequest) &&
                !CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(pendingRequest, ground))
            {
                if (CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive)
                    return true;
            }

            if (IsAgentRunning)
                return true;
            if (LavaTubeCaveBuildPipeline.IsPhasedBuildActive)
                return true;

            if (CaveBuildPendingGeometryBuild.HasPending && !IsAgentRunning)
                return false;

            if (LavaTubeCaveBuilder.IsBuildInProgress && !CaveBuildPendingGeometryBuild.HasPending)
                return true;

            return CaveBuildActionPacing.IsHeavyRunning && !CaveBuildPendingGeometryBuild.HasPending;
        }

        /// <summary>Starts mandatory workflow: research → compile_gate → ladder chain.</summary>
        public static bool TryBeginPostBuildWorkflow(
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            _workflowReport = report;
            _workflowCaveRoot = caveRoot;
            _workflowGround = ground;
            _postBuildWorkflowActive = true;
            _postBuildWorkflowLadderPhase = false;
            _preBuildWorkflowActive = false;
            _preBuildWorkflowLadderPhase = false;
            _mandatoryPhaseIndex = 0;
            _compileGateAttempts = 0;
            _ladderChainCompletedRungs.Clear();
            return StartPostBuildWorkflowStep(report, caveRoot, ground);
        }

        static bool StartPostBuildWorkflowStep(
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            report ??= _workflowReport;
            caveRoot ??= _workflowCaveRoot;
            ground ??= _workflowGround;

            if (_mandatoryPhaseIndex < CaveBuildPromptLadder.PostBuildMandatoryPhases.Length)
            {
                var phase = CaveBuildPromptLadder.PostBuildMandatoryPhases[_mandatoryPhaseIndex];
                _currentWorkflowRung = phase;
                CaveBuildWorkflowExporter.SetCurrentPhase(phase);
                _ladderChainTotal =
                    CaveBuildPromptLadder.PostBuildMandatoryPhases.Length + CaveBuildPromptLadder.MaxChainPerMeatPass;
                var chainIndex = _mandatoryPhaseIndex + 1;

                UnityEngine.Debug.Log(
                    $"[CaveCursor] Post-build workflow step {chainIndex}/{_ladderChainTotal}: phase={phase}");

                if (!TryInvokeGradeAndFixBackground(
                        out var msg,
                        rung: phase,
                        startLadderChain: _mandatoryPhaseIndex == 0))
                {
                    UnityEngine.Debug.LogWarning("[CaveCursor] Post-build workflow failed to start: " + msg);
                    _postBuildWorkflowActive = false;
                    return false;
                }

                return true;
            }

            return BeginLadderPhaseAfterWorkflow(report, caveRoot, ground);
        }

        static bool BeginLadderPhaseAfterWorkflow(
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (CaveBuildCompileGate.HasBlockingErrors())
            {
                UnityEngine.Debug.LogWarning(
                    "[CaveCursor] Ladder phase blocked — compile errors remain. Re-running compile_gate.");
                _mandatoryPhaseIndex = 1;
                _postBuildWorkflowLadderPhase = false;
                return StartPostBuildWorkflowStep(report, caveRoot, ground);
            }

            _postBuildWorkflowLadderPhase = true;
            _mandatoryPhaseIndex = CaveBuildPromptLadder.PostBuildMandatoryPhases.Length;
            CaveBuildWorkflowExporter.SetCurrentPhase("ladder_fixes");
            _ladderChainRemaining = CaveBuildPromptLadder.MaxChainPerMeatPass - 1;

            var rung = CaveBuildPromptLadder.PickActiveRung(report, caveRoot, ground);
            _currentWorkflowRung = rung;
            if (!TryInvokeGradeAndFixBackground(out var msg, rung: rung, startLadderChain: false))
            {
                UnityEngine.Debug.LogWarning("[CaveCursor] Ladder phase failed to start: " + msg);
                _postBuildWorkflowActive = false;
                return false;
            }

            UnityEngine.Debug.Log($"[CaveCursor] Post-build ladder phase started: rung={rung}");
            return true;
        }

        /// <returns>True if another agent invoke was scheduled.</returns>
        static string BuildAgentFailureNote(double elapsedSeconds)
        {
            var hint = CaveCursorStreamLog.GetRecentErrorSummary();
            var baseNote = $"Cursor exit non-zero after {elapsedSeconds:F0}s";
            return string.IsNullOrEmpty(hint) ? baseNote : $"{baseNote} — {hint}";
        }

        /// <summary>Research agent is optional when local ResearchCache exists — do not block compile_gate.</summary>
        static bool ContinueAfterResearchAgentFailure(bool isPreBuild, double elapsedSeconds)
        {
            var note = BuildAgentFailureNote(elapsedSeconds);
            CaveBuildAgentMemoryExporter.RecordFailure(
                CaveBuildPromptLadder.RungResearch,
                CaveBuildPromptLadder.RungResearch,
                note + " (continued with local ResearchCache)");

            UnityEngine.Debug.LogWarning(
                "[CaveCursor] Research agent failed — continuing workflow (ResearchCache pull already ran). " +
                note);

            if (isPreBuild)
            {
                CaveBuildPreBuildWorkflowExporter.RecordPhaseResult(
                    CaveBuildPromptLadder.RungResearch,
                    true,
                    "Agent skipped; disk cache + images used");
                _mandatoryPhaseIndex = 1;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () => StartPreBuildWorkflowStep(null, null),
                    "pre-build plan phase (after research skip)",
                    CaveBuildActionPacing.ActionWeight.Normal);
                return true;
            }

            CaveBuildWorkflowExporter.RecordPhaseResult(
                CaveBuildPromptLadder.RungResearch,
                true,
                "Agent skipped; disk cache + images used");
            _mandatoryPhaseIndex = 1;
            CaveBuildActionPacing.ScheduleBuildStep(
                () => StartPostBuildWorkflowStep(null, null, null),
                "post-build compile_gate (after research skip)",
                CaveBuildActionPacing.ActionWeight.Normal);
            return true;
        }

        static bool AdvancePostBuildWorkflow(bool success, double elapsedSeconds)
        {
            var phase = _currentWorkflowRung ?? "unknown";
            if (!success)
            {
                if (phase == CaveBuildPromptLadder.RungResearch)
                    return ContinueAfterResearchAgentFailure(isPreBuild: false, elapsedSeconds);

                CaveBuildWorkflowExporter.RecordPhaseResult(phase, false, BuildAgentFailureNote(elapsedSeconds));
                _postBuildWorkflowActive = false;
                CaveBuildDialogPolicy.Notify(
                    "Cursor Agent",
                    $"Workflow phase '{phase}' failed. See Console and CaveBuildAgentMemory.json.");
                return false;
            }

            CaveBuildWorkflowExporter.RecordPhaseResult(phase, true);

            if (phase == CaveBuildPromptLadder.RungCompileGate && !_postBuildWorkflowLadderPhase)
            {
                CaveBuildCompileGate.ExportDiagnostics();
                if (CaveBuildCompileGate.HasBlockingErrors())
                {
                    _compileGateAttempts++;
                    var errCount = CaveBuildCompileGate.Capture().ErrorCount;
                    CaveBuildAgentMemoryExporter.RecordFailure(
                        phase,
                        phase,
                        $"Compile still failing ({errCount} errors) after agent pass #{_compileGateAttempts}");

                    if (_compileGateAttempts < 3)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[CaveCursor] compile_gate: {errCount} errors remain — retry {_compileGateAttempts}/3");
                        CaveBuildActionPacing.ScheduleBuildStep(
                            () => StartPostBuildWorkflowStep(null, null, null),
                            "post-build compile_gate retry",
                            CaveBuildActionPacing.ActionWeight.Normal);
                        return true;
                    }

                    UnityEngine.Debug.LogError(
                        "[CaveCursor] compile_gate exhausted retries — fix compile errors manually, then Re-grade.");
                    _postBuildWorkflowActive = false;
                    CaveBuildDialogPolicy.Notify(
                        "Cursor Agent",
                        "Compile errors remain after 3 compile_gate passes. Fix errors in Console, then Re-grade.");
                    return false;
                }

                _compileGateAttempts = 0;
                _mandatoryPhaseIndex = CaveBuildPromptLadder.PostBuildMandatoryPhases.Length;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () => BeginLadderPhaseAfterWorkflow(null, null, null),
                    "post-build ladder after compile_gate",
                    CaveBuildActionPacing.ActionWeight.Normal);
                return true;
            }

            if (phase == CaveBuildPromptLadder.RungResearch)
            {
                _mandatoryPhaseIndex = 1;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () => StartPostBuildWorkflowStep(null, null, null),
                    "post-build compile_gate phase",
                    CaveBuildActionPacing.ActionWeight.Normal);
                return true;
            }

            if (_postBuildWorkflowLadderPhase)
            {
                _ladderChainCompletedRungs.Add(phase);
                if (ScheduleNextLadderRungWithDelay())
                    return true;

                CaveBuildWorkflowExporter.RecordPhaseResult("ladder_fixes", true);
                CaveBuildWorkflowExporter.RecordPhaseResult("verify_rebuild", true);
                _postBuildWorkflowActive = false;
                UnityEngine.Debug.Log("[CaveCursor] Post-build workflow complete (research → compile → ladder).");

                var settings = CaveBuildCursorSettings.LoadOrCreate();
                settings.LoadFromPrefs();
                if (settings.autoRebuildAfterAgentSuccess &&
                    !CaveBuildPipelineCompletion.ShouldBlockAutoRebuildAfterAgent())
                {
                    ScheduleAutoRebuildAfterCompile();
                    CaveBuildDialogPolicy.Notify(
                        "Cursor Agent",
                        $"Workflow finished ({elapsedSeconds:F0}s). Unity will re-build the cave after scripts compile.");
                }
                else
                {
                    var note = CaveBuildPipelineCompletion.ShouldBlockAutoRebuildAfterAgent()
                        ? "Workflow finished. Auto-rebuild is suppressed after a full pipeline run — use Build Complete Cave Level when ready."
                        : "Workflow finished. Run Build Complete Cave Level to apply fixes.";
                    CaveBuildDialogPolicy.Notify("Cursor Agent", note);
                }

                return false;
            }

            _postBuildWorkflowActive = false;
            return false;
        }

        static IReadOnlyList<string> LoadFailingRungsFromDisk()
        {
            var path = Path.Combine(ResolveHubRoot(), CaveBuildAgentContextExporter.LadderContextPath);
            if (!File.Exists(path))
                return CaveBuildPromptLadder.CursorRungOrder;

            try
            {
                var ctx = JsonUtility.FromJson<LadderContextDisk>(File.ReadAllText(path));
                if (ctx?.failingRungs != null && ctx.failingRungs.Length > 0)
                    return ctx.failingRungs;
            }
            catch
            {
                // ignored
            }

            return CaveBuildPromptLadder.CursorRungOrder;
        }

        static void LogLadderInvokeBanner(string rung, int chainIndex, int chainTotal)
        {
            var hub = ResolveHubRoot();
            var grade = "?";
            var score = "?";
            var dud = "?";
            var strict = "?";
            var topFails = "";

            var reportPath = Path.Combine(hub, "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json");
            if (File.Exists(reportPath))
            {
                try
                {
                    var disk = JsonUtility.FromJson<QualityReportDisk>(File.ReadAllText(reportPath));
                    if (disk != null)
                    {
                        grade = disk.letterGrade ?? grade;
                        score = disk.overallScore.ToString();
                        dud = disk.isDud.ToString().ToLowerInvariant();
                        strict = disk.meetsStrictTarget.ToString().ToLowerInvariant();
                        if (disk.topFailingStages != null && disk.topFailingStages.Length > 0)
                            topFails = string.Join(", ", disk.topFailingStages);
                    }
                }
                catch
                {
                    // ignored
                }
            }

            UnityEngine.Debug.Log(
                "[CaveCursor] ═══════════════════════════════════════\n" +
                $"[CaveCursor] LADDER INVOKE  rung={rung}  pass={chainIndex}/{chainTotal}\n" +
                $"[CaveCursor] Grade {grade} ({score}/100)  dud={dud}  strictTarget={strict}\n" +
                (string.IsNullOrEmpty(topFails)
                    ? "[CaveCursor] Failing stages: (see CaveBuildFailingStages.json)\n"
                    : $"[CaveCursor] Failing stages: {topFails}\n") +
                "[CaveCursor] Prompt → Assets/EnvironmentKit/Generated/CaveBuildAgentPrompt.md\n" +
                "[CaveCursor] ═══════════════════════════════════════");
        }

        static string ResolveActiveRungFromDisk()
        {
            var path = Path.Combine(ResolveHubRoot(), CaveBuildAgentContextExporter.LadderContextPath);
            if (!File.Exists(path))
                return CaveBuildPromptLadder.RungVisualShell;

            try
            {
                var ctx = JsonUtility.FromJson<LadderContextDisk>(File.ReadAllText(path));
                return string.IsNullOrWhiteSpace(ctx.activeRung)
                    ? CaveBuildPromptLadder.RungVisualShell
                    : ctx.activeRung;
            }
            catch
            {
                return CaveBuildPromptLadder.RungVisualShell;
            }
        }

        [Serializable]
        class LadderContextDisk
        {
            public string activeRung;
            public string[] failingRungs;
        }

        [Serializable]
        class PreBuildLadderContextDisk
        {
            public string activeRung;
            public string[] failingRungs;
        }

        [Serializable]
        class QualityReportDisk
        {
            public string letterGrade;
            public int overallScore;
            public bool isDud;
            public bool meetsStrictTarget;
            public string[] topFailingStages;
        }

        public static void CancelPendingAutoRebuild()
        {
            _autoRebuildPending = false;
            CaveBuildLayoutRollSession.ClearPreserveRequest();
            EditorApplication.update -= PollAutoRebuildAfterCompile;
        }

        public static void ScheduleAutoRebuildAfterCompile()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (settings.stabilizationMode)
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Stabilization mode: skipping auto-rebuild schedule (manual trigger only).");
                return;
            }

            CaveBuildLayoutRollSession.RequestPreserveNextBuild();
            _autoRebuildPending = true;
            _autoRebuildNextAttemptAt = EditorApplication.timeSinceStartup + 0.5;
            _autoRebuildDeferLogCount = 0;
            CaveBuildPipelineCompletion.AllowAutoRebuildAfterAgent();
            EditorApplication.update -= PollAutoRebuildAfterCompile;
            EditorApplication.update += PollAutoRebuildAfterCompile;
        }

        static void PollAutoRebuildAfterCompile()
        {
            if (!_autoRebuildPending)
            {
                EditorApplication.update -= PollAutoRebuildAfterCompile;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < _autoRebuildNextAttemptAt)
                return;

            if (EditorApplication.isCompiling)
            {
                _autoRebuildNextAttemptAt = now + 1.0;
                return;
            }

            if (LavaTubeCaveBuildPipeline.IsPhasedBuildActive)
            {
                var queuedStep = CaveBuildRunStatusPublisher.CurrentQueuedStep;
                if (_autoRebuildDeferLogCount == 0 ||
                    (queuedStep >= 48 && queuedStep < 57))
                    LavaTubeCaveBuildPipeline.ResumePipelineAfterCursorAgent();
                else if (queuedStep == LavaTubeCaveBuildPipeline.ManifestQueuedStepIndex)
                {
                    LavaTubeCaveBuildPipeline.ResetManifestFinalizeResumeArmed();
                    LavaTubeCaveBuildPipeline.ResumePipelineAfterManifestIfStuck();
                }
                else if (_autoRebuildDeferLogCount == 1)
                    LavaTubeCaveBuildPipeline.ResumePipelineAfterManifestIfStuck();
                _autoRebuildDeferLogCount++;
                if (_autoRebuildDeferLogCount == 1 || _autoRebuildDeferLogCount % 15 == 0)
                {
                    var s = CaveBuildRunStatusPublisher.CurrentQueuedStep;
                    UnityEngine.Debug.Log(
                        $"[CaveCursor] Auto-rebuild waiting — pipeline still at step {s + 1}/{CaveBuildRunStatusPublisher.QueuedStepTotal}…");
                }

                _autoRebuildNextAttemptAt = now + 1.0;
                return;
            }

            if (CaveBuildCursorAgentBridge.IsAgentRunning)
            {
                _autoRebuildNextAttemptAt = now + 1.0;
                return;
            }

            if (CaveBuildActionPacing.IsBusy && CaveBuildActionPacing.IsHeavyRunning)
            {
                _autoRebuildNextAttemptAt = now + 0.75;
                return;
            }

            _autoRebuildPending = false;
            EditorApplication.update -= PollAutoRebuildAfterCompile;
            UnityEngine.Debug.Log("[CaveCursor] Auto-rebuild: Build Complete Cave (same seed as last layout roll)…");
            LavaTubeCaveBuilder.BuildInActiveScene(
                openMainSceneFirst: false,
                hideLegacyBlockout: true,
                skipDialogs: true);
        }

        [MenuItem("Window/Environment Kit/Cave Build/Sync API Key from .env")]
        public static void MenuSyncApiKeyFromDotEnv()
        {
            CaveBuildCursorSettings.SyncApiKeyFromDotEnvToEditorPrefs();
            EditorUtility.DisplayDialog(
                "Cursor API Key",
                CaveBuildCursorAgentBridge.HasApiKey
                    ? "Synced from Tools/cave-grader/.env — Unity auto-invoke can use this key."
                    : $"No key found. Create:\n{CaveBuildCursorSettings.DotEnvPath}",
                "OK");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Invoke Cursor Agent (Grade & Fix)")]
        public static void MenuInvoke()
        {
            if (TryInvokeGradeAndFix(out var msg))
                EditorUtility.DisplayDialog("Cursor Agent", "Agent run finished. Check Console for details.", "OK");
            else
                EditorUtility.DisplayDialog("Cursor Agent", msg, "OK");
            if (!string.IsNullOrEmpty(msg))
                UnityEngine.Debug.Log("[CaveCursor] " + msg);
        }

        /// <summary>After grading: invoke Cursor when settings allow (default: every non-perfect build).</summary>
        public static void TryAutoInvokeAfterGrade(CaveBuildQualityReport report)
        {
            if (report == null)
            {
                UnityEngine.Debug.Log("[CaveCursor] Auto-invoke after grade skipped — no quality report.");
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (!settings.autoInvokeAfterEveryBuild && !settings.autoInvokeOnDud)
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Auto-invoke after grade skipped — autoInvokeAfterEveryBuild and autoInvokeOnDud are both off in Cave Build Cursor Settings.");
                return;
            }

            if (!HasApiKey)
            {
                UnityEngine.Debug.LogWarning(
                    "[CaveCursor] Auto-invoke after grade skipped — set CURSOR_API_KEY or save key in Cave Build Cursor Settings.");
                return;
            }

            if (IsAgentRunning)
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Auto-invoke after grade skipped — Cursor agent already running.");
                return;
            }

            if (!ShouldInvokeForReport(report, settings, out var skipReason))
            {
                UnityEngine.Debug.Log("[CaveCursor] Auto-invoke after grade skipped — " + skipReason);
                return;
            }

            if (!TryInvokeGradeAndFixBackground(out var msg, startLadderChain: false))
                UnityEngine.Debug.LogWarning("[CaveCursor] Auto-invoke after grade failed: " + msg);
        }

        public static void TryAutoInvokeOnDud(CaveBuildQualityReport report) =>
            TryAutoInvokeAfterGrade(report);

        /// <summary>
        /// Final invoke after a full build. Skips when Ship target is already met (no post-build Cursor loop).
        /// </summary>
        public static void TryAutoInvokeAfterBuildComplete(
            CaveBuildQualityReport report,
            Transform caveRoot = null,
            SceneGroundInfo ground = null,
            bool afterPostBuildPasses = false)
        {
            if (report == null)
            {
                UnityEngine.Debug.Log(
                    "[CaveCursor] Auto-invoke after build skipped — no quality report.");
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var phase = afterPostBuildPasses ? "post-build passes" : "build complete";

            if (!settings.autoInvokeAfterEveryBuild && !settings.autoInvokeOnDud)
            {
                UnityEngine.Debug.Log(
                    $"[CaveCursor] Auto-invoke after {phase} skipped — autoInvokeAfterEveryBuild and autoInvokeOnDud are both off.");
                return;
            }

            if (!HasApiKey)
            {
                UnityEngine.Debug.LogWarning(
                    $"[CaveCursor] Auto-invoke after {phase} skipped — set CURSOR_API_KEY or save key in Cave Build Cursor Settings.");
                return;
            }

            if (IsAgentRunning)
            {
                UnityEngine.Debug.Log(
                    $"[CaveCursor] Auto-invoke after {phase} skipped — Cursor agent already running (grade {report.LetterGrade} {report.OverallScore}/100).");
                return;
            }

            if (!ShouldInvokeForReport(report, settings, out var skipReason))
            {
                UnityEngine.Debug.Log(
                    $"[CaveCursor] Auto-invoke after {phase} skipped — {skipReason}");
                return;
            }

            if (CaveBuildPipelineCompletion.ShouldSuppressPostBuildCursorInvoke())
            {
                UnityEngine.Debug.Log(
                    $"[CaveCursor] Auto-invoke after {phase} skipped — post-build Cursor already started for this build.");
                return;
            }

            CaveBuildPipelineCompletion.MarkPostBuildCursorStarted();
            CaveBuildPostBuildWorkflow.BeginAfterCaveGeneration(report, caveRoot, ground);
        }

        static bool ShouldInvokeForReport(
            CaveBuildQualityReport report,
            CaveBuildCursorSettings settings,
            out string skipReason)
        {
            skipReason = null;

            if (CaveBuildQualityRubric.MeetsShipTarget(report))
            {
                skipReason =
                    $"build already meets Ship target ({report.LetterGrade} {report.OverallScore}/100, " +
                    $"{CaveBuildQualityRubric.TargetGrade} {CaveBuildQualityRubric.ShipScore}+).";
                return false;
            }

            if (settings.autoInvokeAfterEveryBuild)
                return true;

            if (!settings.autoInvokeOnDud)
            {
                skipReason = "autoInvokeOnDud is off.";
                return false;
            }

            if (report.IsDud ||
                report.RecommendedAction == CaveBuildRecommendedAction.InvokeCursorAgent ||
                !report.BuildAcceptable)
                return true;

            skipReason =
                $"build acceptable ({report.LetterGrade} {report.OverallScore}/100) and not a dud — autoInvokeOnDud only.";
            return false;
        }
    }
}
