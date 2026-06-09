#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Paced mountain pipeline — massif (wilderness + mouth) then annex (labyrinth, summit caps, cliffs, trails).
    /// Phases advance only after <see cref="CaveBuildActionPacing.QueueWhenIdle"/> (queue + tsx drained).
    /// </summary>
    public static class SurfaceMountainResearchPipeline
    {
        public static readonly string[] PhaseIds =
        {
            "mountain_research_brief",
            "mountain_wilderness_tiles",
            "mountain_foothill_sculpt",
            "mountain_peak_sculpt",
            "mountain_wilderness_cave_mouth",
            "mountain_labyrinth_research",
            "mountain_labyrinth_carve",
            "mountain_peak_summit_caps",
            "mountain_cliffs",
            "mountain_trails",
            "mountain_play_smooth",
        };

        const int QueuedStepBase = 52;
        const double PhaseAdvanceWatchdogSeconds = 25.0;

        sealed class PipelineState
        {
            public Terrain MainTerrain;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public Transform SurfaceRoot;
            public Vector3 Center;
            public float Extent;
            public Vector3 PrimaryForward;
            public int Seed;
            public int PhaseIndex;
            public int AdvanceGateToken;
            public List<Vector3[]> TrailPolylines = new();
            public List<TerrainStageGrade> Grades = new();
            public Action OnComplete;
        }

        public static void QueueAfterNineTilePolish(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Vector3 center,
            float extent,
            Vector3 primaryForward,
            int seed,
            Action onComplete)
        {
            FullWorldConceptLayoutCatalog.EnsureConceptOnRequest(request);

            if (mainTerrain == null || request == null || !request.SurfaceIncludeMountains ||
                !request.UseOuterRingMountains ||
                !SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(
                    request,
                    request.SurfaceScope == SurfaceBuildScope.FullWorld))
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildGradedArtifactGate.Reset();
            var state = new PipelineState
            {
                MainTerrain = mainTerrain,
                Ground = ground,
                Request = request,
                SurfaceRoot = surfaceRoot,
                Center = center,
                Extent = extent,
                PrimaryForward = primaryForward,
                Seed = seed,
                OnComplete = onComplete,
            };

            CaveBuildEditorLog.LogSurface(
                "[Surface] Mountain pipeline — massif (wilderness + 3 south peak mouths) → " +
                "annex (south 3×2 labyrinth, summit caps, cliffs, trails); queued steps wait for idle.",
                forceUnityConsole: true);
            ScheduleMountainMicroStep(() => Advance(state));
        }

        static void ScheduleMountainMicroStep(Action action) =>
            CaveBuildActionPacing.ScheduleLight(
                action,
                CaveBuildPipelineDomains.QueueLabel("mountain pipeline — phase gate"));

        static void Advance(PipelineState state)
        {
            if (state.PhaseIndex >= PhaseIds.Length)
            {
                QueuePlanV4WorldContent(state);
                CaveBuildEditorLog.LogSurface("[Surface] Mountain pipeline complete.", forceUnityConsole: true);
                state.OnComplete?.Invoke();
                return;
            }

            var phaseId = PhaseIds[state.PhaseIndex];
            var step = QueuedStepBase + state.PhaseIndex;
            CaveBuildRunStatusPublisher.PulseSubOperation("mountain pipeline", phaseId);

            if (state.Request != null)
                CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(step, state.Request, out _);

            CaveBuildUnifiedPromptBridge.RefreshForPhase(
                phaseId,
                "ground_placement",
                -1,
                step,
                state.Seed,
                out _);

            switch (phaseId)
            {
                case "mountain_research_brief":
                    RunResearchBriefPhase(state);
                    break;
                case "mountain_wilderness_tiles":
                    RunWildernessTilesPhase(state);
                    break;
                case "mountain_foothill_sculpt":
                    RunFoothillSculptPhase(state);
                    break;
                case "mountain_peak_sculpt":
                    RunPeakSculptPhase(state);
                    break;
                case "mountain_wilderness_cave_mouth":
                    RunWildernessCaveMouthPhase(state);
                    break;
                case "mountain_labyrinth_research":
                    RunLabyrinthResearchPhase(state);
                    break;
                case "mountain_labyrinth_carve":
                    RunLabyrinthCarvePhase(state);
                    break;
                case "mountain_peak_summit_caps":
                    RunPeakSummitCapsPhase(state);
                    break;
                case "mountain_cliffs":
                    RunCliffsPhase(state);
                    break;
                case "mountain_trails":
                    RunTrailsPhase(state);
                    break;
                case "mountain_play_smooth":
                    RunPlaySmoothPhase(state);
                    break;
                default:
                    FinishPhase(state, null);
                    break;
            }
        }

        static void RunWildernessTilesPhase(PipelineState state)
        {
            if (SurfaceFloridaDemBuildState.FullWorldDirectionalBuildCompletedThisBuild)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Wilderness tiles — built via extended flat grid (~289 slots); re-stitching foothill+peak ring before audit.",
                    forceUnityConsole: true);
                SurfaceTerrainTileExpansion.QueueFullWorldOuterRingSeamPipeline(
                    state.MainTerrain,
                    () =>
                        CaveBuildLayoutAuditMilestones.QueueAfterPlayDisk(
                            state.MainTerrain,
                            state.Ground,
                            state.Request,
                            () =>
                                CaveBuildLayoutAuditMilestones.QueueAfterFoothillRing(
                                    state.MainTerrain,
                                    state.Ground,
                                    state.Request,
                                    () =>
                                        CaveBuildLayoutAuditMilestones.QueueAfterWilderness(
                                            state.MainTerrain,
                                            state.Ground,
                                            state.Request,
                                            () => FinishPhase(state, "mountain_wilderness_tiles")))));
                return;
            }

            CaveBuildEditorLog.LogSurface(
                "[Surface] FullWorld outer rings — 16 foothill + 24 peak; per tile: seed → sculpt → seam → lock, then next.",
                forceUnityConsole: true);
            SurfaceTerrainTileExpansion.QueueAttachMountainWildernessTiles(
                state.MainTerrain,
                state.Request,
                (_, msg) =>
                {
                    CaveBuildEditorLog.LogSurface("[Surface] " + msg, forceUnityConsole: true);
                    CaveBuildLayoutAuditMilestones.QueueAfterWilderness(
                        state.MainTerrain,
                        state.Ground,
                        state.Request,
                        () => FinishPhase(state, "mountain_wilderness_tiles"));
                });
        }

        static void RunResearchBriefPhase(PipelineState state)
        {
            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    CaveBuildResearchCacheBridge.SyncTerrainResearchExecutionBrief("mountain_research", -1, out var researchMsg);
                    CaveBuildResearchCacheBridge.SyncTerrainResearchExecutionBrief("mountain_lidar", -1, out var lidarMsg);
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Mountain research brief — Appalachian relief + fullworld_do_not wired for outer-ring sculpt " +
                        $"(research={!string.IsNullOrEmpty(researchMsg)}, lidar={!string.IsNullOrEmpty(lidarMsg)}).",
                        forceUnityConsole: true);
                    FinishPhase(state, "mountain_research");
                },
                CaveBuildPipelineDomains.QueueLabel("mountain research brief"));
        }

        static void RunFoothillSculptPhase(PipelineState state)
        {
            var foothills = SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(state.MainTerrain);
            if (foothills.Length == 0)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] No foothill wilderness tiles — legacy QueueApplyFoothillRing (9-tile play disk).",
                    forceUnityConsole: true);
                SurfaceOuterRingMountainsAuthor.QueueApplyFoothillRing(
                    state.MainTerrain,
                    state.Request,
                    () => FinishPhase(state, "mountain_foothill_sculpt_legacy"));
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Foothill sculpt — rolling Appalachian roll-off on {foothills.Length} tile(s).",
                forceUnityConsole: true);
            SurfaceOuterRingMountainsAuthor.QueueApplyFoothillRing(
                state.MainTerrain,
                state.Request,
                () =>
                    SurfaceTerrainTileExpansion.QueueLockFoothillInnerEdgesToPlayDisk(
                        state.MainTerrain,
                        () =>
                            SurfaceTerrainSeamWelds.QueuePerPhaseSeamWeld(
                                state.MainTerrain,
                                foothills,
                                "mountain_foothill_sculpt",
                                () => FinishPhase(state, null))));
        }

        static void RunPeakSculptPhase(PipelineState state)
        {
            var peaks = SurfaceTerrainTileExpansion.CollectMountainPeakTiles(state.MainTerrain);
            if (peaks.Length == 0)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] No peak wilderness tiles — legacy QueueApplyPeakRing.",
                    forceUnityConsole: true);
                SurfaceOuterRingMountainsAuthor.QueueApplyPeakRing(
                    state.MainTerrain,
                    state.Request,
                    () => FinishPhase(state, "mountain_peak_sculpt_legacy"));
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Peak pass — denoise + foothill lock only ({peaks.Length} tile(s)); labyrinth is mountain_labyrinth_carve.",
                forceUnityConsole: true);
            SurfaceOuterRingMountainsAuthor.QueueDenoiseWildernessTiles(
                state.MainTerrain,
                peaks,
                () =>
                    SurfaceTerrainTileExpansion.QueueLockPeakInnerEdgesToFoothillRing(
                        state.MainTerrain,
                        () =>
                            SurfaceTerrainSeamWelds.QueuePerPhaseSeamWeld(
                                state.MainTerrain,
                                peaks,
                                "mountain_peak_sculpt",
                                () => FinishPhase(state, "outer_ring_mountains"))));
        }

        static void RunWildernessCaveMouthPhase(PipelineState state)
        {
            SurfaceMountainWildernessCaveMouthAuthor.QueueApply(
                state.MainTerrain,
                state.Ground,
                state.Request,
                state.SurfaceRoot,
                state.Seed,
                () =>
                    SurfaceTerrainSeamWelds.QueuePerPhaseSeamWeld(
                        state.MainTerrain,
                        SurfaceMountainSouthAnnex.CollectSouthPeakAnnexTiles(state.MainTerrain),
                        "mountain_wilderness_cave_mouth",
                        () => FinishPhase(state, "mountain_wilderness_cave_mouth")));
        }

        static void RunPeakSummitCapsPhase(PipelineState state)
        {
            if (!state.Request.UsePeakSummitCap)
            {
                FinishPhase(state, null);
                return;
            }

            SurfacePeakSummitCapAuthor.QueueApplyPeakSummitCaps(
                state.MainTerrain,
                state.Request,
                state.SurfaceRoot,
                state.Center,
                () => FinishPhase(state, "mountain_peak_summit_caps"));
        }

        static void RunLabyrinthResearchPhase(PipelineState state)
        {
            if (!state.Request.SurfaceIncludeMountainLabyrinth)
            {
                FinishPhase(state, null);
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    // Never block mountain pipeline on brief tsx here; use on-disk brief during active builds.
                    CaveBuildResearchCacheBridge.TryFastPathResearchPull("mountain_labyrinth_research", out _);
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Mountain labyrinth research — proper maze + labyrinth_do_not anti-patterns (ResearchCache; carve next phase).",
                        forceUnityConsole: true);
                    FinishPhase(state, "mountain_labyrinth_research");
                },
                CaveBuildPipelineDomains.QueueLabel("mountain labyrinth research"));
        }

        static void RunLabyrinthCarvePhase(PipelineState state)
        {
            if (!state.Request.SurfaceIncludeMountainLabyrinth)
            {
                FinishPhase(state, null);
                return;
            }

            SurfaceMountainLabyrinthAuthor.QueueApply(
                state.MainTerrain,
                state.Ground,
                state.Request,
                state.SurfaceRoot,
                state.Center,
                state.Seed,
                () =>
                    SurfaceMountainLabyrinthAuthor.QueueRestoreNonAnnexFoothillRollingHills(
                        state.MainTerrain,
                        () =>
                            SurfaceTerrainSeamWelds.QueueSouthAnnexSeamCluster(
                                state.MainTerrain,
                                () =>
                                {
                                    CaveBuildEditorLog.LogSurface(
                                        "[Surface] Mountain labyrinth carved — outer-ring seam pass.",
                                        forceUnityConsole: true);
                                    SurfaceTerrainTileExpansion.QueueFullWorldOuterRingSeamPipeline(
                                        state.MainTerrain,
                                        () =>
                                            CaveBuildLayoutAuditMilestones.QueueAfterWilderness(
                                                state.MainTerrain,
                                                state.Ground,
                                                state.Request,
                                                () => FinishPhase(state, "mountain_labyrinth")));
                                })));
        }

        static void RunCliffsPhase(PipelineState state)
        {
            CaveBuildEditorLog.LogSurface(
                "[Surface] mountain_cliffs — bulk peak dressing for any peak tiles not dressed per-tile after terraform.",
                forceUnityConsole: true);
            SurfaceMountainTerrainPhases.QueueCliffAccent(
                state.MainTerrain,
                () => SurfaceOuterRingMountainsAuthor.QueueDenoiseOuterBand(
                    state.MainTerrain,
                    () => SurfaceMountainPeakDressingAuthor.QueueApply(
                        state.MainTerrain,
                        state.SurfaceRoot,
                        state.Seed,
                        () => FinishPhase(state, "mountain_cliffs"))));
        }

        static void RunTrailsPhase(PipelineState state)
        {
            SurfaceMountainTerrainPhases.QueuePerimeterTrails(
                state.SurfaceRoot,
                state.MainTerrain,
                state.Center,
                state.Extent,
                state.PrimaryForward,
                state.Seed,
                state.Request,
                polylines =>
                {
                    state.TrailPolylines = polylines;
                    var cave = CaveRouteProbeRunner.FindCaveRoot();
                    SurfaceTrailWalkabilityRepair.QueueTryRepair(
                        state.Ground,
                        state.Request,
                        state.SurfaceRoot,
                        cave,
                        (_, msg) =>
                        {
                            if (!string.IsNullOrEmpty(msg))
                            {
                                CaveBuildEditorLog.LogSurface(
                                    "[Surface] Mountain trail walkability — " + msg,
                                    forceUnityConsole: true);
                            }

                            FinishPhase(state, "mountain_trails");
                        });
                });
        }

        static void QueuePlanV4WorldContent(PipelineState state)
        {
            if (state.MainTerrain == null || state.Request == null)
                return;

            if (state.Request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return;

            WorldPlanV4ContentAuthor.Apply(state.MainTerrain, state.Request);
        }

        static void RunPlaySmoothPhase(PipelineState state)
        {
            var nineTiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(state.MainTerrain);
            SurfaceOuterRingMountainsAuthor.ComputeNineTileWorldBounds(
                nineTiles,
                out var minX,
                out var maxX,
                out var minZ,
                out var maxZ);

            SurfaceTerrainRefinement.QueueSelectiveSmoothPlayBand(
                state.MainTerrain,
                state.Center,
                state.Extent * 0.4f,
                state.TrailPolylines,
                minX,
                maxX,
                minZ,
                maxZ,
                _ =>
                {
                    SurfaceTerrainLadderFixer.QueueFixCraters(
                        state.Ground,
                        state.Center,
                        state.Extent,
                        (_, __) =>
                        {
                            var peaks = SurfaceTerrainTileExpansion.CollectMountainPeakTiles(state.MainTerrain);
                            var horizon = SurfaceTerrainTileExpansion.CollectMountainHorizonTiles(state.MainTerrain);
                            var denoiseTiles = new List<Terrain>(peaks.Length + horizon.Length);
                            denoiseTiles.AddRange(peaks);
                            denoiseTiles.AddRange(horizon);
                            if (denoiseTiles.Count == 0)
                            {
                                FinishPhase(state, "mountain_play_smooth");
                                return;
                            }

                            SurfaceOuterRingMountainsAuthor.QueueDenoiseWildernessTiles(
                                state.MainTerrain,
                                denoiseTiles,
                                () => FinishPhase(state, "mountain_play_smooth"));
                        });
                });
        }

        static void FinishPhase(PipelineState state, string rungId)
        {
            if (!string.IsNullOrEmpty(rungId))
            {
                var mapped = MapPhaseToRung(rungId);
                foreach (var def in SurfaceMountainBuildLadder.RungOrder)
                {
                    if (def.Id != mapped)
                        continue;
                    var grade = SurfaceMountainBuildLadder.GradeOneRung(
                        def,
                        state.Ground,
                        state.Request,
                        state.SurfaceRoot);
                    state.Grades.Add(grade);
                    if (!grade.Passed)
                    {
                        SurfaceMountainBuildLadder.ExportRungPrompt(def.Id, state.Seed, grade);
                        CaveBuildResearchConstrainedGate.OnRungFailed(
                            def.Id,
                            PhaseIds[state.PhaseIndex],
                            state.Seed,
                            string.Join("; ", grade.Issues ?? new List<string>()));
                    }

                    break;
                }
            }

            state.PhaseIndex++;
            var completedPhase = PhaseIds[state.PhaseIndex - 1];
            var gateToken = ++state.AdvanceGateToken;
            var gateLabel = CaveBuildPipelineDomains.QueueLabel($"mountain pipeline — after {completedPhase}");

            void ContinueAdvance()
            {
                if (gateToken != state.AdvanceGateToken)
                    return;

                // Invalidate this gate so watchdog / duplicate callbacks cannot re-enter.
                state.AdvanceGateToken++;
                ScheduleMountainMicroStep(() => Advance(state));
            }

            void StartAdvanceWatchdog()
            {
                var startedAt = EditorApplication.timeSinceStartup;
                var watchdogLabel = gateLabel + " (watchdog)";

                void PollWatchdog()
                {
                    if (gateToken != state.AdvanceGateToken)
                        return;

                    if (EditorApplication.timeSinceStartup - startedAt < PhaseAdvanceWatchdogSeconds)
                    {
                        CaveBuildActionPacing.ScheduleLight(PollWatchdog, watchdogLabel);
                        return;
                    }

                    CaveBuildEditorLog.LogSurfaceWarning(
                        $"[Surface] Mountain pipeline watchdog forced advance after {PhaseAdvanceWatchdogSeconds:F0}s at {completedPhase}.");
                    ContinueAdvance();
                }

                CaveBuildActionPacing.ScheduleLight(PollWatchdog, watchdogLabel);
            }

            // This phase has repeatedly stalled on idle handoff in the field; bypass idle gate.
            if (completedPhase == "mountain_labyrinth_research")
            {
                CaveBuildActionPacing.ScheduleLight(ContinueAdvance, gateLabel + " (direct)");
                return;
            }

            CaveBuildActionPacing.QueueWhenIdle(ContinueAdvance, gateLabel);
            StartAdvanceWatchdog();
        }

        static string MapPhaseToRung(string phaseOrRungId) => phaseOrRungId switch
        {
            "mountain_peak_sculpt" => "outer_ring_mountains",
            "mountain_peak_summit_caps" => "outer_ring_mountains",
            "mountain_labyrinth" => "mountain_labyrinth",
            "mountain_labyrinth_research" => "mountain_labyrinth_research",
            "mountain_wilderness_cave_mouth" => "outer_ring_mountains",
            "mountain_outer_ring" => "outer_ring_mountains",
            "mountain_wilderness_tiles" => "mountain_wilderness_tiles",
            _ => phaseOrRungId,
        };
    }
}
#endif
