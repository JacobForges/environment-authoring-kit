using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    public struct CaveSplineSample
    {
        public Vector3 Position;
        public Vector3 Tangent;
        public Vector3 Right;
        public Vector3 Up;
        public float RadiusX;
        public float RadiusY;
        public float Distance;
        public float Normalized;
    }

    /// <summary>Catmull-Rom spline through path knots with per-knot elliptical radius.</summary>
    public sealed class CaveSplinePath
    {
        readonly List<Vector3> _points = new();
        readonly List<float> _radiusX = new();
        readonly List<float> _radiusY = new();

        float[] _segmentLengths;
        float _totalLength;

        public float TotalLength => _totalLength;
        public int KnotCount => _points.Count;
        public IReadOnlyList<Vector3> Points => _points;

        public void SetKnots(IReadOnlyList<CavePathKnot> knots)
        {
            _points.Clear();
            _radiusX.Clear();
            _radiusY.Clear();

            if (knots == null || knots.Count == 0)
                return;

            foreach (var k in knots)
            {
                _points.Add(k.Position);
                _radiusX.Add(k.RadiusX);
                _radiusY.Add(k.RadiusY);
            }

            RebuildArcLength();
        }

        public CaveSplineSample SampleAtDistance(float distance)
        {
            var t = _totalLength > 0.0001f ? Mathf.Clamp01(distance / _totalLength) : 0f;
            return SampleAtNormalized(t, distance);
        }

        public CaveSplineSample SampleAtNormalized(float t, float distanceOverride = -1f)
        {
            t = Mathf.Clamp01(t);
            var n = _points.Count;
            if (n == 0)
                return default;

            if (n == 1)
            {
                return new CaveSplineSample
                {
                    Position = _points[0],
                    Tangent = Vector3.forward,
                    Right = Vector3.right,
                    Up = Vector3.up,
                    RadiusX = _radiusX[0],
                    RadiusY = _radiusY[0],
                    Distance = 0f,
                    Normalized = t
                };
            }

            var targetLen = t * _totalLength;
            var segIndex = 0;
            while (segIndex < _segmentLengths.Length && targetLen > _segmentLengths[segIndex])
                targetLen -= _segmentLengths[segIndex++];

            segIndex = Mathf.Clamp(segIndex, 0, Mathf.Max(0, n - 2));
            var segT = _segmentLengths.Length > 0 && _segmentLengths[segIndex] > 0.0001f
                ? targetLen / _segmentLengths[segIndex]
                : 0f;
            segT = Mathf.Clamp01(segT);

            var globalT = (segIndex + segT) / (n - 1);
            var pos = EvaluatePosition(segIndex, segT);
            var tan = EvaluateTangent(segIndex, segT).normalized;
            if (tan.sqrMagnitude < 0.0001f)
                tan = Vector3.forward;

            var up = Vector3.up;
            var right = Vector3.Cross(up, tan).normalized;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            up = Vector3.Cross(tan, right).normalized;

            var rx = Mathf.Lerp(_radiusX[segIndex], _radiusX[Mathf.Min(segIndex + 1, n - 1)], segT);
            var ry = Mathf.Lerp(_radiusY[segIndex], _radiusY[Mathf.Min(segIndex + 1, n - 1)], segT);
            var dist = distanceOverride >= 0f ? distanceOverride : t * _totalLength;

            return new CaveSplineSample
            {
                Position = pos,
                Tangent = tan,
                Right = right,
                Up = up,
                RadiusX = rx,
                RadiusY = ry,
                Distance = dist,
                Normalized = globalT
            };
        }

        void RebuildArcLength()
        {
            var n = _points.Count;
            if (n < 2)
            {
                _segmentLengths = System.Array.Empty<float>();
                _totalLength = 0f;
                return;
            }

            _segmentLengths = new float[n - 1];
            _totalLength = 0f;
            const int steps = 12;
            for (var seg = 0; seg < n - 1; seg++)
            {
                var len = 0f;
                var prev = EvaluatePosition(seg, 0f);
                for (var s = 1; s <= steps; s++)
                {
                    var p = EvaluatePosition(seg, s / (float)steps);
                    len += Vector3.Distance(prev, p);
                    prev = p;
                }

                _segmentLengths[seg] = len;
                _totalLength += len;
            }
        }

        Vector3 EvaluatePosition(int segIndex, float segT)
        {
            var n = _points.Count;
            var i0 = Mathf.Max(segIndex - 1, 0);
            var i1 = segIndex;
            var i2 = Mathf.Min(segIndex + 1, n - 1);
            var i3 = Mathf.Min(segIndex + 2, n - 1);
            return CatmullRom(_points[i0], _points[i1], _points[i2], _points[i3], segT);
        }

        Vector3 EvaluateTangent(int segIndex, float segT)
        {
            const float dt = 0.01f;
            var t0 = Mathf.Clamp01(segT - dt);
            var t1 = Mathf.Clamp01(segT + dt);
            return EvaluatePosition(segIndex, t1) - EvaluatePosition(segIndex, t0);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
