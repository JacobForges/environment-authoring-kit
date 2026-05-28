using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Converts BillemotdonggulLavaTubePack materials from Unreal/Built-in shaders to URP Lit
    /// so they render correctly (fixes magenta/pink error shader on URP).
    /// </summary>
    public static class LavaTubeMaterialUpgrader
    {
        public const string PackRoot = "Assets/BillemotdonggulLavaTubePack";
        public const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        static bool _packUpgradedThisSession;
        static bool _deferredAssetFlush;

        static readonly string[] AlbedoPropertyNames =
        {
            "Material_Texture2D_1", "_BaseMap", "_MainTex"
        };

        static readonly string[] NormalPropertyNames =
        {
            "Material_Texture2D_0", "_BumpMap", "_NormalMap"
        };

        static readonly string[] OcclusionPropertyNames =
        {
            "Material_Texture2D_2", "_OcclusionMap"
        };

        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Fix Lava Tube Materials (URP)")]
        public static void UpgradeAllFromMenu()
        {
            var count = UpgradeAllPackMaterials();
            var caveRoot = GameObject.Find("LavaTubeCaveSystem");
            var sceneMsg = string.Empty;
            if (caveRoot != null)
            {
                var report = CaveSceneMaterialRepair.RepairCaveRoot(caveRoot.transform);
                sceneMsg =
                    $"\n\nScene repair: removed {report.BlockoutRemoved} blockout caps, " +
                    $"fixed {report.RenderersFixed} renderer(s), tiled {report.TilingApplied} module(s).";
            }

            EditorUtility.DisplayDialog(
                "Lava Tube Materials",
                $"Upgraded {count} material(s) under {PackRoot} to URP/Lit with textures wired.{sceneMsg}",
                "OK");
        }

        /// <summary>Runs pack URP upgrade once per editor session unless <paramref name="force"/>.</summary>
        public static void EnsurePackMaterialsUpgraded(bool force = false)
        {
            if (!force && _packUpgradedThisSession)
                return;

            UpgradeAllPackMaterialsInternal(forceMenuLog: false);
            _packUpgradedThisSession = true;
        }

        public static int UpgradeAllPackMaterials() =>
            UpgradeAllPackMaterialsInternal(forceMenuLog: true);

        static int UpgradeAllPackMaterialsInternal(bool forceMenuLog)
        {
            var urpLit = Shader.Find(UrpLitShaderName);
            if (urpLit == null)
            {
                Debug.LogError("[LavaTubeMaterialUpgrader] URP Lit shader not found. Is URP installed?");
                return 0;
            }

            var upgraded = 0;
            var searched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in LavaTubePrefabCatalog.GetModuleAssetRoots())
            {
                if (!searched.Add(root))
                    continue;

                var guids = AssetDatabase.FindAssets("t:Material", new[] { root });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)
                        continue;

                    if (UpgradeMaterial(mat, urpLit))
                        upgraded++;
                }
            }

            if (upgraded > 0)
            {
                if (CaveEditorUndo.IsBulkBuild)
                    _deferredAssetFlush = true;
                else
                    FlushAssetChanges();
            }

            if (forceMenuLog || upgraded > 0)
                Debug.Log($"[LavaTubeMaterialUpgrader] Upgraded {upgraded} material(s).");

            return upgraded;
        }

        /// <summary>Call once after bulk cave generation finishes (deferred SaveAssets/Refresh).</summary>
        public static void FlushDeferredAssetChanges()
        {
            if (!_deferredAssetFlush)
                return;

            _deferredAssetFlush = false;
            FlushAssetChanges();
        }

        static void FlushAssetChanges()
        {
            AssetDatabase.SaveAssets();
            EnvironmentKitScopedAssetRefresh.ImportMaterialsPackNow();
        }

        public static void UpgradeRenderersUnder(Transform root)
        {
            if (root == null)
                return;

            CaveSceneMaterialRepair.RepairCaveRoot(root);
        }

        public static bool UpgradeMaterial(Material mat, Shader urpLit)
        {
            if (mat == null || urpLit == null)
                return false;

            var albedo = GetFirstTexture(mat, AlbedoPropertyNames);
            var normal = GetFirstTexture(mat, NormalPropertyNames);
            var occlusion = GetFirstTexture(mat, OcclusionPropertyNames);

            if (albedo == null)
                albedo = FindPackTexture(mat.name, "_BC");
            if (normal == null)
                normal = FindPackTexture(mat.name, "_NM");
            if (occlusion == null)
                occlusion = FindPackTexture(mat.name, "_OC");

            var needsShader = mat.shader == null ||
                              mat.shader.name.StartsWith("Unreal/") ||
                              mat.shader.name.Contains("Hidden/InternalErrorShader") ||
                              !mat.shader.name.Contains("Universal");

            var needsTextures = albedo != null && mat.GetTexture("_BaseMap") != albedo;
            var missingBase = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null && albedo != null;

            if (!needsShader && !needsTextures && !missingBase && mat.shader == urpLit)
                return false;

            if (!CaveEditorUndo.IsBulkBuild)
                Undo.RecordObject(mat, "Upgrade Lava Tube Material");

            mat.shader = urpLit;

            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTexture("_MainTex", albedo);
                mat.SetColor("_BaseColor", Color.white);
            }

            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (occlusion != null)
            {
                mat.SetTexture("_OcclusionMap", occlusion);
                mat.SetFloat("_OcclusionStrength", 1f);
            }

            mat.SetFloat("_Smoothness", 0.35f);
            mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;

            EditorUtility.SetDirty(mat);
            return true;
        }

        static Texture GetFirstTexture(Material mat, string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (!mat.HasProperty(name))
                    continue;
                var tex = mat.GetTexture(name);
                if (tex != null)
                    return tex;
            }

            return null;
        }

        static Texture FindPackTexture(string materialName, string suffix)
        {
            var baseName = MaterialNameToTextureBase(materialName);
            if (string.IsNullOrEmpty(baseName))
                return null;

            foreach (var root in LavaTubePrefabCatalog.GetModuleAssetRoots())
            {
                var candidates = new[]
                {
                    $"{root}/Texture/T_{baseName}{suffix}.png",
                    $"{root}/Textures/T_{baseName}{suffix}.png",
                    $"{root}/Material/T_{baseName}{suffix}.png",
                    $"{root}/Materials/T_{baseName}{suffix}.png",
                    $"{PackRoot}/Texture/T_{baseName}{suffix}.png",
                };

                foreach (var path in candidates)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null)
                        return tex;
                }
            }

            return null;
        }

        static string MaterialNameToTextureBase(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return null;

            var name = materialName;
            if (name.StartsWith("MI_"))
                name = name.Substring(3);
            else if (name.StartsWith("M_"))
                name = name.Substring(2);

            // Pack typo: MI_Ceiling6G → T_Ceiling06G_*
            if (name.StartsWith("Ceiling6") && !name.StartsWith("Ceiling06"))
                name = "Ceiling0" + name.Substring("Ceiling".Length);

            return name;
        }

        public static bool IsBrokenMaterial(Material mat)
        {
            if (mat == null)
                return true;

            if (mat.shader == null)
                return true;

            var shaderName = mat.shader.name;
            if (shaderName.Contains("InternalErrorShader") || shaderName.StartsWith("Unreal/"))
                return true;

            if (GraphicsSettings.currentRenderPipeline != null &&
                !shaderName.Contains("Universal") &&
                !shaderName.Contains("Shader Graphs"))
                return true;

            return mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null &&
                   GetFirstTexture(mat, AlbedoPropertyNames) == null;
        }
    }
}
