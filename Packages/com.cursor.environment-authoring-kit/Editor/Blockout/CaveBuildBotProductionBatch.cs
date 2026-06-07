#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Production bot post-pass: compile → meat loop (multi-rung) → route probe → gates → JSON export.
    /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunBotProductionPostPass
    /// </summary>
    public static class CaveBuildBotProductionBatch
    {
        const int DefaultRungBudget = 5;
        const int DefaultMeatPasses = 8;
        const int PerformanceTriBudget = 800_000;
        const float RouteProbeMinPassPct = 0.95f;

        public static void Run()
        {
            TryOpenWorldScene();
            CaveBuildCompileGate.RequestRecompileAndExportDiagnostics();

            var caveRoot = FindCaveRoot();
            if (caveRoot == null)
            {
                Debug.LogError("[CaveBuild] Production post-pass: cave root not found.");
                EditorApplication.Exit(2);
                return;
            }

            if (CaveBuildCompileGate.HasBlockingErrors())
            {
                Debug.LogError("[CaveBuild] Production post-pass: compile errors remain.");
                EditorApplication.Exit(2);
                return;
            }

            var ground = SceneGroundResolver.Resolve();
            var request = ResolveRequest(caveRoot);
            var seed = request?.Seed ?? 0;
            var rungBudget = ReadEnvInt("CAVE_RUNG_BUDGET", DefaultRungBudget);
            var meatPasses = ReadEnvInt("CAVE_MEAT_LOOP_PASSES", DefaultMeatPasses);
            var scoreBefore = 0;

            var quality = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
            scoreBefore = quality.OverallScore;
            CaveBuildQualitySystem.SetLastGradedReport(quality);

            var fixesApplied = 0;
            var skipped = new HashSet<string>();

            for (var pass = 0; pass < meatPasses && fixesApplied < rungBudget; pass++)
            {
                var rung = CaveBuildPromptLadder.PickActiveRung(quality, caveRoot, ground);
                var stageId = MapRungToStage(rung, quality);
                if (string.IsNullOrEmpty(stageId) || skipped.Contains(stageId))
                    break;

                if (!CaveBuildQualityStageFixer.CanAutoFix(stageId) ||
                    !CaveBuildQualityStageFixer.TryFix(stageId, caveRoot, request, ground, seed, out var action))
                {
                    skipped.Add(stageId);
                    continue;
                }

                fixesApplied++;
                Debug.Log($"[CaveBuild] Production fix {fixesApplied}/{rungBudget} ({stageId}): {action}");

                quality = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
                CaveBuildQualitySystem.ApplyDudDetection(quality, caveRoot, request);
                quality.RecalculateOverall();
                if (quality.BuildAcceptable && quality.OverallScore >= 85)
                    break;
            }

            var routeReport = CaveRouteProbeRunner.Run(caveRoot);
            CaveRouteProbeRunner.Export(routeReport, caveRoot);

            quality = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
            CaveBuildQualitySystem.ApplyDudDetection(quality, caveRoot, request);
            quality.RecalculateOverall();
            CaveBuildQualitySystem.ApplyRecommendedAction(quality, request);
            CaveBuildQualitySystem.SetLastGradedReport(quality);

            var triCount = CountCaveTriangles(caveRoot);
            var routeOk = routeReport.Passed || routeReport.PathSteps <= 0;
            var perfOk = triCount <= PerformanceTriBudget;
            var demoGate = quality.BuildAcceptable && quality.OverallScore >= 75 && routeOk && perfOk;

            CaveBuildQualityReportWriter.Write(quality, gradingMode: "bot_production_post_pass");
            CaveBuildAgentContextExporter.Export(quality, caveRoot, meatLoopPass: fixesApplied, ground);
            CaveBuildQualityAgentBridge.WriteStructuredPrompt(quality, includeLiveSection: false);
            WriteProductionGateJson(quality, routeOk, perfOk, triCount, demoGate, scoreBefore, fixesApplied);

            EditorUtility.SetDirty(caveRoot.gameObject);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log(
                $"[CaveBuild] Production post-pass — {quality.LetterGrade} ({quality.OverallScore}/100) " +
                $"fixes={fixesApplied} routeOk={routeOk} perfOk={perfOk} demoGate={demoGate}");

            if (quality.BuildAcceptable && quality.OverallScore >= 85 && routeOk && perfOk)
                EditorApplication.Exit(0);

            EditorApplication.Exit(demoGate ? 1 : 3);
        }

        static void WriteProductionGateJson(
            CaveBuildQualityReport quality,
            bool routeOk,
            bool perfOk,
            int triCount,
            bool demoGate,
            int scoreBefore,
            int fixesApplied)
        {
            var path = CaveBuildAgentContextExporter.Folder + "/CaveBuildBotProductionGate.json";
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"capturedUtc\": \"{DateTime.UtcNow:o}\",\n");
            sb.Append($"  \"overallScore\": {quality.OverallScore},\n");
            sb.Append($"  \"scoreBefore\": {scoreBefore},\n");
            sb.Append($"  \"buildAcceptable\": {(quality.BuildAcceptable ? "true" : "false")},\n");
            sb.Append($"  \"demoGate\": {(demoGate ? "true" : "false")},\n");
            sb.Append($"  \"shipGate\": {(quality.BuildAcceptable && quality.OverallScore >= 85 ? "true" : "false")},\n");
            sb.Append($"  \"routeProbePassed\": {(routeOk ? "true" : "false")},\n");
            sb.Append($"  \"performanceOk\": {(perfOk ? "true" : "false")},\n");
            sb.Append($"  \"triangleCount\": {triCount},\n");
            sb.Append($"  \"fixesApplied\": {fixesApplied}\n");
            sb.Append("}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? CaveBuildAgentContextExporter.Folder);
            File.WriteAllText(path, sb.ToString());
        }

        static int CountCaveTriangles(Transform caveRoot)
        {
            var total = 0;
            foreach (var mf in caveRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null)
                    total += mf.sharedMesh.triangles.Length / 3;
            }

            return total;
        }

        static int ReadEnvInt(string key, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            return int.TryParse(raw, out var v) && v > 0 ? v : fallback;
        }

        static WorldGenerationRequest ResolveRequest(Transform caveRoot)
        {
            var request = CaveBuildRecipeLibrary.BuildRequestFromActiveRecipe(out _);
            request ??= new WorldGenerationRequest
            {
                UseSplineMesh = true,
                UseBlockTunnel = true,
                UseTrue3DCaveSystem = true,
                UseLayoutPrototype = CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot),
            };

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                request.Seed = meta.seed;
                request.CaveTunnelSegments = meta.tunnelSegments;
                request.CaveChamberCount = meta.chamberCount;
                request.MazeGenFlavor = meta.mazeGenFlavor;
            }

            return request;
        }

        static string MapRungToStage(string rung, CaveBuildQualityReport quality = null)
        {
            if (rung == CaveBuildPromptLadder.RungOther && quality != null)
            {
                var next = CaveBuildQualityLadder.PickNextStageId(quality);
                if (!string.IsNullOrEmpty(next))
                    return next;
            }

            if (string.IsNullOrEmpty(rung) || rung == CaveBuildPromptLadder.RungOther)
                return "visual_shell";
            if (rung == "floor_collision")
                return "player_floor";
            if (rung == "ground_placement")
                return "ground_placement";
            return rung;
        }

        static Transform FindCaveRoot()
        {
            var grid = GameObject.Find("Grid");
            if (grid != null)
            {
                var t = grid.transform.Find(CaveGeometryPaths.CaveSystemRootName);
                if (t != null)
                    return t;
                t = grid.transform.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (t != null)
                    return t;
            }

            var legacy = GameObject.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            return legacy != null ? legacy.transform : null;
        }

        static void TryOpenWorldScene()
        {
            foreach (var path in new[]
                     {
                         "Assets/TheStart.unity",
                         "Assets/MainScene.unity",
                         "Assets/NeonCity.unity",
                     })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    continue;
                if (SceneManager.GetActiveScene().path != path)
                    EnvironmentKitSceneSafeguards.TryOpenSceneByPath(path);
                return;
            }
        }
    }
}
#endif
