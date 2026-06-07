using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Post-build polish: visible block walls, no floating sky slabs, warm torch read.</summary>
    public static class CaveAdventureVisualPass
    {
        public static int Apply(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var n = CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
            // unity6-mesh-data-procedural — post-build uses grading shell (RouteTerrain strips + capped minables).
            n += RestoreBlockWallsForGrading(caveRoot);
            n += CaveCompactRouteUtility.TrimBlocksToGradingBudget(caveRoot);
            n += CaveInvisibleColliderUtility.StripForAdventure(caveRoot);
            n += BoostPathTorches(caveRoot);
            return n;
        }

        /// <summary>Strip duplicate horizontal slabs (walkways, ceiling cover, legacy per-cell ceilings, sky seal).</summary>
        public static int StripLayeredGeometry(Transform caveRoot) =>
            CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);

        /// <summary>Grading/compact-route hook: restore minable walls without full <see cref="Apply"/>.</summary>
        public static int RestoreBlockWallsForGrading(Transform caveRoot) =>
            RestoreBlockWalls(caveRoot, forGrading: true);

        /// <summary>Re-enable block tunnel renderers after purge/cull (grading and compact-route rebuild).</summary>
        public static int RestoreBlockWalls(Transform caveRoot, bool forGrading = false)
        {
            var n = 0;
            var culler = caveRoot.GetComponent<CaveBlockTunnelCuller>();
            if (culler != null)
                culler.distanceCullingEnabled = false;

            // unity6-mesh-data-procedural — grading keeps RouteTerrain + capped minables, not every block renderer.
            if (forGrading)
            {
                CavePerformanceBudget.DisableSplineSubtreeRenderers(caveRoot);
                n += CavePerformanceBudget.DisableNonMinableShellBlockRenderers(caveRoot);
                n += CavePerformanceBudget.ApplyMinableVisibilityBudget(caveRoot, 24);
                n += CavePerformanceBudget.EnsureGradingTriangleBudget(caveRoot);
            }
            else if (culler != null)
            {
                culler.RestoreAllBlocks();
                n++;
            }

            // NVIDIA 3D-GENERALIST 2026 — restore walls only under the canonical BlockTunnel (not legacy root shells).
            if (!forGrading)
            {
                var tunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
                if (tunnel != null)
                {
                    foreach (var mr in tunnel.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (mr == null || !mr.gameObject.name.StartsWith("CaveBlock_Minable"))
                            continue;
                        mr.enabled = true;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        mr.receiveShadows = true;
                        n++;
                    }
                }
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry != null)
            {
                var floor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
                var ceil = geometry.Find(CaveEnclosureShellBuilder.CeilingRootName);
                if (floor != null)
                {
                    var fmr = floor.GetComponent<MeshRenderer>();
                    if (fmr != null)
                        fmr.enabled = true;
                }

                if (ceil != null)
                {
                    var cmr = ceil.GetComponent<MeshRenderer>();
                    if (cmr != null)
                        cmr.enabled = true;
                }
            }

            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);

            return n;
        }

        static int BoostPathTorches(Transform caveRoot)
        {
            var count = 0;
            foreach (var light in caveRoot.GetComponentsInChildren<Light>(true))
            {
                if (light == null || !light.gameObject.name.Contains("Torch"))
                    continue;

                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.28f);
                light.intensity = Mathf.Max(light.intensity, 5.5f);
                light.range = Mathf.Max(light.range, 18f);
                light.shadows = LightShadows.Soft;
                count++;
            }

            return count;
        }
    }
}
