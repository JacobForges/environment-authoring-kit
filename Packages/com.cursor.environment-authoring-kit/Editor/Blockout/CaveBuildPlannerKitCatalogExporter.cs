#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Exports kit prefab pools + AssetPreview thumbnails for the build wizard concept cards.
    /// </summary>
    public static class CaveBuildPlannerKitCatalogExporter
    {
        public const string ManifestRel =
            "Assets/EnvironmentKit/ResearchCache/planner-kit-catalog/manifest.json";
        public const string ThumbsRel =
            "Assets/EnvironmentKit/ResearchCache/planner-kit-catalog/thumbs";

        [Serializable]
        public sealed class CatalogManifest
        {
            public int version = 1;
            public string exportedUtc;
            public Dictionary<string, List<string>> pools = new();
            public Dictionary<string, string> prefabThumbs = new();
        }

        [MenuItem("Window/Environment Kit/Export planner kit catalog")]
        public static void ExportMenu()
        {
            if (ExportIfStale(out var msg, force: true))
                EditorUtility.DisplayDialog("Planner kit catalog", msg, "OK");
            else
                EditorUtility.DisplayDialog("Planner kit catalog", msg ?? "Export failed.", "OK");
        }

        /// <summary>Called when opening the build wizard — refreshes thumbnails if missing.</summary>
        public static bool ExportIfStale(out string message, bool force = false)
        {
            message = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var manifestPath = Path.Combine(hub, ManifestRel);
            if (!force && File.Exists(manifestPath))
            {
                try
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(manifestPath);
                    if (age.TotalHours < 12)
                    {
                        message = "Catalog fresh — skipped export.";
                        return true;
                    }
                }
                catch
                {
                    // fall through to export
                }
            }

            try
            {
                var manifest = BuildManifest();
                var dir = Path.GetDirectoryName(manifestPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonUtility.ToJson(manifest, true);
                // JsonUtility cannot serialize Dictionary — write manually
                WriteManifestJson(manifestPath, manifest);
                AssetDatabase.Refresh();
                message =
                    $"Exported {manifest.prefabThumbs.Count} prefab thumbnail(s) " +
                    $"across {manifest.pools.Count} pool(s).";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        static CatalogManifest BuildManifest()
        {
            var manifest = new CatalogManifest
            {
                exportedUtc = DateTime.UtcNow.ToString("o"),
                pools = new Dictionary<string, List<string>>(),
                prefabThumbs = new Dictionary<string, string>(),
            };

            var roots = new[]
            {
                CaveBuildPlannerGeneratedProps.PrefabRoot,
                BiomePropCatalog.LpMagicalForestRoot,
                "Assets/EnvironmentKit/CC0Imports/Prefabs/Characters",
                "Assets/EnvironmentKit/CC0Imports/Prefabs/Items",
                "Assets/GameData/Prefabs",
            };

            var all = new HashSet<string>();
            foreach (var root in roots)
            {
                if (!AssetDatabase.IsValidFolder(root))
                    continue;

                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        continue;
                    all.Add(path.Replace('\\', '/'));
                }
            }

            foreach (var path in all.OrderBy(p => p))
            {
                var bucket = Classify(path);
                if (!manifest.pools.TryGetValue(bucket, out var list))
                {
                    list = new List<string>();
                    manifest.pools[bucket] = list;
                }

                list.Add(path);
                if (bucket.StartsWith("prop_", StringComparison.Ordinal))
                {
                    if (!manifest.pools.TryGetValue("prop", out var union))
                    {
                        union = new List<string>();
                        manifest.pools["prop"] = union;
                    }

                    if (!union.Contains(path))
                        union.Add(path);
                }

                try
                {
                    var thumbRel = ExportThumb(path);
                    if (!string.IsNullOrEmpty(thumbRel))
                        manifest.prefabThumbs[path] = thumbRel;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlannerKitCatalog] Thumb skipped for {path}: {ex.Message}");
                }
            }

            return manifest;
        }

        static string Classify(string path)
        {
            var low = path.ToLowerInvariant();
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (low.Contains("/characters/") || name.StartsWith("npc_"))
                return name.StartsWith("enemy_") || name.StartsWith("boss_") ? "enemy" : "npc";
            if (low.Contains("/items/"))
                return "collectible";
            if (name.Contains("tree"))
                return "prop_tree";
            if (name.Contains("grass") || name.Contains("flower") || name.Contains("plant"))
                return "prop_grass";
            if (name.Contains("rock") || name.Contains("stone") || name.Contains("boulder"))
                return "prop_rock";
            if (name.Contains("bush"))
                return "prop_bush";
            return "prop";
        }

        /// <summary>Public entry for per-card mesh generation + concept preview refresh.</summary>
        public static string ExportThumbnailForPrefab(string prefabPath) => ExportThumb(prefabPath);

        const int PreviewPixels = 320;

        static string ExportThumb(string prefabPath)
        {
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    return null;

                var norm = prefabPath.Replace('\\', '/');
                var isGenerated = norm.Contains(
                    "EnvironmentKit/Generated/PlannerProps/",
                    StringComparison.OrdinalIgnoreCase);

                Texture2D tex = null;
                if (isGenerated)
                {
                    AssetDatabase.Refresh();
                    tex = TryAssetPreviewTexture(prefab);
                }
                else
                    tex = TryAssetPreviewTexture(prefab);

                if (tex == null)
                    tex = CapturePrefabWithPreviewUtility(prefab);
                if (tex == null || LooksLikeEmptyPreview(tex as Texture2D))
                    return null;

                tex = NormalizePreviewTexture(tex);
                if (tex == null || LooksLikeEmptyPreview(tex))
                    return null;

                var safe = prefabPath.Replace('/', '_').Replace('\\', '_');
                var rel = $"{ThumbsRel}/{safe}.png";
                var abs = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), rel);
                Directory.CreateDirectory(Path.GetDirectoryName(abs) ?? string.Empty);

                var png = EncodePreviewPng(tex);
                if (png == null || png.Length == 0)
                    return null;

                File.WriteAllBytes(abs, png);
                return rel.Replace('\\', '/');
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlannerKitCatalog] ExportThumb failed for {prefabPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Wait for AssetPreview — never accept GetMiniThumbnail (always the package icon).</summary>
        static Texture2D TryAssetPreviewTexture(GameObject prefab)
        {
            AssetPreview.SetPreviewTextureCacheSize(512);
            for (var attempt = 0; attempt < 160; attempt++)
            {
                if (AssetPreview.IsLoadingAssetPreview(prefab.GetInstanceID()))
                {
                    PumpEditor(attempt);
                    continue;
                }

                var tex = AssetPreview.GetAssetPreview(prefab) as Texture2D;
                if (tex != null && tex.width > 0 && tex.height > 0)
                    return tex;

                PumpEditor(attempt);
            }

            return null;
        }

        static void PumpEditor(int attempt)
        {
            if (!Application.isBatchMode && attempt % 8 == 0)
                EditorApplication.QueuePlayerLoopUpdate();
            System.Threading.Thread.Sleep(30);
        }

        /// <summary>Render the prefab in a lit preview scene when AssetPreview is not ready.</summary>
        static Texture2D CapturePrefabWithPreviewUtility(GameObject prefab)
        {
            var utility = new PreviewRenderUtility(true);
            try
            {
                var instance = utility.InstantiatePrefabInScene(prefab);
                if (instance == null)
                    return null;

                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(-14f, 36f, 0f));

                var bounds = CalculateRenderBounds(instance);
                instance.transform.position = -bounds.center;
                bounds = CalculateRenderBounds(instance);

                var maxExtent = Mathf.Max(
                    0.02f,
                    bounds.extents.x,
                    Mathf.Max(bounds.extents.y, bounds.extents.z));
                var distance = maxExtent * 3.4f + 0.35f;
                var direction = new Vector3(0.38f, 0.44f, -1f).normalized;
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.backgroundColor = new Color(24f / 255f, 28f / 255f, 38f / 255f, 1f);
                utility.camera.transform.position = bounds.center + direction * distance;
                utility.camera.transform.LookAt(bounds.center);
                utility.camera.nearClipPlane = 0.01f;
                utility.camera.farClipPlane = distance * 6f;
                utility.camera.fieldOfView = 30f;
                utility.camera.stereoTargetEye = StereoTargetEyeMask.None;

                ConfigurePreviewLights(utility);
                utility.ambientColor = new Color(0.42f, 0.46f, 0.54f, 1f);

                for (var frame = 0; frame < 6; frame++)
                {
                    if (!Application.isBatchMode)
                        EditorApplication.QueuePlayerLoopUpdate();
                    System.Threading.Thread.Sleep(35);
                }

                var tex = TryCaptureRender(utility, useScriptableRenderPipeline: true);
                if (tex == null)
                    tex = TryCaptureRender(utility, useScriptableRenderPipeline: false);
                return tex;
            }
            finally
            {
                utility.Cleanup();
            }
        }

        static void ConfigurePreviewLights(PreviewRenderUtility utility)
        {
            if (utility.lights == null || utility.lights.Length == 0)
                return;

            utility.lights[0].intensity = 1.2f;
            utility.lights[0].transform.rotation = Quaternion.Euler(50f, 38f, 0f);
            if (utility.lights.Length > 1)
            {
                utility.lights[1].intensity = 0.6f;
                utility.lights[1].transform.rotation = Quaternion.Euler(335f, 215f, 175f);
            }
        }

        static Texture2D TryCaptureRender(PreviewRenderUtility utility, bool useScriptableRenderPipeline)
        {
            utility.BeginPreview(new Rect(0, 0, PreviewPixels, PreviewPixels), GUIStyle.none);
            utility.Render(useScriptableRenderPipeline, false);
            var tex = utility.EndPreview() as Texture2D;
            if (tex == null || LooksLikeEmptyPreview(tex))
                return null;
            return tex;
        }

        static Texture2D NormalizePreviewTexture(Texture2D src)
        {
            if (src == null)
                return null;
            if (src.width >= PreviewPixels && src.height >= PreviewPixels)
                return src;

            var rt = RenderTexture.GetTemporary(PreviewPixels, PreviewPixels, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Texture2D normalized = null;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(24f / 255f, 28f / 255f, 38f / 255f, 1f));
                Graphics.Blit(src, rt);
                normalized = new Texture2D(PreviewPixels, PreviewPixels, TextureFormat.RGBA32, false);
                normalized.ReadPixels(new Rect(0, 0, PreviewPixels, PreviewPixels), 0, 0);
                normalized.Apply();
                return normalized;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static bool LooksLikeEmptyPreview(Texture2D tex)
        {
            if (tex == null || tex.width < 32 || tex.height < 32)
                return true;

            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                readable.Apply();

                var pixels = readable.GetPixels32();
                var bright = 0;
                var dark = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var luma = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
                    if (luma > 28f)
                        bright++;
                    if (luma < 12f)
                        dark++;
                }

                UnityEngine.Object.DestroyImmediate(readable);
                var total = pixels.Length;
                return bright < total * 0.02f || dark > total * 0.97f;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Bounds CalculateRenderBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.5f);

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>AssetPreview textures are often not readable — blit to a temp RT first.</summary>
        static byte[] EncodePreviewPng(Texture2D src)
        {
            if (src == null)
                return null;

            var w = Mathf.Max(8, src.width);
            var h = Mathf.Max(8, src.height);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readable.Apply();
                return readable.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        static void WriteManifestJson(string path, CatalogManifest manifest)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"version\": {manifest.version},");
            sb.AppendLine($"  \"exportedUtc\": \"{manifest.exportedUtc}\",");
            sb.AppendLine("  \"pools\": {");
            var poolKeys = manifest.pools.Keys.OrderBy(k => k).ToList();
            for (var i = 0; i < poolKeys.Count; i++)
            {
                var key = poolKeys[i];
                var items = manifest.pools[key];
                sb.Append($"    \"{key}\": [");
                for (var j = 0; j < items.Count; j++)
                {
                    sb.Append($"\"{items[j]}\"");
                    if (j < items.Count - 1)
                        sb.Append(", ");
                }

                sb.Append("]");
                sb.AppendLine(i < poolKeys.Count - 1 ? "," : string.Empty);
            }

            sb.AppendLine("  },");
            sb.AppendLine("  \"prefabThumbs\": {");
            var thumbKeys = manifest.prefabThumbs.Keys.OrderBy(k => k).ToList();
            for (var i = 0; i < thumbKeys.Count; i++)
            {
                var key = thumbKeys[i];
                sb.Append($"    \"{key}\": \"{manifest.prefabThumbs[key]}\"");
                sb.AppendLine(i < thumbKeys.Count - 1 ? "," : string.Empty);
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
#endif
