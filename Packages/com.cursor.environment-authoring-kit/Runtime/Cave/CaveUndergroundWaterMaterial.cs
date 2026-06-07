using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Runtime loader for the committed cave water material (Ignite Simple Water Shader).</summary>
    public static class CaveUndergroundWaterMaterial
    {
        public const string AssetPath = "Assets/EnvironmentKit/Presets/CaveUndergroundWater_URP.mat";
        const string IgniteFallbackPath = "Assets/IgniteCoders/Simple Water Shader/Resources/Water_mat_01.mat";

        static Material _cached;

        public static void Cache(Material material)
        {
            if (material != null)
                _cached = material;
        }

        public static Material GetShared()
        {
            if (_cached != null)
                return _cached;

#if UNITY_EDITOR
            _cached = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(AssetPath);
            if (_cached == null)
                _cached = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(IgniteFallbackPath);
#endif
            return _cached;
        }

        public static void ApplyToRenderer(Renderer renderer)
        {
            if (renderer == null)
                return;

            var mat = GetShared();
            if (mat != null)
                renderer.sharedMaterial = mat;
        }
    }
}
