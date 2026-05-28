using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Removes underground water geometry — avoids broken Ignite/SUIMONO blue planes in maze caves.</summary>
    public static class CaveWaterUtility
    {
        public static void ClearAllWater(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var waterRoot = caveRoot.Find("Water");
            if (waterRoot != null)
            {
                for (var i = waterRoot.childCount - 1; i >= 0; i--)
                    CaveEditorUndo.DestroyImmediate(waterRoot.GetChild(i).gameObject);

                var fx = waterRoot.GetComponent<CaveWaterFxPlayer>();
                if (fx != null)
                    CaveEditorUndo.DestroyImmediate(fx);
            }

            var branch = caveRoot.Find("Water/WaterBranchTube");
            if (branch == null)
                branch = caveRoot.Find("SplineMesh")?.Find("WaterBranchTube");
            if (branch != null)
                CaveEditorUndo.DestroyImmediate(branch.gameObject);

            var anchor = caveRoot.GetComponent<CaveWaterBranchAnchor>();
            if (anchor != null)
                CaveEditorUndo.DestroyImmediate(anchor);

            foreach (var pool in caveRoot.GetComponentsInChildren<CaveUndergroundWaterPool>(true))
            {
                if (pool != null)
                    CaveEditorUndo.DestroyImmediate(pool.gameObject);
            }
        }
    }
}
