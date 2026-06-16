using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Snap content-layout anchors to walkable ground (terrain + colliders).</summary>
    public static class ContentLayoutGroundSnap
    {
        public const float NpcFeetClearanceM = 0.08f;
        public const float PropBaseClearanceM = 0.02f;
        public const float SpawnerClearanceM = 0.05f;

        public static Vector3 SnapWorldPosition(Vector3 world, float clearanceM)
        {
            if (TrySampleContentGroundY(world, out var surfaceY))
                return new Vector3(world.x, surfaceY + clearanceM, world.z);
            return world;
        }

        /// <summary>Keep brief Y as meters above sampled ground (pennants, arches).</summary>
        public static Vector3 SnapWorldPositionWithHeightOffset(Vector3 world, float clearanceM, float heightAboveGroundM)
        {
            if (TrySampleContentGroundY(world, out var surfaceY))
            {
                var y = heightAboveGroundM >= 1f
                    ? surfaceY + heightAboveGroundM
                    : surfaceY + clearanceM;
                return new Vector3(world.x, y, world.z);
            }
            return world;
        }

        public static bool TrySampleContentGroundY(Vector3 world, out float surfaceY)
        {
            surfaceY = world.y;

            const float probeUp = 24f;
            const float probeDown = 96f;
            var origin = world + Vector3.up * probeUp;
            var hits = Physics.RaycastAll(origin, Vector3.down, probeUp + probeDown, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            var bestY = float.MinValue;
            var found = false;
            foreach (var hit in hits)
            {
                if (!IsContentGroundHit(hit))
                    continue;

                if (hit.point.y <= bestY)
                    continue;

                bestY = hit.point.y;
                found = true;
            }

            if (!found)
                return false;

            surfaceY = bestY;
            return true;
        }

        static bool IsContentGroundHit(RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.isTrigger)
                return false;
            if (hit.normal.y < 0.35f)
                return false;

            var go = hit.collider.gameObject;
            if (go == null)
                return false;

            if (hit.collider is TerrainCollider)
                return true;

            var n = go.name;
            if (n.Contains("ContentLayout", System.StringComparison.Ordinal)
                || n.StartsWith("npc_", System.StringComparison.OrdinalIgnoreCase)
                || n.Contains("DTA_Floating", System.StringComparison.Ordinal))
                return false;

            if (n.Contains("Ceiling", System.StringComparison.Ordinal)
                || n.Contains("Wall", System.StringComparison.Ordinal)
                || n.StartsWith("Outer_", System.StringComparison.Ordinal)
                || n.Contains("Roof", System.StringComparison.Ordinal)
                || n.Contains("House", System.StringComparison.Ordinal)
                || n.Contains("Building", System.StringComparison.Ordinal)
                || n.Contains("Facade", System.StringComparison.Ordinal)
                || n.Equals("default", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (n.Contains("Road", System.StringComparison.Ordinal)
                || n.Contains("Street", System.StringComparison.Ordinal)
                || n.Contains("Sidewalk", System.StringComparison.Ordinal)
                || n.Contains("Asphalt", System.StringComparison.Ordinal)
                || n.Contains("Lane", System.StringComparison.Ordinal)
                || n.Contains("Grass", System.StringComparison.Ordinal)
                || n.Contains("SurfaceTerrain", System.StringComparison.Ordinal)
                || n.Equals("Ground", System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (n.Contains("Polyvania", System.StringComparison.Ordinal))
                return hit.collider is TerrainCollider;

            return PlayerSpawnHeightUtility.IsWalkableForSpawn(hit.collider)
                   && hit.collider is TerrainCollider or BoxCollider;
        }

        public static void SnapTransform(Transform root, float clearanceM)
        {
            if (root == null)
                return;
            root.position = SnapWorldPosition(root.position, clearanceM);
        }

        /// <summary>After a visual prefab is parented, shift so renderer bounds min Y sits on ground.</summary>
        public static void AlignVisualFeetToGround(Transform root, float clearanceM)
        {
            if (root == null)
                return;

            var world = root.position;
            if (!TrySampleContentGroundY(world, out var surfaceY))
                return;

            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                SnapTransform(root, clearanceM);
                return;
            }

            var minY = float.MaxValue;
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                if (r.transform.name == "Label" || r.GetComponent<TextMesh>() != null)
                    continue;
                minY = Mathf.Min(minY, r.bounds.min.y);
            }

            if (minY >= float.MaxValue - 1f)
            {
                SnapTransform(root, clearanceM);
                return;
            }

            var lift = surfaceY + clearanceM - minY;
            root.position += Vector3.up * lift;
        }
    }
}
