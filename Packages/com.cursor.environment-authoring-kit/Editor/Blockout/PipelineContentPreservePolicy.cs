#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Pipeline-wide rule: never delete authored scene work when repair/skip is possible.
    /// From mid-pipeline onward, new steps merge into existing content (integrate-only).
    /// </summary>
    public static class PipelineContentPreservePolicy
    {
        static bool _integrateOnlyFromMidPipeline;

        public static bool PreferRepairOverReplace => true;

        /// <summary>
        /// True after ~half the FullWorld build (flat grid terraform done) or cave World stage —
        /// destructive clears and purges are blocked for meaningful content.
        /// </summary>
        public static bool IntegrateOnlyActive => _integrateOnlyFromMidPipeline;

        public static void ResetForNewBuildSession() => _integrateOnlyFromMidPipeline = false;

        public static void MarkMidPipelineIntegrateMode(string reason)
        {
            if (_integrateOnlyFromMidPipeline)
                return;

            _integrateOnlyFromMidPipeline = true;
            CaveBuildEditorLog.LogSurface(
                $"[PipelineIntegrate] Mid-pipeline merge mode ON — {reason}. " +
                "New work layers onto existing scene content (no delete of authored work).",
                forceUnityConsole: true);
        }

        public static bool HasMeaningfulContent(GameObject go)
        {
            if (go == null)
                return false;

            if (go.GetComponent<HollowTitanLandmarkBuildData>() != null)
                return true;

            if (go.GetComponent<Terrain>()?.terrainData != null)
                return true;

            if (go.GetComponent<MeshRenderer>() != null || go.GetComponent<MeshFilter>() != null)
                return true;

            if (go.transform.childCount > 0)
                return true;

            return go.GetComponents<Component>().Length > 2;
        }

        public static bool HasMeaningfulContent(Transform t) =>
            t != null && HasMeaningfulContent(t.gameObject);

        /// <summary>False when integrate mode must keep this object (caller should skip Destroy).</summary>
        public static bool ShouldAllowDestroy(GameObject go, string context)
        {
            if (go == null)
                return false;

            if (!IntegrateOnlyActive)
                return true;

            if (!HasMeaningfulContent(go))
                return true;

            CaveBuildEditorLog.LogSurface(
                $"[PipelineIntegrate] Keeping '{go.name}'" +
                (string.IsNullOrEmpty(context) ? "" : $" ({context})") +
                " — merge in place, no delete.",
                forceUnityConsole: false);
            return false;
        }

        /// <summary>When true, ClearChildren should be skipped — add/update children instead.</summary>
        public static bool PreferMergeChildren(Transform parent, string label)
        {
            if (!IntegrateOnlyActive || parent == null || parent.childCount == 0)
                return false;

            CaveBuildEditorLog.LogSurface(
                $"[PipelineIntegrate] Keeping {parent.childCount} child(ren) under '{parent.name}'" +
                (string.IsNullOrEmpty(label) ? "" : $" ({label})") +
                " — additive merge.",
                forceUnityConsole: false);
            return true;
        }

        /// <summary>True when an existing scene root should be kept (log + skip recreate).</summary>
        public static bool TryPreserveSceneRoot(string rootName, out GameObject root, string context = null)
        {
            root = GameObject.Find(rootName);
            if (!HasMeaningfulContent(root))
                return false;

            CaveBuildEditorLog.LogSurface(
                $"[PipelinePreserve] Keeping {rootName}" +
                (string.IsNullOrEmpty(context) ? "" : $" ({context})") +
                " — repair in place, no delete.",
                forceUnityConsole: false);
            return true;
        }

        public static bool TryPreserveTransform(Transform existing, string label)
        {
            if (!HasMeaningfulContent(existing))
                return false;

            CaveBuildEditorLog.LogSurface(
                $"[PipelinePreserve] Keeping {label} — repair in place, no delete.",
                forceUnityConsole: false);
            return true;
        }

        public static bool TryRepairFullWorldTerrainGrid(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request) =>
            SurfaceTerrainTileExpansion.TryRepairFullWorldGridInPlace(mainTerrain, ground, request);

        public static bool ShouldReplaceGeneratedSurfaceWorld(
            bool wouldReplace,
            WorldGenerationRequest request)
        {
            if (!wouldReplace)
                return false;

            if (IntegrateOnlyActive)
            {
                CaveBuildEditorLog.LogSurface(
                    "[PipelineIntegrate] Surface world replace blocked — updating in place.",
                    forceUnityConsole: false);
                return false;
            }

            if (request != null &&
                request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                TryRepairFullWorldTerrainGrid(
                    SurfaceTerrainTileExpansion.FindMainTerrainInScene(),
                    SceneGroundResolver.ResolveForFullWorld(),
                    request))
                return false;

            var ground = SceneGroundResolver.ResolveForFullWorld();
            var envRoot = ground?.Anchor != null
                ? EnvironmentSceneUtility.GetOrCreateRoot(ground)
                : null;
            var surfaceRoot = envRoot != null
                ? envRoot.transform.Find(SurfaceWorldPaths.RootName)
                : null;

            if (TryPreserveTransform(surfaceRoot, SurfaceWorldPaths.RootName))
                return false;

            return wouldReplace;
        }
    }
}
#endif
