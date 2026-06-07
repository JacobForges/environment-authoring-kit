#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Landmark-only enemy and loot tables — never shared with <see cref="WorldLootSpawnDirector"/>.
    /// </summary>
    public static class HollowTitanLandmarkSpawnManifest
    {
        public const string ManifestRel = CaveBuildAgentContextExporter.Folder + "/HollowTitanLandmarkSpawnManifest.json";

        [Serializable]
        public sealed class ManifestFile
        {
            public int seed;
            public string generatedUtc;
            public string scope = HollowTitanLandmarkSpawnCatalog.ManifestScope;
            public string worldEnemyScope = WorldEnemySpawnCatalog.ManifestScope;
            public string defaultEnemyRoleId = HollowTitanLandmarkSpawnCatalog.DefaultEnemyRoleId;
            public string defaultEnemyCc0Slot = HollowTitanLandmarkSpawnCatalog.DefaultEnemyCc0Slot;
            public string[] legendaryEnemyRoleIds = Array.Empty<string>();
            public string[] legendaryEnemyCc0Slots = Array.Empty<string>();
            public string[] lootDefinitionIds = Array.Empty<string>();
            public int enemiesPerFloor = 2;
            public int lootPerFloor = 1;
            public int patrolNodesPerFloor = 3;
            public LootSubstituteEntry[] lootSubstitutes = Array.Empty<LootSubstituteEntry>();
        }

        [Serializable]
        public sealed class LootSubstituteEntry
        {
            public string lootId;
            public string substituteCc0ItemId;
            public string reason;
        }

        static readonly LootSubstituteEntry[] LootSubstitutes =
        {
            new LootSubstituteEntry
            {
                lootId = HollowTitanLandmarkSpawnCatalog.GrapplingHookLootId,
                substituteCc0ItemId = HollowTitanLandmarkSpawnCatalog.GrapplingHookCc0Substitute,
                reason = "Floor-1 legendary grappling hook — CC0 W03 (ARCH_HOOK line hook).",
            },
            new LootSubstituteEntry
            {
                lootId = "T01",
                substituteCc0ItemId = "G-T06",
                reason = "No T01 CC0 prefab — nearest gem-tool cluster (G-T06).",
            },
        };

        /// <summary>Titan-exclusive loot ids (not used by world scatter tables).</summary>
        public static readonly string[] LootDefinitionIds = HollowTitanLandmarkSpawnCatalog.LootDefinitionIds;

        public static int EnemiesPerFloor => CaveBuildCursorSettings.LoadOrCreate().hollowTitanEnemiesPerFloor;
        public static int LootPerFloor => CaveBuildCursorSettings.LoadOrCreate().hollowTitanLootPerFloor;
        public static int PatrolNodesPerFloor => CaveBuildCursorSettings.LoadOrCreate().hollowTitanPatrolNodesPerFloor;

        public static void Write(int seed, Transform landmarkRoot)
        {
            WorldRuntimeResourcesAuthor.EnsureHollowTitanLandmarkPrefabsInResources();

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ManifestRel);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var file = new ManifestFile
            {
                seed = seed,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                defaultEnemyRoleId = HollowTitanLandmarkSpawnCatalog.DefaultEnemyRoleId,
                defaultEnemyCc0Slot = HollowTitanLandmarkSpawnCatalog.DefaultEnemyCc0Slot,
                legendaryEnemyRoleIds = HollowTitanLandmarkSpawnCatalog.LegendaryEnemyRoleIds,
                legendaryEnemyCc0Slots = HollowTitanLandmarkSpawnCatalog.LegendaryEnemyCc0Slots,
                lootDefinitionIds = LootDefinitionIds,
                enemiesPerFloor = EnemiesPerFloor,
                lootPerFloor = LootPerFloor,
                patrolNodesPerFloor = PatrolNodesPerFloor,
                lootSubstitutes = LootSubstitutes,
            };

            File.WriteAllText(path, JsonUtility.ToJson(file, true));
            Debug.Log(
                $"[Surface] Hollow Titan spawn manifest written — {LootDefinitionIds.Length} loot defs, " +
                $"scope landmark-only ({ManifestRel}).",
                landmarkRoot != null ? landmarkRoot.gameObject : null);
        }

        public static string PickLootId(System.Random rng)
        {
            if (LootDefinitionIds.Length == 0)
                return "K01";
            return LootDefinitionIds[rng.Next(LootDefinitionIds.Length)];
        }
    }
}
#endif
