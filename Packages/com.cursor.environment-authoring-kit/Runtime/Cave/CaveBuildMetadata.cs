using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Last cave build parameters — used to rebuild maze walkways after remediation.</summary>
    public sealed class CaveBuildMetadata : MonoBehaviour
    {
        public int seed = 1;
        public int tunnelSegments = 8;
        public int chamberCount = 4;
        public bool adventureHybrid;
        public float cellSizeMeters = 3f;
        [Tooltip("When set, build/meat passes may only adjust root Y (mouth snap), not world XZ.")]
        public bool preserveRootWorldXZ;
        public Vector3 lockedRootWorldPosition;
        public string buildVisualStyle;
        public int mazeGenFlavor;

        public void Set(int buildSeed, int segments, int chambers, bool hybrid, float cellSize = 3f)
        {
            seed = buildSeed;
            tunnelSegments = segments;
            chamberCount = chambers;
            adventureHybrid = hybrid;
            cellSizeMeters = cellSize > 0.5f ? cellSize : 3f;
        }

        public void LockRootWorldXZ(Transform caveRoot)
        {
            if (caveRoot == null)
                return;
            if (!preserveRootWorldXZ)
                lockedRootWorldPosition = caveRoot.position;
            preserveRootWorldXZ = true;
        }

        public static bool ShouldPreserveRootXZ(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            return meta != null && meta.preserveRootWorldXZ;
        }
    }
}
