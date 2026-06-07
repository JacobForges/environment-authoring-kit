using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Landmark-only legendary spawn roster for Hollow Titan — never shared with world scatter tables.
    /// </summary>
    public static class HollowTitanLandmarkSpawnCatalog
    {
        public const string DefaultEnemyRoleId = "HollowTitanGuard";
        public const string DefaultEnemyCc0Slot = "ENEMY_Annex_1";
        public const string BossRoleId = "HollowTitanAlpha";
        public const string GrapplingHookLootId = "G-GRAPPLING_HOOK";
        public const string GrapplingHookCc0Substitute = "W03";
        public const string ManifestScope = "HollowTitanLandmark_only";

        /// <summary>Floor 1 legendary pickup — grappling hook (CC0 W03 / ARCH_HOOK substitute).</summary>
        public const int GrapplingHookFloorIndex = 1;

        public const float BossScaleMultiplier = 1.85f;
        public const float BossHpMultiplier = 3.1f;

        public const WorldSurfaceBiomeId LandmarkBiome = WorldSurfaceBiomeId.AnnexLabyrinth;

        /// <summary>Legendary-tier enemy role ids (fewer, tougher than world scatter).</summary>
        public static readonly string[] LegendaryEnemyRoleIds =
        {
            "HollowTitanGuard",
            "HollowTitanChampion",
            "HollowTitanWarden",
            "HollowTitanAlpha",
        };

        /// <summary>CC0 slots for landmark legendary roster (includes boss-tier variants).</summary>
        public static readonly string[] LegendaryEnemyCc0Slots =
        {
            "ENEMY_Annex_1",
            "BOSS_Add_1",
            "BOSS_Add_2",
            "BOSS_Stage",
        };

        /// <summary>Titan-exclusive legendary loot ids (not used by world scatter tables).</summary>
        public static readonly string[] LootDefinitionIds =
        {
            GrapplingHookLootId,
            "K01",
            "K02",
            "C03",
            "C04",
            "G-G01",
            "G-G06",
            "G-C05",
            "G-T06",
            "T01",
        };

        /// <summary>Floor-index → enemy role for one guard per boulder platform.</summary>
        public static string RoleForFloor(int floorIndex, int topFloorIndex)
        {
            if (floorIndex >= topFloorIndex)
                return BossRoleId;

            if (floorIndex <= 1)
                return "HollowTitanGuard";
            if (floorIndex <= 3)
                return "HollowTitanChampion";
            return "HollowTitanWarden";
        }

        public static string Cc0SlotForEnemyRole(string roleId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
                return DefaultEnemyCc0Slot;

            return roleId switch
            {
                DefaultEnemyRoleId => "ENEMY_Annex_1",
                "HollowTitanChampion" => "BOSS_Add_1",
                "HollowTitanWarden" => "BOSS_Add_2",
                BossRoleId => "BOSS_Stage",
                _ => DefaultEnemyCc0Slot,
            };
        }

        public static string PickLegendaryRoleId(System.Random rng, int floorIndex)
        {
            var top = LegendaryEnemyRoleIds.Length - 1;
            return RoleForFloor(floorIndex, top);
        }

        public static GameObject LoadEnemyPrefab(string roleId) =>
            Cc0RuntimePrefabResolver.LoadCharacter(Cc0SlotForEnemyRole(roleId));

        public static GameObject LoadLootPrefab(string defId) =>
            Cc0RuntimePrefabResolver.LoadItem(defId);
    }
}
