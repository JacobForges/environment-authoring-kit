#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    public static class WorldSurfaceWaterUtility
    {
        public const string SurfaceWaterMatPath = "Assets/EnvironmentKit/Presets/SurfaceWater_URP.mat";

        public static Material GetOrCreateSurfaceWater()
        {
            var preset = AssetDatabase.LoadAssetAtPath<Material>(SurfaceWaterMatPath);
            if (preset != null)
                return preset;

            var ignite = CaveWaterMaterialFactory.GetOrCreate();
            if (ignite != null)
            {
                var copy = new Material(ignite) { name = "SurfaceWater_URP" };
                if (copy.HasProperty("_BaseColor"))
                    copy.SetColor("_BaseColor", new Color(0.12f, 0.42f, 0.55f, 0.82f));
                AssetDatabase.CreateAsset(copy, SurfaceWaterMatPath);
                AssetDatabase.SaveAssets();
                return copy;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                return null;

            var mat = new Material(shader) { name = "SurfaceWater_URP" };
            var c = new Color(0.1f, 0.38f, 0.52f, 0.75f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.94f);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0.05f);
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            AssetDatabase.CreateAsset(mat, SurfaceWaterMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
#endif
