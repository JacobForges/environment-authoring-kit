using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveSplineMaterialFactory
    {
        const string PresetPath = "Assets/EnvironmentKit/Presets/CaveSplineRock.mat";
        static readonly string[] SourceMaterialPaths =
        {
            "Assets/BillemotdonggulLavaTubePack/Material/MI_Wall06A.mat",
            "Assets/BillemotdonggulLavaTubePack/Material/M_Wall03A.mat",
            "Assets/BillemotdonggulLavaTubePack/Mesh/Materials/MI_Wall06A.mat",
            "Assets/BillemotdonggulLavaTubePack/Material/M_Floor03A.mat"
        };

        const string FloorPresetPath = "Assets/EnvironmentKit/Presets/CaveSplineFloor.mat";

        public static Material GetOrCreateCaveFloorMaterial()
        {
            LavaTubeMaterialUpgrader.EnsurePackMaterialsUpgraded();

            var floorTint = new Color(0.48f, 0.4f, 0.34f);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(FloorPresetPath);
            if (existing != null)
            {
                if (!CaveEditorUndo.IsBulkBuild)
                {
                    ApplyFloorSurface(existing, floorTint);
                    EditorUtility.SetDirty(existing);
                }

                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/BillemotdonggulLavaTubePack/Material/M_Floor03A.mat");
            if (source == null)
                source = LoadFirstMaterial();

            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Presets"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                    AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Presets");
            }

            var mat = source != null ? new Material(source) { name = "CaveSplineFloor" } : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            ApplyFloorSurface(mat, floorTint);
            AssetDatabase.CreateAsset(mat, FloorPresetPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void ApplyFloorSurface(Material mat, Color tint)
        {
            if (mat == null)
                return;

            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTextureScale("_BaseMap", new Vector2(3.5f, 3.5f));
                mat.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            else if (mat.HasProperty("_MainTex"))
            {
                mat.SetTextureScale("_MainTex", new Vector2(3.5f, 3.5f));
            }

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.12f);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0.01f);
            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", 0f);
        }

        public static Material GetOrCreateCaveRockMaterial()
        {
            LavaTubeMaterialUpgrader.EnsurePackMaterialsUpgraded();

            // Warm cave stone tint — lets the lava-tube pack's rock texture show through
            // (texture provides the detail; tint just shifts toward earthy brown/amber under torchlight).
            var caveStone = new Color(0.62f, 0.5f, 0.42f);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(PresetPath);
            if (existing != null)
            {
                if (!CaveEditorUndo.IsBulkBuild)
                {
                    if (existing.HasProperty("_Cull"))
                        existing.SetFloat("_Cull", 0f);
                    if (existing.HasProperty("_BaseColor"))
                        existing.SetColor("_BaseColor", caveStone);
                    if (existing.HasProperty("_Color"))
                        existing.SetColor("_Color", caveStone);
                    if (existing.HasProperty("_Smoothness"))
                        existing.SetFloat("_Smoothness", 0.15f);
                    if (existing.HasProperty("_Metallic"))
                        existing.SetFloat("_Metallic", 0.02f);
                    EditorUtility.SetDirty(existing);
                }

                return existing;
            }

            var source = LoadFirstMaterial();
            if (source == null)
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                source = new Material(urp != null ? urp : Shader.Find("Standard"));
            }

            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Presets"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                    AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Presets");
            }

            var mat = new Material(source) { name = "CaveSplineRock" };
            if (mat.HasProperty("_BaseMap"))
            {
                var scale = new Vector2(2.5f, 2.5f);
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", Vector2.zero);
            }

            if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null)
                mat.EnableKeyword("_NORMALMAP");

            mat.SetFloat("_Smoothness", 0.18f);
            mat.SetFloat("_Metallic", 0.02f);
            // Dark cave stone — was sand-beige (0.55, 0.52, 0.48) which made the cave read as a beach.
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", caveStone);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", caveStone);

            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", 0f);

            AssetDatabase.CreateAsset(mat, PresetPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material LoadFirstMaterial()
        {
            foreach (var path in SourceMaterialPaths)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                    return mat;
            }

            return null;
        }
    }
}
