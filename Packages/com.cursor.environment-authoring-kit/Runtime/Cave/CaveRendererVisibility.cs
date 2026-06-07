using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Safe renderer queries — avoids MissingComponentException on broken prefab instances.</summary>
    public static class CaveRendererVisibility
    {
        public static bool IsEnabledSafe(Renderer renderer)
        {
            if (renderer == null)
                return false;

            try
            {
                return renderer.enabled;
            }
            catch (MissingComponentException)
            {
                return false;
            }
        }

        public static bool HasVisibleRenderer(GameObject root, bool includeChildren = true) =>
            HasVisibleRenderer(root != null ? root.transform : null, includeChildren);

        public static bool HasVisibleRenderer(Component root, bool includeChildren = true)
        {
            if (root == null)
                return false;

            if (!includeChildren)
                return HasEnabledRendererOnObject(root.GetComponent<MeshRenderer>()) ||
                       HasEnabledRendererOnObject(root.GetComponent<SkinnedMeshRenderer>());

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null)
                    continue;

                if (HasEnabledRendererOnObject(mf.GetComponent<MeshRenderer>()))
                    return true;
            }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (HasEnabledRendererOnObject(smr))
                    return true;
            }

            return false;
        }

        static bool HasEnabledRendererOnObject(Renderer renderer) => IsEnabledSafe(renderer);
    }
}
