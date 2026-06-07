using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Removes solid colliders on geometry with no visible renderer (invisible wall trap fix).</summary>
    static class CaveInvisibleColliderUtility
    {
        public static int StripInvisibleSolidColliders(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var removed = 0;
            foreach (var col in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger)
                    continue;
                if (CaveColliderUtility.IsProtectedPlayCollider(col, caveRoot))
                    continue;
                if (col.GetComponentInParent<MinableRock>() != null)
                    continue;

                if (!CaveRendererVisibility.HasVisibleRenderer(col, true))
                {
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                }
            }

            return removed;
        }

        public static int StripShellBlockColliders(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var tunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
            if (tunnel == null)
                return 0;

            var removed = 0;
            foreach (var block in tunnel.GetComponentsInChildren<Transform>(true))
            {
                if (block == null || !block.name.StartsWith("CaveBlock_"))
                    continue;
                if (block.name.Contains("Minable"))
                    continue;

                var col = block.GetComponent<Collider>();
                if (col != null)
                {
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                }
            }

            return removed;
        }

        public static int StripForAdventure(Transform caveRoot)
        {
            var removed = StripShellBlockColliders(caveRoot);
            removed += StripInvisibleSolidColliders(caveRoot);
            return removed;
        }
    }
}
