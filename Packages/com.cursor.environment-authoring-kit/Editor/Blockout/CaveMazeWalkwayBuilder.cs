using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Walk colliders snapped to maze floor surfaces (NavMesh + CharacterController).</summary>
    public static class CaveMazeWalkwayBuilder
    {
        public static int Build(Transform cavesRoot, CaveMazeLayout layout)
        {
            if (cavesRoot == null || layout == null || layout.SolutionPath.Count == 0)
                return 0;

            var walkRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Walkways");
            for (var i = walkRoot.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(walkRoot.GetChild(i).gameObject);

            var interior = layout.CellSize - 1.3f;
            var mat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var placed = 0;
            for (var idx = 0; idx < layout.SolutionPath.Count; idx++)
            {
                var cell = layout.SolutionPath[idx];
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var surface = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var forward = Vector3.forward;
                if (idx < layout.SolutionPath.Count - 1)
                {
                    var nextCell = layout.SolutionPath[idx + 1];
                    var next = layout.CellToLocal(nextCell.x, nextCell.y);
                    forward = (next - surface).normalized;
                    if (forward.sqrMagnitude < 0.01f)
                        forward = Vector3.forward;
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(go, "Maze Walk Floor");
                go.name = $"{CaveWalkwayBuilder.WalkFloorPrefix}Maze_{cell.x}_{cell.y}";
                go.transform.SetParent(walkRoot, false);
                go.transform.localPosition = surface + Vector3.up * 0.08f;
                go.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
                go.transform.localScale = new Vector3(interior, 0.42f, interior);

                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null && mat != null)
                {
                    renderer.sharedMaterial = mat;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }

                var col = go.GetComponent<BoxCollider>();
                if (col != null)
                    col.isTrigger = false;

                if (go.GetComponent<CaveWalkableMarker>() == null)
                    go.AddComponent<CaveWalkableMarker>();

                go.isStatic = true;
                placed++;
            }

            for (var idx = 0; idx < layout.SolutionPath.Count - 1; idx++)
            {
                var a = layout.SolutionPath[idx];
                var b = layout.SolutionPath[idx + 1];
                if (layout.IsJumpGap(a.x, a.y) || layout.IsJumpGap(b.x, b.y))
                    continue;
                var surfaceA = layout.GetFloorSurfaceLocal(a.x, a.y);
                var surfaceB = layout.GetFloorSurfaceLocal(b.x, b.y);
                var mid = (surfaceA + surfaceB) * 0.5f + Vector3.up * 0.12f;
                var forward = (surfaceB - surfaceA).normalized;
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.forward;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(go, "Maze Walk Mid");
                go.name = $"{CaveWalkwayBuilder.WalkFloorPrefix}MazeMid_{idx:D2}";
                go.transform.SetParent(walkRoot, false);
                go.transform.localPosition = mid;
                go.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
                go.transform.localScale = new Vector3(interior * 0.92f, 0.4f, interior * 0.92f);

                var midRenderer = go.GetComponent<MeshRenderer>();
                if (midRenderer != null && mat != null)
                {
                    midRenderer.sharedMaterial = mat;
                    midRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    midRenderer.receiveShadows = true;
                }

                var midCol = go.GetComponent<BoxCollider>();
                if (midCol != null)
                    midCol.isTrigger = false;

                if (go.GetComponent<CaveWalkableMarker>() == null)
                    go.AddComponent<CaveWalkableMarker>();

                go.isStatic = true;
                placed++;
            }

            CaveFloorSafetyUtility.EnsureVisibleWalkways(cavesRoot);
            return placed;
        }

        public static int RebuildFromAuthoring(Transform cavesRoot)
        {
            if (cavesRoot == null)
                return 0;

            if (CaveAdventureCaveGenerator.IsAdventureCave(cavesRoot))
            {
                CaveFloorSafetyUtility.Apply(cavesRoot);
                return CountWalkFloors(cavesRoot);
            }

            var meta = cavesRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
            {
                CaveFloorSafetyUtility.EnsureVisibleWalkways(cavesRoot);
                return CountWalkFloors(cavesRoot);
            }

            var layout = CaveMazeLayoutGenerator.Generate(
                meta.seed,
                meta.tunnelSegments,
                meta.chamberCount);
            var placed = Build(cavesRoot, layout);
            CaveFloorSafetyUtility.EnsureVisibleWalkways(cavesRoot);
            return placed;
        }

        public static int CountWalkFloors(Transform cavesRoot)
        {
            var walk = cavesRoot != null ? cavesRoot.Find("Walkways") : null;
            if (walk == null)
                return 0;

            var count = 0;
            foreach (Transform c in walk)
            {
                if (c.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                    count++;
            }

            return count;
        }
    }
}
