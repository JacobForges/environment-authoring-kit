using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Fit interaction colliders to CC0 humanoid visuals after ground snap.</summary>
    public static class ContentLayoutHumanoidFit
    {
        public static void RemoveFallbackBody(Transform root)
        {
            if (root == null)
                return;
            var body = root.Find("Body");
            if (body != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(body.gameObject);
                else
#endif
                    Object.Destroy(body.gameObject);
            }
        }

        public static bool TryGetFeetBounds(Transform root, out Bounds bounds, bool excludeLabels = true)
        {
            bounds = default;
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>();
            var found = false;
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                if (excludeLabels && IsLabelRenderer(r))
                    continue;

                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                    bounds.Encapsulate(r.bounds);
            }

            return found;
        }

        public static void FitCapsuleCollider(Transform root, CapsuleCollider col, float minHeight = 1.6f)
        {
            if (root == null || col == null)
                return;

            if (!TryGetFeetBounds(root, out var bounds))
            {
                col.center = new Vector3(0f, 1f, 0f);
                col.height = 2.1f;
                col.radius = 0.45f;
                return;
            }

            var height = Mathf.Max(minHeight, bounds.size.y);
            var radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.42f, 0.28f, 0.85f);
            var feetY = bounds.min.y - root.position.y;
            col.height = height;
            col.radius = radius;
            col.center = new Vector3(0f, feetY + height * 0.5f, 0f);
            col.direction = 1;
        }

        static bool IsLabelRenderer(Renderer r)
        {
            var t = r.transform;
            return t.name == "Label" || t.GetComponent<TextMesh>() != null;
        }
    }
}
