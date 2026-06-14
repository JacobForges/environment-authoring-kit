using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Keeps trying to add a convex body MeshCollider until the humanoid visual mesh exists
    /// (cosmetics and avatar attach often run after Awake / first EnsureMeshCollider call).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class CharacterMeshColliderEnsurer : MonoBehaviour
    {
        const float MaxSeconds = 8f;

        float _deadline;

        public static void Attach(Transform root)
        {
            if (root == null)
                return;

            var existing = root.GetComponent<CharacterMeshColliderEnsurer>();
            if (existing != null)
            {
                existing._deadline = Time.unscaledTime + MaxSeconds;
                if (CharacterBodyColliderUtility.TryEnsureMeshCollider(root))
                    Object.Destroy(existing);
                return;
            }

            if (CharacterBodyColliderUtility.TryEnsureMeshCollider(root))
                return;

            var ensurer = root.gameObject.AddComponent<CharacterMeshColliderEnsurer>();
            ensurer._deadline = Time.unscaledTime + MaxSeconds;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= _deadline)
            {
                Destroy(this);
                return;
            }

            if (CharacterBodyColliderUtility.TryEnsureMeshCollider(transform))
                Destroy(this);
        }
    }
}
