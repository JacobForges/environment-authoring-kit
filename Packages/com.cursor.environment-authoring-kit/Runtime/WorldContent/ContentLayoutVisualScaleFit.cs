using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    public static class ContentLayoutVisualScaleFit
    {
        public static void FitHumanoidHeight(Transform visual, float catalogScale)
        {
            if (visual == null)
                return;

            HumanoidDimensions.Resolve(out var targetHeight, out _, out _);
            if (!TryGetRendererBounds(visual, out var bounds))
            {
                visual.localScale = Vector3.one * Mathf.Max(0.01f, catalogScale);
                return;
            }

            var height = Mathf.Max(0.01f, bounds.size.y);
            var mul = targetHeight / height;
            visual.localScale = Vector3.one * Mathf.Max(0.01f, catalogScale * mul);
        }

        public static void FitPropHeight(Transform visual, float catalogScale, float targetHeightM = 1.35f)
        {
            if (visual == null)
                return;

            visual.localScale = Vector3.one * Mathf.Max(0.01f, catalogScale);
            if (!TryGetRendererBounds(visual, out var bounds))
                return;

            var height = Mathf.Max(0.01f, bounds.size.y);
            if (height >= targetHeightM * 0.65f)
                return;

            var mul = targetHeightM / height;
            visual.localScale *= mul;
        }

        static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            var renderers = root.GetComponentsInChildren<Renderer>();
            var found = false;
            foreach (var r in renderers)
            {
                if (r == null || r.transform.name == "Label")
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
    }
}
