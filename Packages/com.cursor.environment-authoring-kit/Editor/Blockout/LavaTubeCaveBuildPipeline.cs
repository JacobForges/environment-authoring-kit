using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>40-stage graded cave build with adventure playability pass and quality export.</summary>
    public static partial class LavaTubeCaveBuildPipeline
    {
        public const int StageCount = 40;
        const int PlayabilityStageStart = 5;
        const double ProgressBarMinIntervalSeconds = 0.35;
        static double _lastProgressBarTime;

        public static LavaTubeCaveBuildReport Run(
            Transform groundAnchor,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            XROptimizationProfile xrProfile,
            bool showProgress = true)
        {
            CaveBuildWorkflowCoordinator.BeginSession();
            var catalog = LavaTubePrefabCatalog.Load();
            var rng = new System.Random(request.Seed);
            LavaTubeCaveBuildReport report = null;
            Transform caveRoot = null;

            var remediationPasses = 0;
            try
            {
                Progress(showProgress, 1, "Validate catalog & scene ground…");
                if (!catalog.IsValid)
                {
                    return new LavaTubeCaveBuildReport
                    {
                        Message = "Prefab catalog empty — check lava tube prefabs under Assets/."
                    };
                }

                if (!request.UseLayoutPrototype)
                {
                    request.UseSplineMesh = true;
                    request.UseTrue3DCaveSystem = true;
                    request.UseBlockTunnel = true;
                    request.IncludeCaveWater = true;
                    request.UseTerrainCarve = true;
                }

                Progress(showProgress, 2, $"Roll layout seed {request.Seed}…");
                report = new LavaTubeCaveBuildReport
                {
                    Message = $"Layout seed {request.Seed}, segments {request.CaveTunnelSegments}, chambers {request.CaveChamberCount}."
                };

                Progress(showProgress, 3, "Purge legacy blockout geometry…");
                Progress(showProgress, 4, "Cave geometry (entrance + organic tube)…");
                if (request.UseSplineMesh)
                {
                    report = SplineLavaTubeCaveGenerator.Generate(
                        groundAnchor,
                        ground,
                        request,
                        (t, label) => StageProgress(showProgress, 4, label, 0.1f + t * 0.12f));
                }
                else
                {
                    report = LavaTubeCaveGenerator.Generate(groundAnchor, ground, request);
                }

                caveRoot = groundAnchor.Find(CaveGeometryPaths.CaveSystemRootName);
                if (caveRoot == null)
                    caveRoot = groundAnchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (caveRoot == null)
                    return report;

                if (request.UseLayoutPrototype)
                    return FinalizeLayoutPrototype(caveRoot, ground, request, report, showProgress);

                if (!CavePlayabilityValidator.IsAdventureCave(caveRoot))
                    EnsureBlockTunnelShell(caveRoot, request);

                if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                {
                    var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
                    if (geometry != null)
                        CaveEnclosureShellBuilder.PurgeLayerOffenders(geometry);

                    for (var step = 0; step < CaveAdventurePlayabilityPipeline.StepCount; step++)
                    {
                        Progress(showProgress, PlayabilityStageStart + step,
                            CaveAdventurePlayabilityPipeline.StepLabels[step]);
                        CaveAdventurePlayabilityPipeline.RunStep(step, caveRoot, request, ground);
                        CaveBuildActionPacing.YieldToEditor(
                            CaveBuildActionPacing.ActionWeight.Light,
                            "after playability " + step);
                    }

                    for (var v = 0; v < CaveBuildAutomatedValidation.StepCount; v++)
                    {
                        Progress(showProgress, 22 + v, CaveBuildAutomatedValidation.StepLabels[v]);
                        CaveBuildAutomatedValidation.RunStep(v, caveRoot, ground, request);
                    }

                    CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
                    var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                    if (meta != null)
                    {
                        var layout = CaveMazeLayoutGenerator.Generate(
                            meta.seed, meta.tunnelSegments, meta.chamberCount);
                        CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
                        CaveAdventureCaveLighting.Apply(caveRoot, layout);
                    }
                }

                Progress(showProgress, 27, "Organic mesh QA…");
                report.SeamlessQuality = ValidateOrganicMesh(caveRoot);

                Progress(showProgress, 28, "Scatter natural props along path…");
                if (CaveBuildWorkflowCoordinator.TryConsumeWorldPropScatter())
                    ScatterExtraProps(caveRoot, catalog, rng, request);

                Progress(showProgress, 29, "Occlusion shell (seal sky gaps)…");
                if (request.UseTrue3DCaveSystem)
                {
                    var shell = caveRoot.Find("OcclusionShell");
                    if (shell != null)
                        CaveBuildSceneUtility.ClearChildrenFast(shell);
                    report.ShellPieceCount = 0;
                }
                else
                {
                    report.ShellPieceCount = LavaTubeCaveEnclosureBuilder.Build(
                        caveRoot, catalog, rng, report.PathNodes);
                }

                Progress(showProgress, 30, "Cave rock materials…");
                CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                if (request.IncludeCaveWater)
                    BuildCaveWater(caveRoot);
                else
                    CaveWaterUtility.ClearAllWater(caveRoot);

                Progress(showProgress, 31, "Cave-only lighting…");
                LavaTubeCavePostProcess.ApplyLightingOnly(caveRoot);

                Progress(showProgress, 32, "FX — motes, gleam, entrance glow, fog mist…");
                BuildFx(caveRoot, rng);
                BuildFogMist(caveRoot);

                Progress(showProgress, 33, "Block cull distance + colliders + LOD (XR)…");
                CaveBlockTunnelRuntimeSetup.EnsureOnCaveRoot(caveRoot);
                var postPhysics = LavaTubeCavePostProcess.ApplyPhysicsAndLod(
                    caveRoot, xrProfile, bakeGiHints: true);
                RestoreBlockVisibilityForEditor(caveRoot);
                if (request.UseTrue3DCaveSystem)
                    CaveAdventureVisualPass.Apply(caveRoot);
                report.DrawCallEstimate = postPhysics.DrawCallEstimate;
                report.TriangleEstimate = postPhysics.TriangleEstimate;
                report.PieceCount = postPhysics.PieceCount;

                Progress(showProgress, 34, "NavMesh (floor walkable only)…");
                report.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);

                Progress(showProgress, 35, "Spawn points (surface + cave) & portal…");
                FinalizeGameplay(caveRoot, ground, report, request);

                Progress(showProgress, 36, "Final material + collider pass…");
                CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
                if (!CaveGeometryPaths.IsAdventureCave(caveRoot))
                    CaveFloorSafetyUtility.EnsureVisibleWalkways(caveRoot);
                CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot);
                if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                {
                    CaveBuildWorkflowCoordinator.InvalidateNavMesh();
                    CaveAdventureVisualPass.Apply(caveRoot);
                }

                if (request.UseTrue3DCaveSystem)
                    report.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, force: true);
                if (request.IncludeCaveWater)
                    CaveWaterBuilder.RebuildForCave(caveRoot);
                else
                    CaveWaterUtility.ClearAllWater(caveRoot);

                Progress(showProgress, 37, "Enclosure + playability validation…");
                if (!CaveGeometryPaths.IsAdventureCave(caveRoot))
                    LavaTubeCaveEnclosureBuilder.EnsureAtmosphereZone(caveRoot, report.PathNodes);
                else
                    CaveBuildAutomatedValidation.RunFinalProbe(caveRoot);
                ValidatePlayabilityGate(caveRoot, request);
                ValidateEnclosure(caveRoot, report, request.UseSplineMesh);
                if (request.IncludeCaveWater)
                    ValidateCaveWater(caveRoot);

                if (caveRoot != null && ground != null && ground.HasAnchor)
                {
                    if (CaveGroundPlacementUtility.TryAlignUndergroundRoot(caveRoot, ground, out var alignMsg))
                        Debug.Log("[CaveBuild] " + alignMsg);
                }

                Progress(showProgress, 38, $"{CaveBuildQualityRubric.TargetGrade} quality ladder (grade → fix → re-grade)…");
                var ladderGradePasses = 0;
                var quality = CaveBuildQualityMeatLoop.Run(
                    caveRoot,
                    ground,
                    request,
                    report,
                    showProgress,
                    (step, label) => Progress(showProgress, 38, label),
                    out remediationPasses,
                    out ladderGradePasses);

                quality = RegradeAndExportForCursorInvoke(
                    caveRoot, ground, request, report, quality, "post_meat_loop");

                if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                {
                    CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.PostMeat);
                    CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
                    CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                    CaveAdventureVisualPass.Apply(caveRoot);
                    CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                    EnsureGameplaySpawns(caveRoot, ground);
                    CaveFloorSafetyUtility.EnsureRouteTerrainPlayCollider(caveRoot);
                    CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(caveRoot);
                    if (!CaveAdventurePlayabilityPipeline.CheckSpawnReachability(caveRoot))
                        Debug.LogWarning("[CaveBuild] Spawn reachability still low after quality ladder.");

                    quality = RegradeAndExportForCursorInvoke(
                        caveRoot, ground, request, report, quality, "post_adventure_passes");
                }

                var settings = CaveBuildCursorSettings.LoadOrCreate();
                settings.LoadFromPrefs();
                if (settings.runPostBuildResearchPhase)
                {
                    CaveBuildWorkflowCoordinator.EnterPhase(CaveBuildWorkflowCoordinator.Phase.Research);
                    CaveBuildResearchPhase.RunAnalyzeNeeds(quality, caveRoot, ground);
                    CaveBuildResearchPhase.RunCatalogRefresh(quality, caveRoot, ground);
                    CaveBuildResearchPhase.RunOnlineEnrichment(quality, caveRoot, ground);
                    CaveBuildResearchPhase.RunPersistPhaseSummary(quality);
                }

                var production = CaveBuildCommercialProductionGrader.Grade(
                    caveRoot, ground, request, report, quality);

                Progress(showProgress, 40, "Finalize build report…");
                report.MinableCount = caveRoot.GetComponentsInChildren<MinableRock>(true).Length;
                report.QualityScore = quality.OverallScore;
                report.QualityLetter = quality.LetterGrade;
                report.QualityAcceptable = quality.BuildAcceptable;
                quality.RemediationPasses = remediationPasses;
                quality.LadderGradePasses = ladderGradePasses;
                var targetNote = quality.BuildAcceptable
                    ? "TARGET MET"
                    : $"below {CaveBuildQualityRubric.TargetGrade} ({CaveBuildQualityRubric.TargetOverallScore}+)";
                report.Message =
                    $"Complete cave in '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'. " +
                    $"Grade {quality.LetterGrade} ({quality.OverallScore}/100) {targetNote}. " +
                    $"Production checklist: {production.LetterGrade} ({production.OverallScore}/100). Seed {request.Seed}. " +
                    report.SeamlessQuality + " " + report.Message;

                if (!quality.BuildAcceptable)
                {
                    Debug.LogWarning(
                        $"[CaveBuild] Build finished below {CaveBuildQualityRubric.TargetGrade}. " +
                        $"Open {quality.ExportPath} for failing stages, then rebuild.");
                }

                if (caveRoot != null && quality != null)
                {
                    CaveBuildPipelineCompletion.OnFullPipelineFinished();
                    if (!CaveBuildCursorSettings.DefersPostBuildCursorToAutonomousLoop())
                    {
                        CaveBuildCursorAgentBridge.TryAutoInvokeAfterBuildComplete(
                            quality, caveRoot, ground, afterPostBuildPasses: true);
                    }
                }
            }
            finally
            {
                CaveBuildWorkflowCoordinator.EndSession();
                if (showProgress)
                    EditorUtility.ClearProgressBar();
            }

            return report ?? new LavaTubeCaveBuildReport { Message = "Build failed." };
        }

        static CaveBuildQualityReport RegradeAndExportForCursorInvoke(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            CaveBuildQualityReport previous,
            string gradingMode)
        {
            if (caveRoot == null)
                return previous;

            var inQueue = CaveBuildActionPacing.IsInsideQueueInvoke;
            if (inQueue && previous != null && CaveBuildQualityRubric.MeetsShipTarget(previous))
            {
                previous.GradingMode = gradingMode;
                CaveBuildQualitySystem.ApplyRecommendedAction(previous, request);
                CaveBuildQualityReportWriter.Write(previous, gradingMode);
                CaveBuildAgentContextExporter.Export(previous, caveRoot, meatLoopPass: -1, ground);
                return previous;
            }

            var quality = CaveBuildQualitySystem.Grade(
                caveRoot,
                ground,
                request,
                buildReport,
                gradingMode,
                invokeCursorAgent: false,
                refreshNavMeshBeforeGrade: !inQueue,
                runPostGradeVisualCleanup: !inQueue);
            quality.RemediationPasses = previous?.RemediationPasses ?? 0;
            quality.LadderGradePasses = previous?.LadderGradePasses ?? 0;
            quality.AdventureMode = previous?.AdventureMode ?? CaveGeometryPaths.IsAdventureCave(caveRoot);
            quality.LayoutPrototypeMode = previous?.LayoutPrototypeMode ?? false;
            return quality;
        }

        static bool IsStagePassed(CaveBuildQualityReport quality, string stageId)
        {
            if (quality == null)
                return true;

            foreach (var s in quality.Stages)
            {
                if (s.StageId == stageId)
                    return s.Score >= CaveBuildQualityRubric.StagePassScore;
            }

            return true;
        }

        static string ValidateOrganicMesh(Transform caveRoot)
        {
            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                var shell = caveRoot.Find(
                    $"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}");
                var floors = 0;
                if (shell != null)
                {
                    foreach (Transform child in shell)
                    {
                        if (child.name.StartsWith("Floor_"))
                            floors++;
                    }
                }

                var adventureBlocks = 0;
                var adventureTunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
                if (adventureTunnel != null)
                {
                    foreach (var t in adventureTunnel.GetComponentsInChildren<Transform>(true))
                    {
                        if (t != null && t.name.StartsWith("CaveBlock_"))
                            adventureBlocks++;
                    }
                }

                var platforms = 0;
                var platRoot = caveRoot.Find(
                    $"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureBlockBuilder.PlatformsRootName}");
                if (platRoot != null)
                    platforms = platRoot.childCount;

                if (platforms >= 6 && adventureBlocks >= 80)
                    return $"Cave course OK ({platforms} platforms, {adventureBlocks} rock blocks).";

                return floors >= 6 && adventureBlocks >= 120
                    ? $"Cave OK ({floors} shell floors, {adventureBlocks} blocks)."
                    : $"FAIL: sparse course ({platforms} platforms, {floors} shell floors, {adventureBlocks} blocks).";
            }

            var maze = caveRoot.Find("SplineMesh/CaveMazeVolume");
            if (maze != null)
            {
                var walls = maze.GetComponentsInChildren<MeshRenderer>(true).Length;
                var cols = maze.GetComponentsInChildren<Collider>(true).Length;
                return walls >= 24
                    ? $"Maze volume OK ({walls} walls, {cols} colliders)."
                    : $"FAIL: Maze volume too sparse ({walls} walls, {cols} colliders).";
            }

            var blockRoot = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            var blockCount = blockRoot != null ? blockRoot.GetComponentsInChildren<Transform>(true).Length : 0;
            if (blockCount > 80)
                return $"Block tunnel OK (~{blockCount} transforms).";

            var meshRoot = caveRoot.Find("SplineMesh");
            var main = meshRoot != null ? meshRoot.Find("MainCaveTube") : null;
            if (main == null)
                return "FAIL: Missing CaveMazeVolume, MainCaveTube, and BlockTunnel — rebuild required.";

            var mf = main.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                return "FAIL: MainCaveTube has no mesh.";

            return $"Organic tube OK ({mf.sharedMesh.vertexCount} verts, {mf.sharedMesh.triangles.Length / 3} tris).";
        }

        static void ValidateCaveWater(Transform caveRoot)
        {
            var water = caveRoot.Find("Water");
            if (water == null)
            {
                Debug.LogWarning("[CaveBuild] No Water root.");
                return;
            }

            var pool = water.Find("UndergroundRiver_Pool");
            if (pool == null || pool.GetComponent<CaveUndergroundWaterPool>() == null)
                Debug.LogWarning("[CaveBuild] Missing UndergroundRiver_Pool with CaveUndergroundWaterPool.");
        }

        static void ValidatePlayabilityGate(Transform caveRoot, WorldGenerationRequest request)
        {
            if (caveRoot == null)
                return;

            var adventure = CavePlayabilityValidator.IsAdventureCave(caveRoot);
            var autoFixed = CavePlayabilityValidator.AutoFix(caveRoot);
            var removed = adventure ? 0 : CavePlayabilityValidator.RemoveDecorativeColliders(caveRoot);
            var invisible = CavePlayabilityValidator.CountInvisibleSolidColliders(caveRoot);
            var openCeiling = CavePlayabilityValidator.CountOpenCeilingSamples(caveRoot, samples: 16);
            var waterOk = request != null && request.IncludeCaveWater &&
                          CavePlayabilityValidator.EnsureWaterMaterial(caveRoot);

            if (autoFixed)
                Debug.Log("[CaveBuild] Playability gate auto-fix applied.");
            if (removed > 0)
                Debug.Log($"[CaveBuild] Playability gate removed {removed} decorative collider(s).");
            if (invisible > 0)
                Debug.LogWarning($"[CaveBuild] Playability gate found {invisible} invisible solid collider(s).");
            if (openCeiling > 4)
                Debug.LogWarning($"[CaveBuild] Playability gate found {openCeiling}/16 open-ceiling samples.");
            if (!waterOk)
                Debug.LogWarning("[CaveBuild] Playability gate failed water material check.");

        }

        static void ValidateEnclosure(Transform caveRoot, LavaTubeCaveBuildReport report, bool organicMesh)
        {
            if (organicMesh)
            {
                report.SeamlessQuality += " " + ValidateOrganicMesh(caveRoot);
                return;
            }

            var floors = 0;
            var ceilings = 0;
            var walls = 0;
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                if (n.Contains("Floor") || n.Contains("SM_Floor"))
                    floors++;
                if (n.Contains("Ceiling") || n.Contains("SM_Ceiling") || n.Contains("Cupola"))
                    ceilings++;
                if (n.Contains("Wall") || n.Contains("SM_Wall"))
                    walls++;
            }

            if (floors < 8 || ceilings < 8 || walls < 16)
            {
                Debug.LogWarning(
                    $"[CaveBuild] Low enclosure counts — floors={floors}, ceilings={ceilings}, walls={walls}. Rebuild or check catalog.");
            }
        }

        static void Progress(bool show, int stage, string label)
        {
            if (!show)
                return;

            if (!ShouldRefreshProgressBar())
                return;

            var t = stage / (float)StageCount;
            EditorUtility.DisplayProgressBar(
                "Complete Cave Level",
                $"Stage {stage}/{StageCount} — {label}",
                t);
        }

        /// <summary>Forces the modal progress bar to match the current validate substep (not throttled).</summary>
        public static void ProgressQueuedValidateNow(bool show, int macroStep, int macroTotal, string label)
        {
            if (!show)
                return;
            _lastProgressBarTime = 0;
            ProgressQueued(show, macroStep, macroTotal, label);
        }

        /// <summary>Queued pipeline progress — avoids stale Stage 1/40 while on macro step 2/120.</summary>
        public static void ProgressQueued(bool show, int macroStep, int macroTotal, string label)
        {
            if (!show)
                return;

            if (!ShouldRefreshProgressBar())
                return;

            var idx = Mathf.Clamp(macroStep + 1, 1, macroTotal);
            var t = macroStep / (float)Mathf.Max(1, macroTotal);
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"Build {idx}/{macroTotal} — {label}",
                t);
        }

        static bool ShouldRefreshProgressBar()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastProgressBarTime < ProgressBarMinIntervalSeconds)
                return false;
            _lastProgressBarTime = now;
            return true;
        }

        static bool StageProgress(bool show, int stage, string label, float overallProgress)
        {
            if (!show)
                return false;

            if (!ShouldRefreshProgressBar())
                return false;

            if (EditorUtility.DisplayCancelableProgressBar(
                    "Complete Cave Level",
                    $"Stage {stage}/{StageCount} — {label}",
                    overallProgress))
            {
                Debug.LogWarning("[CaveBuild] Cancelled during geometry build.");
                return true;
            }

            return false;
        }

        static void EnsureBlockTunnelShell(Transform caveRoot, WorldGenerationRequest request)
        {
            if (!request.UseBlockTunnel || CaveGeometryPaths.IsAdventureCave(caveRoot))
                return;

            var blockRoot = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            var blockCount = 0;
            if (blockRoot != null)
            {
                foreach (var t in blockRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("CaveBlock_"))
                        blockCount++;
                }
            }

            if (blockCount >= 40)
                return;

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return;

            var rock = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (rock == null)
            {
                Debug.LogError("[CaveBuild] Cannot build block tunnel — missing cave rock material.");
                return;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var settings = CaveBlockTunnelBuilder.Settings.Default;
            if (request.UseTrue3DCaveSystem)
            {
                settings.RingSpacing = 2.35f;
                settings.AngularSteps = 14;
                settings.FloorLayers = 0;
                settings.CeilingLayers = 0;
                settings.WallThickness = 2;
                settings.InteriorHollow = 0.48f;
                settings.OuterWallMinable = true;
            }

            var placed = CaveBlockTunnelBuilder.Build(caveRoot, spline, rock, request.Seed + 909, settings, sectionName: "Main");
            if (request.UseTrue3DCaveSystem)
            {
                var meshRoot = caveRoot.Find("SplineMesh");
                var mazeVol = meshRoot != null ? meshRoot.Find(CaveMazeVolumeBuilder.MazeVolumeRootName) : null;
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                if (mazeVol != null && meta != null)
                {
                    var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
                    CaveMazeCeilingCoverBuilder.Build(mazeVol, layout, rock);
                }
                else if (meshRoot != null)
                {
                    CaveCeilingSealUtility.BuildAlongSpline(meshRoot, spline, rock, mazeMode: true);
                }
            }
            Debug.Log($"[CaveBuild] Block tunnel remediation placed {placed} cubes.");
        }

        static void RestoreBlockVisibilityForEditor(Transform caveRoot)
        {
            var culler = caveRoot.GetComponent<CaveBlockTunnelCuller>();
            if (culler != null)
            {
                culler.distanceCullingEnabled = false;
                culler.RestoreAllBlocks();
            }

            foreach (var renderer in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                if (renderer.gameObject.name.StartsWith("CaveBlock_Minable"))
                    renderer.enabled = true;
            }

            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot != null)
            {
                foreach (var mr in meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr != null)
                        mr.enabled = true;
                }
            }

            var waterRoot = caveRoot.Find("Water");
            if (waterRoot != null)
            {
                var branchMr = waterRoot.Find("WaterBranchTube")?.GetComponent<MeshRenderer>();
                if (branchMr != null)
                    branchMr.enabled = true;
            }
        }

        static void BuildCaveWater(Transform caveRoot)
        {
            var waterRoot = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "Water");
            CaveLegacyGeometryPurge.Purge(caveRoot);

            var anchor = caveRoot.GetComponent<CaveWaterBranchAnchor>();
            if (anchor != null)
            {
                CaveWaterBuilder.Build(
                    waterRoot,
                    anchor.poolLocalPosition,
                    anchor.waterfallLocalPosition,
                    poolExtentMeters: 12f,
                    caveRoot);
            }

            if (waterRoot.GetComponent<CaveWaterFxPlayer>() == null)
                waterRoot.gameObject.AddComponent<CaveWaterFxPlayer>();
        }

        static void BuildFogMist(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            CaveFogMistBuilder.Build(caveRoot, spline);
        }

        static void ScatterExtraProps(
            Transform caveRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            WorldGenerationRequest request)
        {
            var propsRoot = caveRoot.Find("Details/Props");
            if (propsRoot == null)
                return;

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var count = request.CavePropScatterCount > 0 ? request.CavePropScatterCount : 20;
            CaveBuildLiveSceneFeedback.NotifyStep(
                $"Scattering up to {count} cave props along route…",
                caveRoot,
                frameScene: true);

            for (var i = 0; i < count; i++)
            {
                var dist = (float)rng.NextDouble() * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var offset = sample.Right * Mathf.Cos(angle) * sample.RadiusX * 0.65f +
                             sample.Up * Mathf.Sin(angle) * sample.RadiusY * 0.45f;
                CavePrefabScatter.PlaceRandomProp(propsRoot, catalog, rng, sample.Position + offset, 0.7f);
            }
        }

        static void BuildFx(Transform caveRoot, System.Random rng)
        {
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null)
                return;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            CaveSplineFxBuilder.Build(caveRoot, spline, rng);
        }

        /// <summary>Surface PlayerSpawnPoint + underground cave entrance spawn (called from generator and pipeline).</summary>
        public static void EnsureGameplaySpawns(Transform caveRoot, SceneGroundInfo ground)
        {
            if (caveRoot == null)
                return;

            if (!caveRoot.gameObject.activeSelf)
            {
                CaveEditorUndo.RecordObject(caveRoot.gameObject, "Enable LavaTubeCaveSystem");
                caveRoot.gameObject.SetActive(true);
            }

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            EnsureCaveEntranceSpawn(spawn);
            EnsureMainSceneSurfaceSpawn(caveRoot, ground);
        }

        static void FinalizeGameplay(
            Transform caveRoot,
            SceneGroundInfo ground,
            LavaTubeCaveBuildReport report,
            WorldGenerationRequest request)
        {
            var entrance = caveRoot.Find("Entrance");
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            Transform spawn = null;
            if (authoring != null && entrance != null)
            {
                var spline = new CaveSplinePath();
                spline.SetKnots(authoring.Knots);
                CaveMazeLayout mazeLayout = null;
                if (request != null && request.UseTrue3DCaveSystem)
                {
                    mazeLayout = CaveMazeLayoutGenerator.Generate(
                        request.Seed,
                        request.CaveTunnelSegments,
                        request.CaveChamberCount);
                }

                spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(
                    caveRoot, entrance, spline, keepAtSurfaceMouth: false, mazeLayout);
            }

            spawn = CaveSpawnTeleportAuthority.ApplyMainAreaTeleportSpawn(caveRoot, request) ?? spawn;
            spawn ??= caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            EnsureGameplaySpawns(caveRoot, ground);
            CaveEntrancePortalPreserver.Apply(caveRoot, ground, spawn);
            LavaTubeCavePostProcess.EnsureRegistry(caveRoot);
        }

        /// <summary>Underground portal target only — not Shift+R.</summary>
        static void EnsureCaveEntranceSpawn(Transform caveSpawn)
        {
            if (caveSpawn == null)
                return;

            var entranceMarker = caveSpawn.GetComponent<CaveEntranceSpawnPoint>();
            if (entranceMarker == null)
                entranceMarker = CaveEditorUndo.GetOrAddComponent<CaveEntranceSpawnPoint>(caveSpawn.gameObject);
            entranceMarker.snapPlayerOnStart = false;

            try
            {
                if (!caveSpawn.CompareTag(CaveTags.Entrance))
                {
                    CaveEditorUndo.RecordObject(caveSpawn.gameObject, "Cave Spawn Tag");
                    caveSpawn.tag = CaveTags.Entrance;
                }
            }
            catch (UnityException) { }

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(caveSpawn.gameObject);
            foreach (Transform child in caveSpawn.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
        }

        /// <summary>Shift+R respawn on the surface (Grid/PlayerSpawnPoint), never inside LavaTubeCaveSystem.</summary>
        static void EnsureMainSceneSurfaceSpawn(Transform caveRoot, SceneGroundInfo ground)
        {
            const string spawnName = "PlayerSpawnPoint";
            var parent = caveRoot != null ? caveRoot.parent : null;
            if (parent == null)
                parent = caveRoot;

            var surfacePos = ResolveMainSceneSpawnPosition(ground, caveRoot);
            var forward = ground.HasAnchor ? ground.HorizontalForward : Vector3.forward;

            var go = GameObject.Find(spawnName);
            if (go != null && caveRoot != null && go.transform.IsChildOf(caveRoot))
            {
                CaveEditorUndo.RecordObject(go.transform, "Move PlayerSpawnPoint to surface");
                go.transform.SetParent(parent, true);
            }

            if (go == null)
            {
                go = new GameObject(spawnName);
                CaveEditorUndo.RegisterCreated(go, "PlayerSpawnPoint");
                go.transform.SetParent(parent, false);
            }

            CaveEditorUndo.RecordObject(go.transform, "PlayerSpawnPoint");
            go.transform.position = surfacePos;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            try
            {
                if (!go.CompareTag("PlayerSpawn"))
                {
                    CaveEditorUndo.RecordObject(go, "PlayerSpawn tag");
                    go.tag = "PlayerSpawn";
                }
            }
            catch (UnityException)
            {
                Debug.LogWarning("[CaveBuild] Add tag 'PlayerSpawn' in Project Settings → Tags for Shift+R.");
            }

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        }

        static LavaTubeCaveBuildReport FinalizeLayoutPrototype(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport report,
            bool showProgress)
        {
            Progress(showProgress, 38, "Layout prototype — NavMesh + grade…");
            report.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);
            EnsureGameplaySpawns(caveRoot, ground);

            var quality = CaveBuildQualitySystem.Grade(
                caveRoot, ground, request, report, "layout_prototype", invokeCursorAgent: false);
            quality.AdventureMode = true;
            if (!CaveBuildCursorSettings.DefersPostBuildCursorToAutonomousLoop())
            {
                CaveBuildCursorAgentBridge.TryAutoInvokeAfterBuildComplete(
                    quality, caveRoot, ground, afterPostBuildPasses: false);
            }

            report.QualityScore = quality.OverallScore;
            report.QualityLetter = quality.LetterGrade;
            report.QualityAcceptable = quality.BuildAcceptable;
            report.Message += $"\nLayout grade: {quality.LetterGrade} ({quality.OverallScore}). Blueprint: {CaveLayoutPrototypeGenerator.ExportPath}";

            EditorUtility.SetDirty(caveRoot.gameObject);
            if (showProgress)
                EditorUtility.ClearProgressBar();

            return report;
        }

        static Vector3 ResolveMainSceneSpawnPosition(SceneGroundInfo ground, Transform caveRoot)
        {
            var portal = GameObject.Find("PortalFive");
            if (portal == null || portal.name.Contains("(1)"))
                portal = GameObject.Find("MainScene_CavePortal");

            if (portal != null)
                return portal.transform.position + Vector3.up * 0.35f;

            if (ground.HasAnchor)
            {
                var center = ground.Bounds.center;
                center.y = ground.SurfaceY + 0.35f;
                var back = ground.HorizontalForward * Mathf.Max(8f, ground.Bounds.extents.z * 0.25f);
                return center - back;
            }

            return caveRoot != null ? caveRoot.position + Vector3.up * 12f : Vector3.up * 2f;
        }

        internal sealed class QueuedPipelineContext
        {
            public Transform GroundAnchor;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public XROptimizationProfile XrProfile;
            public bool ShowProgress;
            public System.Action<LavaTubeCaveBuildReport> OnComplete;
            public LavaTubePrefabCatalog Catalog;
            public System.Random Rng;
            public LavaTubeCaveBuildReport Report;
            public Transform CaveRoot;
            public int RemediationPasses;
            public int BuildStep;
            public CaveAdventureCaveGenerator.QueuedBuildState Adventure;
            public CaveBuildQualityMeatLoop.QueuedMeatState Meat;
            public int PostMeatStep;
            public int ValidateSubStep;
            public string ValidateResearchAccumulated;
            public bool CaveOnlyContinuation;
            public bool ValidateBeginUiPublished;
            public bool ValidateAwaitingTsx;
            public bool QueuedAwaitingResearchPrompt;
            public int QueuedAwaitingResearchPromptStep = -1;
        }

        static QueuedPipelineContext _queued;

        public static bool IsPhasedBuildActive => _queued != null;

        /// <summary>Clears the 120-step queued pipeline without scheduling follow-up Cursor/auto-rebuild work.</summary>
        public static void EmergencyAbortQueuedBuild()
        {
            if (_queued == null)
                return;

            CaveBuildValidateTickRunner.EmergencyClear();
            _queued = null;
            CaveBuildPipelineScope.Clear();
            EnvironmentKitHardwareBudget.EndEditorSession();
            CaveBuildLiveSceneFeedback.EndBuildSession();
            CaveBuildWorkflowCoordinator.EndSession();
            ResetResumeAfterAgentArmed();
            CaveBuildAutomatedFullWorldBootstrap.ClearSession();
            EditorUtility.ClearProgressBar();
            Debug.Log("[CaveBuild] Queued pipeline aborted (emergency stop).");
        }

        /// <summary>True while validate micro-steps (research, catalog, flags, adventure init) are still running.</summary>
        public static bool IsValidateSubPipelineActive =>
            _queued != null && _queued.ValidateSubStep != ValidateSubBeginSession;

        /// <summary>Runs the 40-stage pipeline across 34 paced heavy queue steps (8 geometry + 18 playability + 5 world + quality).</summary>
        public static void QueueRun(
            Transform groundAnchor,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            XROptimizationProfile xrProfile,
            bool showProgress,
            System.Action<LavaTubeCaveBuildReport> onComplete)
        {
            if (_queued != null)
            {
                Debug.LogWarning("[CaveBuild] Queued pipeline already active — wait for the current build.");
                return;
            }

            _queued = new QueuedPipelineContext
            {
                GroundAnchor = groundAnchor,
                Ground = ground,
                Request = request,
                XrProfile = xrProfile,
                ShowProgress = showProgress,
                OnComplete = onComplete,
                Catalog = LavaTubePrefabCatalog.Load(),
                Rng = new System.Random(request.Seed),
                CaveOnlyContinuation = CaveBuildPipelineScope.CaveOnlyContinuation,
                ValidateSubStep = ValidateSubBeginSession,
            };

            CaveBuildAutomatedFullWorldBootstrap.ApplyToRequest(request);
            CaveBuildPipelineCompletion.OnUserStartedBuild();
            CaveBuildEnhancementRunner.BeginSession(request);
            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
                CaveBuildEnhancementRunner.RunHook(CaveBuildEnhancementCatalog.Hook.Preflight);
            CaveBuildPhaseContractRegistry.InvalidateCaveGeometryLadderRungs();
            StartQueuedPipeline();
        }

        static void FinishQueued(LavaTubeCaveBuildReport report)
        {
            var ctx = _queued;
            if (ctx?.Request != null)
            {
                CaveBuildHelperScriptOrchestrator.Queue(
                    CaveBuildHelperScriptOrchestrator.Moment.BuildComplete,
                    CaveBuildHelperScriptOrchestrator.MakeContext(ctx.Request));
            }

            if (ctx?.Request != null)
            {
                CaveBuildEnhancementRunner.RunHook(
                    CaveBuildEnhancementCatalog.Hook.OnFinalize,
                    ctx.CaveRoot);
                CaveBuildEnhancementRunner.RunHook(
                    CaveBuildEnhancementCatalog.Hook.PostPipeline,
                    ctx.CaveRoot);
                CaveBuildCompletionContract.Evaluate(ctx.Request, ctx.Ground, report);
            }

            CaveBuildPipelineScope.Clear();
            EnvironmentKitHardwareBudget.EndEditorSession();
            CaveBuildLiveSceneFeedback.EndBuildSession();
            CaveBuildWorkflowCoordinator.EndSession();
            CaveBuildPipelineCompletion.OnFullPipelineFinished();
            ResetResumeAfterAgentArmed();
            CaveBuildAutomatedFullWorldBootstrap.ClearSession();
            CaveBuildTerrainCursorDeferred.TryInvokeAfterCavePipeline();
            _queued = null;
            if (ctx?.ShowProgress == true)
                EditorUtility.ClearProgressBar();

            var finalReport = report ?? new LavaTubeCaveBuildReport { Message = "Queued build failed." };
            if (CaveBuildBatchRunner.TryHandleBuildComplete(
                    finalReport,
                    _ => ctx?.OnComplete?.Invoke(finalReport)))
            {
                return;
            }

            ctx?.OnComplete?.Invoke(finalReport);
        }
    }
}
