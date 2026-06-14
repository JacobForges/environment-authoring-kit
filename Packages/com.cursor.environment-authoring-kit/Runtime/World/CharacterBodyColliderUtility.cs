using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Convex MeshCollider on humanoid visual meshes for player/agent body hits.</summary>
    public static class CharacterBodyColliderUtility
    {
        static readonly string[] VisualMountNames = { "AvatarVisual", "PlayerExplorerVisual" };

        static readonly string[] BodyMeshPriorityNames =
        {
            "Base Character Mesh",
            "Base Character Root",
        };

        static readonly string[] BodyMeshSkipMountNames =
        {
            "ARMOR PARTS",
            "FACE DETAILS PARTS",
            "FACE DETAILS",
            "ARMOR",
        };

        readonly struct BodyMeshTarget
        {
            internal BodyMeshTarget(GameObject gameObject, Mesh mesh, bool isSkinned)
            {
                GameObject = gameObject;
                Mesh = mesh;
                IsSkinned = isSkinned;
            }

            internal GameObject GameObject { get; }
            internal Mesh Mesh { get; }
            internal bool IsSkinned { get; }
            internal bool IsValid => GameObject != null && Mesh != null;
        }

        public static void EnsureMeshCollider(Transform root)
        {
            if (root == null)
                return;

            if (TryEnsureMeshCollider(root))
                return;

            CharacterMeshColliderEnsurer.Attach(root);
        }

        public static bool TryEnsureMeshCollider(Transform root)
        {
            if (root == null)
                return false;

            if (TryEnsureOnTarget(FindBodyMeshTarget(root)))
                return true;

            return TryEnsureOnTarget(FindBodyMeshTarget(root, includeInactive: true));
        }

        /// <summary>Humanoid visual mesh attached (cosmetic / catalog / modular character).</summary>
        public static bool HasActiveVisualMesh(Transform root)
        {
            if (root == null)
                return false;

            var target = FindBodyMeshTarget(root);
            if (!target.IsValid)
                target = FindBodyMeshTarget(root, includeInactive: true);
            if (target.IsValid)
                return true;

            return HasAnyHumanoidMesh(root, includeInactive: true);
        }

        /// <summary>Any skinned body mesh under the character — ignores reveal-gate renderer disable.</summary>
        public static bool HasAnyHumanoidMesh(Transform root, bool includeInactive = false)
        {
            if (root == null)
                return false;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!IsUsableSkinnedMesh(smr, includeInactive))
                    continue;

                if (IsUnderSkippedMount(smr.transform))
                    continue;

                if (IsCosmeticPartName(smr.gameObject.name))
                    continue;

                if (smr.sharedMesh.vertexCount > 0)
                    return true;
            }

            return false;
        }

        /// <summary>Convex MeshCollider on the body mesh matching the active visual.</summary>
        public static bool HasBodyMeshCollider(Transform root)
        {
            if (root == null)
                return false;

            var target = FindBodyMeshTarget(root);
            if (!target.IsValid)
                target = FindBodyMeshTarget(root, includeInactive: true);
            if (!target.IsValid)
                return false;

            var mc = target.GameObject.GetComponent<MeshCollider>();
            return mc != null && mc.enabled && mc.sharedMesh == target.Mesh;
        }

        static bool TryEnsureOnTarget(BodyMeshTarget target)
        {
            if (!target.IsValid)
                return false;

            var go = target.GameObject;
            var mesh = target.Mesh;

            var mc = go.GetComponent<MeshCollider>();
            var added = mc == null;
            if (mc == null)
                mc = go.AddComponent<MeshCollider>();

            if (mc.sharedMesh != mesh)
                mc.sharedMesh = mesh;

            mc.convex = true;
            mc.isTrigger = false;

            if (added)
            {
                Debug.Log(
                    $"[CharacterBodyCollider] Added convex MeshCollider on {GetHierarchyPath(go.transform)}",
                    go);
            }

            return mc.sharedMesh == mesh;
        }

        static BodyMeshTarget FindBodyMeshTarget(Transform root, bool includeInactive = false)
        {
            if (root == null)
                return default;

            foreach (var scope in EnumerateVisualScopes(root))
            {
                var named = FindNamedBodyMeshTarget(scope, includeInactive);
                if (named.IsValid)
                    return named;
            }

            foreach (var scope in EnumerateVisualScopes(root))
            {
                var best = FindBestBodyMeshTarget(scope, includeInactive);
                if (best.IsValid)
                    return best;
            }

            return FindBestBodyMeshTarget(root, includeInactive);
        }

        static System.Collections.Generic.IEnumerable<Transform> EnumerateVisualScopes(Transform root)
        {
            if (root == null)
                yield break;

            var seen = new System.Collections.Generic.HashSet<Transform>();

            var playerVisual = FindDeepChild(root, "PlayerVisual");
            if (playerVisual != null)
            {
                var explorerMount = FindDeepChild(playerVisual, "PlayerExplorerVisual");
                if (explorerMount != null && seen.Add(explorerMount))
                    yield return explorerMount;

                if (seen.Add(playerVisual))
                    yield return playerVisual;
            }

            foreach (var mountName in VisualMountNames)
            {
                var mount = FindDeepChild(root, mountName);
                if (mount != null && seen.Add(mount))
                    yield return mount;
            }
        }

        static BodyMeshTarget FindNamedBodyMeshTarget(Transform scope, bool includeInactive)
        {
            if (scope == null)
                return default;

            foreach (var name in BodyMeshPriorityNames)
            {
                var node = FindDeepChild(scope, name);
                if (node == null)
                    continue;

                if (!includeInactive && !node.gameObject.activeInHierarchy)
                    continue;

                if (IsUnderSkippedMount(node))
                    continue;

                var target = TargetFromTransform(node, includeInactive);
                if (target.IsValid)
                    return target;
            }

            return default;
        }

        static BodyMeshTarget FindBestBodyMeshTarget(Transform scope, bool includeInactive)
        {
            if (scope == null)
                return default;

            BodyMeshTarget best = default;
            var bestVolume = 0f;

            foreach (var smr in scope.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!IsUsableSkinnedMesh(smr, includeInactive))
                    continue;

                if (IsUnderSkippedMount(smr.transform))
                    continue;

                if (IsCosmeticPartName(smr.gameObject.name))
                    continue;

                var volume = RendererBoundsVolume(smr);
                if (volume > bestVolume)
                {
                    bestVolume = volume;
                    best = new BodyMeshTarget(smr.gameObject, smr.sharedMesh, isSkinned: true);
                }
            }

            if (best.IsValid)
                return best;

            foreach (var mf in scope.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsUsableMeshFilter(mf, includeInactive))
                    continue;

                if (IsUnderSkippedMount(mf.transform))
                    continue;

                if (IsCosmeticPartName(mf.gameObject.name))
                    continue;

                var volume = RendererBoundsVolume(mf.GetComponent<MeshRenderer>());
                if (volume > bestVolume)
                {
                    bestVolume = volume;
                    best = new BodyMeshTarget(mf.gameObject, mf.sharedMesh, isSkinned: false);
                }
            }

            return best;
        }

        static BodyMeshTarget TargetFromTransform(Transform node, bool includeInactive)
        {
            if (node == null)
                return default;

            var smr = node.GetComponent<SkinnedMeshRenderer>();
            if (IsUsableSkinnedMesh(smr, includeInactive))
                return new BodyMeshTarget(smr.gameObject, smr.sharedMesh, isSkinned: true);

            foreach (var childSmr in node.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsUnderSkippedMount(childSmr.transform))
                    continue;

                if (IsUsableSkinnedMesh(childSmr, includeInactive))
                    return new BodyMeshTarget(childSmr.gameObject, childSmr.sharedMesh, isSkinned: true);
            }

            var mf = node.GetComponent<MeshFilter>();
            if (IsUsableMeshFilter(mf, includeInactive))
                return new BodyMeshTarget(mf.gameObject, mf.sharedMesh, isSkinned: false);

            foreach (var childMf in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (IsUnderSkippedMount(childMf.transform))
                    continue;

                if (IsUsableMeshFilter(childMf, includeInactive))
                    return new BodyMeshTarget(childMf.gameObject, childMf.sharedMesh, isSkinned: false);
            }

            return default;
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

        static bool IsUnderSkippedMount(Transform node)
        {
            for (var t = node; t != null; t = t.parent)
            {
                foreach (var skipName in BodyMeshSkipMountNames)
                {
                    if (t.name == skipName)
                        return true;
                }
            }

            return false;
        }

        static bool IsCosmeticPartName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            var lower = name.ToLowerInvariant();
            return lower.Contains("eyebrow")
                   || lower.Contains("eyelash")
                   || lower.Contains("hair")
                   || lower.Contains("beard")
                   || lower.Contains("nose")
                   || lower.Contains("ear")
                   || lower.Contains("mouth")
                   || lower.Contains("eye ");
        }

        static bool IsUsableSkinnedMesh(SkinnedMeshRenderer smr, bool includeInactive)
        {
            if (smr == null || smr.sharedMesh == null)
                return false;

            return includeInactive || smr.gameObject.activeInHierarchy;
        }

        static bool IsUsableMeshFilter(MeshFilter mf, bool includeInactive)
        {
            if (mf == null || mf.sharedMesh == null)
                return false;

            return includeInactive || mf.gameObject.activeInHierarchy;
        }

        static float RendererBoundsVolume(Component renderer)
        {
            if (renderer is Renderer rend && rend != null)
            {
                var size = rend.bounds.size;
                return size.x * size.y * size.z;
            }

            return 0f;
        }

        internal static string GetHierarchyPath(Transform t)
        {
            if (t == null)
                return "(null)";

            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }
    }
}
