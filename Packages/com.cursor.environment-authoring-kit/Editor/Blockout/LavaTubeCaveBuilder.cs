using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.XR;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using EnvironmentRoot = EnvironmentAuthoringKit.EnvironmentRoot;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static partial class LavaTubeCaveBuilder
    {
        const string PrefRandomize = "CaveBuild_RandomizeEachTime";
        const string PrefFixedSeed = "CaveBuild_FixedSeed";
        const string PrefSurfaceScope = "CaveBuild_SurfaceScope";

        public static SurfaceBuildScope LastSurfaceScope
        {
            get => (SurfaceBuildScope)EditorPrefs.GetInt(PrefSurfaceScope, (int)SurfaceBuildScope.FullWorld);
            set => EditorPrefs.SetInt(PrefSurfaceScope, (int)value);
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Build Layout Prototype (Interview)", false, 0)]
        public static void BuildCaveLayoutPrototypeActiveScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Cave Layout Prototype",
                    "Builds ONLY the fun gameplay layout:\n\n" +
                    "• Flat walk floor (one height — no horizontal shelf layers)\n" +
                    "• Path / jump / finish markers (see Scene gizmos)\n" +
                    "• Mob spawns + portal + spline path\n" +
                    "• NO block tunnel, NO ceiling meshes, NO onion shells\n" +
                    "• Levels scene Terrain under the route (if Terrain exists)\n\n" +
                    "Sculpt walls/ceiling in Terrain tools. Blueprint: CaveLayoutBlueprint.json",
                    "Build Layout",
                    "Cancel"))
                return;

            BuildInActiveScene(openMainSceneFirst: false, hideLegacyBlockout: true, layoutPrototype: true);
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Run Batch Cave Builds", false, 2)]
        public static void RunBatchCaveBuildsActiveScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Batch Cave Builds",
                    "Runs multiple complete queued cave builds with auto-incrementing seeds.\n\n" +
                    "Configure job count and delay on CaveBuildCursorSettings (enableBatchMode optional).\n" +
                    "Log: Assets/EnvironmentKit/Generated/CaveBuildBatchLog.json",
                    "Run Batch",
                    "Cancel"))
                return;

            RunBatchInActiveScene();
        }

        [MenuItem(CaveBuildMenuPaths.BuildComplete, false, 0)]
        public static void BuildCompleteCaveActiveScene() =>
            TryConfirmAndStartBuild(openMainSceneFirst: false, SurfaceBuildScope.FullWorld);

        /// <summary>
        /// Hub restart / manual resume when planner JSON is finalized but the paced pipeline never started.
        /// Delegates to <see cref="BuildCompleteCaveActiveScene"/> via the wizard gate immediate-start path.
        /// </summary>
        public static bool TryStartFullWorldFromPlannerFinalize()
        {
            if (IsBuildInProgress || CaveBuildHubSessionReconcile.IsCoreBuildRunning())
                return false;

            if (!CaveBuildSessionConfig.HasApprovedPlannerSession())
                return false;

            return TryConfirmAndStartBuild(openMainSceneFirst: false, SurfaceBuildScope.FullWorld);
        }

        /// <summary>Full-world build after wizard pipeline tabs 1–6 — skips browser gate.</summary>
        public static bool TryStartFromWizardPipelineApply(out string message)
        {
            message = string.Empty;
            if (IsBuildInProgress || CaveBuildHubSessionReconcile.IsCoreBuildRunning())
            {
                message = "Build already in progress.";
                return false;
            }

            if (!CaveBuildSessionConfig.HasApprovedPlannerSession())
            {
                message = "Terrain plan not finalized — approve the Terrain tab first.";
                return false;
            }

            CaveBuildPauseController.ClearOnNewBuildSession();
            if (!SamplePresetsExist())
                SamplePresetsCreator.CreateAll();

            EnvironmentKitSceneSafeguards.GuardActiveSceneForBuild();
            ClearInvalidStoredGround();
            LastSurfaceScope = SurfaceBuildScope.FullWorld;
            var ground = ResolveGroundForBuild(LoadUserGround(), SurfaceBuildScope.FullWorld);
            if (!ground.HasAnchor)
            {
                message = "No ground anchor — tag SurfaceTerrainMain as Ground.";
                return false;
            }

            if (!CaveBuildSessionConfig.ReloadAndApplyPlannerSession(out var active) || active == null)
            {
                message = "Could not load approved terrain session config.";
                return false;
            }

            CaveBuildSessionConfig.PrepareFreshBuild(active);
            var portalName = CaveBuildPortalSettings.PortalForBuild != null
                ? CaveBuildPortalSettings.PortalForBuild.name
                : "(auto-detect PortalFive)";
            EnvironmentKitHubWindow.EnsureOpenForBuild();
            Debug.Log(
                "[CaveBuild] Wizard pipeline apply — starting terrain build for " +
                $"{active.label}, {CaveBuildSessionConfig.DescribeActiveTilePlan()}.");
            StartFullWorldBuildAfterWizard(ground, portalName, openMainSceneFirst: false);
            message = "Terrain build started.";
            return true;
        }

        [MenuItem(CaveBuildMenuPaths.BuildSurfaceOnly, false, 1)]
        public static void BuildSurfaceWorldOnlyActiveScene() =>
            TryConfirmAndStartBuild(openMainSceneFirst: false, SurfaceBuildScope.SurfaceOnly);

        [MenuItem(CaveBuildMenuPaths.SnapSurfaceTerrainGrid, false, 45)]
        public static void SnapSurfaceTerrainGridActiveScene()
        {
            var main = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (main == null)
            {
                EditorUtility.DisplayDialog(
                    "Snap surface terrain grid",
                    "No SurfaceTerrainMain found in the active scene.",
                    "OK");
                return;
            }

            SurfaceTerrainTileExpansion.SnapFullWorldTerrainGridNow(main);
            EditorUtility.DisplayDialog(
                "Snap surface terrain grid",
                "All surface terrain slots (81-tile core or ~289 extended grid) were snapped to the grid anchor (edge-to-edge).\n\n" +
                "Re-run layout audit or continue the surface build.",
                "OK");
        }

        [MenuItem(CaveBuildMenuPaths.ExpandOpenWorldTerrainRing, false, 46)]
        public static void ExpandOpenWorldTerrainRingActiveScene()
        {
            var main = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (main == null)
            {
                EditorUtility.DisplayDialog(
                    "Open world terrain expansion",
                    "No SurfaceTerrainMain found in the active scene.",
                    "OK");
                return;
            }

            var manifest = SurfaceOpenWorldGridExpansion.LoadManifest();
            if (manifest.builtChebyshevRadius >= manifest.targetChebyshevRadius)
            {
                EditorUtility.DisplayDialog(
                    "Open world terrain expansion",
                    $"Grid already at ring {manifest.builtChebyshevRadius} (target {manifest.targetChebyshevRadius}, " +
                    $"~{manifest.placedTileCount} tiles).",
                    "OK");
                return;
            }

            SurfaceOpenWorldGridExpansion.QueueExpandOpenWorldNextRing(main, () =>
            {
                var updated = SurfaceOpenWorldGridExpansion.LoadManifest();
                EditorUtility.DisplayDialog(
                    "Open world terrain expansion",
                    $"Ring {updated.builtChebyshevRadius} placed (~{updated.placedTileCount} tiles total).\n\n" +
                    "Run again to add the next ring toward ~289 tiles.",
                    "OK");
            });
        }

        [MenuItem(CaveBuildMenuPaths.BuildCaveOnly, false, 2)]
        public static void BuildCaveOnlyActiveScene() =>
            TryConfirmAndStartBuild(openMainSceneFirst: false, SurfaceBuildScope.CaveOnly);

        [MenuItem(CaveBuildMenuPaths.MacBookAirBudget, false, 4)]
        public static void ApplyMacBookAirHardwareBudgetMenu()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            EnvironmentKitHardwareBudget.ApplyMacBookAirPresetToSettings(settings);
            EditorUtility.DisplayDialog(
                "MacBook Air GPU budget",
                "Laptop-friendly settings enabled (see CaveBuildCursorSettings).\n\n" +
                "Lower GPU RAM: smaller terrain, textures, no reflection probes.\n" +
                "Gaussian splats disabled.\n" +
                "Slightly more CPU: paced steps + asset unload between steps.",
                "OK");
        }

        [MenuItem(CaveBuildMenuPaths.DemoRecordingPreset, false, 5)]
        public static void ApplyDemoRecordingPresetMenu()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            EnvironmentKitHardwareBudget.ApplyDemoRecordingPreset(settings);
            EditorUtility.DisplayDialog(
                "Laptop GPU budget preset",
                "Enabled for MacBook Air / 16GB unified memory:\n\n" +
                "• 257 heightmap (not 513) — much less RAM + GPU\n" +
                "• Terrain meat grades one rung per frame (no 60s hitch)\n" +
                "• No sync NavMesh during grading\n" +
                "• Unload unused assets between steps\n\n" +
                "If you see \"Resource ID out of range in SetResource\": lower terrain tile count or use this preset on a fresh build.",
                "OK");
        }

        [MenuItem(CaveBuildMenuPaths.DemoRecordingPreset, true)]
        static bool ApplyDemoRecordingPresetMenuValidate() =>
            CaveBuildDemoAutoRecorder.HubBuildRecordingOptIn;

        [MenuItem(CaveBuildMenuPaths.RebuildMainScene, false, 3)]
        public static void RebuildCompleteCaveMainScene() =>
            TryConfirmAndStartBuild(openMainSceneFirst: true, SurfaceBuildScope.FullWorld);

        [MenuItem(CaveBuildMenuPaths.Advanced + "Build Complete Cave — Full AAA Rebuild (invalidate ladder)", false, 1)]
        public static void BuildCompleteCaveFullAaaRebuild()
        {
            CaveBuildAaaSessionPolicy.BeginFullAaaRebuild(CaveBuildDemoAutoRecorder.HubBuildRecordingOptIn);
            CaveBuildPauseController.ClearOnNewBuildSession();
            if (!SamplePresetsExist())
                SamplePresetsCreator.CreateAll();
            ClearInvalidStoredGround();
            LastSurfaceScope = SurfaceBuildScope.FullWorld;
            var ground = ResolveGroundForBuild(LoadUserGround(), SurfaceBuildScope.FullWorld);
            if (!ground.HasAnchor)
            {
                CaveBuildCompletionSummary.ShowBlocked(
                    "No ground found. Tag SurfaceTerrainMain as Ground — not Environment/Grid blockout.");
                return;
            }
            _unifiedBuildRoll = CreateLayoutRoll();
            if (!CaveBuildAutomatedFullWorldBootstrap.Prepare(
                    ground,
                    _unifiedBuildRoll.Seed,
                    invalidateEntireLadder: true,
                    out _))
                return;

            var portalName = CaveBuildPortalSettings.PortalForBuild != null
                ? CaveBuildPortalSettings.PortalForBuild.name
                : "(auto-detect PortalFive)";
            EnvironmentKitHubWindow.EnsureOpenForBuild();
            if (!CaveBuildCompletionSummary.ConfirmStartBuild(
                    ground, portalName, _unifiedBuildRoll, SurfaceBuildScope.FullWorld, out _))
                return;

            CaveBuildDemoAutoRecorder.OnHubBuildStarting("FullWorld AAA rebuild");
            OverrideContentTier = WorldBuildContentTier.Aaa;
            BuildInActiveScene(
                openMainSceneFirst: false,
                hideLegacyBlockout: true,
                skipDialogs: true,
                surfaceScope: SurfaceBuildScope.FullWorld);
        }

        static bool TryConfirmAndStartBuild(bool openMainSceneFirst, SurfaceBuildScope scope)
        {
            if (CaveBuildPauseController.UserPauseActive)
            {
                var hasTerrain = SurfaceTerrainTileExpansion.FindMainTerrainInScene() != null;
                if (hasTerrain)
                {
                    var choice = EditorUtility.DisplayDialogComplex(
                        "Build already paused",
                        "A build is paused with terrain in the scene.\n\n" +
                        "• Continue build — resume the queued pipeline (keeps terrain).\n" +
                        "• Start new build — clears pause and may purge/rebuild terrain.\n\n" +
                        "Use Continue build after a playtest break unless you intend to restart.",
                        "Continue build",
                        "Cancel",
                        "Start new build");
                    if (choice == 0)
                    {
                        CaveBuildPauseController.Continue();
                        return false;
                    }

                    if (choice == 1)
                        return false;
                }
            }

            CaveBuildPauseController.ClearOnNewBuildSession();
            if (!SamplePresetsExist())
                SamplePresetsCreator.CreateAll();

            EnvironmentKitSceneSafeguards.GuardActiveSceneForBuild();

            ClearInvalidStoredGround();
            LastSurfaceScope = scope;
            var ground = ResolveGroundForBuild(LoadUserGround(), scope);
            if (!ground.HasAnchor)
            {
                CaveBuildCompletionSummary.ShowBlocked(
                    scope == SurfaceBuildScope.FullWorld
                        ? "No ground found. Tag SurfaceTerrainMain as Ground — not Environment/Grid blockout."
                        : "No ground found. Tag walkable floor as 'Ground' or assign in Environment Kit.");
                return false;
            }

            var portalName = CaveBuildPortalSettings.PortalForBuild != null
                ? CaveBuildPortalSettings.PortalForBuild.name
                : "(auto-detect PortalFive)";
            if (scope == SurfaceBuildScope.FullWorld)
            {
                CaveBuildWizardGate.RequestFullWorldBuild(() =>
                    StartFullWorldBuildAfterWizard(ground, portalName, openMainSceneFirst));
                return true;
            }

            return StartScopedBuildAfterConfirm(ground, portalName, openMainSceneFirst, scope);
        }

        static void StartFullWorldBuildAfterWizard(SceneGroundInfo ground, string portalName, bool openMainSceneFirst)
        {
            _unifiedBuildRoll = CreateLayoutRoll();

            if (!CaveBuildAutomatedFullWorldBootstrap.Prepare(
                    ground,
                    _unifiedBuildRoll.Seed,
                    invalidateEntireLadder: false,
                    out _))
                return;

            EnvironmentKitHubWindow.EnsureOpenForBuild();
            if (!CaveBuildCompletionSummary.ConfirmStartBuild(
                    ground, portalName, _unifiedBuildRoll, SurfaceBuildScope.FullWorld, out _))
                return;

            if (CaveBuildSessionConfig.HasFinalizedActive && CaveBuildSessionConfig.IsFastDemo())
            {
                Debug.Log(
                    "[CaveBuild] Fast demo session — " + CaveBuildSessionConfig.Active.tileCount +
                    " tiles, fresh seed " + _unifiedBuildRoll.Seed +
                    ", props=" + (CaveBuildSessionConfig.Active.playDiskProps ? "on" : "off") + ".");
            }

            CaveBuildDemoAutoRecorder.OnHubBuildStarting(SurfaceBuildScope.FullWorld.ToString());
            BuildInActiveScene(
                openMainSceneFirst: openMainSceneFirst,
                hideLegacyBlockout: true,
                skipDialogs: true,
                surfaceScope: SurfaceBuildScope.FullWorld);
        }

        static bool StartScopedBuildAfterConfirm(
            SceneGroundInfo ground,
            string portalName,
            bool openMainSceneFirst,
            SurfaceBuildScope scope)
        {
            _unifiedBuildRoll = CreateLayoutRoll();

            EnvironmentKitHubWindow.EnsureOpenForBuild();
            if (!CaveBuildCompletionSummary.ConfirmStartBuild(ground, portalName, _unifiedBuildRoll, scope, out _))
                return false;

            CaveBuildDemoAutoRecorder.OnHubBuildStarting(scope.ToString());
            BuildInActiveScene(
                openMainSceneFirst: openMainSceneFirst,
                hideLegacyBlockout: true,
                skipDialogs: true,
                surfaceScope: scope);
            return true;
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Restore Sunny Surface Lighting")]
        public static void RestoreSunnySurfaceLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.62f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.22f);
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = false;
            EditorUtility.DisplayDialog(
                "Surface Lighting",
                "Restored sunny-day ambient and disabled global fog.",
                "OK");
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Toggle Random Seed Each Build")]
        public static void ToggleRandomSeed()
        {
            var next = !EditorPrefs.GetBool(PrefRandomize, true);
            EditorPrefs.SetBool(PrefRandomize, next);
            var note = next
                ? "Each Build Complete Cave gets a new seed + random segments/chambers."
                : $"Builds repeat until you set EditorPrefs {PrefFixedSeed} or turn random back on.";
            Debug.Log($"[CaveBuild] Random seed each build: {next}. {note}");
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Run Pre-Build Gate Only")]
        public static void RunPreBuildGateOnly()
        {
            if (!SamplePresetsExist())
                SamplePresetsCreator.CreateAll();

            ClearInvalidStoredGround();
            var ground = SceneGroundResolver.Resolve(LoadUserGround());
            if (!ground.HasAnchor)
            {
                EditorUtility.DisplayDialog(
                    "Pre-Build Gate",
                    "No ground found. Tag walkable floor as 'Ground' or assign in Environment Kit.",
                    "OK");
                return;
            }

            var roll = CreateLayoutRoll();
            var request = new WorldGenerationRequest
            {
                Biome = BiomeId.Cave,
                CaveMode = CaveGenerationMode.FullSystem,
                UseLayoutPrototype = false,
                UseSplineMesh = true,
                UseTrue3DCaveSystem = true,
                UseBlockTunnel = true,
                UseTerrainCarve = true,
                AllowCreateTerrain = false,
                IncludeCaveWater = false
            };
            roll.ApplyTo(request);

            var report = CaveBuildPreBuildLadder.Run(ground, request, layoutPrototype: false, roll.Seed);
            var msg =
                $"Pre-build grade: {report.LetterGrade} ({report.OverallScore}/100) " +
                $"{(report.BuildAcceptable ? "PASS — safe to build cave" : "BLOCKED — fix readiness first")}\n\n" +
                $"Report: {CaveBuildPreBuildLadder.ReportPath}";

            EditorUtility.DisplayDialog(
                report.BuildAcceptable ? "Pre-Build Gate — PASS" : "Pre-Build Gate — BLOCKED",
                msg,
                "OK");
            Debug.Log("[CaveBuild] " + msg);

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!report.BuildAcceptable && settings.autoInvokePreBuildWorkflow)
                CaveBuildPreBuildWorkflow.Begin(report, ground);
        }

        public static void BuildFromMenu() => BuildInActiveScene(false, false, skipDialogs: true);

        public static void RebuildCompleteCaveBatch()
        {
            BuildInActiveScene(openMainSceneFirst: true, hideLegacyBlockout: true, skipDialogs: true);
            EditorApplication.Exit(0);
        }

        static void RunBatchInActiveScene()
        {
            if (_buildInProgress || LavaTubeCaveBuildPipeline.IsPhasedBuildActive || CaveBuildBatchRunner.IsActive)
            {
                Debug.LogWarning("[CaveBuild] Build or batch already in progress.");
                return;
            }

            _buildInProgress = true;
            try
            {
                if (!SamplePresetsExist())
                    SamplePresetsCreator.CreateAll();

                ClearInvalidStoredGround();
                var ground = SceneGroundResolver.Resolve(LoadUserGround());
                if (!ground.HasAnchor)
                {
                    EditorUtility.DisplayDialog(
                        "Batch Cave Builds",
                        "No ground found. Tag walkable floor as 'Ground' or assign in Environment Kit.",
                        "OK");
                    return;
                }

                var rollSnapshot = CreateLayoutRoll();
                var request = new WorldGenerationRequest
                {
                    Biome = BiomeId.Cave,
                    CaveMode = CaveGenerationMode.FullSystem,
                    UseSplineMesh = true,
                    UseTrue3DCaveSystem = true,
                    UseBlockTunnel = true,
                    UseTerrainCarve = true,
                    IncludeCaveWater = false,
                };
                rollSnapshot.ApplyTo(request);

                if (!CaveBuildUnifiedFlow.TryRunPreBuildPhase(
                        ground,
                        request,
                        rollSnapshot.Seed,
                        layoutPrototype: false,
                        skipPreBuild: true,
                        skipDialogs: true,
                        hideLegacyBlockout: true,
                        out _))
                {
                    _buildInProgress = false;
                    return;
                }

                var xr = ResolveBuildXrProfile();
                var groundAnchor = ground.Anchor;
                var sceneName = SceneManager.GetActiveScene().name;

                LavaTubeMaterialUpgrader.EnsurePackMaterialsUpgraded();
                var oldCave = groundAnchor.Find(CaveGeometryPaths.CaveSystemRootName);
                if (oldCave == null)
                    oldCave = groundAnchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (oldCave != null)
                    CaveEditorUndo.DestroyImmediate(oldCave.gameObject);
                HideLegacyBlockoutCaves();

                CaveBuildBatchRunner.StartFromSettings(
                    groundAnchor,
                    ground,
                    request,
                    xr,
                    hideLegacy: true,
                    skipDialogs: true,
                    summary =>
                    {
                        var last = summary.Entries.Count > 0 ? summary.Entries[summary.Entries.Count - 1] : null;
                        var report = new LavaTubeCaveBuildReport
                        {
                            QualityScore = last?.QualityScore ?? 0,
                            QualityLetter = last?.QualityLetter ?? "?",
                            QualityAcceptable = last?.QualityAcceptable ?? false,
                            Message =
                                $"Batch — {summary.JobsPassed}/{summary.JobsRun} passed. " +
                                (last?.Message ?? string.Empty),
                        };
                        PresentBuildResult(sceneName, report, rollSnapshot, skipDialogs: true);
                        _buildInProgress = false;
                    });
            }
            catch
            {
                _buildInProgress = false;
                throw;
            }
        }

        static bool _buildInProgress;
        static CaveLayoutRoll _unifiedBuildRoll;

        /// <summary>Set before <see cref="BuildCompleteCaveFullAaaRebuild"/> — applied to the next build request.</summary>
        internal static WorldBuildContentTier? OverrideContentTier;
        static int _assetEditingNest;
        static bool _buildSessionOpen;

        struct PendingContinueCaveGeometry
        {
            public string SceneName;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public bool HideLegacyBlockout;
            public bool SkipDialogs;
            public CaveLayoutRoll Roll;
        }

        static PendingContinueCaveGeometry? _pendingContinueCave;

        public static bool IsBuildInProgress => _buildInProgress;

        internal static void ReleaseBuildLock() => _buildInProgress = false;

        internal static void ArmResumeBuildLock()
        {
            if (_buildInProgress)
                return;

            CaveBuildPipelineCompletion.OnUserStartedBuild();
            var buildSettings = CaveBuildCursorSettings.LoadOrCreate();
            buildSettings.LoadFromPrefs();
            CaveBuildEditorResponsiveness.ApplyForActiveBuild(buildSettings);
            EnvironmentKitHardwareBudget.BeginEditorSession();
            _buildInProgress = true;
        }

        /// <summary>Resume after playtest break — does not restart ground lay from batch 1 when research was in progress.</summary>
        public static bool TryResumeFromPacedCheckpoint(
            CaveBuildPacedStepPersistence.PacedStepSnapshot doc,
            out string message)
        {
            message = string.Empty;
            if (doc == null)
            {
                message = "Checkpoint document is null.";
                return false;
            }

            if (_buildInProgress || CaveBuildStartupCoordinator.IsActive)
            {
                message = "Build already in progress.";
                return false;
            }

            CaveBuildActionPacing.EmergencyAbortAll();
            EditorUtility.ClearProgressBar();

            CaveBuildPlaytestSessionSnapshot.TryLoad(out var snap);
            snap ??= new CaveBuildPlaytestSessionSnapshot.SnapshotDoc();
            if (snap.pacedStep <= 0)
                snap.pacedStep = doc.pacedStep;
            if (snap.seed == 0)
                snap.seed = doc.seed;
            if (snap.conceptIndex < 0)
                snap.conceptIndex = doc.conceptIndex;
            if (snap.terrainCount <= 0)
                snap.terrainCount = doc.terrainCount;

            if (snap.conceptIndex >= 0)
                FullWorldGenerationStylePreset.SaveSelectedIndex(snap.conceptIndex);

            ClearInvalidStoredGround();
            LastSurfaceScope = SurfaceBuildScope.FullWorld;
            var ground = ResolveGroundForBuild(LoadUserGround(), SurfaceBuildScope.FullWorld);
            if (!ground.HasAnchor)
            {
                message = "No ground anchor — tag SurfaceTerrainMain as Ground.";
                return false;
            }

            var seed = snap.seed != 0 ? snap.seed : doc.seed;
            if (seed == 0)
                seed = CreateLayoutRoll().Seed;

            _unifiedBuildRoll = CaveLayoutRoll.CreateRandom(seed);
            if (!CaveBuildAutomatedFullWorldBootstrap.Prepare(
                    ground,
                    _unifiedBuildRoll.Seed,
                    invalidateEntireLadder: false,
                    out message))
                return false;

            EnvironmentKitHubWindow.EnsureOpenForBuild();
            EnvironmentKitSceneSafeguards.GuardActiveSceneForBuild();
            CaveBuildStepCounter.RestoreSession(snap.pacedStep > 0 ? snap.pacedStep : doc.pacedStep);
            CaveBuildDemoAutoRecorder.OnHubBuildStarting("playtest resume");
            ArmResumeBuildLock();

            if (CaveBuildFullWorldGridCheckpoint.TryResumeGridCheckpointSilent())
            {
                message = $"Resumed FullWorld grid (paced step {snap.pacedStep}).";
                return true;
            }

            if (snap.researchSubStep >= 0 &&
                snap.researchSubStep < CaveBuildPrePlacementResearch.QueuedResearchStepCount &&
                snap.terrainCount < 9)
            {
                CaveBuildStartupCoordinator.QueueResumeAtResearch(
                    ground,
                    _unifiedBuildRoll,
                    snap.researchSubStep,
                    deferRelease => { if (!deferRelease) _buildInProgress = false; });
                message =
                    $"Resuming research step {snap.researchSubStep + 1}/" +
                    $"{CaveBuildPrePlacementResearch.QueuedResearchStepCount} (paced {snap.pacedStep}).";
                return true;
            }

            CaveBuildStartupCoordinator.QueueResumeAtSurfacePipeline(
                ground,
                _unifiedBuildRoll,
                deferRelease => { if (!deferRelease) _buildInProgress = false; });
            message = $"Resuming surface pipeline (paced step {snap.pacedStep}) — keeps terrain.";
            return true;
        }

        /// <param name="skipPreBuildGate">Internal auto-rebuild after Cursor — skips readiness block.</param>
        public static void BuildInActiveScene(
            bool openMainSceneFirst = false,
            bool hideLegacyBlockout = false,
            bool skipDialogs = false,
            bool layoutPrototype = false,
            bool skipPreBuildGate = false,
            SurfaceBuildScope? surfaceScope = null)
        {
            if (_buildInProgress)
            {
                Debug.LogWarning(
                    "[CaveBuild] Build already in progress — ignoring duplicate call (prevents editor freeze).");
                return;
            }

            CaveBuildPipelineCompletion.OnUserStartedBuild();
            CaveBuildPersistedSessionReset.ClearForNewBuild();
            CaveBuildSurfaceCompletionGate.ResetForNewBuildSession();
            CaveTerrainPipelineOrchestrator.ResetWaitState();
            var buildSettings = CaveBuildCursorSettings.LoadOrCreate();
            buildSettings.LoadFromPrefs();
            CaveBuildEditorResponsiveness.ApplyForActiveBuild(buildSettings);
            buildSettings.SaveToPrefs();
            EnvironmentKitHardwareBudget.BeginEditorSession();
            EnvironmentKitSceneSafeguards.GuardActiveSceneForBuild();
            _buildInProgress = true;
            CaveBuildStartupCoordinator.QueueBuild(
                openMainSceneFirst,
                hideLegacyBlockout,
                skipDialogs,
                layoutPrototype,
                skipPreBuildGate,
                surfaceScope ?? LastSurfaceScope,
                deferRelease =>
                {
                    if (!deferRelease)
                        _buildInProgress = false;
                });
        }

        /// <summary>
        /// Resumes cave geometry only (rung 6+) after surface prep and Cursor pre-build already completed.
        /// Does not re-run surface pipeline, research export, or pre-build gate.
        /// </summary>
        public static void ContinueCaveGeometryAfterPreBuild(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool hideLegacyBlockout,
            bool skipDialogs,
            CaveLayoutRoll layoutRoll = null)
        {
            if (_buildInProgress)
            {
                // Startup sets this true and defers release while Cursor pre-build runs.
                // This call is the second half of the same Build Complete Cave session.
                CaveBuildEditorLog.LogCave(
                    "Resuming deferred cave geometry (same build session after pre-build).",
                    forceUnityConsole: true);
                _buildInProgress = false;
            }

            CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                request,
                ground,
                CaveBuildSurfaceCompletionGate.IsSurfaceWorldGeneratorFinished ||
                CaveBuildSurfaceCompletionGate.IsCompleteForSeed(request));

            if (!ground.HasAnchor)
            {
                if (!skipDialogs)
                    CaveBuildCompletionSummary.ShowBlocked(
                        "No ground anchor for continue-after-pre-build.");
                return;
            }

            var sceneName = SceneManager.GetActiveScene().name;
            CaveBuildPipelineCompletion.OnUserStartedBuild();
            EnvironmentKitHardwareBudget.BeginEditorSession();
            _buildInProgress = true;

            var isAlignOnly = request != null && request.SurfaceScope == SurfaceBuildScope.CaveOnly;
            if (isAlignOnly)
                CaveBuildPipelineScope.BeginCaveOnlyContinuation();
            else
                CaveBuildPipelineScope.BeginFullPipeline();

            var roll = layoutRoll ?? _unifiedBuildRoll ?? CreateLayoutRoll();
            _unifiedBuildRoll = null;
            LogLayoutRoll(roll, caveOnly: isAlignOnly);
            CaveBuildAaaProductionBootstrap.OnPreBuildGatePassed(request.Seed);

            _pendingContinueCave = new PendingContinueCaveGeometry
            {
                SceneName = sceneName,
                Ground = ground,
                Request = request,
                HideLegacyBlockout = hideLegacyBlockout,
                SkipDialogs = skipDialogs,
                Roll = roll,
            };

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                "[Cave] Continue after pre-build — queuing cave pipeline shell…",
                0.04f);
            var scopeLabel = request?.SurfaceScope == SurfaceBuildScope.FullWorld
                ? "FullWorld (surface/terrain done — cave geo aligns to Ground)"
                : request?.SurfaceScope == SurfaceBuildScope.SurfaceOnly
                    ? "SurfaceOnly handoff"
                    : "CaveOnly align-to-surface";
            CaveBuildEditorLog.LogCave(
                $"Continue after pre-build ({scopeLabel}) — cave pipeline next editor tick.",
                forceUnityConsole: true);

            CaveBuildActionPacing.SchedulePipelineFirstStep(
                RunPendingContinueCaveGeometry,
                CaveBuildPipelineDomains.QueueLabel("continue cave geometry"),
                CaveBuildActionPacing.ActionWeight.Light);

        }

        static void RunPendingContinueCaveGeometry()
        {
            if (!_pendingContinueCave.HasValue)
                return;

            var pending = _pendingContinueCave.Value;
            _pendingContinueCave = null;

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    "[Cave] Preparing cave session (materials)…",
                    0.08f);
                LavaTubeMaterialUpgrader.EnsurePackMaterialsUpgraded();
                CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                    RunPendingContinueCaveGeometryPrep(pending));
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                _buildInProgress = false;
                CaveBuildPipelineScope.Clear();
                EditorUtility.ClearProgressBar();
            }
        }

        static void RunPendingContinueCaveGeometryPrep(PendingContinueCaveGeometry pending)
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    "[Cave] Preparing cave session (undo, cleanup)…",
                    0.09f);

                if (pending.Request == null ||
                    pending.Request.SurfaceScope != SurfaceBuildScope.CaveOnly)
                    CaveBuildPipelineScope.BeginFullPipeline();
                else if (!CaveBuildPipelineScope.CaveOnlyContinuation)
                    CaveBuildPipelineScope.BeginCaveOnlyContinuation();

                Undo.IncrementCurrentGroup();
                var undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Build Complete Cave Level");

                BeginBuildAssetEditing();
                CaveBuildAgentArtifacts.ResetForNewBuildSession(pending.SceneName);
                CaveEditorUndo.BeginBulkBuild();
                _buildSessionOpen = true;

                var groundAnchor = pending.Ground.Anchor;
                var oldCave = groundAnchor.Find(CaveGeometryPaths.CaveSystemRootName);
                if (oldCave == null)
                    oldCave = groundAnchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (oldCave != null && pending.Request.SurfaceScope != SurfaceBuildScope.SurfaceOnly)
                {
                    CaveBuildPhaseContractRegistry.InvalidateCaveGeometryLadderRungs();
                    CaveEditorUndo.DestroyImmediate(oldCave.gameObject);
                }

                CleanupStrayCaveSystems(oldCave);
                if (pending.HideLegacyBlockout)
                    HideLegacyBlockoutCaves();

                CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                    RunPendingContinueCaveGeometryQueueRun(pending, undoGroup));
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                _buildInProgress = false;
                CaveBuildPipelineScope.Clear();
                EditorUtility.ClearProgressBar();
            }
        }

        static void RunPendingContinueCaveGeometryQueueRun(
            PendingContinueCaveGeometry pending,
            int undoGroup)
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    "[Cave] Queuing cave pipeline…",
                    0.1f);

                var cursorSettings = CaveBuildCursorSettings.LoadOrCreate();
                cursorSettings.LoadFromPrefs();
                if (!cursorSettings.usePhasedCaveBuild)
                {
                    CaveBuildEditorLog.LogCaveWarning(
                        "Phased cave build disabled in settings — enable usePhasedCaveBuild.");
                }

                var xr = ResolveBuildXrProfile();
                var rollSnapshot = pending.Roll;
                LavaTubeCaveBuildPipeline.QueueRun(
                    pending.Ground.Anchor,
                    pending.Ground,
                    pending.Request,
                    xr,
                    showProgress: true,
                    report =>
                    {
                        try
                        {
                            PresentBuildResult(
                                pending.SceneName,
                                report,
                                rollSnapshot,
                                pending.SkipDialogs);
                        }
                        finally
                        {
                            TeardownBuildSession(undoGroup);
                            _buildInProgress = false;
                        }
                    });
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                _buildInProgress = false;
                CaveBuildPipelineScope.Clear();
                EditorUtility.ClearProgressBar();
            }
        }

        /// <returns>True when teardown is deferred to phased queue completion.</returns>
        static bool QueueOrRunCaveGeometry(
            string sceneName,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool hideLegacyBlockout,
            bool skipDialogs,
            bool layoutPrototype,
            CaveLayoutRoll roll)
        {
            if (!CaveBuildPipelineScope.CaveOnlyContinuation)
                CaveBuildPipelineScope.BeginFullPipeline();

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Complete Cave Level");

            var groundAnchor = ground.Anchor;
            LavaTubeMaterialUpgrader.EnsurePackMaterialsUpgraded();
            BeginBuildAssetEditing();
            CaveBuildAgentArtifacts.ResetForNewBuildSession(sceneName);
            CaveEditorUndo.BeginBulkBuild();
            _buildSessionOpen = true;

            var oldCave = groundAnchor.Find(CaveGeometryPaths.CaveSystemRootName);
            if (oldCave == null)
                oldCave = groundAnchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            if (oldCave != null && request.SurfaceScope != SurfaceBuildScope.SurfaceOnly)
            {
                CaveBuildPhaseContractRegistry.InvalidateCaveGeometryLadderRungs();
                CaveEditorUndo.DestroyImmediate(oldCave.gameObject);
            }

            CleanupStrayCaveSystems(oldCave);
            if (hideLegacyBlockout)
                HideLegacyBlockoutCaves();

            var xr = ResolveBuildXrProfile();

            var cursorSettings = CaveBuildCursorSettings.LoadOrCreate();
            cursorSettings.LoadFromPrefs();
            if (cursorSettings.usePhasedCaveBuild && !layoutPrototype)
            {
                var rollSnapshot = roll;
                if (cursorSettings.enableBatchMode || CaveBuildBatchRunner.IsActive)
                {
                    CaveBuildBatchRunner.StartFromSettings(
                        groundAnchor,
                        ground,
                        request,
                        xr,
                        hideLegacy: hideLegacyBlockout,
                        skipDialogs: skipDialogs,
                        onBatchComplete: summary =>
                        {
                            try
                            {
                                var last = summary.Entries.Count > 0
                                    ? summary.Entries[summary.Entries.Count - 1]
                                    : null;
                                var report = new LavaTubeCaveBuildReport
                                {
                                    QualityScore = last?.QualityScore ?? 0,
                                    QualityLetter = last?.QualityLetter ?? "?",
                                    QualityAcceptable = last?.QualityAcceptable ?? false,
                                    Message =
                                        $"Batch complete — {summary.JobsPassed}/{summary.JobsRun} passed. " +
                                        $"Log: {CaveBuildBatchRunner.LogPath}. " +
                                        (last?.Message ?? string.Empty),
                                };
                                PresentBuildResult(sceneName, report, rollSnapshot, skipDialogs);
                            }
                            finally
                            {
                                TeardownBuildSession(undoGroup);
                                _buildInProgress = false;
                            }
                        });
                    return true;
                }

                LavaTubeCaveBuildPipeline.QueueRun(
                    groundAnchor,
                    ground,
                    request,
                    xr,
                    showProgress: true,
                    report =>
                    {
                        try
                        {
                            PresentBuildResult(sceneName, report, rollSnapshot, skipDialogs);
                        }
                        finally
                        {
                            TeardownBuildSession(undoGroup);
                            _buildInProgress = false;
                        }
                    });
                return true;
            }

            LavaTubeCaveBuildReport report;
            try
            {
                report = LavaTubeCaveBuildPipeline.Run(groundAnchor, ground, request, xr, showProgress: true);
            }
            finally
            {
                TeardownBuildSession(undoGroup);
            }

            PresentBuildResult(sceneName, report, roll, skipDialogs);
            return false;
        }

        static void BeginBuildAssetEditing()
        {
            if (_assetEditingNest++ == 0)
                AssetDatabase.StartAssetEditing();
        }

        static void TeardownBuildSession(int undoGroup)
        {
            if (!_buildSessionOpen)
                return;

            _buildSessionOpen = false;
            CaveEditorUndo.EndBulkBuild();

            if (_assetEditingNest > 0 && --_assetEditingNest == 0)
                AssetDatabase.StopAssetEditing();

            LavaTubeMaterialUpgrader.FlushDeferredAssetChanges();
            Undo.CollapseUndoOperations(undoGroup);
        }

        static void PresentBuildResult(
            string sceneName,
            LavaTubeCaveBuildReport report,
            CaveLayoutRoll roll,
            bool skipDialogs)
        {
            if (report == null)
                report = new LavaTubeCaveBuildReport { Message = "Build produced no report." };

            if (CaveBuildBatchRunner.IsActive && skipDialogs)
            {
                Debug.Log(
                    $"[CaveBuild] Batch job complete — {sceneName} {report.QualityLetter} ({report.QualityScore}/100)");
                var batchQuality = CaveBuildQualitySystem.LastGradedReport ??
                                   CaveBuildRungPromptExporter.TryLoadQualityReport();
                if (CaveBuildGenerationPrefabExporter.TryExportWhenPipelineFinished(
                        sceneName,
                        roll.Seed,
                        batchQuality,
                        "batch_job_complete",
                        out _,
                        out var batchPrefabMsg) &&
                    !string.IsNullOrEmpty(batchPrefabMsg))
                    Debug.Log("[CaveBuild] " + batchPrefabMsg);
                EnvironmentSceneUtility.MarkSceneDirty();
                return;
            }

            var quality = CaveBuildQualitySystem.LastGradedReport ??
                          CaveBuildRungPromptExporter.TryLoadQualityReport();
            if (quality != null)
            {
                report.QualityScore = quality.OverallScore;
                report.QualityLetter = quality.LetterGrade;
                report.QualityAcceptable = quality.MeetsShipTarget;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var cave = CaveRouteProbeRunner.FindCaveRoot();
            var ground = SceneGroundResolver.Resolve();

            CaveBuildQualityFastGates.TrySkipAutonomousShipLoop(roll.Seed, out var skipAutonomousReason);
            if (!string.IsNullOrEmpty(skipAutonomousReason))
                CaveBuildEditorLog.LogCave(skipAutonomousReason, forceUnityConsole: true);

            if (!skipDialogs &&
                settings.enableAutonomousUntilShip &&
                string.IsNullOrEmpty(skipAutonomousReason) &&
                quality != null &&
                !CaveBuildQualityRubric.MeetsShipTarget(quality) &&
                CaveBuildCursorAgentBridge.HasApiKey &&
                cave != null)
            {
                CaveBuildCompletionSummary.WriteCompletionArtifacts(
                    sceneName,
                    report,
                    roll,
                    quality,
                    out _,
                    out _);
                CaveBuildPipelineLog.Info(
                    "Autonomous fix loop starting — finish dialog deferred until Ship or max iterations.",
                    "Autonomous");
                var loopRequest = new WorldGenerationRequest
                {
                    Biome = BiomeId.Cave,
                    CaveMode = CaveGenerationMode.FullSystem,
                };
                roll.ApplyTo(loopRequest);
                CaveBuildAutonomousOrchestrator.Begin(
                    quality,
                    cave,
                    ground,
                    loopRequest,
                    roll,
                    sceneName,
                    () =>
                    {
                        var finalQuality = CaveBuildQualitySystem.LastGradedReport ??
                                           CaveBuildRungPromptExporter.TryLoadQualityReport() ??
                                           quality;
                        if (finalQuality != null)
                        {
                            report.QualityScore = finalQuality.OverallScore;
                            report.QualityLetter = finalQuality.LetterGrade;
                            report.QualityAcceptable = finalQuality.MeetsShipTarget;
                        }

                        if (!CaveBuildPostBuildFinalizeGate.TryOfferPlayModeRecording(
                                sceneName,
                                report,
                                roll,
                                finalQuality,
                                skipDialogs: EnvironmentKitHubWindow.IsOpen))
                        {
                            CaveBuildCompletionSummary.ShowFinished(
                                sceneName,
                                report,
                                roll,
                                finalQuality,
                                showDialog: !EnvironmentKitHubWindow.IsOpen);
                        }

                        CaveBuildDialogPolicy.EndUnifiedSession();
                    });
            }
            else
            {
                if (CaveBuildPostBuildFinalizeGate.TryOfferPlayModeRecording(
                        sceneName,
                        report,
                        roll,
                        quality,
                        skipDialogs))
                    return;

                CaveBuildCompletionSummary.ShowFinished(sceneName, report, roll, quality, showDialog: !skipDialogs);
            }

            if (cave != null)
            {
                var probe = CaveRouteProbeRunner.Run(cave);
                CaveRouteProbeRunner.Export(probe, cave);
                Debug.Log(
                    probe.Passed
                        ? $"[CaveBuild] Editor route probe bot: PASS ({probe.PathSteps} steps) — {CaveRouteProbeRunner.ReportPath}"
                        : $"[CaveBuild] Editor route probe bot: {probe.Issues.Count} issue(s) — {CaveRouteProbeRunner.ReportPath}");

                if (settings.autoRunPlaytestBotAfterBuild)
                    CavePlaytestRouteBotBridge.ScheduleAfterBuild(cave);
            }

            EnvironmentSceneUtility.MarkSceneDirty();
        }

        static CaveLayoutRoll CreateLayoutRoll()
        {
            if (EditorPrefs.GetBool(PrefRandomize, true))
                _unifiedBuildRoll = null;

            if (CaveBuildLayoutRollSession.TryConsumePreservedRoll(out var preserved))
            {
                Debug.Log(
                    $"[CaveBuild] Reusing layout roll seed={preserved.Seed} (agent auto-rebuild — not rolling a new seed).");
                return preserved;
            }

            if (CaveBuildDeterminism.IsPinned)
                return CaveLayoutRoll.CreateRandom(CaveBuildDeterminism.PinnedSeed);

            if (EditorPrefs.GetBool(PrefRandomize, true))
                return CaveLayoutRoll.CreateRandom();

            var fixedSeed = CaveBuildDeterminism.ResolveSeed(EditorPrefs.GetInt(PrefFixedSeed, 0));
            if (fixedSeed > 0)
            {
                Debug.LogWarning(
                    $"[CaveBuild] Random seed is OFF — every build uses fixed seed {fixedSeed}. " +
                    "Use Cave Build → Advanced → Toggle Random Seed Each Build.");
                return CaveLayoutRoll.CreateRandom(fixedSeed);
            }

            Debug.LogWarning(
                "[CaveBuild] Random seed is OFF but no fixed seed set — rolling a new seed anyway.");
            return CaveLayoutRoll.CreateRandom();
        }

        static void LogLayoutRoll(CaveLayoutRoll roll, bool caveOnly = false)
        {
            if (roll == null)
                return;

            var mode = EditorPrefs.GetBool(PrefRandomize, true)
                ? "random each build"
                : $"fixed (EditorPrefs {PrefFixedSeed}={EditorPrefs.GetInt(PrefFixedSeed, 0)})";
            if (caveOnly)
            {
                CaveBuildPipelineDomains.LogCave(
                    $"Layout roll — {roll.ToCaveOnlyString()} | mode={mode} | surface=FROZEN (pre-built)",
                    forceUnityConsole: true);
                return;
            }

            CaveBuildPipelineDomains.LogCave(
                $"Layout roll — {roll} | mode={mode}");
            CaveBuildPipelineDomains.LogSurface(
                "Surface roll applies when surface pipeline runs (sectors, time/weather, props).");
        }

        static void CleanupStrayCaveSystems(Transform keepUnder)
        {
            foreach (var envRoot in UnityEngine.Object.FindObjectsByType<EnvironmentRoot>(FindObjectsInactive.Exclude))
            {
                for (var i = envRoot.transform.childCount - 1; i >= 0; i--)
                {
                    var child = envRoot.transform.GetChild(i);
                    if (child == null)
                        continue;

                    var n = child.name;
                    if (n.StartsWith(CaveBuildSatelliteCaveBuilder.PoiRootPrefix))
                        continue;

                    if (n != CaveGeometryPaths.CaveSystemRootName &&
                        n != CaveGeometryPaths.LegacyCaveSystemRootName)
                        continue;

                    if (child != keepUnder)
                        CaveEditorUndo.DestroyImmediate(child.gameObject);
                }
            }
        }

        static XROptimizationProfile ResolveBuildXrProfile()
        {
            var xr = AssetDatabase.LoadAssetAtPath<XROptimizationProfile>(
                $"{EnvironmentKitSettings.PresetsFolder}/VitureXRPro.asset");
            return EnvironmentKitHardwareBudget.ResolveXrProfile(xr);
        }

        static void TryOpenMainScene()
        {
            const string path = "Assets/MainScene.unity";
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset == null)
                return;

            EnvironmentKitSceneSafeguards.TryOpenSceneAsset(asset, allowUserPrompt: false);
        }

        static bool SamplePresetsExist() =>
            AssetDatabase.LoadAssetAtPath<BiomeCatalog>(
                $"{EnvironmentKitSettings.PresetsFolder}/BiomeCatalog.asset") != null;

        static Transform LoadUserGround()
        {
            var stored = EditorPrefs.GetString(EnvironmentKitSettings.GroundObjectKey, string.Empty);
            if (string.IsNullOrEmpty(stored) || !GlobalObjectId.TryParse(stored, out var id))
                return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is GameObject go ? go.transform : null;
        }

        static void ClearInvalidStoredGround()
        {
            var assigned = LoadUserGround();
            if (assigned != null &&
                SceneGroundResolver.IsValidGroundAnchor(assigned) &&
                !SceneGroundResolver.IsModularKitBlockoutGrid(assigned))
                return;
            SceneGroundResolver.ClearStoredGround();
        }

        internal static SceneGroundInfo ResolveGroundForBuild(Transform userAssigned, SurfaceBuildScope scope)
        {
            CaveBuildProjectSetup.EnsureMinimalSceneDefaults(out _);
            return scope == SurfaceBuildScope.FullWorld
                ? SceneGroundResolver.EnforceFullWorldGround(SceneGroundResolver.ResolveForFullWorld(userAssigned))
                : SceneGroundResolver.Resolve(userAssigned);
        }

        static void HideLegacyBlockoutCaves()
        {
            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentRoot>();
            if (envRoot == null)
                return;
            var legacy = envRoot.transform.Find("Caves");
            if (legacy == null)
                return;
            CaveEditorUndo.RecordObject(legacy.gameObject, "Hide Legacy Caves");
            legacy.gameObject.SetActive(false);
        }
    }
}
