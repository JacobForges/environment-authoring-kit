using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    [RequireComponent(typeof(Light))]
    public sealed class CaveLightRangeClamp : MonoBehaviour
    {
        public bool chamberLight;

        void Awake() => CaveLightingSettings.ApplyCaveLight(GetComponent<Light>(), chamberLight);
    }
}
