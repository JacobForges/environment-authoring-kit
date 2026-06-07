#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Keeps MainScene PortalFive cave teleport working with an above-ground entrance mouth.
    /// Does not modify PortalFive (1) shop portals.
    /// </summary>
    static class CaveEntrancePortalPreserver
    {
        public static void Apply(Transform caveRoot, SceneGroundInfo ground, Transform explicitSpawn = null)
        {
            if (caveRoot == null)
                return;

            var spawn = CaveEntranceTeleport.ResolveSpawnPoint(explicitSpawn);
            if (spawn == null)
                return;

            if (CavePortalSpawnLinker.LinkScenePortals(caveRoot, spawn) == 0)
                Debug.LogWarning("[CavePortal] No cave portal linked. Use Window → Environment Kit → Setup MainScene Cave Portal.");

            OrientSurfacePortalTowardMouth(caveRoot);
        }

        static void OrientSurfacePortalTowardMouth(Transform caveRoot)
        {
            var portalGo = GameObject.Find("PortalFive");
            if (portalGo == null || portalGo.name.Contains("(1)"))
                portalGo = GameObject.Find("MainScene_CavePortal");

            if (portalGo == null || portalGo.name.Contains("(1)"))
                return;

            var mouth = caveRoot.Find("Entrance/CaveEntrance_Marker");
            if (mouth == null)
                mouth = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (mouth == null)
                return;

            var toMouth = mouth.position - portalGo.transform.position;
            toMouth.y = 0f;
            if (toMouth.sqrMagnitude < 0.25f)
                return;

            CaveEditorUndo.RecordObject(portalGo.transform, "Orient Cave Portal");
            portalGo.transform.rotation = Quaternion.LookRotation(toMouth.normalized, Vector3.up);
        }
    }
}
#endif
