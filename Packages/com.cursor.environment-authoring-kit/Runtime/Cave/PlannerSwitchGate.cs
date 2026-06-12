using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Hub maze switch gate — toggles collider blocking when player enters trigger.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PlannerSwitchGate : MonoBehaviour
    {
        public bool gateOpen;
        public GameObject blockingVolume;

        void Reset()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            gateOpen = !gateOpen;
            if (blockingVolume != null)
                blockingVolume.SetActive(!gateOpen);
        }
    }
}
