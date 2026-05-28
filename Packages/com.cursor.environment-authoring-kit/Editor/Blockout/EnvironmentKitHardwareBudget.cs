#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Laptop-friendly defaults (MacBook Air M-series, 16GB unified memory) — trades a little CPU for much less GPU pressure.
    /// </summary>
    public static class EnvironmentKitHardwareBudget
    {
        public enum Preset
        {
            Default = 0,
            MacBookAir16Gb = 1,
        }

        public readonly struct Settings
        {
            public readonly Preset Preset;
            public readonly int TerrainHeightmapResolution;
            public readonly int TerrainMaxSizeMeters;
            public readonly float TerrainMaxHeightMeters;
            public readonly float SurfaceExtentMaxMeters;
            public readonly int MaxExtraTerrainTiles;
            public readonly int MaxScatterTextureSize;
            public readonly int MaxTerrainTextureSize;
            public readonly int ReflectionProbeResolution;
            public readonly bool SkipReflectionProbes;
            public readonly bool UnloadUnusedAssetsBetweenQueueSteps;
            public readonly int EditorTextureMipmapLimit;
            public readonly float RenderScale;
            public readonly int ShadowDistance;
            public readonly bool DisableLiveSceneFraming;

            public bool ConserveGpuMemory => Preset != Preset.Default;

            public Settings(
                Preset preset,
                int terrainHeightmapResolution,
                int terrainMaxSizeMeters,
                float terrainMaxHeightMeters,
                float surfaceExtentMaxMeters,
                int maxExtraTerrainTiles,
                int maxScatterTextureSize,
                int maxTerrainTextureSize,
                int reflectionProbeResolution,
                bool skipReflectionProbes,
                bool unloadUnusedAssetsBetweenQueueSteps,
                int editorTextureMipmapLimit,
                float renderScale,
                int shadowDistance,
                bool disableLiveSceneFraming)
            {
                Preset = preset;
                TerrainHeightmapResolution = terrainHeightmapResolution;
                TerrainMaxSizeMeters = terrainMaxSizeMeters;
                TerrainMaxHeightMeters = terrainMaxHeightMeters;
                SurfaceExtentMaxMeters = surfaceExtentMaxMeters;
                MaxExtraTerrainTiles = maxExtraTerrainTiles;
                MaxScatterTextureSize = maxScatterTextureSize;
                MaxTerrainTextureSize = maxTerrainTextureSize;
                ReflectionProbeResolution = reflectionProbeResolution;
                SkipReflectionProbes = skipReflectionProbes;
                UnloadUnusedAssetsBetweenQueueSteps = unloadUnusedAssetsBetweenQueueSteps;
                EditorTextureMipmapLimit = editorTextureMipmapLimit;
                RenderScale = renderScale;
                ShadowDistance = shadowDistance;
                DisableLiveSceneFraming = disableLiveSceneFraming;
            }
        }

        static readonly Settings DefaultSettings = new(
            Preset.Default,
            terrainHeightmapResolution: 513,
            terrainMaxSizeMeters: 384,
            terrainMaxHeightMeters: 120f,
            surfaceExtentMaxMeters: 512f,
            maxExtraTerrainTiles: SurfaceTerrainTileExpansion.MaxExtraTiles,
            maxScatterTextureSize: 1024,
            maxTerrainTextureSize: 2048,
            reflectionProbeResolution: 64,
            skipReflectionProbes: false,
            unloadUnusedAssetsBetweenQueueSteps: false,
            editorTextureMipmapLimit: 0,
            renderScale: 0.9f,
            shadowDistance: 30,
            disableLiveSceneFraming: false);

        static readonly Settings MacBookAirSettings = new(
            Preset.MacBookAir16Gb,
            terrainHeightmapResolution: 257,
            terrainMaxSizeMeters: 256,
            terrainMaxHeightMeters: 80f,
            surfaceExtentMaxMeters: 200f,
            maxExtraTerrainTiles: 4,
            maxScatterTextureSize: 512,
            maxTerrainTextureSize: 1024,
            reflectionProbeResolution: 32,
            skipReflectionProbes: true,
            unloadUnusedAssetsBetweenQueueSteps: true,
            editorTextureMipmapLimit: 1,
            renderScale: 0.85f,
            shadowDistance: 24,
            disableLiveSceneFraming: true);

        static int _savedTextureMipmapLimit = -1;
        static bool _sessionActive;

        public static Settings Active
        {
            get
            {
                var s = CaveBuildCursorSettings.LoadOrCreate();
                s.LoadFromPrefs();
                return s.hardwareBudget == Preset.MacBookAir16Gb ? MacBookAirSettings : DefaultSettings;
            }
        }

        public static void ApplyMacBookAirPresetToSettings(CaveBuildCursorSettings settings)
        {
            if (settings == null)
                return;

            settings.hardwareBudget = Preset.MacBookAir16Gb;
            settings.showLiveScenePlacement = false;
            settings.mirrorPacedBuildLogsToConsole = false;
            settings.editorQueueBatchSize = 1;
            settings.enableBatchMode = false;
            settings.autoRunPlaytestBotAfterBuild = false;
            settings.exportGenerationPrefabWhenFinished = false;
            settings.runPostBuildResearchPhase = false;
            settings.enableAutonomousUntilShip = false;
            settings.SaveToPrefs();
            BeginEditorSession();
            CaveBuildEditorLog.LogCave(
                "MacBook Air (16GB) budget ON — lower terrain/texture GPU use, more CPU-paced steps.",
                forceUnityConsole: true);
        }

        public static void BeginEditorSession()
        {
            if (!Active.ConserveGpuMemory)
                return;
            if (_sessionActive)
                return;

            _sessionActive = true;
            _savedTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit;
            QualitySettings.globalTextureMipmapLimit = Mathf.Max(
                _savedTextureMipmapLimit,
                Active.EditorTextureMipmapLimit);

            EditorApplication.QueuePlayerLoopUpdate();
        }

        public static void EndEditorSession()
        {
            if (!_sessionActive)
                return;

            _sessionActive = false;
            if (_savedTextureMipmapLimit >= 0)
            {
                QualitySettings.globalTextureMipmapLimit = _savedTextureMipmapLimit;
                _savedTextureMipmapLimit = -1;
            }

            if (Active.UnloadUnusedAssetsBetweenQueueSteps)
                Resources.UnloadUnusedAssets();
        }

        static int _unloadStepCounter;

        public static void OnQueueStepCompletedThrottled()
        {
            if (!CaveBuildEditorResponsiveness.IsLongBuildActive)
                return;

            _unloadStepCounter++;
            var interval = Active.UnloadUnusedAssetsBetweenQueueSteps ? 2 : 6;
            if (_unloadStepCounter % interval != 0)
                return;

            Resources.UnloadUnusedAssets();
        }

        public static void OnQueueStepCompleted()
        {
            OnQueueStepCompletedThrottled();
        }

        public static int ClampHeightmapResolution(int requested) =>
            Active.ConserveGpuMemory
                ? Mathf.Min(requested, Active.TerrainHeightmapResolution)
                : requested;

        public static int ClampTerrainSizeMeters(int requested) =>
            Active.ConserveGpuMemory
                ? Mathf.Min(requested, Active.TerrainMaxSizeMeters)
                : requested;

        public static float ClampSurfaceExtent(float requested) =>
            Active.ConserveGpuMemory
                ? Mathf.Min(requested, Active.SurfaceExtentMaxMeters)
                : requested;

        public static XROptimizationProfile ResolveXrProfile(XROptimizationProfile source)
        {
            if (!Active.ConserveGpuMemory)
                return source;

            var profile = source != null
                ? Object.Instantiate(source)
                : ScriptableObject.CreateInstance<XROptimizationProfile>();

            profile.maxScatterTextureSize = Mathf.Min(profile.maxScatterTextureSize, Active.MaxScatterTextureSize);
            profile.maxTerrainTextureSize = Mathf.Min(profile.maxTerrainTextureSize, Active.MaxTerrainTextureSize);
            profile.renderScale = Mathf.Min(profile.renderScale, Active.RenderScale);
            profile.shadowDistance = Mathf.Min(profile.shadowDistance, Active.ShadowDistance);
            profile.msaa = Mathf.Min(profile.msaa, 2);
            profile.shadowCascades = 1;
            return profile;
        }
    }
}
#endif
