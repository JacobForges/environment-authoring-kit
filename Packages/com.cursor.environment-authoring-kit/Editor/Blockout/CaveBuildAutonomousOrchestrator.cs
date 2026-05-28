#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Runs until Ship target: AI next-steps + do-not prompts, phase prompts, Cursor invoke, compile, re-grade.
    /// </summary>
    public static class CaveBuildAutonomousOrchestrator
    {
        enum State
        {
            Idle,
            Cooldown,
            ExportPrompts,
            InvokeAgent,
            WaitAgent,
            WaitCompile,
            Regrade,
            Done,
        }

        static State _state = State.Idle;
        static int _iteration;
        static int _maxIterations;
        static double _phaseReadyAt;
        static CaveBuildQualityReport _quality;
        static Transform _caveRoot;
        static SceneGroundInfo _ground;
        static WorldGenerationRequest _request;
        static CaveLayoutRoll _roll;
        static string _sceneName;
        static Action _onComplete;
        static string _activeRung;
        static string _activePhaseId;
        static bool _rebuildScheduled;

        public static bool IsRunning => _state != State.Idle && _state != State.Done;

        public static void ForceStop(string reason = "user abort")
        {
            if (_state == State.Idle || _state == State.Done)
                return;

            _state = State.Done;
            EditorApplication.update -= Tick;
            CaveBuildPipelineLog.Info("Autonomous loop aborted: " + reason, "Autonomous");
            _onComplete = null;
        }

        public static void Begin(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            CaveLayoutRoll roll,
            string sceneName,
            Action onComplete)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[CaveBuild] Autonomous loop already running.");
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.enableAutonomousUntilShip)
            {
                onComplete?.Invoke();
                return;
            }

            if (!CaveBuildCursorAgentBridge.HasApiKey)
            {
                Debug.LogWarning("[CaveBuild] Autonomous loop skipped — no CURSOR_API_KEY.");
                onComplete?.Invoke();
                return;
            }

            _quality = quality;
            _caveRoot = caveRoot;
            _ground = ground;
            _request = request;
            _roll = roll;
            _sceneName = sceneName;
            _onComplete = onComplete;
            _iteration = 0;
            _maxIterations = Mathf.Max(1, settings.maxAutonomousIterations);
            _rebuildScheduled = false;
            _state = State.Cooldown;
            _phaseReadyAt = EditorApplication.timeSinceStartup;

            CaveBuildPipelineLog.Info(
                $"Autonomous fix loop started (max {_maxIterations} iterations). " +
                "Open Pipeline Console for live status.",
                "Autonomous");

            if (settings.showReloopDialog)
                CaveBuildDialogPolicy.TryShowReloopDialog(_iteration + 1, _maxIterations);

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (_state == State.Idle || _state == State.Done)
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (EditorApplication.timeSinceStartup < _phaseReadyAt)
                return;

            try
            {
                switch (_state)
                {
                    case State.Cooldown:
                        EnterExportPrompts();
                        break;
                    case State.ExportPrompts:
                        break;
                    case State.InvokeAgent:
                        break;
                    case State.WaitAgent:
                        PollAgent();
                        break;
                    case State.WaitCompile:
                        PollCompile();
                        break;
                    case State.Regrade:
                        EnterRegrade();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Finish($"Autonomous loop error: {ex.Message}");
            }
        }

        static void EnterExportPrompts()
        {
            _activeRung = CaveBuildPromptLadder.PickActiveRung(_quality, _caveRoot, _ground);
            var mission = CaveBuildMeatLoopPassPlan.GetMission(Mathf.Min(_iteration, 15));
            _activePhaseId = PhaseIdForMission(mission.Pass, _activeRung);

            CaveBuildPipelineLog.Info(
                $"Iteration {_iteration + 1}/{_maxIterations} — {mission.Title} / phase {_activePhaseId}",
                "Autonomous");

            var helperCtx = new CaveBuildHelperScriptOrchestrator.Context
            {
                Request = _request,
                PhaseId = _activePhaseId,
                Rung = _activeRung,
                MeatPass = mission.Pass,
                AutonomousIteration = _iteration,
            };
            CaveBuildHelperScriptOrchestrator.Queue(
                CaveBuildHelperScriptOrchestrator.Moment.AutonomousIteration,
                helperCtx,
                (_, helperMsg) =>
                {
                    if (!string.IsNullOrEmpty(helperMsg))
                        Debug.Log("[CaveBuild] Autonomous helpers: " + helperMsg);

                    System.Environment.SetEnvironmentVariable(
                        "CAVE_AUTONOMOUS_ITERATION",
                        _iteration.ToString());
                    CaveBuildRungPromptExporter.PrepareAgentInvoke(
                        _activeRung, _caveRoot, _ground, _quality, _iteration);

                    _state = State.InvokeAgent;
                    _phaseReadyAt = EditorApplication.timeSinceStartup + 0.5;
                    ScheduleInvoke();
                });
        }

        static void ScheduleInvoke()
        {
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    if (_state != State.InvokeAgent)
                        return;

                    if (CaveBuildCursorAgentBridge.IsAgentRunning)
                    {
                        _state = State.WaitAgent;
                        _phaseReadyAt = EditorApplication.timeSinceStartup + 1.0;
                        return;
                    }

                    if (!CaveBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(
                            out var msg,
                            rung: _activeRung,
                            startLadderChain: false))
                    {
                        Debug.LogWarning("[CaveBuild] Autonomous invoke failed: " + msg);
                        Finish("Autonomous loop stopped — agent invoke failed.");
                        return;
                    }

                    CaveBuildPipelineLog.Info($"Cursor agent started (rung={_activeRung}, iteration={_iteration + 1})", "Autonomous");
                    _state = State.WaitAgent;
                    _phaseReadyAt = EditorApplication.timeSinceStartup + 2.0;
                },
                $"autonomous invoke {_activeRung}",
                CaveBuildActionPacing.ActionWeight.Heavy);
        }

        static void PollAgent()
        {
            if (CaveBuildCursorAgentBridge.IsAgentRunning)
            {
                _phaseReadyAt = EditorApplication.timeSinceStartup + 1.5;
                return;
            }

            _state = State.WaitCompile;
            _phaseReadyAt = EditorApplication.timeSinceStartup + 1.0;
            CaveBuildPipelineLog.Info("Agent idle — waiting for script compile", "Autonomous");
        }

        static void PollCompile()
        {
            if (EditorApplication.isCompiling)
            {
                _phaseReadyAt = EditorApplication.timeSinceStartup + 1.0;
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (settings.autoRebuildAfterAgentSuccess && !_rebuildScheduled && _caveRoot != null)
            {
                _rebuildScheduled = true;
                CaveBuildPipelineLog.Info("Scheduling scene rebuild to apply script fixes", "Autonomous");
                CaveBuildCursorAgentBridge.ScheduleAutoRebuildAfterCompile();
                _phaseReadyAt = EditorApplication.timeSinceStartup + 3.0;
                return;
            }

            _state = State.Regrade;
            _phaseReadyAt = EditorApplication.timeSinceStartup + 0.5;
        }

        static void EnterRegrade()
        {
            if (_caveRoot != null && _ground != null && _request != null)
            {
                _quality = CaveBuildQualitySystem.Grade(
                    _caveRoot,
                    _ground,
                    _request,
                    null,
                    gradingMode: $"autonomous_pass_{_iteration}",
                    invokeCursorAgent: false);
            }

            var meetsShip = _quality != null && CaveBuildQualityRubric.MeetsShipTarget(_quality);
            CaveBuildPipelineLog.Info(
                $"Re-grade: {_quality?.LetterGrade} ({_quality?.OverallScore}/100) ship={meetsShip}",
                "Autonomous");

            if (meetsShip || _iteration + 1 >= _maxIterations)
            {
                var reason = meetsShip ? "Ship target met" : "Max iterations reached";
                Finish(reason);
                return;
            }

            _iteration++;
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var cooldown = Mathf.Max(1f, settings.autonomousIterationCooldownSeconds);
            _state = State.Cooldown;
            _phaseReadyAt = EditorApplication.timeSinceStartup + cooldown;
            _rebuildScheduled = false;

            if (settings.showReloopDialog)
                CaveBuildDialogPolicy.TryShowReloopDialog(_iteration + 1, _maxIterations);

            CaveBuildPipelineLog.Info($"Continuing autonomous loop — cooldown {cooldown:F1}s", "Autonomous");
        }

        static void Finish(string reason)
        {
            _state = State.Done;
            EditorApplication.update -= Tick;
            CaveBuildPipelineLog.Info("Autonomous loop finished: " + reason, "Autonomous");
            _onComplete?.Invoke();
            _onComplete = null;
        }

        static string PhaseIdForMission(int meatPass, string rung)
        {
            switch (meatPass)
            {
                case 0: return "visual_shell";
                case 1: return "ground_placement";
                case 2: return "floor_collision";
                case 5: return "materials_lighting";
                case 6: return "atmosphere_fog";
                case 7: return "mob_spawns";
                case 10: return "performance";
                case 14:
                case 15: return "packaging_ship";
            }

            if (rung == CaveBuildPromptLadder.RungGroundPlacement)
                return "ground_placement";
            if (rung == CaveBuildPromptLadder.RungVisualShell)
                return "visual_shell";
            if (rung == CaveBuildPromptLadder.RungFloorCollision)
                return "floor_collision";
            if (rung == CaveBuildPromptLadder.RungMaterials)
                return "materials_lighting";
            if (rung == CaveBuildPromptLadder.RungPerformance)
                return "performance";
            if (rung == CaveBuildPromptLadder.RungResearch)
                return "research";
            return "packaging_ship";
        }
    }
}
#endif
