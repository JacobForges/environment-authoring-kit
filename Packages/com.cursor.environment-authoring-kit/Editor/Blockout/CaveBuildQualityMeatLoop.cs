using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Strict grade → fix → purge → re-grade until visual shell + rubric pass.
    /// Writes JSON for Cursor Agent review each pass.
    /// </summary>
    public static partial class CaveBuildQualityMeatLoop
    {
        public const int AdventureMaxPasses = 16;

        public static CaveBuildQualityReport Run(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            bool showProgress,
            Action<int, string> onStep,
            out int fixesApplied,
            out int gradePasses)
        {
            var state = BeginQueued(caveRoot, ground, request, buildReport, showProgress);
            while (!RunQueuedPhase(state, onStep))
            {
                CaveBuildActionPacing.ApplyCooldownTimers(CaveBuildActionPacing.ActionWeight.Normal);
            }

            fixesApplied = state.FixesApplied;
            gradePasses = state.GradePasses;
            return state.Quality;
        }

        static void TryInvokeCursorDuringMeatLoop(
            CaveBuildQualityReport quality,
            int pass,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (quality == null)
            {
                Debug.Log($"[CaveCursor] Meat pass {pass}: skipped — no quality report.");
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (settings.suppressMeatLoopCursorInvokes)
            {
                Debug.Log(
                    $"[CaveCursor] Meat pass {pass}: skipped — suppressMeatLoopCursorInvokes is on (end-of-build invoke only).");
                return;
            }

            if (!settings.autoInvokeEachMeatLoopPass)
            {
                Debug.Log(
                    $"[CaveCursor] Meat pass {pass}: skipped — autoInvokeEachMeatLoopPass is off in Cave Build Cursor Settings.");
                return;
            }

            if (CaveBuildQualityRubric.MeetsShipTarget(quality))
            {
                Debug.Log(
                    $"[CaveCursor] Meat pass {pass}: skipped — strict target already met " +
                    $"({quality.LetterGrade} {quality.OverallScore}/100).");
                return;
            }

            if (!CaveBuildCursorAgentBridge.HasApiKey)
            {
                Debug.LogWarning(
                    $"[CaveCursor] Meat pass {pass}: skipped — set CURSOR_API_KEY or save key in Cave Build Cursor Settings.");
                return;
            }

            if (CaveBuildCursorAgentBridge.IsAgentRunning)
            {
                Debug.Log(
                    $"[CaveCursor] Meat pass {pass}: quality JSON updated; agent already running — skipped duplicate invoke.");
                return;
            }

            var mission = CaveBuildMeatLoopPassPlan.GetMission(pass);
            System.Environment.SetEnvironmentVariable("CAVE_MEAT_PASS", pass.ToString());
            System.Environment.SetEnvironmentVariable("CAVE_FORCE_PROMPT_EXPORT", "1");
            var rung = CaveBuildPromptLadder.PickActiveRung(quality, caveRoot, ground);
            CaveBuildRungPromptExporter.EnsureTailoredPromptOnDisk(rung, quality, pass);
            CaveBuildRungPromptExporter.PrepareAgentInvoke(rung, caveRoot, ground, quality, pass);
            Debug.Log(
                $"[CaveCursor] Meat pass {pass} mission: {mission.Title} | research: {mission.ResearchFocus}");
            if (!CaveBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(out var msg, rung: rung, startLadderChain: pass == 0))
                Debug.LogWarning($"[CaveCursor] Meat pass {pass}: Cursor invoke failed: {msg}");
            else
                Debug.Log(
                    $"[CaveCursor] Meat pass {pass}: ladder rung '{rung}' → Cursor API (CaveBuildLadderContext.json).");
        }
    }
}
