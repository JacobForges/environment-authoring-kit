#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Walkable floor/ceiling for labyrinth annex cells (after route shell walkway).</summary>
    public static class CaveLabyrinthVolumeBuilder
    {
        public const string RootName = "LabyrinthAnnex";

        public static int Build(Transform geometryRoot, CaveMazeLayout layout, Material rockMat, Material floorMat)
        {
            if (geometryRoot == null || layout == null || !layout.HasLabyrinthAnnex)
                return 0;

            var existing = geometryRoot.Find(RootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(RootName);
            CaveEditorUndo.RegisterCreated(root, "Labyrinth annex");
            root.transform.SetParent(geometryRoot, false);

            if (rockMat == null)
                rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (floorMat == null)
                floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();

            var wallThickness = 0.75f;
            var interior = layout.CellSize - wallThickness * 2f;
            var placed = 0;
            var pathSet = layout.SolutionPathSet;

            for (var x = 0; x < layout.Width; x++)
            {
                for (var z = 0; z < layout.Height; z++)
                {
                    if (!layout.IsPassage(x, z))
                        continue;
                    if (!layout.IsLabyrinthAnnexCell(x, z))
                        continue;

                    var cell = new Vector2Int(x, z);
                    if (pathSet != null && pathSet.Contains(cell) &&
                        cell != layout.LabyrinthEntranceCell &&
                        cell != layout.CavernCenter)
                        continue;

                    placed += CaveMazeVolumeBuilder.BuildCorridorCellFloorCeilingOnlyPublic(
                        root.transform, layout, x, z, rockMat, floorMat, interior, wallThickness);
                }
            }

            return placed;
        }
    }
}
#endif
