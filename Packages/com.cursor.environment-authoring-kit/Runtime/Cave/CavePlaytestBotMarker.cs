using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Marks playtest bot colliders — used instead of project tags when CavePlaytestBot tag is undefined.</summary>
    public sealed class CavePlaytestBotMarker : MonoBehaviour
    {
        public static bool IsBotCollider(Collider col) =>
            col != null && col.GetComponentInParent<CavePlaytestBotMarker>() != null;
    }
}
