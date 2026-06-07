using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Editor.Atmosphere;
using EnvironmentAuthoringKit.Editor.Blockout;
using CaveTerrainUtility = EnvironmentAuthoringKit.Editor.TerrainAuthoring.CaveTerrainUtility;
using EnvironmentAuthoringKit.Editor.Scatter;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Generation
{
    public sealed class WorldGenerationResult
    {
        public bool Success;
        public string Message;
        public WorldGenerationRequest Request;
    }

    public static class WorldGenerator
    {
        public static WorldGenerationResult Generate(
            string description,
            BiomeCatalog catalog,
            int seed,
            bool optimizeForXr,
            XROptimizationProfile xrProfile)
        {
            var result = new WorldGenerationResult();
            if (string.IsNullOrWhiteSpace(description))
            {
                result.Message = "Description is empty.";
                return result;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate World");

            try
            {
                if (!ActiveSceneUtility.HasValidActiveScene)
                {
                    result.Message = "No active scene. Open MainScene (or your level) before generating.";
                    return result;
                }

                var activeSceneName = ActiveSceneUtility.ActiveScene.name;
                var request = DescriptionParser.Parse(description, seed);
                UnityAIWorldInterpreter.TryEnrich(request);
                ApplyWeatherAndTimeOverrides(request);

                var ground = SceneGroundResolver.Resolve(LoadUserGround());
                if (!ground.HasAnchor)
                {
                    result.Message =
                        $"No ground/plane found in '{activeSceneName}'. Select your Ground object and click Use Selection in Environment Kit.";
                    return result;
                }

                var root = EnvironmentSceneUtility.GetOrCreateRoot(ground);
                EnvironmentSceneUtility.ClearGeneratedChildren(root.transform);

                var biomeEntry = catalog != null ? catalog.GetOrDefault(request.Biome) : null;
                var allowTerrain = request.AllowCreateTerrain && !EnvironmentKitSettings.NeverCreateNewTerrain;
                var terrain = EnvironmentSceneUtility.FindTerrainInActiveScene(root.transform, ground, allowTerrain);
                var terrainPreset = biomeEntry?.terrainPreset;

                if (terrain != null)
                {
                    if (terrainPreset != null)
                    {
                        TerrainDressingApplier.ApplyHeightStyle(terrain, request.HeightStyle, terrainPreset, seed);
                        TerrainDressingApplier.Apply(terrain, terrainPreset);
                    }
                    else
                    {
                        TerrainDressingApplier.ApplyHeightStyle(terrain, request.HeightStyle, null, seed);
                    }

                    if (request.Biome == BiomeId.Cave || request.CaveMode != CaveGenerationMode.None)
                        CaveTerrainUtility.ApplyCaveEntranceMouth(terrain, seed);
                }

                var scatterProfile = biomeEntry?.scatterProfile;
                var skipOutdoorScatter = request.Biome == BiomeId.Cave && request.CaveMode != CaveGenerationMode.None;
                if (scatterProfile != null && terrain != null && !skipOutdoorScatter)
                {
                    var densityMul = request.Density switch
                    {
                        ScatterDensityLevel.Empty => 0.1f,
                        ScatterDensityLevel.Sparse => 0.5f,
                        ScatterDensityLevel.Dense => 1.6f,
                        _ => 1f
                    };
                    ScatterPlacer.ScatterOverTerrain(scatterProfile, terrain, root.transform, seed, densityMul);
                }

                var isCaveOnly = request.Biome == BiomeId.Cave || request.CaveMode != CaveGenerationMode.None;
                if (!isCaveOnly)
                {
                    var atmosphere = biomeEntry?.atmospherePreset ?? CreateFallbackAtmosphere(request);
                    ApplyAtmosphereModifiers(atmosphere, request);
                    AtmosphereApplier.Apply(atmosphere);
                }

                GenerateCavesAndBlockout(root.transform, request, biomeEntry, seed, ground, xrProfile, result);

                var lavaCave = root.transform.Find("LavaTubeCaveSystem");
                if (lavaCave != null)
                    LavaTubeCavePostProcess.Apply(lavaCave, xrProfile, bakeNavMesh: true, bakeGiHints: true);

                root.SetGenerationInfo(description, seed);
                var metadata = root.GetComponent<GeneratedWorldMetadata>() ?? root.gameObject.AddComponent<GeneratedWorldMetadata>();
                metadata.Apply(description, request.Biome.ToString(), request.Time.ToString(), request.Weather.ToString(), seed, false);

                if (optimizeForXr && xrProfile != null)
                {
                    var report = XROptimizer.Apply(xrProfile, root.transform);
                    metadata.Apply(description, request.Biome.ToString(), request.Time.ToString(), request.Weather.ToString(), seed, true);
                    result.Message = report.Summary;
                }
                else
                {
                    var groundLabel = ground.HasAnchor ? ground.Anchor.name : "scene origin";
                    var caveNote = request.CaveMode != CaveGenerationMode.None
                        ? $" Cave: {request.CaveMode}."
                        : string.Empty;
                    result.Message =
                        $"Generated {request.Biome} in scene '{activeSceneName}' under '{groundLabel}' ({request.Time}, {request.Weather}).{caveNote}";
                }

                result.Success = true;
                result.Request = request;
                EnvironmentSceneUtility.MarkSceneDirty();
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            return result;
        }

        static Transform LoadUserGround()
        {
            var stored = EditorPrefs.GetString(EnvironmentKitSettings.GroundObjectKey, string.Empty);
            if (string.IsNullOrEmpty(stored) || !GlobalObjectId.TryParse(stored, out var id))
                return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is GameObject go ? go.transform : null;
        }


        /// <summary>Same automated path as Build Complete Cave: pre-build gate → 40-stage pipeline.</summary>
        static bool TryGenerateFullCavePipeline(
            Transform root,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            int seed,
            XROptimizationProfile xrProfile,
            out bool blockedByPreBuildGate)
        {
            blockedByPreBuildGate = false;
            if (!LavaTubePrefabCatalog.Load().IsValid)
                return false;

            request.Seed = seed;
            request.UseSplineMesh = true;
            request.UseTrue3DCaveSystem = true;
            request.UseBlockTunnel = true;
            request.UseTerrainCarve = true;
            request.IncludeCaveWater = true;

            if (!CaveBuildUnifiedFlow.TryRunPreBuildPhaseForWorldGen(
                    root,
                    ground,
                    request,
                    seed,
                    xrProfile,
                    out _))
            {
                if (CaveBuildPendingGeometryBuild.HasPending)
                    return true;

                blockedByPreBuildGate = true;
                return false;
            }

            CaveBuildUnifiedFlow.LogFlowStart(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, false);
            LavaTubeCaveBuildPipeline.Run(root, ground, request, xrProfile, showProgress: true);
            return true;
        }

        static void GenerateCavesAndBlockout(
            Transform root,
            WorldGenerationRequest request,
            BiomeEntry biomeEntry,
            int seed,
            SceneGroundInfo ground,
            XROptimizationProfile xrProfile,
            WorldGenerationResult result)
        {
            if (request.CaveMode == CaveGenerationMode.FullSystem ||
                request.BlockoutLayout == BlockoutLayoutKind.CaveSystem)
            {
                if (request.CaveMode == CaveGenerationMode.None)
                    request.CaveMode = CaveGenerationMode.FullSystem;
                if (TryGenerateFullCavePipeline(
                        root,
                        ground,
                        request,
                        seed,
                        xrProfile,
                        out var blockedByGate))
                    return;

                if (blockedByGate)
                {
                    result.Success = false;
                    result.Message =
                        "Cave generation blocked by pre-build gate (or surface not ready). " +
                        $"See {CaveBuildPreBuildLadder.ReportPath}.";
                    return;
                }

                CaveSystemGenerator.Generate(root, request, ground);
                return;
            }

            if (request.CaveMode == CaveGenerationMode.EntranceOnly ||
                request.BlockoutLayout == BlockoutLayoutKind.CaveEntrance)
            {
                if (request.CaveMode == CaveGenerationMode.None)
                    request.CaveMode = CaveGenerationMode.EntranceOnly;
                CaveSystemGenerator.GenerateEntranceOnly(root, request, ground);
                return;
            }

            if (request.Biome == BiomeId.Cave)
            {
                request.CaveMode = CaveGenerationMode.FullSystem;
                if (TryGenerateFullCavePipeline(
                        root,
                        ground,
                        request,
                        seed,
                        xrProfile,
                        out var blockedByGate))
                    return;

                if (blockedByGate)
                {
                    result.Success = false;
                    result.Message =
                        "Cave generation blocked by pre-build gate (or surface not ready). " +
                        $"See {CaveBuildPreBuildLadder.ReportPath}.";
                    return;
                }

                CaveSystemGenerator.Generate(root, request, ground);
                return;
            }

            var layout = request.BlockoutLayout != BlockoutLayoutKind.None
                ? request.BlockoutLayout
                : biomeEntry?.defaultBlockout ?? BlockoutLayoutKind.None;
            if (layout != BlockoutLayoutKind.None &&
                layout != BlockoutLayoutKind.CaveSystem &&
                layout != BlockoutLayoutKind.CaveEntrance)
                BlockoutTool.GenerateLayout(layout, root.transform, seed);
        }

        static void ApplyWeatherAndTimeOverrides(WorldGenerationRequest request)
        {
            if (request.Weather == WeatherKind.Foggy)
                request.FogDensityMultiplier *= 1.5f;
            if (request.Time == TimeOfDay.Night)
                request.ColorMood *= 0.5f;
        }

        static AtmospherePreset CreateFallbackAtmosphere(WorldGenerationRequest request)
        {
            var preset = ScriptableObject.CreateInstance<AtmospherePreset>();
            preset.fogEnabled = request.Weather == WeatherKind.Foggy || request.Weather == WeatherKind.Rainy;
            preset.fogDensity = 0.01f * request.FogDensityMultiplier;
            preset.sunIntensity = request.Time == TimeOfDay.Night ? 0.25f : 1.1f;
            preset.sunColor = request.Time == TimeOfDay.Night ? new Color(0.6f, 0.7f, 1f) : Color.white;
            preset.skyTint = request.Biome switch
            {
                BiomeId.Desert => new Color(0.95f, 0.85f, 0.6f),
                BiomeId.Cave => new Color(0.08f, 0.09f, 0.12f),
                _ => new Color(0.5f, 0.65f, 0.9f)
            };
            if (request.Biome == BiomeId.Cave)
            {
                preset.fogEnabled = true;
                preset.fogDensity = 0.025f;
                preset.sunIntensity = 0.15f;
            }

            return preset;
        }

        static void ApplyAtmosphereModifiers(AtmospherePreset preset, WorldGenerationRequest request)
        {
            if (preset == null)
                return;

            if (request.Biome == BiomeId.Cave)
            {
                preset.fogEnabled = true;
                preset.fogDensity = Mathf.Max(preset.fogDensity, 0.02f) * request.FogDensityMultiplier;
                preset.sunIntensity *= 0.25f;
                preset.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                preset.skyAmbient = new Color(0.06f, 0.07f, 0.09f);
            }

            preset.fogDensity *= request.FogDensityMultiplier;
            if (request.Weather == WeatherKind.Foggy)
                preset.fogEnabled = true;
            if (request.Time == TimeOfDay.Dusk || request.Time == TimeOfDay.Dawn)
                preset.sunColor = Color.Lerp(preset.sunColor, new Color(1f, 0.6f, 0.35f), 0.5f);
            if (request.Time == TimeOfDay.Night)
            {
                preset.sunIntensity *= 0.35f;
                preset.bloomIntensity = Mathf.Max(preset.bloomIntensity, 0.15f);
            }

            preset.colorAdjustmentsPostExposure = Mathf.Lerp(-0.3f, 0.3f, request.ColorMood);
        }
    }
}
