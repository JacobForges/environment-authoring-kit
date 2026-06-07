using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Marks procedural maze volume geometry — colliders here must never be stripped by cleanup passes.</summary>
    [DisallowMultipleComponent]
    public sealed class CaveMazeVolumeMarker : MonoBehaviour
    {
    }
}
