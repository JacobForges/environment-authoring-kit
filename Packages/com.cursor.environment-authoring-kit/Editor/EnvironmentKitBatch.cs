using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor
{
    /// <summary>
    /// Command-line: Unity -batchmode -projectPath /path/to/Hub -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateDefaultWorld -quit
    /// </summary>
    public static class EnvironmentKitBatch
    {
        const string DefaultDescription = "misty pine forest at dusk with a small clearing";
        const int DefaultSeed = 4242;

        const string CaveDescription =
            "hilly cave system with multiple tunnels, chambers, and a dramatic cave entrance, dense stalactites";

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Legacy/Build Lava Tube XR Cave (VITURE)")]
        public static void BuildLavaTubeCaveFromMenu() => LavaTubeCaveBuilder.BuildFromMenu();

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateLavaTubeCave</summary>
        public static void GenerateLavaTubeCave()
        {
            LavaTubeCaveBuilder.BuildInActiveScene(openMainSceneFirst: false);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Unity -batchmode -projectPath &lt;Hub&gt; -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunShowcaseHeadless -quit
        /// </summary>
        public static void RunShowcaseHeadless() =>
            CaveBuildHeadlessRunner.RunRecipe(
                CaveBuildAaaProductionBootstrap.FullProductionRecipeId,
                exitEditorWhenDone: true);

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunHeadlessCaveLadder -quit
        /// Optional env CAVE_BUILD_RECIPE_ID (default showcase-florida-karst-xr).
        /// </summary>
        public static void RunHeadlessCaveLadder()
        {
            var recipeId = System.Environment.GetEnvironmentVariable("CAVE_BUILD_RECIPE_ID");
            if (string.IsNullOrWhiteSpace(recipeId))
                recipeId = CaveBuildShowcaseMenu.ShowcaseRecipeId;
            CaveBuildHeadlessRunner.RunRecipe(recipeId, exitEditorWhenDone: true);
        }

        /// <summary>Headless surface-only (faster than showcase). Not used by nightly CI.</summary>
        public static void RunSurfaceIterationHeadless() =>
            CaveBuildHeadlessRunner.RunRecipe(CaveBuildShowcaseMenu.SurfaceIterationRecipeId, exitEditorWhenDone: true);

        /// <summary>Regenerate Hub.sln and compile scripts for CodeQL (self-hosted CI). See docs/CODEQL_SELFHOSTED_INSTALL.md.</summary>
        public static void PrepareForCodeQl() => CodeQlUnityBootstrap.PrepareForCodeQl();

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.ExportCompileDiagnosticsForAgent -quit
        /// Exports CaveBuildCompileDiagnostics.json; exit 2 when verified compile errors remain.
        /// </summary>
        public static void ExportCompileDiagnosticsForAgent()
        {
            CaveBuildCompileGate.ExportDiagnostics();
            EditorApplication.Exit(CaveBuildCompileGate.HasBlockingErrors() ? 2 : 0);
        }

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunAgentPostPassForBot -quit
        /// Applies one ladder fix + re-grade after a Cursor agent session (terminal bot loop).
        /// </summary>
        public static void RunAgentPostPassForBot() => CaveBuildAgentPostPassBatch.Run();

        /// <summary>
        /// Production bot loop: multi-rung fixes + route probe + performance gate.
        /// </summary>
        public static void RunBotProductionPostPass() => CaveBuildBotProductionBatch.Run();

        /// <summary>
        /// Read-only pipeline audit — probes + JSON only (no meat loop, no scene save).
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunBotPipelineAudit
        /// </summary>
        public static void RunBotPipelineAudit() => CaveBuildBotValidationBatch.Run();

        /// <summary>Code bot post-pass — compile, competition smoke, HubCodeProgress sync.</summary>
        public static void RunCodeBotPostPass() => InvokeHubEditorStaticVoid("CodeBotPostPass", "RunSilently");

        /// <summary>Export Hub demo smoke checklist JSON for bot supervisor.</summary>
        public static void ExportHubDemoSmokeChecklist()
        {
            TryOpenWorldScene();
            InvokeHubEditorStatic("HubDemoSmokeExporter", "Export");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunHubDemoSmokeGate -quit
        /// Exports scene checklist, syncs HubGameProgress from play report when acceptancePass.
        /// </summary>
        public static void RunHubDemoSmokeGate()
        {
            TryOpenWorldScene();
            InvokeHubEditorStatic("HubDemoSmokeExporter", "Export");
            var synced = InvokeHubEditorStatic("HubDemoSmokeProgressSync", "TrySyncFromPlayReport", false);
            Debug.Log($"[HubDemoSmoke] Gate export complete — progressSynced={synced}");
            EditorApplication.Exit(synced ? 0 : 1);
        }

        static bool InvokeHubEditorStatic(string typeName, string methodName, bool defaultValue = false)
        {
            var type = System.Type.GetType($"{typeName}, Hub.Editor");
            var method = type?.GetMethod(
                methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
                return defaultValue;

            var result = method.Invoke(null, method.GetParameters().Length == 0 ? null : new object[] { true });
            return result is bool b ? b : defaultValue;
        }

        static void InvokeHubEditorStaticVoid(string typeName, string methodName)
        {
            var type = System.Type.GetType($"{typeName}, Hub.Editor");
            var method = type?.GetMethod(
                methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, null);
        }

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.BuildCinematicTimelines -quit</summary>
        public static void BuildCinematicTimelines()
        {
            TryOpenWorldScene();
            var triggers = Object.FindObjectsByType<EnvironmentAuthoringKit.World.WorldCinematicTrigger>(
                FindObjectsInactive.Include);
            if (triggers.Length == 0)
            {
                var terrain = Object.FindAnyObjectByType<Terrain>();
                WorldFxAndMediaAuthor.Apply(terrain, null);
            }

            WorldCinematicTimelineAuthor.BindAllTriggersInScene();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.Exit(0);
        }

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.BuildHollowTitan -quit</summary>
        public static void BuildHollowTitan()
        {
            TryOpenWorldScene();
            var terrain = Object.FindAnyObjectByType<Terrain>();
            var request = new WorldGenerationRequest
            {
                ContentTier = WorldBuildContentTier.Aaa,
                Seed = DefaultSeed,
                SurfaceScope = SurfaceBuildScope.FullWorld,
            };
            HollowTitanLandmarkAuthor.PlaceAaaBeforeTerraform(terrain, request);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.Exit(0);
        }

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.WireHubCombat -quit</summary>
        public static void WireHubCombat()
        {
            TryOpenWorldScene();
            HubSurfaceCombatWiring.WireFromMenu();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.Exit(0);
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

            Debug.LogWarning("[EnvironmentKitBatch] No known world scene — using active scene.");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Legacy/Build Cave World Now (Blockout)")]
        public static void BuildCaveWorldFromMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Cave World",
                    "Generates caves under your scene ground/plane in the ACTIVE scene:\n\n\"" +
                    CaveDescription + "\"",
                    "Build",
                    "Cancel"))
                return;

            GenerateWorldInEditor(CaveDescription, 7721, openMainSceneFirst: true);
        }

        static void TryOpenMainScene()
        {
            const string mainScenePath = "Assets/MainScene.unity";
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(mainScenePath);
            if (sceneAsset == null)
            {
                Debug.LogWarning($"[Environment Kit] {mainScenePath} not found. Using the currently open scene.");
                return;
            }

            if (SceneManager.GetActiveScene().path != mainScenePath)
                EnvironmentKitSceneSafeguards.TryOpenSceneByPath(mainScenePath);
        }

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Legacy/Build Demo World Now")]
        public static void BuildDemoWorldFromMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Demo World",
                    "Generates in the ACTIVE scene under your ground/plane:\n\n\"" + DefaultDescription + "\"",
                    "Build",
                    "Cancel"))
                return;

            GenerateWorldInEditor(DefaultDescription, DefaultSeed);
        }

        public static void GenerateDefaultWorld()
        {
            GenerateWorld(DefaultDescription, DefaultSeed);
        }

        public static void GenerateWorldInEditor(string description, int seed, bool openMainSceneFirst = false)
        {
            if (openMainSceneFirst)
                TryOpenMainScene();

            if (AssetDatabase.LoadAssetAtPath<BiomeCatalog>(
                    $"{EnvironmentKitSettings.PresetsFolder}/BiomeCatalog.asset") == null)
                SamplePresetsCreator.CreateAll();

            var catalog = AssetDatabase.LoadAssetAtPath<BiomeCatalog>(
                $"{EnvironmentKitSettings.PresetsFolder}/BiomeCatalog.asset");
            var xr = EnvironmentKitHardwareBudget.ResolveXrProfile(
                AssetDatabase.LoadAssetAtPath<XROptimizationProfile>(
                    $"{EnvironmentKitSettings.PresetsFolder}/VitureXRPro.asset"));

            if (catalog == null)
            {
                EditorUtility.DisplayDialog("Environment Kit", "Could not load BiomeCatalog.", "OK");
                return;
            }

            var result = WorldGenerator.Generate(description, catalog, seed, optimizeForXr: true, xr);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Environment Kit", result.Message, "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Environment Kit",
                $"{result.Message}\n\nSaved in active scene. Use Ctrl+S to persist.",
                "OK");

            AssetDatabase.SaveAssets();
            EnvironmentSceneUtility.MarkSceneDirty();
        }

        public static void GenerateWorld(string description, int seed)
        {
            GenerateWorldInEditor(description, seed);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Unity -batchmode -projectPath &lt;Hub&gt; -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RepairLegacyModelImports -quit
        /// </summary>
        public static void RepairLegacyModelImports()
        {
            HubModelImportRepairUtility.RepairAll(forceReimport: true);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RenderRecapBotAvatar -quit
        /// Env: RECAP_BOT_ENVELOPE_JSON, RECAP_BOT_FRAME_DIR.
        /// </summary>
        public static void RenderRecapBotAvatar() => CaveBuildRecapBotAvatarRecorder.RenderFromEnvironment();

        /// <summary>
        /// Unity -batchmode -projectPath &lt;Hub&gt; -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.ExportPlannerKitCatalog -quit
        /// Writes planner-kit-catalog manifest + AssetPreview PNGs for build wizard prop cards.
        /// </summary>
        public static void GeneratePlannerProps()
        {
            CaveBuildPlannerGeneratedProps.GenerateForBatch();
        }

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateConceptCardMesh
        /// Reads planner-concept-card-mesh.request.json written by the build wizard.
        /// </summary>
        public static void GenerateConceptCardMesh()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = System.IO.Path.Combine(hub, CaveBuildPlannerGeneratedProps.CardMeshRequestRel);
            if (!System.IO.File.Exists(path))
            {
                Debug.LogError("[PlannerProps] No card mesh request file.");
                EditorApplication.Exit(1);
                return;
            }

            CaveBuildPlannerGeneratedProps.CardMeshRequest req;
            try
            {
                req = JsonUtility.FromJson<CaveBuildPlannerGeneratedProps.CardMeshRequest>(
                    System.IO.File.ReadAllText(path));
                System.IO.File.Delete(path);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[PlannerProps] " + ex.Message);
                EditorApplication.Exit(1);
                return;
            }

            var ok = CaveBuildPlannerGeneratedProps.TryGenerateForCard(
                req.cardId,
                req.categoryKey,
                req.label,
                req.seed,
                req.regenIndex,
                req.aiSpec,
                out var prefabRel,
                out var thumbRel,
                out var msg);

            var donePath = System.IO.Path.Combine(hub, CaveBuildPlannerGeneratedProps.CardMeshDoneRel);
            try
            {
                var dir = System.IO.Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(
                    donePath,
                    $"{{\"ok\":{(ok ? "true" : "false")},\"cardId\":\"{req.cardId}\","
                    + $"\"prefabPath\":\"{prefabRel ?? ""}\",\"thumbRel\":\"{thumbRel ?? ""}\","
                    + $"\"message\":\"{(msg ?? "").Replace("\"", "'")}\"}}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[PlannerProps] done file: " + ex.Message);
            }

            Debug.Log(ok ? "[PlannerProps] " + msg : "[PlannerProps] failed: " + msg);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void ExportPlannerKitCatalog()
        {
            var ok = CaveBuildPlannerKitCatalogExporter.ExportIfStale(out var msg, force: true);
            if (!ok)
            {
                Debug.LogError($"[PlannerKitCatalog] Export failed: {msg}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[PlannerKitCatalog] {msg}");
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.ExportConceptCardThumbnail
        /// Reads planner-concept-card-thumb.request.json for a single prefab preview.
        /// </summary>
        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateConceptCardSculpt
        /// Reads planner-concept-card-sculpt.request.json written by the build wizard.
        /// </summary>
        public static void GenerateConceptCardSculpt()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = System.IO.Path.Combine(hub, CaveBuildPlannerGeneratedCharacters.CardSculptRequestRel);
            if (!System.IO.File.Exists(path))
            {
                Debug.LogError("[PlannerCharacters] No card sculpt request file.");
                EditorApplication.Exit(1);
                return;
            }

            CaveBuildPlannerGeneratedCharacters.CardSculptRequest req;
            try
            {
                req = JsonUtility.FromJson<CaveBuildPlannerGeneratedCharacters.CardSculptRequest>(
                    System.IO.File.ReadAllText(path));
                System.IO.File.Delete(path);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[PlannerCharacters] " + ex.Message);
                EditorApplication.Exit(1);
                return;
            }

            var ok = CaveBuildPlannerGeneratedCharacters.TrySculptForCard(req, out var prefabRel, out var msg);

            var donePath = System.IO.Path.Combine(hub, CaveBuildPlannerGeneratedCharacters.CardSculptDoneRel);
            try
            {
                var dir = System.IO.Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(
                    donePath,
                    $"{{\"ok\":{(ok ? "true" : "false")},\"cardId\":\"{req?.cardId ?? ""}\","
                    + $"\"prefabPath\":\"{prefabRel ?? ""}\",\"message\":\"{(msg ?? "").Replace("\"", "'")}\"}}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[PlannerCharacters] done file: " + ex.Message);
            }

            Debug.Log(ok ? "[PlannerCharacters] " + msg : "[PlannerCharacters] failed: " + msg);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void ExportConceptCardThumbnail()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            const string requestRel =
                "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.request.json";
            const string doneRel =
                "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.done.json";
            var path = System.IO.Path.Combine(hub, requestRel);
            if (!System.IO.File.Exists(path))
            {
                Debug.LogError("[PlannerKitCatalog] No concept card thumb request file.");
                EditorApplication.Exit(1);
                return;
            }

            string prefabPath;
            try
            {
                var json = System.IO.File.ReadAllText(path);
                System.IO.File.Delete(path);
                var marker = "\"prefabPath\"";
                var idx = json.IndexOf(marker, System.StringComparison.Ordinal);
                if (idx < 0)
                    throw new System.InvalidOperationException("prefabPath missing");
                var start = json.IndexOf('"', idx + marker.Length) + 1;
                var end = json.IndexOf('"', start);
                prefabPath = json.Substring(start, end - start);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[PlannerKitCatalog] " + ex.Message);
                EditorApplication.Exit(1);
                return;
            }

            var thumbRel = CaveBuildPlannerKitCatalogExporter.ExportThumbnailForPrefab(prefabPath);
            var ok = !string.IsNullOrEmpty(thumbRel);
            var msg = ok
                ? $"Exported preview for {System.IO.Path.GetFileNameWithoutExtension(prefabPath)}"
                : $"Could not render preview for {prefabPath}";

            var donePath = System.IO.Path.Combine(hub, doneRel);
            try
            {
                var dir = System.IO.Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(
                    donePath,
                    $"{{\"ok\":{(ok ? "true" : "false")},\"thumbRel\":\"{thumbRel ?? ""}\","
                    + $"\"prefabPath\":\"{prefabPath}\",\"message\":\"{msg.Replace("\"", "'")}\"}}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[PlannerKitCatalog] done file: " + ex.Message);
            }

            Debug.Log(ok ? "[PlannerKitCatalog] " + msg : "[PlannerKitCatalog] failed: " + msg);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
