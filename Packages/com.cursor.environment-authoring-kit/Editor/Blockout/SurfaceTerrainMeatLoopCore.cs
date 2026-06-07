#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Shared grade → fix prompt → apply fix loop for terrain geo and props meat passes.</summary>
    internal static class SurfaceTerrainMeatLoopCore
    {
        public enum Kind
        {
            Geo,
            Props,
        }

        internal sealed class Config
        {
            public Kind LoopKind;
            public string GradingMode;
            public string WorkflowEnv;
            public string PhaseId;
            public string LogPrefix;
            public int MaxFixRounds = 4;
            public int TargetOverallScore = 85;
            public Func<string, bool> RungFilter;
            public Action<SurfaceTerrainAiPhases.QueueState> OnComplete;
            public bool RequirePlayableLandBeforeFinish;
        }

        enum Phase
        {
            Preflight,
            Grade,
            ExportFixPrompt,
            ApplyFix,
        }

        sealed class MeatState
        {
            public Config Config;
            public SurfaceTerrainAiPhases.QueueState TerrainState;
            public Phase Phase = Phase.Preflight;
            public int FixRound;
            public int GradeRungIndex;
            public int FixesApplied;
            public string ActiveRung;
            public HashSet<string> SkipRungs = new();
            public SurfaceTerrainLadderReport Report;
            public SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog VegCatalog;
            public string LastFixRung;
            public int LastOverallScore = -1;
            public int SameRungStreak;
            public bool HelpersQueued;
        }

        static readonly Dictionary<Kind, MeatState> ActiveByKind = new();

        public static bool IsRunning(Kind kind) =>
            ActiveByKind.TryGetValue(kind, out var s) && s != null;

        public static bool IsAnyRunning
        {
            get
            {
                foreach (var kv in ActiveByKind)
                {
                    if (kv.Value != null)
                        return true;
                }

                return false;
            }
        }

        public static void Queue(SurfaceTerrainAiPhases.QueueState state, Config config)
        {
            if (state?.Ground?.Terrain == null)
            {
                config.OnComplete?.Invoke(state);
                return;
            }

            if (IsRunning(config.LoopKind))
            {
                CaveBuildEditorLog.LogSurface(
                    $"{config.LogPrefix} Already running — duplicate queue ignored.",
                    forceUnityConsole: true);
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"{config.LogPrefix} Starting — up to {config.MaxFixRounds} fix rounds (grade → prompt → apply).",
                forceUnityConsole: true);

            var meat = new MeatState
            {
                Config = config,
                TerrainState = state,
                Report = state.LadderReport ?? new SurfaceTerrainLadderReport
                {
                    SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    Seed = state.Request?.Seed ?? 0,
                    GradingMode = config.GradingMode,
                    GradingTask = CaveBuildGradingProfile.ResolveGradingTask(state.Request, config.GradingMode),
                },
            };

            if (meat.Report.Stages == null || meat.Report.Stages.Count == 0)
                meat.Report.Stages = new List<TerrainStageGrade>();

            state.LadderReport = meat.Report;
            state.LadderReport.GradingMode = config.GradingMode;
            ActiveByKind[config.LoopKind] = meat;
            ScheduleStep(meat);
        }

        static void ScheduleStep(MeatState meat) =>
            CaveBuildActionPacing.ScheduleBuildStep(
                () => RunStep(meat),
                CaveBuildPipelineDomains.QueueLabel($"{meat.Config.LogPrefix} {meat.Phase}"),
                CaveBuildActionPacing.ActionWeight.Light);

        static void Finish(MeatState meat)
        {
            EditorUtility.ClearProgressBar();
            var kind = meat.Config.LoopKind;
            ActiveByKind.Remove(kind);
            meat.Config.OnComplete?.Invoke(meat.TerrainState);
        }

        static void RunStep(MeatState meat)
        {
            if (meat == null || !ActiveByKind.TryGetValue(meat.Config.LoopKind, out var active) || active != meat)
                return;

            var state = meat.TerrainState;
            if (state?.Ground?.Terrain == null)
            {
                Finish(meat);
                return;
            }

            switch (meat.Phase)
            {
                case Phase.Preflight:
                    RunPreflight(meat);
                    break;
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

        static void RunPreflight(MeatState meat)
        {
            if (meat.Config.LoopKind != Kind.Geo)
            {
                meat.Phase = Phase.Grade;
                ScheduleStep(meat);
                return;
            }

            var status = SurfaceDefaultTerrainDetector.Evaluate(
                meat.TerrainState.Ground,
                meat.TerrainState.Request);

            if (!status.unusableLand)
            {
                meat.Phase = Phase.Grade;
                ScheduleStep(meat);
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"{meat.Config.LogPrefix} Unusable/default land detected (score {status.score}) — remediating before geo grade.",
                forceUnityConsole: true);

            SurfaceDefaultTerrainDetector.QueueRemediateDefaultLand(
                meat.TerrainState.Ground,
                meat.TerrainState.Request,
                (_, _) =>
                {
                    meat.Phase = Phase.Grade;
                    ScheduleStep(meat);
                });
        }

        static IEnumerable<SurfaceTerrainBuildLadder.TerrainRungDef> FilteredRungs(Func<string, bool> filter)
        {
            foreach (var def in SurfaceTerrainBuildLadder.RungOrder)
            {
                if (filter == null || filter(def.Id))
                    yield return def;
            }
        }

        static void RunGradePhase(MeatState meat)
        {
            var state = meat.TerrainState;
            var cfg = meat.Config;

            if (meat.FixRound >= cfg.MaxFixRounds)
            {
                Debug.LogWarning(
                    $"{cfg.LogPrefix} Fix cap reached — see TerrainBuildTailoredAgentPrompt.md and ladder JSON.");
                Finish(meat);
                return;
            }

            var rungs = new List<SurfaceTerrainBuildLadder.TerrainRungDef>(FilteredRungs(cfg.RungFilter));
            var rungTotal = rungs.Count;

            if (meat.GradeRungIndex == 0)
            {
                state.Surface = SurfaceTerrainAiPhases.ResolveSurfaceRootPublic(state);
                if (cfg.LoopKind == Kind.Geo)
                    SurfaceTerrainBuildLadder.ClearCachedGradedReport();
            }

            if (meat.GradeRungIndex < rungTotal)
            {
                var i = meat.GradeRungIndex;
                var def = rungs[i];
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    cfg.LogPrefix,
                    $"grade {i + 1}/{rungTotal} ({def.Id})");
                var stage = SurfaceTerrainBuildLadder.GradeOneRung(
                    def,
                    state.Ground,
                    state.Request,
                    state.Surface,
                    ref meat.VegCatalog);
                UpsertStage(meat.Report, stage);
                meat.GradeRungIndex++;
                CaveBuildActionPacing.ScheduleBuildStep(
                    () => RunGradePhase(meat),
                    CaveBuildPipelineDomains.QueueLabel($"{cfg.LogPrefix} grade {meat.GradeRungIndex}/{rungTotal}"),
                    CaveBuildActionPacing.ActionWeight.Light);
                return;
            }

            meat.GradeRungIndex = 0;
            SurfaceTerrainBuildLadder.FinalizeReport(meat.Report, state.Ground, state.Request, state.Surface);
            state.LadderReport = meat.Report;

            var loopScore = ScoreForFilter(meat.Report, cfg.RungFilter);
            var acceptable = loopScore >= cfg.TargetOverallScore && !HasFailingInFilter(meat.Report, cfg.RungFilter);

            CaveBuildEditorLog.LogSurface(
                $"{cfg.LogPrefix} Grade → loop {loopScore}/100, acceptable={acceptable}.",
                forceUnityConsole: true);

            if (acceptable)
            {
                if (cfg.LoopKind == Kind.Geo && cfg.RequirePlayableLandBeforeFinish)
                {
                    var land = SurfaceDefaultTerrainDetector.Evaluate(state.Ground, state.Request);
                    if (land.unusableLand)
                    {
                        meat.ActiveRung = "heightfield_no_craters";
                        meat.Phase = Phase.ApplyFix;
                        ScheduleStep(meat);
                        return;
                    }
                }

                Finish(meat);
                return;
            }

            meat.ActiveRung = SurfaceTerrainBuildLadder.PickActiveRung(meat.Report, meat.SkipRungs, cfg.RungFilter);
            if (string.IsNullOrEmpty(meat.ActiveRung))
            {
                Finish(meat);
                return;
            }

            if (meat.ActiveRung == meat.LastFixRung && loopScore <= meat.LastOverallScore)
                meat.SameRungStreak++;
            else
            {
                meat.LastFixRung = meat.ActiveRung;
                meat.SameRungStreak = 1;
            }

            meat.LastOverallScore = loopScore;

            if (meat.SameRungStreak >= 3)
            {
                TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(
                    meat.ActiveRung,
                    meat.Report,
                    state.Request?.Seed ?? 0,
                    meat.FixRound,
                    cfg.WorkflowEnv,
                    cfg.PhaseId);
                meat.SkipRungs.Add(meat.ActiveRung);
                meat.SameRungStreak = 0;
                meat.ActiveRung = SurfaceTerrainBuildLadder.PickActiveRung(meat.Report, meat.SkipRungs, cfg.RungFilter);
                if (string.IsNullOrEmpty(meat.ActiveRung))
                {
                    Finish(meat);
                    return;
                }
            }

            meat.Phase = Phase.ExportFixPrompt;
            meat.HelpersQueued = false;
            ScheduleStep(meat);
        }

        static void UpsertStage(SurfaceTerrainLadderReport report, TerrainStageGrade stage)
        {
            var idx = report.Stages.FindIndex(s => s.StageId == stage.StageId);
            if (idx >= 0)
                report.Stages[idx] = stage;
            else
                report.Stages.Add(stage);
        }

        static int ScoreForFilter(SurfaceTerrainLadderReport report, Func<string, bool> filter)
        {
            var totalWeight = 0;
            var weighted = 0;
            foreach (var s in report.Stages)
            {
                if (filter != null && !filter(s.StageId))
                    continue;
                totalWeight += s.Weight;
                weighted += s.Score * s.Weight;
            }

            return totalWeight > 0 ? Mathf.RoundToInt(weighted / (float)totalWeight) : report.OverallScore;
        }

        static bool HasFailingInFilter(SurfaceTerrainLadderReport report, Func<string, bool> filter)
        {
            foreach (var s in report.Stages)
            {
                if (filter != null && !filter(s.StageId))
                    continue;
                if (!s.Passed || s.Score < SurfaceTerrainBuildLadder.StagePassScore)
                    return true;
            }

            return false;
        }

        static void RunExportFixPromptPhase(MeatState meat)
        {
            var state = meat.TerrainState;
            var cfg = meat.Config;
            var rung = meat.ActiveRung;

            if (!meat.HelpersQueued)
            {
                meat.HelpersQueued = true;
                CaveBuildHelperScriptOrchestrator.Queue(
                    CaveBuildHelperScriptOrchestrator.Moment.TerrainMeatPassStart,
                    new CaveBuildHelperScriptOrchestrator.Context
                    {
                        Request = state.Request,
                        MeatPass = meat.FixRound,
                        Rung = rung,
                        PhaseId = cfg.PhaseId,
                    },
                    (_, msg) =>
                    {
                        if (!string.IsNullOrEmpty(msg))
                            Debug.LogWarning(cfg.LogPrefix + " helpers: " + msg);
                        TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(
                            rung,
                            meat.Report,
                            state.Request?.Seed ?? 0,
                            meat.FixRound,
                            cfg.WorkflowEnv,
                            cfg.PhaseId);
                        meat.Phase = Phase.ApplyFix;
                        ScheduleStep(meat);
                    });
                return;
            }

            TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(
                rung,
                meat.Report,
                state.Request?.Seed ?? 0,
                meat.FixRound,
                cfg.WorkflowEnv,
                cfg.PhaseId);
            meat.Phase = Phase.ApplyFix;
            ScheduleStep(meat);
        }

        static void RunApplyFixPhase(MeatState meat)
        {
            var state = meat.TerrainState;
            var cfg = meat.Config;
            var rung = meat.ActiveRung;

            TryInvokeCursor(rung, meat.Report, cfg);

            if (cfg.LoopKind == Kind.Geo && rung == "heightfield_no_craters")
            {
                var land = SurfaceDefaultTerrainDetector.Evaluate(state.Ground, state.Request);
                if (land.unusableLand)
                {
                    SurfaceDefaultTerrainDetector.QueueRemediateDefaultLand(
                        state.Ground,
                        state.Request,
                        (_, _) =>
                        {
                            meat.FixRound++;
                            meat.Phase = Phase.Grade;
                            ScheduleStep(meat);
                        });
                    return;
                }
            }

            SurfaceTerrainLadderFixer.QueueTryFix(
                rung,
                state.Ground,
                state.Request,
                state.Surface,
                (fixedOk, action) =>
                {
                    if (!fixedOk)
                        meat.SkipRungs.Add(rung);
                    else
                        meat.FixesApplied++;

                    meat.FixRound++;
                    meat.Phase = Phase.Grade;
                    ScheduleStep(meat);
                });
        }

        static void TryInvokeCursor(string rung, SurfaceTerrainLadderReport report, Config cfg)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (settings.suppressMeatLoopCursorInvokes)
                return;

            if (!settings.autoInvokeEachMeatLoopPass && !settings.autoInvokeTerrainAfterSurfaceBuild)
                return;

            if (!CaveBuildCursorAgentBridge.HasApiKey || CaveBuildCursorAgentBridge.IsAgentRunning)
                return;

            System.Environment.SetEnvironmentVariable("CAVE_WORKFLOW", cfg.WorkflowEnv);
            TerrainBuildRungPromptExporter.PrepareAgentInvokeFromReport(rung, report, out _);
            if (TerrainBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(out var msg, rung))
                CaveBuildEditorLog.LogSurface(cfg.LogPrefix + " Cursor: " + msg, forceUnityConsole: true);
        }
    }
}
#endif
