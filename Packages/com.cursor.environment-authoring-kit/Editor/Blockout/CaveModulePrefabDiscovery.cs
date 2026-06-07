using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Scans all of Assets/ for 3D prefabs usable as cave floor/wall/ceiling modules and props.
    /// No store-specific folder required.
    /// </summary>
    static class CaveModulePrefabDiscovery
    {
        static readonly string[] SkipPathFragments =
        {
            "/slimui/", "/modern menu/", "/ganzse/", "/character_miho/", "/suimono/",
            "/canvas", "/button", "/btn_", "/ui/", "/hud/", "/gameobject/",
            "/gamedata/prefabs/caveenemy", "/editor/", "/tutorial/",
            "/textmesh pro/", "/plugins/", "/settings/", "/packages/",
            "/environmentkit/", "/samples/ui", "/demo/ui",
            "/buildings/", "/building/", "/architecture/", "/house/", "/housing/",
            "/town/", "/city/", "/village/", "/furniture/", "/interior/",
            "/office/", "/shop/", "/store/", "/street/", "/sidewalk/",
        };

        static readonly string[] SkipNameFragments =
        {
            "canvas", "button", "btn_", "eventsystem", "player", "character", "avatar",
            "camera", "lightprobe", "reflectionprobe", "navmesh", "postprocess",
            "pickup_truck", "car_", "vehicle", "billboard", "spawnpoint",
        };

        static readonly string[] TreeFragments =
        {
            "/trees/", "/tree/", "tree_", "_tree", "pine_", "oak_", "poplar_", "birch_",
            "palm_", "spruce_", "cedar_", "willow_", "fruit_tree",
        };

        public static void ScanProject(LavaTubePrefabCatalog catalog, List<GameObject> unclassifiedModules)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkipPath(path))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !HasRenderableMesh(prefab))
                    continue;

                if (LavaTubePrefabCatalog.IsAlreadyInCatalog(catalog, prefab))
                    continue;

                var name = prefab.name;
                if (LavaTubePrefabCatalog.TryClassifyModuleFromAsset(catalog, path, name, prefab))
                    continue;

                if (IsTreeLike(path, name))
                {
                    LavaTubePrefabCatalog.AddProp(catalog, path, name, prefab);
                    continue;
                }

                unclassifiedModules.Add(prefab);
            }
        }

        public static void CollectMaterialRootsFromCatalog(LavaTubePrefabCatalog catalog, HashSet<string> roots)
        {
            void Walk(IEnumerable<GameObject> list)
            {
                foreach (var go in list)
                {
                    if (go == null)
                        continue;

                    var path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    AddPackRootsForPath(path, roots);
                }
            }

            Walk(catalog.Floors);
            Walk(catalog.Walls);
            Walk(catalog.Ceilings);
            Walk(catalog.Rockfalls);
            Walk(catalog.Cupolas);
            Walk(catalog.Stalactites);
            Walk(catalog.Artifacts);
            Walk(catalog.Mushrooms);
            Walk(catalog.Crystals);
            Walk(catalog.MossProps);
        }

        public static void AddPackRootsForPath(string assetPath, HashSet<string> roots)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(dir) && dir.StartsWith("Assets", StringComparison.Ordinal))
            {
                roots.Add(dir);
                var parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(parent) || parent == dir)
                    break;

                if (IsLikelyPackRoot(parent))
                {
                    roots.Add(parent);
                    break;
                }

                dir = parent;
            }
        }

        static bool IsLikelyPackRoot(string folder)
        {
            var name = Path.GetFileName(folder) ?? string.Empty;
            if (name.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return true;

            return name.Contains("Pack", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Assets", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Cave", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Dungeon", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Environment", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Modular", StringComparison.OrdinalIgnoreCase);
        }

        static bool ShouldSkipPath(string path)
        {
            var lower = path.ToLowerInvariant();
            foreach (var frag in SkipPathFragments)
            {
                if (lower.Contains(frag, StringComparison.Ordinal))
                    return true;
            }

            var file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            foreach (var frag in SkipNameFragments)
            {
                if (file.Contains(frag, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static bool IsTreeLike(string path, string name)
        {
            var blob = (path + "/" + name).ToLowerInvariant();
            foreach (var frag in TreeFragments)
            {
                if (blob.Contains(frag, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static bool HasRenderableMesh(GameObject prefab) =>
            prefab.GetComponentInChildren<MeshFilter>(true) != null ||
            prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
    }
}
