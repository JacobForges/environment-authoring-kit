using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    public enum CaveLayoutMarkerKind
    {
        Path,
        Start,
        Finish,
        JumpGap,
        Landmark,
        MobZone
    }

    /// <summary>Gizmo-friendly marker for layout-only cave prototypes (no art geometry).</summary>
    public sealed class CaveLayoutMarker : MonoBehaviour
    {
        public CaveLayoutMarkerKind kind = CaveLayoutMarkerKind.Path;

        void OnDrawGizmos()
        {
            var color = kind switch
            {
                CaveLayoutMarkerKind.Start => Color.green,
                CaveLayoutMarkerKind.Finish => Color.cyan,
                CaveLayoutMarkerKind.JumpGap => new Color(1f, 0.35f, 0.1f),
                CaveLayoutMarkerKind.Landmark => new Color(0.85f, 0.55f, 1f),
                CaveLayoutMarkerKind.MobZone => Color.red,
                _ => Color.yellow
            };

            Gizmos.color = color;
            Gizmos.DrawSphere(transform.position, 0.45f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
        }
    }
}
