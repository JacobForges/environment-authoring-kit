#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class CaveThirdPersonLayoutUtility
    {
        public static void ApplyToLayout(CaveMazeLayout layout)
        {
            if (layout == null)
                return;

            layout.CorridorHeight = Mathf.Max(
                layout.CorridorHeight,
                CaveThirdPersonClearance.ResolveDefaultCorridorHeight());
            layout.CavernRadiusCells = Mathf.Max(layout.CavernRadiusCells, 2);
            layout.SyncCeilingClearanceFromCorridor();
        }

        public static CaveMazeLayout GenerateForCave(CaveBuildMetadata meta, bool layoutPrototype)
        {
            if (meta == null)
                return null;

            var layout = layoutPrototype
                ? CaveMazeLayoutGenerator.GeneratePrototype(meta.seed, meta.tunnelSegments, meta.chamberCount)
                : CaveMazeLayoutGenerator.Generate(
                    meta.seed,
                    meta.tunnelSegments,
                    meta.chamberCount,
                    meta.mazeGenFlavor);

            ApplyToLayout(layout);
            return layout;
        }
    }
}
#endif
