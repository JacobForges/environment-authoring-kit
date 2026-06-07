#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Hollow Titan landmark rubric ladder — aligned with meat phases + hollow-titan-pipeline-phases.ts.</summary>
    public static class HollowTitanBuildLadder
    {
        public const string ReportPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanBuildLadderReport.json";
        public const string ActivePromptPath =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanActiveRungPrompt.md";

        public const int TargetOverallScore = 85;
        public const int StagePassScore = 88;
        public const int StageFloorScore = 62;

        /// <summary>Concept art baseline trunk radius × 600% display scale.</summary>
        public const float ConceptTrunkRadiusMeters = 30f;
        public const float TitanDisplayScaleMultiplier = 7.5f;

        public static readonly HollowTitanRungDef[] RungOrder =
        {
            new("hollow_titan_site_pick", 10, critical: true),
            new("hollow_titan_base_snap", 6, critical: false),
            new("hollow_titan_exterior_stump", 14, critical: true),
            new("hollow_titan_hollow_carve", 8, critical: false),
            new("hollow_titan_floor_plates", 14, critical: true),
            new("hollow_titan_stair_spiral", 12, critical: true),
            new("hollow_titan_entrance_framing", 8, critical: false),
            new("hollow_titan_per_floor_spawns", 14, critical: true),
            new("hollow_titan_lighting_fog", 6, critical: false),
            new("hollow_titan_landmark_loot", 8, critical: false),
            new("hollow_titan_enemy_patrol", 10, critical: false),
        };

        public struct HollowTitanRungDef
        {
            public string Id;
            public int Weight;
            public bool Critical;

            public HollowTitanRungDef(string id, int weight, bool critical)
            {
                Id = id;
                Weight = weight;
                Critical = critical;
            }
        }

        public static HollowTitanLadderReport Run(
            Transform landmarkRoot,
            Terrain mainTerrain,
            WorldGenerationRequest request)
        {
            var report = new HollowTitanLadderReport
            {
                SceneName = SceneManager.GetActiveScene().name,
                Seed = request?.Seed ?? 0,
                GradingMode = "hollow_titan_build_ladder",
            };

            for (var i = 0; i < RungOrder.Length; i++)
            {
                var def = RungOrder[i];
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    $"[HollowTitan] Ladder grading ({i + 1}/{RungOrder.Length}): {def.Id}",
                    (i + 1) / (float)RungOrder.Length);
                report.Stages.Add(GradeOneRung(def, landmarkRoot, mainTerrain, request));
            }

            EditorUtility.ClearProgressBar();
            FinalizeReport(report);
            return report;
        }

        public static TerrainStageGrade GradeOneRung(
            HollowTitanRungDef def,
            Transform landmarkRoot,
            Terrain mainTerrain,
            WorldGenerationRequest request)
        {
            if (!CaveBuildGradedArtifactGate.TryBegin(CaveBuildGradedArtifactGate.Track.HollowTitan, def.Id))
            {
                return new TerrainStageGrade
                {
                    StageId = def.Id,
                    StageName = def.Id,
                    Score = 70,
                    Passed = true,
                    Issues = new List<string> { "Another graded task is active — deferred." },
                };
            }

            try
            {
                var g = new TerrainStageGrade
                {
                    StageId = def.Id,
                    StageName = def.Id,
                    Weight = def.Weight,
                    Critical = def.Critical,
                    Score = 100,
                };

                switch (def.Id)
                {
                    case "hollow_titan_site_pick":
                        GradeSitePick(g, landmarkRoot);
                        break;
                    case "hollow_titan_base_snap":
                        GradeBaseSnap(g, landmarkRoot, mainTerrain);
                        break;
                    case "hollow_titan_exterior_stump":
                        GradeExteriorStump(g, landmarkRoot);
                        break;
                    case "hollow_titan_hollow_carve":
                        GradeHollowCarve(g, landmarkRoot);
                        break;
                    case "hollow_titan_floor_plates":
                        GradeFloorPlates(g, landmarkRoot);
                        break;
                    case "hollow_titan_stair_spiral":
                        GradeStairSpiral(g, landmarkRoot);
                        break;
                    case "hollow_titan_entrance_framing":
                        GradeEntranceFraming(g, landmarkRoot);
                        break;
                    case "hollow_titan_per_floor_spawns":
                        GradePerFloorSpawns(g, landmarkRoot);
                        break;
                    case "hollow_titan_lighting_fog":
                        GradeLightingFog(g, landmarkRoot);
                        break;
                    case "hollow_titan_landmark_loot":
                        GradeLandmarkLoot(g, landmarkRoot, request);
                        break;
                    case "hollow_titan_enemy_patrol":
                        GradeEnemyPatrol(g, landmarkRoot);
                        break;
                }

                g.Passed = g.Score >= StagePassScore;
                if (def.Critical && g.Score < StageFloorScore)
                    g.Passed = false;

                if (g.Passed)
                    CaveBuildGradedArtifactGate.CompleteAndRelease(
                        CaveBuildGradedArtifactGate.Track.HollowTitan,
                        def.Id,
                        request?.Seed ?? 0);
                else
                    CaveBuildGradedArtifactGate.Release(CaveBuildGradedArtifactGate.Track.HollowTitan, def.Id);

                return g;
            }
            catch
            {
                CaveBuildGradedArtifactGate.Release(CaveBuildGradedArtifactGate.Track.HollowTitan, def.Id);
                throw;
            }
        }

        /// <summary>Maps meat phase to ladder rung id (null when phase has no rung).</summary>
        public static string RungIdForPhase(HollowTitanLandmarkMeatPhases.Phase phase) =>
            phase switch
            {
                HollowTitanLandmarkMeatPhases.Phase.SitePick => "hollow_titan_site_pick",
                HollowTitanLandmarkMeatPhases.Phase.BaseSnap => "hollow_titan_base_snap",
                HollowTitanLandmarkMeatPhases.Phase.TrunkShell => "hollow_titan_exterior_stump",
                HollowTitanLandmarkMeatPhases.Phase.HollowCarve => "hollow_titan_hollow_carve",
                HollowTitanLandmarkMeatPhases.Phase.FloorPlates => "hollow_titan_floor_plates",
                HollowTitanLandmarkMeatPhases.Phase.StairSpiral => "hollow_titan_stair_spiral",
                HollowTitanLandmarkMeatPhases.Phase.DeadBranchScatter => null,
                HollowTitanLandmarkMeatPhases.Phase.EntranceFraming => "hollow_titan_entrance_framing",
                HollowTitanLandmarkMeatPhases.Phase.PerFloorSpawnMarkers => "hollow_titan_per_floor_spawns",
                HollowTitanLandmarkMeatPhases.Phase.LightingFogMood => "hollow_titan_lighting_fog",
                HollowTitanLandmarkMeatPhases.Phase.LandmarkLootTable => "hollow_titan_landmark_loot",
                HollowTitanLandmarkMeatPhases.Phase.EnemyPatrolNodes => "hollow_titan_enemy_patrol",
                _ => null,
            };

        public static bool TryFindRungDef(string rungId, out HollowTitanRungDef def)
        {
            foreach (var r in RungOrder)
            {
                if (r.Id == rungId)
                {
                    def = r;
                    return true;
                }
            }

            def = default;
            return false;
        }

        public static void GradeAndLogPhase(
            HollowTitanLandmarkMeatPhases.Phase phase,
            Transform landmarkRoot,
            Terrain mainTerrain,
            WorldGenerationRequest request,
            HollowTitanLadderReport report)
        {
            var rungId = RungIdForPhase(phase);
            if (string.IsNullOrEmpty(rungId))
            {
                if (phase == HollowTitanLandmarkMeatPhases.Phase.DeadBranchScatter)
                {
                    CaveBuildEditorLog.LogSurface(
                        "[HollowTitan] dead_branch_scatter deprecated — exterior is terraform-only (no rung grade).",
                        forceUnityConsole: false);
                }

                return;
            }

            if (!TryFindRungDef(rungId, out var def))
                return;

            var grade = GradeOneRung(def, landmarkRoot, mainTerrain, request);
            UpsertStage(report, grade);

            var status = grade.Passed ? "PASS" : "FAIL";
            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Rung {rungId} — {status} score {grade.Score} (pass ≥ {StagePassScore}).",
                forceUnityConsole: !grade.Passed);

            if (!grade.Passed)
                ExportRungPrompt(rungId, request?.Seed ?? 0, grade);
        }

        public static void FinalizeReport(HollowTitanLadderReport report)
        {
            if (report == null)
                return;

            report.RecalculateOverall();
            WriteReport(report);
        }

        public static string PickActiveRung(IReadOnlyList<TerrainStageGrade> stages, ISet<string> skip = null)
        {
            var map = new Dictionary<string, TerrainStageGrade>();
            if (stages != null)
            {
                foreach (var s in stages)
                {
                    if (s != null && !string.IsNullOrEmpty(s.StageId))
                        map[s.StageId] = s;
                }
            }

            foreach (var def in RungOrder)
            {
                if (skip != null && skip.Contains(def.Id))
                    continue;
                if (!map.TryGetValue(def.Id, out var g) || !g.Passed)
                    return def.Id;
            }

            return null;
        }

        public static void ExportRungPrompt(string rungId, int seed, TerrainStageGrade grade)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildUnifiedPromptBridge.RefreshForPhase(
                rungId,
                "ground_placement",
                -1,
                QueuedStepForRung(rungId),
                seed,
                out _);

            var sb = new StringBuilder();
            sb.AppendLine("# Hollow Titan ladder — active rung");
            sb.AppendLine();
            sb.AppendLine($"**Rung:** `{rungId}` | **Seed:** {seed}");
            if (grade != null)
            {
                sb.AppendLine($"**Score:** {grade.Score} | **Passed:** {grade.Passed} (pass ≥ {StagePassScore})");
                if (grade.Issues?.Count > 0)
                    sb.AppendLine($"**Issues:** {string.Join("; ", grade.Issues)}");
                if (grade.Fixes?.Count > 0)
                    sb.AppendLine($"**Fixes:** {string.Join("; ", grade.Fixes)}");
            }

            sb.AppendLine();
            sb.AppendLine(
                "ONE task only — read `hollow_titan_do_not` + `HollowTitanBuildLadderReport.json` before editing scene.");
            Directory.CreateDirectory(Path.Combine(hub, CaveBuildAgentContextExporter.Folder));
            File.WriteAllText(Path.Combine(hub, ActivePromptPath), sb.ToString());
        }

        [MenuItem("Window/Environment Kit/World/Grade Hollow Titan Landmark")]
        public static void GradeFromMenu()
        {
            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (root == null)
            {
                Debug.LogWarning("[HollowTitan] No HollowTitanLandmark in scene — run phased meat build first.");
                return;
            }

            var terrain = UnityEngine.Object.FindAnyObjectByType<Terrain>();
            var data = root.GetComponent<HollowTitanLandmarkBuildData>();
            var request = new WorldGenerationRequest
            {
                ContentTier = WorldBuildContentTier.Aaa,
                Seed = data != null ? data.BuildSeed : 0,
                SurfaceScope = SurfaceBuildScope.FullWorld,
            };

            var report = Run(root.transform, terrain, request);
            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Ladder grade complete — {report.OverallScore}/100 ({report.LetterGrade}), " +
                $"acceptable={report.BuildAcceptable}. Report: {ReportPath}",
                forceUnityConsole: true);
        }

        static int QueuedStepForRung(string rungId)
        {
            for (var i = 0; i < RungOrder.Length; i++)
            {
                if (RungOrder[i].Id == rungId)
                    return 60 + i;
            }

            return 60;
        }

        static void UpsertStage(HollowTitanLadderReport report, TerrainStageGrade stage)
        {
            if (report?.Stages == null || stage == null)
                return;

            for (var i = 0; i < report.Stages.Count; i++)
            {
                if (report.Stages[i].StageId == stage.StageId)
                {
                    report.Stages[i] = stage;
                    return;
                }
            }

            report.Stages.Add(stage);
        }

        static void WriteReport(HollowTitanLadderReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"scene\": \"{Escape(report.SceneName)}\",");
            sb.AppendLine($"  \"seed\": {report.Seed},");
            sb.AppendLine($"  \"gradingMode\": \"{Escape(report.GradingMode)}\",");
            sb.AppendLine($"  \"overallScore\": {report.OverallScore},");
            sb.AppendLine($"  \"letterGrade\": \"{report.LetterGrade}\",");
            sb.AppendLine($"  \"buildAcceptable\": {(report.BuildAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"targetScore\": {TargetOverallScore},");
            sb.AppendLine($"  \"stagePassScore\": {StagePassScore},");
            sb.AppendLine($"  \"stageFloorScore\": {StageFloorScore},");
            sb.AppendLine($"  \"activeRung\": \"{Escape(PickActiveRung(report.Stages) ?? "")}\",");
            sb.AppendLine($"  \"exportPath\": \"{ReportPath}\",");
            sb.AppendLine("  \"stages\": [");
            for (var i = 0; i < report.Stages.Count; i++)
            {
                var s = report.Stages[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{s.StageId}\",");
                sb.AppendLine($"      \"name\": \"{Escape(s.StageName)}\",");
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

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static HollowTitanLandmarkBuildData ResolveBuildData(Transform root) =>
            root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null;

        static void GradeSitePick(TerrainStageGrade g, Transform root)
        {
            if (root == null)
            {
                g.Score = 0;
                g.Issues.Add("HollowTitanLandmark root missing.");
                g.Fixes.Add("Run phased meat build from Window/Environment Kit/World/Build Hollow Titan.");
                return;
            }

            var data = ResolveBuildData(root);
            if (data == null)
            {
                g.Score = 42;
                g.Issues.Add("HollowTitanLandmarkBuildData missing on root.");
                g.Fixes.Add("Re-run site_pick phase — Configure build data on root.");
                return;
            }

            var expectedRadius = ConceptTrunkRadiusMeters * TitanDisplayScaleMultiplier;
            var radiusOk = data.TrunkRadius >= expectedRadius * 0.75f &&
                           data.TrunkRadius <= expectedRadius * 1.35f;
            var siteOk = data.SiteWorldPosition.sqrMagnitude > 1f;

            g.Score = radiusOk && siteOk && data.FloorCount >= 2 ? 98 : radiusOk ? 82 : 58;
            if (!siteOk)
            {
                g.Issues.Add("SiteWorldPosition unset or at origin.");
                g.Fixes.Add("Re-run site_pick — write anchor tile + site XZ into build data.");
            }

            if (!radiusOk)
            {
                g.Issues.Add(
                    $"Trunk radius {data.TrunkRadius:F1}m outside expected ~{expectedRadius:F0}m (ConceptTrunkRadius×6).");
                g.Fixes.Add("Check massive scale jitter on site_pick — target 600% concept display scale.");
            }
        }

        static void GradeBaseSnap(TerrainStageGrade g, Transform root, Terrain mainTerrain)
        {
            if (root == null)
            {
                g.Score = 0;
                g.Issues.Add("Root missing.");
                return;
            }

            if (mainTerrain == null)
            {
                g.Score = 75;
                g.Issues.Add("No terrain in scene — cannot verify snap.");
                return;
            }

            var pos = root.position;
            var groundY = mainTerrain.SampleHeight(pos) + mainTerrain.transform.position.y;
            var delta = Mathf.Abs(pos.y - groundY);
            var clearance = ResolveBuildData(root)?.BaseClearanceMeters ?? 0.35f;
            var maxDelta = clearance + 2.5f;

            g.Score = delta <= clearance + 0.5f ? 96 : delta <= maxDelta ? 84 : delta <= maxDelta * 2f ? 68 : 44;
            if (delta > maxDelta)
            {
                g.Issues.Add($"Root Y {pos.y:F2}m vs terrain {groundY:F2}m (Δ{delta:F2}m).");
                g.Fixes.Add("Re-run base_snap or Resnap Hollow Titan to terrain.");
            }
        }

        static void GradeExteriorStump(TerrainStageGrade g, Transform root)
        {
            if (root == null)
            {
                g.Score = 0;
                g.Issues.Add("Root missing.");
                return;
            }

            var exterior = root.Find("Exterior");
            var legacyProps = CountLegacyExteriorProps(exterior);
            var plateTrimCount = CountPlateTrim(root);
            var shell = root.Find("Exterior/TrunkShell");
            var hasBarkOuter = shell != null && shell.Find("BarkOuter") != null;
            var hasSculptMarker = shell != null &&
                                  shell.Find(HollowTitanExteriorTerraform.SculptMarkerName) != null;
            var hasShell = hasBarkOuter || hasSculptMarker;

            var data = ResolveBuildData(root);
            var expectedRadius = ConceptTrunkRadiusMeters * TitanDisplayScaleMultiplier;
            var scaleOk = data == null ||
                          (data.TrunkRadius >= expectedRadius * 0.75f &&
                           data.TrunkRadius <= expectedRadius * 1.35f);

            var score = 100;
            if (legacyProps > 0)
            {
                score -= 35;
                g.Issues.Add($"Legacy Branch_/Log_ props under Exterior: {legacyProps} (terraform-only stump).");
                g.Fixes.Add("Remove CC0 branch/log props — exterior is hollow stump bowl terraform + BarkOuter shell.");
            }

            if (plateTrimCount > 0)
            {
                score -= 40;
                g.Issues.Add($"Radial PlateTrim CC0 props: {plateTrimCount} (removed — use terrain stump only).");
                g.Fixes.Add("Window → Environment Kit → World → Strip Hollow Titan Legacy Props (radial trim).");
            }

            if (!hasShell)
            {
                score -= 40;
                g.Issues.Add("Missing BarkOuter collision shell and StumpTerraformDone marker.");
                g.Fixes.Add("Run trunk_shell phase + Refresh Exterior (terraform stump bowl).");
            }
            else if (!hasSculptMarker)
            {
                score -= 8;
                g.Issues.Add("StumpTerraformDone marker absent — collision shell only (terraform may be deferred).");
            }

            if (!scaleOk && data != null)
            {
                score -= 15;
                g.Issues.Add($"Trunk radius {data.TrunkRadius:F1}m off ConceptTrunkRadius×6 (~{expectedRadius:F0}m).");
            }

            g.Score = Mathf.Clamp(score, 0, 100);
        }

        static int CountPlateTrim(Transform root)
        {
            if (root == null)
                return 0;

            var floors = root.Find("Interior/Floors");
            if (floors == null)
                return 0;

            var count = 0;
            for (var f = 0; f < floors.childCount; f++)
            {
                var trim = floors.GetChild(f).Find("PlateTrim");
                if (trim == null)
                    continue;

                count += trim.childCount;
            }

            return count;
        }

        static int CountLegacyExteriorProps(Transform exterior)
        {
            if (exterior == null)
                return 0;

            var count = 0;
            CountLegacyPropsRecursive(exterior, ref count);
            return count;
        }

        static void CountLegacyPropsRecursive(Transform node, ref int count)
        {
            for (var i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                var n = child.name;
                if (n.StartsWith("Branch_", StringComparison.Ordinal) ||
                    n.StartsWith("Log_", StringComparison.Ordinal) ||
                    n.StartsWith("HollowTitan_L", StringComparison.Ordinal))
                    count++;

                CountLegacyPropsRecursive(child, ref count);
            }
        }

        static void GradeHollowCarve(TerrainStageGrade g, Transform root)
        {
            var volume = root != null ? root.Find("Interior/HollowVolume") : null;
            g.Score = volume != null ? 95 : 38;
            if (volume == null)
            {
                g.Issues.Add("Interior/HollowVolume missing.");
                g.Fixes.Add("Re-run hollow_carve phase — cylinder void marker ~62% trunk radius.");
            }
        }

        static void GradeFloorPlates(TerrainStageGrade g, Transform root)
        {
            var floorsRoot = root != null ? root.Find("Interior/Floors") : null;
            var data = ResolveBuildData(root);
            var expected = data != null ? data.FloorCount : CaveBuildCursorSettings.LoadOrCreate().hollowTitanFloorCount;
            var floorCount = 0;
            if (floorsRoot != null)
            {
                for (var i = 0; i < floorsRoot.childCount; i++)
                {
                    if (floorsRoot.GetChild(i).name.StartsWith("Floor_", StringComparison.Ordinal))
                        floorCount++;
                }
            }

            g.Score = floorCount >= expected ? 98 : floorCount >= expected - 1 ? 82 : floorCount >= 1 ? 64 : 36;
            if (floorCount < expected)
            {
                g.Issues.Add($"Floor plates {floorCount}/{expected} under Interior/Floors.");
                g.Fixes.Add("Re-run floor_plates — one Floor_XX per FloorPlan level.");
            }
        }

        static void GradeStairSpiral(TerrainStageGrade g, Transform root)
        {
            var stairsRoot = root != null ? root.Find("Interior/Stairs") : null;
            var stairCount = 0;
            if (stairsRoot != null)
            {
                for (var i = 0; i < stairsRoot.childCount; i++)
                {
                    if (stairsRoot.GetChild(i).name.StartsWith("Stair_", StringComparison.Ordinal))
                        stairCount++;
                }
            }

            g.Score = stairCount >= 12 ? 96 : stairCount >= 4 ? 86 : stairCount > 0 ? 72 : 40;
            if (stairCount == 0)
            {
                g.Issues.Add("No stair treads under Interior/Stairs.");
                g.Fixes.Add("Re-run stair_spiral — cube steps from FloorPlan.Stairs.");
            }
            else if (stairCount < 4)
            {
                g.Issues.Add($"Only {stairCount} stair treads — spiral may be incomplete.");
            }
        }

        static void GradeEntranceFraming(TerrainStageGrade g, Transform root)
        {
            var entrance = root != null ? root.Find("Exterior/Entrance") : null;
            var hasArch = entrance != null && entrance.Find("EntranceArch") != null;
            var hasOpening = entrance != null && entrance.Find("EntranceOpening") != null;

            g.Score = hasArch && hasOpening ? 94 : hasArch || hasOpening ? 78 : entrance != null ? 62 : 44;
            if (!hasArch || !hasOpening)
            {
                g.Issues.Add("EntranceArch and/or EntranceOpening missing under Exterior/Entrance.");
                g.Fixes.Add("Re-run entrance_framing along FloorPlan.EntranceForward.");
            }
        }

        static void GradePerFloorSpawns(TerrainStageGrade g, Transform root)
        {
            if (root == null)
            {
                g.Score = 0;
                g.Issues.Add("Root missing.");
                return;
            }

            var data = ResolveBuildData(root);
            var topFloor = data != null ? data.FloorCount - 1 : 6;
            var enemiesRoot = root.Find("Spawns/Enemies");
            var enemyMarkers = CollectSpawnPoints(enemiesRoot, HollowTitanSpawnKind.Enemy);
            var bossCount = 0;
            foreach (var m in enemyMarkers)
            {
                if (m.DefinitionId == HollowTitanLandmarkSpawnCatalog.BossRoleId)
                    bossCount++;
            }

            var hasSpawner = root.GetComponent<HollowTitanLandmarkSpawner>() != null;
            var hasPortal = root.Find("BossStagePortal") != null;
            var settingsPerFloor = HollowTitanLandmarkSpawnManifest.EnemiesPerFloor;
            var boulderCount = CountChildren(root.Find("Interior/BoulderPlatforms"));

            var score = 100;
            if (enemyMarkers.Count == 0)
            {
                score -= 45;
                g.Issues.Add("No enemy spawn markers under Spawns/Enemies.");
                g.Fixes.Add("Re-run per_floor_spawn_markers — one marker per boulder platform.");
            }
            else if (enemyMarkers.Count < boulderCount)
            {
                score -= 12;
                g.Issues.Add($"Enemy markers {enemyMarkers.Count} vs {boulderCount} boulder platforms.");
            }

            if (settingsPerFloor > 1 && enemyMarkers.Count < (topFloor + 1) * settingsPerFloor)
            {
                g.Issues.Add(
                    $"Note: hollowTitanEnemiesPerFloor={settingsPerFloor} not honored — " +
                    $"meat build uses one spawn per boulder platform ({enemyMarkers.Count} markers).");
            }

            if (bossCount == 0)
            {
                score -= 30;
                g.Issues.Add($"No {HollowTitanLandmarkSpawnCatalog.BossRoleId} on top floor (index {topFloor}).");
                g.Fixes.Add("Ensure top-floor boulder platform gets boss role via RoleForFloor.");
            }

            if (!hasSpawner)
            {
                score -= 20;
                g.Issues.Add("HollowTitanLandmarkSpawner missing on root.");
                g.Fixes.Add("Re-run per_floor_spawn_markers — EnsureLandmarkSpawner.");
            }

            if (!hasPortal)
            {
                score -= 15;
                g.Issues.Add("BossStagePortal child missing.");
                g.Fixes.Add("Re-run trunk_shell — EnsurePortal on root.");
            }

            g.Score = Mathf.Clamp(score, 0, 100);
        }

        static void GradeLightingFog(TerrainStageGrade g, Transform root)
        {
            var lightsRoot = root != null ? root.Find("Mood/Lights") : null;
            var fogVolume = root != null ? root.Find("Mood/FogVolume/FogMoodVolume") : null;
            var data = ResolveBuildData(root);
            var expectedFloors = data != null ? data.FloorCount : 4;
            var lightCount = lightsRoot != null ? lightsRoot.childCount : 0;

            var score = 100;
            if (lightCount < expectedFloors)
            {
                score -= lightCount == 0 ? 40 : 18;
                g.Issues.Add($"Mood lights {lightCount}/{expectedFloors} floors.");
                g.Fixes.Add("Re-run lighting_fog — one point light per floor under Mood/Lights.");
            }

            if (fogVolume == null)
            {
                score -= 22;
                g.Issues.Add("Mood/FogVolume/FogMoodVolume missing.");
                g.Fixes.Add("Re-run lighting_fog — interior fog volume marker.");
            }

            g.Score = Mathf.Clamp(score, 0, 100);
        }

        static void GradeLandmarkLoot(TerrainStageGrade g, Transform root, WorldGenerationRequest request)
        {
            var lootRoot = root != null ? root.Find("Spawns/Loot") : null;
            var lootMarkers = CollectSpawnPoints(lootRoot, HollowTitanSpawnKind.Loot);
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var manifestPath = Path.Combine(hub, HollowTitanLandmarkSpawnManifest.ManifestRel);
            var hasManifest = File.Exists(manifestPath);

            var score = 100;
            if (lootMarkers.Count == 0)
            {
                score -= 40;
                g.Issues.Add("No loot spawn markers under Spawns/Loot.");
                g.Fixes.Add("Re-run landmark_loot_table phase.");
            }

            if (!hasManifest)
            {
                score -= 25;
                g.Issues.Add("HollowTitanLandmarkSpawnManifest.json missing.");
                g.Fixes.Add("Re-run landmark_loot_table — HollowTitanLandmarkSpawnManifest.Write.");
            }

            var hookFound = false;
            foreach (var m in lootMarkers)
            {
                if (m.DefinitionId == HollowTitanLandmarkSpawnCatalog.GrapplingHookLootId)
                    hookFound = true;
            }

            if (!hookFound)
            {
                score -= 10;
                g.Issues.Add("Grappling hook loot marker missing on hook floor.");
            }

            g.Score = Mathf.Clamp(score, 0, 100);
        }

        static void GradeEnemyPatrol(TerrainStageGrade g, Transform root)
        {
            var patrolRoot = root != null ? root.Find("Spawns/Patrol") : null;
            var patrolMarkers = CollectSpawnPoints(patrolRoot, HollowTitanSpawnKind.Patrol);
            var data = ResolveBuildData(root);
            var floorCount = data != null ? data.FloorCount : CaveBuildCursorSettings.LoadOrCreate().hollowTitanFloorCount;
            var expected = floorCount * HollowTitanLandmarkSpawnManifest.PatrolNodesPerFloor;

            g.Score = patrolMarkers.Count >= expected ? 96 :
                patrolMarkers.Count >= expected * 0.5f ? 80 :
                patrolMarkers.Count > 0 ? 68 : 42;

            if (patrolMarkers.Count < expected)
            {
                g.Issues.Add($"Patrol nodes {patrolMarkers.Count}/{expected} ({floorCount} floors × patrol/floor).");
                g.Fixes.Add("Re-run enemy_patrol_nodes — ring at ~75% floor radius.");
            }
        }

        static List<HollowTitanLandmarkSpawnPoint> CollectSpawnPoints(
            Transform root,
            HollowTitanSpawnKind kind)
        {
            var list = new List<HollowTitanLandmarkSpawnPoint>();
            if (root == null)
                return list;

            root.GetComponentsInChildren(true, list);
            if (kind == HollowTitanSpawnKind.Enemy)
                list.RemoveAll(m => m.Kind != HollowTitanSpawnKind.Enemy);
            else if (kind == HollowTitanSpawnKind.Loot)
                list.RemoveAll(m => m.Kind != HollowTitanSpawnKind.Loot);
            else
                list.RemoveAll(m => m.Kind != HollowTitanSpawnKind.Patrol);

            return list;
        }

        static int CountChildren(Transform root) => root != null ? root.childCount : 0;
    }

    public class HollowTitanLadderReport
    {
        public string SceneName = string.Empty;
        public int Seed;
        public string GradingMode = "hollow_titan_build_ladder";
        public int OverallScore;
        public string LetterGrade = "F";
        public bool BuildAcceptable;
        public string ExportPath = HollowTitanBuildLadder.ReportPath;
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
                if (s.Critical && s.Score < HollowTitanBuildLadder.StageFloorScore)
                    criticalFail = true;
            }

            OverallScore = totalWeight > 0 ? Mathf.RoundToInt(weighted / (float)totalWeight) : 0;
            LetterGrade = ScoreToLetter(OverallScore);
            BuildAcceptable = !criticalFail && OverallScore >= HollowTitanBuildLadder.TargetOverallScore;
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
}
#endif
