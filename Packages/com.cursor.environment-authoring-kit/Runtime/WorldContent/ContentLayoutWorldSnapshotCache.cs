using System;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Loads scene-exported zone centers from CaveBuildContentLayoutWorldSnapshot.json.</summary>
    public static class ContentLayoutWorldSnapshotCache
    {
        const string SnapshotPath = "Assets/EnvironmentKit/Generated/CaveBuildContentLayoutWorldSnapshot.json";

        static SnapshotFile _cached;
        static bool _loaded;

        public static void InvalidateCache()
        {
            _loaded = false;
            _cached = null;
        }

        public static bool TryGetZoneCenter(string zoneId, out Vector3 center)
        {
            center = default;
            EnsureLoaded();
            if (_cached?.zones == null || string.IsNullOrEmpty(zoneId))
                return false;

            var entry = zoneId switch
            {
                "town_center" => _cached.zones.town_center,
                "east_trail" => _cached.zones.east_trail,
                "cave_gate" => _cached.zones.cave_gate,
                "battle_arena" => _cached.zones.battle_arena,
                "wild_east" => _cached.zones.wild_east,
                "wild_west" => _cached.zones.wild_west,
                _ => null
            };

            if (entry == null)
                return false;

            center = entry.ResolveCenter();
            return true;
        }

        static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            try
            {
                var path = Path.Combine(Application.dataPath, "EnvironmentKit/Generated/CaveBuildContentLayoutWorldSnapshot.json");
                if (File.Exists(path))
                    _cached = JsonUtility.FromJson<SnapshotFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ContentLayout] Snapshot load failed: {ex.Message}");
            }
        }

        [Serializable]
        class SnapshotFile
        {
            public int version;
            public ZoneTable zones;
        }

        [Serializable]
        class ZoneTable
        {
            public ZoneCenterEntry town_center;
            public ZoneCenterEntry east_trail;
            public ZoneCenterEntry cave_gate;
            public ZoneCenterEntry battle_arena;
            public ZoneCenterEntry wild_east;
            public ZoneCenterEntry wild_west;
        }

        [Serializable]
        class ZoneCenterEntry
        {
            public float centerX;
            public float centerY;
            public float centerZ;
            public LegacyCenterVec center;

            public Vector3 ResolveCenter()
            {
                if (center != null)
                    return new Vector3(center.x, center.y, center.z);
                return new Vector3(centerX, centerY, centerZ);
            }
        }

        [Serializable]
        class LegacyCenterVec
        {
            public float x;
            public float y;
            public float z;
        }
    }
}
