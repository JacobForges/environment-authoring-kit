using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Editor-only capsule preview on mob spawners; hidden during play.</summary>
    public sealed class CaveSpawnEditorMarker : MonoBehaviour
    {
        void Awake()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = false;
        }
    }
}
