using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Resolves spawn/feet height on terrain and planner walk meshes (slightly above surface).</summary>
    public static class PlayerSpawnHeightUtility
    {
        public const float FeetClearanceM = 0.12f;
        public const float DefaultDropHeightM = 5.5f;

        public static float PivotYForSurface(float surfaceY, CharacterController controller)
        {
            if (controller == null)
                return surfaceY + FeetClearanceM;

            var soleOffset = controller.center.y - controller.height * 0.5f;
            return surfaceY + FeetClearanceM + controller.skinWidth - soleOffset;
        }

        public static Vector3 ResolvePivotPosition(Vector3 near, CharacterController controller)
        {
            if (TrySampleWalkableSurfaceY(near, out var surfaceY))
                return new Vector3(near.x, PivotYForSurface(surfaceY, controller), near.z);

            return near + Vector3.up * (controller != null ? FeetClearanceM + controller.height * 0.5f : FeetClearanceM);
        }

        public static Vector3 ResolveDropSpawnPosition(Vector3 near, CharacterController controller, float dropM = DefaultDropHeightM)
        {
            var grounded = ResolvePivotPosition(near, controller);
            grounded.y += Mathf.Max(2f, dropM);
            return grounded;
        }

        public static Vector3 ResolveSpawnMarkerPosition(Vector3 markerPosition, CharacterController controller = null)
        {
            var pivot = ResolvePivotPosition(markerPosition, controller);
            if (controller == null)
                return pivot;

            Physics.SyncTransforms();
            return pivot;
        }

        public static bool TrySampleWalkableSurfaceY(Vector3 world, out float surfaceY)
        {
            surfaceY = world.y;

            if (TrySampleTerrain(world, out var terrainY))
            {
                surfaceY = terrainY;
                return true;
            }

            const float probeUp = 16f;
            const float probeDown = 72f;
            const float maxHoriz = 2.75f;
            const float maxAbove = 4f;
            var origin = world + Vector3.up * probeUp;
            var hits = Physics.RaycastAll(origin, Vector3.down, probeUp + probeDown, ~0, QueryTriggerInteraction.Ignore);

            var bestY = float.MinValue;
            var found = false;
            if (hits != null)
            {
                foreach (var hit in hits)
                {
                    if (!IsSpawnSurfaceHit(hit))
                        continue;

                    var horizontal = new Vector2(hit.point.x - world.x, hit.point.z - world.z);
                    if (horizontal.sqrMagnitude > maxHoriz * maxHoriz)
                        continue;

                    if (hit.point.y > world.y + maxAbove)
                        continue;

                    if (hit.point.y <= bestY)
                        continue;

                    bestY = hit.point.y;
                    found = true;
                }
            }

            if (found)
            {
                surfaceY = bestY;
                return true;
            }

            return false;
        }

        static bool IsSpawnSurfaceHit(RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.isTrigger)
                return false;

            if (hit.normal.y < 0.45f)
                return false;

            if (IsWalkableForSpawn(hit.collider))
                return true;

            if (CaveWalkableSurface.IsWalkableCollider(hit.collider))
                return true;

            if (hit.normal.y < 0.65f)
                return false;

            var n = hit.collider.gameObject.name;
            if (n.Contains("Ceiling", System.StringComparison.Ordinal)
                || n.StartsWith("Outer_", System.StringComparison.Ordinal)
                || n.Contains("Wall", System.StringComparison.Ordinal))
                return false;

            return hit.collider is MeshCollider or TerrainCollider or BoxCollider;
        }

        static bool TrySampleTerrain(Vector3 world, out float surfaceY)
        {
            surfaceY = world.y;
            var terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
                return false;

            foreach (var terrain in terrains)
            {
                if (terrain?.terrainData == null)
                    continue;

                var pos = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (world.x < pos.x || world.z < pos.z || world.x > pos.x + size.x || world.z > pos.z + size.z)
                    continue;

                surfaceY = terrain.SampleHeight(world) + pos.y;
                return true;
            }

            return false;
        }

        public static bool IsWalkableForSpawn(Collider collider)
        {
            if (collider == null || collider.isTrigger)
                return false;

            if (collider is TerrainCollider)
                return true;

            if (CaveWalkableSurface.IsWalkableCollider(collider))
                return true;

            var n = collider.gameObject.name;
            return n.StartsWith("Walk", System.StringComparison.Ordinal) ||
                   n.Contains("HopPad", System.StringComparison.Ordinal) ||
                   n.Contains("BridgeRamp", System.StringComparison.Ordinal) ||
                   n.Contains("BridgePad", System.StringComparison.Ordinal);
        }
    }
}
