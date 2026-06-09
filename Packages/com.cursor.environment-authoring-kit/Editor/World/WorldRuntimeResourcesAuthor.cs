#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Copies CC0 prefabs into Resources for runtime landmark and world enemy loads.</summary>
    public static class WorldRuntimeResourcesAuthor
    {
        const string CharacterResourceRoot = "Assets/EnvironmentKit/CC0Imports/Resources/CC0/Characters";
        const string ItemResourceRoot = "Assets/EnvironmentKit/CC0Imports/Resources/CC0/Items";

        public static void EnsureBossInResources() =>
            CopyCharacterToResources("BOSS_Stage");

        /// <summary>All world scatter + landmark legendary CC0 enemy slots in Resources.</summary>
        public static void EnsureAllEnemyPrefabsInResources()
        {
            var slots = new HashSet<string>();
            foreach (var slot in WorldEnemySpawnCatalog.EnumerateUniqueCc0Slots())
                slots.Add(slot);
            foreach (var slot in HollowTitanLandmarkSpawnCatalog.LegendaryEnemyCc0Slots)
                slots.Add(slot);

            foreach (var slot in slots)
                CopyCharacterToResources(slot);
        }

        public static void EnsureHollowTitanLandmarkPrefabsInResources()
        {
            for (var i = 0; i < HollowTitanResourceStepCount; i++)
                RunHollowTitanResourceStep(i, flushSave: false);
        }

        public static int HollowTitanResourceStepCount =>
            HollowTitanLandmarkSpawnCatalog.LegendaryEnemyCc0Slots.Length +
            HollowTitanLandmarkSpawnCatalog.LootDefinitionIds.Length;

        public static void RunHollowTitanResourceStep(int stepIndex, bool flushSave = false)
        {
            var enemySlots = HollowTitanLandmarkSpawnCatalog.LegendaryEnemyCc0Slots;
            if (stepIndex >= 0 && stepIndex < enemySlots.Length)
            {
                CopyCharacterToResources(enemySlots[stepIndex], flushSave);
                return;
            }

            var lootIndex = stepIndex - enemySlots.Length;
            var lootIds = HollowTitanLandmarkSpawnCatalog.LootDefinitionIds;
            if (lootIndex >= 0 && lootIndex < lootIds.Length)
                CopyItemIfMissing(lootIds[lootIndex], flushSave);
        }

        /// <summary>Legacy hook — prefabs persist via SaveAsPrefabAsset; SO saves are paced in Cc0ContentImportPipeline.</summary>
        public static void FlushPendingResourceCopies()
        {
        }

        static void CopyCharacterToResources(string slot, bool flushSave = true)
        {
            if (string.IsNullOrWhiteSpace(slot))
                return;

            var targetPath = $"{CharacterResourceRoot}/{slot}.prefab";
            var source = Cc0ContentImportUtility.LoadCharacterPrefab(slot);
            if (source == null)
            {
                Debug.LogWarning($"[CC0] Character prefab missing for Resources copy: {slot}");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();
                EnsureFolder(CharacterResourceRoot);
                var depsFolder = $"{CharacterResourceRoot}/{slot}_Deps";
                EnsureFolder(depsFolder);
                CopyPrefabDependencies(AssetDatabase.GetAssetPath(source), depsFolder);
                SaveFullPrefabCopy(source, targetPath, slot, flushSave);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static void CopyItemIfMissing(string itemId, bool flushSave = true)
        {
            var targetPath = $"{ItemResourceRoot}/{itemId}.prefab";
            if (File.Exists(targetPath))
                return;

            var source = Cc0ContentImportUtility.LoadItemPrefab(itemId);
            if (source == null &&
                Cc0RuntimePrefabResolver.TryResolveItemSubstitute(itemId, out var substituteId))
            {
                source = Cc0ContentImportUtility.LoadItemPrefab(substituteId);
                if (source != null)
                {
                    Debug.Log(
                        $"[CC0] Item '{itemId}' missing — copying substitute '{substituteId}' to Resources as {itemId}.prefab.");
                }
            }

            if (source == null)
            {
                Debug.LogWarning($"[CC0] Item prefab missing for Resources copy: {itemId}");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();
                EnsureFolder(ItemResourceRoot);
                SaveFullPrefabCopy(source, targetPath, itemId, flushSave);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static void CopyPrefabDependencies(string prefabPath, string depsFolder)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return;

            foreach (var dep in AssetDatabase.GetDependencies(prefabPath, true))
            {
                if (dep == prefabPath)
                    continue;
                if (dep.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!dep.StartsWith("Assets/EnvironmentKit/CC0Imports", System.StringComparison.Ordinal))
                    continue;
                if (dep.Contains("/Resources/", System.StringComparison.Ordinal))
                    continue;

                var rel = dep.Substring("Assets/EnvironmentKit/CC0Imports/".Length).Replace('/', '_');
                var dest = $"{depsFolder}/{rel}";
                var destDir = Path.GetDirectoryName(dest)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(destDir))
                    EnsureFolder(destDir);
                if (File.Exists(dest))
                    continue;

                AssetDatabase.CopyAsset(dep, dest);
            }
        }

        static void SaveFullPrefabCopy(GameObject source, string targetPath, string assetName, bool flushSave = true)
        {
            var copy = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (copy == null)
                return;

            copy.name = assetName;
            PrefabUtility.SaveAsPrefabAsset(copy, targetPath);
            UnityEngine.Object.DestroyImmediate(copy);
            if (flushSave)
                SaveAssetAtPathIfDirty(targetPath);
        }

        static void SaveAssetAtPathIfDirty(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (main != null && EditorUtility.IsDirty(main))
                AssetDatabase.SaveAssetIfDirty(main);
        }

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
    }
}
#endif
