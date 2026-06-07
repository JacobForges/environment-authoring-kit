using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Loads CC0 character and item prefabs from Resources at runtime.
    /// Editor pipeline copies needed prefabs via WorldRuntimeResourcesAuthor.
    /// </summary>
    public static class Cc0RuntimePrefabResolver
    {
        const string CharacterResourceRoot = "CC0/Characters";
        const string ItemResourceRoot = "CC0/Items";

        /// <summary>Loot ids with no CC0 prefab — nearest substitute for runtime spawn.</summary>
        static readonly Dictionary<string, string> ItemSubstitutes = new()
        {
            { "T01", "G-T06" },
            { HollowTitanLandmarkSpawnCatalog.GrapplingHookLootId, HollowTitanLandmarkSpawnCatalog.GrapplingHookCc0Substitute },
        };

        public static GameObject LoadCharacter(string slot) =>
            LoadPrefabWithDependencies($"{CharacterResourceRoot}/{slot.Trim()}");

        public static GameObject LoadItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            var trimmed = itemId.Trim();
            var prefab = LoadPrefabWithDependencies($"{ItemResourceRoot}/{trimmed}");
            if (prefab != null)
                return prefab;

            if (ItemSubstitutes.TryGetValue(trimmed, out var substitute))
                return LoadPrefabWithDependencies($"{ItemResourceRoot}/{substitute}");

            return null;
        }

        public static bool TryResolveItemSubstitute(string itemId, out string substituteId)
        {
            substituteId = null;
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            return ItemSubstitutes.TryGetValue(itemId.Trim(), out substituteId);
        }

        static GameObject LoadPrefabWithDependencies(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
                return null;

            ValidateRuntimePrefab(prefab, resourcePath);
            return prefab;
        }

        /// <summary>
        /// Ensures CC0 prefabs expose mesh, animator, and avatar for Play-mode spawns.
        /// </summary>
        public static void PrepareSpawnedInstance(GameObject instance)
        {
            if (instance == null)
                return;

            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
        }

        static void ValidateRuntimePrefab(GameObject prefab, string resourcePath)
        {
            if (prefab == null)
                return;

            var hasMesh = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null
                || prefab.GetComponentInChildren<MeshRenderer>(true) != null;
            if (!hasMesh)
                Debug.LogWarning($"[CC0] Resources prefab has no mesh renderer: {resourcePath}", prefab);
        }
    }
}
