#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Bakes walkable NavMesh for open-sky surface (terrain + trails); merges into final cave bake.</summary>
    public static class SurfaceNavMeshBaker
    {
        static NavMeshDataInstance _surfaceInstance;
        static bool _surfaceInstanceValid;

        public static void ClearSurfaceNavMeshData(string reason = null)
        {
            if (_surfaceInstanceValid && _surfaceInstance.valid)
                NavMesh.RemoveNavMeshData(_surfaceInstance);
            _surfaceInstanceValid = false;
            _surfaceInstance = default;
            if (!string.IsNullOrEmpty(reason))
                CaveBuildPipelineLog.Info($"Surface NavMesh cleared — {reason}", "Surface-NavMesh");
        }

        public static int AppendWalkableSources(List<NavMeshBuildSource> sources, ref Bounds bounds)
        {
            var env = Object.FindAnyObjectByType<EnvironmentRoot>();
            var terrain = env != null ? env.GetComponentInChildren<Terrain>() : null;
            terrain ??= Object.FindAnyObjectByType<Terrain>();
            var added = AppendSurfaceTerrainSources(sources, ref bounds, terrain);
            added += AppendWalkableMeshSources(sources, ref bounds);
            return added;
        }

        /// <summary>All Environment Kit surface tiles (main + neighbors) — required for trail NavMesh probes on multi-tile grids.</summary>
        public static int AppendSurfaceTerrainSources(
            List<NavMeshBuildSource> sources,
            ref Bounds bounds,
            Terrain mainTerrain)
        {
            var added = 0;
            if (mainTerrain == null)
                return 0;

            foreach (var terrain in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (terrain?.terrainData == null)
                    continue;

                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Terrain,
                    sourceObject = terrain.terrainData,
                    transform = Matrix4x4.TRS(terrain.transform.position, Quaternion.identity, Vector3.one),
                    area = 0,
                });
                bounds.Encapsulate(
                    new Bounds(
                        terrain.transform.position + terrain.terrainData.size * 0.5f,
                        terrain.terrainData.size));
                added++;
            }

            return added;
        }

        static int AppendWalkableMeshSources(List<NavMeshBuildSource> sources, ref Bounds bounds)
        {
            var env = Object.FindAnyObjectByType<EnvironmentRoot>();
            var surfaceRoot = env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
            return surfaceRoot == null
                ? 0
                : AppendWalkableMeshSourcesFromRoot(sources, ref bounds, surfaceRoot);
        }

        static int AppendWalkableMeshSourcesFromRoot(
            List<NavMeshBuildSource> sources,
            ref Bounds bounds,
            Transform surfaceRoot)
        {
            var added = 0;
            if (surfaceRoot == null)
                return added;

            foreach (var meshFilter in surfaceRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh == null || !IsWalkableSurfaceObject(meshFilter.gameObject))
                    continue;

                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = meshFilter.sharedMesh,
                    transform = meshFilter.transform.localToWorldMatrix,
                    area = 0,
                });
                var rend = meshFilter.GetComponent<Renderer>();
                if (rend != null)
                    bounds.Encapsulate(rend.bounds);
                added++;
            }

            return added;
        }

        public static bool BakePhase(Transform environmentRoot, Terrain terrain, Transform surfaceRoot, out string message) =>
            BakePhase(environmentRoot, terrain, surfaceRoot, null, out message);

        public static bool BakePhase(
            Transform environmentRoot,
            Terrain terrain,
            Transform surfaceRoot,
            IReadOnlyList<Vector3> trailWaypoints,
            out string message)
        {
            message = null;
            if (environmentRoot == null)
            {
                message = "No environment root.";
                return false;
            }

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = trailWaypoints != null && trailWaypoints.Count > 0 ? 0.75f : 0.5f;
            settings.agentSlope = trailWaypoints != null && trailWaypoints.Count > 0 ? 48f : 42f;

            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(environmentRoot.position, Vector3.one * 40f);

            var terrainSources = AppendSurfaceTerrainSources(sources, ref bounds, terrain);
            var meshSources = surfaceRoot != null
                ? AppendWalkableMeshSourcesFromRoot(sources, ref bounds, surfaceRoot)
                : 0;

            if (sources.Count == 0)
            {
                ClearSurfaceNavMeshData("no walkable sources");
                message = "Surface NavMesh skipped — no walkable sources.";
                return false;
            }

            EncapsulateTrailWaypoints(ref bounds, trailWaypoints);
            bounds.Expand(24f);
            var data = NavMeshBuilder.BuildNavMeshData(
                settings,
                sources,
                bounds,
                Vector3.zero,
                Quaternion.identity);

            if (data == null)
            {
                message = "NavMesh build failed for surface.";
                return false;
            }

            if (_surfaceInstanceValid && _surfaceInstance.valid)
                NavMesh.RemoveNavMeshData(_surfaceInstance);

            _surfaceInstance = NavMesh.AddNavMeshData(data);
            _surfaceInstanceValid = true;

            message =
                $"Surface NavMesh phase ({sources.Count} sources: {terrainSources} terrain tile(s), {meshSources} trail/road mesh).";
            CaveBuildPipelineLog.Info(message, "Surface-NavMesh");
            return true;
        }

        static void EncapsulateTrailWaypoints(ref Bounds bounds, IReadOnlyList<Vector3> trailWaypoints)
        {
            if (trailWaypoints == null || trailWaypoints.Count == 0)
                return;

            for (var i = 0; i < trailWaypoints.Count; i++)
            {
                var p = trailWaypoints[i];
                bounds.Encapsulate(new Vector3(p.x, p.y + 14f, p.z));
                bounds.Encapsulate(new Vector3(p.x, p.y - 4f, p.z));
            }
        }

        static bool IsWalkableSurfaceObject(GameObject go)
        {
            if (go == null)
                return false;

            var n = go.name;
            if (n.StartsWith("Water") || n.Contains("Pond") || n.Contains("River"))
                return false;
            if (n.StartsWith("Mountain") || n.Contains("PeakMarker"))
                return false;
            if (n.Contains("CaveOpening") || n.Contains("Opening_"))
                return false;
            if (go.GetComponent<SurfaceCaveOpeningMarker>() != null)
                return false;

            return n.StartsWith("Trail") || n.StartsWith("Road") || n.Contains("Walk");
        }
    }
}
#endif
