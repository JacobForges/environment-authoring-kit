using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Aligns scene Terrain with the flat layout route so you can sculpt walls/ceiling in Unity Terrain tools.</summary>
    static class CaveLayoutTerrainPad
    {
        public static string PrepareSculptSurface(
            Transform environmentRoot,
            Transform caveRoot,
            CaveMazeLayout layout,
            SceneGroundInfo ground)
        {
            if (caveRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return string.Empty;

            var allowCreate = !EnvironmentKitSettings.NeverCreateNewTerrain;
            var terrain = EnvironmentSceneUtility.FindTerrainInActiveScene(
                environmentRoot, ground, allowCreate, size: 192, height: 48f);

            if (terrain == null)
            {
                return
                    "No Terrain in scene (LayoutWalkFloor cubes are your walk surface). " +
                    "Add a Terrain object or disable 'Never create new terrain' in Environment Kit settings.";
            }

            FlattenPadAlongRoute(terrain, caveRoot, layout);
            return
                $"Terrain '{terrain.name}' leveled under route — use Paint Terrain / Raise-Lower to sculpt. " +
                "Walk on LayoutWalkFloor until terrain matches.";
        }

        static void FlattenPadAlongRoute(Terrain terrain, Transform caveRoot, CaveMazeLayout layout)
        {
            var data = terrain.terrainData;
            if (data == null)
                return;

            CaveEditorUndo.RecordObject(data, "Layout Terrain Pad");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var scaleY = Mathf.Max(0.01f, terrain.transform.lossyScale.y);

            var start = layout.SolutionPath[0];
            var targetWorldY = caveRoot.TransformPoint(layout.GetFloorSurfaceLocal(start.x, start.y)).y;
            var normalized = Mathf.Clamp01((targetWorldY - origin.y) / (size.y * scaleY));

            const float padMeters = 4f;
            foreach (var cell in layout.SolutionPath)
            {
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var local = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var world = caveRoot.TransformPoint(local);
                var radius = Mathf.Max(layout.PlatformSpan, layout.PlatformDepth) * 0.55f + padMeters;
                StampFlatDisc(heights, res, size, origin, world, radius, normalized);
            }

            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        static void StampFlatDisc(
            float[,] heights,
            int res,
            Vector3 terrainSize,
            Vector3 terrainOrigin,
            Vector3 worldCenter,
            float radiusMeters,
            float heightNormalized)
        {
            var nx = (worldCenter.x - terrainOrigin.x) / terrainSize.x;
            var nz = (worldCenter.z - terrainOrigin.z) / terrainSize.z;
            var radiusX = radiusMeters / terrainSize.x;
            var radiusZ = radiusMeters / terrainSize.z;

            var x0 = Mathf.Clamp(Mathf.FloorToInt((nx - radiusX) * res), 0, res - 1);
            var x1 = Mathf.Clamp(Mathf.CeilToInt((nx + radiusX) * res), 0, res - 1);
            var z0 = Mathf.Clamp(Mathf.FloorToInt((nz - radiusZ) * res), 0, res - 1);
            var z1 = Mathf.Clamp(Mathf.CeilToInt((nz + radiusZ) * res), 0, res - 1);

            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var px = x / (float)(res - 1);
                    var pz = z / (float)(res - 1);
                    var dx = (px - nx) / Mathf.Max(0.001f, radiusX);
                    var dz = (pz - nz) / Mathf.Max(0.001f, radiusZ);
                    if (dx * dx + dz * dz > 1f)
                        continue;

                    var y = Mathf.Lerp(heights[z, x], heightNormalized, 0.85f);
                    heights[z, x] = Mathf.Min(heights[z, x], y);
                }
            }
        }
    }
}
