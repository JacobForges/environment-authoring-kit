using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Places breakable 3D wall blocks along the tunnel (pickaxe targets).</summary>
    public static class CaveMinableWallBuilder
    {
        const string WallRootName = "MinableWalls";
        const float BlockSize = 1.05f;

        public static int Build(
            Transform cavesRoot,
            CaveSplinePath spline,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            float spacingMeters = 13f)
        {
            if (cavesRoot == null || spline == null || spline.KnotCount < 2)
                return 0;

            var wallRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, WallRootName);
            ClearChildren(wallRoot);

            var material = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            var count = Mathf.Max(2, Mathf.FloorToInt(spline.TotalLength / spacingMeters));
            var placed = 0;

            for (var i = 1; i <= count; i++)
            {
                var dist = (i / (float)(count + 1)) * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var side = (i % 2 == 0) ? 1f : -1f;
                if (catalog != null && catalog.Walls.Count > 0 && rng.NextDouble() < 0.35)
                {
                    placed += PlacePrefabWallBlock(wallRoot, catalog, rng, sample, side);
                    continue;
                }

                placed += PlaceCubeWallColumn(wallRoot, sample, side, material, rows: 2, cols: 2);
            }

            return placed;
        }

        static int PlacePrefabWallBlock(
            Transform wallRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplineSample sample,
            float side)
        {
            var prefab = catalog.Pick(catalog.Walls, rng);
            var offset = sample.Right * side * sample.RadiusX * 0.52f;
            var floor = sample.Position - sample.Up * (sample.RadiusY * 0.72f);
            var pos = floor + offset + sample.Up * (BlockSize * 0.55f);
            var rot = Quaternion.LookRotation(-sample.Right * side, sample.Up);

            if (!CavePrefabScatter.PlaceModule(wallRoot, prefab, pos, rot, Vector3.one * 0.95f, "MinableWall", true))
                return 0;

            var instance = wallRoot.GetChild(wallRoot.childCount - 1);
            var rock = instance.GetComponent<MinableRock>();
            if (rock != null)
                rock.hitPoints = 5;
            return 1;
        }

        static int PlaceCubeWallColumn(
            Transform wallRoot,
            CaveSplineSample sample,
            float side,
            Material rockMaterial,
            int rows,
            int cols)
        {
            var placed = 0;
            var floor = sample.Position - sample.Up * (sample.RadiusY * 0.74f);
            var inward = -sample.Right * side;

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var lateral = sample.Right * side * (sample.RadiusX * 0.42f + col * BlockSize * 0.92f);
                    var pos = floor + lateral + sample.Up * (BlockSize * 0.5f + row * BlockSize);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    CaveEditorUndo.RegisterCreated(go, "Minable Wall Block");
                    go.name = $"MinableWallBlock_{placed:D3}";
                    go.transform.SetParent(wallRoot, false);
                    go.transform.localPosition = pos;
                    go.transform.localRotation = Quaternion.LookRotation(inward, sample.Up);
                    go.transform.localScale = Vector3.one * BlockSize;

                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null && rockMaterial != null)
                        renderer.sharedMaterial = rockMaterial;

                    var rock = go.AddComponent<MinableRock>();
                    rock.hitPoints = 4;
                    go.tag = CaveTags.Minable;
                    placed++;
                }
            }

            return placed;
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
