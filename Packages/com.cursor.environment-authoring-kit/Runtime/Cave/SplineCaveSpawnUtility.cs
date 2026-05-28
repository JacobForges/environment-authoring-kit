using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    public static class SplineCaveSpawnUtility
    {
        const float CharacterRadius = 0.4f;
        const float CharacterHeight = 2f;

        /// <summary>Fraction of tube radius down from path center to walk floor.</summary>
        const float FloorRadiusFactor = 0.76f;

        public static float FindSafeSpawnDistance(CaveSplinePath spline)
        {
            if (spline == null || spline.TotalLength < 1f)
                return 1f;

            for (var dist = 0.5f; dist < spline.TotalLength * 0.22f; dist += 0.75f)
            {
                if (IsClear(spline, dist, null))
                    return dist;
            }

            return 1f;
        }

        public static float FindSafeSpawnDistance(CaveSplinePathAuthoring authoring)
        {
            var local = CaveSplinePathSpace.CreateLocalSpline(authoring);
            return FindSafeSpawnDistance(local);
        }

        public static bool TryGetSpawnPose(
            CaveSplinePath spline,
            float distance,
            out Vector3 position,
            out Quaternion rotation)
        {
            return TryGetSpawnPose(spline, distance, null, out position, out rotation);
        }

        public static bool TryGetSpawnPose(
            CaveSplinePath spline,
            float distance,
            Transform pathRoot,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (spline == null)
                return false;

            distance = Mathf.Clamp(distance, 0f, spline.TotalLength);
            var sample = spline.SampleAtDistance(distance);
            var floor = sample.Position - sample.Up * (sample.RadiusY * FloorRadiusFactor);
            var localPos = floor + sample.Up * (CharacterHeight * 0.5f + 0.15f);
            var localRot = Quaternion.LookRotation(sample.Tangent, Vector3.up);

            position = pathRoot != null ? pathRoot.TransformPoint(localPos) : localPos;
            rotation = pathRoot != null ? pathRoot.rotation * localRot : localRot;

            var up = pathRoot != null ? pathRoot.up : Vector3.up;
            var center = position;
            if (Physics.CheckCapsule(
                    center - up * (CharacterHeight * 0.5f - CharacterRadius),
                    center + up * (CharacterHeight * 0.5f - CharacterRadius),
                    CharacterRadius,
                    ~0,
                    QueryTriggerInteraction.Ignore))
                return false;

            return true;
        }

        public static bool TryGetWorldSpawnPose(
            CaveSplinePathAuthoring authoring,
            float distance,
            out Vector3 position,
            out Quaternion rotation)
        {
            var local = CaveSplinePathSpace.CreateLocalSpline(authoring);
            if (local == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            return TryGetSpawnPose(local, distance, authoring.transform, out position, out rotation);
        }

        static bool IsClear(CaveSplinePath spline, float distance, Transform pathRoot)
        {
            return TryGetSpawnPose(spline, distance, pathRoot, out _, out _);
        }

        public static Pose EvaluateCameraPose(
            CaveSplinePath spline,
            float distance,
            float heightAboveFloor,
            float sideOffset,
            float lookAheadMeters)
        {
            return EvaluateCameraPose(spline, distance, heightAboveFloor, sideOffset, lookAheadMeters, null);
        }

        public static Pose EvaluateCameraPose(
            CaveSplinePath spline,
            float distance,
            float heightAboveFloor,
            float sideOffset,
            float lookAheadMeters,
            Transform pathRoot)
        {
            var sample = spline.SampleAtDistance(distance);
            var ahead = spline.SampleAtDistance(Mathf.Min(distance + lookAheadMeters, spline.TotalLength));
            var floor = sample.Position - sample.Up * (sample.RadiusY * FloorRadiusFactor);
            var camPos = floor + sample.Up * heightAboveFloor + sample.Right * sideOffset;
            var lookTarget = ahead.Position + sample.Up * 1.35f;
            var rot = Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up);

            if (pathRoot != null)
            {
                camPos = pathRoot.TransformPoint(camPos);
                lookTarget = pathRoot.TransformPoint(lookTarget);
                rot = Quaternion.LookRotation((lookTarget - camPos).normalized, pathRoot.up);
            }

            return new Pose(camPos, rot);
        }
    }
}
