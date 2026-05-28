#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Imports only the Unity assets required for the current pipeline task (materials, prefabs, textures).
    /// Never bulk-imports Generated JSON/markdown (that caused 200+ synchronous imports and editor stalls).
    /// </summary>
    static class EnvironmentKitScopedAssetRefresh
    {
        public const string GeneratedFolder = "Assets/EnvironmentKit/Generated";
        public const string GeneratedPrefabsFolder = GeneratedFolder + "/Prefabs";

        static readonly string[] SkipExtensions =
        {
            ".json", ".md", ".txt", ".meta", ".log", ".csv", ".xml", ".yaml", ".yml",
        };

        static readonly string[] ImportExtensions =
        {
            ".prefab", ".mat", ".asset", ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff",
            ".exr", ".hdr", ".fbx", ".obj", ".shader", ".shadergraph", ".terrainlayer",
        };

        public static bool IsMetadataOnlyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return false;
            ext = ext.ToLowerInvariant();
            foreach (var skip in SkipExtensions)
            {
                if (ext == skip)
                    return true;
            }

            return false;
        }

        public static bool IsUnityAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path) || IsMetadataOnlyPath(path))
                return false;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ImportExtensions.Any(e => e == ext);
        }

        /// <summary>Import only these paths (prefab export, portals, etc.).</summary>
        public static int ImportAssetPaths(params string[] assetPaths) =>
            ImportAssetPathsNow(assetPaths);

        public static int ImportAssetPathsNow(params string[] assetPaths) =>
            ImportAssetPathsNow((IEnumerable<string>)assetPaths);

        public static int ImportAssetPathsNow(IEnumerable<string> assetPaths)
        {
            if (assetPaths == null)
                return 0;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in assetPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var trimmed = path.Trim();
                if (IsUnityAssetPath(trimmed))
                    set.Add(trimmed);
            }

            return ImportPathSet(set, "explicit paths");
        }

        public static int ImportForTaskNow(CaveBuildAssetImportTask task, LavaTubePrefabCatalog catalog = null)
        {
            switch (task)
            {
                case CaveBuildAssetImportTask.MaterialsPack:
                    return ImportMaterialsPackNow();
                case CaveBuildAssetImportTask.StructurePrefabs:
                    return ImportStructurePrefabsNow(catalog);
                case CaveBuildAssetImportTask.ScatterProps:
                    return ImportScatterPropsNow(catalog);
                case CaveBuildAssetImportTask.ExportedPrefabs:
                    return ImportExportedPrefabsNow();
                case CaveBuildAssetImportTask.None:
                default:
                    return 0;
            }
        }

        public static int ImportMaterialsPackNow()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in LavaTubePrefabCatalog.GetModuleAssetRoots())
                CollectAssetsUnder(root, "t:Material", paths);
            CollectAssetsUnder(LavaTubeMaterialUpgrader.PackRoot, "t:Material", paths);
            CollectAssetsUnder(LavaTubePrefabCatalog.MaterialsRoot, "t:Material", paths);

            var materialPaths = paths.ToList();
            foreach (var matPath in materialPaths)
                CollectMaterialTexturePaths(matPath, paths);

            return ImportPathSet(paths, "materials pack (floor/wall/ceiling textures)");
        }

        public static int ImportStructurePrefabsNow(LavaTubePrefabCatalog catalog)
        {
            if (catalog == null)
                return 0;

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectPrefabList(catalog.Floors, paths);
            CollectPrefabList(catalog.Walls, paths);
            CollectPrefabList(catalog.Ceilings, paths);
            CollectPrefabList(catalog.Rockfalls, paths);
            CollectPrefabList(catalog.Stalactites, paths);
            CollectPrefabList(catalog.Cupolas, paths);
            CollectPrefabList(catalog.Artifacts, paths);
            return ImportPathSet(paths, "structure prefabs (floor/wall/ceiling modules)");
        }

        public static int ImportPrefabListNow(IList<GameObject> prefabs)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectPrefabList(prefabs, paths);
            return ImportPathSet(paths, "prefab list");
        }

        public static int ImportScatterPropsNow(LavaTubePrefabCatalog catalog)
        {
            if (catalog == null)
                return 0;

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectPrefabList(catalog.Mushrooms, paths);
            CollectPrefabList(catalog.Crystals, paths);
            CollectPrefabList(catalog.MossProps, paths);
            CollectPrefabList(catalog.GlowProps, paths);
            CollectPrefabList(catalog.Rockfalls, paths);
            CollectPrefabList(catalog.Artifacts, paths);
            return ImportPathSet(paths, "scatter props");
        }

        public static int ImportExportedPrefabsNow()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedPrefabsFolder))
                return 0;

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { GeneratedPrefabsFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsUnityAssetPath(path))
                    paths.Add(path);
            }

            return ImportPathSet(paths, "exported generation prefabs");
        }

        static void CollectAssetsUnder(string folder, string filter, HashSet<string> paths)
        {
            if (!AssetDatabase.IsValidFolder(folder.TrimEnd('/')))
                return;

            foreach (var guid in AssetDatabase.FindAssets(filter, new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsUnityAssetPath(path))
                    paths.Add(path);
            }
        }

        static void CollectPrefabList(IList<GameObject> list, HashSet<string> paths)
        {
            if (list == null)
                return;

            foreach (var prefab in list)
            {
                if (prefab == null)
                    continue;
                var path = AssetDatabase.GetAssetPath(prefab);
                if (IsUnityAssetPath(path))
                    paths.Add(path);
            }
        }

        static void CollectMaterialTexturePaths(string materialPath, HashSet<string> paths)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null || mat.shader == null)
                return;

            var shader = mat.shader;
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                    continue;

                var tex = mat.GetTexture(shader.GetPropertyName(i));
                if (tex == null)
                    continue;
                var texPath = AssetDatabase.GetAssetPath(tex);
                if (IsUnityAssetPath(texPath))
                    paths.Add(texPath);
            }
        }

        static int ImportPathSet(HashSet<string> paths, string label)
        {
            if (paths == null || paths.Count == 0)
                return 0;

            CaveBuildSearchIndexGuard.EnsureSearchFolder();
            var imported = 0;
            foreach (var path in paths.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!File.Exists(AbsolutePath(path)))
                    continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                imported++;
            }

            if (imported > 0)
            {
                Debug.Log(
                    $"[CaveBuild] Task import ({label}): {imported} asset(s) — skipped JSON/markdown in Generated.");
            }

            return imported;
        }

        static string AbsolutePath(string assetPath) =>
            Path.Combine(Application.dataPath, "..", assetPath);
    }
}
#endif
