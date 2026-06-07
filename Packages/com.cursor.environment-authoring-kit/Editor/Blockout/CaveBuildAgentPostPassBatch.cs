#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Synchronous batch pass after a Cursor agent session: compile export → scene fix → re-grade → JSON export.
    /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunAgentPostPassForBot -quit
    /// </summary>
    public static class CaveBuildAgentPostPassBatch
    {
        public static void Run()
        {
            TryOpenWorldScene();
            CaveBuildCompileGate.RequestRecompileAndExportDiagnostics();

            var caveRoot = FindCaveRoot();
            if (caveRoot == null)
            {
                Debug.LogError("[CaveBuild] Agent post-pass: cave root not found.");
                EditorApplication.Exit(2);
                return;
            }

            if (CaveBuildCompileGate.HasBlockingErrors())
            {
                Debug.LogError("[CaveBuild] Agent post-pass: compile errors remain — fix CS errors before meat loop.");
                EditorApplication.Exit(2);
                return;
            }

            var ground = SceneGroundResolver.Resolve();
            var request = ResolveRequest(caveRoot);
            var seed = request?.Seed ?? 0;

            var quality = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
            CaveBuildQualitySystem.SetLastGradedReport(quality);

            var rung = CaveBuildPromptLadder.PickActiveRung(quality, caveRoot, ground);
            var stageId = MapRungToStage(rung, quality);
            var fixedStage = false;
            if (!string.IsNullOrEmpty(stageId) &&
                CaveBuildQualityStageFixer.CanAutoFix(stageId) &&
                CaveBuildQualityStageFixer.TryFix(
                    stageId,
                    caveRoot,
                    request,
                    ground,
                    seed,
                    out var action))
            {
                fixedStage = true;
                Debug.Log($"[CaveBuild] Agent post-pass fix ({rung} → {stageId}): {action}");
            }
            else
            {
                Debug.Log($"[CaveBuild] Agent post-pass: no auto-fix applied for rung '{rung}'.");
            }

            quality = CaveBuildQualityGrader.GradeFullBuild(caveRoot, ground, request, null);
            CaveBuildQualitySystem.ApplyDudDetection(quality, caveRoot, request);
            quality.RecalculateOverall();
            CaveBuildQualitySystem.ApplyRecommendedAction(quality, request);
            CaveBuildQualitySystem.SetLastGradedReport(quality);
            CaveBuildQualityReportWriter.Write(quality, gradingMode: "agent_post_pass");
            CaveBuildAgentContextExporter.Export(quality, caveRoot, meatLoopPass: -1, ground);
            CaveBuildQualityAgentBridge.WriteStructuredPrompt(quality, includeLiveSection: false);

            if (fixedStage)
                EditorUtility.SetDirty(caveRoot.gameObject);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log(
                $"[CaveBuild] Agent post-pass done — {quality.LetterGrade} ({quality.OverallScore}/100) " +
                $"buildAcceptable={quality.BuildAcceptable} rung={rung}");
            EditorApplication.Exit(quality.BuildAcceptable ? 0 : 3);
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
            // ea-seed-leveraging-llms-efficient-failure-analysis: triage dud builds to the next ladder stage, not visual_shell when rubric already passes.
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
