using System;
using System.Collections.Generic;
using System.Linq;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Environment module + prop prefabs from configured folders (any licensed pack naming).
    /// </summary>
    public sealed class LavaTubePrefabCatalog
    {
        public const string DefaultPrefabRoot = "Assets/BillemotdonggulLavaTubePack/Prefabs/";
        public const string MaterialsRoot = "Assets/BillemotdonggulLavaTubePack/Material/";
        public const string DefaultPropRoot = "Assets/PolitePenguin/LPMagicalForest/Prefabs/";
        public const string PrefabRoot = DefaultPrefabRoot;
        public const string PropRoot = DefaultPropRoot;

        public readonly List<GameObject> Floors = new();
        public readonly List<GameObject> Walls = new();
        public readonly List<GameObject> Ceilings = new();
        public readonly List<GameObject> Rockfalls = new();
        public readonly List<GameObject> Artifacts = new();
        public readonly List<GameObject> Cupolas = new();
        public readonly List<GameObject> Stalactites = new();
        public readonly List<GameObject> Mushrooms = new();
        public readonly List<GameObject> Crystals = new();
        public readonly List<GameObject> MossProps = new();
        public readonly List<GameObject> GlowProps = new();
        public readonly List<GameObject> WaterProps = new();

        public int ModulePrefabCount =>
            Floors.Count + Walls.Count + Ceilings.Count + Rockfalls.Count + Cupolas.Count + Stalactites.Count;

        public bool IsValid => Floors.Count > 0 && Walls.Count > 0 && Ceilings.Count > 0;

        static LavaTubePrefabCatalog _cached;

        public static LavaTubePrefabCatalog Load(bool forceRefresh = false)
        {
            if (!forceRefresh && _cached != null && _cached.IsValid)
                return _cached;

            var catalog = new LavaTubePrefabCatalog();
            var unclassifiedModules = new List<GameObject>();

            foreach (var folder in ParseFolders(EnvironmentKitSettings.CaveLavaPrefabFolders, DefaultPrefabRoot))
                LoadFolder(catalog, folder, lavaTube: true, unclassifiedModules);

            foreach (var folder in ParseFolders(EnvironmentKitSettings.CavePropPrefabFolders, DefaultPropRoot))
                LoadFolder(catalog, folder, lavaTube: false, unclassifiedModules);

            if (EnvironmentKitSettings.CaveScanAllAssets)
                FantasyCavePrefabCatalog.Populate(catalog);

            FinalizeModuleCatalog(catalog, unclassifiedModules);
            SortAll(catalog);
            _cached = catalog;

            Debug.Log(
                $"[CaveCatalog] floors={catalog.Floors.Count} walls={catalog.Walls.Count} ceilings={catalog.Ceilings.Count} " +
                $"rocks={catalog.Rockfalls.Count} mushrooms={catalog.Mushrooms.Count} crystals={catalog.Crystals.Count} " +
                $"props={catalog.MossProps.Count} valid={catalog.IsValid} " +
                $"lavaFolders='{EnvironmentKitSettings.CaveLavaPrefabFolders}' propFolders='{EnvironmentKitSettings.CavePropPrefabFolders}'");

            return catalog;
        }

        public static IEnumerable<string> GetConfiguredModuleFolders() =>
            ParseFolders(EnvironmentKitSettings.CaveLavaPrefabFolders, DefaultPrefabRoot);

        /// <summary>Pack roots (folder + parent) for materials/textures under user-configured module paths.</summary>
        public static IEnumerable<string> GetModuleAssetRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in GetConfiguredModuleFolders())
            {
                var f = folder.Trim().TrimEnd('/');
                if (!AssetDatabase.IsValidFolder(f))
                    continue;

                roots.Add(f);
                var parent = System.IO.Path.GetDirectoryName(f)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parent) && parent.StartsWith("Assets", StringComparison.Ordinal))
                    roots.Add(parent);
            }

            if (roots.Count == 0 && AssetDatabase.IsValidFolder("Assets/BillemotdonggulLavaTubePack"))
                roots.Add("Assets/BillemotdonggulLavaTubePack");

            return roots;
        }

        public static bool IsUnderModuleAssetRoots(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            var p = assetPath.Replace('\\', '/');
            foreach (var root in GetModuleAssetRoots())
            {
                if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static IEnumerable<string> ParseFolders(string raw, string fallback)
        {
            var parsed = (raw ?? string.Empty)
                .Split(';')
                .Select(s => (s ?? string.Empty).Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct();

            var hasAny = false;
            foreach (var p in parsed)
            {
                hasAny = true;
                yield return p;
            }

            if (!hasAny)
                yield return fallback;
        }

        static void LoadFolder(
            LavaTubePrefabCatalog catalog,
            string folder,
            bool lavaTube,
            List<GameObject> unclassifiedModules)
        {
            if (!AssetDatabase.IsValidFolder(folder.TrimEnd('/')))
                return;

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                if (lavaTube)
                {
                    if (!TryClassifyModulePrefab(catalog, path, name, prefab))
                        unclassifiedModules.Add(prefab);
                }
                else
                    ClassifyProp(catalog, path, name, prefab);
            }
        }

        static bool TryClassifyModulePrefab(LavaTubePrefabCatalog catalog, string path, string name, GameObject prefab)
        {
            if (name.StartsWith("SM_Floor", StringComparison.Ordinal))
            {
                catalog.Floors.Add(prefab);
                return true;
            }

            if (name.StartsWith("SM_Wall", StringComparison.Ordinal))
            {
                catalog.Walls.Add(prefab);
                return true;
            }

            if (name.StartsWith("SM_Ceiling", StringComparison.Ordinal))
            {
                catalog.Ceilings.Add(prefab);
                return true;
            }

            if (name.StartsWith("SM_Rockfall", StringComparison.Ordinal))
            {
                catalog.Rockfalls.Add(prefab);
                return true;
            }

            if (name.StartsWith("SM_Artifact", StringComparison.Ordinal))
            {
                catalog.Artifacts.Add(prefab);
                return true;
            }

            if (name.StartsWith("SM_Cupola", StringComparison.Ordinal))
            {
                catalog.Cupolas.Add(prefab);
                return true;
            }

            if (name.Contains("stalactite", StringComparison.OrdinalIgnoreCase))
            {
                catalog.Stalactites.Add(prefab);
                return true;
            }

            var blob = (path + "/" + name).ToLowerInvariant();
            if (ContainsAny(blob, "floor", "ground", "walkway", "platform", "path_tile", "pavement"))
            {
                catalog.Floors.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "ceiling", "roof", "overhead", "top_cap", "cavern_top"))
            {
                catalog.Ceilings.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "wall", "siding", "cliff_face", "pillar", "column", "barrier"))
            {
                catalog.Walls.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "rockfall", "rubble", "debris", "boulder"))
            {
                catalog.Rockfalls.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "tunnel", "corridor", "cave", "cavern", "hall", "tube", "module", "segment", "passage"))
            {
                var role = ClassifyByMeshShape(prefab);
                switch (role)
                {
                    case ModuleShapeRole.Floor:
                        catalog.Floors.Add(prefab);
                        return true;
                    case ModuleShapeRole.Ceiling:
                        catalog.Ceilings.Add(prefab);
                        return true;
                    case ModuleShapeRole.Wall:
                        catalog.Walls.Add(prefab);
                        return true;
                }
            }

            return false;
        }

        static void FinalizeModuleCatalog(LavaTubePrefabCatalog catalog, List<GameObject> unclassified)
        {
            var pool = unclassified
                .Where(p => p != null && !IsAlreadyCataloged(catalog, p))
                .Distinct()
                .ToList();

            foreach (var prefab in pool.ToList())
            {
                var role = ClassifyByMeshShape(prefab);
                switch (role)
                {
                    case ModuleShapeRole.Floor:
                        catalog.Floors.Add(prefab);
                        pool.Remove(prefab);
                        break;
                    case ModuleShapeRole.Wall:
                        catalog.Walls.Add(prefab);
                        pool.Remove(prefab);
                        break;
                    case ModuleShapeRole.Ceiling:
                        catalog.Ceilings.Add(prefab);
                        pool.Remove(prefab);
                        break;
                }
            }

            EnsureRoleFilled(catalog, catalog.Floors, pool, ModuleShapeRole.Floor);
            EnsureRoleFilled(catalog, catalog.Walls, pool, ModuleShapeRole.Wall);
            EnsureRoleFilled(catalog, catalog.Ceilings, pool, ModuleShapeRole.Ceiling);

            foreach (var prefab in pool)
            {
                if (prefab == null || IsAlreadyCataloged(catalog, prefab))
                    continue;
                catalog.Rockfalls.Add(prefab);
            }
        }

        static void EnsureRoleFilled(
            LavaTubePrefabCatalog catalog,
            List<GameObject> roleList,
            List<GameObject> pool,
            ModuleShapeRole role)
        {
            if (roleList.Count > 0)
                return;

            GameObject best = null;
            var bestScore = float.MinValue;
            foreach (var prefab in pool)
            {
                if (prefab == null || IsAlreadyCataloged(catalog, prefab))
                    continue;

                var score = ScoreForRole(prefab, role);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = prefab;
                }
            }

            if (best == null && catalog.ModulePrefabCount > 0)
                best = catalog.Floors.FirstOrDefault() ?? catalog.Walls.FirstOrDefault() ?? catalog.Ceilings.FirstOrDefault();

            if (best == null)
                return;

            roleList.Add(best);
            pool.Remove(best);
        }

        static bool IsAlreadyCataloged(LavaTubePrefabCatalog catalog, GameObject prefab) =>
            catalog.Floors.Contains(prefab) ||
            catalog.Walls.Contains(prefab) ||
            catalog.Ceilings.Contains(prefab) ||
            catalog.Rockfalls.Contains(prefab) ||
            catalog.Cupolas.Contains(prefab) ||
            catalog.Stalactites.Contains(prefab) ||
            catalog.Artifacts.Contains(prefab);

        enum ModuleShapeRole
        {
            Unknown,
            Floor,
            Wall,
            Ceiling,
        }

        static ModuleShapeRole ClassifyByMeshShape(GameObject prefab)
        {
            if (!TryGetMeshExtents(prefab, out var size))
                return ModuleShapeRole.Unknown;

            var xz = Mathf.Max(size.x, size.z);
            var y = size.y;
            if (xz < 0.01f || y < 0.01f)
                return ModuleShapeRole.Unknown;

            var flatness = xz / Mathf.Max(y, 0.01f);
            if (flatness >= 2.5f)
                return ModuleShapeRole.Floor;

            if (y >= xz * 1.2f)
                return ModuleShapeRole.Wall;

            if (flatness >= 1.4f)
                return ModuleShapeRole.Ceiling;

            return ModuleShapeRole.Wall;
        }

        static float ScoreForRole(GameObject prefab, ModuleShapeRole role)
        {
            if (!TryGetMeshExtents(prefab, out var size))
                return 0f;

            var xz = Mathf.Max(size.x, size.z);
            var y = size.y;
            return role switch
            {
                ModuleShapeRole.Floor => xz / Mathf.Max(y, 0.01f),
                ModuleShapeRole.Ceiling => xz / Mathf.Max(y, 0.01f) * 0.9f,
                ModuleShapeRole.Wall => y / Mathf.Max(xz, 0.01f),
                _ => 0f,
            };
        }

        static bool TryGetMeshExtents(GameObject prefab, out Vector3 size)
        {
            size = Vector3.zero;
            var hasBounds = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;

                var meshBounds = mf.sharedMesh.bounds;
                var scale = mf.transform.lossyScale;
                var scaledSize = Vector3.Scale(meshBounds.size, scale);
                var center = mf.transform.localPosition + Vector3.Scale(meshBounds.center, scale);

                if (!hasBounds)
                {
                    bounds = new Bounds(center, scaledSize);
                    hasBounds = true;
                }
                else
                    bounds.Encapsulate(new Bounds(center, scaledSize));
            }

            if (!hasBounds)
            {
                foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null)
                        continue;

                    if (!hasBounds)
                    {
                        bounds = smr.localBounds;
                        hasBounds = true;
                    }
                    else
                        bounds.Encapsulate(smr.localBounds);
                }
            }

            if (!hasBounds)
                return false;

            size = bounds.size;
            return true;
        }

        static bool ContainsAny(string haystack, params string[] needles)
        {
            foreach (var n in needles)
            {
                if (haystack.Contains(n, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static void ClassifyProp(LavaTubePrefabCatalog catalog, string path, string name, GameObject prefab)
        {
            var lower = path.ToLowerInvariant();
            if (lower.Contains("/mushroom"))
                catalog.Mushrooms.Add(prefab);
            else if (lower.Contains("/crystal"))
                catalog.Crystals.Add(prefab);
            else if (lower.Contains("/rock") && name.Contains("Mossy"))
                catalog.MossProps.Add(prefab);
            else if (lower.Contains("/vines") || lower.Contains("/plant") || lower.Contains("/environmental/"))
                catalog.MossProps.Add(prefab);
        }

        static void SortAll(LavaTubePrefabCatalog catalog)
        {
            catalog.Floors.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Walls.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Ceilings.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Rockfalls.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Artifacts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Cupolas.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Stalactites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Mushrooms.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.Crystals.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.MossProps.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.GlowProps.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            catalog.WaterProps.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        public GameObject Pick(List<GameObject> list, System.Random rng)
        {
            if (list.Count == 0)
                return null;
            return list[rng.Next(list.Count)];
        }
    }
}
