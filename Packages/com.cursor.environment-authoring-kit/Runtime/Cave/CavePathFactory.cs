using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Builds a strictly descending, twisting path underground (never climbs, never stacks horizontally).
    /// </summary>
    public static class CavePathFactory
    {
        public const float CharacterHeightRef = 2.5f;
        public const float MinSizeMultiplier = 2.5f;
        public static readonly float MinInterior = CharacterHeightRef * MinSizeMultiplier;

        public static List<CavePathKnot> BuildDescendingPath(
            int segments,
            int chamberCount,
            int seed,
            float stepLength = 11f,
            float dropPerStep = 0.32f,
            float yawVarianceDegrees = 22f,
            float chamberSizeMultiplier = 2.35f,
            float entranceYawDegrees = 0f)
        {
            segments = Mathf.Clamp(segments, 6, 20);
            chamberCount = Mathf.Clamp(chamberCount, 2, 8);
            var chamberEvery = Mathf.Max(2, segments / Mathf.Max(1, chamberCount));

            var rng = new System.Random(seed);
            var path = new List<CavePathKnot>(segments + 2);
            var pos = Vector3.zero;
            var tunnelRx = MinInterior * 0.5f;
            var tunnelRy = MinInterior * 0.5f;

            var forward = Quaternion.Euler(0f, entranceYawDegrees, 0f) * Vector3.forward;
            forward = TiltDownward(forward, 14f);
            path.Add(new CavePathKnot(pos, tunnelRx, tunnelRy, false));

            var minDrop = Mathf.Max(0.42f, dropPerStep);
            var prevY = pos.y;

            for (var i = 0; i < segments; i++)
            {
                var yaw = (float)(rng.NextDouble() * 2 - 1) * yawVarianceDegrees;
                var pitch = 10f + (float)rng.NextDouble() * 14f;
                var roll = (float)(rng.NextDouble() * 2 - 1) * 6f;
                forward = TiltDownward(Quaternion.Euler(roll, yaw, 0f) * forward, pitch);
                forward.Normalize();

                var step = Mathf.Max(7f, stepLength) + (float)rng.NextDouble() * 2.5f;
                pos += forward * step;
                pos += Vector3.down * minDrop;

                if (pos.y >= prevY - minDrop * 0.5f)
                    pos.y = prevY - minDrop;
                prevY = pos.y;

                var isChamber = i > 0 && (i + 1) % chamberEvery == 0;
                var mul = isChamber ? chamberSizeMultiplier : 1f;
                path.Add(new CavePathKnot(pos, tunnelRx * mul, tunnelRy * mul, isChamber));
            }

            return path;
        }

        static Vector3 TiltDownward(Vector3 forward, float pitchDegrees)
        {
            return Quaternion.Euler(pitchDegrees, 0f, 0f) * forward;
        }
    }
}
