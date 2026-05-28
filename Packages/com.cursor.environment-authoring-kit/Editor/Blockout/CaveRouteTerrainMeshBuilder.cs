using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Backward-compatible aliases — use <see cref="CaveEnclosureShellBuilder"/> for floor/ceiling.</summary>
    public static class CaveRouteTerrainMeshBuilder
    {
        public const string FloorRootName = CaveEnclosureShellBuilder.FloorRootName;
        public const string CeilingRootName = CaveEnclosureShellBuilder.CeilingRootName;

        public static int Build(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material floorMat,
            Material rockMat,
            int seed) =>
            CaveEnclosureShellBuilder.BuildFloorOnly(geometryRoot, layout, floorMat, seed);

        public static void DestroyCeilingRibbon(Transform geometryRoot) =>
            CaveEnclosureShellBuilder.DestroyCeiling(geometryRoot);
    }
}
