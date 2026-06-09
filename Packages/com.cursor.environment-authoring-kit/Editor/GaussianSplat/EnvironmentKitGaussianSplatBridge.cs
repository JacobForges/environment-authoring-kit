#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using EnvironmentAuthoringKit.GaussianSplat;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.GaussianSplat
{
    /// <summary>
    /// Optional UnityGaussianSplatting (aras-p) — no compile-time dependency; reflection when installed.
    /// </summary>
    static class EnvironmentKitGaussianSplatBridge
    {
        const string PackageGitUrl =
            "https://github.com/aras-p/UnityGaussianSplatting.git?path=/package";

        public static bool IsPackagePresent =>
            FindType("GaussianSplatRenderer") != null;

        public static Type ResolveSplatAssetType() =>
            FindType("GaussianSplatAsset") ?? typeof(UnityEngine.Object);

        public static void OpenInstallInstructions()
        {
            EditorUtility.DisplayDialog(
                "UnityGaussianSplatting (optional)",
                "Environment Kit does not bundle 3D Gaussian Splatting.\n\n" +
                "1. Unity 6 + URP/HDRP/BiRP supported\n" +
                "2. Package Manager → Add package from git URL:\n" +
                PackageGitUrl + "\n" +
                "3. Tools → Gaussian Splats → Create asset (PLY/SPZ)\n" +
                "4. Enable hero splat on CaveBuildCursorSettings\n\n" +
                "See docs/GAUSSIAN_SPLAT_INTEGRATION.md",
                "OK");
            Application.OpenURL("https://github.com/aras-p/UnityGaussianSplatting");
        }

        public static bool TryAttachRenderer(GaussianSplatHeroSlot slot, UnityEngine.Object splatAsset, out string message)
        {
            message = null;
            if (slot == null)
            {
                message = "No hero slot.";
                return false;
            }

            var rendererType = FindType("GaussianSplatRenderer");
            if (rendererType == null)
            {
                message =
                    "UnityGaussianSplatting not installed — hero marker placed only. " +
                    "Window → Environment Kit → Cave Build → Advanced → Install Gaussian Splat Package Help.";
                return false;
            }

            var renderer = slot.GetComponent(rendererType);
            if (renderer == null)
                renderer = Undo.AddComponent(slot.gameObject, rendererType);

            if (splatAsset != null)
                TrySetMember(renderer, "m_Asset", splatAsset);

            TrySetMember(renderer, "enabled", slot.allowRendering);
            slot.ApplyRenderingPolicy();
            message = "GaussianSplatRenderer attached.";
            return true;
        }

        public static UnityEngine.Object LoadSplatAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            var path = assetPath.Trim();
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                path = "Assets/" + path.TrimStart('/');

            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        }

        static void TrySetMember(object target, string memberName, object value)
        {
            if (target == null)
                return;

            var type = target.GetType();
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
                prop.SetValue(target, value);
        }

        static Type FindType(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var direct = asm.GetType(simpleName, throwOnError: false);
                    if (direct != null)
                        return direct;

                    var match = asm.GetTypes().FirstOrDefault(t => t.Name == simpleName);
                    if (match != null)
                        return match;
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (var t in ex.Types)
                    {
                        if (t != null && t.Name == simpleName)
                            return t;
                    }
                }
            }

            return null;
        }
    }
}
#endif
