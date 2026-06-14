using System.Collections;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Visual mesh readiness (show character) is separate from body MeshCollider setup (physics hits).
    /// Cosmetics and catalog avatars often attach after early EnsureMeshCollider calls.
    /// </summary>
    public static class PlayerAvatarReadiness
    {
        const float DefaultTimeoutSeconds = 8f;

        public static bool IsVisualReady(Transform root) =>
            CharacterBodyColliderUtility.HasActiveVisualMesh(root);

        public static bool IsFullyReady(Transform root) =>
            IsVisualReady(root) && CharacterBodyColliderUtility.HasBodyMeshCollider(root);

        public static void BeginEnsure(Transform root)
        {
            if (root == null)
                return;

            CharacterBodyColliderUtility.EnsureMeshCollider(root);
            CharacterMeshColliderEnsurer.Attach(root);
        }

        /// <summary>Wait until a humanoid visual mesh exists — never blocks on MeshCollider.</summary>
        public static IEnumerator WaitUntilVisualReady(Transform root, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (root == null)
                yield break;

            BeginEnsure(root);
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (IsVisualReady(root))
                    yield break;

                yield return null;
            }

            if (!IsVisualReady(root))
            {
                Debug.LogWarning(
                    $"[PlayerAvatarReadiness] Timed out waiting for visual mesh on {root.name} — forcing renderers on.",
                    root);
                ForceEnableVisualRenderers(root);
            }
        }

        /// <summary>Wait for visual mesh + body MeshCollider; visibility gates should use <see cref="WaitUntilVisualReady"/> instead.</summary>
        public static IEnumerator WaitUntilReady(Transform root, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (root == null)
                yield break;

            yield return WaitUntilVisualReady(root, timeoutSeconds);

            BeginEnsure(root);
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                BeginEnsure(root);
                if (CharacterBodyColliderUtility.HasBodyMeshCollider(root))
                    yield break;

                yield return null;
            }

            if (!CharacterBodyColliderUtility.HasBodyMeshCollider(root))
            {
                Debug.LogWarning(
                    $"[PlayerAvatarReadiness] Timed out waiting for body MeshCollider on {root.name} — visual stays visible.",
                    root);
                BeginEnsure(root);
            }
        }

        /// <summary>Re-enable every renderer under the avatar visual subtree (SkinnedMeshRenderer included).</summary>
        public static void ForceEnableVisualRenderers(Transform root)
        {
            if (root == null)
                return;

            EnsureVisualMountsActive(root);

            foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend != null)
                    rend.enabled = true;
            }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr != null)
                    smr.enabled = true;
            }
        }

        static void EnsureVisualMountsActive(Transform root)
        {
            foreach (var mountName in new[] { "PlayerVisual", "PlayerExplorerVisual", "AvatarVisual" })
            {
                var mount = FindDeepChild(root, mountName);
                if (mount != null && !mount.gameObject.activeSelf)
                    mount.gameObject.SetActive(true);
            }
        }

        static Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent == null)
                return null;

            if (parent.name == childName)
                return parent;

            for (var i = 0; i < parent.childCount; i++)
            {
                var found = FindDeepChild(parent.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
