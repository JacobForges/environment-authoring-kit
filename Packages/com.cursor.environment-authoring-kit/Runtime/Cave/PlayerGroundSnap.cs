using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Snaps a character to solid walkable ground below a point (maze floors, walkways — not outer shell).
    /// </summary>
    public static class PlayerGroundSnap
    {
        public static bool TryGetGroundPosition(Vector3 near, CharacterController controller, out Vector3 grounded)
        {
            grounded = near;
            const float probeUp = 6f;
            const float probeDown = 48f;
            var origin = near + Vector3.up * probeUp;

            var hits = Physics.RaycastAll(origin, Vector3.down, probeUp + probeDown, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            RaycastHit? best = null;
            var bestScore = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.collider == null)
                    continue;

                if (!CaveWalkableSurface.IsWalkableCollider(hit.collider) &&
                    !IsLikelyCaveFloorCollider(hit.collider))
                    continue;

                if (hit.point.y > near.y + 2.5f)
                    continue;

                var horizontal = new Vector2(hit.point.x - near.x, hit.point.z - near.z);
                var score = horizontal.sqrMagnitude + Mathf.Abs(hit.point.y - near.y) * 4f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = hit;
            }

            if (!best.HasValue)
                return false;

            var surface = best.Value.point;
            if (controller != null)
            {
                grounded = new Vector3(
                    near.x,
                    surface.y + controller.height * 0.5f + controller.skinWidth + 0.04f,
                    near.z);
            }
            else
                grounded = surface + Vector3.up * 0.2f;

            return true;
        }

        public static bool SnapTransform(Transform root, Vector3 near)
        {
            if (root == null)
                return false;

            var controller = root.GetComponent<CharacterController>();
            var wasEnabled = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;

            var snapped = TrySnapToNearbySpawnPad(root, near, controller, out var grounded);
            if (!snapped)
                snapped = TryGetGroundPosition(near, controller, out grounded);

            if (snapped)
                root.position = grounded;
            else
                root.position = near;

            if (controller != null)
            {
                Physics.SyncTransforms();
                controller.enabled = wasEnabled;
            }

            if (!snapped && controller != null)
            {
                var nudge = near + Vector3.up * 0.35f;
                root.position = nudge;
                Physics.SyncTransforms();
            }

            return snapped;
        }

        /// <summary>Prefer SpawnGroundPad under the cave entrance — raycasts often miss at deep Y.</summary>
        static bool TrySnapToNearbySpawnPad(
            Transform root,
            Vector3 near,
            CharacterController controller,
            out Vector3 grounded)
        {
            grounded = near;
            Transform pad = null;
            var spawn = CaveEntranceTeleport.ResolveSpawnPoint();
            if (spawn != null)
            {
                pad = spawn.Find(CaveSpawnPadUtility.PadName);
                if (pad != null && Vector3.Distance(pad.position, near) > 80f)
                    pad = null;
            }

            if (pad == null)
            {
                foreach (var col in Physics.OverlapSphere(near, 12f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (col == null || col.gameObject.name != CaveSpawnPadUtility.PadName)
                        continue;
                    pad = col.transform;
                    break;
                }
            }

            if (pad == null)
                return false;

            var top = pad.position.y + pad.lossyScale.y * 0.5f;
            if (controller != null)
            {
                grounded = new Vector3(
                    near.x,
                    top + controller.height * 0.5f + controller.skinWidth + 0.06f,
                    near.z);
            }
            else
                grounded = new Vector3(near.x, top + 0.2f, near.z);

            return true;
        }

        static bool IsLikelyCaveFloorCollider(Collider collider)
        {
            if (collider == null || collider.isTrigger)
                return false;

            var n = collider.gameObject.name;
            if (n.Contains("Ceiling") || n.StartsWith("CaveBlock_") || n.StartsWith("Outer_"))
                return false;

            var t = collider.transform;
            while (t != null)
            {
                if (t.name == CaveGeometryPaths.AdventureShell || t.name == CaveGeometryPaths.GeometryRoot)
                    return n.Contains("Floor") || n.Contains("Entrance_Floor") || n.Contains("SpawnGroundPad") ||
                           n.StartsWith(CaveWalkableSurface.CaveWalkFloorPrefix);
                if (t.name == CaveEntranceTeleport.CaveSystemObjectName)
                    return n.Contains("SpawnGroundPad") || n.Contains("Entrance_Floor");
                t = t.parent;
            }

            return false;
        }
    }
}
