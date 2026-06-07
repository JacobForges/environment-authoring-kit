using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>World volume that starts a procedural cinematic when the player enters.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldCinematicTrigger : MonoBehaviour
    {
        [Tooltip("intro_guide, boss_portal, biome_foothill, etc. Empty = use object name.")]
        public string cinematicId;

        public WorldSurfaceBiomeId biomeHint = WorldSurfaceBiomeId.PlayKarst;
        public bool playOnce = true;
        public float duration;

        void Reset()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            if (WorldCinematicTimelineBridge.TryPlayOn(this))
                return;

            if (WorldCinematicDirector.Instance == null)
            {
                var go = new GameObject("WorldCinematicDirector");
                go.AddComponent<WorldCinematicDirector>();
            }

            WorldCinematicDirector.Instance.TryPlay(this);
        }
    }
}
