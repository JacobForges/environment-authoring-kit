#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Imports CC0 FBX under EnvironmentKit/CC0Imports: Humanoid rigs, prefabs, pickup meshes.
    /// Runs automatically at end of FullWorld pipeline.
    /// </summary>
    public static class Cc0ContentImportUtility
    {
        public const string Cc0Root = "Assets/EnvironmentKit/CC0Imports";
        public const string CharacterFbxFolder = Cc0Root + "/Characters";
        public const string ItemFbxFolder = Cc0Root + "/Items";
        public const string CharacterPrefabFolder = Cc0Root + "/Prefabs/Characters";
        public const string ItemPrefabFolder = Cc0Root + "/Prefabs/Items";

        static readonly string[] CharacterSlots =
        {
            "NPC_PlayGuide", "NPC_PlayMerchant", "NPC_FoothillRanger", "NPC_FoothillHerbalist",
            "NPC_PeakHermit", "NPC_PeakOreTrader", "NPC_PeakGuard_A", "NPC_PeakGuard_B",
            "NPC_HorizonWatcher", "NPC_Ambient_A", "NPC_Ambient_B", "NPC_Ambient_C",
            "BOSS_Stage", "BOSS_Add_1", "BOSS_Add_2",
            "ENEMY_Foothill_1", "ENEMY_Peak_1", "ENEMY_Annex_1",
        };

        static readonly string[] EnemySlots =
        {
            "ENEMY_Foothill_1", "ENEMY_Peak_1", "ENEMY_Annex_1", "BOSS_Stage", "BOSS_Add_1", "BOSS_Add_2",
        };

        [MenuItem("Window/Environment Kit/World/Import CC0 Characters & Items")]
        public static void ImportFromMenu() => EnsureAll(importItems: true, reimportFbx: true);

        [MenuItem("Window/Environment Kit/World/Fix CC0 Item Rig Settings (Generic, no avatar)")]
        public static void FixItemRigsFromMenu() => Cc0PropRigFixUtility.FixFromMenu();

        public static void EnsureAll(bool importItems = true, bool reimportFbx = false)
        {
            if (reimportFbx)
            {
                using (Cc0ImportWarningSuppressor.Begin())
                    EnsureAllCore(importItems, reimportFbx);
                return;
            }

            EnsureAllCore(importItems, reimportFbx);
        }

        static void EnsureAllCore(bool importItems, bool reimportFbx)
        {
            var characterPrefabs = new Dictionary<string, GameObject>();
            var itemPrefabs = new Dictionary<string, GameObject>();
            RunEnsureAllPrep(reimportFbx);
            RunEnsureAllCharacters(characterPrefabs, reimportFbx);
            if (importItems)
            {
                FixAllItemModelImportSettings(reimport: reimportFbx);
                foreach (var path in CollectItemMeshFullPaths())
                    TryEnsureOneItem(path, reimportFbx, itemPrefabs);
            }

            RunEnsureAllFinalize(characterPrefabs, itemPrefabs, syncAssetRefresh: false);
        }

        internal static void RunEnsureAllPrep(bool reimportFbx)
        {
            if (reimportFbx)
            {
                var patchedPaths = new List<string>();
                Cc0PropRigFixUtility.PatchStaleMetaFiles(patchedPaths);
                Cc0PropRigFixUtility.ReimportAllStaticProps(
                    log: false,
                    onlyPaths: patchedPaths.Count > 0 ? patchedPaths : null,
                    onlyIfImporterDirty: patchedPaths.Count == 0);
            }
            else
            {
                Cc0PropRigFixUtility.PatchStaleMetaFiles(null);
            }

            Cc0BlenderSourceStripper.StripUnderCc0Imports();
            EnsureFolder(CharacterPrefabFolder);
            EnsureFolder(ItemPrefabFolder);
        }

        internal static void RunEnsureAllCharacters(
            Dictionary<string, GameObject> characterPrefabs,
            bool reimportFbx)
        {
            if (characterPrefabs == null)
                return;

            foreach (var slot in CharacterSlots)
            {
                var fbx = $"{CharacterFbxFolder}/{slot}.fbx";
                if (!File.Exists(fbx))
                    continue;

                var prefabPath = $"{CharacterPrefabFolder}/{slot}.prefab";
                var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (!reimportFbx && existingPrefab != null)
                {
                    characterPrefabs[slot] = existingPrefab;
                    continue;
                }

                ConfigureHumanoidFbx(fbx);
                var prefab = EnsureCharacterPrefab(fbx, slot);
                if (prefab != null)
                    characterPrefabs[slot] = prefab;
            }

            Cc0CharacterPrefabRegistry.Save(characterPrefabs);
        }

        internal static string[] CollectItemMeshFullPaths()
        {
            if (!Directory.Exists(ItemFbxFolder))
                return new string[0];

            var meshFiles = Directory.GetFiles(ItemFbxFolder, "*.fbx");
            var objs = Directory.GetFiles(ItemFbxFolder, "*.obj");
            var combined = new string[meshFiles.Length + objs.Length];
            meshFiles.CopyTo(combined, 0);
            objs.CopyTo(combined, meshFiles.Length);
            return combined;
        }

        internal static bool TryEnsureOneItem(
            string fullPath,
            bool reimportFbx,
            Dictionary<string, GameObject> itemPrefabs)
        {
            if (string.IsNullOrEmpty(fullPath) || itemPrefabs == null)
                return false;

            var id = Path.GetFileNameWithoutExtension(fullPath);
            ConfigurePropFbx(ToAssetPath(fullPath));
            var prefab = EnsureItemPrefab(fullPath, id);
            if (prefab == null)
                return false;

            itemPrefabs[id] = prefab;
            return true;
        }

        internal const string ItemRegistryAssetPath = Cc0Root + "/item-prefab-registry.asset";

        internal static void SaveItemRegistryIfNeeded(Dictionary<string, GameObject> itemPrefabs)
        {
            if (itemPrefabs == null || itemPrefabs.Count == 0)
                return;

            Cc0ItemPrefabRegistry.Save(itemPrefabs);
        }

        internal static void SaveItemRegistryAsset()
        {
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                var reg = AssetDatabase.LoadAssetAtPath<Cc0PrefabRegistry>(ItemRegistryAssetPath);
                if (reg != null && EditorUtility.IsDirty(reg))
                    AssetDatabase.SaveAssetIfDirty(reg);
                return;
            }

            var regSync = AssetDatabase.LoadAssetAtPath<Cc0PrefabRegistry>(ItemRegistryAssetPath);
            if (regSync != null && EditorUtility.IsDirty(regSync))
                AssetDatabase.SaveAssetIfDirty(regSync);
        }

        internal static void RunEnsureAllFinalize(
            Dictionary<string, GameObject> characterPrefabs,
            Dictionary<string, GameObject> itemPrefabs,
            bool syncAssetRefresh)
        {
            if (itemPrefabs != null && itemPrefabs.Count > 0)
                Cc0ItemPrefabRegistry.Save(itemPrefabs);

            WorldItemCatalogBuilder.BuildFromManifest();
            WorldRuntimeResourcesAuthor.EnsureHollowTitanLandmarkPrefabsInResources();
            SaveItemRegistryAsset();

            if (syncAssetRefresh)
                AssetDatabase.Refresh();

            Debug.Log(
                "[CC0] Import complete — prefabs in CC0Imports/Prefabs/.",
                AssetDatabase.LoadMainAssetAtPath(CharacterPrefabFolder));
        }

        public static GameObject LoadCharacterPrefab(string slot) =>
            Cc0CharacterPrefabRegistry.Load(slot);

        public static GameObject LoadItemPrefab(string itemId) =>
            Cc0ItemPrefabRegistry.Load(itemId);

        static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;
            var parts = assetPath.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static void ConfigureHumanoidFbx(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                return;

            var humanoid = ModelHasHumanoidLegs(assetPath);
            var animType = humanoid
                ? ModelImporterAnimationType.Human
                : ModelImporterAnimationType.Generic;
            var avatarSetup = humanoid
                ? ModelImporterAvatarSetup.CreateFromThisModel
                : ModelImporterAvatarSetup.NoAvatar;
            var importAnim = humanoid;

            var dirty =
                importer.animationType != animType ||
                importer.avatarSetup != avatarSetup ||
                importer.importAnimation != importAnim ||
                importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            if (!dirty)
                return;

            importer.animationType = animType;
            importer.avatarSetup = avatarSetup;
            importer.importAnimation = importAnim;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            if (!humanoid)
            {
                Debug.LogWarning(
                    $"[CC0] '{assetPath}' has no humanoid leg bones — importing as Generic (no Humanoid avatar).",
                    importer);
            }

            importer.SaveAndReimport();
        }

        /// <summary>Unity Humanoid requires at least feet + hips; CC0 slots without rigs must stay Generic.</summary>
        static bool ModelHasHumanoidLegs(string assetPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null)
                return false;

            var hasLeftFoot = FindBoneRecursive(root.transform, "LeftFoot") != null;
            var hasRightFoot = FindBoneRecursive(root.transform, "RightFoot") != null;
            var hasHips = FindBoneRecursive(root.transform, "Hips") != null
                || FindBoneRecursive(root.transform, "hip") != null;
            return hasLeftFoot && hasRightFoot && hasHips;
        }

        static Transform FindBoneRecursive(Transform t, string boneName)
        {
            if (t.name.Equals(boneName, System.StringComparison.OrdinalIgnoreCase))
                return t;

            for (var i = 0; i < t.childCount; i++)
            {
                var found = FindBoneRecursive(t.GetChild(i), boneName);
                if (found != null)
                    return found;
            }

            return null;
        }

        public static void FixAllItemModelImportSettings(bool reimport)
        {
            if (reimport)
                return;

            Cc0PropRigFixUtility.FixAll();
        }

        static string ToAssetPath(string fullPath)
        {
            fullPath = fullPath.Replace('\\', '/');
            var data = Application.dataPath.Replace('\\', '/');
            if (!fullPath.StartsWith(data, System.StringComparison.Ordinal))
                return fullPath;
            return "Assets" + fullPath.Substring(data.Length);
        }

        internal static bool ConfigurePropFbx(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                return false;

            var dirty =
                importer.animationType != ModelImporterAnimationType.None ||
                importer.avatarSetup != ModelImporterAvatarSetup.NoAvatar ||
                importer.importAnimation ||
                importer.autoGenerateAvatarMappingIfUnspecified ||
                Mathf.Abs(importer.globalScale - 0.35f) > 0.001f ||
                importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            Cc0ModelImportPostprocessor.ApplyStaticPropImporterSettings(importer);
            importer.globalScale = 0.35f;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            if (!dirty)
                return false;

            importer.SaveAndReimport();
            return true;
        }

        static GameObject EnsureCharacterPrefab(string fbxPath, string slot)
        {
            var prefabPath = $"{CharacterPrefabFolder}/{slot}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return existing;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null)
                return null;

            var aggression = slot.StartsWith("ENEMY_") || slot.StartsWith("BOSS_")
                ? CaveMobAggression.Aggressive
                : CaveMobAggression.Passive;
            var instance = HumanoidCombatSpawner.SpawnEnemy(
                Vector3.zero,
                Quaternion.identity,
                null,
                source,
                seed: slot.GetHashCode(),
                aggression);
            instance.name = slot;
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        static GameObject EnsureItemPrefab(string fbxPath, string itemId)
        {
            var prefabPath = $"{ItemPrefabFolder}/{itemId}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return existing;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null)
                return null;

            var root = Object.Instantiate(source);
            root.name = $"Item_{itemId}";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }

    static class Cc0CharacterPrefabRegistry
    {
        const string Path = Cc0ContentImportUtility.Cc0Root + "/character-prefab-registry.asset";

        public static void Save(Dictionary<string, GameObject> map)
        {
            var reg = LoadOrCreate();
            reg.entries.Clear();
            foreach (var kv in map)
                reg.entries.Add(new Cc0PrefabEntry { slot = kv.Key, prefab = kv.Value });
            EditorUtility.SetDirty(reg);
        }

        public static GameObject Load(string slot)
        {
            var reg = LoadOrCreate();
            foreach (var e in reg.entries)
            {
                if (e.slot == slot && e.prefab != null)
                    return e.prefab;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{Cc0ContentImportUtility.CharacterPrefabFolder}/{slot}.prefab");
        }

        static Cc0PrefabRegistry LoadOrCreate()
        {
            var reg = AssetDatabase.LoadAssetAtPath<Cc0PrefabRegistry>(Path);
            if (reg != null)
                return reg;
            reg = ScriptableObject.CreateInstance<Cc0PrefabRegistry>();
            AssetDatabase.CreateAsset(reg, Path);
            return reg;
        }
    }

    static class Cc0ItemPrefabRegistry
    {
        const string Path = Cc0ContentImportUtility.Cc0Root + "/item-prefab-registry.asset";

        public static void Save(Dictionary<string, GameObject> map)
        {
            var reg = LoadOrCreate();
            reg.entries.Clear();
            foreach (var kv in map)
                reg.entries.Add(new Cc0PrefabEntry { slot = kv.Key, prefab = kv.Value });
            EditorUtility.SetDirty(reg);
        }

        public static GameObject Load(string itemId)
        {
            var reg = LoadOrCreate();
            foreach (var e in reg.entries)
            {
                if (e.slot == itemId && e.prefab != null)
                    return e.prefab;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{Cc0ContentImportUtility.ItemPrefabFolder}/{itemId}.prefab");
        }

        static Cc0PrefabRegistry LoadOrCreate()
        {
            var reg = AssetDatabase.LoadAssetAtPath<Cc0PrefabRegistry>(Path);
            if (reg != null)
                return reg;
            reg = ScriptableObject.CreateInstance<Cc0PrefabRegistry>();
            AssetDatabase.CreateAsset(reg, Path);
            return reg;
        }
    }
}
#endif
