using EnvironmentAuthoringKit.Editor.Atmosphere;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.Scatter;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor
{
    public static class SamplePresetsCreator
    {
        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Create Sample Presets")]
        public static void CreateAll()
        {
            EnsureFolder(EnvironmentKitSettings.PresetsFolder);

            var forestScatter = CreateScatter("ForestScatter", new Color(0.2f, 0.5f, 0.2f));
            var desertScatter = CreateScatter("DesertScatter", new Color(0.7f, 0.6f, 0.35f));
            var snowScatter = CreateScatter("SnowScatter", new Color(0.85f, 0.9f, 0.95f));
            var beachScatter = CreateScatter("BeachScatter", new Color(0.3f, 0.6f, 0.5f));
            var cityScatter = CreateScatter("CityScatter", new Color(0.4f, 0.4f, 0.5f));
            var dungeonScatter = CreateScatter("DungeonScatter", new Color(0.35f, 0.3f, 0.35f));
            var caveScatter = CreateScatter("CaveScatter", new Color(0.4f, 0.38f, 0.35f));
            caveScatter.densityPerSquareMeter = 0.02f;

            var forestTerrain = CreateTerrain("ForestTerrain", new Color(0.28f, 0.45f, 0.22f), new Color(0.35f, 0.38f, 0.25f));
            var desertTerrain = CreateTerrain("DesertTerrain", new Color(0.75f, 0.62f, 0.35f), new Color(0.55f, 0.45f, 0.3f));
            var snowTerrain = CreateTerrain("SnowTerrain", new Color(0.9f, 0.92f, 0.95f), new Color(0.7f, 0.75f, 0.8f));
            var beachTerrain = CreateTerrain("BeachTerrain", new Color(0.85f, 0.78f, 0.55f), new Color(0.2f, 0.45f, 0.55f));
            var cityTerrain = CreateTerrain("CityTerrain", new Color(0.35f, 0.35f, 0.38f), new Color(0.25f, 0.25f, 0.28f));
            var dungeonTerrain = CreateTerrain("DungeonTerrain", new Color(0.25f, 0.22f, 0.22f), new Color(0.18f, 0.16f, 0.16f));
            var caveTerrain = CreateTerrain("CaveTerrain", new Color(0.32f, 0.3f, 0.28f), new Color(0.22f, 0.2f, 0.18f));

            var forestAtmo = CreateAtmosphere("ForestOvercast", true, new Color(0.7f, 0.75f, 0.8f), 0.018f);
            var desertAtmo = CreateAtmosphere("DesertNoon", false, new Color(0.9f, 0.85f, 0.7f), 0.004f);
            var snowAtmo = CreateAtmosphere("SnowDay", false, new Color(0.85f, 0.9f, 1f), 0.008f);
            var beachAtmo = CreateAtmosphere("BeachDay", false, new Color(0.6f, 0.8f, 0.95f), 0.006f);
            var cityAtmo = CreateAtmosphere("CityNight", true, new Color(0.15f, 0.2f, 0.35f), 0.015f);
            var dungeonAtmo = CreateAtmosphere("DungeonDim", true, new Color(0.15f, 0.12f, 0.14f), 0.02f);
            var caveAtmo = CreateAtmosphere("CaveDark", true, new Color(0.08f, 0.09f, 0.11f), 0.028f);
            caveAtmo.sunIntensity = 0.2f;
            caveAtmo.skyTint = new Color(0.05f, 0.06f, 0.08f);

            var catalog = ScriptableObject.CreateInstance<BiomeCatalog>();
            catalog.biomes = new[]
            {
                Entry(BiomeId.Forest, forestTerrain, forestScatter, forestAtmo),
                Entry(BiomeId.Desert, desertTerrain, desertScatter, desertAtmo),
                Entry(BiomeId.Snow, snowTerrain, snowScatter, snowAtmo),
                Entry(BiomeId.Beach, beachTerrain, beachScatter, beachAtmo),
                Entry(BiomeId.City, cityTerrain, cityScatter, cityAtmo),
                Entry(BiomeId.Dungeon, dungeonTerrain, dungeonScatter, dungeonAtmo),
                CaveEntry(caveTerrain, caveScatter, caveAtmo),
                Entry(BiomeId.Jungle, forestTerrain, forestScatter, forestAtmo, BiomeId.Jungle)
            };

            var xr = ScriptableObject.CreateInstance<XROptimizationProfile>();
            xr.name = "VitureXRPro";
            xr.renderScale = 0.9f;
            xr.targetFrameRate = 72;
            xr.shadowDistance = 30f;

            Save(catalog, "BiomeCatalog");
            Save(xr, "VitureXRPro");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Environment Kit", $"Sample presets created in {EnvironmentKitSettings.PresetsFolder}", "OK");
        }

        static BiomeEntry Entry(BiomeId id, TerrainDressingPreset t, ScatterProfile s, AtmospherePreset a, BiomeId? overrideId = null)
        {
            return new BiomeEntry
            {
                biomeId = overrideId ?? id,
                terrainPreset = t,
                scatterProfile = s,
                atmospherePreset = a
            };
        }

        static BiomeEntry CaveEntry(TerrainDressingPreset t, ScatterProfile s, AtmospherePreset a)
        {
            return new BiomeEntry
            {
                biomeId = BiomeId.Cave,
                terrainPreset = t,
                scatterProfile = s,
                atmospherePreset = a,
                defaultBlockout = BlockoutLayoutKind.CaveSystem
            };
        }

        static ScatterProfile CreateScatter(string name, Color placeholderColor)
        {
            var profile = ScriptableObject.CreateInstance<ScatterProfile>();
            profile.name = name;
            profile.densityPerSquareMeter = 0.06f;
            profile.usePlaceholderPrimitives = true;
            profile.entries = new System.Collections.Generic.List<ScatterProfile.ScatterEntry>
            {
                new() { weight = 1f, scaleRange = new Vector2(0.6f, 1.4f) }
            };
            Save(profile, name);
            return profile;
        }

        static TerrainDressingPreset CreateTerrain(string name, Color ground, Color secondary)
        {
            var preset = ScriptableObject.CreateInstance<TerrainDressingPreset>();
            preset.name = name;
            preset.groundTint = ground;
            preset.secondaryTint = secondary;
            Save(preset, name);
            return preset;
        }

        static AtmospherePreset CreateAtmosphere(string name, bool fog, Color fogColor, float density)
        {
            var preset = ScriptableObject.CreateInstance<AtmospherePreset>();
            preset.name = name;
            preset.fogEnabled = fog;
            preset.fogColor = fogColor;
            preset.fogDensity = density;
            Save(preset, name);
            return preset;
        }

        static void Save(ScriptableObject asset, string fileName)
        {
            var path = $"{EnvironmentKitSettings.PresetsFolder}/{fileName}.asset";
            AssetDatabase.CreateAsset(asset, path);
        }

        static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Presets");
        }
    }
}
