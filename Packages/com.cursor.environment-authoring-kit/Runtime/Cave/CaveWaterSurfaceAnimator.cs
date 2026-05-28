using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Scrolls Ignite Simple Water Shader UVs on the underground pool.</summary>
    [DisallowMultipleComponent]
    public sealed class CaveWaterSurfaceAnimator : MonoBehaviour
    {
        public float scrollSpeedU = 0.045f;
        public float scrollSpeedV = 0.032f;
        public float waveBobAmplitude = 0.04f;
        public float waveBobFrequency = 0.85f;

        static readonly int IgniteNormalId = Shader.PropertyToID("Texture2D_6d0f902902b04ba687ee00a51db7ba6d");

        Material _materialInstance;
        Vector2 _baseOffset;
        Vector3 _baseLocalPosition;
        int _scrollPropertyId;

        void Awake()
        {
            _baseLocalPosition = transform.localPosition;
            var renderer = GetComponent<Renderer>();
            if (renderer == null)
                return;

            _materialInstance = renderer.material;
            _scrollPropertyId = _materialInstance != null && _materialInstance.HasProperty(IgniteNormalId)
                ? IgniteNormalId
                : 0;
        }

        void Update()
        {
            if (_materialInstance == null || _scrollPropertyId == 0)
                return;

            _baseOffset.x += scrollSpeedU * Time.deltaTime;
            _baseOffset.y += scrollSpeedV * Time.deltaTime;
            _materialInstance.SetTextureOffset(_scrollPropertyId, _baseOffset);

            var bob = Mathf.Sin(Time.time * waveBobFrequency) * waveBobAmplitude;
            transform.localPosition = _baseLocalPosition + Vector3.up * bob;
        }
    }
}
