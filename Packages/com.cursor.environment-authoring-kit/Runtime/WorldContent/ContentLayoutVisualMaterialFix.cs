using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Upgrade CC0 instance materials to URP Lit so props/NPCs are not white/pink.</summary>
    public static class ContentLayoutVisualMaterialFix
    {
        static Shader _urpLit;

        public static void FixVisualMaterials(GameObject root)
        {
            if (root == null)
                return;

            _urpLit ??= Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (_urpLit == null)
                return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                var mats = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || !NeedsUpgrade(src))
                        continue;

                    mats[i] = BuildUpgradedMaterial(src);
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = mats;
            }
        }

        static bool NeedsUpgrade(Material mat)
        {
            if (mat.shader == null)
                return true;

            var shaderName = mat.shader.name ?? string.Empty;
            if (shaderName.Contains("InternalErrorShader", System.StringComparison.Ordinal))
                return true;
            if (shaderName.StartsWith("Unreal/", System.StringComparison.Ordinal))
                return true;
            if (shaderName.Contains("Universal", System.StringComparison.Ordinal))
            {
                if (!mat.HasProperty("_BaseMap") || mat.GetTexture("_BaseMap") != null)
                    return false;

                var albedo = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                albedo ??= mat.mainTexture;
                return albedo != null;
            }

            return true;
        }

        static Material BuildUpgradedMaterial(Material src)
        {
            var mat = new Material(_urpLit);
            var albedo = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
            albedo ??= src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            albedo ??= src.mainTexture;

            if (albedo != null)
            {
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", albedo);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", albedo);
            }

            if (mat.HasProperty("_BaseColor"))
            {
                var c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color;
                mat.SetColor("_BaseColor", c);
            }

            if (src.HasProperty("_BumpMap") && mat.HasProperty("_BumpMap"))
            {
                var normal = src.GetTexture("_BumpMap");
                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            return mat;
        }
    }
}
