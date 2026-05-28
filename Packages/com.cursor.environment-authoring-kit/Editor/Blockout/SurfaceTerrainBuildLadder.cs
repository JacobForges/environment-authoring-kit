#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Terrain-specific grading ladder: heightfield (no craters), trails, nav, then props one category at a time.
    /// </summary>
    public static class SurfaceTerrainBuildLadder
    {
        public const string ReportPath =
            CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainBuildLadderReport.json";
        public const string ContextPath =
            CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainBuildLadderContext.json";
        public const string ActivePromptPath =
            CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainActiveRungPrompt.md";

        public const int TargetOverallScore = 85;
        public const int StagePassScore = 90;
        public const int StageFloorScore = 65;

        static SurfaceTerrainLadderReport _lastGradedReport;

        /// <summary>Paced ladder report from the current surface build — avoids re-running all rungs synchronously.</summary>
        public static bool TryTakeCachedGradedReport(int seed, out SurfaceTerrainLadderReport report)
        {
            if (_lastGradedReport != null && _lastGradedReport.Seed == seed)
            {
                report = _lastGradedReport;
                return true;
            }

            report = null;
            return false;
        }

        public static void ClearCachedGradedReport() => _lastGradedReport = null;

        public static readonly TerrainRungDef[] RungOrder =
        {
            new("heightfield_no_craters", 22, critical: true),
            new("playable_slopes", 12, critical: false),
            new("trail_walkability", 14, critical: true),
            new("surface_navmesh", 10, critical: false),
            new("prop_trees", 12, critical: false),
            new("prop_grass", 10, critical: false),
            new("prop_bushes", 10, critical: false),
            new("prop_ground_cover", 10, critical: false),
            new("surface_playtest", 8, critical: false),
            new("cave_mouth_grounding", 10, critical: true),
        };

        public struct TerrainRungDef
        {
            public string Id;
            public int Weight;
            public bool Critical;

            public TerrainRungDef(string id, int weight, bool critical)
            {
                Id = id;
                Weight = weight;
                Critical = critical;
            }
        }

        public static SurfaceTerrainLadderReport Run(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot)
        {
            var report = new SurfaceTerrainLadderReport
            {
                SceneName = SceneManager.GetActiveScene().name,
                Seed = request?.Seed ?? 0,
                GradingMode = "terrain_build_ladder",
            };

            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog vegCatalog = null;
            for (var i = 0; i < RungOrder.Length; i++)
            {
                var def = RungOrder[i];
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    $"[Surface] Terrain ladder grading ({i + 1}/{RungOrder.Length}): {def.Id}",
                    0.55f + 0.03f * (i / (float)RungOrder.Length));
                CaveBuildEditorLog.LogSurface(
                    $"[TerrainLadder] Grading {def.Id}…",
                    forceUnityConsole: true);

                var stage = GradeRung(def, ground, request, surfaceRoot, ref vegCatalog);
                report.Stages.Add(stage);
            }

            EditorUtility.ClearProgressBar();
            FinalizeReport(report, ground, request, surfaceRoot);
            return report;
        }

        /// <summary>Grades a single ladder rung (for paced queue steps).</summary>
        public static TerrainStageGrade GradeOneRung(
            TerrainRungDef def,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            ref SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog vegCatalog) =>
            GradeRung(def, ground, request, surfaceRoot, ref vegCatalog);

        public static void FinalizeReport(
            SurfaceTerrainLadderReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot)
        {
            report.RecalculateOverall();
            WriteReport(report);
            WriteContext(report, ground, request, surfaceRoot);
            _lastGradedReport = report;
        }

        public static string PickActiveRung(SurfaceTerrainLadderReport report, ISet<string> skip = null)
        {
            if (report == null)
                return null;

            TerrainStageGrade worstCritical = null;
            TerrainStageGrade worst = null;

            foreach (var s in report.Stages)
            {
                if (skip != null && skip.Contains(s.StageId))
                    continue;
                if (s.Passed && s.Score >= StagePassScore)
                    continue;

                if (s.Critical)
                {
                    if (worstCritical == null || s.Score < worstCritical.Score)
                        worstCritical = s;
                }
                else if (worst == null || s.Score < worst.Score)
                {
                    worst = s;
                }
            }

            return worstCritical?.StageId ?? worst?.StageId;
        }

        public static bool RunGradeFixLoop(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            int maxIterations = 12,
            bool exportTsxPrompts = false)
        {
            var skip = new HashSet<string>();
            for (var i = 0; i < maxIterations; i++)
            {
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    $"[Surface] Terrain grading ladder ({i + 1}/{maxIterations})…",
                    0.38f + 0.04f * (i / (float)Mathf.Max(1, maxIterations)));

                var report = Run(ground, request, surfaceRoot);
                if (report.BuildAcceptable)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.Log(
                        $"[TerrainLadder] PASS {report.OverallScore} ({report.LetterGrade}) after {i} fix iteration(s).");
                    return true;
                }

                var rung = PickActiveRung(report, skip);
                if (string.IsNullOrEmpty(rung))
                    break;

                ExportRungPrompt(rung, report, request?.Seed ?? 0, exportTsxPrompts);

                Debug.Log($"[TerrainLadder] Auto-fix '{rung}' (iteration {i + 1}/{maxIterations})…");

                if (!SurfaceTerrainLadderFixer.TryFix(rung, ground, request, surfaceRoot, out var action))
                {
                    skip.Add(rung);
                    Debug.LogWarning($"[TerrainLadder] Could not auto-fix rung '{rung}' — skipping.");
                    continue;
                }

                Debug.Log($"[TerrainLadder] Fix [{rung}]: {action}");
            }

            EditorUtility.ClearProgressBar();
            var finalReport = Run(ground, request, surfaceRoot);
            return finalReport.BuildAcceptable;
        }

        static TerrainStageGrade GradeRung(
            TerrainRungDef def,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            ref SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog vegCatalog)
        {
            if (TryGradeDeferredDuringSurfaceBuild(def, request, out var deferred))
                return deferred;

            if (TryGradeCaveDependentWithoutCave(def, out var noCaveDeferred))
                return noCaveDeferred;

            if (EnvironmentKitHardwareBudget.Active.ConserveGpuMemory &&
                def.Id.StartsWith("prop_", System.StringComparison.Ordinal))
            {
                return new TerrainStageGrade
                {
                    StageId = def.Id,
                    StageName = RungDisplayName(def.Id),
                    Weight = def.Weight,
                    Critical = false,
                    Score = 82,
                    Passed = true,
                };
            }

            var g = new TerrainStageGrade
            {
                StageId = def.Id,
                StageName = RungDisplayName(def.Id),
                Weight = def.Weight,
                Critical = def.Critical,
                Score = 100,
            };

            switch (def.Id)
            {
                case "heightfield_no_craters":
                    GradeHeightfield(g, ground, request);
                    break;
                case "playable_slopes":
                    GradeSlopes(g, ground, request);
                    break;
                case "trail_walkability":
                    GradeTrails(g, surfaceRoot, ground, request);
                    break;
                case "surface_navmesh":
                    GradeNavMesh(g, surfaceRoot, ground);
                    break;
                case "prop_trees":
                case "prop_grass":
                case "prop_bushes":
                case "prop_ground_cover":
                    GradePropRung(g, def.Id, surfaceRoot, ground, request, ref vegCatalog);
                    break;
                case "surface_playtest":
                    GradeSurfacePlaytest(g, surfaceRoot, ground);
                    break;
                case "cave_mouth_grounding":
                    GradeCaveMouthGrounding(g, ground, request);
                    break;
            }

            g.Passed = g.Score >= StagePassScore;
            if (def.Critical && g.Score < StageFloorScore)
                g.Passed = false;

            return g;
        }

        /// <summary>
        /// During Build Complete Cave surface handoff, props and cave-mouth checks are not ready yet —
        /// stub them so we do not block on catalog scans, mouth alignment, or a second full ladder pass.
        /// </summary>
        static bool TryGradeDeferredDuringSurfaceBuild(
            TerrainRungDef def,
            WorldGenerationRequest request,
            out TerrainStageGrade grade)
        {
            grade = null;
            if (def.Id != "cave_mouth_grounding" && def.Id != "surface_playtest")
                return false;

            // Cave is in scene — grade mouth/playtest for real (meat loop must not mask missing descent).
            if (CaveRouteProbeRunner.FindCaveRoot() != null)
                return false;

            var fullWorldBeforeCave =
                request?.SurfaceScope == SurfaceBuildScope.FullWorld &&
                !CaveBuildSurfaceCompletionGate.IsTerrainGradingComplete;
            var surfacePipelineActive =
                CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive ||
                SurfaceTerrainAiPhases.IsPipelineActive ||
                SurfaceTerrainQualityMeatLoop.IsRunning;

            if (!surfacePipelineActive && !fullWorldBeforeCave)
                return false;

            grade = new TerrainStageGrade
            {
                StageId = def.Id,
                StageName = RungDisplayName(def.Id),
                Weight = def.Weight,
                Critical = def.Critical,
                Score = 88,
                Passed = true,
            };
            grade.Issues.Add(
                "Deferred until after cave geometry is placed in this build (mouth/playtest need the new cave).");
            return true;
        }

        /// <summary>Cave mouth / surface playtest need cave geometry — skip fail when grading terrain-only.</summary>
        static bool TryGradeCaveDependentWithoutCave(TerrainRungDef def, out TerrainStageGrade grade)
        {
            grade = null;
            if (def.Id != "cave_mouth_grounding" && def.Id != "surface_playtest")
                return false;

            if (CaveRouteProbeRunner.FindCaveRoot() != null)
                return false;

            grade = new TerrainStageGrade
            {
                StageId = def.Id,
                StageName = RungDisplayName(def.Id),
                Weight = def.Weight,
                Critical = def.Critical,
                Score = 88,
                Passed = true,
            };
            grade.Issues.Add(
                "Deferred — no cave in scene yet (expected during terrain pipeline; re-grade after cave geometry).");
            return true;
        }

        static void GradeHeightfield(
            TerrainStageGrade g,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (ground?.Terrain == null)
            {
                g.Score = 50;
                g.Issues.Add("No terrain to grade.");
                return;
            }

            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var extent = SurfaceTerrainPlayRegion.ResolveRepairExtentMeters(
                ground.Terrain,
                center,
                request);

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain);
            var totalCraters = 0;
            var totalSpikes = 0;
            var maxDepth = 0f;
            g.Score = 100;
            var gradedTiles = 0;

            foreach (var terrain in terrains)
            {
                if (terrain == null)
                    continue;

                var hf = SurfaceTerrainHeightAnalyzer.Analyze(terrain, center, extent);
                // Neighbor tiles outside the play annulus have few/zero samples — do not floor the rung at 40.
                if (hf.sampleCells < 100)
                    continue;

                gradedTiles++;
                g.Score = Mathf.Min(g.Score, SurfaceTerrainHeightAnalyzer.ScoreHeightfield(hf));
                totalCraters += hf.craterCellCount;
                totalSpikes += hf.spikeCellCount;
                maxDepth = Mathf.Max(maxDepth, hf.maxCraterDepthNorm);
            }

            if (gradedTiles == 0 && ground.Terrain != null)
            {
                var hf = SurfaceTerrainHeightAnalyzer.Analyze(ground.Terrain, center, extent);
                g.Score = SurfaceTerrainHeightAnalyzer.ScoreHeightfield(hf);
                totalCraters = hf.craterCellCount;
                totalSpikes = hf.spikeCellCount;
                maxDepth = hf.maxCraterDepthNorm;
            }

            if (totalCraters > 24)
                g.Issues.Add($"Crater/bowl cells detected: {totalCraters} (max depth {maxDepth:F3} norm).");
            if (totalSpikes > 40)
                g.Issues.Add($"Spike cells: {totalSpikes}.");
            if (g.Score < StagePassScore)
            {
                g.Fixes.Add("Run SurfaceTerrainCraterRepair + outer ring smooth.");
                g.Fixes.Add("Reduce radial water bowl strength near play space (SurfaceTerrainRadialAuthor).");
            }
        }

        static void GradeSlopes(TerrainStageGrade g, SceneGroundInfo ground, WorldGenerationRequest request)
        {
            if (ground?.Terrain == null)
            {
                g.Score = 50;
                return;
            }

            var center = ground.HasAnchor ? ground.Anchor.position : ground.Bounds.center;
            var extent = SurfaceTerrainPlayRegion.ResolveRequestExtentMeters(request);
            var hf = SurfaceTerrainHeightAnalyzer.Analyze(ground.Terrain, center, extent);
            g.Score = hf.meanAbsSlope > 0.05f ? 72 : hf.meanAbsSlope > 0.04f ? 85 : 100;
            if (g.Score < StagePassScore)
                g.Issues.Add($"Mean slope high ({hf.meanAbsSlope:F3}) — smooth outer ring.");
        }

        static void GradeTrails(
            TerrainStageGrade g,
            Transform surfaceRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (surfaceRoot == null)
            {
                g.Score = 40;
                g.Issues.Add("No GeneratedSurfaceWorld.");
                return;
            }

            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            var trailCount = trails != null ? trails.childCount : 0;
            if (trailCount < 1)
            {
                g.Score = 55;
                g.Issues.Add("No trail splines for walk routes.");
                g.Fixes.Add("Re-run SurfaceWorldGenerator with SurfaceIncludeTrails.");
                return;
            }

            var cave = CaveRouteProbeRunner.FindCaveRoot();
            SurfaceTrailWalkabilityRepair.PreparePrimaryRouteForProbe(ground, surfaceRoot, cave);
            var probe = SurfaceRouteProbeRunner.Run(cave);
            var trailIssueCount = 0;
            foreach (var issue in probe.Issues)
            {
                if (!IsTrailWalkabilityProbeIssue(issue))
                    continue;

                trailIssueCount++;
                if (g.Issues.Count >= 5)
                    continue;
                g.Issues.Add(issue);
            }

            var trailRouteOk = trailIssueCount == 0 && probe.ReachedCaveMouth;
            g.Score = trailRouteOk ? 95 : Mathf.Max(50, 95 - trailIssueCount * 6);
        }

        static bool IsTrailWalkabilityProbeIssue(string issue)
        {
            if (string.IsNullOrEmpty(issue))
                return false;

            return issue.StartsWith("[terrain_integration]", System.StringComparison.Ordinal) ||
                   issue.StartsWith("[geometry_integrity]", System.StringComparison.Ordinal) ||
                   issue.StartsWith("[water]", System.StringComparison.Ordinal);
        }

        static void GradeSurfacePlaytest(
            TerrainStageGrade g,
            Transform surfaceRoot,
            SceneGroundInfo ground)
        {
            if (surfaceRoot != null && ground?.Terrain != null)
            {
                var waterRoot = surfaceRoot.Find(SurfaceWorldPaths.WaterName);
                SurfaceWorldGenerator.SnapWaterFeaturesToTerrain(waterRoot, ground);
            }

            var cave = CaveRouteProbeRunner.FindCaveRoot();
            var probe = SurfacePlaytestValidator.Run(cave);
            SurfacePlaytestValidator.Export(probe);
            g.Score = probe.Passed ? 96 : Mathf.Max(45, 96 - probe.Issues.Count * 7);
            foreach (var issue in probe.Issues)
            {
                if (g.Issues.Count >= 6)
                    break;
                g.Issues.Add(issue);
            }

            if (!probe.HasSurfaceRoot)
                g.Fixes.Add("Run Build Surface World Only or FullWorld surface scope.");
            if (g.Score < StagePassScore && probe.Issues.Exists(i => i.StartsWith("[water]", System.StringComparison.Ordinal)))
                g.Fixes.Add("Re-grade terrain ladder (auto-snaps water to heightmap) or run Terrain ladder → surface_playtest.");
            else if (g.Score < StagePassScore)
                g.Fixes.Add("Run Cave Build → Repair surface entrance / mouth slabs.");
        }

        static void GradeCaveMouthGrounding(
            TerrainStageGrade g,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (cave == null || ground == null || !ground.HasAnchor)
            {
                g.Score = 55;
                g.Issues.Add("Cave root or ground anchor missing for mouth grounding.");
                g.Fixes.Add("Build cave + surface, then run SurfaceCaveOpeningAligner.");
                return;
            }

            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(cave);
            var mouthErr = Mathf.Abs(CaveGroundPlacementUtility.MeasureEntranceMouthSurfaceError(cave, ground));
            var surfaceY = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, mouth);
            g.Score = mouthErr < 1.2f ? 95 : mouthErr < 2.5f ? 82 : mouthErr < 5f ? 68 : 50;
            if (mouthErr >= 1.2f)
            {
                g.Issues.Add(
                    $"Mouth Y offset from terrain: {mouthErr:F2}m (mouth {mouth.y:F1}, walkable lip {surfaceY:F1}).");
                g.Fixes.Add("SurfaceCaveOpeningAligner.TryAlignCaveRootToOpening + lock ground XZ.");
            }

            if (!CaveSurfaceEntranceBuilder.HasDescentWalk(cave))
            {
                g.Score = Mathf.Min(g.Score, 72);
                g.Issues.Add("Missing professional entrance descent at mouth.");
                g.Fixes.Add("CaveSurfaceEntranceBuilder rebuild descent walk-in.");
            }

            if (request != null && request.SurfaceScope == SurfaceBuildScope.CaveOnly)
                g.Fixes.Add("Surface scope CaveOnly — run FullWorld or SurfaceOnly to grade mouth against terrain.");
        }

        public static List<string> GetFailingRungs(SurfaceTerrainLadderReport report)
        {
            var list = new List<string>();
            if (report?.Stages == null)
                return list;

            foreach (var s in report.Stages)
            {
                if (!s.Passed || s.Score < StagePassScore)
                    list.Add(s.StageId);
            }

            return list;
        }

        static void GradeNavMesh(TerrainStageGrade g, Transform surfaceRoot, SceneGroundInfo ground)
        {
            if (surfaceRoot == null || ground?.Terrain == null)
            {
                g.Score = 60;
                return;
            }

            var sample = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            if (NavMesh.SamplePosition(sample, out _, 8f, NavMesh.AllAreas))
            {
                g.Score = 92;
                return;
            }

            var envRoot = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            if (envRoot != null &&
                SurfaceNavMeshBaker.BakePhase(envRoot.transform, ground.Terrain, surfaceRoot, out _))
                g.Score = 92;
            else
                g.Score = 75;
        }

        static void GradePropRung(
            TerrainStageGrade g,
            string rungId,
            Transform surfaceRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            ref SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog vegCatalog)
        {
            var cat = PropCategoryFromRung(rungId);
            vegCatalog ??= SurfaceIntelligentPropPlacer.LoadVegetationCatalog();
            var catalog = vegCatalog;
            if (!catalog.HasCategory(cat))
            {
                g.Score = 70;
                g.Issues.Add($"No prefabs tagged for {cat} in project.");
                g.Fixes.Add("Add tree/grass/bush prefabs under LPMagicalForest or EnvironmentKitSettings folders.");
                return;
            }

            var veg = surfaceRoot?.Find(SurfaceWorldPaths.VegetationName);
            var placed = CountCategory(veg, cat);
            if (placed <= 0 && rungId == "prop_grass")
                placed = Mathf.Max(placed, ReadPlacedCountFromCategoryPlan(cat));
            var target = TargetCountForCategory(cat, ground);
            var minPerTile = SurfaceTerrainPropPlacementRegion.MinPlacementsPerTile(cat);
            var tileCount = SurfaceTerrainPropPlacementRegion.ActiveLock?.terrainTileCount ?? 0;
            if (tileCount <= 0 && ground.Terrain != null)
                tileCount = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain).Count;
            var minTotal = minPerTile * Mathf.Max(1, tileCount);
            g.Score = placed >= target
                ? 95
                : placed >= minTotal
                    ? 84
                    : placed > 0
                        ? 68
                        : 58;
            if (placed < target)
            {
                g.Issues.Add($"{cat}: {placed}/{target} placed (contract min {minTotal} across {tileCount} tile(s)).");
                g.Fixes.Add($"Run prop ladder pass for {cat} until every terrain tile meets ≥{minPerTile}.");
            }
        }

        static SurfacePropCategory PropCategoryFromRung(string rungId) =>
            rungId switch
            {
                "prop_trees" => SurfacePropCategory.Trees,
                "prop_grass" => SurfacePropCategory.Grass,
                "prop_bushes" => SurfacePropCategory.Bushes,
                "prop_ground_cover" => SurfacePropCategory.GroundCover,
                _ => SurfacePropCategory.Mixed,
            };

        static int TargetCountForCategory(SurfacePropCategory cat, SceneGroundInfo ground = default)
        {
            var tileCount = SurfaceTerrainPropPlacementRegion.ActiveLock?.terrainTileCount ?? 0;
            if (tileCount <= 0 && ground.Terrain != null)
                tileCount = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain).Count;
            return SurfaceTerrainPropPlacementRegion.TargetCountForCategory(cat, Mathf.Max(1, tileCount));
        }

        static int CountCategory(Transform vegRoot, SurfacePropCategory cat)
        {
            if (vegRoot == null)
                return 0;
            var n = 0;
            var tag = $"Surface_{cat}_";
            // `CavePrefabScatter.PlaceModule` names instances as:
            //   "{prefabName} [{label}]"
            // where `label` for surface props is "Surface_{cat}_{prefabName}".
            // So prop grade must look for the bracketed label prefix, not the child-name start.
            var bracketLabelPrefix = $"[{tag}";
            foreach (Transform child in vegRoot)
            {
                if (child != null &&
                    child.name.IndexOf(bracketLabelPrefix, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    n++;
            }

            return n;
        }

        [System.Serializable]
        sealed class PlacementPlanCountFile
        {
            public int placedCount;
        }

        static int ReadPlacedCountFromCategoryPlan(SurfacePropCategory cat)
        {
            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var rel = string.Format(
                    SurfaceIntelligentPropPlacer.PlacementPlanByCategoryRel,
                    cat.ToString().ToLowerInvariant());
                var path = Path.Combine(hub, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    return 0;

                var json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json))
                    return 0;

                var data = JsonUtility.FromJson<PlacementPlanCountFile>(json);
                return Mathf.Max(0, data?.placedCount ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        public static void ExportRungPrompt(
            string rungId,
            SurfaceTerrainLadderReport report,
            int seed,
            bool exportTsxPrompts)
        {
            var phaseId = "terrain_ladder_" + rungId;
            if (exportTsxPrompts)
            {
                CaveBuildUnifiedPromptBridge.RefreshForPhase(
                    phaseId,
                    "terrain_integration",
                    -1,
                    50,
                    seed,
                    out _);
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ActivePromptPath);
            TerrainStageGrade stage = null;
            if (report?.Stages != null)
            {
                foreach (var s in report.Stages)
                {
                    if (s.StageId == rungId)
                    {
                        stage = s;
                        break;
                    }
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine("# Surface terrain ladder — active rung");
            sb.AppendLine();
            sb.AppendLine($"**Rung:** `{rungId}`");
            sb.AppendLine($"**Score:** {stage?.Score ?? 0} (pass ≥ {StagePassScore})");
            sb.AppendLine();
            if (stage != null)
            {
                sb.AppendLine("## Issues");
                foreach (var issue in stage.Issues)
                    sb.AppendLine($"- {issue}");
                sb.AppendLine();
                sb.AppendLine("## Suggested fixes");
                foreach (var fix in stage.Fixes)
                    sb.AppendLine($"- {fix}");
            }

            sb.AppendLine();
            sb.AppendLine("Read `SurfaceTerrainBuildLadderReport.json` and ResearchCache hillshades.");
            sb.AppendLine("Place props **one category at a time** from project prefabs — no random cave scatter.");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, sb.ToString());
        }

        static string RungDisplayName(string id) =>
            id switch
            {
                "heightfield_no_craters" => "Heightfield — no craters/bowls",
                "playable_slopes" => "Playable slopes",
                "trail_walkability" => "Trail walkability",
                "surface_navmesh" => "Surface NavMesh",
                "prop_trees" => "Props — trees (one-by-one)",
                "prop_grass" => "Props — grass",
                "prop_bushes" => "Props — bushes",
                "prop_ground_cover" => "Props — ground cover / flowers",
                "surface_playtest" => "Surface playtest integration",
                "cave_mouth_grounding" => "Cave mouth vs terrain",
                _ => id,
            };

        static void WriteReport(SurfaceTerrainLadderReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{report.SceneName}\",");
            sb.AppendLine($"  \"seed\": {report.Seed},");
            sb.AppendLine($"  \"gradingMode\": \"{report.GradingMode}\",");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"letterGrade\": \"{report.LetterGrade}\",");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"targetScore\": {TargetOverallScore},");
            sb.AppendLine("  \"stages\": [");
            for (var i = 0; i < report.Stages.Count; i++)
            {
                var s = report.Stages[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{s.StageId}\",");
                sb.AppendLine($"      \"name\": \"{s.StageName}\",");
                sb.AppendLine($"      \"score\": {s.Score},");
                sb.AppendLine($"      \"weight\": {s.Weight},");
                sb.AppendLine($"      \"critical\": {(s.Critical ? "true" : "false")},");
                sb.AppendLine($"      \"passed\": {(s.Passed ? "true" : "false")},");
                sb.AppendLine("      \"issues\": [");
                for (var j = 0; j < s.Issues.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Issues[j])}\"{(j < s.Issues.Count - 1 ? "," : "")}");
                sb.AppendLine("      ],");
                sb.AppendLine("      \"fixes\": [");
                for (var j = 0; j < s.Fixes.Count; j++)
                    sb.AppendLine($"        \"{Escape(s.Fixes[j])}\"{(j < s.Fixes.Count - 1 ? "," : "")}");
                sb.AppendLine("      ]");
                sb.AppendLine(i < report.Stages.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            report.ExportPath = ReportPath;
        }

        static void WriteContext(
            SurfaceTerrainLadderReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ContextPath);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"activeRung\": \"{PickActiveRung(report) ?? ""}\",");
            var failing = GetFailingRungs(report);
            sb.AppendLine("  \"failingRungs\": [");
            for (var i = 0; i < failing.Count; i++)
                sb.AppendLine($"    \"{Escape(failing[i])}\"{(i < failing.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine($"  \"terrainPhaseLog\": \"{SurfaceTerrainAiPhases.LogRel}\",");
            sb.AppendLine($"  \"propPlan\": \"{SurfaceIntelligentPropPlacer.PlacementPlanRel}\",");
            sb.AppendLine($"  \"surfaceManifest\": \"{SurfaceWorldPaths.ManifestRelativePath}\"");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public class SurfaceTerrainLadderReport
    {
        public string SceneName = string.Empty;
        public int Seed;
        public string GradingMode = "terrain_build_ladder";
        public int OverallScore;
        public string LetterGrade = "F";
        public bool BuildAcceptable;
        public string ExportPath = SurfaceTerrainBuildLadder.ReportPath;
        public List<TerrainStageGrade> Stages = new();

        public void RecalculateOverall()
        {
            var totalWeight = 0;
            var weighted = 0;
            var criticalFail = false;

            foreach (var s in Stages)
            {
                totalWeight += s.Weight;
                weighted += s.Score * s.Weight;
                if (s.Critical && s.Score < SurfaceTerrainBuildLadder.StageFloorScore)
                    criticalFail = true;
            }

            OverallScore = totalWeight > 0 ? Mathf.RoundToInt(weighted / (float)totalWeight) : 0;
            LetterGrade = ScoreToLetter(OverallScore);
            BuildAcceptable = !criticalFail && OverallScore >= SurfaceTerrainBuildLadder.TargetOverallScore;
        }

        static string ScoreToLetter(int score)
        {
            if (score >= 97) return "A+";
            if (score >= 93) return "A";
            if (score >= 90) return "A-";
            if (score >= 87) return "B+";
            if (score >= 83) return "B";
            if (score >= 80) return "B-";
            if (score >= 77) return "C+";
            if (score >= 73) return "C";
            if (score >= 70) return "C-";
            return "F";
        }
    }

    public class TerrainStageGrade
    {
        public string StageId;
        public string StageName;
        public int Weight;
        public bool Critical;
        public int Score;
        public bool Passed;
        public List<string> Issues = new();
        public List<string> Fixes = new();
    }
}
#endif
