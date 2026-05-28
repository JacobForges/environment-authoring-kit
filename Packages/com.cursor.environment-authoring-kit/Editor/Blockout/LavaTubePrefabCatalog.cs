using System.Collections.Generic;
using System.Linq;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Primary lava-tube pack plus natural props scanned from all of <c>Assets/</c>
    /// (see <see cref="FantasyCavePrefabCatalog"/> — buildings/architecture excluded).
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

        public bool IsValid => Floors.Count > 0 && Walls.Count > 0 && Ceilings.Count > 0;

        static LavaTubePrefabCatalog _cached;

        public static LavaTubePrefabCatalog Load(bool forceRefresh = false)
        {
            if (!forceRefresh && _cached != null && _cached.IsValid)
                return _cached;

            var catalog = new LavaTubePrefabCatalog();
            foreach (var folder in ParseFolders(EnvironmentKitSettings.CaveLavaPrefabFolders, DefaultPrefabRoot))
                LoadFolder(catalog, folder, lavaTube: true);
            foreach (var folder in ParseFolders(EnvironmentKitSettings.CavePropPrefabFolders, DefaultPropRoot))
                LoadFolder(catalog, folder, lavaTube: false);
            if (EnvironmentKitSettings.CaveScanAllAssets)
                FantasyCavePrefabCatalog.Populate(catalog);
            SortAll(catalog);
            _cached = catalog;
            Debug.Log(
                $"[CaveCatalog] floors={catalog.Floors.Count} walls={catalog.Walls.Count} ceilings={catalog.Ceilings.Count} " +
                $"rocks={catalog.Rockfalls.Count} mushrooms={catalog.Mushrooms.Count} crystals={catalog.Crystals.Count} " +
                $"props={catalog.MossProps.Count} glow={catalog.GlowProps.Count} " +
                $"lavaFolders='{EnvironmentKitSettings.CaveLavaPrefabFolders}' propFolders='{EnvironmentKitSettings.CavePropPrefabFolders}' " +
                $"scanAllAssets={EnvironmentKitSettings.CaveScanAllAssets}");
            return catalog;
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

        static void LoadFolder(LavaTubePrefabCatalog catalog, string folder, bool lavaTube)
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
                    ClassifyLavaTube(catalog, name, prefab);
                else
                    ClassifyProp(catalog, path, name, prefab);
            }
        }

        static void ClassifyLavaTube(LavaTubePrefabCatalog catalog, string name, GameObject prefab)
        {
            if (name.StartsWith("SM_Floor"))
                catalog.Floors.Add(prefab);
            else if (name.StartsWith("SM_Wall"))
                catalog.Walls.Add(prefab);
            else if (name.StartsWith("SM_Ceiling"))
                catalog.Ceilings.Add(prefab);
            else if (name.StartsWith("SM_Rockfall"))
                catalog.Rockfalls.Add(prefab);
            else if (name.StartsWith("SM_Artifact"))
                catalog.Artifacts.Add(prefab);
            else if (name.StartsWith("SM_Cupola"))
                catalog.Cupolas.Add(prefab);
            else if (name.Contains("stalactite", System.StringComparison.OrdinalIgnoreCase))
                catalog.Stalactites.Add(prefab);
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
