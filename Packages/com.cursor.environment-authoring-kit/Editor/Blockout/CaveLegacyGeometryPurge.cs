using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Removes old blockout/SUIMONO junk so spline mesh builds are not mixed with flat planes.</summary>
    static class CaveLegacyGeometryPurge
    {
        public static int Purge(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var removed = 0;

            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == caveRoot)
                    continue;

                var n = t.name;
                if (n.Contains("SUIMONO_Surface") || n.Contains("Suimono_ObjectScale") ||
                    n == "SUIMONO_Module" || n == "SUIMONO_System" || n == "Suimono_Object")
                {
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                    continue;
                }

                if (n.StartsWith("TunnelRing_") || n.StartsWith("TunnelSegment_") || n == "BlockoutTunnel")
                {
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                }
            }

            var pool = caveRoot.Find("Water/UndergroundRiver_Pool");
            if (pool != null)
            {
                for (var i = pool.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(pool.GetChild(i).gameObject);
                    removed++;
                }
            }

            return removed;
        }
    }
}
