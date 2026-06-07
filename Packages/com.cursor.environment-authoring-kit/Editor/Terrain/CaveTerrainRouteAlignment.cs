#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.TerrainAuthoring
{
    /// <summary>
    /// Aligns cave route floors with scene Terrain (Unity 6 heightmap sculpt + alphamap paint).
    /// Underground tube uses RouteTerrain meshes; surface mouth follows carved terrain.
    /// </summary>
    public static class CaveTerrainRouteAlignment
    {
        const float EntranceAlignPathFraction = 0.45f;

        public static bool IntegrateSceneTerrain(
            Transform cavesRoot,
            CaveMazeLayout layout,
            CaveSplinePath spline,
            WorldGenerationRequest request)
        {
            if (cavesRoot == null || layout == null || spline == null || request == null || !request.UseTerrainCarve)
                return false;

            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogWarning(
                    "[CaveBuild] UseTerrainCarve is on but no Terrain in scene — " +
                    "add a Terrain for surface floors/walls or disable terrain carve in request.");
                return false;
            }

            CaveEditorUndo.RecordObject(terrain.terrainData, "Terrain cave integration");
            var carved = CaveTerrainCarveUtility.CarveForCaveSystem(cavesRoot, spline, null);
            ApplyEntranceFloorTerrainOffsets(cavesRoot, layout, terrain);
            CaveTerrainUtility.ApplyCaveEntranceMouth(terrain, request.Seed, cavesRoot);
            terrain.Flush();

            Debug.Log(
                $"[CaveBuild] Terrain integration: carved={carved}, " +
                $"entrance floor offsets={layout.PlatformHeightOffsets?.Count ?? 0} " +
                "(Unity Terrain height + rock paint at mouth; RouteTerrain floor/ceiling inside tube).");
            return carved;
        }

        static void ApplyEntranceFloorTerrainOffsets(
            Transform cavesRoot,
            CaveMazeLayout layout,
            Terrain terrain)
        {
            if (layout.SolutionPath == null || layout.SolutionPath.Count < 2)
                return;

            layout.PlatformHeightOffsets ??= new System.Collections.Generic.Dictionary<Vector2Int, float>();
            var alignCount = Mathf.Max(
                3,
                Mathf.CeilToInt(layout.SolutionPath.Count * EntranceAlignPathFraction));

            for (var i = 0; i < alignCount; i++)
            {
                var cell = layout.SolutionPath[i];
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var floorBase = GetWalkFloorBaseLocal(layout, cell.x, cell.y);
                var world = cavesRoot.TransformPoint(floorBase);
                var terrainY = terrain.SampleHeight(world);
                var delta = terrainY - world.y;

                if (Mathf.Abs(delta) < 0.08f)
                    continue;

                layout.PlatformHeightOffsets[cell] = delta;
            }
        }

        static Vector3 GetWalkFloorBaseLocal(CaveMazeLayout layout, int x, int z)
        {
            var center = layout.CellToLocal(x, z);
            var h = layout.IsCavernCell(x, z) ? layout.CorridorHeight * 1.75f : layout.CorridorHeight;
            const float wallThickness = 0.75f;
            return center + Vector3.down * (h * 0.5f - wallThickness);
        }
    }
}
#endif
