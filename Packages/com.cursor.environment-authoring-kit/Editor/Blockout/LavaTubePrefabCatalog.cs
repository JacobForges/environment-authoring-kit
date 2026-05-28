using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Environment module + prop prefabs discovered from the whole project (any licensed pack).
    /// </summary>
    public sealed class LavaTubePrefabCatalog
    {
        /// <summary>Legacy label; discovery no longer depends on this path.</summary>
        public const string DefaultPrefabRoot = "Assets/";
        public const string MaterialsRoot = "Assets/";
        public const string DefaultPropRoot = "Assets/";
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
        static string _lastDiscoverySummary = string.Empty;

        public static string LastDiscoverySummary => _lastDiscoverySummary;

        public static LavaTubePrefabCatalog Load(bool forceRefresh = false)
        {
            if (!forceRefresh && _cached != null && _cached.IsValid)
                return _cached;

            ProjectCaveMaterialResolver.ClearCache();
            var catalog = new LavaTubePrefabCatalog();
            var unclassifiedModules = new List<GameObject>();

            foreach (var folder in ParseUserFolders(EnvironmentKitSettings.CaveLavaPrefabFolders))
                LoadFolder(catalog, folder, lavaTube: true, unclassifiedModules);

            CaveModulePrefabDiscovery.ScanProject(catalog, unclassifiedModules);

            foreach (var folder in ParseUserFolders(EnvironmentKitSettings.CavePropPrefabFolders))
                LoadFolder(catalog, folder, lavaTube: false, unclassifiedModules);

            FantasyCavePrefabCatalog.PopulateProps(catalog);

            FinalizeModuleCatalog(catalog, unclassifiedModules);
            SortAll(catalog);
            _cached = catalog;

            _lastDiscoverySummary =
                $"floors={catalog.Floors.Count} walls={catalog.Walls.Count} ceilings={catalog.Ceilings.Count} " +
                $"rocks={catalog.Rockfalls.Count} props={catalog.MossProps.Count + catalog.Mushrooms.Count} valid={catalog.IsValid}";

            Debug.Log(
                $"[CaveCatalog] {_lastDiscoverySummary} " +
                $"(user module folders='{EnvironmentKitSettings.CaveLavaPrefabFolders}' prop folders='{EnvironmentKitSettings.CavePropPrefabFolders}')");

            if (!catalog.IsValid)
            {
                Debug.LogWarning(
                    "[CaveCatalog] No floor+wall+ceiling modules found. Import a 3D modular cave/dungeon pack with mesh prefabs " +
                    "(not texture-only or 2D tile sprites). The kit scans all of Assets/ automatically.");
            }

            return catalog;
        }

        public static IEnumerable<string> GetConfiguredModuleFolders() =>
            ParseUserFolders(EnvironmentKitSettings.CaveLavaPrefabFolders);

        public static IEnumerable<string> GetModuleAssetRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in GetConfiguredModuleFolders())
            {
                var f = folder.Trim().TrimEnd('/');
                if (AssetDatabase.IsValidFolder(f))
                    roots.Add(f);
            }

            var catalog = _cached ?? Load();
            CaveModulePrefabDiscovery.CollectMaterialRootsFromCatalog(catalog, roots);

            if (roots.Count == 0)
                roots.Add("Assets");

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

        public static bool IsAlreadyInCatalog(LavaTubePrefabCatalog catalog, GameObject prefab) =>
            catalog.Floors.Contains(prefab) ||
            catalog.Walls.Contains(prefab) ||
            catalog.Ceilings.Contains(prefab) ||
            catalog.Rockfalls.Contains(prefab) ||
            catalog.Cupolas.Contains(prefab) ||
            catalog.Stalactites.Contains(prefab) ||
            catalog.Artifacts.Contains(prefab) ||
            catalog.Mushrooms.Contains(prefab) ||
            catalog.Crystals.Contains(prefab) ||
            catalog.MossProps.Contains(prefab) ||
            catalog.GlowProps.Contains(prefab) ||
            catalog.WaterProps.Contains(prefab);

        public static bool TryClassifyModuleFromAsset(
            LavaTubePrefabCatalog catalog,
            string path,
            string name,
            GameObject prefab) =>
            TryClassifyModulePrefab(catalog, path, name, prefab);

        public static void AddProp(LavaTubePrefabCatalog catalog, string path, string name, GameObject prefab) =>
            ClassifyProp(catalog, path, name, prefab);

        static IEnumerable<string> ParseUserFolders(string raw) =>
            (raw ?? string.Empty)
            .Split(';')
            .Select(s => (s ?? string.Empty).Trim().TrimEnd('/'))
            .Where(s => !string.IsNullOrEmpty(s) && AssetDatabase.IsValidFolder(s))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        static void LoadFolder(
            LavaTubePrefabCatalog catalog,
            string folder,
            bool lavaTube,
            List<GameObject> unclassifiedModules)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || IsAlreadyInCatalog(catalog, prefab))
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
            if (ContainsAny(blob, "floor", "ground", "walkway", "platform", "pavement", "bridge", "tile_floor"))
            {
                catalog.Floors.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "ceiling", "roof", "overhead", "top_cap", "cavern_top"))
            {
                catalog.Ceilings.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "wall", "siding", "cliff_face", "pillar", "column", "barrier", "fence", "gate"))
            {
                catalog.Walls.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "rockfall", "rubble", "debris", "boulder", "rock", "stone", "ore"))
            {
                var role = ClassifyByMeshShape(prefab);
                if (role == ModuleShapeRole.Floor)
                {
                    catalog.Floors.Add(prefab);
                    return true;
                }

                if (role == ModuleShapeRole.Ceiling)
                {
                    catalog.Ceilings.Add(prefab);
                    return true;
                }

                if (role == ModuleShapeRole.Wall)
                {
                    catalog.Walls.Add(prefab);
                    return true;
                }

                catalog.Rockfalls.Add(prefab);
                return true;
            }

            if (ContainsAny(blob, "tunnel", "corridor", "cave", "cavern", "hall", "tube", "module", "segment",
                    "passage", "dungeon", "modular"))
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
                .Where(p => p != null && !IsAlreadyInCatalog(catalog, p))
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

            PromoteRockfallsToModules(catalog);

            EnsureRoleFilled(catalog, catalog.Floors, pool, ModuleShapeRole.Floor);
            EnsureRoleFilled(catalog, catalog.Walls, pool, ModuleShapeRole.Wall);
            EnsureRoleFilled(catalog, catalog.Ceilings, pool, ModuleShapeRole.Ceiling);

            if (!catalog.IsValid)
                EnsureRolesFromRockfalls(catalog);

            foreach (var prefab in pool)
            {
                if (prefab == null || IsAlreadyInCatalog(catalog, prefab))
                    continue;
                catalog.Rockfalls.Add(prefab);
            }
        }

        static void PromoteRockfallsToModules(LavaTubePrefabCatalog catalog)
        {
            if (catalog.IsValid)
                return;

            foreach (var rock in catalog.Rockfalls.ToList())
            {
                if (rock == null)
                    continue;

                var role = ClassifyByMeshShape(rock);
                switch (role)
                {
                    case ModuleShapeRole.Floor when catalog.Floors.Count == 0:
                        catalog.Floors.Add(rock);
                        catalog.Rockfalls.Remove(rock);
                        break;
                    case ModuleShapeRole.Wall when catalog.Walls.Count == 0:
                        catalog.Walls.Add(rock);
                        catalog.Rockfalls.Remove(rock);
                        break;
                    case ModuleShapeRole.Ceiling when catalog.Ceilings.Count == 0:
                        catalog.Ceilings.Add(rock);
                        catalog.Rockfalls.Remove(rock);
                        break;
                }
            }
        }

        static void EnsureRolesFromRockfalls(LavaTubePrefabCatalog catalog)
        {
            var rocks = catalog.Rockfalls
                .Where(r => r != null)
                .OrderByDescending(r => ScoreForRole(r, ModuleShapeRole.Floor))
                .ToList();

            if (catalog.Floors.Count == 0 && rocks.Count > 0)
            {
                var best = rocks.OrderByDescending(r => ScoreForRole(r, ModuleShapeRole.Floor)).First();
                catalog.Floors.Add(best);
                catalog.Rockfalls.Remove(best);
            }

            if (catalog.Walls.Count == 0 && catalog.Rockfalls.Count > 0)
            {
                var best = catalog.Rockfalls.OrderByDescending(r => ScoreForRole(r, ModuleShapeRole.Wall)).First();
                catalog.Walls.Add(best);
                catalog.Rockfalls.Remove(best);
            }

            if (catalog.Ceilings.Count == 0 && catalog.Rockfalls.Count > 0)
            {
                var best = catalog.Rockfalls.OrderByDescending(r => ScoreForRole(r, ModuleShapeRole.Ceiling)).First();
                catalog.Ceilings.Add(best);
                catalog.Rockfalls.Remove(best);
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
                if (prefab == null || IsAlreadyInCatalog(catalog, prefab))
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
            if (flatness >= 1.8f)
                return ModuleShapeRole.Floor;

            if (y >= xz * 1.1f)
                return ModuleShapeRole.Wall;

            if (flatness >= 1.2f)
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

        internal static bool TryGetMeshExtents(GameObject prefab, out Vector3 size)
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
            if (IsAlreadyInCatalog(catalog, prefab))
                return;

            var lower = path.ToLowerInvariant();
            if (lower.Contains("/mushroom"))
                catalog.Mushrooms.Add(prefab);
            else if (lower.Contains("/crystal"))
                catalog.Crystals.Add(prefab);
            else if (lower.Contains("/rock") && name.Contains("Mossy"))
                catalog.MossProps.Add(prefab);
            else if (lower.Contains("/vine") || lower.Contains("/plant") || lower.Contains("/environmental/"))
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
