using System;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Resolves entity positions from ContentLayoutWorldAnchors.json + live hub.</summary>
    public static class ContentLayoutSlotResolver
    {
        /// <summary>Bump when anchor JSON schema changes — helps verify Unity picked up recompile.</summary>
        public const int ResolverSchemaVersion = 4;

        static AnchorFile _cached;
        static Vector3 _cachedHub;
        static bool _loaded;
        static bool _hubReady;

        public static void InvalidateCache()
        {
            _loaded = false;
            _hubReady = false;
            _cached = null;
            _cachedHub = default;
            ContentLayoutWorldSnapshotCache.InvalidateCache();
        }

        public static bool TryResolveNpc(string npcId, out Vector3 world, out float rotationY)
        {
            world = default;
            rotationY = 0f;
            if (!TryGetNpcSlot(npcId, out var slot))
                return false;

            world = SlotWorld(slot);
            rotationY = slot.rotationY;
            return true;
        }

        public static bool TryResolveEnemy(string enemyId, out Vector3 world)
        {
            world = default;
            if (!TryGetEnemySlot(enemyId, out var slot))
                return false;

            world = SlotWorld(slot);
            return true;
        }

        public static bool TryResolveEnemyPatrol(string enemyId, out Vector3[] waypoints)
        {
            waypoints = null;
            if (!TryGetEnemySlot(enemyId, out var slot)
                || slot.patrolOffsetX == null
                || slot.patrolOffsetZ == null
                || slot.patrolOffsetX.Length == 0)
                return false;

            var center = ZoneCenter(slot.zoneId);
            var count = Math.Min(slot.patrolOffsetX.Length, slot.patrolOffsetZ.Length);
            waypoints = new Vector3[count];
            for (var i = 0; i < count; i++)
                waypoints[i] = center + new Vector3(slot.patrolOffsetX[i], 0f, slot.patrolOffsetZ[i]);

            return true;
        }

        public static bool TryResolveProp(string propId, out Vector3 world)
        {
            world = default;
            try
            {
                if (!TryGetPropSlot(propId, out var slot))
                    return false;

                world = SlotWorld(slot);
                if (slot.height >= 1f)
                    world.y = slot.height;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[ContentLayout] Prop slot resolve failed for '{propId}' (schema v{ResolverSchemaVersion}): {ex.Message}");
                return false;
            }
        }

        static Vector3 SlotWorld(EntitySlot slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.zoneId))
                return HubOrigin();

            var center = ZoneCenter(slot.zoneId);
            return center + new Vector3(slot.offsetX, 0f, slot.offsetZ);
        }

        static bool TryGetNpcSlot(string id, out EntitySlot slot)
        {
            slot = null;
            EnsureLoaded();
            return _cached?.npcSlots != null && _cached.npcSlots.TryGet(id, out slot) && IsValidSlot(slot);
        }

        static bool TryGetEnemySlot(string id, out EntitySlot slot)
        {
            slot = null;
            EnsureLoaded();
            return _cached?.enemySlots != null && _cached.enemySlots.TryGet(id, out slot) && IsValidSlot(slot);
        }

        static bool TryGetPropSlot(string id, out EntitySlot slot)
        {
            slot = null;
            EnsureLoaded();
            return _cached?.propSlots != null && _cached.propSlots.TryGet(id, out slot) && IsValidSlot(slot);
        }

        static bool IsValidSlot(EntitySlot slot) =>
            slot != null && !string.IsNullOrEmpty(slot.zoneId);

        static Vector3 ZoneCenter(string zoneId)
        {
            // Town center should always track live four-way intersection in the open scene.
            // Snapshot exports can be stale if generated before resolver improvements.
            if (string.Equals(zoneId, "town_center", StringComparison.Ordinal))
                return HubOrigin();

            if (ContentLayoutWorldSnapshotCache.TryGetZoneCenter(zoneId, out var exported))
                return exported;

            EnsureLoaded();
            var hub = HubOrigin();
            if (_cached?.zones != null && _cached.zones.TryGetHubOffset(zoneId, out var x, out var z))
                return hub + new Vector3(x, 0f, z);
            return hub;
        }

        static Vector3 HubOrigin()
        {
            if (_hubReady)
                return _cachedHub;

            if (PolyvaniaSocialHubResolver.TryResolveFourWayCenter(out var hub, out _))
            {
                if (ContentLayoutGroundSnap.TrySampleContentGroundY(hub, out var y))
                    hub.y = y;
                _cachedHub = hub;
                _hubReady = true;
                return hub;
            }

            var spawn = GameObject.Find("PlayerSpawnPoint");
            _cachedHub = spawn != null ? spawn.transform.position : Vector3.zero;
            _hubReady = true;
            return _cachedHub;
        }

        static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            try
            {
                var path = Path.Combine(Application.dataPath, "EnvironmentKit/ContentLayout/ContentLayoutWorldAnchors.json");
                if (!File.Exists(path))
                {
                    Debug.LogWarning(
                        $"[ContentLayout] World anchors missing at {path} — using brief fallbacks (schema v{ResolverSchemaVersion}).");
                    return;
                }

                _cached = JsonUtility.FromJson<AnchorFile>(File.ReadAllText(path));
                if (_cached?.propSlots == null)
                    Debug.LogWarning(
                        $"[ContentLayout] propSlots failed to load from anchors — check flat offsetX/offsetZ JSON (schema v{ResolverSchemaVersion}).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ContentLayout] World anchors load failed: {ex.Message}");
            }

            HubOrigin();
        }

        [Serializable]
        class AnchorFile
        {
            public ZoneTable zones;
            public NpcSlotTable npcSlots;
            public EnemySlotTable enemySlots;
            public PropSlotTable propSlots;
        }

        [Serializable]
        class ZoneTable
        {
            public ZoneEntry town_center;
            public ZoneEntry east_trail;
            public ZoneEntry cave_gate;
            public ZoneEntry battle_arena;
            public ZoneEntry wild_east;
            public ZoneEntry wild_west;

            public bool TryGetHubOffset(string zoneId, out float x, out float z)
            {
                x = 0f;
                z = 0f;
                var entry = zoneId switch
                {
                    "town_center" => town_center,
                    "east_trail" => east_trail,
                    "cave_gate" => cave_gate,
                    "battle_arena" => battle_arena,
                    "wild_east" => wild_east,
                    "wild_west" => wild_west,
                    _ => null
                };

                if (entry == null)
                    return false;

                x = entry.hubOffsetX;
                z = entry.hubOffsetZ;
                return true;
            }
        }

        [Serializable]
        class ZoneEntry
        {
            public float hubOffsetX;
            public float hubOffsetZ;
        }

        [Serializable]
        class EntitySlot
        {
            public string zoneId;
            public float offsetX;
            public float offsetZ;
            public float rotationY;
            public float height;
            public float[] patrolOffsetX;
            public float[] patrolOffsetZ;
        }

        [Serializable]
        class NpcSlotTable
        {
            public EntitySlot npc_mara_dispatch;
            public EntitySlot npc_ambient_sigrid;
            public EntitySlot npc_sera_scout;
            public EntitySlot npc_lumen_gate;
            public EntitySlot npc_ambient_jonas;
            public EntitySlot npc_ren_coach;
            public EntitySlot npc_ambient_ren;

            public bool TryGet(string id, out EntitySlot slot)
            {
                slot = id switch
                {
                    "npc_mara_dispatch" => npc_mara_dispatch,
                    "npc_ambient_sigrid" => npc_ambient_sigrid,
                    "npc_sera_scout" => npc_sera_scout,
                    "npc_lumen_gate" => npc_lumen_gate,
                    "npc_ambient_jonas" => npc_ambient_jonas,
                    "npc_ren_coach" => npc_ren_coach,
                    "npc_ambient_ren" => npc_ambient_ren ?? npc_ren_coach,
                    // Current generated brief NPC ids (map to stable plaza slots).
                    "NPC_QUEST_ELARA_MR" => npc_mara_dispatch,
                    "NPC_TRAINER_BRANN_BC" => npc_ren_coach,
                    "NPC_MERCHANT_BRANN_TC" => npc_ambient_ren ?? npc_ren_coach,
                    "NPC_AMBIENT_TOREN_GD" => npc_lumen_gate,
                    "NPC_AMBIENT_IVO_MR" => npc_ambient_sigrid,
                    "NPC_AMBIENT_LYRA_BSK" => npc_sera_scout,
                    "NPC_GATE_RHEA_GT" => npc_ambient_jonas,
                    "NPC_QUEST_NIA_TC_PROXY" => npc_ren_coach,
                    "NPC_QUEST_DAX_TC_PROXY" => npc_mara_dispatch,
                    "NPC_QUEST_VENN_TC_PROXY" => npc_lumen_gate,
                    _ => null
                };
                return slot != null;
            }
        }

        [Serializable]
        class EnemySlotTable
        {
            public EntitySlot enemy_spawner_east_trail;
            public EntitySlot enemy_spawner_arena_ring;

            public bool TryGet(string id, out EntitySlot slot)
            {
                slot = id switch
                {
                    "enemy_spawner_east_trail" => enemy_spawner_east_trail,
                    "enemy_spawner_arena_ring" => enemy_spawner_arena_ring,
                    // Current generated brief enemy ids.
                    "ENEMY_SPAWNER_WEAST_WOLF_PACK_A" => enemy_spawner_east_trail,
                    "ENEMY_SPAWNER_WWEST_BANDIT_SCOUTS_A" => enemy_spawner_arena_ring,
                    _ => null
                };
                return slot != null;
            }
        }

        [Serializable]
        class PropSlotTable
        {
            public EntitySlot prop_depot_sign;
            public EntitySlot prop_mara_manifest_crate;
            public EntitySlot prop_depot_pennant;
            public EntitySlot prop_sera_fork_sign;
            public EntitySlot prop_sera_fork_pennant;
            public EntitySlot prop_jonas_rumor_board;
            public EntitySlot prop_cave_mouth_arch;
            public EntitySlot prop_ren_bleacher_banners;
            public EntitySlot prop_arena_supply_crate;

            public bool TryGet(string id, out EntitySlot slot)
            {
                slot = id switch
                {
                    "prop_depot_sign" => prop_depot_sign,
                    "prop_mara_manifest_crate" => prop_mara_manifest_crate,
                    "prop_depot_pennant" => prop_depot_pennant,
                    "prop_sera_fork_sign" => prop_sera_fork_sign,
                    "prop_sera_fork_pennant" => prop_sera_fork_pennant,
                    "prop_jonas_rumor_board" => prop_jonas_rumor_board,
                    "prop_cave_mouth_arch" => prop_cave_mouth_arch,
                    "prop_ren_bleacher_banners" => prop_ren_bleacher_banners,
                    "prop_arena_supply_crate" => prop_arena_supply_crate,
                    // Current generated brief prop ids (map to nearest authored anchors).
                    "prop_tc_notice_board" => prop_depot_sign,
                    "sign_tc_to_easttrail_01" => prop_sera_fork_sign,
                    "prop_tc_east_gate_crates" => prop_mara_manifest_crate,
                    "PROP_ET_RELAY_POST_01" => prop_jonas_rumor_board,
                    "prop_trail_lantern_pair" => prop_sera_fork_pennant,
                    "PROP_CG_HAZARD_BOARD_01" => prop_cave_mouth_arch,
                    "prop_cavegate_barricade_crates" => prop_cave_mouth_arch,
                    "PROP_BA_RULE_BANNER_01" => prop_ren_bleacher_banners,
                    "prop_arena_muster_01" => prop_arena_supply_crate,
                    "prop_arena_entrance_lanterns" => prop_ren_bleacher_banners,
                    _ => null
                };
                return slot != null;
            }
        }
    }
}
