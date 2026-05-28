#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Cursor SDK terrain workflow — mirrors cave grader with --workflow=terrain.</summary>
    public static class TerrainBuildCursorAgentBridge
    {
        public const string WorkflowTerrain = "terrain";

        static bool _terrainWorkflowActive;
        static SurfaceTerrainLadderReport _terrainReport;
        static SceneGroundInfo _terrainGround;
        static readonly HashSet<string> _completedRungs = new();

        public static bool HasApiKey => CaveBuildCursorAgentBridge.HasApiKey;
        public static bool IsAgentRunning => CaveBuildCursorAgentBridge.IsAgentRunning;
        public static bool IsTerrainWorkflowActive => _terrainWorkflowActive;

        internal static bool ResolveTerrainWorkflowForProcess() => _terrainWorkflowActive;

        /// <summary>Stops optional terrain Cursor so pre-build / cave geometry can continue.</summary>
        public static void YieldForPendingCaveBuild()
        {
            if (!_terrainWorkflowActive && !IsAgentRunning)
                return;

            UnityEngine.Debug.Log(
                "[TerrainCursor] Yielding — cave geometry has priority over terrain grading agent.");
            _terrainWorkflowActive = false;
            CaveBuildCursorAgentBridge.SetTerrainWorkflowOverride(false);
            CaveBuildCursorAgentBridge.TerminateBackgroundAgentIfRunningPublic();
        }

        [MenuItem(CaveBuildMenuPaths.TerrainInvokeCursor, false, 20)]
        public static void MenuInvoke()
        {
            if (TryInvokeGradeAndFix(out var msg))
                Debug.Log("[TerrainCursor] " + msg);
            else
                EditorUtility.DisplayDialog("Terrain Cursor", msg, "OK");
        }

        [MenuItem(CaveBuildMenuPaths.TerrainBeginWorkflow, false, 21)]
        public static void MenuBeginWorkflow()
        {
            var ground = SceneGroundResolver.Resolve();
            var surface = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
            var request = new WorldGenerationRequest
            {
                SurfaceScope = SurfaceBuildScope.SurfaceOnly,
                SurfaceIncludeTrails = true,
            };
            var report = SurfaceTerrainQualityGrader.Run(ground, request, surface);
            if (TryBeginTerrainWorkflow(report, ground, out var msg))
                Debug.Log("[TerrainCursor] " + msg);
            else
                EditorUtility.DisplayDialog("Terrain Workflow", msg, "OK");
        }

        public static bool TryInvokeGradeAndFix(out string message)
        {
            var ground = SceneGroundResolver.Resolve();
            var surface = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
            var request = new WorldGenerationRequest { SurfaceIncludeTrails = true };
            var report = SurfaceTerrainQualityGrader.Run(ground, request, surface);
            var rung = SurfaceTerrainBuildLadder.PickActiveRung(report);
            TerrainBuildRungPromptExporter.PrepareAgentInvoke(rung, out _);
            return TryInvokeGradeAndFixBackground(out message, rung);
        }

        public static bool TryInvokeGradeAndFixBackground(out string message, string rung = null)
        {
            _terrainWorkflowActive = true;
            CaveBuildCursorAgentBridge.SetTerrainWorkflowOverride(true);
            return CaveBuildCursorAgentBridge.TryInvokeTerrainAgentBackground(out message, rung);
        }

        public static bool TryBeginTerrainWorkflow(
            SurfaceTerrainLadderReport report,
            SceneGroundInfo ground,
            out string message)
        {
            message = null;
            if (IsAgentRunning)
            {
                message = "Cursor agent already running.";
                return false;
            }

            if (!HasApiKey)
            {
                message = CaveBuildCursorSettings.CursorWorkflowCredentialHint();
                return false;
            }

            _terrainWorkflowActive = true;
            _terrainReport = report;
            _terrainGround = ground;
            _completedRungs.Clear();
            CaveBuildCursorAgentBridge.SetTerrainWorkflowOverride(true);
            return AdvanceTerrainWorkflow(out message);
        }

        internal static void OnStreamPhaseComplete(CaveBuildPhaseCompleteSignal signal)
        {
            if (!_terrainWorkflowActive || signal.Workflow != WorkflowTerrain)
                return;

            UnityEngine.Debug.Log(
                $"[TerrainCursor] Phase-complete: rung={signal.Rung} reason={signal.Reason}");
            if (!string.IsNullOrEmpty(signal.Rung))
                _completedRungs.Add(signal.Rung);

            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    RefreshTerrainReport();
                    AdvanceTerrainWorkflow(out _);
                },
                $"terrain workflow after phase-complete ({signal.Rung})");
        }

        internal static void OnBackgroundAgentFinished(bool success, double elapsedSeconds)
        {
            if (!_terrainWorkflowActive)
                return;

            if (!success)
            {
                UnityEngine.Debug.LogWarning(
                    $"[TerrainCursor] Agent failed after {elapsedSeconds:F0}s — see {TerrainBuildCursorGraderDiagnostics.LastRunPath}");
                FinishTerrainWorkflow(false);
                return;
            }

            RefreshTerrainReport();
            if (!AdvanceTerrainWorkflow(out var msg))
            {
                UnityEngine.Debug.Log("[TerrainCursor] " + msg);
                FinishTerrainWorkflow(true);
            }
        }

        static bool AdvanceTerrainWorkflow(out string message)
        {
            message = null;
            RefreshTerrainReport();
            var report = _terrainReport;
            if (report == null)
            {
                message = "No terrain report.";
                FinishTerrainWorkflow(false);
                return false;
            }

            if (report.BuildAcceptable)
            {
                message = $"Terrain workflow complete — {report.LetterGrade} ({report.OverallScore}).";
                FinishTerrainWorkflow(true);
                return false;
            }

            for (var i = 0; i < SurfaceTerrainBuildLadder.RungOrder.Length + 2; i++)
            {
                var rung = SurfaceTerrainBuildLadder.PickActiveRung(report, _completedRungs);
                if (string.IsNullOrEmpty(rung))
                {
                    message = "No failing rung — done.";
                    FinishTerrainWorkflow(true);
                    return false;
                }

                if (IsRungPassing(report, rung))
                {
                    _completedRungs.Add(rung);
                    continue;
                }

                TerrainBuildRungPromptExporter.PrepareAgentInvoke(rung, out var exportMsg);
                if (!TryInvokeGradeAndFixBackground(out message, rung))
                {
                    FinishTerrainWorkflow(false);
                    return false;
                }

                message = $"Started Cursor for terrain rung={rung}. {exportMsg}";
                return true;
            }

            message = "Terrain workflow iteration cap reached.";
            FinishTerrainWorkflow(false);
            return false;
        }

        static bool IsRungPassing(SurfaceTerrainLadderReport report, string rungId)
        {
            foreach (var s in report.Stages)
            {
                if (s.StageId != rungId)
                    continue;
                return s.Passed && s.Score >= SurfaceTerrainBuildLadder.StagePassScore;
            }

            return false;
        }

        static void RefreshTerrainReport()
        {
            if (_terrainGround == null)
                _terrainGround = SceneGroundResolver.Resolve();
            var surface = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
            var request = new WorldGenerationRequest { SurfaceIncludeTrails = true };
            _terrainReport = SurfaceTerrainQualityGrader.Run(_terrainGround, request, surface);
        }

        static void FinishTerrainWorkflow(bool success)
        {
            _terrainWorkflowActive = false;
            CaveBuildCursorAgentBridge.SetTerrainWorkflowOverride(false);
            if (!success)
                return;

            // Avoid repeating full surface rebuild loops after every terrain agent pass.
            // Terrain grading completion is the handoff signal; cave alignment is preserved by the cave gate.
            var seed = _terrainReport?.Seed ?? 0;
            CaveBuildSurfaceCompletionGate.MarkTerrainGradingComplete(
                new WorldGenerationRequest
                {
                    Seed = seed,
                    SurfaceScope = SurfaceBuildScope.FullWorld,
                });
        }

        static void ScheduleSurfaceRebuild()
        {
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    try
                    {
                        LavaTubeCaveBuilder.BuildSurfaceWorldOnlyActiveScene();
                        UnityEngine.Debug.Log("[TerrainCursor] Auto surface rebuild after agent OK.");

                        // The cave FullWorld gate requires terrain grading completion with SurfaceScope=FullWorld.
                        // Auto surface rebuild runs SurfaceOnly builds, which can mark the gate for the wrong scope
                        // and leave cave meat waiting forever. Re-assert completion for the active seed/scope.
                        var seed = _terrainReport?.Seed ?? 0;
                        CaveBuildSurfaceCompletionGate.MarkTerrainGradingComplete(
                            new WorldGenerationRequest
                            {
                                Seed = seed,
                                SurfaceScope = SurfaceBuildScope.FullWorld,
                            });
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogWarning("[TerrainCursor] Auto surface rebuild failed: " + ex.Message);
                    }
                },
                "terrain auto-rebuild surface after Cursor");
        }

    }
}
#endif
