using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor
{
    public sealed class SceneGroundInfo
    {
        public Transform Anchor;
        public float SurfaceY;
        public Bounds Bounds;
        public UnityEngine.Terrain Terrain;
        public Vector3 Down => Anchor != null ? -Anchor.up : Vector3.down;
        public Vector3 HorizontalForward => Anchor != null ? Anchor.forward : Vector3.forward;

        public bool HasAnchor => Anchor != null;
        public bool HasTerrain => Terrain != null;

        /// <summary>World-space ground anchor (bounds center XZ, surface Y). Used by surface meat-loop + entrance builders.</summary>
        public Vector3 AnchorWorld =>
            HasAnchor
                ? new Vector3(Bounds.center.x, SurfaceY, Bounds.center.z)
                : Vector3.zero;
    }

    public static class SceneGroundResolver
    {
        static readonly string[] GroundNames =
        {
            "grid", "ground", "groundplane", "ground_plane", "groundcollision", "plane", "floor", "terrain", "land"
        };

        static readonly string[] RejectedNameFragments =
        {
            "background", "skybox", "sky", "backdrop", "ui", "canvas", "camera"
        };

        public static SceneGroundInfo Resolve(Transform userAssigned = null)
        {
            var info = new SceneGroundInfo();

            if (IsValidGroundAnchor(userAssigned))
                info.Anchor = userAssigned;
            else
            {
                var stored = LoadAssignedGround();
                if (IsValidGroundAnchor(stored))
                    info.Anchor = stored;
            }

            if (info.Anchor == null)
                info.Anchor = FindByGroundTag();

            if (info.Anchor == null)
                info.Anchor = FindGroundByName();

            if (info.Anchor == null)
                info.Terrain = ActiveSceneUtility.FindInActiveScene<UnityEngine.Terrain>();

            if (info.Anchor == null && info.Terrain != null)
                info.Anchor = info.Terrain.transform;

            if (info.Anchor == null)
                info.Anchor = FindLargestWalkableSurface();

            if (info.Terrain == null && info.Anchor != null)
                info.Terrain = info.Anchor.GetComponent<UnityEngine.Terrain>();

            if (info.Terrain == null)
                info.Terrain = ActiveSceneUtility.FindInActiveScene<UnityEngine.Terrain>();

            ComputeSurface(info);
            return info;
        }

        public static bool IsValidGroundAnchor(Transform candidate)
        {
            if (candidate == null)
                return false;

            if (candidate.CompareTag("Ground"))
                return true;

            var lower = candidate.name.ToLowerInvariant();
            if (IsRejectedName(lower))
                return false;

            foreach (var token in GroundNames)
            {
                if (lower == token || lower.StartsWith(token + "_", StringComparison.Ordinal) ||
                    lower.EndsWith("_" + token, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static void SaveAssignedGround(Transform ground)
        {
            if (!IsValidGroundAnchor(ground))
            {
                Debug.LogWarning(
                    $"[Environment Kit] '{ground?.name}' is not a valid ground anchor. Tag it 'Ground' or rename to Grid/Ground/Floor.");
                return;
            }

            var id = GlobalObjectId.GetGlobalObjectIdSlow(ground.gameObject);
            EditorPrefs.SetString(EnvironmentKitSettings.GroundObjectKey, id.ToString());
        }

        static Transform LoadAssignedGround()
        {
            var stored = EditorPrefs.GetString(EnvironmentKitSettings.GroundObjectKey, string.Empty);
            if (string.IsNullOrEmpty(stored))
                return null;

            if (!GlobalObjectId.TryParse(stored, out var id))
                return null;

            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            if (obj == null || obj.scene != SceneManager.GetActiveScene())
                return null;

            return IsValidGroundAnchor(obj.transform) ? obj.transform : null;
        }

        static Transform FindByGroundTag()
        {
            Transform best = null;
            var bestArea = 0f;

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.CompareTag("Ground"))
                        continue;

                    var area = EstimateHorizontalArea(t);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = t;
                    }
                }
            }

            return best;
        }

        static Transform FindGroundByName()
        {
            Transform best = null;
            var bestScore = int.MinValue;
            var bestArea = 0f;

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var score = ScoreName(t.name);
                    if (score <= 0)
                        continue;

                    var area = EstimateHorizontalArea(t);
                    if (score > bestScore || (score == bestScore && area > bestArea))
                    {
                        bestScore = score;
                        bestArea = area;
                        best = t;
                    }
                }
            }

            return best;
        }

        static int ScoreName(string objectName)
        {
            var lower = objectName.ToLowerInvariant();
            if (IsRejectedName(lower))
                return 0;

            for (var i = 0; i < GroundNames.Length; i++)
            {
                if (lower == GroundNames[i])
                    return 200 - i;
            }

            for (var i = 0; i < GroundNames.Length; i++)
            {
                if (lower.StartsWith(GroundNames[i], StringComparison.Ordinal) ||
                    lower.EndsWith(GroundNames[i], StringComparison.Ordinal))
                    return 120 - i;
            }

            return 0;
        }

        static bool IsRejectedName(string lowerName)
        {
            foreach (var fragment in RejectedNameFragments)
            {
                if (lowerName.Contains(fragment, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static Transform FindLargestWalkableSurface()
        {
            Transform best = null;
            var bestArea = 0f;

            if (!ActiveSceneUtility.HasValidActiveScene)
                return null;

            foreach (var root in ActiveSceneUtility.ActiveScene.GetRootGameObjects())
            {
                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                {
                    if (col is not (MeshCollider or BoxCollider or TerrainCollider))
                        continue;

                    if (IsRejectedName(col.name.ToLowerInvariant()))
                        continue;

                    if (IsUnderCaveSystem(col.transform))
                        continue;

                    var area = col.bounds.size.x * col.bounds.size.z;
                    if (area <= bestArea)
                        continue;

                    bestArea = area;
                    best = col.transform;
                }
            }

            return best;
        }

        static float EstimateHorizontalArea(Transform t)
        {
            var col = t.GetComponent<Collider>();
            if (col != null)
                return col.bounds.size.x * col.bounds.size.z;

            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0f;

            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b.size.x * b.size.z;
        }

        static void ComputeSurface(SceneGroundInfo info)
        {
            if (info.Anchor == null)
            {
                info.SurfaceY = 0f;
                info.Bounds = new Bounds(Vector3.zero, Vector3.one * 50f);
                return;
            }

            info.Bounds = CalculateBounds(info.Anchor);
            info.SurfaceY = info.Bounds.max.y;

            if (info.Terrain != null)
            {
                var t = info.Terrain;
                var sample = ResolveTerrainSampleXZ(info);
                info.SurfaceY = t.SampleHeight(sample) + t.transform.position.y;
            }
        }

        static Vector3 ResolveTerrainSampleXZ(SceneGroundInfo info)
        {
            var caveRoot = CaveGeometryPaths.FindCaveSystemRoot();
            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth != Vector3.zero)
                    return new Vector3(mouth.x, 0f, mouth.z);
            }

            return new Vector3(info.Bounds.center.x, 0f, info.Bounds.center.z);
        }

        static Bounds CalculateBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var initialized = false;
                var b = new Bounds(root.position, Vector3.zero);
                for (var i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null || IsUnderCaveSystem(renderers[i].transform))
                        continue;

                    if (!initialized)
                    {
                        b = renderers[i].bounds;
                        initialized = true;
                    }
                    else
                        b.Encapsulate(renderers[i].bounds);
                }

                if (initialized)
                    return b;
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                var initialized = false;
                var b = new Bounds(root.position, Vector3.zero);
                for (var i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] == null || IsUnderCaveSystem(colliders[i].transform))
                        continue;

                    if (!initialized)
                    {
                        b = colliders[i].bounds;
                        initialized = true;
                    }
                    else
                        b.Encapsulate(colliders[i].bounds);
                }

                if (initialized)
                    return b;
            }

            return new Bounds(root.position, Vector3.one * 20f);
        }

        static bool IsUnderCaveSystem(Transform t)
        {
            while (t != null)
            {
                var n = t.name;
                if (n == CaveGeometryPaths.CaveSystemRootName || n == CaveGeometryPaths.LegacyCaveSystemRootName ||
                    n == CaveGeometryPaths.GeometryRoot || n == CaveGeometryPaths.RouteTerrainFloor ||
                    n == CaveGeometryPaths.RouteTerrainCeiling || n == CaveGeometryPaths.PathPlatforms ||
                    n == CaveGeometryPaths.BlockTunnel || n == CaveGeometryPaths.AdventureShell ||
                    n == "CaveMazeVolume" || n == "MainCaveTube" || n == "MainCaveOuterShell")
                    return true;
                t = t.parent;
            }

            return false;
        }
    }
}
