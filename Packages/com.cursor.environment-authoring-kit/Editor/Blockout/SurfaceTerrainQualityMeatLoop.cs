#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Terrain: grade all rungs → write tailored fix prompt (issues + fixes) → apply one fix → re-grade.
    /// Never re-grades without going through the fix stage first.
    /// </summary>
    static class SurfaceTerrainQualityMeatLoop
    {
        public const int MaxFixRounds = 8;
        public const int TargetOverallScore = 85;

        enum Phase
        {
            Grade,
            ExportFixPrompt,
            ApplyFix,
        }

        sealed class MeatState
        {
            public SurfaceTerrainAiPhases.QueueState TerrainState;
            public Phase Phase = Phase.Grade;
            public int FixRound;
            public int FixesApplied;
            public string ActiveRung;
            public HashSet<string> SkipRungs = new();
            public SurfaceTerrainLadderReport Report;
            public SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog VegCatalog;
            public string LastFixRung;
            public int LastOverallScore = -1;
            public int SameRungStreak;
            public bool PromptReady;
            public bool HelpersQueued;
        }

        static MeatState _active;

        public static bool IsRunning => _active != null;

        public static void QueueAfterTerrainPhases(SurfaceTerrainAiPhases.QueueState state)
        {
            if (state?.Ground?.Terrain == null)
            {
                SurfaceTerrainAiPhases.ContinueAfterTerrainMeatLoop(state);
                return;
            }

            if (_active != null)
            {
                CaveBuildEditorLog.LogSurface(
                    "[TerrainMeat] Already running — ignoring duplicate queue.",
                    forceUnityConsole: true);
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Terrain meat loop — up to {MaxFixRounds} rounds: grade → fix prompt → apply fix → re-grade.",
                forceUnityConsole: true);

            _active = new MeatState
            {
                TerrainState = state,
                Report = state.LadderReport ?? new SurfaceTerrainLadderReport
                {
                    SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    Seed = state.Request?.Seed ?? 0,
                    GradingMode = "terrain_meat_loop",
                },
            };
            state.LadderReport = _active.Report;
            ScheduleStep(_active);
        }

        static void ScheduleStep(MeatState meat) =>
            CaveBuildActionPacing.ScheduleHeavyChain(
                () => RunStep(meat),
                CaveBuildPipelineDomains.SurfaceQueueLabel(
                    $"terrain meat {meat.Phase} round {meat.FixRound + 1}/{MaxFixRounds}"));

        static void Finish(MeatState meat, SurfaceTerrainAiPhases.QueueState state)
        {
            EditorUtility.ClearProgressBar();
            _active = null;
            SurfaceTerrainAiPhases.ContinueAfterTerrainMeatLoop(state);
        }

        static void RunStep(MeatState meat)
        {
            if (meat == null || meat != _active)
                return;

            var state = meat.TerrainState;
            if (state?.Ground?.Terrain == null)
            {
                Finish(meat, state);
                return;
            }

            switch (meat.Phase)
            {
                case Phase.Grade:
                    RunGradePhase(meat);
                    break;
                case Phase.ExportFixPrompt:
                    RunExportFixPromptPhase(meat);
                    break;
                case Phase.ApplyFix:
                    RunApplyFixPhase(meat);
                    break;
            }
        }

        static void RunGradePhase(MeatState meat)
        {
            var state = meat.TerrainState;
            if (meat.FixRound >= MaxFixRounds)
            {
                Debug.LogWarning(
                    "[TerrainMeat] Fix round cap reached — continuing build. See TerrainBuildTailoredAgentPrompt.md and SurfaceTerrainBuildLadderReport.json.");
                Finish(meat, state);
                return;
            }

            state.Surface = SurfaceTerrainAiPhases.ResolveSurfaceRootPublic(state);
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] Terrain meat round {meat.FixRound + 1}/{MaxFixRounds} — grading all rungs…",
                0.52f + 0.04f * (meat.FixRound / (float)MaxFixRounds));

            SurfaceTerrainBuildLadder.ClearCachedGradedReport();
            meat.Report.Stages.Clear();

            for (var i = 0; i < SurfaceTerrainBuildLadder.RungOrder.Length; i++)
            {
                var def = SurfaceTerrainBuildLadder.RungOrder[i];
                CaveBuildEditorLog.LogSurface(
                    $"[TerrainMeat] Grade {meat.FixRound + 1}/{MaxFixRounds} — rung {i + 1}/{SurfaceTerrainBuildLadder.RungOrder.Length}: {def.Id}",
                    forceUnityConsole: true);
                meat.Report.Stages.Add(
                    SurfaceTerrainBuildLadder.GradeOneRung(
                        def,
                        state.Ground,
                        state.Request,
                        state.Surface,
                        ref meat.VegCatalog));
            }

            SurfaceTerrainBuildLadder.FinalizeReport(meat.Report, state.Ground, state.Request, state.Surface);
            state.LadderReport = meat.Report;

            CaveBuildEditorLog.LogSurface(
                $"[TerrainMeat] Grade result → {meat.Report.OverallScore} ({meat.Report.LetterGrade}), acceptable={meat.Report.BuildAcceptable}.",
                forceUnityConsole: true);

            if (meat.Report.BuildAcceptable || meat.Report.OverallScore >= TargetOverallScore)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[TerrainMeat] PASS after {meat.FixRound + 1} round(s), {meat.FixesApplied} fix(es).",
                    forceUnityConsole: true);
                Finish(meat, state);
                return;
            }

            meat.ActiveRung = SurfaceTerrainBuildLadder.PickActiveRung(meat.Report, meat.SkipRungs);
            if (string.IsNullOrEmpty(meat.ActiveRung))
            {
                Debug.LogWarning("[TerrainMeat] No fixable rung — continuing build.");
                Finish(meat, state);
                return;
            }

            if (meat.ActiveRung == meat.LastFixRung &&
                meat.Report.OverallScore <= meat.LastOverallScore)
                meat.SameRungStreak++;
            else
            {
                meat.LastFixRung = meat.ActiveRung;
                meat.SameRungStreak = 1;
            }

            meat.LastOverallScore = meat.Report.OverallScore;

            if (meat.SameRungStreak >= 3)
            {
                Debug.LogWarning(
                    $"[TerrainMeat] Rung '{meat.ActiveRung}' did not improve after {meat.SameRungStreak} fix round(s). " +
                    $"Fix prompt: Assets/EnvironmentKit/Generated/TerrainBuildTailoredAgentPrompt.md — continuing build.");
                TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(
                    meat.ActiveRung,
                    meat.Report,
                    state.Request?.Seed ?? 0,
                    meat.FixRound);
                meat.SkipRungs.Add(meat.ActiveRung);
                meat.SameRungStreak = 0;
                meat.ActiveRung = SurfaceTerrainBuildLadder.PickActiveRung(meat.Report, meat.SkipRungs);
                if (string.IsNullOrEmpty(meat.ActiveRung))
                {
                    Finish(meat, state);
                    return;
                }
            }

            meat.Phase = Phase.ExportFixPrompt;
            meat.PromptReady = false;
            meat.HelpersQueued = false;
            ScheduleStep(meat);
        }

        static void RunExportFixPromptPhase(MeatState meat)
        {
            var state = meat.TerrainState;
            var rung = meat.ActiveRung;
            var seed = state.Request?.Seed ?? 0;

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] Terrain meat round {meat.FixRound + 1}/{MaxFixRounds} — building fix prompt ({rung})…",
                0.56f);

            if (!meat.HelpersQueued)
            {
                meat.HelpersQueued = true;
                var helperCtx = new CaveBuildHelperScriptOrchestrator.Context
                {
                    Request = state.Request,
                    MeatPass = meat.FixRound,
                    Rung = rung,
                    PhaseId = "terrain_meat_loop",
                };
                CaveBuildHelperScriptOrchestrator.Queue(
                    CaveBuildHelperScriptOrchestrator.Moment.TerrainMeatPassStart,
                    helperCtx,
                    (ok, msg) =>
                    {
                        if (!ok && !string.IsNullOrEmpty(msg))
                            Debug.LogWarning("[TerrainMeat] Helper scripts: " + msg);
                        RunExportFixPromptAfterHelpers(meat);
                    });
                return;
            }

            RunExportFixPromptAfterHelpers(meat);
        }

        static void RunExportFixPromptAfterHelpers(MeatState meat)
        {
            var state = meat.TerrainState;
            var rung = meat.ActiveRung;
            var seed = state.Request?.Seed ?? 0;

            if (!CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(rung, meat.Report, seed, meat.FixRound);
                meat.PromptReady = true;
                meat.Phase = Phase.ApplyFix;
                ScheduleStep(meat);
                return;
            }

            TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(rung, meat.Report, seed, meat.FixRound);
            meat.PromptReady = true;
            meat.Phase = Phase.ApplyFix;
            ScheduleStep(meat);
        }

        static void RunApplyFixPhase(MeatState meat)
        {
            var state = meat.TerrainState;
            var rung = meat.ActiveRung;
            var promptRel = TerrainBuildRungPromptExporter.TailoredPromptPath;

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] Terrain meat round {meat.FixRound + 1}/{MaxFixRounds} — applying fix: {rung}…",
                0.58f);

            CaveBuildEditorLog.LogSurface(
                $"[TerrainMeat] FIX STAGE rung={rung} — read `{promptRel}` (issues + Hub C# fixes).",
                forceUnityConsole: true);

            TryInvokeCursorForTerrainFix(rung, meat.Report);

            SurfaceTerrainLadderFixer.QueueTryFix(
                rung,
                state.Ground,
                state.Request,
                state.Surface,
                (fixedOk, action) =>
                {
                    if (!fixedOk)
                    {
                        meat.SkipRungs.Add(rung);
                        CaveBuildEditorLog.LogSurface(
                            $"[TerrainMeat] Auto-fix failed for '{rung}' — skipped. Use fix prompt + Cursor agent.",
                            forceUnityConsole: true);
                    }
                    else
                    {
                        meat.FixesApplied++;
                        CaveBuildEditorLog.LogSurface(
                            $"[TerrainMeat] Auto-fix [{rung}]: {action}",
                            forceUnityConsole: true);
                    }

                    meat.FixRound++;
                    meat.Phase = Phase.Grade;
                    ScheduleStep(meat);
                });
        }

        static void TryInvokeCursorForTerrainFix(string rung, SurfaceTerrainLadderReport report)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (settings.suppressMeatLoopCursorInvokes)
            {
                CaveBuildEditorLog.LogSurface(
                    "[TerrainMeat] Cursor invoke skipped (suppressMeatLoopCursorInvokes). Fix prompt is on disk.",
                    forceUnityConsole: true);
                return;
            }

            if (!settings.autoInvokeEachMeatLoopPass && !settings.autoInvokeTerrainAfterSurfaceBuild)
                return;

            if (!CaveBuildCursorAgentBridge.HasApiKey)
            {
                Debug.LogWarning("[TerrainMeat] Set CURSOR_API_KEY to run Cursor on TerrainBuildTailoredAgentPrompt.md");
                return;
            }

            if (CaveBuildCursorAgentBridge.IsAgentRunning)
            {
                CaveBuildEditorLog.LogSurface(
                    "[TerrainMeat] Cursor already running — finish fix prompt on disk when idle.",
                    forceUnityConsole: true);
                return;
            }

            TerrainBuildRungPromptExporter.PrepareAgentInvokeFromReport(rung, report, out _);
            if (TerrainBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(out var msg, rung))
                CaveBuildEditorLog.LogSurface("[TerrainMeat] Cursor agent started: " + msg, forceUnityConsole: true);
            else
                Debug.LogWarning("[TerrainMeat] Cursor invoke failed: " + msg);
        }
    }
}
#endif
