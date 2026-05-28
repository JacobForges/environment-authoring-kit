using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Scans all prefabs under Assets/ and classifies cave-usable content.</summary>
    public static class FantasyCavePrefabCatalog
    {
        static readonly string[] ExcludePathContains =
        {
            "/slimui/", "/modern menu/", "/ganzse", "/character_miho/", "/suimono/",
            "/canvas", "/button", "/btn_", "/ui/", "/hud/", "/gameobject/",
            "/gamedata/prefabs/caveenemy", "/editor/", "/samples/", "/tutorial/",
            "/textmesh pro/", "/plugins/", "/settings/",
            // Architectural / town content — cave must stay natural geology only
            "/buildings/", "/building/", "/architecture/", "/architectural/",
            "/house/", "/housing/", "/home/", "/town/", "/city/", "/village/",
            "/furniture/", "/interior/", "/exterior/", "/facade/", "/modular building/",
            "/modular home/", "/polygonshop/buildings", "/suburb", "/office/", "/shop/",
            "/store/", "/tavern/", "/inn/", "/church/", "/temple/", "/castle pack/",
            "/street/", "/sidewalk/", "/fence pack/", "/window pack/", "/door pack/"
        };

        static readonly string[] ExcludeNameContains =
        {
            "canvas", "button", "btn_", "eventsystem", "player", "character", "avatar",
            "camera", "lightprobe", "reflectionprobe", "navmesh", "postprocess",
            "building", "house", "cottage", "cabin", "castle", "fortress", "tower",
            "town", "village", "city", "suburb", "facade", "doorway", "door_", "_door",
            "window", "furniture", "chair", "table", "bed", "cupboard", "shelf", "sofa",
            "chimney", "balcony", "porch", "fence", "lamp_post", "streetlight", "mailbox",
            "pickup_truck", "car_", "vehicle", "sign_", "billboard"
        };

        /// <summary>Legacy entry — module scan is automatic; this only enriches prop lists.</summary>
        public static void Populate(LavaTubePrefabCatalog catalog) => PopulateProps(catalog);

        public static void PopulateProps(LavaTubePrefabCatalog catalog)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkip(path))
                    continue;

                if (IsArchitecturalPrefab(path))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !HasRenderableMesh(prefab))
                    continue;

                if (LavaTubePrefabCatalog.IsAlreadyInCatalog(catalog, prefab))
                    continue;

                var name = prefab.name;
                var lowerPath = path.ToLowerInvariant();
                var lowerName = name.ToLowerInvariant();

                if (ClassifyLavaTubeName(catalog, name, prefab))
                    continue;

                ClassifyFantasyAsset(catalog, lowerPath, lowerName, prefab);
            }
        }

        static bool ShouldSkip(string path)
        {
            var lower = path.ToLowerInvariant();
            foreach (var frag in ExcludePathContains)
            {
                if (lower.Contains(frag))
                    return true;
            }

            var fileName = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            foreach (var frag in ExcludeNameContains)
            {
                if (fileName.Contains(frag))
                    return true;
            }

            return false;
        }

        /// <summary>Extra pass: skip man-made structures even if path slipped through.</summary>
        static bool IsArchitecturalPrefab(string path)
        {
            var lower = path.ToLowerInvariant();
            var file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            if (file.StartsWith("sm_floor") || file.StartsWith("sm_wall") || file.StartsWith("sm_ceiling") ||
                file.StartsWith("sm_rock") || file.StartsWith("sm_cupola") || file.StartsWith("sm_artifact"))
                return false;

            if (lower.Contains("lava") || lower.Contains("cave") || lower.Contains("cavern") ||
                lower.Contains("stalact") || lower.Contains("stalag") || lower.Contains("geode"))
                return false;

            string[] archHints =
            {
                "building", "house", "home", "cottage", "cabin", "hut", "castle", "fort", "fortress",
                "tower_", "town", "village", "city", "suburb", "shop", "store", "tavern", "inn",
                "church", "temple", "mosque", "barn", "warehouse", "office", "apartment", "hotel",
                "door", "window", "fence", "gate_", "porch", "balcony", "chimney", "roof_tile",
                "shingle", "brick_house", "facade", "street", "sidewalk", "lamp_post", "fountain_plaza"
            };

            foreach (var hint in archHints)
            {
                if (file.Contains(hint) || lower.Contains("/" + hint))
                    return true;
            }

            return false;
        }

        static bool HasRenderableMesh(GameObject prefab) =>
            prefab.GetComponentInChildren<MeshRenderer>(true) != null ||
            prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

        static bool ClassifyLavaTubeName(LavaTubePrefabCatalog catalog, string name, GameObject prefab)
        {
            if (name.StartsWith("SM_Floor"))
            {
                AddUnique(catalog.Floors, prefab);
                return true;
            }

            if (name.StartsWith("SM_Wall"))
            {
                AddUnique(catalog.Walls, prefab);
                return true;
            }

            if (name.StartsWith("SM_Ceiling"))
            {
                AddUnique(catalog.Ceilings, prefab);
                return true;
            }

            if (name.StartsWith("SM_Rockfall"))
            {
                AddUnique(catalog.Rockfalls, prefab);
                return true;
            }

            if (name.StartsWith("SM_Artifact"))
            {
                AddUnique(catalog.Artifacts, prefab);
                return true;
            }

            if (name.StartsWith("SM_Cupola"))
            {
                AddUnique(catalog.Cupolas, prefab);
                return true;
            }

            if (name.Contains("stalactite", System.StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(catalog.Stalactites, prefab);
                return true;
            }

            return false;
        }

        static void ClassifyFantasyAsset(
            LavaTubePrefabCatalog catalog,
            string lowerPath,
            string lowerName,
            GameObject prefab)
        {
            if (lowerPath.Contains("mushroom") || lowerName.Contains("mushroom"))
                AddUnique(catalog.Mushrooms, prefab);
            else if (lowerPath.Contains("crystal") || lowerName.Contains("crystal") || lowerName.Contains("gem"))
                AddUnique(catalog.Crystals, prefab);
            else if (lowerName.Contains("stalact") || lowerName.Contains("icicle"))
                AddUnique(catalog.Stalactites, prefab);
            else if (lowerPath.Contains("cupola") || lowerName.Contains("dome"))
                AddUnique(catalog.Cupolas, prefab);
            else if (lowerName.Contains("wall") || lowerName.Contains("cliff") || lowerName.Contains("pillar"))
                AddUnique(catalog.Walls, prefab);
            else if (lowerName.Contains("floor") || lowerName.Contains("ground") || lowerName.Contains("platform"))
                AddUnique(catalog.Floors, prefab);
            else if (lowerName.Contains("ceiling") || lowerName.Contains("roof"))
                AddUnique(catalog.Ceilings, prefab);
            else if (lowerName.Contains("rock") || lowerName.Contains("boulder") || lowerName.Contains("stone"))
                AddUnique(catalog.Rockfalls, prefab);
            else if (lowerPath.Contains("/vine") || lowerPath.Contains("/plant") || lowerPath.Contains("/bush") ||
                     lowerPath.Contains("/fern") || lowerPath.Contains("/moss") || lowerPath.Contains("/root") ||
                     lowerPath.Contains("/log") || lowerPath.Contains("/stump"))
                AddUnique(catalog.MossProps, prefab);
            else if (lowerPath.Contains("artifact") || lowerPath.Contains("rune") || lowerPath.Contains("relic") ||
                     lowerPath.Contains("totem"))
                AddUnique(catalog.Artifacts, prefab);
            else if (lowerPath.Contains("torch") || lowerPath.Contains("lantern") || lowerPath.Contains("light") ||
                     lowerPath.Contains("particle") || lowerPath.Contains("glow"))
                AddUnique(catalog.GlowProps, prefab);
            else if (lowerPath.Contains("water") || lowerPath.Contains("pool") || lowerName.Contains("fall"))
                AddUnique(catalog.WaterProps, prefab);
        }

        static void AddUnique(List<GameObject> list, GameObject prefab)
        {
            if (prefab == null || list.Contains(prefab))
                return;
            list.Add(prefab);
        }
    }
}
