using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Shared rules for which cave colliders are gameplay-critical (Unity NavMesh + physics best practice: colliders define walkable bounds).</summary>
    static class CaveColliderUtility
    {
        public static bool IsMazeVolumeCollider(GameObject go) =>
            go != null && IsMazeVolumeCollider(go.transform);

        public static bool IsMazeVolumeCollider(Transform transform) =>
            transform != null && IsMazeVolumeCollider((Component)transform);

        public static bool IsMazeVolumeCollider(Component component)
        {
            if (component == null)
                return false;

            var t = component.transform;
            while (t != null)
            {
                if (t.GetComponent<CaveMazeVolumeMarker>() != null)
                    return true;
                if (t.name == CaveMazeVolumeBuilder.MazeVolumeRootName)
                    return true;
                t = t.parent;
            }

            return false;
        }

        public static bool IsProtectedPlayCollider(Collider collider, Transform caveRoot = null)
        {
            if (collider == null)
                return false;

            if (IsMazeVolumeCollider(collider))
                return true;

            if (IsAuthoredKitPiece(collider, caveRoot))
                return true;

            var n = collider.gameObject.name;
            if (n.StartsWith("CaveBlock_"))
            {
                if (n.Contains("Minable") || collider.GetComponentInParent<MinableRock>() != null)
                    return true;
                return CaveRendererVisibility.HasVisibleRenderer(collider, true);
            }

            if (n.StartsWith("Ledge_") || n == "Pit_Volume" ||
                collider.GetComponentInParent<CaveWalkableMarker>() != null)
                return true;

            if (n == CaveEnclosureShellBuilder.FloorRootName ||
                n == CaveLayoutPrototypeGenerator.FlatFloorRootName ||
                n.Contains(CaveEnclosureShellBuilder.FloorRootName))
                return true;

            return n.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix) || n.Contains("SpawnGroundPad");
        }

        /// <summary>Lava-tube prefab modules (entrance floors/walls) must keep colliders and renderers.</summary>
        public static bool IsAuthoredKitPiece(Collider collider, Transform caveRoot)
        {
            if (collider == null)
                return false;

            if (collider.GetComponent<CavePrefabSource>() != null ||
                collider.GetComponentInParent<CavePrefabSource>() != null)
                return true;

            var n = collider.gameObject.name;
            if (n.Contains("SM_Floor") || n.Contains("SM_Wall") || n.Contains("SM_Ceiling") ||
                n.Contains("SM_Rockfall") || n.Contains("asset_reference:"))
                return true;

            if (caveRoot == null)
                return false;

            var entrance = caveRoot.Find("Entrance");
            return entrance != null && collider.transform.IsChildOf(entrance);
        }

        public static int EnsureMazeVolumeColliders(Transform caveRoot)
        {
            Transform maze = null;
            if (caveRoot != null)
            {
                maze = caveRoot.Find($"SplineMesh/{CaveMazeVolumeBuilder.MazeVolumeRootName}");
                if (maze == null)
                {
                    maze = caveRoot.Find(
                        $"{CaveAdventureCaveGenerator.GeometryRootName}/{CaveAdventureShellBuilder.ShellRootName}");
                }
            }

            if (maze == null)
                return 0;

            if (maze.GetComponent<CaveMazeVolumeMarker>() == null)
                maze.gameObject.AddComponent<CaveMazeVolumeMarker>();

            var fixedCount = 0;
            foreach (var mf in maze.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;
                if (go.GetComponent<Collider>() != null)
                    continue;

                var box = go.AddComponent<BoxCollider>();
                var size = go.transform.localScale;
                box.size = Vector3.one;
                box.center = Vector3.zero;
                box.isTrigger = false;
                fixedCount++;
            }

            return fixedCount;
        }
    }
}
