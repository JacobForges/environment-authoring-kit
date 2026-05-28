using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    public enum CaveFeatureKind
    {
        Entrance,
        UndergroundWater,
        HiddenWaterfall,
        NavWaypoint,
        FinishGoal
    }

    /// <summary>
    /// Marker for entrance / water features so gameplay and XR systems can locate them.
    /// Tags are applied in Awake (not OnValidate) to avoid editor SendMessage errors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveFeatureMarker : MonoBehaviour
    {
        public CaveFeatureKind featureKind;
        [TextArea] public string notes;

        [Header("Finish")]
        public string victoryMessage = "You reached the end of the cave!";

        void Awake()
        {
            ApplyTag();
        }

        void OnTriggerEnter(Collider other)
        {
            if (featureKind != CaveFeatureKind.FinishGoal)
                return;

            if (!other.CompareTag("Player") && other.GetComponentInParent<CharacterController>() == null)
                return;

            Debug.Log($"[Cave] {victoryMessage}", this);
        }

        public void ApplyTag()
        {
            var desired = GetTagForKind();
            if (!string.IsNullOrEmpty(desired) && !CompareTag(desired))
                gameObject.tag = desired;
        }

        string GetTagForKind()
        {
            return featureKind switch
            {
                CaveFeatureKind.Entrance => CaveTags.Entrance,
                CaveFeatureKind.UndergroundWater => CaveTags.Water,
                CaveFeatureKind.HiddenWaterfall => CaveTags.HiddenWaterfall,
                _ => null
            };
        }
    }
}
