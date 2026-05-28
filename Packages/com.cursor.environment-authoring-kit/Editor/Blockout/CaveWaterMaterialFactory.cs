using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Creates the cave pool material from Ignite Simple Water Shader (URP).</summary>
    public static class CaveWaterMaterialFactory
    {
        public const string CaveWaterMatPath = "Assets/EnvironmentKit/Presets/CaveUndergroundWater_URP.mat";
        public const string CaveLavaMatPath = "Assets/EnvironmentKit/Presets/CaveLavaEmissive_URP.mat";
        const string IgniteWaterMatPath = "Assets/IgniteCoders/Simple Water Shader/Resources/Water_mat_01.mat";

        /// <summary>Red emissive lava for pits and small pools (not flat blue water).</summary>
        public static Material GetOrCreateLava()
        {
            var preset = AssetDatabase.LoadAssetAtPath<Material>(CaveLavaMatPath);
            if (preset != null)
                return preset;

            EnsurePresetsFolder();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "CaveLavaEmissive_URP" };
            var baseColor = new Color(0.85f, 0.12f, 0.02f, 1f);
            var emission = new Color(2.2f, 0.45f, 0.05f);

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", emission);
                mat.EnableKeyword("_EMISSION");
            }

            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.75f);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0.05f);

            AssetDatabase.CreateAsset(mat, CaveLavaMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        public static Material GetOrCreate()
        {
            var preset = AssetDatabase.LoadAssetAtPath<Material>(CaveWaterMatPath);
            if (preset != null && IsIgniteWaterShader(preset.shader))
                return preset;

            if (preset != null)
                AssetDatabase.DeleteAsset(CaveWaterMatPath);

            var ignite = AssetDatabase.LoadAssetAtPath<Material>(IgniteWaterMatPath);
            if (ignite == null)
            {
                Debug.LogError(
                    "[CaveWater] Missing Ignite water at " + IgniteWaterMatPath +
                    ". Import 'Simple Water Shader URP' or add " + CaveWaterMatPath);
                return preset;
            }

            EnsurePresetsFolder();
            var mat = new Material(ignite) { name = "CaveUndergroundWater_URP" };
            TuneForUndergroundCave(mat);
            AssetDatabase.CreateAsset(mat, CaveWaterMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        public static bool IsIgniteWaterShader(Shader shader)
        {
            if (shader == null)
                return false;

            var name = shader.name;
            return name.Contains("Simple Water Shader") || name.Contains("IgniteCoders");
        }

        public static bool IsCaveWaterMaterial(Material material)
        {
            if (material == null)
                return false;

            if (IsIgniteWaterShader(material.shader))
                return true;

            var path = AssetDatabase.GetAssetPath(material);
            return !string.IsNullOrEmpty(path) && path == CaveWaterMatPath;
        }

        public static bool IsCaveLavaMaterial(Material material)
        {
            if (material == null)
                return false;

            var path = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(path) && path == CaveLavaMatPath)
                return true;

            if (material.HasProperty("_EmissionColor") &&
                material.GetColor("_EmissionColor").maxColorComponent > 1.2f)
                return true;

            return material.name.Contains("Lava", System.StringComparison.OrdinalIgnoreCase);
        }

        public static void ForceCaveWaterMaterial(MeshRenderer renderer)
        {
            if (renderer == null)
                return;

            var current = renderer.sharedMaterial;
            if (IsCaveWaterMaterial(current))
                return;

            var caveMat = GetOrCreate();
            if (caveMat != null)
                renderer.sharedMaterial = caveMat;
        }

        static void TuneForUndergroundCave(Material mat)
        {
            if (mat.HasProperty("Color_36218622185947c6a5ae36366d8e21d8"))
                mat.SetColor("Color_36218622185947c6a5ae36366d8e21d8", new Color(0.06f, 0.18f, 0.28f, 0.92f));
            if (mat.HasProperty("Color_93e06cd551a5449091bcde90b46765a0"))
                mat.SetColor("Color_93e06cd551a5449091bcde90b46765a0", new Color(0.05f, 0.35f, 0.42f, 0.35f));
        }

        static void EnsurePresetsFolder()
        {
            if (AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Presets"))
                return;

            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Presets");
        }
    }
}
