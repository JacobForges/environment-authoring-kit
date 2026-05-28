using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Playtest bot uses the same capsule dimensions as the tagged Player.</summary>
    public static class CavePlaytestBotScale
    {
        public const float DefaultCellSizeMeters = 3f;

        public static void GetDimensions(Transform root, out float height, out float radius)
        {
            HumanoidDimensions.Resolve(out height, out radius, out _);
        }

        public static float ResolveCellSizeMeters(Transform root)
        {
            for (var t = root; t != null; t = t.parent)
            {
                var meta = t.GetComponent<CaveBuildMetadata>();
                if (meta != null && meta.cellSizeMeters > 0.5f)
                    return meta.cellSizeMeters;
            }

            return 3f;
        }
    }
}
