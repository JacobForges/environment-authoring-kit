using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Accurate enabled-renderer triangle totals for cave grading (index-count based, not mesh.triangles copies).
    /// </summary>
    static class CaveTriangleBudgetUtility
    {
        static readonly Dictionary<int, int> MeshTriangleCache = new();

        public static int CountMeshTriangles(Mesh mesh)
        {
            if (mesh == null)
                return 0;

            var id = UnityObjectCompat.ReferenceId(mesh);
            if (MeshTriangleCache.TryGetValue(id, out var cached))
                return cached;

            var tris = 0;
            var subMeshes = Mathf.Max(1, mesh.subMeshCount);
            for (var s = 0; s < subMeshes; s++)
            {
                var indexCount = mesh.GetIndexCount(s);
                if (indexCount > 0)
                    tris += (int)indexCount / 3;
            }

            if (tris <= 0)
                tris = mesh.triangles.Length / 3;

            MeshTriangleCache[id] = tris;
            return tris;
        }

        public static void ClearCache() => MeshTriangleCache.Clear();

        /// <summary>Matches CaveBuildQualityGrader.GradePerformance counting (mesh.triangles per enabled renderer).</summary>
        public static int EstimateEnabledTriangleCountForGrading(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var tris = 0;
            foreach (var mf in caveRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null)
                    continue;

                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled)
                    continue;

                tris += mf.sharedMesh.triangles.Length / 3;
            }

            return tris;
        }

        public static int EstimateEnabledTriangleCount(Transform caveRoot) =>
            EstimateEnabledTriangleCountForGrading(caveRoot);

        public static bool IsProtectedGameplayRenderer(GameObject go)
        {
            if (go == null)
                return false;

            var n = go.name;
            if (n == CaveEnclosureShellBuilder.FloorRootName ||
                n == CaveEnclosureShellBuilder.CeilingRootName ||
                n == CaveLayoutPrototypeGenerator.FlatFloorRootName)
                return true;

            if (n.Contains("SpawnGroundPad") || n.Contains("CaveEntrance_SpawnPoint"))
                return true;

            if (n.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                return true;

            return false;
        }
    }
}
