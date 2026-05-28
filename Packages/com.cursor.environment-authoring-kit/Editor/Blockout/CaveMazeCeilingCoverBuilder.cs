using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Continuous ceiling over the solution path — closes grader sky samples without floating SkySeal slabs.</summary>
    public static class CaveMazeCeilingCoverBuilder
    {
        public const string RootName = "CeilingCover";

        public static int Build(Transform mazeVolumeRoot, CaveMazeLayout layout, Material rockMat)
        {
            if (mazeVolumeRoot == null || layout == null)
                return 0;

            if (rockMat == null)
                rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

            var existing = mazeVolumeRoot.Find(RootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(RootName);
            CaveEditorUndo.RegisterCreated(root, "Maze Ceiling Cover");
            root.transform.SetParent(mazeVolumeRoot, false);

            var placed = 0;
            var thickness = 1.15f;
            var span = layout.CellSize * 0.98f;

            foreach (var cell in layout.SolutionPath)
            {
                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var clearance = layout.GetCeilingClearanceAt(cell.x, cell.y);
                var y = floor.y + clearance - thickness * 0.5f;
                placed += CreatePanel(
                    root.transform,
                    $"Cover_{cell.x}_{cell.y}",
                    new Vector3(floor.x, y, floor.z),
                    span,
                    thickness,
                    rockMat);
            }

            for (var i = 0; i < layout.SolutionPath.Count - 1; i++)
            {
                var a = layout.SolutionPath[i];
                var b = layout.SolutionPath[i + 1];
                var ca = layout.CellToLocal(a.x, a.y);
                var cb = layout.CellToLocal(b.x, b.y);
                var mid = (ca + cb) * 0.5f;
                var floorA = layout.GetFloorSurfaceLocal(a.x, a.y);
                var floorB = layout.GetFloorSurfaceLocal(b.x, b.y);
                var clearance = Mathf.Max(
                    layout.GetCeilingClearanceAt(a.x, a.y),
                    layout.GetCeilingClearanceAt(b.x, b.y));
                var y = Mathf.Lerp(floorA.y, floorB.y, 0.5f) + clearance - thickness * 0.5f;
                var forward = (cb - ca).normalized;
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.forward;

                placed += CreateBridge(root.transform, $"Bridge_{i:D2}", mid, y, span, thickness, forward, rockMat);
            }

            if (layout.PathKnots != null)
            {
                for (var k = 0; k < layout.PathKnots.Count; k++)
                {
                    var knot = layout.PathKnots[k];
                    var y = knot.Position.y + layout.GetCeilingClearanceAt(0, 0) * 0.5f - thickness * 0.5f;
                    placed += CreatePanel(
                        root.transform,
                        $"KnotCap_{k:D3}",
                        new Vector3(knot.Position.x, y, knot.Position.z),
                        span * 1.02f,
                        thickness,
                        rockMat);
                }
            }

            return placed;
        }

        static int CreatePanel(
            Transform parent,
            string name,
            Vector3 localPos,
            float span,
            float thickness,
            Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Ceiling Cover");
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(span, thickness, span);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }

            var col = go.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);

            go.isStatic = true;
            return 1;
        }

        static int CreateBridge(
            Transform parent,
            string name,
            Vector3 mid,
            float y,
            float span,
            float thickness,
            Vector3 forward,
            Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Ceiling Bridge");
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(mid.x, y, mid.z);
            go.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            go.transform.localScale = new Vector3(span * 0.94f, thickness, span * 0.94f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }

            var col = go.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);

            go.isStatic = true;
            return 1;
        }
    }
}
