using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Stores the built spline for gizmos / future hand-editing.</summary>
    public class CaveSplinePathAuthoring : MonoBehaviour
    {
        [SerializeField] List<CavePathKnot> knots = new();
        [SerializeField] float totalLength;

        public IReadOnlyList<CavePathKnot> Knots => knots;
        public float TotalLength => totalLength;

        public void SetPath(IReadOnlyList<CavePathKnot> pathKnots, float length)
        {
            knots.Clear();
            if (pathKnots != null)
                knots.AddRange(pathKnots);
            totalLength = length;
        }

        void OnDrawGizmosSelected()
        {
            if (knots == null || knots.Count < 2)
                return;

            var spline = new CaveSplinePath();
            spline.SetKnots(knots);

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            var steps = Mathf.Max(16, Mathf.CeilToInt(totalLength / 2f));
            var prev = spline.SampleAtDistance(0f).Position;
            for (var i = 1; i <= steps; i++)
            {
                var s = spline.SampleAtDistance((i / (float)steps) * totalLength);
                Gizmos.DrawLine(transform.TransformPoint(prev), transform.TransformPoint(s.Position));
                prev = s.Position;
            }

            Gizmos.color = Color.yellow;
            foreach (var k in knots)
            {
                var p = transform.TransformPoint(k.Position);
                Gizmos.DrawWireSphere(p, Mathf.Max(k.RadiusX, k.RadiusY) * 0.35f);
            }
        }
    }
}
