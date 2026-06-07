using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Ensures saved caves are not an invisible void: interior tube mesh on, block culling off until configured.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveTunnelVisibilityBootstrap : MonoBehaviour
    {
        [Tooltip("Re-enable interior tube meshes that older builds disabled when block shells were added.")]
        public bool enableInteriorTubeMeshes = true;

        [Tooltip("Disable distance culling on load so blocks stay visible away from the surface spawn.")]
        public bool disableDistanceCullingOnLoad = true;

        void Awake()
        {
            var meta = GetComponent<CaveBuildMetadata>();
            if (meta != null && meta.adventureHybrid)
                enableInteriorTubeMeshes = false;

            if (enableInteriorTubeMeshes)
                EnableInteriorMeshes();

            if (disableDistanceCullingOnLoad)
            {
                var culler = GetComponent<CaveBlockTunnelCuller>();
                if (culler != null)
                {
                    culler.distanceCullingEnabled = false;
                    culler.RestoreAllBlocks();
                }
            }

            TightenAtmosphereZone();
            foreach (var atmosphere in GetComponentsInChildren<CaveUndergroundAtmosphere>(true))
                atmosphere.ResetSurfaceLookIfUnoccupied();
        }

        void TightenAtmosphereZone()
        {
            var zone = transform.Find("CaveAtmosphereZone");
            var geometry = transform.Find("CaveGeometry");
            if (zone == null || geometry == null)
                return;

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            foreach (var r in geometry.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer)
                    continue;

                var b = r.bounds;
                min = Vector3.Min(min, transform.InverseTransformPoint(b.min));
                max = Vector3.Max(max, transform.InverseTransformPoint(b.max));
            }

            if (float.IsPositiveInfinity(min.x))
                return;

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            zone.localPosition = bounds.center;
            var box = zone.GetComponent<BoxCollider>();
            if (box == null)
                box = zone.gameObject.AddComponent<BoxCollider>();

            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = bounds.size + new Vector3(6f, 8f, 6f);
        }

        void EnableInteriorMeshes()
        {
            var meshRoot = transform.Find("SplineMesh");
            if (meshRoot != null)
            {
                foreach (var mr in meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr != null)
                        mr.enabled = true;
                }
            }

            var water = transform.Find("Water");
            var branch = water != null ? water.Find("WaterBranchTube") : null;
            if (branch != null)
            {
                var mr = branch.GetComponent<MeshRenderer>();
                if (mr != null)
                    mr.enabled = true;
            }
        }
    }
}
