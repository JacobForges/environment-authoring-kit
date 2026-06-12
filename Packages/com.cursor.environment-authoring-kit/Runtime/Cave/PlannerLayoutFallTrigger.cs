using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Kill volume under planner platform gaps — teleports the player to the surface spawn.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PlannerLayoutFallTrigger : MonoBehaviour
    {
        public float cooldownSeconds = 1.5f;
        float _lastTrigger;

        void Reset()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            if (Time.time - _lastTrigger < cooldownSeconds)
                return;

            _lastTrigger = Time.time;
            CaveMainAreaRespawn.TryRespawnPlayer(other.transform, "Planner fall volume");
        }
    }
}
