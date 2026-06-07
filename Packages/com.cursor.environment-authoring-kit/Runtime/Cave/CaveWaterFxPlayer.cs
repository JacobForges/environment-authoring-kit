using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Starts waterfall particle systems at runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class CaveWaterFxPlayer : MonoBehaviour
    {
        void Start()
        {
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                ps.Play();
        }
    }
}
