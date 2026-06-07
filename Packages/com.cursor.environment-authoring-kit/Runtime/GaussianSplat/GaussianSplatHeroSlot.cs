using UnityEngine;

namespace EnvironmentAuthoringKit.GaussianSplat
{
    /// <summary>
    /// Marks the primary cave-mouth hero splat anchor. Visual-only — use terrain/cave colliders for gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GaussianSplatHeroSlot : MonoBehaviour
    {
        public enum QualityTier
        {
            Low = 0,
            Medium = 1,
            High = 2,
        }

        [Tooltip("Optional GaussianSplatAsset under Assets/. Assigned by Environment Kit build or inspector.")]
        public Object splatAsset;

        [Tooltip("When off, splat renderers under this slot stay disabled (GPU safe).")]
        public bool allowRendering = true;

        [Tooltip("Draw splats in the Editor Scene view (costly with large terrains).")]
        public bool renderInEditMode;

        [Tooltip("Draw splats during Play Mode / builds.")]
        public bool renderInPlayMode = true;

        public QualityTier qualityTier = QualityTier.Medium;

        [Tooltip("World-space radius used for gizmos and budget checks.")]
        public float heroRadiusMeters = 18f;

        [TextArea(2, 4)]
        public string integrationNotes;

        void OnEnable() => ApplyRenderingPolicy();

        void OnValidate() => ApplyRenderingPolicy();

        /// <summary>Called from editor integration after attaching optional UnityGaussianSplatting components.</summary>
        public void ApplyRenderingPolicy()
        {
            var show = allowRendering &&
                       (Application.isPlaying ? renderInPlayMode : renderInEditMode);

            foreach (var behaviour in GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || behaviour is GaussianSplatHeroSlot)
                    continue;

                var name = behaviour.GetType().Name;
                if (name.Contains("GaussianSplat") && name.Contains("Renderer"))
                    behaviour.enabled = show;
            }
        }
    }
}
