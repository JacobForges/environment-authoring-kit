using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Find Polyvania four-way intersection for social-hub content placement.</summary>
    public static class PolyvaniaSocialHubResolver
    {
        public const string MapObjectName = "PolyvaniaTownMap";

        public static bool TryResolveFourWayCenter(out Vector3 center, out float hubRadiusM)
        {
            center = Vector3.zero;
            hubRadiusM = 12f;

            var map = GameObject.Find(MapObjectName);
            if (map == null)
                return false;

            if (TryNamedHub(map.transform, out center))
                return true;

            if (TryRoadCrossingCenter(map.transform, out center))
                return true;

            center = map.transform.position;
            return true;
        }

        static bool TryNamedHub(Transform map, out Vector3 center)
        {
            center = default;
            Transform best = null;
            var bestScore = int.MinValue;
            var tieBreakDistance = float.MaxValue;
            var spawn = GameObject.Find("PlayerSpawnPoint");
            var anchor = spawn != null ? spawn.transform.position : map.position;

            foreach (var t in map.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == map)
                    continue;

                var n = t.name ?? string.Empty;
                var score = ScoreName(n);
                if (score <= 0)
                    continue;

                var d = Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(anchor.x, anchor.z));
                if (score > bestScore || (score == bestScore && d < tieBreakDistance))
                {
                    bestScore = score;
                    best = t;
                    tieBreakDistance = d;
                }
            }

            if (best == null)
                return false;

            center = best.position;
            return true;
        }

        static int ScoreName(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("intersection"))
                return 100;
            if (n.Contains("cross_") || n.Contains("_cross") || n.Contains("cross"))
                return 98;
            if (n.Contains("crossroad") || n.Contains("cross_road") || n.Contains("fourway"))
                return 95;
            if (n.Contains("town_center") || n.Contains("towncenter"))
                return 90;
            if (n.Equals("center") || n.Contains("plaza") || n.Contains("square") || n.Contains("hub"))
                return 80;
            if (n.Contains("main_street") || n.Contains("mainstreet"))
                return 60;
            return 0;
        }

        static bool TryRoadCrossingCenter(Transform map, out Vector3 center)
        {
            center = default;
            var renderers = map.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return false;

            var origin = map.position;
            var crossWeightedCenter = Vector3.zero;
            var crossCount = 0;
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            var count = 0;

            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                var n = r.gameObject.name.ToLowerInvariant();
                if (!n.Contains("road") && !n.Contains("street") && !n.Contains("lane") && !n.Contains("asphalt"))
                    continue;

                var b = r.bounds;
                if (n.Contains("cross") || n.Contains("intersection"))
                {
                    crossWeightedCenter += b.center;
                    crossCount++;
                }
                minX = Mathf.Min(minX, b.min.x);
                maxX = Mathf.Max(maxX, b.max.x);
                minZ = Mathf.Min(minZ, b.min.z);
                maxZ = Mathf.Max(maxZ, b.max.z);
                count++;
            }

            if (crossCount > 0)
            {
                var c = crossWeightedCenter / crossCount;
                center = new Vector3(c.x, origin.y, c.z);
                return true;
            }

            if (count < 2)
                return false;

            center = new Vector3((minX + maxX) * 0.5f, origin.y, (minZ + maxZ) * 0.5f);
            return true;
        }

        public static Vector3 OffsetFromHub(Vector3 hub, float x, float z) =>
            new Vector3(hub.x + x, hub.y, hub.z + z);
    }
}
