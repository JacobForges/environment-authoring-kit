using UnityEngine;

namespace EnvironmentAuthoringKit.Runtime
{
    /// <summary>
    /// Lightweight wind sway + idle automation for Hub-generated planner props.
    /// </summary>
    public sealed class PlannerPropWindMotion : MonoBehaviour
    {
        [SerializeField] float swayAmplitude = 4f;
        [SerializeField] float swaySpeed = 1.1f;
        [SerializeField] float bobAmplitude = 0.02f;
        [SerializeField] float bobSpeed = 0.7f;

        Vector3 _baseEuler;
        Vector3 _basePos;
        float _phase;

        void Awake()
        {
            _baseEuler = transform.localEulerAngles;
            _basePos = transform.localPosition;
            _phase = Random.Range(0f, Mathf.PI * 2f);
        }

        void Update()
        {
            var t = Time.time + _phase;
            var sway = Mathf.Sin(t * swaySpeed) * swayAmplitude;
            var bob = Mathf.Sin(t * bobSpeed) * bobAmplitude;
            transform.localEulerAngles = _baseEuler + new Vector3(sway * 0.35f, sway, 0f);
            transform.localPosition = _basePos + new Vector3(0f, bob, 0f);
        }
    }
}
