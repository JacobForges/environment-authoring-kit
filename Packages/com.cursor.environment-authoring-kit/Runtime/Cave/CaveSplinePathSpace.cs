using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Converts cave spline knots (stored in <see cref="CaveSplinePathAuthoring"/> local space) to world space.</summary>
    public static class CaveSplinePathSpace
    {
        public static CaveSplinePath CreateLocalSpline(CaveSplinePathAuthoring authoring)
        {
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return null;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            return spline;
        }

        public static CaveSplinePath CreateWorldSpline(CaveSplinePathAuthoring authoring)
        {
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return null;

            var t = authoring.transform;
            var worldKnots = new List<CavePathKnot>(authoring.Knots.Count);
            foreach (var k in authoring.Knots)
            {
                worldKnots.Add(new CavePathKnot(
                    t.TransformPoint(k.Position),
                    k.RadiusX,
                    k.RadiusY,
                    k.IsChamber));
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(worldKnots);
            return spline;
        }
    }
}
