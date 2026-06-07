using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Solid walk colliders along the cave floor (CharacterController + NavMesh).</summary>
    public static class CaveWalkwayBuilder
    {
        public const string WalkFloorPrefix = "WalkFloor_";

        public static int Build(
            Transform cavesRoot,
            CaveSplinePath spline,
            float widthMeters = 5.2f,
            float spacingMeters = 2.6f,
            bool visibleMeshes = false)
        {
            if (cavesRoot == null || spline == null || spline.KnotCount < 2)
                return 0;

            var walkRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Walkways");
            ClearChildren(walkRoot);

            var count = Mathf.Max(3, Mathf.CeilToInt(spline.TotalLength / spacingMeters));
            var placed = 0;
            for (var i = 0; i < count; i++)
            {
                var dist = count <= 1 ? 0f : (i / (float)(count - 1)) * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var floorCenter = sample.Position - sample.Up * (sample.RadiusY * 0.82f);

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(go, "Walk Floor");
                go.name = $"{WalkFloorPrefix}{i:D3}";
                go.transform.SetParent(walkRoot, false);
                go.transform.localPosition = floorCenter + sample.Up * 0.11f;
                go.transform.localRotation = Quaternion.LookRotation(sample.Tangent, sample.Up);
                go.transform.localScale = new Vector3(widthMeters, 0.24f, spacingMeters * 0.96f);

                var col = go.GetComponent<BoxCollider>();
                if (col != null)
                    col.isTrigger = false;

                if (visibleMeshes)
                    CaveFloorSafetyUtility.EnsureWalkFloorRenderer(go);
                else
                {
                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        Object.DestroyImmediate(renderer);
                    var meshFilter = go.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                        Object.DestroyImmediate(meshFilter);
                }

                if (go.GetComponent<CaveWalkableMarker>() == null)
                    go.AddComponent<CaveWalkableMarker>();

                go.isStatic = true;
                placed++;
            }

            return placed;
        }

        public static bool IsMazeCave(Transform cavesRoot) =>
            cavesRoot != null && (
                cavesRoot.Find($"SplineMesh/{CaveMazeVolumeBuilder.MazeVolumeRootName}") != null ||
                CaveAdventureCaveGenerator.IsAdventureCave(cavesRoot));

        public static int RebuildFromAuthoring(Transform cavesRoot)
        {
            if (cavesRoot == null)
                return 0;

            if (IsMazeCave(cavesRoot))
                return CaveMazeWalkwayBuilder.RebuildFromAuthoring(cavesRoot);

            var authoring = cavesRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return 0;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            return Build(cavesRoot, spline, visibleMeshes: true);
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
