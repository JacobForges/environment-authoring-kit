#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Five terrain phases (DEM → smooth → trails → water → final polish) plus grading ladder.
    /// Queued one step at a time so the editor stays responsive.
    /// </summary>
    public static class SurfaceTerrainAiPhases
    {
        /// <summary>True while paced terrain phases / ladder are still on the editor queue (blocks cave handoff).</summary>
        public static bool IsPipelineActive { get; private set; }

        static double _pipelineStartAt;
        static bool _pipelineWatchdogArmed;
        static QueueState _watchdogState;
        const double PipelineWatchdogSeconds = 180.0;

        static void ArmPipelineWatchdog(QueueState state)
        {
            _watchdogState = state;
            _pipelineStartAt = EditorApplication.timeSinceStartup;
            if (_pipelineWatchdogArmed)
                return;

            _pipelineWatchdogArmed = true;
            EditorApplication.update -= PipelineWatchdogTick;
            EditorApplication.update += PipelineWatchdogTick;
        }

        static void PipelineWatchdogTick()
        {
            if (!_pipelineWatchdogArmed)
                return;

            if (!IsPipelineActive)
            {
                _pipelineWatchdogArmed = false;
                _watchdogState = null;
                EditorApplication.update -= PipelineWatchdogTick;
                return;
            }

            if (_watchdogState?.Request == null)
                return;

            var elapsed = EditorApplication.timeSinceStartup - _pipelineStartAt;
            if (elapsed < PipelineWatchdogSeconds)
                return;

            var req = _watchdogState.Request;
            var summary =
                $"Terrain pipeline watchdog forced completion after {elapsed:F0}s (avoids cave deadlock).";
            CaveBuildEditorLog.LogSurfaceWarning("[TerrainWatchdog] " + summary);

            // Unstick the cave pipeline: clear IsPipelineActive + mark surface/build completion.
            // If a callback got stuck mid-flight, we still prevent indefinite waits.
            IsPipelineActive = false;
            EditorUtility.ClearProgressBar();
            CaveBuildSurfaceCompletionGate.MarkSurfaceBuildFinished(req, success: true);
            CaveBuildSurfaceCompletionGate.MarkTerrainGradingComplete(req);
            _watchdogState.OnComplete?.Invoke(false, summary);

            _pipelineWatchdogArmed = false;
            _watchdogState = null;
            EditorApplication.update -= PipelineWatchdogTick;
        }

        public const int PhaseCount = 6;
        public const string LogRel = CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainPhaseLog.json";

        public static readonly string[] PhaseIds =
        {
            "terrain_phase_smooth",
            "terrain_phase_trails",
            "terrain_phase_water",
            "terrain_phase_final_polish",
            "terrain_phase_dem",
            "terrain_phase_navmesh",
        };

        public static readonly string[] PhaseTitles =
        {
            "Terrain phase 1 — uniform smooth all surface terrains",
            "Terrain phase 2 — trail/road benches",
            "Terrain phase 3 — water basins + hydro polish",
            "Terrain phase 4 — light playable smooth",
            "Terrain phase 5 — LiDAR DEM authoritative stamp (main tile)",
            "Terrain phase 6 — surface NavMesh bake",
        };

        /// <summary>Public slice of phase queue state for LiDAR / research-guided DEM (avoids nested-type visibility issues).</summary>
        public readonly struct TerrainDemStampContext
        {
            public readonly SceneGroundInfo Ground;
            public readonly WorldGenerationRequest Request;
            public readonly Transform Surface;
            public readonly EnvironmentAuthoringKit.EnvironmentRoot EnvRoot;
            public readonly Vector3 Center;
            public readonly float Extent;

            public TerrainDemStampContext(
                SceneGroundInfo ground,
                WorldGenerationRequest request,
                Transform surface,
                EnvironmentAuthoringKit.EnvironmentRoot envRoot,
                Vector3 center,
                float extent)
            {
                Ground = ground;
                Request = request;
                Surface = surface;
                EnvRoot = envRoot;
                Center = center;
                Extent = extent;
            }
        }

        static TerrainDemStampContext ToDemStampContext(QueueState state) =>
            new(
                state.Ground,
                state.Request,
                state.Surface,
                state.EnvRoot,
                state.Center,
                state.Extent);

        internal sealed class QueueState
        {
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public Transform Surface;
            public EnvironmentAuthoringKit.EnvironmentRoot EnvRoot;
            public Vector3 Center;
            public float Extent;
            public int PhaseIndex;
            public int LadderIteration;
            public int LadderRungIndex;
            public int MaxLadderIterations;
            public bool PhasesOk = true;
            public StringBuilder Log = new();
            public HashSet<string> SkipRungs = new();
            public SurfaceTerrainLadderReport LadderReport;
            public SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog LadderVegCatalog;
            public SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog PropsCatalog;
            public bool PropsCatalogImported;
            public int PropCategoryIndex;
            public int PropCategoriesPlaced;
            public SurfaceIntelligentPropPlacer.CategoryPlacementSession PropPlacementSession;
            public int Phase0SmoothIndex;
            public int Phase0SmoothCells;
            public bool PostPropsStabilizationDone;
            public bool PropPolishPassDone;
            public bool PreLadderCraterRepairDone;
            public string LastLadderFailingRung;
            public int SameRungFixStreak;
            public int LastLadderOverallScore = -1;
            public Action<bool, string> OnComplete;
        }

        /// <summary>Runs all terrain phases + grade/fix ladder across paced editor queue steps.</summary>
        public static void QueueAllPhasesAndLadder(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action<bool, string> onComplete)
        {
            if (ground?.Terrain == null || request == null)
            {
                onComplete?.Invoke(false, "Terrain phases skipped — no terrain.");
                return;
            }

            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = envRoot != null ? envRoot.transform.Find(SurfaceWorldPaths.RootName) : null;
            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var requestExtent = request.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;
            var extent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                ground.Terrain,
                center,
                requestExtent);
            var duringCaveBuildSurface = CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive;
            var fullWorld = request.SurfaceScope == SurfaceBuildScope.FullWorld;

            var state = new QueueState
            {
                Ground = ground,
                Request = request,
                Surface = surface,
                EnvRoot = envRoot,
                Center = center,
                Extent = extent,
                MaxLadderIterations = duringCaveBuildSurface && fullWorld ? 8 : duringCaveBuildSurface ? 5 : 12,
                OnComplete = onComplete,
            };
            CaveBuildWorkflowGuardrails.ClassifyTerrainDelta(
                ground,
                out var neighborOnlyDelta,
                out var deltaMsg);
            CaveBuildEditorLog.LogSurface(
                "[Surface] " + deltaMsg + (neighborOnlyDelta ? " (seam-only fast path armed)." : string.Empty),
                forceUnityConsole: true);

            state.Log.AppendLine("{");
            state.Log.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
            state.Log.AppendLine("  \"phases\": [");

            IsPipelineActive = true;

            CaveBuildEditorLog.LogSurface(
                duringCaveBuildSurface
                    ? $"Terrain pipeline queued — {PhaseCount} phases + vegetation pass + terrain ladder (up to {state.MaxLadderIterations} paced fix step(s), then cave)."
                    : $"Terrain pipeline queued — {PhaseCount} phases + up to {state.MaxLadderIterations} ladder fix step(s).",
                forceUnityConsole: true);

            SchedulePhaseStep(state);
            ArmPipelineWatchdog(state);
        }

        /// <summary>Runs phases + ladder on the calling thread (menu / sync callers only).</summary>
        public static bool RunAllPhasesBlocking(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string summary)
        {
            summary = string.Empty;
            if (ground?.Terrain == null || request == null)
            {
                summary = "Terrain phases skipped — no terrain.";
                return false;
            }

            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = envRoot != null ? envRoot.transform.Find(SurfaceWorldPaths.RootName) : null;
            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var extent = request.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;
            var lightGate = request.SurfaceScope == SurfaceBuildScope.FullWorld;
            var ok = true;

            for (var i = 0; i < PhaseCount; i++)
            {
                if (!lightGate)
                    CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(37 + i, request, out _);

                CaveBuildEditorLog.LogSurface(
                    $"[Surface] {PhaseTitles[i]}…",
                    forceUnityConsole: true);

                ok &= RunPhase(i, ground, surface, envRoot, center, extent, request, out _);
            }

            var report = SurfaceTerrainBuildLadder.Run(ground, request, surface);
            var ladderOk = report.BuildAcceptable;

            summary = ok
                ? ladderOk
                    ? "Terrain phases + grading ladder PASS."
                    : "Terrain phases complete; ladder report written."
                : "Terrain phase issues — see SurfaceTerrainPhaseLog.json.";

            return ok;
        }

        static void SchedulePhaseStep(QueueState state)
        {
            if (state.PhaseIndex >= PhaseCount)
            {
                FinishPhaseLog(state);
                state.LadderIteration = 0;
                SurfaceTerrainQualityMeatLoop.QueueAfterTerrainPhases(state);
                return;
            }

            var index = state.PhaseIndex;
            var title = PhaseTitles[index];

            if (index == 4)
            {
                if (SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild)
                {
                    var tileCount = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(state.Ground.Terrain).Count;
                    if (tileCount > 1)
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] Terrain phase 5 — seam stitch on {tileCount - 1} neighbor tile(s) (main LiDAR already authoritative).",
                            forceUnityConsole: true);
                        CaveBuildActionPacing.ScheduleLight(
                            () =>
                            {
                                var stitched = SurfaceTerrainTileExpansion.ApplyUnifiedSurfaceWorldPolishSync(
                                    state.Ground.Terrain,
                                    state.Ground,
                                    state.Request);
                                state.PhasesOk &= stitched > 0;
                                AppendPhaseLog(
                                    state,
                                    index,
                                    state.PhasesOk,
                                    $"Neighbor seam stitch only ({stitched} tile(s)) — no per-tile DEM re-stamp.",
                                    "FullWorld stitch-only");
                                EnvironmentKitHardwareBudget.OnQueueStepCompleted();
                                state.PhaseIndex++;
                                SchedulePhaseStep(state);
                            },
                            CaveBuildPipelineDomains.SurfaceQueueLabel("terrain phase 5 neighbor seam stitch"));
                        return;
                    }

                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Terrain phase 5 — LiDAR DEM skipped (single main tile already stamped).",
                        forceUnityConsole: true);
                    state.PhasesOk &= true;
                    AppendPhaseLog(
                        state,
                        index,
                        true,
                        "Skipped — authoritative Florida DEM on main tile only.",
                        "surface world DEM");
                    state.PhaseIndex++;
                    SchedulePhaseStep(state);
                    return;
                }

                CaveBuildActionPacing.ScheduleHeavyChain(
                    () => RunDemStampPhase(state),
                    CaveBuildPipelineDomains.SurfaceQueueLabel($"terrain phase {index + 1}/{PhaseCount} DEM"));
                return;
            }

            if (index == 0)
            {
                QueueTerrainPhaseHelpers(
                    state,
                    index,
                    () => CaveBuildActionPacing.ScheduleHeavyChain(
                        () => SchedulePhase0OuterSmooth(state),
                        CaveBuildPipelineDomains.SurfaceQueueLabel(
                            $"terrain phase {index + 1}/{PhaseCount} outer smooth")));
                return;
            }

            if (index == 3)
            {
                SchedulePhase4LightSmooth(state);
                return;
            }

            QueueTerrainPhaseHelpers(
                state,
                index,
                () => CaveBuildActionPacing.ScheduleHeavyChain(
                    () => RunTerrainPhaseBody(state, index, title),
                    CaveBuildPipelineDomains.SurfaceQueueLabel($"terrain phase {index + 1}/{PhaseCount}")));
        }

        static void QueueTerrainPhaseHelpers(QueueState state, int phaseIndex, Action continuation)
        {
            var ctx = new CaveBuildHelperScriptOrchestrator.Context
            {
                Request = state.Request,
                TerrainPhaseIndex = phaseIndex,
            };
            CaveBuildHelperScriptOrchestrator.Queue(
                CaveBuildHelperScriptOrchestrator.Moment.TerrainPhaseStart,
                ctx,
                (_, _) => continuation?.Invoke());
        }

        static void RunTerrainPhaseBody(QueueState state, int index, string title)
        {
            if (state.Ground?.Terrain == null)
            {
                Complete(state, false, "Terrain phases aborted — terrain missing.");
                return;
            }

            var lightGate = state.Request.SurfaceScope == SurfaceBuildScope.FullWorld;
            var gateMsg = lightGate ? "skipped (FullWorld fast path)" : string.Empty;
            if (!lightGate)
            {
                CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(
                    37 + index,
                    state.Request,
                    out gateMsg);
            }

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] {title}",
                0.4f + 0.04f * (index / (float)PhaseCount));

            var phaseOk = RunPhase(
                index,
                state.Ground,
                state.Surface,
                state.EnvRoot,
                state.Center,
                state.Extent,
                state.Request,
                out var phaseMsg);

            state.PhasesOk &= phaseOk;
            AppendPhaseLog(state, index, phaseOk, phaseMsg, gateMsg);

            CaveBuildEditorLog.LogSurface(
                $"[Surface] {title} — {(phaseOk ? "OK" : "issues")}: {phaseMsg}",
                forceUnityConsole: true);

            state.PhaseIndex++;
            SchedulePhaseStep(state);
        }

        static void RunDemStampPhase(QueueState state)
        {
            if (state.Ground?.Terrain == null)
            {
                Complete(state, false, "Terrain phases aborted — terrain missing.");
                return;
            }

            var index = 4;
            var title = PhaseTitles[index];
            var lightGate = state.Request.SurfaceScope == SurfaceBuildScope.FullWorld;
            var gateMsg = lightGate ? "skipped (FullWorld fast path)" : string.Empty;
            if (!lightGate)
            {
                CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(
                    37 + index,
                    state.Request,
                    out gateMsg);
            }

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] {title}",
                0.4f + 0.04f * (index / (float)PhaseCount));

            SurfaceTerrainResearchGuidedLidar.QueueDemStamp(ToDemStampContext(state), phaseMsg =>
                {
                    var phaseOk = !string.IsNullOrEmpty(phaseMsg) &&
                                  phaseMsg.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) < 0 &&
                                  phaseMsg.IndexOf("Could not", StringComparison.OrdinalIgnoreCase) < 0;
                    state.PhasesOk &= phaseOk;
                    AppendPhaseLog(state, index, phaseOk, phaseMsg, gateMsg);
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] {title} — {(phaseOk ? "OK" : "issues")}: {phaseMsg}",
                        forceUnityConsole: true);
                    state.PhaseIndex++;
                    SchedulePhaseStep(state);
                });
        }

        static readonly SurfacePropCategory[] PropCategories =
        {
            SurfacePropCategory.Trees,
            SurfacePropCategory.Grass,
            SurfacePropCategory.Bushes,
            SurfacePropCategory.GroundCover,
        };

        internal static void ContinueAfterTerrainMeatLoop(QueueState state)
        {
            if (state == null)
                return;
            state.LadderIteration = 0;
            ScheduleSurfacePropsStep(state);
        }

        internal static Transform ResolveSurfaceRootPublic(QueueState state) => ResolveSurfaceRoot(state);

        static Transform ResolveSurfaceRoot(QueueState state)
        {
            if (state.Surface != null)
                return state.Surface;

            if (state.EnvRoot != null)
            {
                var found = state.EnvRoot.transform.Find(SurfaceWorldPaths.RootName);
                if (found != null)
                    return found;
            }

            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            return envRoot != null ? envRoot.transform.Find(SurfaceWorldPaths.RootName) : null;
        }

        static void ScheduleSurfacePropsStep(QueueState state)
        {
            state.Surface = ResolveSurfaceRoot(state);
            state.PropCategoryIndex = 0;
            state.PropCategoriesPlaced = 0;
            state.PropPolishPassDone = false;
            state.PropsCatalog = SurfaceIntelligentPropPlacer.LoadVegetationCatalog();

            if (!state.PropsCatalog.HasAny)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Vegetation skipped — no tree/bush/grass prefabs found (add LPMagicalForest or kit prop folders).",
                    forceUnityConsole: true);
                ScheduleLadderStep(state);
                return;
            }

            CaveBuildActionPacing.ScheduleHeavyChain(
                () => SchedulePropsLockStep(state),
                CaveBuildPipelineDomains.SurfaceQueueLabel("surface props — lock terrains"));
        }

        static void SchedulePropsLockStep(QueueState state)
        {
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                "[Surface] Props — locking surface terrains…",
                0.5f);

            SurfaceTerrainPropPlacementRegion.LockAndMarkSurfaceTerrains(
                state.Ground.Terrain,
                state.Ground,
                state.Request);

            if (!state.PropsCatalogImported)
            {
                SurfaceIntelligentPropPlacer.ImportCatalogPrefabsOnce(state.PropsCatalog);
                state.PropsCatalogImported = true;
            }

            CaveBuildActionPacing.ScheduleHeavyChain(
                () => SchedulePropsPlanStep(state),
                CaveBuildPipelineDomains.SurfaceQueueLabel("surface props — plan"));
        }

        static void SchedulePropsPlanStep(QueueState state)
        {
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                "[Surface] Props — fast placement plan (no grid scan)…",
                0.51f);

            var planOk = SurfaceIntelligentPropPlacer.WritePlacementPlanBeforeExecute(
                state.Ground.Terrain,
                state.Surface,
                state.Center,
                state.Extent,
                state.Request.Seed,
                state.PropsCatalog,
                out var planMsg);

            CaveBuildEditorLog.LogSurface("[Surface] " + planMsg, forceUnityConsole: true);
            if (!planOk)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Prop plan grade below threshold — placing anyway from locked terrain slots.",
                    forceUnityConsole: true);
            }

            CaveBuildActionPacing.ScheduleHeavyChain(
                () => ScheduleSurfacePropCategory(state),
                CaveBuildPipelineDomains.SurfaceQueueLabel("surface props — execute categories"));
        }

        static void SchedulePropPolishPass(QueueState state)
        {
            var polishIndex = 0;
            void RunNext()
            {
                if (polishIndex >= PropCategories.Length)
                {
                    ScheduleSurfacePropCategory(state);
                    return;
                }

                var cat = PropCategories[polishIndex++];
                CaveBuildActionPacing.ScheduleHeavy(
                    () =>
                    {
                        if (SurfaceIntelligentPropPlacer.TryPolishCategoryDensityPass(
                                state.Surface,
                                state.Ground.Terrain,
                                state.Center,
                                state.Extent,
                                state.Request.Seed,
                                cat,
                                state.PropsCatalog,
                                out var msg))
                            CaveBuildEditorLog.LogSurface("[Surface] " + msg, forceUnityConsole: true);
                        RunNext();
                    },
                    CaveBuildPipelineDomains.SurfaceQueueLabel($"props polish {cat}"));
            }

            CaveBuildEditorLog.LogSurface(
                "[Surface] Prop polish — score-sorted top-up toward contract density…",
                forceUnityConsole: true);
            RunNext();
        }

        static void ScheduleSurfacePropCategory(QueueState state)
        {
            state.Surface = ResolveSurfaceRoot(state);
            if (state.Ground?.Terrain == null || state.Surface == null ||
                state.PropCategoryIndex >= PropCategories.Length)
            {
                if (!state.PropPolishPassDone)
                {
                    state.PropPolishPassDone = true;
                    SchedulePropPolishPass(state);
                    return;
                }

                if (!state.PostPropsStabilizationDone && state.Ground?.Terrain != null)
                {
                    state.PostPropsStabilizationDone = true;
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Post-props stabilization — crater/seam cleanup before ladder grade…",
                        forceUnityConsole: true);
                    SurfaceTerrainLadderFixer.QueueFixCraters(
                        state.Ground,
                        state.Center,
                        state.Extent,
                        (_, _) => ScheduleSurfacePropCategory(state));
                    return;
                }

                if (CaveBuildWorkflowGuardrails.AuditSurfacePropCoverage(out var coverageMsg))
                    CaveBuildEditorLog.LogSurface("[Surface] " + coverageMsg, forceUnityConsole: true);
                else
                    CaveBuildEditorLog.LogSurfaceWarning("[Surface] " + coverageMsg);
                CaveBuildEditorLog.LogSurface(
                    state.PropCategoriesPlaced > 0
                        ? $"[Surface] Vegetation pass complete ({state.PropCategoriesPlaced} categories placed)."
                        : "[Surface] Vegetation pass — no prefabs placed (check LPMagicalForest / kit folders).",
                    forceUnityConsole: true);
                ScheduleLadderStep(state);
                return;
            }

            var cat = PropCategories[state.PropCategoryIndex];
            var catNum = state.PropCategoryIndex + 1;
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    var tileCount = SurfaceTerrainPropPlacementRegion.ActiveLock?.terrainTileCount ??
                                    SurfaceTerrainPlayRegion.CollectSurfaceTerrains(state.Ground.Terrain).Count;
                    var target = SurfaceTerrainPropPlacementRegion.TargetCountForCategory(cat, tileCount);
                    var session = state.PropPlacementSession;
                    var vegRoot = state.Surface.Find(SurfaceIntelligentPropPlacer.VegetationLayerName);
                    if (vegRoot == null)
                    {
                        var go = new GameObject(SurfaceIntelligentPropPlacer.VegetationLayerName);
                        CaveEditorUndo.RegisterCreated(go, "Surface vegetation root");
                        go.transform.SetParent(state.Surface, false);
                        vegRoot = go.transform;
                    }

                    if (session == null ||
                        session.Finalized ||
                        session.Category != cat)
                    {
                        if (!SurfaceIntelligentPropPlacer.TryBeginCategoryPlacementSession(
                                state.Surface,
                                state.Ground.Terrain,
                                state.Center,
                                state.Extent,
                                state.Request.Seed,
                                cat,
                                state.PropsCatalog,
                                session,
                                out session,
                                out vegRoot,
                                out var beginMsg))
                        {
                            CaveBuildEditorLog.LogSurfaceWarning(
                                $"[Surface] Props {cat}: {beginMsg}",
                                forceUnityConsole: true);
                            state.PropPlacementSession = null;
                            state.PropCategoryIndex++;
                            ScheduleSurfacePropCategory(state);
                            return;
                        }

                        state.PropPlacementSession = session;
                    }

                    var placedBefore = session.Placed;
                    SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPassChunk(
                        state.Ground.Terrain,
                        vegRoot,
                        state.Request.Seed,
                        cat,
                        session,
                        SurfaceIntelligentPropPlacer.DefaultPropsPerEditorChunk,
                        out var placedChunk);

                    EditorUtility.DisplayProgressBar(
                        "Environment Kit",
                        $"[Surface] Props {cat} ({catNum}/{PropCategories.Length}) {session.Placed}/{target} on {tileCount} tile(s)…",
                        0.52f + 0.02f * state.PropCategoryIndex + 0.12f * (session.Placed / (float)Mathf.Max(1, target)));

                    if (session.IsComplete)
                    {
                        if (SurfaceIntelligentPropPlacer.TryFinalizeCategoryPlacementSession(
                                state.Ground.Terrain,
                                vegRoot,
                                cat,
                                session,
                                out var msg))
                        {
                            state.PropCategoriesPlaced++;
                            CaveBuildEditorLog.LogSurface($"[Surface] Props {cat}: {msg}", forceUnityConsole: true);
                        }

                        state.PropPlacementSession = null;
                        state.PropCategoryIndex++;
                    }
                    else if (placedChunk <= 0 && session.Placed == placedBefore)
                    {
                        SurfaceIntelligentPropPlacer.TryFinalizeCategoryPlacementSession(
                            state.Ground.Terrain, vegRoot, cat, session, out _);
                        state.PropPlacementSession = null;
                        state.PropCategoryIndex++;
                    }

                    ScheduleSurfacePropCategory(state);
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel($"terrain vegetation {cat}"));
        }

        static void ScheduleLadderStep(QueueState state)
        {
            if (state.LadderReport != null &&
                state.LadderReport.GradingMode == "terrain_meat_loop")
            {
                SchedulePostMeatTerrainFinish(state);
                return;
            }

            if (state.LadderReport == null)
            {
                state.LadderReport = new SurfaceTerrainLadderReport
                {
                    SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    Seed = state.Request?.Seed ?? 0,
                    GradingMode = "terrain_build_ladder",
                };
                state.LadderRungIndex = 0;
            }

            if (state.LadderReport is { BuildAcceptable: true })
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Terrain meat loop already PASS ({state.LadderReport.OverallScore}) — props ladder fix cap only.",
                    forceUnityConsole: true);
                state.MaxLadderIterations = Mathf.Min(state.MaxLadderIterations, 2);
            }

            if (state.LadderIteration == 0 && !state.PreLadderCraterRepairDone)
            {
                state.PreLadderCraterRepairDone = true;
                var center = state.Center;
                var extent = state.Extent;
                CaveBuildEditorLog.LogSurface(
                    "[TerrainLadder] Pre-pass — crater repair on all surface terrains before grading…",
                    forceUnityConsole: true);
                SurfaceTerrainLadderFixer.QueueFixCraters(
                    state.Ground,
                    center,
                    extent,
                    (_, _) => ScheduleLadderGradeRung(state));
                return;
            }

            ScheduleLadderGradeRung(state);
        }

        /// <summary>Meat loop already graded all rungs — props may change prop scores; fix-only after props.</summary>
        static void SchedulePostMeatTerrainFinish(QueueState state)
        {
            state.Surface = ResolveSurfaceRoot(state);
            state.MaxLadderIterations = Mathf.Min(state.MaxLadderIterations, 3);

            if (state.PropCategoriesPlaced > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Post-meat — re-grading prop rungs only (meat loop already graded height/trails).",
                    forceUnityConsole: true);
                RegradePropRungsOnly(state);
            }
            else
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Skipping full terrain ladder re-grade — meat loop already graded all rungs.",
                    forceUnityConsole: true);
            }

            SurfaceTerrainBuildLadder.FinalizeReport(
                state.LadderReport,
                state.Ground,
                state.Request,
                state.Surface);

            if (state.LadderReport.BuildAcceptable)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Terrain PASS after meat loop ({state.LadderReport.OverallScore} {state.LadderReport.LetterGrade}).",
                    forceUnityConsole: true);
                Complete(
                    state,
                    state.PhasesOk,
                    $"Terrain meat loop PASS ({state.LadderReport.OverallScore}).");
                return;
            }

            state.LadderIteration = 0;
            state.LadderRungIndex = SurfaceTerrainBuildLadder.RungOrder.Length;
            ScheduleLadderFixStep(state);
        }

        static void RegradePropRungsOnly(QueueState state)
        {
            if (state.LadderReport?.Stages == null)
                return;

            foreach (var def in SurfaceTerrainBuildLadder.RungOrder)
            {
                if (!def.Id.StartsWith("prop_", System.StringComparison.Ordinal))
                    continue;

                var idx = state.LadderReport.Stages.FindIndex(s => s.StageId == def.Id);
                var stage = SurfaceTerrainBuildLadder.GradeOneRung(
                    def,
                    state.Ground,
                    state.Request,
                    state.Surface,
                    ref state.LadderVegCatalog);
                if (idx >= 0)
                    state.LadderReport.Stages[idx] = stage;
                else
                    state.LadderReport.Stages.Add(stage);
            }
        }

        static void ScheduleLadderGradeRung(QueueState state)
        {
            if (state.LadderRungIndex >= SurfaceTerrainBuildLadder.RungOrder.Length)
            {
                CaveBuildEditorLog.LogSurface(
                    "[TerrainLadder] All rungs graded — writing report…",
                    forceUnityConsole: true);
                SurfaceTerrainBuildLadder.FinalizeReport(
                    state.LadderReport,
                    state.Ground,
                    state.Request,
                    state.Surface);
                CaveBuildEditorLog.LogSurface(
                    $"[TerrainLadder] Report ready — {state.LadderReport.OverallScore} ({state.LadderReport.LetterGrade}), acceptable={state.LadderReport.BuildAcceptable}.",
                    forceUnityConsole: true);
                ScheduleLadderFixStep(state);
                return;
            }

            var def = SurfaceTerrainBuildLadder.RungOrder[state.LadderRungIndex];
            var rungNum = state.LadderRungIndex + 1;
            var rungTotal = SurfaceTerrainBuildLadder.RungOrder.Length;

            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    if (state.Ground?.Terrain == null)
                    {
                        Complete(state, false, "Terrain ladder aborted — terrain missing.");
                        return;
                    }

                    var ladderCtx = new CaveBuildHelperScriptOrchestrator.Context
                    {
                        Request = state.Request,
                        Rung = def.Id,
                    };
                    CaveBuildHelperScriptOrchestrator.Queue(
                        CaveBuildHelperScriptOrchestrator.Moment.TerrainLadderRung,
                        ladderCtx,
                        (_, _) => GradeTerrainLadderRung(state, def, rungNum, rungTotal));
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel($"terrain ladder grade {rungNum}/{rungTotal}"));
        }

        static void GradeTerrainLadderRung(
            QueueState state,
            SurfaceTerrainBuildLadder.TerrainRungDef def,
            int rungNum,
            int rungTotal)
        {
            if (state.Ground?.Terrain == null)
            {
                Complete(state, false, "Terrain ladder aborted — terrain missing.");
                return;
            }

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] Terrain ladder grade {rungNum}/{rungTotal}: {def.Id}",
                0.55f + 0.035f * (rungNum / (float)rungTotal));

            CaveBuildEditorLog.LogSurface(
                $"[TerrainLadder] Grading {def.Id} ({rungNum}/{rungTotal})…",
                forceUnityConsole: true);
            CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                $"Terrain ladder {rungNum}/{rungTotal}: {def.Id}");

            var stage = SurfaceTerrainBuildLadder.GradeOneRung(
                def,
                state.Ground,
                state.Request,
                state.Surface,
                ref state.LadderVegCatalog);
            state.LadderReport.Stages.Add(stage);
            CaveBuildEditorLog.LogSurface(
                $"[TerrainLadder] {def.Id} → {stage.Score} ({(stage.Passed ? "pass" : "fail")})",
                forceUnityConsole: true);
            state.LadderRungIndex++;
            ScheduleLadderGradeRung(state);
        }

        static void ScheduleLadderFixStep(QueueState state)
        {
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    if (state.Ground?.Terrain == null)
                    {
                        Complete(state, false, "Terrain ladder aborted — terrain missing.");
                        return;
                    }

                    CaveBuildEditorLog.LogSurface(
                        $"[TerrainLadder] Fix iteration {state.LadderIteration + 1}/{state.MaxLadderIterations}…",
                        forceUnityConsole: true);

                    EditorUtility.DisplayProgressBar(
                        "Environment Kit",
                        $"[Surface] Terrain grading ladder fix ({state.LadderIteration + 1}/{state.MaxLadderIterations})…",
                        0.55f + 0.03f * (state.LadderIteration / (float)Mathf.Max(1, state.MaxLadderIterations)));

                    var report = state.LadderReport;

                    if (report.BuildAcceptable)
                    {
                        EditorUtility.ClearProgressBar();
                        CaveBuildEditorLog.LogSurface(
                            $"[TerrainLadder] PASS {report.OverallScore} ({report.LetterGrade}) after {state.LadderIteration} fix iteration(s).",
                            forceUnityConsole: true);
                        Complete(
                            state,
                            state.PhasesOk,
                            state.PhasesOk
                                ? $"Terrain phases + grading ladder PASS ({report.OverallScore})."
                                : "Terrain phases had issues; ladder PASS — see SurfaceTerrainPhaseLog.json.");
                        return;
                    }

                    var rung = SurfaceTerrainBuildLadder.PickActiveRung(report, state.SkipRungs);
                    if (!string.IsNullOrEmpty(rung))
                    {
                        if (rung == state.LastLadderFailingRung &&
                            report.OverallScore <= state.LastLadderOverallScore)
                            state.SameRungFixStreak++;
                        else
                        {
                            state.LastLadderFailingRung = rung;
                            state.SameRungFixStreak = 1;
                        }

                        state.LastLadderOverallScore = report.OverallScore;
                    }

                    if (!string.IsNullOrEmpty(rung) && state.SameRungFixStreak >= 3)
                    {
                        Debug.LogWarning(
                            $"[TerrainLadder] Rung '{rung}' did not improve score after {state.SameRungFixStreak} fix(es) — skipping. " +
                            "Open TerrainBuildTailoredAgentPrompt.md for Cursor fix.");
                        state.SkipRungs.Add(rung);
                        state.SameRungFixStreak = 0;
                        rung = SurfaceTerrainBuildLadder.PickActiveRung(report, state.SkipRungs);
                    }

                    if (string.IsNullOrEmpty(rung) || state.LadderIteration >= state.MaxLadderIterations)
                    {
                        EditorUtility.ClearProgressBar();
                        if (state.LadderIteration >= state.MaxLadderIterations)
                        {
                            var activeRung = SurfaceTerrainBuildLadder.PickActiveRung(report, state.SkipRungs);
                            if (!string.IsNullOrEmpty(activeRung))
                            {
                                TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(
                                    activeRung,
                                    report,
                                    state.Request.Seed,
                                    state.LadderIteration);
                                TerrainBuildRungPromptExporter.TryExportRungAsync(activeRung, out _);
                            }
                        }

                        Debug.LogWarning(
                            "[CaveBuild] Terrain grading ladder below target — see SurfaceTerrainBuildLadderReport.json.");
                        Complete(
                            state,
                            state.PhasesOk,
                            state.PhasesOk
                                ? "Terrain phases complete; ladder report written."
                                : "Terrain phases + ladder finished with issues — see logs.");
                        return;
                    }

                    var fixRung = rung;
                    var fixCtx = new CaveBuildHelperScriptOrchestrator.Context
                    {
                        Request = state.Request,
                        Rung = fixRung,
                        MeatPass = state.LadderIteration,
                        PhaseId = "terrain_build_ladder",
                    };
                    CaveBuildHelperScriptOrchestrator.Queue(
                        CaveBuildHelperScriptOrchestrator.Moment.TerrainLadderRung,
                        fixCtx,
                        (helperOk, helperMsg) =>
                        {
                            if (!helperOk && !string.IsNullOrEmpty(helperMsg))
                                Debug.LogWarning("[TerrainLadder] Helper scripts: " + helperMsg);

                            CaveBuildEditorLog.LogSurface(
                                $"[TerrainLadder] FIX STAGE — rung={fixRung} (see TerrainBuildTailoredAgentPrompt.md)",
                                forceUnityConsole: true);
                            TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(
                                fixRung,
                                report,
                                state.Request.Seed,
                                state.LadderIteration);

                            SurfaceTerrainLadderFixer.QueueTryFix(
                                fixRung,
                                state.Ground,
                                state.Request,
                                state.Surface,
                                (fixedOk, action) =>
                                {
                                    if (!fixedOk)
                                    {
                                        state.SkipRungs.Add(fixRung);
                                        Debug.LogWarning(
                                            $"[TerrainLadder] Could not auto-fix rung '{fixRung}' — skipping.");
                                    }
                                    else if (!string.IsNullOrEmpty(action))
                                    {
                                        CaveBuildEditorLog.LogSurface(
                                            $"[TerrainLadder] Fix [{fixRung}]: {action}",
                                            forceUnityConsole: true);
                                    }

                                    state.LadderIteration++;
                                    state.LadderReport.Stages.Clear();
                                    state.LadderRungIndex = 0;
                                    ScheduleLadderGradeRung(state);
                                });
                        });
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel(
                    $"terrain ladder fix {state.LadderIteration + 1}/{state.MaxLadderIterations}"));
        }

        static void SchedulePhase4LightSmooth(QueueState state)
        {
            if (state.Ground?.Terrain == null)
            {
                Complete(state, false, "Terrain phases aborted — terrain missing.");
                return;
            }

            var index = 3;
            var title = PhaseTitles[index];
            if (state.Request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Terrain phase 4 — skipped (Florida DEM + surface world already committed).",
                    forceUnityConsole: true);
                state.PhasesOk &= true;
                AppendPhaseLog(
                    state,
                    index,
                    true,
                    "Skipped — authoritative Florida DEM already on main tile.",
                    "FullWorld fast path");
                state.PhaseIndex++;
                SchedulePhaseStep(state);
                return;
            }

            CaveBuildActionPacing.ScheduleHeavyChain(
                () =>
                {
                    EditorUtility.DisplayProgressBar(
                        "Environment Kit",
                        "[Surface] Terrain phase 4 — light smooth (all terrains, one step)…",
                        0.44f);

                    SurfaceTerrainRefinement.QueueSmoothAllSurfaceTerrainsFootprint(
                        state.Ground.Terrain,
                        0.1f,
                        cells =>
                        {
                            var phaseMsg = $"Light footprint polish ({cells} cells, paced all terrains).";
                            state.PhasesOk &= cells >= 0;
                            AppendPhaseLog(state, index, true, phaseMsg, "paced footprint");
                            CaveBuildEditorLog.LogSurface(
                                $"[Surface] {title} — OK: {phaseMsg}",
                                forceUnityConsole: true);
                            state.PhaseIndex++;
                            SchedulePhaseStep(state);
                        });
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel("terrain phase 4 light smooth"));
        }

        static void SchedulePhase0OuterSmooth(QueueState state)
        {
            if (state.Ground?.Terrain == null)
            {
                Complete(state, false, "Terrain phases aborted — terrain missing.");
                return;
            }

            state.Phase0SmoothCells = 0;
            var title = PhaseTitles[0];
            var terrainCount = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(state.Ground.Terrain).Count;

            if (state.Request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild &&
                terrainCount > 1)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Terrain phase 1 — skipped full-tile smooth on neighbor grid (preserves Florida LiDAR + seam seed).",
                    forceUnityConsole: true);
                SurfaceTerrainTileExpansion.QueueStitchNeighborSeamsOnly(
                    state.Ground.Terrain,
                    () =>
                    {
                        var stitched = SurfaceTerrainTileExpansion.CollectGameplayTiles(state.Ground.Terrain).Length;
                        state.Phase0SmoothCells = stitched;
                        var phaseMsg =
                            $"Seam stitch only on {terrainCount - 1} neighbor tile(s) ({stitched} stitched) — no DEM re-stamp or footprint smooth.";
                        state.PhasesOk = stitched > 0;
                        AppendPhaseLog(state, 0, state.PhasesOk, phaseMsg, "FullWorld neighbor fast path");
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] {title} — {(state.PhasesOk ? "OK" : "issues")}: {phaseMsg}",
                            forceUnityConsole: true);
                        state.PhaseIndex++;
                        SchedulePhaseStep(state);
                    });
                return;
            }

            var unifiedExtent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                state.Ground.Terrain,
                state.Center,
                state.Extent);

            SurfaceTerrainRefinement.QueueSmoothAllSurfaceTerrainsFootprint(
                state.Ground.Terrain,
                0.18f,
                cells =>
                {
                    var deNoise = 0;
                    if (terrainCount <= 1)
                    {
                        SurfaceTerrainPlayRegion.ForEachSurfaceTerrainUnified(
                            state.Ground.Terrain,
                            state.Center,
                            (terrain, playCenter) =>
                            {
                                deNoise += SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                                    terrain,
                                    playCenter,
                                    unifiedExtent,
                                    strength: 0.22f);
                                terrain.Flush();
                            });
                    }

                    state.Phase0SmoothCells = cells + deNoise;
                    var phaseMsg = terrainCount <= 1
                        ? $"Uniform smooth + de-noise on main terrain — {state.Phase0SmoothCells} cells."
                        : $"Uniform smooth on {terrainCount} terrain(s) — {state.Phase0SmoothCells} cells (neighbors: no radial de-noise).";
                    state.PhasesOk &= state.Phase0SmoothCells > 0;
                    AppendPhaseLog(state, 0, state.PhasesOk, phaseMsg, "paced per tile");
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] {title} — {(state.PhasesOk ? "OK" : "issues")}: {phaseMsg}",
                        forceUnityConsole: true);
                    state.PhaseIndex++;
                    SchedulePhaseStep(state);
                });
        }

        static void AppendPhaseLog(QueueState state, int index, bool phaseOk, string phaseMsg, string gateMsg)
        {
            state.Log.AppendLine("    {");
            state.Log.AppendLine($"      \"id\": \"{PhaseIds[index]}\",");
            state.Log.AppendLine($"      \"passed\": {(phaseOk ? "true" : "false")},");
            state.Log.AppendLine($"      \"message\": \"{Escape(phaseMsg)}\",");
            state.Log.AppendLine($"      \"gate\": \"{Escape(gateMsg)}\"");
            state.Log.AppendLine(index < PhaseCount - 1 ? "    }," : "    }");
        }

        static void FinishPhaseLog(QueueState state)
        {
            state.Log.AppendLine("  ]");
            state.Log.AppendLine("}");
            WriteLog(state.Log.ToString());
        }

        static void Complete(QueueState state, bool ok, string summary)
        {
            IsPipelineActive = false;
            EditorUtility.ClearProgressBar();

            if (state?.Request == null || !ok)
            {
                state.OnComplete?.Invoke(ok, summary);
                return;
            }

            var ctx = CaveBuildHelperScriptOrchestrator.MakeContext(state.Request);
            CaveBuildHelperScriptOrchestrator.Queue(
                CaveBuildHelperScriptOrchestrator.Moment.TerrainPipelineComplete,
                ctx,
                (_, _) => FinishTerrainPipelineComplete(state, ok, summary));
        }

        static void FinishTerrainPipelineComplete(QueueState state, bool ok, string summary)
        {
            var cave = UnityEngine.Object.FindAnyObjectByType<CaveBuildMetadata>()?.transform;
            if (cave != null)
            {
                CaveBuildActionPacing.ApplyCooldownTimers(CaveBuildActionPacing.ActionWeight.Light);
                CaveGroundPlacementUtility.ReseatCaveUnderTerrainAfterSurface(
                    cave, state.Ground, out var reseatMsg);
                if (!string.IsNullOrEmpty(reseatMsg))
                    CaveBuildEditorLog.LogSurface("[Surface] " + reseatMsg, forceUnityConsole: true);

                SurfaceCaveOpeningAligner.TryAlignCaveRootToOpening(cave, state.Ground, preferredSector: -1);
                CaveGroundPlacementUtility.EnsureRootWorldXZLock(cave);
                SurfaceTerrainAutomatedValidation.RunAll(cave, state.Ground, state.Request);
            }

            if (CaveBuildWorkflowGuardrails.TryFinalSurfaceNavMeshCommit(
                    state.Ground,
                    state.Surface,
                    state.EnvRoot,
                    out var navCommitMsg))
                CaveBuildEditorLog.LogSurface("[Surface] " + navCommitMsg, forceUnityConsole: true);
            else
                CaveBuildEditorLog.LogSurfaceWarning("[Surface] " + navCommitMsg);

            CaveBuildSurfaceCompletionGate.MarkTerrainGradingComplete(state.Request);
            CaveBuildEditorLog.LogSurface(
                "[Surface] Terrain pipeline complete — cave meat loop may proceed.",
                forceUnityConsole: true);
            state.OnComplete?.Invoke(ok, summary);
        }

        static bool RunPhase(
            int phase,
            SceneGroundInfo ground,
            Transform surface,
            EnvironmentAuthoringKit.EnvironmentRoot envRoot,
            Vector3 center,
            float extent,
            WorldGenerationRequest request,
            out string message)
        {
            message = string.Empty;
            var terrain = ground.Terrain;
            switch (phase)
            {
                case 0:
                {
                    var cells = 0;
                    ForEachSurfaceTerrainUnified(terrain, center, (t, playCenter) =>
                    {
                        cells += SurfaceTerrainRefinement.SmoothTerrainFootprintUniform(t, 0.18f);
                    });
                    message = $"Uniform smooth {cells} cells (all surface terrains, one world center).";
                    return cells > 0;
                }
                case 1:
                    return SurfaceTerrainRefinement.TryRefineRoadsAndWater(
                        terrain, surface, center, extent, request.Seed, out message);
                case 2:
                    SurfaceTerrainRefinement.TryLidarRefineAndSmooth(
                        terrain, center, extent * 0.85f, request.Seed + 7, out var wMsg);
                    message = "Water/trail hydro polish: " + wMsg;
                    return true;
                case 3:
                {
                    var final = 0;
                    ForEachSurfaceTerrainUnified(terrain, center, (t, playCenter) =>
                    {
                        final += SurfaceTerrainRefinement.SmoothTerrainFootprintUniform(t, 0.1f);
                    });
                    message = $"Light footprint polish ({final} cells, all terrains).";
                    return true;
                }
                case 4:
                {
                    var unifiedExtent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                        terrain, center, extent);
                    return SurfaceDemGeoreferenceAuthor.ApplyGeoreferencedStamp(
                        terrain, center, unifiedExtent, request.Seed, out message);
                }
                case 5:
                {
                    if (envRoot == null)
                    {
                        message = "Surface NavMesh skipped — no environment root.";
                        return false;
                    }

                    var surfaceRoot = surface != null
                        ? surface
                        : envRoot.transform.Find(SurfaceWorldPaths.RootName);
                    return SurfaceNavMeshBaker.BakePhase(
                        envRoot.transform, terrain, surfaceRoot, out message);
                }
                default:
                    return false;
            }
        }

        internal static void ForEachSurfaceTerrain(
            Terrain mainTerrain,
            Action<Terrain, Vector3> action) =>
            SurfaceTerrainPlayRegion.ForEachSurfaceTerrain(mainTerrain, action);

        internal static void ForEachSurfaceTerrainUnified(
            Terrain mainTerrain,
            Vector3 unifiedPlayCenter,
            Action<Terrain, Vector3> action) =>
            SurfaceTerrainPlayRegion.ForEachSurfaceTerrainUnified(
                mainTerrain,
                unifiedPlayCenter,
                action);

        static void WriteLog(string json)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, LogRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, json);
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
