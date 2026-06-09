#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Phased meat-loop build for the mystical medieval hollow tree landmark.
    /// Each phase logs progress and notifies <see cref="CaveBuildLiveSceneFeedback"/> for camera beats.
    /// </summary>
    public static class HollowTitanLandmarkMeatPhases
    {
        public enum Phase
        {
            SitePick = 0,
            BaseSnap,
            TrunkShell,
            HollowCarve,
            FloorPlates,
            StairSpiral,
            DeadBranchScatter,
            EntranceFraming,
            PerFloorSpawnMarkers,
            LightingFogMood,
            LandmarkLootTable,
            EnemyPatrolNodes,
            Done,
        }

        public static readonly string[] PhaseLabels =
        {
            "Hollow Titan — site pick",
            "Hollow Titan — base snap",
            "Hollow Titan — trunk shell",
            "Hollow Titan — hollow carve",
            "Hollow Titan — floor plates",
            "Hollow Titan — stair spiral",
            "Hollow Titan — dead branches",
            "Hollow Titan — entrance frame",
            "Hollow Titan — floor spawn markers",
            "Hollow Titan — lighting & fog",
            "Hollow Titan — landmark loot table",
            "Hollow Titan — enemy patrol nodes",
        };

        /// <summary>Concept art baseline (~30 m bowl radius) × display scale for in-world landmark legibility.</summary>
        const float ConceptTrunkRadiusMeters = 30f;
        const float ConceptTrunkHeightMeters = 82f;
        /// <summary>600% of concept bowl footprint (6× radius). Interior height stays human-scale inside the bowl.</summary>
        const float TitanDisplayScaleMultiplier = 7.5f;

        static float MassiveElementScale(BuildSession session) =>
            session.Massive ? session.TrunkRadius / ConceptTrunkRadiusMeters : 1f;

        /// <summary>Human-scale vertical stack inside the mega bowl — not 6× full tree height.</summary>
        static float ResolveMassiveInteriorHeightMeters(BuildSession session)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            var floors = Mathf.Max(2, settings.hollowTitanFloorCount);
            var floorH = Mathf.Max(4f, settings.hollowTitanFloorHeightMeters);
            return floors * floorH + 6f;
        }

        static float ResolveStumpWallHeightMeters(BuildSession session) =>
            session.Massive ? ResolveMassiveInteriorHeightMeters(session) : session.TrunkHeight;

        sealed class BuildSession
        {
            public Terrain MainTerrain;
            public WorldGenerationRequest Request;
            public bool Massive = true;
            public Phase Phase = Phase.SitePick;
            public Action OnComplete;
            public Transform Root;
            public HollowTitanLandmarkBuildData BuildData;
            public HollowTitanLandmarkFloorPlanner.Plan FloorPlan;
            public Terrain SiteTile;
            public Vector3 SiteWorld;
            public float TrunkRadius;
            public float TrunkHeight;
            public float TrunkYaw;
            public System.Random Rng;
            public HollowTitanLadderReport LadderReport = new();
            public bool ForceSyncFullBuild;
            public bool PreservedExisting;
        }

        static BuildSession _active;

        public static bool IsRunning => _active != null;

        public static void BeginBuild(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            bool massive = true,
            Action onComplete = null,
            bool forceSyncFullBuild = false)
        {
            if (mainTerrain == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (IsRunning)
            {
                CaveBuildEditorLog.LogSurfaceWarning("[HollowTitan] Phased build already running — ignored duplicate.");
                return;
            }

            if (HollowTitanLandmarkAuthor.TryFindPreservableRoot(request, out var preservedRoot, out var preservedData))
            {
                EnsureLandmarkExteriorVisible();
                CaveBuildEditorLog.LogSurface(
                    forceSyncFullBuild
                        ? "[HollowTitan] Existing landmark preserved — skipping meat rebuild during Full AAA sync."
                        : "[HollowTitan] Existing landmark preserved — skipping phased meat rebuild.",
                    forceUnityConsole: false);
                onComplete?.Invoke();
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            Cc0ContentImportUtility.EnsureAll(importItems: true);

            _active = new BuildSession
            {
                MainTerrain = mainTerrain,
                Request = request,
                Massive = massive,
                OnComplete = onComplete,
                Rng = new System.Random(request.Seed + 0x54495441),
                ForceSyncFullBuild = forceSyncFullBuild,
            };

            var queueBetweenPhases = ShouldQueueMeatPhases(forceSyncFullBuild);

            if (!settings.hollowTitanPhasedBuild && !CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                RunAllPhasesSync(_active);
                Finish(_active);
                return;
            }

            HollowTitanPhaseResearchGate.EnsureBeforeMeatPhase(0, request.Seed, out _);

            CaveBuildEditorLog.LogSurface(
                queueBetweenPhases
                    ? "[HollowTitan] Starting queued phased meat build — 12 steps (paced)."
                    : "[HollowTitan] Starting phased meat build — 12 steps (sync).",
                forceUnityConsole: true);

            if (queueBetweenPhases)
            {
                ScheduleNext(_active);
                return;
            }

            RunAllPhasesSync(_active);
            Finish(_active);
        }

        static bool ShouldQueueMeatPhases(bool forceSyncFullBuild)
        {
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                return true;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            return !forceSyncFullBuild &&
                   settings.hollowTitanPhasedBuild &&
                   CaveBuildLiveSceneFeedback.SessionActive;
        }

        /// <summary>True while phased meat or 32-phase stump sculpt is still running (FullWorld must wait).</summary>
        public static bool IsBlockingSurfaceTerraform =>
            IsRunning || HollowTitanStumpSculptPhases.IsRunning;

        /// <summary>Re-snap base after terrain sculpt and refresh interior Y offsets.</summary>
        public static void ResnapAfterTerrainSculpt(Terrain mainTerrain)
        {
            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (root == null || mainTerrain == null)
                return;

            var data = root.GetComponent<HollowTitanLandmarkBuildData>();
            if (data == null)
            {
                HollowTitanLandmarkTerrainSnap.SnapRootBaseToGround(
                    mainTerrain,
                    root.transform,
                    HollowTitanLandmarkTerrainSnap.ResolveBaseClearance());
                return;
            }

            if (HollowTitanLandmarkTerrainSnap.ResnapFromBuildData(mainTerrain, data))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[HollowTitan] Re-snapped to terrain @ {root.transform.position} after sculpt.",
                    forceUnityConsole: false);
                CaveBuildLiveSceneFeedback.NotifyStep(
                    "Hollow Titan re-snapped to terrain",
                    root.transform,
                    frameScene: true);
            }
        }

        public static void RunSinglePhase(Phase phase, Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null || request == null)
                return;

            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (root == null && phase != Phase.SitePick)
            {
                BeginBuild(mainTerrain, request, massive: true);
                return;
            }

            var session = new BuildSession
            {
                MainTerrain = mainTerrain,
                Request = request,
                Massive = true,
                Phase = phase,
                Root = root != null ? root.transform : null,
                BuildData = root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null,
                Rng = new System.Random(request.Seed + 0x54495441),
            };

            if (session.BuildData != null)
            {
                session.TrunkRadius = session.BuildData.TrunkRadius;
                session.TrunkHeight = session.BuildData.TrunkHeight;
                session.SiteWorld = session.BuildData.SiteWorldPosition;
            }

            RunPhase(session, phase);
        }

        static void ScheduleNext(BuildSession session) =>
            CaveBuildActionPacing.ScheduleLight(
                () => RunQueuedStep(session),
                CaveBuildPipelineDomains.QueueLabel($"Hollow Titan {session.Phase}"));

        static void RunQueuedStep(BuildSession session)
        {
            if (session == null)
                return;

            var phase = session.Phase;
            var seed = session.Request != null ? session.Request.Seed : 0;

            HollowTitanPhaseResearchGate.EnsureBeforeMeatPhaseAsync(
                (int)phase,
                seed,
                () => ExecuteQueuedStep(session, phase));
        }

        static void ExecuteQueuedStep(BuildSession session, Phase phase)
        {
            RunPhase(session, phase);
            NotifyPhase(phase, session.Root);
            GradePhaseRung(session, phase);
            if (((int)phase + 1) % 4 == 0 &&
                !CaveBuildLateBuildPerformance.ShouldSkipIntermediateTitanMilestoneSave())
            {
                CaveBuildFullWorldGridCheckpoint.SaveActiveSceneMilestone(
                    $"Hollow Titan meat phase {(int)phase + 1}/12");
            }

            if (phase >= Phase.EnemyPatrolNodes)
            {
                session.Phase = Phase.Done;
                Finish(session);
                return;
            }

            session.Phase = phase + 1;
            ScheduleNext(session);
        }

        static void RunAllPhasesSync(BuildSession session)
        {
            if (session.PreservedExisting &&
                session.BuildData != null &&
                session.BuildData.MeatPhasesComplete)
            {
                EnsureLandmarkExteriorVisible();
                CaveBuildEditorLog.LogSurface(
                    "[HollowTitan] Meat phases already complete — skipped destructive re-run.",
                    forceUnityConsole: false);
                return;
            }

            var seed = session.Request != null ? session.Request.Seed : 0;
            for (var p = Phase.SitePick; p <= Phase.EnemyPatrolNodes; p++)
            {
                HollowTitanPhaseResearchGate.EnsureBeforeMeatPhase((int)p, seed, out _);
                RunPhase(session, p);
                NotifyPhase(p, session.Root);
                GradePhaseRung(session, p);
                CaveBuildActionPacing.TouchQueueActivity();
            }
        }

        static void Finish(BuildSession session)
        {
            var cb = session?.OnComplete;
            if (session?.BuildData != null)
            {
                session.BuildData.MarkMeatPhasesComplete();
                EditorUtility.SetDirty(session.BuildData);
            }

            if (session?.Root != null && session.Massive)
                EnsureVisibleStumpSilhouetteOnRoot(session);

            EnsureLandmarkExteriorVisible();

            FinalizeLandmarkExterior(session);

            if (session?.LadderReport != null)
            {
                session.LadderReport.Seed = session.Request != null ? session.Request.Seed : 0;
                HollowTitanBuildLadder.FinalizeReport(session.LadderReport);
                CaveBuildEditorLog.LogSurface(
                    $"[HollowTitan] Ladder report — {session.LadderReport.OverallScore}/100 " +
                    $"({session.LadderReport.LetterGrade}), acceptable={session.LadderReport.BuildAcceptable}. " +
                    $"→ {HollowTitanBuildLadder.ReportPath}",
                    forceUnityConsole: true);
            }

            _active = null;
            CaveBuildEditorLog.LogSurface(
                "[HollowTitan] Phased meat build complete.",
                forceUnityConsole: true);
            CaveBuildFullWorldGridCheckpoint.SaveActiveSceneMilestone("Hollow Titan meat complete");
            cb?.Invoke();
        }

        static void GradePhaseRung(BuildSession session, Phase phase)
        {
            if (session?.Root == null)
                return;

            session.LadderReport ??= new HollowTitanLadderReport();
            session.LadderReport.Seed = session.Request != null ? session.Request.Seed : 0;
            HollowTitanBuildLadder.GradeAndLogPhase(
                phase,
                session.Root,
                session.MainTerrain,
                session.Request,
                session.LadderReport);
        }

        static void NotifyPhase(Phase phase, Transform focus)
        {
            var idx = (int)phase;
            if (idx < 0 || idx >= PhaseLabels.Length)
                return;

            var label = PhaseLabels[idx];
            CaveBuildEditorLog.LogSurface($"[HollowTitan] {label}", forceUnityConsole: false);
            HollowTitanConceptCatalog.LogPhasePrompt(phase);
            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Agent prompts → {HollowTitanPhasePromptBridge.ActivePhasePromptPath} | gate: {HollowTitanPhaseResearchGate.GateRel}",
                forceUnityConsole: false);
            CaveBuildLiveSceneFeedback.NotifySurfacePhase(label);
            if (focus != null)
                CaveBuildLiveSceneFeedback.NotifyStep(label, focus, frameScene: true);
        }

        static void RunPhase(BuildSession session, Phase phase)
        {
            switch (phase)
            {
                case Phase.SitePick:
                    RunSitePick(session);
                    break;
                case Phase.BaseSnap:
                    RunBaseSnap(session);
                    break;
                case Phase.TrunkShell:
                    RunTrunkShell(session);
                    break;
                case Phase.HollowCarve:
                    RunHollowCarve(session);
                    break;
                case Phase.FloorPlates:
                    RunFloorPlates(session);
                    break;
                case Phase.StairSpiral:
                    RunStairSpiral(session);
                    break;
                case Phase.DeadBranchScatter:
                    RunDeadBranchScatter(session);
                    break;
                case Phase.EntranceFraming:
                    RunEntranceFraming(session);
                    break;
                case Phase.PerFloorSpawnMarkers:
                    RunPerFloorSpawnMarkers(session);
                    break;
                case Phase.LightingFogMood:
                    RunLightingFogMood(session);
                    break;
                case Phase.LandmarkLootTable:
                    RunLandmarkLootTable(session);
                    break;
                case Phase.EnemyPatrolNodes:
                    RunEnemyPatrolNodes(session);
                    break;
            }
        }

        static void RunSitePick(BuildSession session)
        {
            if (HollowTitanLandmarkAuthor.TryFindPreservableRoot(
                    session.Request,
                    out var preservedRoot,
                    out var preservedData))
            {
                session.Root = preservedRoot;
                session.BuildData = preservedData;
                session.SiteWorld = preservedData.SiteWorldPosition;
                session.TrunkRadius = preservedData.TrunkRadius;
                session.TrunkHeight = preservedData.TrunkHeight;
                session.PreservedExisting = true;
                session.FloorPlan = HollowTitanLandmarkFloorPlanner.Build(
                    preservedData.BuildSeed,
                    preservedData.FloorCount,
                    preservedData.TrunkRadius,
                    preservedData.TrunkHeight,
                    preservedData.FloorHeightMeters);
                session.TrunkYaw = session.FloorPlan.EntranceYawDegrees + 180f;
                if (Mathf.Abs(preservedData.EntranceYawDegrees) < 0.01f)
                    preservedData.SetEntranceYaw(session.FloorPlan.EntranceYawDegrees);
                CaveBuildEditorLog.LogSurface(
                    "[HollowTitan] Site pick — preserving existing landmark (no destroy during surface build).",
                    forceUnityConsole: false);
                return;
            }

            var existing = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (existing != null)
            {
                if (HollowTitanLandmarkAuthor.HasMeatBuildProgress(existing.transform))
                {
                    session.Root = existing.transform;
                    session.BuildData = existing.GetComponent<HollowTitanLandmarkBuildData>();
                    session.PreservedExisting = true;
                    if (session.BuildData != null)
                    {
                        session.SiteWorld = session.BuildData.SiteWorldPosition;
                        session.TrunkRadius = session.BuildData.TrunkRadius;
                        session.TrunkHeight = session.BuildData.TrunkHeight;
                    }

                    CaveBuildEditorLog.LogSurfaceWarning(
                        "[HollowTitan] Site pick — adopting existing landmark root (no delete).",
                        forceUnityConsole: true);
                    return;
                }

                if (PipelineContentPreservePolicy.ShouldAllowDestroy(
                        existing,
                        "incomplete Hollow Titan root"))
                    UnityEngine.Object.DestroyImmediate(existing);
                else
                {
                    session.Root = existing.transform;
                    session.BuildData = existing.GetComponent<HollowTitanLandmarkBuildData>();
                    session.PreservedExisting = true;
                    CaveBuildEditorLog.LogSurface(
                        "[PipelineIntegrate] Site pick — kept existing landmark for merge.",
                        forceUnityConsole: false);
                    return;
                }
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            var useAll = session.Massive ||
                         CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid ||
                         session.Request.SurfaceScope == SurfaceBuildScope.FullWorld;

            if (!HollowTitanLandmarkAuthor.TryPickLandmarkSpotPublic(
                    session.MainTerrain,
                    session.Request.Seed,
                    useAll,
                    out session.SiteTile,
                    out session.SiteWorld))
            {
                session.SiteTile = session.MainTerrain;
                var td = session.MainTerrain.terrainData;
                var lx = td.size.x * 0.5f;
                var lz = td.size.z * 0.5f;
                HollowTitanLandmarkTerrainSnap.TrySampleGroundY(
                    session.MainTerrain,
                    session.MainTerrain.transform.position.x + lx,
                    session.MainTerrain.transform.position.z + lz,
                    out var gy,
                    out _);
                session.SiteWorld = new Vector3(
                    session.MainTerrain.transform.position.x + lx,
                    gy,
                    session.MainTerrain.transform.position.z + lz);
            }

            if (session.Massive)
            {
                var scaleJitter = 0.88f + (float)session.Rng.NextDouble() * 0.28f;
                session.TrunkRadius = ConceptTrunkRadiusMeters * scaleJitter * TitanDisplayScaleMultiplier;
                session.TrunkHeight = ResolveMassiveInteriorHeightMeters(session);
            }
            else
            {
                session.TrunkRadius = 4f;
                session.TrunkHeight = 12f;
            }

            var rootGo = new GameObject(HollowTitanLandmarkAuthor.RootName);
            session.Root = rootGo.transform;
            session.Root.position = new Vector3(session.SiteWorld.x, session.SiteWorld.y, session.SiteWorld.z);

            session.BuildData = rootGo.AddComponent<HollowTitanLandmarkBuildData>();
            session.BuildData.Configure(
                session.Request.Seed,
                settings.hollowTitanFloorCount,
                session.TrunkRadius,
                session.TrunkHeight,
                settings.hollowTitanBaseClearanceMeters,
                settings.hollowTitanFloorHeightMeters,
                session.SiteWorld,
                session.SiteTile != null ? session.SiteTile.name : string.Empty);

            session.FloorPlan = HollowTitanLandmarkFloorPlanner.Build(
                session.Request.Seed,
                settings.hollowTitanFloorCount,
                session.TrunkRadius,
                session.TrunkHeight,
                settings.hollowTitanFloorHeightMeters);

            session.TrunkYaw = session.FloorPlan.EntranceYawDegrees + 180f;
            session.BuildData.SetEntranceYaw(session.FloorPlan.EntranceYawDegrees);
        }

        static void RunBaseSnap(BuildSession session)
        {
            if (session.Root == null)
                return;

            var clearance = session.BuildData != null
                ? session.BuildData.BaseClearanceMeters
                : HollowTitanLandmarkTerrainSnap.ResolveBaseClearance();

            HollowTitanLandmarkTerrainSnap.SnapRootBaseToGround(
                session.MainTerrain,
                session.Root,
                clearance);

            if (session.BuildData != null)
            {
                var pos = session.Root.position;
                session.SiteWorld = pos;
                session.BuildData.Configure(
                    session.BuildData.BuildSeed,
                    session.BuildData.FloorCount,
                    session.TrunkRadius,
                    session.TrunkHeight,
                    clearance,
                    session.BuildData.FloorHeightMeters,
                    pos,
                    session.SiteTile != null ? session.SiteTile.name : string.Empty,
                    session.FloorPlan != null ? session.FloorPlan.EntranceYawDegrees : session.BuildData.EntranceYawDegrees);
            }
        }

        static void RunTrunkShell(BuildSession session)
        {
            if (session.Root == null)
                return;

            if (session.PreservedExisting &&
                session.Root.Find("Exterior/StumpSilhouette/BarkTrunkWall") != null)
            {
                EnsureVisibleStumpSilhouetteOnRoot(session);
                EnsurePortal(session);
                CaveBuildEditorLog.LogSurface(
                    "[HollowTitan] Trunk shell — preserved landmark stump (skipped destructive rebuild).",
                    forceUnityConsole: false);
                return;
            }

            ClearChild(session.Root, "Exterior/TrunkShell");
            HollowTitanLandmarkCleanup.StripLegacyExteriorAndTrim(session.Root);

            var shellRoot = GetOrCreateChild(session.Root, "Exterior/TrunkShell");
            ClearChildren(shellRoot);
            EnsureCollisionFallbackShell(session, shellRoot);
            EnsureVisibleStumpSilhouette(session, shellRoot);

            CaveBuildEditorLog.LogSurface(
                HollowTitanExteriorTerraform.ShouldDeferUntilPostTerraform()
                    ? "[HollowTitan] Trunk shell — collision shell; stump terraform runs after surface sculpt."
                    : "[HollowTitan] Trunk shell — collision shell; run Resnap or Refresh Exterior to terraform stump.",
                forceUnityConsole: false);

            EnsurePortal(session);
        }

        static void EnsureCollisionFallbackShell(BuildSession session, Transform shellRoot)
        {
            var wallHeight = ResolveStumpWallHeightMeters(session);
            var outer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            outer.name = "BarkOuter";
            outer.transform.SetParent(shellRoot, false);
            outer.transform.localScale = new Vector3(
                session.TrunkRadius,
                wallHeight,
                session.TrunkRadius);
            outer.transform.localPosition = Vector3.up * (wallHeight * 0.5f);
            outer.transform.localRotation = Quaternion.Euler(0f, session.TrunkYaw, 0f);

            var mr = outer.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = false;
        }

        /// <summary>Visible hollow trunk wall (bleached bark outside, dark void inside) until terrain bowl replaces it.</summary>
        static void EnsureVisibleStumpSilhouette(BuildSession session, Transform shellRoot)
        {
            var visRoot = GetOrCreateChild(session.Root, "Exterior/StumpSilhouette");
            ClearChildren(visRoot);

            var wallH = Mathf.Clamp(ResolveStumpWallHeightMeters(session), 24f, 48f);
            var outerDiam = session.TrunkRadius * 1.08f;
            var innerDiam = session.TrunkRadius * 0.58f;
            var halfH = wallH * 0.5f;

            var bark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bark.name = "BarkTrunkWall";
            bark.transform.SetParent(visRoot, false);
            bark.transform.localScale = new Vector3(outerDiam, halfH, outerDiam);
            bark.transform.localPosition = Vector3.up * wallH * 0.5f;
            bark.transform.localRotation = Quaternion.Euler(0f, session.TrunkYaw, 0f);
            TintPrimitive(bark, new Color(0.76f, 0.69f, 0.55f));

            var hollow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hollow.name = "HollowTrunkVoid";
            hollow.transform.SetParent(visRoot, false);
            hollow.transform.localScale = new Vector3(innerDiam, halfH * 0.96f, innerDiam);
            hollow.transform.localPosition = Vector3.up * wallH * 0.5f;
            hollow.transform.localRotation = Quaternion.Euler(0f, session.TrunkYaw, 0f);
            TintPrimitive(hollow, new Color(0.1f, 0.08f, 0.06f));

            foreach (var col in visRoot.GetComponentsInChildren<Collider>())
            {
                if (col != null)
                    UnityEngine.Object.DestroyImmediate(col);
            }
        }

        static void FinalizeLandmarkExterior(BuildSession session)
        {
            if (session?.MainTerrain == null || session.Root == null || !session.Massive)
                return;

            var forceSculpt = session.ForceSyncFullBuild ||
                              !HollowTitanExteriorTerraform.ShouldDeferUntilPostTerraform();
            if (!forceSculpt)
            {
                CaveBuildEditorLog.LogSurface(
                    "[HollowTitan] Exterior trunk proxy ready — terrain bowl deferred until post-terraform.",
                    forceUnityConsole: false);
                return;
            }

            HollowTitanExteriorTerraform.SculptHollowStumpBowl(
                session.MainTerrain,
                session.BuildData != null
                    ? session.BuildData.SiteWorldPosition
                    : session.Root.position,
                session.TrunkRadius,
                session.TrunkHeight,
                session.Request != null ? session.Request.Seed : 0,
                session.FloorPlan != null
                    ? session.FloorPlan.EntranceYawDegrees
                    : session.BuildData != null
                        ? session.BuildData.EntranceYawDegrees
                        : 0f,
                session.Root,
                force: true);
            ResnapAfterTerrainSculpt(session.MainTerrain);
            GradePhaseRung(session, Phase.TrunkShell);
            CaveBuildLiveSceneFeedback.NotifyStep(
                "Hollow Titan trunk + bowl complete",
                session.Root,
                frameScene: true);
            CaveBuildEditorLog.LogSurface(
                "[HollowTitan] Full landmark exterior finalized (trunk proxy + terrain bowl) — surface may continue.",
                forceUnityConsole: true);
        }

        static void EnsureVisibleStumpSilhouetteOnRoot(BuildSession session)
        {
            if (session?.Root == null)
                return;

            var shell = session.Root.Find("Exterior/TrunkShell");
            if (shell == null)
                shell = GetOrCreateChild(session.Root, "Exterior/TrunkShell").transform;

            EnsureVisibleStumpSilhouette(session, shell);
        }

        /// <summary>Re-show stump proxy + interior after pipeline steps hide or strip the landmark.</summary>
        public static void EnsureLandmarkExteriorVisible()
        {
            var rootGo = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (rootGo == null)
                return;

            var root = rootGo.transform;
            var data = rootGo.GetComponent<HollowTitanLandmarkBuildData>();
            HollowTitanLandmarkCleanup.SetInteriorRenderersVisible(root, true);

            if (data == null)
                return;

            var session = new BuildSession
            {
                Root = root,
                Massive = true,
                TrunkRadius = data.TrunkRadius,
                TrunkHeight = data.TrunkHeight,
                TrunkYaw = data.EntranceYawDegrees + 180f,
            };
            EnsureVisibleStumpSilhouetteOnRoot(session);
            HollowTitanExteriorTerraform.SetStumpSilhouetteProxyVisible(root, true);
        }

        /// <summary>Re-show stump proxy + interior after an over-aggressive cleanup hid the landmark.</summary>
        public static void RestoreLandmarkSceneVisibility()
        {
            var rootGo = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (rootGo == null)
            {
                Debug.LogWarning("[HollowTitan] No HollowTitanLandmark in scene — nothing to restore.");
                return;
            }

            var root = rootGo.transform;
            HollowTitanLandmarkCleanup.StripLegacyExteriorAndTrim(root);
            EnsureLandmarkExteriorVisible();

            CaveBuildLiveSceneFeedback.NotifyStep(
                "Hollow Titan visibility restored",
                root,
                frameScene: true);
            Debug.Log(
                "[HollowTitan] Restored interior meshes + stump silhouette proxy (terrain bowl unchanged).",
                rootGo);
        }

        static void RunHollowCarve(BuildSession session)
        {
            var interior = GetOrCreateChild(session.Root, "Interior");
            ClearChild(interior, "HollowVolume");

            var volume = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            volume.name = "HollowVolume";
            volume.transform.SetParent(interior, false);
            var innerR = session.TrunkRadius * 0.62f;
            var innerH = session.TrunkHeight * 0.88f;
            volume.transform.localScale = new Vector3(innerR, innerH, innerR);
            volume.transform.localPosition = Vector3.up * (innerH * 0.5f + session.TrunkHeight * 0.06f);

            var col = volume.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.DestroyImmediate(col);
            var mr = volume.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = false;
        }

        static void RunFloorPlates(BuildSession session)
        {
            var floorsRoot = GetOrCreateChild(session.Root, "Interior/Floors");
            ClearChildren(floorsRoot);

            if (session.FloorPlan == null && session.BuildData != null)
            {
                session.FloorPlan = HollowTitanLandmarkFloorPlanner.Build(
                    session.Request.Seed,
                    session.BuildData.FloorCount,
                    session.TrunkRadius,
                    session.TrunkHeight,
                    session.BuildData.FloorHeightMeters);
            }

            if (session.FloorPlan == null)
                return;

            var buildSeed = session.BuildData != null ? session.BuildData.BuildSeed : session.Request.Seed;
            var floorHeight = session.FloorPlan.FloorHeightMeters;
            var entranceYaw = session.FloorPlan.EntranceYawDegrees;
            var totalWalls = 0;
            var elemScale = MassiveElementScale(session);

            foreach (var floor in session.FloorPlan.Floors)
            {
                var floorRoot = new GameObject($"Floor_{floor.Index:D2}");
                floorRoot.transform.SetParent(floorsRoot, false);
                floorRoot.transform.localPosition = Vector3.zero;

                var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                plate.name = "Plate";
                plate.transform.SetParent(floorRoot.transform, false);
                plate.transform.localScale = new Vector3(
                    floor.Radius * 1.85f,
                    0.35f * elemScale,
                    floor.Radius * 1.85f);
                plate.transform.localPosition = new Vector3(0f, floor.LocalY, 0f);
                TintPrimitive(plate, new Color(0.22f, 0.17f, 0.12f));

                var mazeRoot = new GameObject("Maze");
                mazeRoot.transform.SetParent(floorRoot.transform, false);
                mazeRoot.transform.localPosition = Vector3.zero;

                var mazeLayout = HollowTitanLandmarkInteriorMaze.Generate(
                    buildSeed,
                    floor.Index,
                    floor.Radius,
                    floorHeight,
                    entranceYaw,
                    elemScale);
                totalWalls += HollowTitanLandmarkInteriorMaze.BuildWallGeometry(
                    mazeRoot.transform,
                    mazeLayout,
                    floor.LocalY + 0.35f * elemScale,
                    floorHeight * 0.72f * elemScale,
                    HollowTitanLandmarkInteriorMaze.PickWallTint(floor.Index),
                    buildSeed + floor.Index);
            }

            BuildBoulderPlatforms(session);

            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Floor plates + interior mazes — {session.FloorPlan.FloorCount} levels, {totalWalls} wall cells.",
                forceUnityConsole: false);
        }

        static void BuildBoulderPlatforms(BuildSession session)
        {
            var platformsRoot = GetOrCreateChild(session.Root, "Interior/BoulderPlatforms");
            ClearChildren(platformsRoot);

            if (session.FloorPlan?.BoulderPlatforms == null)
                return;

            foreach (var platform in session.FloorPlan.BoulderPlatforms)
            {
                var boulder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                boulder.name = $"BoulderPlatform_F{platform.FloorIndex:D2}";
                boulder.transform.SetParent(platformsRoot, false);
                boulder.transform.localPosition = platform.LocalPosition;
                boulder.transform.localScale = Vector3.one * platform.Scale;
                TintPrimitive(boulder, new Color(0.32f, 0.27f, 0.2f));

                var anchorGo = new GameObject("HookAnchor");
                anchorGo.transform.SetParent(boulder.transform, false);
                anchorGo.transform.localPosition = Vector3.up * 0.42f;
                anchorGo.AddComponent<HollowTitanHookAnchor>();
            }
        }

        static void RunStairSpiral(BuildSession session)
        {
            var stairsRoot = GetOrCreateChild(session.Root, "Interior/Stairs");
            ClearChildren(stairsRoot);

            if (session.FloorPlan == null)
                return;

            var metrics = session.FloorPlan.Metrics;
            var elemScale = MassiveElementScale(session);
            var treadDepth = HollowTitanLandmarkFloorPlanner.TreadDepthMeters * elemScale;
            var treadHeight = HollowTitanLandmarkFloorPlanner.TreadHeightMeters * elemScale;

            var stepIndex = 0;
            foreach (var stair in session.FloorPlan.Stairs)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"Stair_{stepIndex:D3}_F{stair.FromFloor}";
                step.transform.SetParent(stairsRoot, false);
                step.transform.localPosition = stair.LocalPosition;
                step.transform.localRotation = Quaternion.Euler(0f, stair.YawDegrees, 0f);
                step.transform.localScale = new Vector3(3.6f * elemScale, treadHeight, treadDepth);
                TintPrimitive(step, new Color(0.35f, 0.28f, 0.2f));
                step.AddComponent<HollowTitanHookAnchor>();
                stepIndex++;
            }

            var hookNote = metrics != null && metrics.HookRequiredForVerticalGaps
                ? " Hook required for some vertical gaps."
                : " Jump traversal OK without hook.";
            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Stair spiral — {stepIndex} treads, " +
                $"rise {metrics?.StepRiseMeters:F2}m, run ~{metrics?.StepRunMeters:F2}m, " +
                $"{metrics?.StepsPerFloorTransition} steps/floor.{hookNote}",
                forceUnityConsole: false);
        }

        static void RunDeadBranchScatter(BuildSession session)
        {
            var extRoot = GetOrCreateChild(session.Root, "Exterior/DeadBranches");
            ClearChildren(extRoot);
        }

        static void RunEntranceFraming(BuildSession session)
        {
            var frameRoot = GetOrCreateChild(session.Root, "Exterior/Entrance");
            ClearChildren(frameRoot);

            if (session.FloorPlan == null)
                return;

            var local = session.FloorPlan.EntranceLocal;
            var forward = session.FloorPlan.EntranceForward;
            var entranceScale = session.TrunkRadius / ConceptTrunkRadiusMeters;

            var arch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arch.name = "EntranceArch";
            arch.transform.SetParent(frameRoot, false);
            arch.transform.localPosition = local + forward * (1.2f * MassiveElementScale(session));
            arch.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            arch.transform.localScale = new Vector3(8f, 10f, 1.2f) * entranceScale;
            TintPrimitive(arch, new Color(0.22f, 0.17f, 0.12f));

            var gap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gap.name = "EntranceOpening";
            gap.transform.SetParent(frameRoot, false);
            gap.transform.localPosition = local;
            gap.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            gap.transform.localScale = new Vector3(5.5f, 7f, 0.6f) * entranceScale;
            TintPrimitive(gap, new Color(0.05f, 0.04f, 0.03f));
        }

        static void RunPerFloorSpawnMarkers(BuildSession session)
        {
            var spawnsRoot = GetOrCreateChild(session.Root, "Spawns");
            var enemiesRoot = GetOrCreateChild(spawnsRoot, "Enemies");
            ClearChildren(enemiesRoot);

            if (session.FloorPlan == null)
                return;

            var topFloor = session.FloorPlan.FloorCount - 1;
            var elemScale = MassiveElementScale(session);

            foreach (var platform in session.FloorPlan.BoulderPlatforms)
            {
                var roleId = HollowTitanLandmarkSpawnCatalog.RoleForFloor(platform.FloorIndex, topFloor);
                var markerGo = new GameObject($"EnemySpawn_F{platform.FloorIndex}_Platform");
                markerGo.transform.SetParent(enemiesRoot, false);
                markerGo.transform.localPosition = platform.LocalPosition + Vector3.up * (platform.Scale * 0.35f);
                markerGo.transform.localRotation = Quaternion.LookRotation(
                    session.FloorPlan.EntranceForward,
                    Vector3.up);

                var marker = markerGo.AddComponent<HollowTitanLandmarkSpawnPoint>();
                marker.Configure(HollowTitanSpawnKind.Enemy, platform.FloorIndex, roleId, 1);
                AddCapsulePreview(
                    markerGo,
                    roleId == HollowTitanLandmarkSpawnCatalog.BossRoleId
                        ? new Color(0.75f, 0.12f, 0.55f, 0.55f)
                        : new Color(0.55f, 0.15f, 0.1f, 0.45f),
                    elemScale);
            }

            EnsureLandmarkSpawner(session);
        }

        static void RunLightingFogMood(BuildSession session)
        {
            var moodRoot = GetOrCreateChild(session.Root, "Mood");
            var lightsRoot = GetOrCreateChild(moodRoot, "Lights");
            ClearChildren(lightsRoot);

            if (session.FloorPlan == null)
                return;

            foreach (var floor in session.FloorPlan.Floors)
            {
                var lightGo = new GameObject($"MoodLight_F{floor.Index}");
                lightGo.transform.SetParent(lightsRoot, false);
                lightGo.transform.localPosition = new Vector3(0f, floor.LocalY + 2.5f * MassiveElementScale(session), 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.85f, 0.62f, 0.38f);
                light.intensity = floor.Index == 0 ? 1.4f : 0.9f;
                light.range = session.TrunkRadius * 1.2f;
            }

            var fogGo = GetOrCreateChild(moodRoot, "FogVolume");
            ClearChildren(fogGo);
            var fogMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fogMarker.name = "FogMoodVolume";
            fogMarker.transform.SetParent(fogGo, false);
            fogMarker.transform.localPosition = Vector3.up * (session.TrunkHeight * 0.45f);
            fogMarker.transform.localScale = Vector3.one * session.TrunkRadius * 1.6f;
            var fogMr = fogMarker.GetComponent<MeshRenderer>();
            if (fogMr != null)
                fogMr.enabled = false;
            var fogCol = fogMarker.GetComponent<Collider>();
            if (fogCol != null)
                UnityEngine.Object.DestroyImmediate(fogCol);
        }

        static void RunLandmarkLootTable(BuildSession session)
        {
            var lootRoot = GetOrCreateChild(GetOrCreateChild(session.Root, "Spawns"), "Loot");
            ClearChildren(lootRoot);

            HollowTitanLandmarkSpawnManifest.Write(session.Request.Seed, session.Root);

            if (session.FloorPlan == null)
                return;

            PlaceGrapplingHookLoot(session, lootRoot);

            var elemScale = MassiveElementScale(session);
            var rng = new System.Random(session.Request.Seed + 0x4C4F4F54);
            foreach (var floor in session.FloorPlan.Floors)
            {
                for (var l = 0; l < HollowTitanLandmarkSpawnManifest.LootPerFloor; l++)
                {
                    var defId = HollowTitanLandmarkSpawnManifest.PickLootId(rng);
                    if (defId == HollowTitanLandmarkSpawnCatalog.GrapplingHookLootId)
                        continue;

                    var angle = 45f + floor.Index * 40f + l * 55f + (float)rng.NextDouble() * 20f;
                    var rad = angle * Mathf.Deg2Rad;
                    var r = floor.Radius * 0.55f;

                    var markerGo = new GameObject($"LootSpawn_F{floor.Index}_L{l}_{defId}");
                    markerGo.transform.SetParent(lootRoot, false);
                    markerGo.transform.localPosition = new Vector3(
                        Mathf.Sin(rad) * r,
                        floor.LocalY + 0.65f * elemScale,
                        Mathf.Cos(rad) * r);

                    var marker = markerGo.AddComponent<HollowTitanLandmarkSpawnPoint>();
                    marker.Configure(HollowTitanSpawnKind.Loot, floor.Index, defId, 1);
                    AddCapsulePreview(markerGo, new Color(0.15f, 0.55f, 0.25f, 0.45f), elemScale);
                }
            }
        }

        static void PlaceGrapplingHookLoot(BuildSession session, Transform lootRoot)
        {
            var hookFloor = HollowTitanLandmarkSpawnCatalog.GrapplingHookFloorIndex;
            var floor = session.FloorPlan.Floors.Find(f => f.Index == hookFloor);
            if (floor == null && session.FloorPlan.Floors.Count > 0)
                floor = session.FloorPlan.Floors[Mathf.Min(1, session.FloorPlan.Floors.Count - 1)];

            if (floor == null)
                return;

            var hookAngle = session.FloorPlan.EntranceYawDegrees + 35f;
            var hookRad = hookAngle * Mathf.Deg2Rad;
            var elemScale = MassiveElementScale(session);
            var hookPos = new Vector3(
                Mathf.Sin(hookRad) * floor.Radius * 0.42f,
                floor.LocalY + 0.75f * elemScale,
                Mathf.Cos(hookRad) * floor.Radius * 0.42f);

            var hookGo = new GameObject(
                $"LootSpawn_F{floor.Index}_GrapplingHook_{HollowTitanLandmarkSpawnCatalog.GrapplingHookLootId}");
            hookGo.transform.SetParent(lootRoot, false);
            hookGo.transform.localPosition = hookPos;

            var marker = hookGo.AddComponent<HollowTitanLandmarkSpawnPoint>();
            marker.Configure(
                HollowTitanSpawnKind.Loot,
                floor.Index,
                HollowTitanLandmarkSpawnCatalog.GrapplingHookLootId,
                1);
            AddCapsulePreview(hookGo, new Color(0.1f, 0.75f, 0.95f, 0.65f), elemScale);
        }

        static void RunEnemyPatrolNodes(BuildSession session)
        {
            var patrolRoot = GetOrCreateChild(GetOrCreateChild(session.Root, "Spawns"), "Patrol");
            ClearChildren(patrolRoot);

            if (session.FloorPlan == null)
                return;

            var elemScale = MassiveElementScale(session);
            var rng = new System.Random(session.Request.Seed + 0x50415452); // "PATR"
            foreach (var floor in session.FloorPlan.Floors)
            {
                for (var p = 0; p < HollowTitanLandmarkSpawnManifest.PatrolNodesPerFloor; p++)
                {
                    var angle = p * (360f / HollowTitanLandmarkSpawnManifest.PatrolNodesPerFloor) +
                                floor.Index * 25f;
                    var rad = angle * Mathf.Deg2Rad;
                    var r = floor.Radius * 0.75f;

                    var nodeGo = new GameObject($"Patrol_F{floor.Index}_N{p}");
                    nodeGo.transform.SetParent(patrolRoot, false);
                    nodeGo.transform.localPosition = new Vector3(
                        Mathf.Sin(rad) * r,
                        floor.LocalY + 0.4f * elemScale,
                        Mathf.Cos(rad) * r);

                    var marker = nodeGo.AddComponent<HollowTitanLandmarkSpawnPoint>();
                    marker.Configure(HollowTitanSpawnKind.Patrol, floor.Index, "PatrolNode", 1);
                    AddCapsulePreview(nodeGo, new Color(0.2f, 0.35f, 0.65f, 0.35f), elemScale);
                }
            }

            Debug.Log(
                $"[Surface] Hollow Titan landmark on '{session.SiteTile?.name}' @ {session.Root.position} — " +
                $"seed {session.Request.Seed}, {session.FloorPlan.FloorCount} floors, phased build.",
                session.Root.gameObject);
        }

        static void EnsurePortal(BuildSession session)
        {
            var portal = session.Root.Find("BossStagePortal");
            if (portal == null)
            {
                var portalGo = new GameObject("BossStagePortal");
                portalGo.transform.SetParent(session.Root, false);
                portal = portalGo.transform;
            }

            WorldProjectTagSetup.TrySetTag(portal.gameObject, HollowTitanLandmarkAuthor.PortalTag);
            portal.localPosition = session.FloorPlan != null
                ? session.FloorPlan.EntranceLocal + session.FloorPlan.EntranceForward * 0.8f
                : new Vector3(0f, 2f, 0f);

            var col = portal.GetComponent<BoxCollider>();
            if (col == null)
                col = portal.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = session.Massive
                ? new Vector3(14f, 12f, 14f) * MassiveElementScale(session)
                : new Vector3(6f, 5f, 6f);

            if (portal.GetComponent<WorldBossStagePortal>() == null)
                portal.gameObject.AddComponent<WorldBossStagePortal>();
        }

        static void EnsureLandmarkSpawner(BuildSession session)
        {
            var spawner = session.Root.GetComponent<HollowTitanLandmarkSpawner>();
            if (spawner == null)
                spawner = session.Root.gameObject.AddComponent<HollowTitanLandmarkSpawner>();

            WorldRuntimeResourcesAuthor.EnsureHollowTitanLandmarkPrefabsInResources();
            var guardPrefab = HollowTitanLandmarkSpawnCatalog.LoadEnemyPrefab(
                    HollowTitanLandmarkSpawnCatalog.DefaultEnemyRoleId)
                ?? Cc0ContentImportUtility.LoadCharacterPrefab(
                    HollowTitanLandmarkSpawnCatalog.DefaultEnemyCc0Slot)
                ?? CaveCombatSetupUtility.EnsureEnemyPrefab();
            spawner.enemyPrefab = guardPrefab;
            spawner.spawnSeed = session.Request.Seed + 0x5350574E;
            spawner.spawnOnStart = true;
        }

        static Transform GetOrCreateChild(Transform parent, string path)
        {
            var parts = path.Split('/');
            var current = parent;
            foreach (var part in parts)
            {
                var child = current.Find(part);
                if (child == null)
                {
                    var go = new GameObject(part);
                    go.transform.SetParent(current, false);
                    child = go.transform;
                }

                current = child;
            }

            return current;
        }

        static void ClearChildren(Transform parent)
        {
            if (parent == null)
                return;

            if (PipelineContentPreservePolicy.PreferMergeChildren(parent, "Hollow Titan interior/exterior"))
                return;

            for (var i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        static void ClearChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
                return;

            if (PipelineContentPreservePolicy.HasMeaningfulContent(child))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[PipelineIntegrate] Keeping Hollow Titan '{childName}' — merge in place.",
                    forceUnityConsole: false);
                return;
            }

            UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        static Shader ResolveTintShader() =>
            Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Legacy Shaders/Diffuse");

        static void TintPrimitive(GameObject go, Color color)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
                return;

            var shader = ResolveTintShader();
            if (shader == null)
            {
                Debug.LogWarning($"[HollowTitan] No tint shader found for '{go.name}' — bark may appear black.");
                return;
            }

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            else
                mat.color = color;
            mr.sharedMaterial = mat;
        }

        static void AddCapsulePreview(GameObject host, Color color, float scale = 1f)
        {
            if (CaveBuildCursorSettings.LoadOrCreate().hollowTitanPhasedBuild)
                return;
            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cap.name = "EditorPreview";
            cap.transform.SetParent(host.transform, false);
            cap.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f) * scale;
            TintPrimitive(cap, color);
            var col = cap.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.DestroyImmediate(col);
        }
    }
}
#endif
