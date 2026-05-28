using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor
{
    static class EnvironmentKitSettings
    {
        const string PrefsPrefix = "EnvironmentKit_";

        public static float GridSnapSize
        {
            get => EditorPrefs.GetFloat(PrefsPrefix + "GridSnap", 1f);
            set => EditorPrefs.SetFloat(PrefsPrefix + "GridSnap", Mathf.Max(0.25f, value));
        }

        public static int GenerationSeed
        {
            get => EditorPrefs.GetInt(PrefsPrefix + "Seed", 12345);
            set => EditorPrefs.SetInt(PrefsPrefix + "Seed", value);
        }

        public static bool OptimizeForVitureOnGenerate
        {
            get => EditorPrefs.GetBool(PrefsPrefix + "XrOnGenerate", true);
            set => EditorPrefs.SetBool(PrefsPrefix + "XrOnGenerate", value);
        }

        public static string LastDescription
        {
            get => EditorPrefs.GetString(PrefsPrefix + "LastDesc", "misty pine forest at dusk with a small clearing");
            set => EditorPrefs.SetString(PrefsPrefix + "LastDesc", value ?? string.Empty);
        }

        public static string PresetsFolder => "Assets/EnvironmentKit/Presets";

        public const string GroundObjectKey = "EnvironmentKit_GroundObject";

        public static bool PlaceUnderGroundSurface
        {
            get => EditorPrefs.GetBool(PrefsPrefix + "UnderGround", true);
            set => EditorPrefs.SetBool(PrefsPrefix + "UnderGround", value);
        }

        public static bool GenerateInActiveSceneOnly
        {
            get => EditorPrefs.GetBool(PrefsPrefix + "ActiveSceneOnly", true);
            set => EditorPrefs.SetBool(PrefsPrefix + "ActiveSceneOnly", value);
        }

        public static bool SkipNewTerrainWhenGroundExists
        {
            get => EditorPrefs.GetBool(PrefsPrefix + "SkipNewTerrain", true);
            set => EditorPrefs.SetBool(PrefsPrefix + "SkipNewTerrain", value);
        }

        /// <summary>When true, never spawns a new Unity Terrain (use your plane/ground only).</summary>
        public static bool NeverCreateNewTerrain
        {
            get => EditorPrefs.GetBool(PrefsPrefix + "NeverNewTerrain", true);
            set => EditorPrefs.SetBool(PrefsPrefix + "NeverNewTerrain", value);
        }

        /// <summary>Semicolon-separated prefab folders for primary cave modules (floors/walls/ceilings).</summary>
        public static string CaveLavaPrefabFolders
        {
            get => EditorPrefs.GetString(
                PrefsPrefix + "CaveLavaPrefabFolders",
                "Assets/BillemotdonggulLavaTubePack/Prefabs/");
            set => EditorPrefs.SetString(PrefsPrefix + "CaveLavaPrefabFolders", value ?? string.Empty);
        }

        /// <summary>Semicolon-separated prefab folders for cave props/details.</summary>
        public static string CavePropPrefabFolders
        {
            get => EditorPrefs.GetString(
                PrefsPrefix + "CavePropPrefabFolders",
                "Assets/PolitePenguin/LPMagicalForest/Prefabs/");
            set => EditorPrefs.SetString(PrefsPrefix + "CavePropPrefabFolders", value ?? string.Empty);
        }

        /// <summary>When enabled, scans all Assets for extra cave props (can pull noisy content).</summary>
        public static bool CaveScanAllAssets
        {
            get => EditorPrefs.GetBool(PrefsPrefix + "CaveScanAllAssets", false);
            set => EditorPrefs.SetBool(PrefsPrefix + "CaveScanAllAssets", value);
        }
    }
}
