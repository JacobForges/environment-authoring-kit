using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Scale and ground humanoid visuals so feet sit at the CharacterController base
    /// and overall height matches the gameplay capsule.
    /// </summary>
    public static class HumanoidVisualFit
    {
        public const float DefaultTargetHeight = 1.8f;

        static readonly string[] VisualMountNames =
        {
            "AvatarVisual",
            "PlayerExplorerVisual",
            "PlayerVisual",
            "KenneyAdventurer",
        };

        public static bool TryFitToCharacterController(Transform characterRoot, float targetHeight = DefaultTargetHeight)
        {
            if (characterRoot == null)
                return false;

            var cc = characterRoot.GetComponent<CharacterController>();
            if (cc != null)
                targetHeight = Mathf.Max(1.2f, cc.height);

            var mount = FindVisualMount(characterRoot);
            if (mount == null)
                return false;

            if (!TryMeasureLocalBounds(characterRoot, mount, out var min, out var max))
                return false;

            var height = max.y - min.y;
            if (height < 0.01f)
                return false;

            var scaleFactor = targetHeight / height;
            mount.localScale = Vector3.Scale(mount.localScale, Vector3.one * scaleFactor);

            if (!TryMeasureLocalBounds(characterRoot, mount, out min, out max))
                return false;

            var footDelta = -min.y;
            if (Mathf.Abs(footDelta) > 0.0001f)
                mount.localPosition += new Vector3(0f, footDelta, 0f);

            return true;
        }

        public static Transform FindVisualMount(Transform characterRoot)
        {
            if (characterRoot == null)
                return null;

            foreach (var mountName in VisualMountNames)
            {
                var mount = FindDeepChild(characterRoot, mountName);
                if (mount != null && HasRenderableChildren(mount))
                    return mount;
            }

            return null;
        }

        static bool HasRenderableChildren(Transform root)
        {
            foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend != null && rend.enabled)
                    return true;
            }

            return false;
        }

        static bool TryMeasureLocalBounds(Transform characterRoot, Transform mount, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var any = false;

            foreach (var rend in mount.GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null || !rend.enabled)
                    continue;

                if (IsCosmeticOnlyRenderer(rend))
                    continue;

                EncapsulateRendererBounds(characterRoot, rend, ref min, ref max, ref any);
            }

            return any;
        }

        static void EncapsulateRendererBounds(
            Transform characterRoot,
            Renderer rend,
            ref Vector3 min,
            ref Vector3 max,
            ref bool any)
        {
            var bounds = rend.bounds;
            var center = bounds.center;
            var extents = bounds.extents;

            for (var xi = -1; xi <= 1; xi += 2)
            {
                for (var yi = -1; yi <= 1; yi += 2)
                {
                    for (var zi = -1; zi <= 1; zi += 2)
                    {
                        var world = center + Vector3.Scale(extents, new Vector3(xi, yi, zi));
                        var local = characterRoot.InverseTransformPoint(world);
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                        any = true;
                    }
                }
            }
        }

        static bool IsCosmeticOnlyRenderer(Renderer rend)
        {
            var name = rend.gameObject.name.ToLowerInvariant();
            return name.Contains("hair")
                   || name.Contains("eyebrow")
                   || name.Contains("eyelash")
                   || name.Contains("beard");
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
