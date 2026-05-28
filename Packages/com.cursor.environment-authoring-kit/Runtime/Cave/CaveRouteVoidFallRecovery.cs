using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// When the player is inside the cave system and falls far below the route, respawn on the surface main area.
    /// Catches missed pit triggers and stray invisible floor colliders after they are removed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveRouteVoidFallRecovery : MonoBehaviour
    {
        public float fallBelowRouteMeters = 5f;
        public float checkInterval = 0.25f;
        public float cooldownSeconds = 2.5f;

        float _nextCheck;
        float _lastRecovery;
        float _routeFloorLocalY = float.NaN;

        void Start() => CacheRouteFloor();

        void Update()
        {
            if (Time.time < _nextCheck)
                return;

            _nextCheck = Time.time + checkInterval;
            if (float.IsNaN(_routeFloorLocalY))
                CacheRouteFloor();

            var player = ResolvePlayer();
            if (player == null || Time.time - _lastRecovery < cooldownSeconds)
                return;

            var local = transform.InverseTransformPoint(player.position);
            var horizontal = new Vector2(local.x, local.z).magnitude;
            if (horizontal > 120f)
                return;

            if (local.y > _routeFloorLocalY - fallBelowRouteMeters)
                return;

            _lastRecovery = Time.time;
            if (CaveMainAreaRespawn.TryRespawnPlayer(player, "Void fall below route"))
                return;

            Debug.LogWarning("[CaveRouteVoidFallRecovery] No surface spawn found for void fall.", this);
        }

        void CacheRouteFloor()
        {
            var geometry = transform.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
            {
                _routeFloorLocalY = -4f;
                return;
            }

            var floor = geometry.Find(CaveGeometryPaths.RouteTerrainFloorName);
            if (floor != null)
            {
                _routeFloorLocalY = floor.localPosition.y;
                return;
            }

            var minY = float.PositiveInfinity;
            foreach (var marker in geometry.GetComponentsInChildren<CaveWalkableMarker>(true))
            {
                if (marker == null)
                    continue;
                minY = Mathf.Min(minY, marker.transform.localPosition.y);
            }

            _routeFloorLocalY = float.IsPositiveInfinity(minY) ? -4f : minY;
        }

        static Transform ResolvePlayer()
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
                return tagged.transform;

            var controller = Object.FindAnyObjectByType<CharacterController>();
            return controller != null ? controller.transform : null;
        }
    }
}
