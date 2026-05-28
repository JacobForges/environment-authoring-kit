#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CavePortalSpawnLinker
    {
        public static void LinkMainScenePortal(Transform caveRoot, Transform explicitSpawn = null) =>
            LinkScenePortals(caveRoot, explicitSpawn);

        public static int LinkScenePortals(Transform caveRoot, Transform explicitSpawn = null)
        {
            if (caveRoot == null)
                return 0;

            var spawn = CaveEntranceTeleport.ResolveSpawnPoint(explicitSpawn);
            if (spawn == null)
                return 0;

            var linked = 0;
            var assigned = CaveBuildPortalSettings.PortalForBuild;
            if (TryLinkPortalObject(assigned, spawn))
                linked++;

            if (linked == 0 && TryLinkPortalObject(GameObject.Find("PortalFive"), spawn))
                linked++;
            if (linked == 0 && TryLinkPortalObject(GameObject.Find("MainScene_CavePortal"), spawn))
                linked++;

            if (linked == 0)
                linked += TryLinkNearestCaveEntrancePortal(spawn);

            EnsureWarpTransition(caveRoot);
            if (linked > 0)
                Debug.Log($"[CavePortal] Linked {linked} cave portal(s) to {spawn.name} (shop portals untouched).");

            return linked;
        }

        static bool TryLinkPortalObject(GameObject portalGo, Transform spawn)
        {
            if (!IsCaveSurfacePortal(portalGo))
                return false;

            var cavePortal = portalGo.GetComponent("CaveEntrancePortal");
            if (cavePortal == null)
                return false;

            var so = new SerializedObject(cavePortal);
            var prop = so.FindProperty("caveEntranceSpawn");
            if (prop == null)
                return false;

            prop.objectReferenceValue = spawn;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static bool IsCaveSurfacePortal(GameObject go)
        {
            if (go == null)
                return false;

            if (go.name.Contains("(1)"))
                return false;

            return go.name == "PortalFive"
                   || go.name == "MainScene_CavePortal"
                   || go.GetComponent("CaveEntrancePortal") != null;
        }

        /// <summary>Link only the nearest single cave portal — avoids wiring every portal in the scene.</summary>
        static int TryLinkNearestCaveEntrancePortal(Transform spawn)
        {
            MonoBehaviour nearest = null;
            var bestDist = float.MaxValue;
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
            foreach (var behaviour in all)
            {
                if (behaviour == null || behaviour.GetType().Name != "CaveEntrancePortal")
                    continue;
                if (!IsCaveSurfacePortal(behaviour.gameObject))
                    continue;

                var dist = (behaviour.transform.position - spawn.position).sqrMagnitude;
                if (dist >= bestDist)
                    continue;

                bestDist = dist;
                nearest = behaviour;
            }

            if (nearest != null && TryLinkPortalObject(nearest.gameObject, spawn))
                return 1;

            return 0;
        }

        static void EnsureWarpTransition(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var warpType = System.Type.GetType("CaveWarpTransition, Assembly-CSharp");
            if (warpType != null && caveRoot.GetComponent(warpType) == null)
                caveRoot.gameObject.AddComponent(warpType);

            var introType = System.Type.GetType("CaveIntroDirector, Assembly-CSharp");
            if (introType != null && caveRoot.GetComponent(introType) == null)
                caveRoot.gameObject.AddComponent(introType);
        }
    }
}
#endif
