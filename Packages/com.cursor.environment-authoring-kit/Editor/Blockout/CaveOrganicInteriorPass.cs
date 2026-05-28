using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Interior rock ribs + entrance collar so the tube reads as enclosed geology, not a void.</summary>
    static class CaveOrganicInteriorPass
    {
        public static int Build(Transform caveRoot, CaveSplinePath spline, LavaTubePrefabCatalog catalog, System.Random rng)
        {
            if (caveRoot == null || spline == null || catalog == null || !catalog.IsValid)
                return 0;

            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot == null)
                meshRoot = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "SplineMesh");
            var ribsRoot = EnvironmentSceneUtility.GetOrCreateChild(meshRoot, "InteriorRibs");
            ClearChildren(ribsRoot);

            var placed = 0;
            var spacing = Mathf.Clamp(spline.TotalLength / 28f, 2.8f, 5.5f);
            var count = Mathf.Max(4, Mathf.CeilToInt(spline.TotalLength / spacing));

            for (var i = 0; i < count; i++)
            {
                var dist = count <= 1 ? 0f : (i / (float)(count - 1)) * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var prefab = catalog.Pick(catalog.Walls, rng) ?? catalog.Pick(catalog.Rockfalls, rng);
                if (prefab == null)
                    continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ribsRoot);
                if (go == null)
                    continue;

                CaveEditorUndo.RegisterCreated(go, "Interior Rib");
                go.name = $"InteriorRib_{i:D3}";
                var floor = sample.Position - sample.Up * (sample.RadiusY * 0.72f);
                go.transform.localPosition = floor;
                go.transform.localRotation = Quaternion.LookRotation(sample.Tangent, sample.Up);
                var ringScale = Mathf.Max(sample.RadiusX, sample.RadiusY) * 0.42f;
                go.transform.localScale = new Vector3(ringScale, ringScale * 0.35f, ringScale);

                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                placed++;
            }

            if (placed < 4)
                placed += BuildProceduralRibs(ribsRoot, spline);

            placed += BuildEntranceCollar(caveRoot, spline, catalog, rng);
            return placed;
        }

        static int BuildProceduralRibs(Transform ribsRoot, CaveSplinePath spline)
        {
            if (ribsRoot == null || spline == null || spline.TotalLength < 1f)
                return 0;

            var mat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            var spacing = Mathf.Clamp(spline.TotalLength / 24f, 4f, 7f);
            var count = Mathf.Max(6, Mathf.CeilToInt(spline.TotalLength / spacing));
            var placed = 0;

            for (var i = 0; i < count; i++)
            {
                var dist = count <= 1 ? 0f : (i / (float)(count - 1)) * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var side = sample.Right * (i % 2 == 0 ? 1f : -1f) * sample.RadiusX * 0.62f;
                var height = sample.Up * (sample.RadiusY * 0.35f);

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(go, "Procedural Interior Rib");
                go.name = $"InteriorRib_{i:D3}";
                go.transform.SetParent(ribsRoot, false);
                go.transform.localPosition = sample.Position + side + height;
                go.transform.localRotation = Quaternion.LookRotation(sample.Tangent, sample.Up);
                go.transform.localScale = new Vector3(
                    sample.RadiusX * 0.35f,
                    sample.RadiusY * 0.28f,
                    sample.RadiusX * 0.22f);

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null && mat != null)
                    mr.sharedMaterial = mat;

                var col = go.GetComponent<Collider>();
                if (col != null)
                    Object.DestroyImmediate(col);

                placed++;
            }

            return placed;
        }

        static int BuildEntranceCollar(
            Transform caveRoot,
            CaveSplinePath spline,
            LavaTubePrefabCatalog catalog,
            System.Random rng)
        {
            var entrance = caveRoot.Find("Entrance");
            if (entrance == null)
                return 0;

            var sample = spline.SampleAtDistance(Mathf.Min(3f, spline.TotalLength * 0.05f));
            var prefab = catalog.Pick(catalog.Rockfalls, rng);
            if (prefab == null)
                return 0;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, entrance);
            if (go == null)
                return 0;

            CaveEditorUndo.RegisterCreated(go, "Entrance Collar");
            go.name = "EntranceRockCollar";
            go.transform.localPosition = sample.Position + Vector3.up * 0.4f;
            go.transform.localRotation = Quaternion.LookRotation(sample.Tangent, Vector3.up);
            var s = Mathf.Max(sample.RadiusX, sample.RadiusY) * 0.55f;
            go.transform.localScale = new Vector3(s, s * 0.5f, s);

            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

            return 1;
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
