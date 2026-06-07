#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Procedural hedge/stone labyrinth inside each hollow-tree floor plate.
    /// Central opening stays clear for the spiral stair column.
    /// </summary>
    public static class HollowTitanLandmarkInteriorMaze
    {
        public sealed class Layout
        {
            public int FloorIndex;
            public bool[,] Passage;
            public int Width;
            public int Height;
            public float CellSizeMeters;
            public float OuterRadiusMeters;
            public float CentralOpeningRadiusMeters;
        }

        public static Layout Generate(
            int buildSeed,
            int floorIndex,
            float floorRadius,
            float floorHeightMeters,
            float entranceYawDegrees = 0f,
            float layoutScale = 1f)
        {
            floorRadius = Mathf.Max(6f, floorRadius);
            floorHeightMeters = Mathf.Max(4f, floorHeightMeters);
            layoutScale = Mathf.Max(1f, layoutScale);

            var rng = new System.Random(buildSeed + 0x494E4D5A + floorIndex * 7919); // "INMZ"
            var cellSize = Mathf.Clamp(floorHeightMeters * 0.42f * layoutScale, 2.6f, 14f);
            var outerRadius = floorRadius * 0.84f;
            var centralOpening = floorRadius * 0.2f;

            var gridSpan = outerRadius * 2.05f;
            var gridCells = Mathf.CeilToInt(gridSpan / cellSize);
            if ((gridCells & 1) == 0)
                gridCells++;
            gridCells = Mathf.Clamp(gridCells, 11, 127);

            var layout = new Layout
            {
                FloorIndex = floorIndex,
                Width = gridCells,
                Height = gridCells,
                CellSizeMeters = cellSize,
                OuterRadiusMeters = outerRadius,
                CentralOpeningRadiusMeters = centralOpening,
                Passage = new bool[gridCells, gridCells],
            };

            ApplyBoundaryMask(layout);
            CarveCentralOpening(layout);
            CarveProperMaze(layout, rng);
            WidenPassages(layout, 1);

            if (floorIndex == 0)
                CarveEntranceCorridor(layout, entranceYawDegrees, rng);

            return layout;
        }

        public static int BuildWallGeometry(
            Transform mazeRoot,
            Layout layout,
            float floorLocalY,
            float wallHeightMeters,
            Color wallTint,
            int buildSeed = 0)
        {
            if (layout?.Passage == null || mazeRoot == null)
                return 0;

            wallHeightMeters = Mathf.Max(2.4f, wallHeightMeters);
            var placed = 0;
            var half = layout.Width * 0.5f;
            var rng = new System.Random(buildSeed + 0x57414C4C); // "WALL"
            var useCc0 = buildSeed != 0;
            var cc0Budget = EnvironmentKitHardwareBudget.Active.ConserveGpuMemory ? 48 : 140;
            var cc0Placed = 0;

            for (var gz = 0; gz < layout.Height; gz++)
            {
                for (var gx = 0; gx < layout.Width; gx++)
                {
                    if (layout.Passage[gx, gz])
                        continue;

                    var localX = (gx - half + 0.5f) * layout.CellSizeMeters;
                    var localZ = (gz - half + 0.5f) * layout.CellSizeMeters;
                    var dist = Mathf.Sqrt(localX * localX + localZ * localZ);
                    if (dist > layout.OuterRadiusMeters + layout.CellSizeMeters * 0.35f)
                        continue;
                    if (dist < layout.CentralOpeningRadiusMeters - layout.CellSizeMeters * 0.2f)
                        continue;

                    var pos = new Vector3(localX, floorLocalY + wallHeightMeters * 0.5f, localZ);
                    var cell = layout.CellSizeMeters * 0.92f;

                    if (useCc0 && cc0Placed < cc0Budget &&
                        TryPlaceCc0Wall(mazeRoot, pos, cell, wallHeightMeters, rng))
                    {
                        placed++;
                        cc0Placed++;
                        continue;
                    }

                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name = $"Wall_{gx:D2}_{gz:D2}";
                    wall.transform.SetParent(mazeRoot, false);
                    wall.transform.localPosition = pos;
                    wall.transform.localScale = new Vector3(cell, wallHeightMeters, cell);
                    TintPrimitive(wall, wallTint);
                    placed++;
                }
            }

            return placed;
        }

        static readonly string[] WallPropIds = { "B05", "B06", "B07", "B08", "B09", "B10", "K05", "K06" };

        static bool TryPlaceCc0Wall(
            Transform mazeRoot,
            Vector3 localPos,
            float cellSize,
            float wallHeightMeters,
            System.Random rng)
        {
            var id = WallPropIds[rng.Next(WallPropIds.Length)];
            var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
            if (prefab == null)
                return false;

            var wall = (GameObject)PrefabUtility.InstantiatePrefab(prefab, mazeRoot);
            if (wall == null)
                return false;

            wall.name = $"WallCc0_{id}";
            wall.transform.localPosition = localPos;
            wall.transform.localRotation = Quaternion.Euler(
                0f,
                rng.Next(0, 4) * 90f + (float)rng.NextDouble() * 12f - 6f,
                0f);
            var scale = Mathf.Max(cellSize, wallHeightMeters * 0.42f) / 1.2f;
            wall.transform.localScale = new Vector3(scale, wallHeightMeters / 1.8f, scale);
            return true;
        }

        public static Color PickWallTint(int floorIndex)
        {
            return floorIndex % 2 == 0
                ? new Color(0.3f, 0.28f, 0.24f)
                : new Color(0.2f, 0.34f, 0.17f);
        }

        static void ApplyBoundaryMask(Layout layout)
        {
            var half = layout.Width * 0.5f;
            for (var gz = 0; gz < layout.Height; gz++)
            {
                for (var gx = 0; gx < layout.Width; gx++)
                {
                    var localX = (gx - half + 0.5f) * layout.CellSizeMeters;
                    var localZ = (gz - half + 0.5f) * layout.CellSizeMeters;
                    var dist = Mathf.Sqrt(localX * localX + localZ * localZ);
                    layout.Passage[gx, gz] = dist <= layout.OuterRadiusMeters;
                }
            }
        }

        static void CarveCentralOpening(Layout layout)
        {
            var half = layout.Width * 0.5f;
            for (var gz = 0; gz < layout.Height; gz++)
            {
                for (var gx = 0; gx < layout.Width; gx++)
                {
                    var localX = (gx - half + 0.5f) * layout.CellSizeMeters;
                    var localZ = (gz - half + 0.5f) * layout.CellSizeMeters;
                    var dist = Mathf.Sqrt(localX * localX + localZ * localZ);
                    if (dist <= layout.CentralOpeningRadiusMeters)
                        layout.Passage[gx, gz] = true;
                }
            }
        }

        /// <summary>Recursive-backtracker maze — same cadence as play-disk labyrinth.</summary>
        static void CarveProperMaze(Layout layout, System.Random rng)
        {
            var visited = new bool[layout.Width, layout.Height];
            var stack = new Stack<Vector2Int>();
            var start = PickMazeStart(layout, rng);
            if (start.x < 0)
                return;

            visited[start.x, start.y] = true;
            layout.Passage[start.x, start.y] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var current = stack.Peek();
                var neighbors = new List<Vector2Int>();

                foreach (var step in new[]
                         {
                             new Vector2Int(2, 0), new Vector2Int(-2, 0), new Vector2Int(0, 2), new Vector2Int(0, -2),
                         })
                {
                    var nx = current.x + step.x;
                    var nz = current.y + step.y;
                    if (nx < 1 || nz < 1 || nx >= layout.Width - 1 || nz >= layout.Height - 1)
                        continue;
                    if (visited[nx, nz] || !IsMazeCarveCell(layout, nx, nz))
                        continue;
                    neighbors.Add(new Vector2Int(nx, nz));
                }

                if (neighbors.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                var next = neighbors[rng.Next(neighbors.Count)];
                var between = new Vector2Int((current.x + next.x) / 2, (current.y + next.y) / 2);
                layout.Passage[between.x, between.y] = true;
                layout.Passage[next.x, next.y] = true;
                visited[next.x, next.y] = true;
                stack.Push(next);
            }
        }

        static Vector2Int PickMazeStart(Layout layout, System.Random rng)
        {
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var gx = rng.Next(2, layout.Width - 2);
                var gz = rng.Next(2, layout.Height - 2);
                if (IsMazeCarveCell(layout, gx, gz))
                    return new Vector2Int(gx, gz);
            }

            var half = layout.Width / 2;
            return IsMazeCarveCell(layout, half, half + 4)
                ? new Vector2Int(half, half + 4)
                : new Vector2Int(-1, -1);
        }

        static bool IsMazeCarveCell(Layout layout, int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= layout.Width || gz >= layout.Height)
                return false;
            if (!layout.Passage[gx, gz])
                return false;

            var half = layout.Width * 0.5f;
            var localX = (gx - half + 0.5f) * layout.CellSizeMeters;
            var localZ = (gz - half + 0.5f) * layout.CellSizeMeters;
            var dist = Mathf.Sqrt(localX * localX + localZ * localZ);
            return dist > layout.CentralOpeningRadiusMeters + layout.CellSizeMeters * 0.45f &&
                   dist < layout.OuterRadiusMeters - layout.CellSizeMeters * 0.35f;
        }

        static void CarveEntranceCorridor(Layout layout, float entranceYawDegrees, System.Random rng)
        {
            var half = layout.Width * 0.5f;
            var rad = entranceYawDegrees * Mathf.Deg2Rad;
            var dirX = Mathf.Sin(rad);
            var dirZ = Mathf.Cos(rad);
            var steps = Mathf.CeilToInt(layout.OuterRadiusMeters / layout.CellSizeMeters);

            for (var s = 0; s <= steps; s++)
            {
                var dist = layout.CentralOpeningRadiusMeters + s * layout.CellSizeMeters * 0.85f;
                if (dist > layout.OuterRadiusMeters)
                    break;

                var wx = dirX * dist;
                var wz = dirZ * dist;
                var gx = Mathf.RoundToInt(wx / layout.CellSizeMeters + half);
                var gz = Mathf.RoundToInt(wz / layout.CellSizeMeters + half);
                gx = Mathf.Clamp(gx, 1, layout.Width - 2);
                gz = Mathf.Clamp(gz, 1, layout.Height - 2);
                layout.Passage[gx, gz] = true;

                if (rng.NextDouble() < 0.35)
                {
                    var side = rng.NextDouble() < 0.5f ? -1 : 1;
                    var sgx = Mathf.Clamp(gx + Mathf.RoundToInt(-dirZ * side), 1, layout.Width - 2);
                    var sgz = Mathf.Clamp(gz + Mathf.RoundToInt(dirX * side), 1, layout.Height - 2);
                    layout.Passage[sgx, sgz] = true;
                }
            }
        }

        static void WidenPassages(Layout layout, int iterations)
        {
            if (layout?.Passage == null || iterations <= 0)
                return;

            for (var pass = 0; pass < iterations; pass++)
            {
                var next = (bool[,])layout.Passage.Clone();
                for (var z = 0; z < layout.Height; z++)
                {
                    for (var x = 0; x < layout.Width; x++)
                    {
                        if (!layout.Passage[x, z])
                            continue;

                        for (var dz = -1; dz <= 1; dz++)
                        {
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dz == 0)
                                    continue;
                                var nx = x + dx;
                                var nz = z + dz;
                                if (nx < 0 || nz < 0 || nx >= layout.Width || nz >= layout.Height)
                                    continue;
                                if (!IsMazeCarveCell(layout, nx, nz) && !layout.Passage[nx, nz])
                                    continue;
                                next[nx, nz] = true;
                            }
                        }
                    }
                }

                layout.Passage = next;
                ApplyBoundaryMask(layout);
                CarveCentralOpening(layout);
            }
        }

        static Shader ResolveTintShader() =>
            Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Legacy Shaders/Diffuse");

        static void TintPrimitive(GameObject go, Color color)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
                return;

            var shader = ResolveTintShader();
            if (shader == null)
            {
                Debug.LogWarning($"[HollowTitan] No tint shader found for '{go.name}' — preview may appear black.");
                return;
            }

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            else
                mat.color = color;
            mr.sharedMaterial = mat;
        }
    }
}
#endif
