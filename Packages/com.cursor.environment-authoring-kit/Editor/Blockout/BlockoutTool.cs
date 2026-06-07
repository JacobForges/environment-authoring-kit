using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    [InitializeOnLoad]
    public static class BlockoutTool
    {
        public static bool Active;

        static BlockoutTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        public static void PlacePrimitive(BlockoutPrimitiveKind kind, Vector3 position, Quaternion rotation, Transform parent, string undoLabel)
        {
            var go = CreatePrimitive(kind);
            Undo.RegisterCreatedObjectUndo(go, undoLabel);
            go.transform.SetParent(parent, false);
            go.transform.position = Snap(position);
            go.transform.rotation = rotation;
            AssignMaterial(go, kind);
        }

        public static void GenerateLayout(BlockoutLayoutKind layout, Transform parent, int seed)
        {
            var blockoutRoot = EnvironmentSceneUtility.GetOrCreateChild(parent, "Blockout");
            var rng = new System.Random(seed);

            switch (layout)
            {
                case BlockoutLayoutKind.Arena:
                    PlacePrimitive(BlockoutPrimitiveKind.Plane, Vector3.zero, Quaternion.identity, blockoutRoot, "Blockout Arena");
                    PlacePrimitive(BlockoutPrimitiveKind.Wall, new Vector3(0f, 1.5f, 12f), Quaternion.identity, blockoutRoot, "Blockout Wall");
                    PlacePrimitive(BlockoutPrimitiveKind.Wall, new Vector3(0f, 1.5f, -12f), Quaternion.identity, blockoutRoot, "Blockout Wall");
                    break;
                case BlockoutLayoutKind.Paths:
                    for (var i = -3; i <= 3; i++)
                    {
                        PlacePrimitive(
                            BlockoutPrimitiveKind.Plane,
                            new Vector3(i * 4f, 0.05f, 0f),
                            Quaternion.identity,
                            blockoutRoot,
                            "Blockout Path");
                    }
                    break;
                case BlockoutLayoutKind.Rooms:
                    for (var x = 0; x < 2; x++)
                    {
                        for (var z = 0; z < 2; z++)
                        {
                            var offset = new Vector3(x * 10f + (float)rng.NextDouble(), 0f, z * 10f);
                            PlacePrimitive(BlockoutPrimitiveKind.Cube, offset + Vector3.up, Quaternion.identity, blockoutRoot, "Blockout Room");
                        }
                    }
                    break;
            }
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!Active)
                return;

            var e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.ScrollWheel && e.control)
            {
                EnvironmentKitSettings.GridSnapSize += e.delta.y > 0 ? -0.25f : 0.25f;
                e.Use();
                view.Repaint();
            }

            Handles.BeginGUI();
            GUI.Label(new Rect(12f, 8f, 400f, 20f), $"Blockout | Grid: {EnvironmentKitSettings.GridSnapSize:0.##}m | Ctrl+Scroll to resize");
            Handles.EndGUI();

            if (e.type != EventType.MouseDown || e.button != 0 || e.alt)
                return;

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 5000f))
            {
                hit.point = Snap(ray.origin + ray.direction * 10f);
                hit.normal = Vector3.up;
            }

            e.Use();
            var root = EnvironmentSceneUtility.GetOrCreateRoot().transform;
            var blockoutRoot = EnvironmentSceneUtility.GetOrCreateChild(root, "Blockout");
            PlacePrimitive(BlockoutSettings.SelectedPrimitive, hit.point, Quaternion.LookRotation(hit.normal), blockoutRoot, "Blockout Place");
            EnvironmentSceneUtility.MarkSceneDirty();
        }

        static GameObject CreatePrimitive(BlockoutPrimitiveKind kind)
        {
            return kind switch
            {
                BlockoutPrimitiveKind.Ramp => CreateScaledCube(new Vector3(4f, 1f, 4f), "Ramp"),
                BlockoutPrimitiveKind.Cylinder => GameObject.CreatePrimitive(PrimitiveType.Cylinder),
                BlockoutPrimitiveKind.Plane => CreateScaledCube(new Vector3(6f, 0.1f, 6f), "Plane"),
                BlockoutPrimitiveKind.Wall => CreateScaledCube(new Vector3(8f, 3f, 0.2f), "Wall"),
                _ => GameObject.CreatePrimitive(PrimitiveType.Cube)
            };
        }

        static GameObject CreateScaledCube(Vector3 scale, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.localScale = scale;
            return go;
        }

        static Vector3 Snap(Vector3 position)
        {
            var g = EnvironmentKitSettings.GridSnapSize;
            return new Vector3(
                Mathf.Round(position.x / g) * g,
                Mathf.Round(position.y / g) * g,
                Mathf.Round(position.z / g) * g);
        }

        static void AssignMaterial(GameObject go, BlockoutPrimitiveKind kind)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return;

            var color = kind switch
            {
                BlockoutPrimitiveKind.Ramp => BlockoutSettings.RampColor,
                BlockoutPrimitiveKind.Cylinder => BlockoutSettings.CylinderColor,
                BlockoutPrimitiveKind.Plane => BlockoutSettings.PlaneColor,
                BlockoutPrimitiveKind.Wall => BlockoutSettings.WallColor,
                _ => BlockoutSettings.CubeColor
            };

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            renderer.sharedMaterial = mat;
        }
    }
}
