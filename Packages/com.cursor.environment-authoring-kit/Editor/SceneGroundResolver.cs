using System;
using EnvironmentAuthoringKit;
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
        public const string PreferredGroundObjectName = "Ground";

        static readonly string[] GroundNames =
        {
            "ground", "groundplane", "ground_plane", "groundcollision", "plane", "floor", "terrain", "land"
        };

        static readonly string[] RejectedNameFragments =
        {
            "background", "skybox", "sky", "backdrop", "ui", "canvas", "camera"
        };

        public static SceneGroundInfo Resolve(Transform userAssigned = null) =>
            ResolveInternal(userAssigned, fullWorldTerrainFirst: false);

        /// <summary>FullWorld: never anchor on Environment/Grid modular rooms — terrain or portal instead.</summary>
        public static SceneGroundInfo ResolveForFullWorld(Transform userAssigned = null) =>
            ResolveInternal(userAssigned, fullWorldTerrainFirst: true);

        public static void RefreshSurface(SceneGroundInfo info) => ComputeSurface(info);

        /// <summary>Copies resolved anchor/terrain into an existing ground info object (caller still holds same reference).</summary>
        public static void ApplyGround(SceneGroundInfo target, SceneGroundInfo source)
        {
            if (target == null || source == null)
                return;

            target.Anchor = source.Anchor;
            target.Terrain = source.Terrain;
            target.SurfaceY = source.SurfaceY;
            target.Bounds = source.Bounds;
        }

        /// <summary>FullWorld: demote Grid tag, clear stored Grid ground, re-resolve to terrain or portal.</summary>
        public static SceneGroundInfo EnforceFullWorldGround(SceneGroundInfo current)
        {
            SurfaceTerrainBlockoutLayoutAuthor.DemoteModularGridGroundTag();
            if (IsModularKitBlockoutGrid(current?.Anchor) || IsInvalidFullWorldSurfaceGround(current?.Anchor))
                ClearStoredGround();

            var resolved = ResolveForFullWorld(current?.Anchor);
            if (IsInvalidFullWorldSurfaceGround(resolved.Anchor))
            {
                resolved.Anchor = ResolveFullWorldFallbackAnchor(resolved.Terrain);
                ComputeSurface(resolved);
            }

            return resolved;
        }

        static SceneGroundInfo ResolveInternal(Transform userAssigned, bool fullWorldTerrainFirst)
        {
            var info = new SceneGroundInfo();

            if (fullWorldTerrainFirst && IsInvalidFullWorldSurfaceGround(userAssigned))
                userAssigned = null;

            if (IsValidGroundAnchor(userAssigned))
                info.Anchor = userAssigned;
            else
            {
                var stored = LoadAssignedGround();
                if (fullWorldTerrainFirst && IsModularKitBlockoutGrid(stored))
                    ClearStoredGround();
                else if (fullWorldTerrainFirst && IsInvalidFullWorldSurfaceGround(stored))
                    ClearStoredGround();
                else if (IsValidGroundAnchor(stored))
                    info.Anchor = stored;
            }

            if (info.Anchor == null)
                info.Anchor = FindNamedGroundObject();

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

            if (fullWorldTerrainFirst)
            {
                PreferSurfaceTerrainMainForFullWorld(info);
                if (IsInvalidFullWorldSurfaceGround(info.Anchor))
                    info.Anchor = ResolveFullWorldFallbackAnchor(info.Terrain);
            }

            ComputeSurface(info);
            LogGroundAnchorResolution(info, fullWorldTerrainFirst ? "FullWorld" : "Standard");
            return info;
        }

        /// <summary>Scene object named <see cref="PreferredGroundObjectName"/> or tagged Ground (not modular Grid).</summary>
        public static bool TryFindSceneGroundTransform(out Transform ground, out string source)
        {
            ground = FindNamedGroundObject();
            if (ground != null)
            {
                source = $"named:{ground.name}";
                return true;
            }

            ground = FindByGroundTag();
            if (ground != null)
            {
                source = $"tag:Ground ({ground.name})";
                return true;
            }

            source = string.Empty;
            return false;
        }

        public static void LogGroundAnchorResolution(SceneGroundInfo info, string context)
        {
            if (info == null)
                return;

            if (info.HasAnchor)
            {
                var isTerrain = info.Anchor.GetComponent<UnityEngine.Terrain>() != null;
                var tag = info.Anchor.CompareTag("Ground") ? "Ground-tagged" : "untagged";
                Debug.Log(
                    $"[Surface] Grid anchor — {context}: using '{info.Anchor.name}' ({tag}, " +
                    $"terrain={isTerrain}) at ({info.Anchor.position.x:F1}, {info.SurfaceY:F1}, {info.Anchor.position.z:F1}).");
                return;
            }

            Debug.LogWarning(
                $"[Surface] Grid anchor — {context}: no Ground object found — fallback to world origin / largest surface.");
        }

        static Transform FindNamedGroundObject()
        {
            if (!ActiveSceneUtility.HasValidActiveScene)
                return null;

            Transform exact = null;
            Transform caseInsensitive = null;

            foreach (var root in ActiveSceneUtility.ActiveScene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (IsModularKitBlockoutGrid(t) || IsInvalidFullWorldSurfaceGround(t))
                        continue;

                    if (t.name == PreferredGroundObjectName)
                        return t;

                    if (caseInsensitive == null &&
                        string.Equals(t.name, PreferredGroundObjectName, StringComparison.OrdinalIgnoreCase))
                        caseInsensitive = t;
                }
            }

            return exact ?? caseInsensitive;
        }

        static bool IsSceneWalkSurfaceAnchor(Transform anchor) =>
            anchor != null &&
            !IsInvalidFullWorldSurfaceGround(anchor) &&
            anchor.GetComponent<UnityEngine.Terrain>() == null &&
            (anchor.CompareTag("Ground") ||
             string.Equals(anchor.name, PreferredGroundObjectName, StringComparison.OrdinalIgnoreCase) ||
             IsValidGroundAnchor(anchor));

        public static bool IsModularKitBlockoutGrid(Transform t) =>
            SurfaceTerrainBlockoutLayoutAuthor.IsModularKitBlockoutGrid(t);

        /// <summary>
        /// FullWorld surface must not anchor on cave route meshes, tilted floors, or modular Grid.
        /// Parenting Environment to RouteTerrainFloor tilts every terrain tile in world space.
        /// </summary>
        public static bool IsInvalidFullWorldSurfaceGround(Transform t)
        {
            if (t == null)
                return true;

            if (IsModularKitBlockoutGrid(t))
                return true;

            if (IsUnderCaveSystem(t))
                return true;

            var lower = t.name.ToLowerInvariant();
            if (lower.Contains("routeterrainfloor", StringComparison.Ordinal) ||
                lower.Contains("routeterrainceiling", StringComparison.Ordinal) ||
                lower.Contains("layoutwalkfloor", StringComparison.Ordinal))
                return true;

            if (Vector3.Dot(t.up, Vector3.up) < 0.995f)
                return true;

            return false;
        }

        public static bool IsAxisAlignedGroundAnchor(Transform t) =>
            t != null && Vector3.Dot(t.up, Vector3.up) >= 0.995f;

        static Transform ResolveFullWorldFallbackAnchor(UnityEngine.Terrain existingTerrain)
        {
            if (existingTerrain != null)
                return existingTerrain.transform;

            var portal = GameObject.Find("PortalFive");
            if (portal != null)
                return portal.transform;

            var env = GameObject.Find(EnvironmentRoot.DefaultName);
            if (env != null)
                return env.transform;

            return FindLargestWalkableSurface();
        }

        public static void ClearStoredGround() =>
            EditorPrefs.DeleteKey(EnvironmentKitSettings.GroundObjectKey);

        /// <summary>
        /// FullWorld builds on <see cref="Blockout.SurfaceTerrainTileExpansion.MainTerrainName"/> — not modular Grid blockout.
        /// </summary>
        static void PreferSurfaceTerrainMainForFullWorld(SceneGroundInfo info)
        {
            if (info == null)
                return;

            var main = FindTerrainByName(SurfaceTerrainTileExpansion.MainTerrainName);
            if (main == null)
                return;

            info.Terrain = main;
            if (IsSceneWalkSurfaceAnchor(info.Anchor))
                return;

            if (info.Anchor == null ||
                IsInvalidFullWorldSurfaceGround(info.Anchor) ||
                string.Equals(info.Anchor.name, "Grid", StringComparison.OrdinalIgnoreCase) ||
                info.Anchor.GetComponent<UnityEngine.Terrain>() != null)
            {
                info.Anchor = main.transform;
            }
        }

        static UnityEngine.Terrain FindTerrainByName(string terrainName)
        {
            if (!ActiveSceneUtility.HasValidActiveScene || string.IsNullOrEmpty(terrainName))
                return null;

            foreach (var root in ActiveSceneUtility.ActiveScene.GetRootGameObjects())
            {
                foreach (var terrain in root.GetComponentsInChildren<UnityEngine.Terrain>(true))
                {
                    if (terrain != null && terrain.name == terrainName)
                        return terrain;
                }
            }

            return null;
        }

        public static bool IsValidGroundAnchor(Transform candidate)
        {
            if (candidate == null)
                return false;

            if (IsModularKitBlockoutGrid(candidate))
                return false;

            if (IsUnderCaveSystem(candidate))
                return false;

            var lower = candidate.name.ToLowerInvariant();
            if (lower.Contains("routeterrainfloor", StringComparison.Ordinal) ||
                lower.Contains("routeterrainceiling", StringComparison.Ordinal))
                return false;

            if (candidate.CompareTag("Ground"))
                return true;

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
                    $"[Environment Kit] '{ground?.name}' is not a valid FullWorld ground anchor. Tag SurfaceTerrainMain as Ground — not Environment/Grid blockout.");
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
                    if (!t.CompareTag("Ground") || IsModularKitBlockoutGrid(t) ||
                        IsInvalidFullWorldSurfaceGround(t))
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
                    if (IsModularKitBlockoutGrid(t) || IsInvalidFullWorldSurfaceGround(t))
                        continue;

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

            if (info.Terrain != null && info.Terrain.terrainData != null)
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

                    if (colliders[i] is TerrainCollider tc &&
                        tc.GetComponent<UnityEngine.Terrain>() == null)
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
