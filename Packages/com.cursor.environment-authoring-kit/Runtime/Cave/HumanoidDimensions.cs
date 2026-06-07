using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Match playtest bot and fallback enemies to the tagged Player CharacterController.</summary>
    public static class HumanoidDimensions
    {
        public const float DefaultHeight = 1.85f;
        public const float DefaultRadius = 0.35f;
        public const float DefaultStepOffset = 0.32f;

        public static void Resolve(out float height, out float radius, out float stepOffset)
        {
            height = DefaultHeight;
            radius = DefaultRadius;
            stepOffset = DefaultStepOffset;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;

            var cc = player.GetComponent<CharacterController>();
            if (cc == null)
                cc = player.GetComponentInChildren<CharacterController>();
            if (cc == null)
                return;

            height = Mathf.Max(1.2f, cc.height);
            radius = Mathf.Max(0.2f, cc.radius);
            stepOffset = Mathf.Clamp(cc.stepOffset, 0.15f, height * 0.35f);
        }

        public static Vector3 CapsuleLocalScale(float height, float radius)
        {
            const float defaultCapsuleHeight = 2f;
            const float defaultCapsuleRadius = 0.5f;
            return new Vector3(
                radius * 2f / defaultCapsuleRadius,
                height / defaultCapsuleHeight,
                radius * 2f / defaultCapsuleRadius);
        }
    }
}
