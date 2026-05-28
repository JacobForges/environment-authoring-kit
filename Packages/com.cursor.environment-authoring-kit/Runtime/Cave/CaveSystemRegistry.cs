using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Runtime lookup for tagged cave features (entrance, water, hidden waterfall).
    /// </summary>
    public sealed class CaveSystemRegistry : MonoBehaviour
    {
        public static CaveSystemRegistry Instance { get; private set; }

        public Transform entrance;
        public Transform undergroundWater;
        public Transform hiddenWaterfall;
        public IReadOnlyList<MinableRock> MinableRocks => _minables;
        readonly List<MinableRock> _minables = new();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            Refresh();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Refresh()
        {
            _minables.Clear();
            _minables.AddRange(GetComponentsInChildren<MinableRock>(true));

            entrance ??= FindFeature(CaveFeatureKind.Entrance);
            undergroundWater ??= FindFeature(CaveFeatureKind.UndergroundWater);
            hiddenWaterfall ??= FindFeature(CaveFeatureKind.HiddenWaterfall);
        }

        Transform FindFeature(CaveFeatureKind kind)
        {
            foreach (var marker in GetComponentsInChildren<CaveFeatureMarker>(true))
            {
                if (marker.featureKind == kind)
                    return marker.transform;
            }

            return null;
        }
    }
}
