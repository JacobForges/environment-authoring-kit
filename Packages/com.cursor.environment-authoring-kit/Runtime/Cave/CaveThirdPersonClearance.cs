using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Headroom + corridor targets for third-person camera (player + spectator), derived from humanoid scale.
    /// </summary>
    public static class CaveThirdPersonClearance
    {
        public const float MinWalkClearanceFloorMeters = 3.6f;
        public const float MinCeilingMultiplier = 3.9f;
        public const float DefaultCorridorHeightFloorMeters = 8.75f;
        public const float MinCeilingAboveFloorMeters = 26f;
        public const float CavernHeadroomScale = 1.58f;
        public const float HeadroomProbePaddingMeters = 1.65f;

        public static float ResolveMinWalkClearance()
        {
            HumanoidDimensions.Resolve(out var height, out _, out _);
            return Mathf.Max(MinWalkClearanceFloorMeters, height * 1.9f);
        }

        public static float ResolveDefaultCorridorHeight()
        {
            HumanoidDimensions.Resolve(out var height, out _, out _);
            var shoulder = height * 0.78f;
            return Mathf.Max(DefaultCorridorHeightFloorMeters, height + shoulder + 2.4f);
        }

        public static float ResolveMinCeilingAboveFloor()
        {
            HumanoidDimensions.Resolve(out var height, out _, out _);
            var preset = ThirdPersonFollowCamera.PlayerPreset;
            return Mathf.Max(
                MinCeilingAboveFloorMeters,
                height + preset.ShoulderHeight + preset.Distance * 0.42f + 5f);
        }
    }
}
