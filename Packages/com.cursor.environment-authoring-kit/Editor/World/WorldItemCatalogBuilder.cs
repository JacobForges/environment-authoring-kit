#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    public static class WorldItemCatalogBuilder
    {
        const string CatalogAssetPath =
            "Assets/EnvironmentKit/CC0Imports/Resources/WorldItemCatalog.asset";

        const string ManifestRelative = "PlanV4-AssetReview/asset-manifest.json";

        [Serializable]
        sealed class ManifestFile
        {
            public ManifestItem[] items;
        }

        [Serializable]
        sealed class ManifestItem
        {
            public string id;
            public string display;
            public string archetype;
        }

        [MenuItem("Window/Environment Kit/World/Build Item Catalog From Manifest")]
        public static void BuildFromMenu() => BuildFromManifest();

        public static void BuildFromManifest()
        {
            var manifestPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", ManifestRelative));
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[WorldItemCatalog] Manifest not found: {manifestPath}");
                return;
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonUtility.FromJson<ManifestFile>(WrapItemsArray(json));
            if (manifest?.items == null || manifest.items.Length == 0)
            {
                Debug.LogError("[WorldItemCatalog] No items parsed from manifest.");
                return;
            }

            var catalog = LoadOrCreateCatalog();
            catalog.entries.Clear();

            foreach (var item in manifest.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                catalog.entries.Add(BuildEntry(item));
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            WorldItemCatalog.ReloadForEditor(catalog);
            Debug.Log($"[WorldItemCatalog] Built {catalog.entries.Count} definitions → {CatalogAssetPath}.");
        }

        public static int CatalogSliceBatchSizePublic =>
            CaveBuildLoadAwareBatching.Clamp(2);

        public static bool TryGetManifestItemCount(out int count, out string error)
        {
            count = 0;
            error = string.Empty;
            if (!TryLoadManifest(out var manifest, out error))
                return false;

            count = manifest.items?.Length ?? 0;
            return count > 0;
        }

        /// <summary>Paced rebuild — call slice 0 with startIndex 0 to clear entries first.</summary>
        public static void RebuildCatalogSlice(int startIndex, int count)
        {
            if (count <= 0)
                return;

            if (!TryLoadManifest(out var manifest, out var error))
            {
                Debug.LogError("[WorldItemCatalog] " + error);
                return;
            }

            var items = manifest.items;
            if (items == null || items.Length == 0)
                return;

            var catalog = LoadOrCreateCatalog();
            if (startIndex <= 0)
                catalog.entries.Clear();

            var end = Mathf.Min(startIndex + count, items.Length);
            for (var i = startIndex; i < end; i++)
            {
                var item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                catalog.entries.Add(BuildEntry(item));
            }

            EditorUtility.SetDirty(catalog);
        }

        public static void CommitCatalogRebuildWithoutSave()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WorldItemCatalogAsset>(CatalogAssetPath);
            if (catalog == null)
                return;

            WorldItemCatalog.ReloadForEditor(catalog);
        }

        public static void SaveCatalogAsset()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WorldItemCatalogAsset>(CatalogAssetPath);
            if (catalog != null && EditorUtility.IsDirty(catalog))
                AssetDatabase.SaveAssetIfDirty(catalog);
        }

        public static bool IsCatalogDirty()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WorldItemCatalogAsset>(CatalogAssetPath);
            return catalog != null && EditorUtility.IsDirty(catalog);
        }

        /// <summary>
        /// Full AAA pattern — defer large Resources catalog write until grid pipeline completes
        /// (avoids main-thread stall at grid ~70% CC0 finalize).
        /// </summary>
        public static void QueuePacedSaveIfDirty(System.Action onComplete = null)
        {
            if (!IsCatalogDirty())
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleHeavyChain(
                () =>
                {
                    CaveBuildEditorResponsiveness.BeginSlice();
                    SaveCatalogAsset();
                    EnvironmentKitScopedAssetRefresh.ImportAssetPathsNow(CatalogAssetPath);
                    CaveBuildEditorLog.LogSurface(
                        "[WorldItemCatalog] Persisted deferred catalog save after FullWorld grid.",
                        forceUnityConsole: false);
                    if (onComplete != null)
                        EditorApplication.delayCall += () => onComplete();
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel("persist WorldItemCatalog"));
        }

        static bool TryLoadManifest(out ManifestFile manifest, out string error)
        {
            manifest = null;
            error = string.Empty;
            var manifestPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", ManifestRelative));
            if (!File.Exists(manifestPath))
            {
                error = $"Manifest not found: {manifestPath}";
                return false;
            }

            var json = File.ReadAllText(manifestPath);
            manifest = JsonUtility.FromJson<ManifestFile>(WrapItemsArray(json));
            if (manifest?.items == null || manifest.items.Length == 0)
            {
                error = "No items parsed from manifest.";
                return false;
            }

            return true;
        }

        static string WrapItemsArray(string json)
        {
            if (json.Contains("\"items\":"))
                return json;
            return "{\"items\":" + json + "}";
        }

        static WorldItemCatalogAsset LoadOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WorldItemCatalogAsset>(CatalogAssetPath);
            if (catalog != null)
                return catalog;

            var dir = Path.GetDirectoryName(CatalogAssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir)));

            catalog = ScriptableObject.CreateInstance<WorldItemCatalogAsset>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            return catalog;
        }

        static WorldItemDefinitionEntry BuildEntry(ManifestItem item)
        {
            var id = item.id.Trim();
            var archetype = item.archetype ?? string.Empty;
            var category = ResolveCategory(id, archetype);
            var gemGrade = ResolveGemGrade(id);
            var family = id.StartsWith("G-", StringComparison.Ordinal) && id.Length >= 4
                ? id.Substring(0, 3)
                : string.Empty;

            return new WorldItemDefinitionEntry
            {
                id = id,
                displayName = string.IsNullOrWhiteSpace(item.display) ? id : item.display,
                archetype = archetype,
                category = category,
                gemGrade = gemGrade,
                gemFamily = family,
                buyPriceCopper = PriceBuy(id, category, gemGrade),
                sellPriceCopper = PriceSell(id, category, gemGrade),
                stackMax = category == ItemCategory.Currency ? 999 : 99,
                isCurrency = category == ItemCategory.Currency,
                isLandmark = id.StartsWith("L", StringComparison.Ordinal) && id.Length == 3,
            };
        }

        static ItemCategory ResolveCategory(string id, string archetype)
        {
            if (id.StartsWith("C0", StringComparison.Ordinal))
                return ItemCategory.Currency;
            if (id.StartsWith("G-", StringComparison.Ordinal))
                return ItemCategory.Gem;
            if (id.StartsWith("K", StringComparison.Ordinal))
                return ItemCategory.Key;
            if (id.StartsWith("L", StringComparison.Ordinal))
                return ItemCategory.Landmark;
            if (id.StartsWith("P", StringComparison.Ordinal) ||
                id.StartsWith("F", StringComparison.Ordinal) ||
                archetype.Contains("POTION", StringComparison.Ordinal) ||
                archetype.Contains("FLASK", StringComparison.Ordinal) ||
                archetype.Contains("FOOD", StringComparison.Ordinal))
                return ItemCategory.Consumable;
            if (archetype.Contains("SWORD", StringComparison.Ordinal) ||
                archetype.Contains("STAFF", StringComparison.Ordinal) ||
                archetype.Contains("SLING", StringComparison.Ordinal) ||
                archetype.Contains("HOOK", StringComparison.Ordinal) ||
                archetype.Contains("DISC", StringComparison.Ordinal) ||
                archetype.Contains("PICK", StringComparison.Ordinal) ||
                archetype.Contains("SICKLE", StringComparison.Ordinal))
                return ItemCategory.Weapon;
            if (archetype.Contains("SHIELD", StringComparison.Ordinal) ||
                archetype.Contains("HELM", StringComparison.Ordinal) ||
                archetype.Contains("BOOTS", StringComparison.Ordinal) ||
                archetype.Contains("PACK", StringComparison.Ordinal) ||
                archetype.Contains("AMULET", StringComparison.Ordinal))
                return ItemCategory.Armor;
            return ItemCategory.Material;
        }

        static GemGrade ResolveGemGrade(string id)
        {
            if (!id.StartsWith("G-", StringComparison.Ordinal) || id.Length < 5)
                return GemGrade.Tool;
            var tier = id[^1];
            return tier switch
            {
                '1' => GemGrade.Tool,
                '2' => GemGrade.Cabochon,
                _ => GemGrade.CutGem,
            };
        }

        static int PriceBuy(string id, ItemCategory cat, GemGrade grade)
        {
            if (id == "C01") return 1;
            if (id == "C02") return 1;
            if (id == "C03") return 1;
            if (id == "C04") return 1;
            if (cat == ItemCategory.Gem)
                return grade switch
                {
                    GemGrade.Tool => 25,
                    GemGrade.Cabochon => 120,
                    _ => 480,
                };
            if (cat == ItemCategory.Weapon) return 180;
            if (cat == ItemCategory.Armor) return 140;
            if (cat == ItemCategory.Consumable) return 35;
            if (cat == ItemCategory.Key) return 90;
            return 20;
        }

        static int PriceSell(string id, ItemCategory cat, GemGrade grade) =>
            Mathf.Max(1, PriceBuy(id, cat, grade) / 2);
    }
}
#endif
