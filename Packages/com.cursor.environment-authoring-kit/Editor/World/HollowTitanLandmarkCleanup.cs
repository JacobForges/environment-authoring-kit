#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Removes legacy exterior props and radial CC0 trim from Hollow Titan landmarks.</summary>
    static class HollowTitanLandmarkCleanup
    {
        public static void StripLegacyExteriorAndTrim(Transform root)
        {
            if (root == null)
                return;

            ClearChild(root, "Exterior/DeadBranches");
            ClearChild(root, "HollowTitan_Proxy");
            ClearChild(root, "HollowTitan_L01");

            var floors = root.Find("Interior/Floors");
            if (floors != null)
            {
                for (var i = 0; i < floors.childCount; i++)
                {
                    var floor = floors.GetChild(i);
                    var trim = floor.Find("PlateTrim");
                    if (trim != null)
                        Object.DestroyImmediate(trim.gameObject);
                }
            }

            StripLegacyPropNamesUnder(root.Find("Exterior"));
            StripEditorPreviews(root);
        }

        public static void StripEditorPreviews(Transform root)
        {
            if (root == null)
                return;

            foreach (var preview in root.GetComponentsInChildren<Transform>(true))
            {
                if (preview != null && preview.name == "EditorPreview")
                    Object.DestroyImmediate(preview.gameObject);
            }
        }

        public static void SetInteriorRenderersVisible(Transform root, bool visible)
        {
            var interior = root != null ? root.Find("Interior") : null;
            if (interior == null)
                return;

            foreach (var mr in interior.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null)
                    mr.enabled = visible;
            }
        }

        public static bool TerrainContainsWorldXZ(Terrain tile, Vector3 worldPos)
        {
            if (tile?.terrainData == null)
                return false;

            var origin = tile.transform.position;
            var size = tile.terrainData.size;
            return worldPos.x >= origin.x && worldPos.x <= origin.x + size.x &&
                   worldPos.z >= origin.z && worldPos.z <= origin.z + size.z;
        }

        static void StripLegacyPropNamesUnder(Transform exterior)
        {
            if (exterior == null)
                return;

            for (var i = exterior.childCount - 1; i >= 0; i--)
            {
                var child = exterior.GetChild(i);
                if (child.name.StartsWith("Branch_", System.StringComparison.Ordinal) ||
                    child.name.StartsWith("Log_", System.StringComparison.Ordinal) ||
                    child.name.StartsWith("Tree_", System.StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        static void ClearChild(Transform root, string path)
        {
            var node = root.Find(path);
            if (node == null)
                return;

            for (var i = node.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(node.GetChild(i).gameObject);
        }
    }
}
#endif
