using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Resolve content-layout positions from world anchors + live hub.</summary>
    public static class ContentLayoutWorldPlacement
    {
        const float MaxSlotDriftFromBriefMeters = 12f;

        public static void InvalidateCache()
        {
            ContentLayoutSlotResolver.InvalidateCache();
            ContentLayoutWorldSnapshotCache.InvalidateCache();
        }

        public static Vector3 ResolveNpcPosition(string npcId, Vector3 briefFallback, float briefRotationY, out float rotationY)
        {
            if (ContentLayoutSlotResolver.TryResolveNpc(npcId, out var world, out rotationY))
            {
                if (IsReasonablyCloseToBrief(world, briefFallback))
                    return world;
            }
            rotationY = briefRotationY;
            return briefFallback;
        }

        public static Vector3 ResolveEnemyPosition(string enemyId, Vector3 briefFallback)
        {
            if (ContentLayoutSlotResolver.TryResolveEnemy(enemyId, out var world)
                && IsReasonablyCloseToBrief(world, briefFallback))
                return world;
            return briefFallback;
        }

        public static Vector3 ResolvePropPosition(string propId, Vector3 briefFallback)
        {
            try
            {
                if (ContentLayoutSlotResolver.TryResolveProp(propId, out var world)
                    && IsReasonablyCloseToBrief(world, briefFallback))
                    return world;
                return briefFallback;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    $"[ContentLayout] ResolvePropPosition fallback for '{propId}': {ex.Message}");
                return briefFallback;
            }
        }

        static bool IsReasonablyCloseToBrief(Vector3 resolvedWorld, Vector3 briefFallback)
        {
            var a = new Vector2(resolvedWorld.x, resolvedWorld.z);
            var b = new Vector2(briefFallback.x, briefFallback.z);
            return Vector2.Distance(a, b) <= MaxSlotDriftFromBriefMeters;
        }

        public static bool TryResolveEnemyPatrol(string enemyId, out Vector3[] waypoints) =>
            ContentLayoutSlotResolver.TryResolveEnemyPatrol(enemyId, out waypoints);
    }
}
