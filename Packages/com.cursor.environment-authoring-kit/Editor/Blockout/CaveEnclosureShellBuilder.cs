using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Full-build enclosure: exactly one walkable floor mesh and one ceiling mesh along the route.
    /// No per-cell slabs, PathCeiling segments, or ribbon stacks.
    /// </summary>
    public static class CaveEnclosureShellBuilder
    {
        public const string FloorRootName = "RouteTerrainFloor";
        public const string CeilingRootName = "RouteTerrainCeiling";

        const int CrossSegments = 5;
        const float NoiseScale = 0.22f;
        const float FloorNoiseAmp = 0.14f;
        const float CeilingNoiseAmp = 0.12f;

        /// <summary>UV repeats per meter along path and across width (0.25 = one tile every 4m).</summary>
        const float FloorUvTilesPerMeter = 0.25f;

        /// <summary>Removes onion offenders before (re)building floor/ceiling.</summary>
        public static int PurgeLayerOffenders(Transform geometryRoot)
        {
            if (geometryRoot == null)
                return 0;

            var removed = 0;
            removed += DestroyIfPresent(geometryRoot, CaveAdventureShellBuilder.ShellRootName);
            removed += DestroyIfPresent(geometryRoot, "SkyRockCap");
            removed += DestroyIfPresent(geometryRoot, CaveMazeCeilingCoverBuilder.RootName);
            removed += DestroyNamedChildren(geometryRoot, "PathCeiling_");
            removed += DestroyNamedChildren(geometryRoot, "Floor_");
            removed += DestroyNamedChildren(geometryRoot, "Ceiling_");
            removed += DestroyNamedChildren(geometryRoot, "Cavern_");
            removed += DestroyNamedChildren(geometryRoot, "Entrance_Shaft_");
            removed += DestroyNamedChildren(geometryRoot, "Outer_");
            return removed;
        }

        /// <summary>One floor + one ceiling mesh; hides flat PathPlatforms.</summary>
        public static int Build(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material floorMat,
            Material ceilingMat,
            int seed)
        {
            if (geometryRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return 0;

            PurgeLayerOffenders(geometryRoot);
            HideFlatPlatforms(geometryRoot);

            var rng = new System.Random(seed + 509);
            var count = BuildSurface(geometryRoot, layout, floorMat, isCeiling: false, rng);
            if (ceilingMat != null)
                count += BuildSurface(geometryRoot, layout, ceilingMat, isCeiling: true, rng);
            return count;
        }

        public static int BuildFloorOnly(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material floorMat,
            int seed)
        {
            if (geometryRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return 0;

            PurgeLayerOffenders(geometryRoot);
            DestroyCeiling(geometryRoot);
            HideFlatPlatforms(geometryRoot);
            var rng = new System.Random(seed + 509);
            return BuildSurface(geometryRoot, layout, floorMat, isCeiling: false, rng);
        }

        public static int EnsureSingleCeiling(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material ceilingMat,
            int seed)
        {
            if (geometryRoot == null || layout == null || ceilingMat == null)
                return 0;

            var existing = geometryRoot.Find(CeilingRootName);
            if (existing != null && existing.GetComponentInChildren<MeshRenderer>() != null)
                return 0;

            DestroyNamedChildren(geometryRoot, "PathCeiling_");
            var rng = new System.Random(seed + 611);
            return BuildSurface(geometryRoot, layout, ceilingMat, isCeiling: true, rng);
        }

        public static void DestroyCeiling(Transform geometryRoot)
        {
            if (geometryRoot == null)
                return;

            var existing = geometryRoot.Find(CeilingRootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);
        }

        /// <summary>Hides slab PathPlatforms when RouteTerrainFloor is the visible walk surface (stops onion grading).</summary>
        public static int HideRoutePlatformSlabs(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return 0;

            var floor = geometry.Find(FloorRootName);
            if (floor == null || floor.GetComponentInChildren<MeshRenderer>() == null)
                return 0;

            HideFlatPlatforms(geometry);
            return 1;
        }

        static void HideFlatPlatforms(Transform geometryRoot)
        {
            var platforms = geometryRoot.Find(CaveAdventureBlockBuilder.PlatformsRootName);
            if (platforms == null)
                return;

            foreach (var mr in platforms.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null)
                    mr.enabled = false;
            }

            foreach (var col in platforms.GetComponentsInChildren<Collider>(true))
            {
                if (col != null)
                    col.enabled = false;
            }
        }

        static int BuildSurface(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material mat,
            bool isCeiling,
            System.Random rng)
        {
            var rootName = isCeiling ? CeilingRootName : FloorRootName;
            var existing = geometryRoot.Find(rootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var samples = CollectPathSamples(layout);
            if (samples.Count < 2 || mat == null)
                return 0;

            var halfWidth = layout.PlatformSpan * 0.55f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            var arcLengthAlong = new float[samples.Count];
            for (var i = 1; i < samples.Count; i++)
                arcLengthAlong[i] = arcLengthAlong[i - 1] +
                                    Vector3.Distance(samples[i - 1].Floor, samples[i].Floor);

            for (var s = 0; s < samples.Count; s++)
            {
                var sample = samples[s];
                var forward = sample.Forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.forward;
                forward.Normalize();
                var right = Vector3.Cross(Vector3.up, forward).normalized;

                for (var c = 0; c <= CrossSegments; c++)
                {
                    var t = c / (float)CrossSegments;
                    var lateral = Mathf.Lerp(-halfWidth, halfWidth, t);
                    var basePos = sample.Floor + right * lateral;
                    var h = isCeiling
                        ? Mathf.Max(
                            layout.GetCeilingClearanceAt(sample.Cell.x, sample.Cell.y),
                            CaveMazeLayout.MinWalkClearanceMeters + 2.8f)
                        : (layout.IsCavernCell(sample.Cell.x, sample.Cell.y)
                            ? layout.CorridorHeight * 1.45f
                            : layout.CorridorHeight);
                    var y = isCeiling ? basePos.y + h : basePos.y;
                    var n = Noise2(basePos.x * NoiseScale + s * 0.17f, basePos.z * NoiseScale, rng.Next());
                    var amp = isCeiling ? CeilingNoiseAmp : FloorNoiseAmp;
                    y += (n - 0.5f) * amp * 2f;

                    verts.Add(new Vector3(basePos.x, y, basePos.z));
                    var uAcross = (lateral + halfWidth) * FloorUvTilesPerMeter;
                    var uAlong = arcLengthAlong[s] * FloorUvTilesPerMeter;
                    uvs.Add(new Vector2(uAcross, uAlong));
                }
            }

            var row = CrossSegments + 1;
            for (var s = 0; s < samples.Count - 1; s++)
            {
                for (var c = 0; c < CrossSegments; c++)
                {
                    var i0 = s * row + c;
                    var i1 = i0 + 1;
                    var i2 = i0 + row;
                    var i3 = i2 + 1;
                    if (isCeiling)
                    {
                        tris.Add(i0);
                        tris.Add(i2);
                        tris.Add(i1);
                        tris.Add(i1);
                        tris.Add(i2);
                        tris.Add(i3);
                    }
                    else
                    {
                        tris.Add(i0);
                        tris.Add(i1);
                        tris.Add(i2);
                        tris.Add(i1);
                        tris.Add(i3);
                        tris.Add(i2);
                    }
                }
            }

            var mesh = new Mesh { name = rootName };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var root = new GameObject(rootName);
            CaveEditorUndo.RegisterCreated(root, rootName);
            root.transform.SetParent(geometryRoot, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            if (!isCeiling)
            {
                var mc = root.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                if (root.GetComponent<CaveWalkableMarker>() == null)
                    root.AddComponent<CaveWalkableMarker>();
            }

            if (!isCeiling)
                PersistFloorMeshAsset(mf, rootName);

            return 1;
        }

        /// <summary>Deletes cached floor mesh so the next build regenerates UVs/geometry.</summary>
        public static void InvalidatePersistedFloorAsset()
        {
            var path = $"Assets/EnvironmentKit/Generated/{FloorRootName}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        static void PersistFloorMeshAsset(MeshFilter mf, string rootName)
        {
            var path = $"Assets/EnvironmentKit/Generated/{rootName}.asset";
            if (!System.IO.Directory.Exists("Assets/EnvironmentKit/Generated"))
                System.IO.Directory.CreateDirectory("Assets/EnvironmentKit/Generated");
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (asset != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mf.sharedMesh, path);
            AssetDatabase.SaveAssets();
            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mf.GetComponent<MeshCollider>() is MeshCollider col)
                col.sharedMesh = mf.sharedMesh;
        }

        struct PathSample
        {
            public Vector2Int Cell;
            public Vector3 Floor;
            public Vector3 Forward;
        }

        static List<PathSample> CollectPathSamples(CaveMazeLayout layout)
        {
            var list = new List<PathSample>();
            var path = layout.SolutionPath;
            for (var i = 0; i < path.Count; i++)
            {
                var cell = path[i];
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                Vector3 forward;
                if (i < path.Count - 1)
                {
                    var next = path[i + 1];
                    var nf = layout.GetFloorSurfaceLocal(next.x, next.y);
                    forward = nf - floor;
                }
                else if (i > 0)
                {
                    var prev = path[i - 1];
                    var pf = layout.GetFloorSurfaceLocal(prev.x, prev.y);
                    forward = floor - pf;
                }
                else
                    forward = Vector3.forward;

                list.Add(new PathSample { Cell = cell, Floor = floor, Forward = forward });

                if (i >= path.Count - 1)
                    continue;

                var nextCell = path[i + 1];
                if (layout.IsJumpGap(nextCell.x, nextCell.y))
                    continue;

                var nextFloor = layout.GetFloorSurfaceLocal(nextCell.x, nextCell.y);
                var midFloor = (floor + nextFloor) * 0.5f;
                var midForward = nextFloor - floor;
                list.Add(new PathSample { Cell = cell, Floor = midFloor, Forward = midForward });
            }

            return list;
        }

        static float Noise2(float x, float z, int salt)
        {
            var n = Mathf.PerlinNoise(x + salt * 0.013f, z + salt * 0.019f);
            n += Mathf.PerlinNoise(x * 2.1f + 4f, z * 2.1f + 2f) * 0.35f;
            return Mathf.Clamp01(n / 1.35f);
        }

        static int DestroyIfPresent(Transform parent, string childName)
        {
            var child = parent?.Find(childName);
            if (child == null)
                return 0;
            CaveEditorUndo.DestroyImmediate(child.gameObject);
            return 1;
        }

        static int DestroyNamedChildren(Transform parent, string namePrefix)
        {
            if (parent == null)
                return 0;

            var removed = 0;
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child == null || !child.name.StartsWith(namePrefix))
                    continue;
                CaveEditorUndo.DestroyImmediate(child.gameObject);
                removed++;
            }

            return removed;
        }
    }
}
