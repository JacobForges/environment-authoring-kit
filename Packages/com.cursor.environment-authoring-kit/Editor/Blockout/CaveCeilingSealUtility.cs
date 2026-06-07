using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Caps the cave upward samples so quality grading does not see open sky/void.</summary>
    public static class CaveCeilingSealUtility
    {
        public const string SkySealRootName = "SkySeal";

        public static int BuildAlongSpline(
            Transform parent,
            CaveSplinePath spline,
            Material rockMat,
            bool mazeMode = false)
        {
            if (parent == null || spline == null || rockMat == null || spline.TotalLength <= 0.1f)
                return 0;

            var sealRoot = EnvironmentSceneUtility.GetOrCreateChild(parent, SkySealRootName);
            CaveBuildSceneUtility.ClearChildrenFast(sealRoot);

            var spacing = mazeMode ? 3.2f : 7f;
            var count = Mathf.Max(mazeMode ? 14 : 8, Mathf.CeilToInt(spline.TotalLength / spacing));
            var placed = 0;

            for (var i = 0; i < count; i++)
            {
                var t = count <= 1 ? 0f : i / (float)(count - 1);
                var sample = spline.SampleAtNormalized(t);
                placed += PlaceSealPanel(sealRoot, rockMat, $"SkySeal_{i:D3}_C", sample, 0f, mazeMode);
                if (mazeMode)
                {
                    placed += PlaceSealPanel(sealRoot, rockMat, $"SkySeal_{i:D3}_L", sample, -sample.RadiusX * 0.85f, mazeMode);
                    placed += PlaceSealPanel(sealRoot, rockMat, $"SkySeal_{i:D3}_R", sample, sample.RadiusX * 0.85f, mazeMode);
                }
            }

            return placed;
        }

        static int PlaceSealPanel(
            Transform parent,
            Material rockMat,
            string name,
            CaveSplineSample sample,
            float lateralOffset,
            bool mazeMode)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Cave Sky Seal");
            go.name = name;
            go.transform.SetParent(parent, false);

            var heightAbove = mazeMode ? sample.RadiusY + 2.8f : sample.RadiusY + 3.6f;
            go.transform.localPosition =
                sample.Position +
                sample.Right * lateralOffset +
                sample.Up * heightAbove;
            go.transform.localRotation = Quaternion.LookRotation(sample.Tangent, sample.Up);

            var width = mazeMode ? sample.RadiusX * 1.55f : sample.RadiusX * 2.6f;
            var depth = mazeMode ? 6.5f : 8.2f;
            var thickness = mazeMode ? 1.8f : 1.2f;
            go.transform.localScale = new Vector3(Mathf.Max(4f, width), thickness, depth);

            var col = go.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = rockMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }

            return 1;
        }
    }
}
