#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Hollow Titan exterior — terraformed hollow stump bowl (see <see cref="HollowTitanExteriorTerraform"/>).
    /// Prop-based log/branch dressing removed per concept art (dead hollow karst stump).
    /// </summary>
    static class HollowTitanExteriorDressing
    {
        public static int BuildHyperRealTrunkShell(
            Transform shellRoot,
            float trunkRadius,
            float trunkHeight,
            float trunkYaw,
            System.Random rng,
            bool massive) =>
            0;

        public static int ScatterDeadBranches(
            Transform branchRoot,
            float trunkRadius,
            float trunkHeight,
            float trunkYaw,
            System.Random rng,
            bool massive) =>
            0;

        public static bool TryPlaceEntranceArch(
            Transform frameRoot,
            Vector3 localPos,
            Vector3 forward,
            float scale) =>
            false;
    }
}
#endif
