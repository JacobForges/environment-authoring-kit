using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Aligns underground cave entrance to the nearest surface opening marker when building cave-only from surface.</summary>
    public static class SurfaceCaveOpeningAligner
    {
        public static bool TryAlignCaveRootToOpening(Transform caveRoot, SceneGroundInfo ground, int preferredSector = -1)
        {
            if (caveRoot == null)
                return false;

            var markers = SurfaceWorldGenerator.FindCaveOpenings();
            if (markers.Count == 0)
                return false;

            SurfaceCaveOpeningMarker pick = null;
            if (preferredSector >= 0)
            {
                foreach (var m in markers)
                {
                    if (m != null && m.sectorIndex == preferredSector)
                    {
                        pick = m;
                        break;
                    }
                }
            }

            if (pick == null)
            {
                var portal = CaveBuildPortalSettings.PortalForBuild;
                if (portal != null)
                {
                    var bestDist = float.MaxValue;
                    foreach (var marker in markers)
                    {
                        if (marker == null)
                            continue;
                        var dist = (marker.transform.position - portal.transform.position).sqrMagnitude;
                        if (dist >= bestDist)
                            continue;
                        bestDist = dist;
                        pick = marker;
                    }
                }
            }

            pick ??= markers[0];
            if (pick == null)
                return false;

            // If the cave root already has a locked build-site XZ (meat-loop / Cursor ladder),
            // preserve it to avoid lateral drift during a "depth-only" mouth grounding pass.
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var preserveRootXz = meta != null && meta.preserveRootWorldXZ;
            if (preserveRootXz && meta != null)
            {
                var lockPos = meta.lockedRootWorldPosition;
                var distToPick = Vector2.Distance(
                    new Vector2(lockPos.x, lockPos.z),
                    new Vector2(pick.transform.position.x, pick.transform.position.z));
                if (distToPick > 6f)
                    preserveRootXz = false;
            }
            var targetX = preserveRootXz ? meta.lockedRootWorldPosition.x : pick.transform.position.x;
            var targetZ = preserveRootXz ? meta.lockedRootWorldPosition.z : pick.transform.position.z;
            // unity6-terrain-heightmap / fl-bay-hillshade: mouth on opening lip, root = surface − mouth offset.
            // When XZ is locked, sample the walkable lip at locked XZ to avoid marker-Y/target-XZ mismatch.
            var mouthOffset = CaveGroundPlacementUtility.ResolveMouthOffsetForExpectedPlacement(caveRoot);
            var openingSurface = new Vector3(targetX, pick.transform.position.y, targetZ);
            if (ground != null && ground.HasAnchor)
            {
                openingSurface.y = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, openingSurface);
            }

            var targetRoot = new Vector3(
                targetX,
                openingSurface.y - mouthOffset,
                targetZ);
            caveRoot.position = targetRoot;
            caveRoot.rotation = Quaternion.LookRotation(-pick.transform.forward, Vector3.up);

            if (meta == null)
                meta = caveRoot.gameObject.AddComponent<CaveBuildMetadata>();
            meta.LockRootWorldXZ(caveRoot);

            if (ground != null && ground.HasAnchor)
                CaveGroundPlacementUtility.FinalizeGroundPlacement(caveRoot, ground, out _, seed: meta != null ? meta.seed : 0);

            Debug.Log(
                $"[CaveBuild] Aligned cave to surface opening '{pick.name}' sector {pick.sectorIndex} " +
                $"@ {pick.transform.position}." +
                (CaveBuildPortalSettings.PortalForBuild != null
                    ? $" Portal='{CaveBuildPortalSettings.PortalForBuild.name}'."
                    : string.Empty));
            return true;
        }
    }
}
